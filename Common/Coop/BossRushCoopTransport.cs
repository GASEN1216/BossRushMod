using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 仅此边界认识第三方联机类型。绑定公开 ModNetworkApi，绝不接管其 backend。
    /// 对照原版工坊 3591341282 / 1.3.6；没有程序集时不建立硬依赖。
    /// </summary>
    internal sealed class BossRushCoopTransport : IDisposable
    {
        internal const string Channel = "BossRush.Cooperative.v1";
        private Type serviceType;
        private Type apiType;
        private Type eventsType;
        private Type databaseType;
        private Type managerType;
        private MethodInfo sendHost;
        private MethodInfo sendPeer;
        private MethodInfo broadcast;
        private MethodInfo getPlayerId;
        private MethodInfo getSelfId;
        private Type writerActionType;
        private MethodInfo writerPut;
        private IDisposable messageSubscription;
        private BossRushCoopSceneLoadCompatibility sceneLoadCompatibility;
        private readonly List<KeyValuePair<EventInfo, Delegate>> subscriptions = new List<KeyValuePair<EventInfo, Delegate>>();
        private readonly Action<object, byte[]> receive;
        private readonly Action<object> peerDisconnected;
        private readonly Action<string, CharacterMainControl, bool> playerSpawned;
        private readonly Action<int, CharacterMainControl> aiSpawned;
        internal object Service { get { return serviceType == null ? null : Read(serviceType, null, "Instance"); } }
        internal bool Bound { get { return messageSubscription != null; } }
        internal bool NetworkStarted { get { return Bool(Read(serviceType, Service, "NetworkStarted")); } }
        internal bool IsServer { get { return Bool(Read(serviceType, Service, "IsServer")); } }
        internal object HostPeer { get { return Read(serviceType, Service, "connectedPeer"); } }
        internal int PeerCount
        {
            get { var manager = Read(serviceType, Service, "netManager"); return manager != null ? Convert.ToInt32(Read(manager.GetType(), manager, "ConnectedPeersCount")) : 0; }
        }
        internal bool IsPeerConnected(object peer)
        {
            if (peer == null) return false;
            var manager = Read(serviceType, Service, "netManager");
            var connected = manager != null ? Read(manager.GetType(), manager, "ConnectedPeerList") as IEnumerable : null;
            if (connected == null) return false;
            foreach (object current in connected) if (ReferenceEquals(current, peer)) return true;
            return false;
        }

        internal BossRushCoopTransport(Action<object, byte[]> receive, Action<object> disconnected,
            Action<string, CharacterMainControl, bool> playerSpawned, Action<int, CharacterMainControl> aiSpawned)
        {
            this.receive = receive; peerDisconnected = disconnected; this.playerSpawned = playerSpawned; this.aiSpawned = aiSpawned;
        }

        internal bool TryBind()
        {
            if (Bound) return true;
            serviceType = FindType("EscapeFromDuckovCoopMod.NetService");
            apiType = FindType("EscapeFromDuckovCoopMod.ModNetworkApi");
            eventsType = FindType("EscapeFromDuckovCoopMod.ModApiEvents");
            databaseType = FindType("EscapeFromDuckovCoopMod.CoopSyncDatabase");
            managerType = FindType("EscapeFromDuckovCoopMod.COOPManager");
            if (serviceType == null || apiType == null || eventsType == null) return false;
            try
            {
                MethodInfo register = apiType.GetMethod("RegisterHandler", BindingFlags.Public | BindingFlags.Static);
                sendHost = apiType.GetMethod("SendToServer", BindingFlags.Public | BindingFlags.Static);
                sendPeer = apiType.GetMethod("SendToClient", BindingFlags.Public | BindingFlags.Static);
                broadcast = apiType.GetMethod("Broadcast", BindingFlags.Public | BindingFlags.Static);
                getPlayerId = serviceType.GetMethod("GetPlayerId", BindingFlags.Public | BindingFlags.Instance);
                getSelfId = serviceType.GetMethod("GetSelfNetworkId", BindingFlags.Public | BindingFlags.Instance);
                if (register == null || sendHost == null || sendPeer == null || broadcast == null || getPlayerId == null || getSelfId == null)
                    throw new InvalidOperationException("Required public API is missing.");
                writerActionType = sendHost.GetParameters()[1].ParameterType;
                Type writerType = writerActionType.GetGenericArguments()[0];
                writerPut = writerType.GetMethod("Put", new[] { typeof(byte[]) });
                if (writerPut == null) throw new InvalidOperationException("NetDataWriter.Put(byte[]) is unavailable.");
                Delegate callback = Adapt(register.GetParameters()[1].ParameterType, OnRawMessage);
                messageSubscription = register.Invoke(null, new object[] { Channel, callback }) as IDisposable;
                if (messageSubscription == null) throw new InvalidOperationException("RegisterHandler returned no subscription.");
                Subscribe(apiType, "PeerDisconnected", OnPeerDisconnected);
                Subscribe(eventsType, "PlayerSpawned", OnPlayerSpawned);
                Subscribe(eventsType, "AiSpawned", OnAiSpawned);
                Subscribe(eventsType, "ServerRegisterAI", OnServerRegisterAi);
                Subscribe(eventsType, "ItemSnapshotCustomDataRequested", OnItemRequested);
                Subscribe(eventsType, "ItemSnapshotCustomDataApplied", OnItemApplied);
                Type sceneNet = FindType("EscapeFromDuckovCoopMod.SceneNet");
                if (sceneNet != null)
                {
                    sceneLoadCompatibility = new BossRushCoopSceneLoadCompatibility(sceneNet, () => Bound && NetworkStarted);
                    sceneLoadCompatibility.Install();
                }
                return true;
            }
            catch (Exception e)
            {
                Dispose();
                ModBehaviour.CriticalLog("coop-api-bind", "[BossRush Coop] Optional API binding failed: " + e.Message);
                return false;
            }
        }

        private void OnRawMessage(object[] args)
        {
            if (args.Length != 1 || args[0] == null) return;
            object context = args[0];
            Type type = context.GetType();
            object memory = Read(type, context, "Payload");
            if (memory == null) return;
            MethodInfo toArray = memory.GetType().GetMethod("ToArray", Type.EmptyTypes);
            byte[] bytes = toArray != null ? toArray.Invoke(memory, null) as byte[] : null;
            if (bytes != null) receive(Read(type, context, "Sender"), bytes);
        }
        private void OnPeerDisconnected(object[] args) { if (args.Length == 1) peerDisconnected(args[0]); }
        private void OnPlayerSpawned(object[] args)
        {
            if (args.Length == 3) playerSpawned(args[1] as string, args[0] as CharacterMainControl, Bool(args[2]));
        }
        private void OnAiSpawned(object[] args)
        {
            if (args.Length == 2) aiSpawned(Convert.ToInt32(args[0]), args[1] as CharacterMainControl);
        }
        private void OnServerRegisterAi(object[] args)
        {
            if (args.Length != 1 || args[0] == null) return;
            object payload = args[0];
            var controller = Read(payload.GetType(), payload, "Controller") as AICharacterController;
            if (controller != null) aiSpawned(Convert.ToInt32(Read(payload.GetType(), payload, "EntryId")), controller.CharacterMainControl);
        }
        private void OnItemRequested(object[] args)
        {
            if (args.Length == 2) BossRushCoop.RaiseItemRequested(args[0] as Item, args[1] as Dictionary<string, string>);
        }
        private void OnItemApplied(object[] args)
        {
            if (args.Length == 2) BossRushCoop.RaiseItemApplied(args[0] as Item, args[1] as IReadOnlyDictionary<string, string>);
        }

        internal string NativeId(object peer)
        {
            object service = Service;
            return service != null && getPlayerId != null ? getPlayerId.Invoke(service, new[] { peer }) as string : null;
        }
        internal string SelfId()
        {
            object service = Service;
            return service != null && getSelfId != null ? getSelfId.Invoke(service, null) as string : null;
        }

        internal CharacterMainControl CharacterForPeer(object peer)
        {
            var map = Read(serviceType, Service, "remoteCharacters") as IDictionary;
            var obj = map != null && peer != null && map.Contains(peer) ? map[peer] as GameObject : null;
            return obj != null ? obj.GetComponent<CharacterMainControl>() : null;
        }

        internal bool RegisterLoot(InteractableLootbox box)
        {
            if (!Bound || box == null || box.Inventory == null) return false;
            Type gate = FindType("EscapeFromDuckovCoopMod.LootSearchWorldGate");
            MethodInfo mark = gate != null ? gate.GetMethod("EnsureWorldFlag", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
            if (mark == null) return false;
            mark.Invoke(null, new object[] { box.Inventory });
            object registry = Read(databaseType, null, "Loot");
            MethodInfo register = registry != null ? registry.GetType().GetMethod("Register") : null;
            if (register == null) return false;
            register.Invoke(registry, new object[] { box, box.Inventory, -1 });
            return true;
        }
        internal bool UnregisterLoot(InteractableLootbox box)
        {
            if (!Bound || box == null) return false;
            object registry = Read(databaseType, null, "Loot");
            MethodInfo unregister = registry != null ? registry.GetType().GetMethod("Unregister", new[] { typeof(InteractableLootbox) }) : null;
            if (unregister == null) return false;
            unregister.Invoke(registry, new object[] { box });
            return true;
        }

        internal bool RegisterDestructible(HealthSimpleBase health, string entityKey)
        {
            if (!Bound || !NetworkStarted || health == null || string.IsNullOrEmpty(entityKey)) return false;
            Type tagType = FindType("EscapeFromDuckovCoopMod.NetDestructibleTag");
            object service = Read(managerType, null, "destructible");
            MethodInfo register = service != null ? service.GetType().GetMethod("RegisterDestructible", new[] { typeof(uint), typeof(HealthSimpleBase) }) : null;
            FieldInfo idField = tagType != null ? tagType.GetField("id") : null;
            if (register == null || idField == null) return false;
            // 双端使用同一个生成 key，不能依赖各端不同的 sibling index。
            // SHA-256 的前四字节只是对象索引；这里不承担认证职责。
            uint id;
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes("BossRush/destructible/" + entityKey));
                id = (uint)bytes[0] | ((uint)bytes[1] << 8) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 24);
            }
            if (id == 0) id = 1;
            Component tag = health.GetComponent(tagType) ?? health.gameObject.AddComponent(tagType);
            idField.SetValue(tag, id);
            register.Invoke(service, new object[] { id, health });
            return true;
        }

        internal void RefreshCharacters(Action<string, CharacterMainControl, bool> callback)
        {
            object service = Service;
            if (service == null) return;
            if (CharacterMainControl.Main != null) callback(SelfId(), CharacterMainControl.Main, true);
            var map = Read(serviceType, service, IsServer ? "remoteCharacters" : "clientRemoteCharacters") as IDictionary;
            if (map == null) return;
            foreach (DictionaryEntry pair in map)
            {
                var obj = pair.Value as GameObject;
                if (obj == null) continue;
                CharacterMainControl character = obj.GetComponent<CharacterMainControl>();
                if (character != null) callback(IsServer ? NativeId(pair.Key) : pair.Key as string, character, false);
            }
        }

        internal void RefreshAi(Action<int, CharacterMainControl> callback)
        {
            object registry = Read(databaseType, null, "AI");
            IEnumerable entries = registry != null ? Read(registry.GetType(), registry, "Entries") as IEnumerable : null;
            if (entries == null) return;
            foreach (object entry in entries)
            {
                if (entry == null) continue;
                Type type = entry.GetType();
                var controller = Read(type, entry, "Controller") as AICharacterController;
                if (controller != null && controller.CharacterMainControl != null)
                    callback(Convert.ToInt32(Read(type, entry, "Id")), controller.CharacterMainControl);
            }
        }

        internal bool RegisterAi(CharacterMainControl character)
        {
            if (!NetworkStarted || !IsServer || character == null) return false;
            var controller = character.GetComponentInChildren<AICharacterController>(true);
            object ai = Read(managerType, null, "AI");
            if (controller == null || ai == null) return false;
            MethodInfo register = ai.GetType().GetMethod("Server_RegisterCharacter", new[] { typeof(AICharacterController) });
            if (register == null) return false;
            register.Invoke(ai, new object[] { controller });
            return true;
        }

        internal bool Send(object peer, byte[] bytes, bool all = false)
        {
            if (!Bound || !NetworkStarted) return false;
            Delegate builder = Adapt(writerActionType, args => writerPut.Invoke(args[0], new object[] { bytes }));
            if (!IsServer) return Bool(sendHost.Invoke(null, new object[] { Channel, builder }));
            if (all) { broadcast.Invoke(null, new object[] { Channel, builder, false }); return true; }
            if (peer == null) return false;
            return Bool(sendPeer.Invoke(null, new object[] { peer, Channel, builder, false }));
        }

        private void Subscribe(Type type, string name, Action<object[]> callback)
        {
            EventInfo info = type.GetEvent(name, BindingFlags.Public | BindingFlags.Static);
            if (info == null) throw new InvalidOperationException(name + " is unavailable.");
            Delegate handler = Adapt(info.EventHandlerType, callback);
            info.AddEventHandler(null, handler);
            subscriptions.Add(new KeyValuePair<EventInfo, Delegate>(info, handler));
        }

        private static Delegate Adapt(Type delegateType, Action<object[]> callback)
        {
            ParameterInfo[] signature = delegateType.GetMethod("Invoke").GetParameters();
            var parameters = new ParameterExpression[signature.Length];
            var values = new Expression[signature.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                parameters[i] = Expression.Parameter(signature[i].ParameterType, "arg" + i);
                values[i] = Expression.Convert(parameters[i], typeof(object));
            }
            return Expression.Lambda(delegateType, Expression.Invoke(Expression.Constant(callback), Expression.NewArrayInit(typeof(object), values)), parameters).Compile();
        }
        internal static object Read(Type type, object target, string name)
        {
            if (type == null) return null;
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && (target != null || property.GetGetMethod().IsStatic)) return property.GetValue(target, null);
            FieldInfo field = type.GetField(name, flags);
            return field != null && (target != null || field.IsStatic) ? field.GetValue(target) : null;
        }
        private static bool Bool(object value) { return value is bool && (bool)value; }
        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }
        public void Dispose()
        {
            if (sceneLoadCompatibility != null) { sceneLoadCompatibility.Dispose(); sceneLoadCompatibility = null; }
            if (messageSubscription != null)
            {
                try { messageSubscription.Dispose(); }
                catch (Exception e) { ModBehaviour.DevLog("[BossRush Coop] Channel unsubscribe failed: " + e.Message); }
                messageSubscription = null;
            }
            foreach (var subscription in subscriptions)
            {
                try { subscription.Key.RemoveEventHandler(null, subscription.Value); }
                catch (Exception e) { ModBehaviour.DevLog("[BossRush Coop] Unsubscribe failed: " + e.Message); }
            }
            subscriptions.Clear();
        }
    }

    /// <summary>
    /// 工坊 1.3.6 的已批准 Base 返回会先等待 Closure，再真正调用 SceneLoader。
    /// 原生十秒授权会在人工等待时过期；只续接同一次批准、同一个源场景与角色的返回。
    /// 不发投票、不设置 ready、不绕过 Closure，也不改其它目标的原生授权。
    /// </summary>
    internal sealed class BossRushCoopSceneLoadCompatibility : IDisposable
    {
        private const string PatchOwner = "BossRush.Coop.ApprovedBaseReturn.v1";
        private const BindingFlags NativeFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static BossRushCoopSceneLoadCompatibility active;
        private readonly Type nativeType;
        private readonly Func<bool> networkStarted;
        private readonly FieldInfo nativeInstance, target, launched, authorizedTarget;
        private Harmony harmony;
        private bool subscribed;
        private ReturnLease lease, pending;

        private sealed class ReturnLease
        {
            internal object Native;
            internal CharacterMainControl Body;
            internal Duckov.Scenes.MultiSceneCore Core;
            internal string Run, Asset;
            internal int Generation, SceneHandle;
            internal bool Authorized;
        }

        internal BossRushCoopSceneLoadCompatibility(Type nativeType, Func<bool> networkStarted)
        {
            this.nativeType = nativeType;
            this.networkStarted = networkStarted;
            nativeInstance = RequireField("Instance");
            target = RequireField("sceneTargetId");
            launched = RequireField("_localSceneLoadLaunched");
            authorizedTarget = RequireField("_allowedLocalSceneLoadId");
        }

        internal void Install()
        {
            if (harmony != null) return;
            if (active != null) active.Dispose();
            active = this;
            harmony = new Harmony(PatchOwner);
            try
            {
                MethodInfo perform = RequireMethod("TryPerformSceneLoad_Local", new[] { typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(string) });
                Patch(perform, "BeforeApprovedLoad", "AfterApprovedLoad");
                Patch(RequireMethod("IsLocalSceneLoadAllowed", new[] { typeof(string) }), null, "AfterAuthorizationQuery");
                Patch(RequireMethod("AuthorizeLocalSceneLoad", new[] { typeof(string), typeof(float) }), "BeforeNativeAuthorization", null);
                foreach (string name in new[] { "ClearLocalSceneLoadAuthorization", "Host_BeginSceneVote_Simple", "Client_RequestBeginSceneVote", "Client_OnSceneVoteStart", "CancelVote", "Client_OnVoteCancelled" })
                {
                    MethodInfo method = nativeType.GetMethod(name, NativeFlags);
                    if (method == null) throw new MissingMethodException(nativeType.FullName, name);
                    Patch(method, "BeforeNewTravelOrCancel", null);
                }
                BossRushCoop.SessionChanged += OnIdentityChanged;
                Saves.SavesSystem.OnSetFile += OnIdentityChanged;
                Saves.SavesSystem.OnSaveDeleted += OnIdentityChanged;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                subscribed = true;
            }
            catch { Dispose(); throw; }
        }

        private FieldInfo RequireField(string name)
        {
            FieldInfo field = nativeType.GetField(name, NativeFlags);
            if (field == null) throw new MissingFieldException(nativeType.FullName, name);
            return field;
        }
        private MethodInfo RequireMethod(string name, Type[] parameters)
        {
            MethodInfo method = nativeType.GetMethod(name, NativeFlags, null, parameters, null);
            if (method == null) throw new MissingMethodException(nativeType.FullName, name);
            return method;
        }
        private void Patch(MethodInfo method, string prefix, string postfix)
        {
            Type own = typeof(BossRushCoopSceneLoadCompatibility);
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(own, prefix), postfix == null ? null : new HarmonyMethod(own, postfix));
        }

        private ReturnLease Capture(object instance, string destination)
        {
            var body = CharacterMainControl.Main;
            var core = Duckov.Scenes.MultiSceneCore.Instance;
            if (!string.Equals(destination, "Base", StringComparison.OrdinalIgnoreCase) || !networkStarted()
                || !BossRushCoop.IsConnected || body == null || core == null || !ReferenceEquals(nativeInstance.GetValue(null), instance)
                || string.IsNullOrEmpty(BossRushCoop.RunId) || string.IsNullOrEmpty(BossRushCoop.LocalAssetId)) return null;
            Scene source = core.gameObject.scene;
            if (!source.IsValid() || !source.isLoaded) return null;
            return new ReturnLease { Native = instance, Body = body, Core = core, Run = BossRushCoop.RunId,
                Asset = BossRushCoop.LocalAssetId, Generation = BossRushCoop.SceneGeneration, SceneHandle = source.handle };
        }
        private bool Current(ReturnLease value, object instance)
        {
            if (value == null || !networkStarted() || !BossRushCoop.IsConnected || value.Body == null || value.Core == null
                || !ReferenceEquals(value.Native, instance) || !ReferenceEquals(nativeInstance.GetValue(null), instance)
                || !ReferenceEquals(value.Body, CharacterMainControl.Main) || !ReferenceEquals(value.Core, Duckov.Scenes.MultiSceneCore.Instance)
                || value.Run != BossRushCoop.RunId || value.Asset != BossRushCoop.LocalAssetId
                || !string.Equals(target.GetValue(instance) as string, "Base", StringComparison.OrdinalIgnoreCase)) return false;
            Scene source = value.Core.gameObject.scene;
            return source.IsValid() && source.isLoaded && source.handle == value.SceneHandle;
        }
        private static void BeforeApprovedLoad(object __instance, string __0, out ReturnLease __state)
        {
            __state = null;
            if (active == null) return;
            active.OnIdentityChanged();
            __state = active.Capture(__instance, __0);
            active.pending = __state;
        }
        private static void BeforeNativeAuthorization(object __instance, string __0)
        {
            if (active == null) return;
            active.lease = null;
            if (active.pending != null && ReferenceEquals(active.pending.Native, __instance))
                active.pending.Authorized = string.Equals(__0, "Base", StringComparison.OrdinalIgnoreCase);
        }
        private static void AfterApprovedLoad(object __instance, ReturnLease __state)
        {
            if (active == null) return;
            if (ReferenceEquals(active.pending, __state) && __state != null && __state.Authorized && active.Current(__state, __instance)
                && (bool)active.launched.GetValue(__instance)
                && string.Equals(active.authorizedTarget.GetValue(__instance) as string, "Base", StringComparison.OrdinalIgnoreCase)) active.lease = __state;
            active.pending = null;
        }
        private static void AfterAuthorizationQuery(object __instance, string __0, ref bool __result)
        {
            if (active == null || active.lease == null) return;
            if (!active.Current(active.lease, __instance)) { active.OnIdentityChanged(); return; }
            if (!__result && string.Equals(__0, "Base", StringComparison.OrdinalIgnoreCase)) __result = true;
        }
        private static void BeforeNewTravelOrCancel() { if (active != null) active.OnIdentityChanged(); }
        private void OnIdentityChanged() { lease = pending = null; }
        private void OnSceneUnloaded(Scene scene)
        {
            if ((lease != null && lease.SceneHandle == scene.handle) || (pending != null && pending.SceneHandle == scene.handle)) OnIdentityChanged();
        }
        public void Dispose()
        {
            OnIdentityChanged();
            if (subscribed)
            {
                BossRushCoop.SessionChanged -= OnIdentityChanged;
                Saves.SavesSystem.OnSetFile -= OnIdentityChanged;
                Saves.SavesSystem.OnSaveDeleted -= OnIdentityChanged;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }
            if (harmony != null) { harmony.UnpatchAll(PatchOwner); harmony = null; }
            if (ReferenceEquals(active, this)) active = null;
        }
    }
}
