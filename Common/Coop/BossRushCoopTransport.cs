using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
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
        // 原生 BuildPayload 在 SendToServer/SendToClient/Broadcast 内同步调用写入回调；只编译一次，按次换字节。
        private Delegate writerBuilder;
        private byte[] outgoing;
        private IDisposable messageSubscription;
        private BossRushCoopSceneLoadCompatibility sceneLoadCompatibility;
        private BossRushCoopMapPaymentCompatibility mapPaymentCompatibility;
        private BossRushCoopSceneIdentityBridge sceneIdentityBridge;
        internal BossRushCoopSceneIdentity SceneIdentity { get; private set; }
        private readonly List<KeyValuePair<EventInfo, Delegate>> subscriptions = new List<KeyValuePair<EventInfo, Delegate>>();
        private readonly Action<object, byte[]> receive;
        private readonly Action<object> peerDisconnected;
        private readonly Action<string, CharacterMainControl, bool> playerSpawned;
        private readonly Action<int, CharacterMainControl> aiSpawned;
        internal object Service { get { return serviceType == null ? null : Read(serviceType, null, "Instance"); } }
        internal bool Bound { get { return messageSubscription != null; } }
        internal bool NetworkStarted { get { return Bool(Read(serviceType, Service, "NetworkStarted")); } }
        internal bool IsServer { get { return Bool(Read(serviceType, Service, "IsServer")); } }
        // Probe the public connection state independently of optional adapter binding.
        // An installed but stopped COOP remains ordinary single-player. An unreadable
        // live service must never grant every client single-player world authority.
        internal bool TryGetNetworkState(out bool started, out bool host)
        {
            started = false; host = false;
            if (serviceType == null) serviceType = FindType("EscapeFromDuckovCoopMod.NetService");
            if (serviceType == null) return true;
            try
            {
                const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
                if (serviceType.GetProperty("Instance", publicStatic) == null && serviceType.GetField("Instance", publicStatic) == null)
                { started = true; return false; }
                object service = Service;
                if (service == null) return true;
                object active = Read(serviceType, service, "NetworkStarted");
                if (!(active is bool)) { started = true; return false; }
                started = (bool)active;
                if (!started) return true;
                object role = Read(serviceType, service, "IsServer");
                if (!(role is bool)) return false;
                host = (bool)role;
                return true;
            }
            catch (Exception) { started = true; return false; }
        }
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

        internal List<object> ConnectedPeers()
        {
            var result = new List<object>();
            var manager = Read(serviceType, Service, "netManager");
            var connected = manager != null ? Read(manager.GetType(), manager, "ConnectedPeerList") as IEnumerable : null;
            if (connected != null) foreach (object peer in connected) if (peer != null) result.Add(peer);
            return result;
        }

        internal string PeerDisplayName(object peer)
        {
            string id = NativeId(peer) ?? ("peer#" + peer.GetHashCode());
            try
            {
                MethodInfo resolve = serviceType.GetMethod("ResolvePeerDisplayName", BindingFlags.Public | BindingFlags.Instance);
                string name = resolve != null ? resolve.Invoke(Service, new object[] { peer, id }) as string : null;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    name = name.Replace('\r', ' ').Replace('\n', ' ');
                    if (name.Length > 100) name = name.Substring(0, 100);
                    return name == id ? id : name + " (" + id + ")";
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush Coop] Native peer name unavailable: " + e.Message); }
            return id;
        }

        // Use COOP's existing server chat RPC so a peer without BossRush can also
        // see the compatibility reason. No user chat input or native registry is replaced.
        internal bool BroadcastCompatibilityNotice(string text)
        {
            if (!Bound || !NetworkStarted || !IsServer || string.IsNullOrEmpty(text)) return false;
            try
            {
                Type messageType = FindType("EscapeFromDuckovCoopMod.ChatMessageRpc");
                Type toolType = FindType("EscapeFromDuckovCoopMod.CoopTool");
                if (messageType == null || toolType == null) return false;
                MethodInfo send = toolType.GetMethod("SendRpc", BindingFlags.Public | BindingFlags.Static);
                if (send == null || !send.IsGenericMethodDefinition) return false;
                object message = Activator.CreateInstance(messageType);
                messageType.GetField("SenderId").SetValue(message, "BossRush");
                messageType.GetField("SenderName").SetValue(message, "BossRush");
                messageType.GetField("Content").SetValue(message, text);
                send.MakeGenericMethod(messageType).Invoke(null, new object[] { message, null });
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.CriticalLog("coop-native-compatibility-notice", "[BossRush Coop] Native room notice failed: " + e.Message);
                return false;
            }
        }

        internal BossRushCoopTransport(Action<object, byte[]> receive, Action<object> disconnected,
            Action<string, CharacterMainControl, bool> playerSpawned, Action<int, CharacterMainControl> aiSpawned)
        {
            this.receive = receive; peerDisconnected = disconnected; this.playerSpawned = playerSpawned; this.aiSpawned = aiSpawned;
            SceneIdentity = new BossRushCoopSceneIdentity(() => Bound && NetworkStarted, () => IsServer,
                () => Service, () => HostPeer, () => BossRushCoop.RunId);
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
                EnsureSpectatorForceEndRegistration(serviceType.Assembly);
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
                writerBuilder = Adapt(writerActionType, WriteOutgoing);
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
                    sceneLoadCompatibility = new BossRushCoopSceneLoadCompatibility(sceneNet, () => Bound && NetworkStarted, () => HostPeer);
                    sceneLoadCompatibility.Install();
                    mapPaymentCompatibility = new BossRushCoopMapPaymentCompatibility(sceneNet, () => Bound && NetworkStarted,
                        () => IsServer, () => Service);
                    mapPaymentCompatibility.Install();
                    sceneIdentityBridge = new BossRushCoopSceneIdentityBridge(sceneNet, SceneIdentity,
                        () => Bound && NetworkStarted, () => IsServer, () => Service, () => HostPeer,
                        () => BossRushCoop.RunId, SendSceneIdentityMarker);
                    sceneIdentityBridge.Install();
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

        // Workshop 1.3.6 sends this existing RPC after ending host spectating, but omits its registry entry.
        // Register its own handler through the native API; never replace a descriptor, opcode or spectator policy.
        private static void EnsureSpectatorForceEndRegistration(Assembly nativeAssembly)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            Type message = nativeAssembly.GetType("EscapeFromDuckovCoopMod.SpectatorForceEndRpc", false);
            if (message == null) return; // Older API versions without this message have no such sender to repair.
            Type registry = nativeAssembly.GetType("EscapeFromDuckovCoopMod.RpcRegistry", true);
            Type handlers = nativeAssembly.GetType("EscapeFromDuckovCoopMod.RPCScene", true);
            Type context = nativeAssembly.GetType("EscapeFromDuckovCoopMod.RpcContext", true);
            MethodInfo initialize = registry.GetMethod("Initialize", flags, null, Type.EmptyTypes, null);
            MethodInfo descriptor = registry.GetMethod("GetDescriptor", flags);
            MethodInfo register = registry.GetMethod("Register", flags);
            MethodInfo handler = handlers.GetMethod("HandleSpectatorForceEnd", flags, null, new[] { context, message }, null);
            Type callback = typeof(Action<,>).MakeGenericType(context, message);
            if (!message.IsValueType || !context.IsValueType || initialize == null || initialize.ReturnType != typeof(void)
                || descriptor == null || !descriptor.IsGenericMethodDefinition || descriptor.GetGenericArguments().Length != 1 || descriptor.GetParameters().Length != 0
                || register == null || !register.IsGenericMethodDefinition || register.GetGenericArguments().Length != 1
                || register.ReturnType != typeof(void) || handler == null || handler.ReturnType != typeof(void))
                throw new MissingMethodException("Native spectator RPC registration contract changed.");
            MethodInfo get = descriptor.MakeGenericMethod(message);
            MethodInfo add = register.MakeGenericMethod(message);
            if (add.GetParameters().Length != 1 || add.GetParameters()[0].ParameterType != callback)
                throw new MissingMethodException("Native spectator RPC callback contract changed.");
            initialize.Invoke(null, null);
            try
            {
                if (get.Invoke(null, null) == null) throw new InvalidOperationException("Native spectator RPC registry returned no descriptor.");
                return; // Native versions that already register it retain their exact original handler.
            }
            catch (TargetInvocationException e)
            {
                if (!(e.InnerException is InvalidOperationException)
                    || e.InnerException.Message != "Rpc message " + message.FullName + " not registered") throw;
            }
            // Native Register rejects duplicate opcodes before changing either registry; propagate any conflict.
            add.Invoke(null, new object[] { Delegate.CreateDelegate(callback, handler) });
            if (get.Invoke(null, null) == null) throw new InvalidOperationException("Native spectator RPC registration returned no descriptor.");
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

        // 只用于丧尸普通共享掉落的已授权拾取；不改动原生其它物品的策略。
        private sealed class NetworkDropScope : IDisposable
        {
            private FieldInfo field;
            private readonly bool previous;
            internal NetworkDropScope(FieldInfo value)
            { field = value; previous = (bool)value.GetValue(null); value.SetValue(null, true); }
            public void Dispose()
            { if (field == null) return; field.SetValue(null, previous); field = null; }
        }

        internal static IDisposable BeginNetworkItemDropScope()
        {
            Type type = FindType("EscapeFromDuckovCoopMod.ItemNet");
            FieldInfo field = type != null ? type.GetField("InNetworkDrop", BindingFlags.Public | BindingFlags.Static) : null;
            return field != null && field.FieldType == typeof(bool) ? new NetworkDropScope(field) : null;
        }

        internal static void ProtectNetworkDropPickup(Item item)
        {
            if (item == null) return;
            try
            {
                Type manager = FindType("EscapeFromDuckovCoopMod.COOPManager");
                object net = manager != null ? Read(manager, null, "ItemNet") : null;
                MethodInfo request = net != null ? net.GetType().GetMethod("Client_RequestPickup", new[] { typeof(Item) }) : null;
                Type database = FindType("EscapeFromDuckovCoopMod.CoopSyncDatabase");
                object registry = database != null ? Read(database, null, "Drops") : null;
                MethodInfo lookup = registry != null ? registry.GetType().GetMethod("TryGetByItem") : null;
                object[] arguments = { item, null };
                if (lookup != null && (bool)lookup.Invoke(registry, arguments) && arguments[1] != null)
                {
                    FieldInfo pendingField = net.GetType().GetField("_pendingPickups", BindingFlags.Instance | BindingFlags.NonPublic);
                    object pending = pendingField != null ? pendingField.GetValue(net) : null;
                    MethodInfo contains = pending != null ? pending.GetType().GetMethod("Contains", new[] { typeof(uint) }) : null;
                    object id = Read(arguments[1].GetType(), arguments[1], "DropId");
                    if (contains != null && (bool)contains.Invoke(pending, new[] { id })) return;
                }
                if (request != null) request.Invoke(net, new object[] { item });
            }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush Coop] Protect native drop pickup failed: " + e.Message); }
        }

        private sealed class NetworkDropLease
        {
            internal object Registry, Entry, ItemNet, Service;
            internal Item Item;
            internal uint Id;
            internal int Generation;
            internal string Room, SceneToken;
            internal MethodInfo TryGetById, Pickup, Broadcast;
            internal Type ContextType, RequestType, DespawnType;
        }

        internal static object CaptureNetworkDrop(Item item)
        {
            try
            {
                Type database = FindType("EscapeFromDuckovCoopMod.CoopSyncDatabase");
                Type manager = FindType("EscapeFromDuckovCoopMod.COOPManager");
                Type serviceType = FindType("EscapeFromDuckovCoopMod.NetService");
                object registry = database != null ? Read(database, null, "Drops") : null;
                object net = manager != null ? Read(manager, null, "ItemNet") : null;
                object service = serviceType != null ? Read(serviceType, null, "Instance") : null;
                if (registry == null || net == null || service == null) return null;
                MethodInfo lookup = registry.GetType().GetMethod("TryGetByItem");
                MethodInfo pickup = net.GetType().GetMethod("Server_HandlePickupRequest");
                Type tool = FindType("EscapeFromDuckovCoopMod.CoopTool");
                MethodInfo broadcast = null;
                if (tool != null)
                    foreach (MethodInfo method in tool.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        if (method.Name == "SendRpc" && method.IsGenericMethodDefinition && method.GetParameters().Length == 2) { broadcast = method; break; }
                object[] arguments = { item, null };
                if (lookup == null || pickup == null || broadcast == null || !(bool)lookup.Invoke(registry, arguments) || arguments[1] == null) return null;
                // 原生无 agent 的异常 fallback 会把 Item 本体当 Agent；它不能作为可拾取掉落租约。
                if (ReferenceEquals(Read(arguments[1].GetType(), arguments[1], "Agent"), item.gameObject)) return null;
                ParameterInfo[] parameters = pickup.GetParameters();
                Type despawn = FindType("EscapeFromDuckovCoopMod.ItemDespawnRpc");
                if (parameters.Length != 2 || despawn == null) return null;
                return new NetworkDropLease { Registry = registry, Entry = arguments[1], Item = item, ItemNet = net, Service = service,
                    Room = BossRushCoop.RunId, Generation = BossRushCoop.SceneGeneration, SceneToken = BossRushCoop.SceneToken,
                    Id = Convert.ToUInt32(Read(arguments[1].GetType(), arguments[1], "DropId")),
                    TryGetById = registry.GetType().GetMethod("TryGetById"), Pickup = pickup, Broadcast = broadcast.MakeGenericMethod(despawn),
                    ContextType = parameters[0].ParameterType, RequestType = parameters[1].ParameterType, DespawnType = despawn };
            }
            catch (Exception) { return null; }
        }

        internal static bool CompleteNetworkDrop(object captured)
        {
            var lease = captured as NetworkDropLease;
            if (lease == null) return false;
            try
            {
                Type database = FindType("EscapeFromDuckovCoopMod.CoopSyncDatabase");
                Type manager = FindType("EscapeFromDuckovCoopMod.COOPManager");
                Type serviceType = FindType("EscapeFromDuckovCoopMod.NetService");
                if (!BossRushCoop.IsHost || lease.Room != BossRushCoop.RunId || lease.Generation != BossRushCoop.SceneGeneration
                    || lease.SceneToken != BossRushCoop.SceneToken || database == null || manager == null || serviceType == null
                    || !ReferenceEquals(lease.Registry, Read(database, null, "Drops"))
                    || !ReferenceEquals(lease.ItemNet, Read(manager, null, "ItemNet"))
                    || !ReferenceEquals(lease.Service, Read(serviceType, null, "Instance"))) return false;
                object[] lookup = { lease.Id, null };
                if ((bool)lease.TryGetById.Invoke(lease.Registry, lookup))
                {
                    if (!ReferenceEquals(lookup[1], lease.Entry) || !ReferenceEquals(Read(lookup[1].GetType(), lookup[1], "Item"), lease.Item)) return false;
                    object context = Activator.CreateInstance(lease.ContextType, new[] { lease.Service, null });
                    object request = Activator.CreateInstance(lease.RequestType);
                    lease.RequestType.GetField("DropId").SetValue(request, lease.Id);
                    lease.Pickup.Invoke(lease.ItemNet, new[] { context, request });
                }
                // 原生拾取可能已经退役注册项；合堆/通知异常仍须确保所有复制体收到同一个幂等删除事实。
                object despawn = Activator.CreateInstance(lease.DespawnType);
                lease.DespawnType.GetField("DropId").SetValue(despawn, lease.Id);
                lease.Broadcast.Invoke(null, new[] { despawn, null });
                return true;
            }
            catch (Exception) { return false; }
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
            byte[] previous = outgoing; outgoing = bytes;
            try
            {
                if (!IsServer) return Bool(sendHost.Invoke(null, new object[] { Channel, writerBuilder }));
                if (all) { broadcast.Invoke(null, new object[] { Channel, writerBuilder, false }); return true; }
                if (peer == null) return false;
                return Bool(sendPeer.Invoke(null, new object[] { peer, Channel, writerBuilder, false }));
            }
            finally { outgoing = previous; }
        }

        private void SendSceneIdentityMarker(byte[] bytes)
        {
            if (!Bound || !NetworkStarted || !IsServer) throw new InvalidOperationException("Scene marker has no native host.");
            byte[] previous = outgoing; outgoing = bytes;
            try { broadcast.Invoke(null, new object[] { BossRushCoopSceneIdentityBridge.MarkerChannel, writerBuilder, false }); }
            finally { outgoing = previous; }
        }

        private void WriteOutgoing(object[] args)
        {
            if (outgoing == null) throw new InvalidOperationException("Native writer callback ran outside a send.");
            writerPut.Invoke(args[0], new object[] { outgoing });
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
            if (mapPaymentCompatibility != null) { mapPaymentCompatibility.Dispose(); mapPaymentCompatibility = null; }
            if (sceneIdentityBridge != null) { sceneIdentityBridge.Dispose(); sceneIdentityBridge = null; }
            SceneIdentity.Dispose();
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
            writerBuilder = null; outgoing = null;
        }
    }

    /// <summary>
    /// Read-only native ingress/egress observation. Our marker is delivered before its native Begin
    /// on the same ReliableOrdered channel, and is observed before ModNetworkApi queues it.
    /// </summary>
    internal sealed class BossRushCoopSceneIdentityBridge : IDisposable
    {
        internal const string MarkerChannel = "BossRush.SceneIdentity.v1";
        private const string PatchOwner = "BossRush.Coop.SceneIdentity.v1";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static BossRushCoopSceneIdentityBridge active;
        private readonly Type sceneNet;
        private readonly BossRushCoopSceneIdentity identity;
        private readonly Func<bool> live, host;
        private readonly Func<object> service, peer;
        private readonly Func<string> session;
        private readonly Action<byte[]> send;
        private readonly FieldInfo nativeInstance, voteActive;
        private readonly FieldInfo[] destination;
        private FieldInfo[] messageDestination;
        private PropertyInfo isServer, sender, contextService, channel, payload;
        private Harmony harmony;
        private long serial;
        private bool reported;

        internal BossRushCoopSceneIdentityBridge(Type sceneNet, BossRushCoopSceneIdentity identity,
            Func<bool> live, Func<bool> host, Func<object> service, Func<object> peer, Func<string> session, Action<byte[]> send)
        {
            this.sceneNet = sceneNet; this.identity = identity; this.live = live; this.host = host;
            this.service = service; this.peer = peer; this.session = session; this.send = send;
            nativeInstance = Field(sceneNet, "Instance", sceneNet);
            voteActive = Field(sceneNet, "sceneVoteActive", typeof(bool));
            destination = Destination(sceneNet, true);
        }

        private static FieldInfo Field(Type type, string name, Type expected)
        {
            FieldInfo field = type.GetField(name, Flags);
            if (field == null || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static FieldInfo[] Destination(Type type, bool native)
        {
            string[] names = native
                ? new[] { "sceneTargetId", "sceneCurtainGuid", "sceneNotifyEvac", "sceneSaveToFile", "sceneUseLocation", "sceneLocationName" }
                : new[] { "SceneId", "CurtainGuid", "NotifyEvac", "SaveToFile", "UseLocation", "LocationName" };
            Type[] types = { typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(string) };
            var result = new FieldInfo[names.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Field(type, names[i], types[i]);
            return result;
        }

        private static BossRushCoopSceneMarker ReadDestination(FieldInfo[] fields, object value)
        {
            return new BossRushCoopSceneMarker { sceneId = fields[0].GetValue(value) as string, curtainGuid = fields[1].GetValue(value) as string,
                notifyEvac = (bool)fields[2].GetValue(value), saveToFile = (bool)fields[3].GetValue(value),
                useLocation = (bool)fields[4].GetValue(value), locationName = fields[5].GetValue(value) as string };
        }

        internal void Install()
        {
            if (harmony != null) return;
            if (active != null) active.Dispose();
            active = this;
            harmony = new Harmony(PatchOwner);
            try
            {
                Type rpc = sceneNet.Assembly.GetType(sceneNet.Namespace + ".RPCScene", true);
                Type modRpc = sceneNet.Assembly.GetType(sceneNet.Namespace + ".RPCModApi", true);
                MethodInfo begin = rpc.GetMethod("HandleSceneBeginLoad", Flags);
                MethodInfo mod = modRpc.GetMethod("HandleModApiMessage", Flags);
                if (begin == null || mod == null || !begin.IsStatic || !mod.IsStatic
                    || begin.GetParameters().Length != 2 || mod.GetParameters().Length != 2)
                    throw new MissingMethodException("Native scene marker ingress contract changed.");
                Type context = begin.GetParameters()[0].ParameterType;
                if (!context.IsValueType || mod.GetParameters()[0].ParameterType != context)
                    throw new MissingMemberException("Native RPC context contract changed.");
                isServer = context.GetProperty("IsServer", Flags);
                sender = context.GetProperty("Sender", Flags);
                contextService = context.GetProperty("Service", Flags);
                Type modMessage = mod.GetParameters()[1].ParameterType;
                channel = modMessage.GetProperty("Channel", Flags);
                payload = modMessage.GetProperty("Payload", Flags);
                if (isServer == null || isServer.PropertyType != typeof(bool) || sender == null || contextService == null
                    || channel == null || channel.PropertyType != typeof(string) || payload == null || payload.PropertyType != typeof(byte[]))
                    throw new MissingMemberException("Native marker payload contract changed.");
                messageDestination = Destination(begin.GetParameters()[1].ParameterType, false);
                MethodInfo broadcastBegin = sceneNet.GetMethod("Server_BroadcastBeginSceneLoad", Flags, null, Type.EmptyTypes, null);
                if (broadcastBegin == null) throw new MissingMethodException(sceneNet.FullName, "Server_BroadcastBeginSceneLoad");
                var sendPatch = new HarmonyMethod(typeof(BossRushCoopSceneIdentityBridge), "ObserveNativeSend") { priority = Priority.Last };
                harmony.Patch(broadcastBegin, transpiler: sendPatch);
                Patch(mod, "BeforeMarkerIngress", null);
                Patch(begin, "BeforeBeginIngress", null);
                foreach (string name in new[] { "Host_BeginSceneVote_Simple", "Client_RequestBeginSceneVote", "Client_OnSceneVoteStart" })
                    Patch(Require(name), "BeforeVote", "AfterNewVote");
                foreach (string name in new[] { "CancelVote", "Client_OnVoteCancelled" })
                    Patch(Require(name), "BeforeVote", "AfterCancel");
                identity.Install();
            }
            catch { Dispose(); throw; }
        }

        private MethodInfo Require(string name)
        {
            MethodInfo method = sceneNet.GetMethod(name, Flags);
            if (method == null) throw new MissingMethodException(sceneNet.FullName, name);
            return method;
        }

        private void Patch(MethodInfo method, string prefix, string postfix)
        {
            Type type = typeof(BossRushCoopSceneIdentityBridge);
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(type, prefix), postfix == null ? null : new HarmonyMethod(type, postfix));
        }

        private bool Ingress(object context)
        {
            return live() && !host() && context != null && !(bool)isServer.GetValue(context, null)
                && ReferenceEquals(contextService.GetValue(context, null), service())
                && peer() != null && ReferenceEquals(sender.GetValue(context, null), peer());
        }

        private void Report(Exception error)
        {
            if (reported) return;
            reported = true;
            ModBehaviour.CriticalLog("coop-scene-identity", "[BossRush Coop] Native scene identity observation failed: " + error.Message);
        }

        // Insert beside the installed native send, after its destination has been built and its
        // possible spectator Closure has returned. Preserve the exact native in-struct and peer.
        private static IEnumerable<CodeInstruction> ObserveNativeSend(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var result = new List<CodeInstruction>();
            int matched = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                var method = instruction.operand as MethodInfo;
                if (instruction.opcode != OpCodes.Call || method == null || !method.IsGenericMethod
                    || method.DeclaringType.FullName != "EscapeFromDuckovCoopMod.CoopTool" || method.Name != "SendRpc"
                    || method.GetGenericArguments().Length != 1 || method.GetGenericArguments()[0].Name != "SceneBeginLoadRpc")
                { result.Add(instruction); continue; }
                var parameters = method.GetParameters();
                if (parameters.Length != 2 || !parameters[0].ParameterType.IsByRef || method.ReturnType != typeof(void))
                    throw new MissingMethodException("Native SceneBegin send signature changed.");
                LocalBuilder excluded = generator.DeclareLocal(parameters[1].ParameterType);
                LocalBuilder marker = generator.DeclareLocal(typeof(BossRushCoopSceneMarker));
                var first = new CodeInstruction(OpCodes.Stloc, excluded);
                first.labels.AddRange(instruction.labels); instruction.labels.Clear();
                first.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                result.Add(first);
                result.Add(new CodeInstruction(OpCodes.Dup));
                result.Add(new CodeInstruction(OpCodes.Ldobj, method.GetGenericArguments()[0]));
                result.Add(new CodeInstruction(OpCodes.Box, method.GetGenericArguments()[0]));
                result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                result.Add(new CodeInstruction(OpCodes.Call, typeof(BossRushCoopSceneIdentityBridge).GetMethod("BeforeNativeSend", Flags)));
                result.Add(new CodeInstruction(OpCodes.Stloc, marker));
                result.Add(new CodeInstruction(OpCodes.Ldloc, excluded));
                result.Add(instruction);
                result.Add(new CodeInstruction(OpCodes.Ldloc, marker));
                result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                result.Add(new CodeInstruction(OpCodes.Call, typeof(BossRushCoopSceneIdentityBridge).GetMethod("AfterNativeSend", Flags)));
                matched++;
            }
            if (matched != 1) throw new MissingMethodException("Expected one installed native SceneBegin send, found " + matched);
            return result;
        }

        private static BossRushCoopSceneMarker BeforeNativeSend(object message, object instance)
        {
            var owner = active;
            if (owner == null || !owner.live() || !owner.host() || !ReferenceEquals(owner.nativeInstance.GetValue(null), instance)) return null;
            try
            {
                BossRushCoopSceneMarker marker = ReadDestination(owner.messageDestination, message);
                marker.session = owner.session(); marker.serial = ++owner.serial; marker.token = Guid.NewGuid().ToString("N");
                if (!marker.Valid()) throw new InvalidOperationException("Native travel preceded its room identity.");
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(BossRushCoopJson.ToJson(marker));
                owner.send(bytes); // Synchronous native backend send before the original native Begin send.
                return marker;
            }
            catch (Exception e) { owner.Report(e); }
            return null;
        }

        private static void AfterNativeSend(BossRushCoopSceneMarker marker, object instance)
        {
            var owner = active;
            if (owner != null && marker != null && owner.live() && owner.host()
                && ReferenceEquals(owner.nativeInstance.GetValue(null), instance)) owner.identity.ApproveHost(marker);
        }

        private static void BeforeMarkerIngress(object __0, object __1)
        {
            var owner = active;
            if (owner == null) return;
            try
            {
                if (!owner.Ingress(__0) || __1 == null || (string)owner.channel.GetValue(__1, null) != MarkerChannel) return;
                byte[] bytes = owner.payload.GetValue(__1, null) as byte[];
                if (bytes == null || bytes.Length == 0 || bytes.Length > 4096) return;
                // Observe before Receive -> Enqueue. Never drain, consume or reorder the native ModNetwork queue.
                owner.identity.StageMarker(BossRushCoopJson.FromJson<BossRushCoopSceneMarker>(System.Text.Encoding.UTF8.GetString(bytes)));
            }
            catch (Exception e) { owner.Report(e); }
        }

        private static void BeforeBeginIngress(object __0, object __1)
        {
            var owner = active;
            if (owner == null) return;
            try
            {
                if (owner.Ingress(__0) && __1 != null)
                    owner.identity.ObserveNativeBegin(ReadDestination(owner.messageDestination, __1));
            }
            catch (Exception e) { owner.Report(e); }
        }

        private struct VoteObservation { internal bool Active; internal long Revision; }

        private static void BeforeVote(object __instance, out VoteObservation __state)
        {
            __state = new VoteObservation { Active = active != null && (bool)active.voteActive.GetValue(__instance),
                Revision = active == null ? -1 : active.identity.Revision };
        }

        private static void AfterNewVote(object __instance, VoteObservation __state)
        {
            var owner = active;
            if (owner != null && !__state.Active && (bool)owner.voteActive.GetValue(__instance)
                && owner.identity.Revision == __state.Revision) owner.identity.CancelPending();
        }

        private static void AfterCancel(object __instance, VoteObservation __state)
        {
            var owner = active;
            if (owner != null && __state.Active && !(bool)owner.voteActive.GetValue(__instance)
                && owner.identity.Revision == __state.Revision) owner.identity.CancelPending();
        }

        public void Dispose()
        {
            if (harmony != null) { harmony.UnpatchAll(PatchOwner); harmony = null; }
            if (ReferenceEquals(active, this)) active = null;
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
        private readonly Func<object> hostPeer;
        private readonly FieldInfo nativeInstance, target, launched, authorizedTarget;
        private MethodInfo spectatorBegin;
        private PropertyInfo beginIsServer, beginSender, beginService;
        private FieldInfo nativeServiceInstance, beginDestination, spectatorInstance, spectatorActive, spectatorPending, voteActive;
        private Harmony harmony;
        private bool subscribed;
        private ReturnLease lease, pending, spectatorApproval;

        private sealed class ReturnLease
        {
            internal object Native;
            internal object ApprovalHost, Spectator;
            internal CharacterMainControl Body;
            internal Duckov.Scenes.MultiSceneCore Core;
            internal string Run, Asset;
            internal int Generation, SceneHandle;
            internal bool Authorized;
        }

        internal BossRushCoopSceneLoadCompatibility(Type nativeType, Func<bool> networkStarted, Func<object> hostPeer)
        {
            this.nativeType = nativeType;
            this.networkStarted = networkStarted;
            this.hostPeer = hostPeer;
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
                InstallSpectatorApproval();
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

        private void InstallSpectatorApproval()
        {
            Type rpc = nativeType.Assembly.GetType(nativeType.Namespace + ".RPCScene", true);
            Type spectator = nativeType.Assembly.GetType(nativeType.Namespace + ".Spectator", true);
            spectatorBegin = rpc.GetMethod("HandleSceneBeginLoad", NativeFlags);
            if (spectatorBegin == null || !spectatorBegin.IsStatic || spectatorBegin.GetParameters().Length != 2)
                throw new MissingMethodException(rpc.FullName, "HandleSceneBeginLoad");
            ParameterInfo[] args = spectatorBegin.GetParameters();
            beginIsServer = args[0].ParameterType.GetProperty("IsServer", NativeFlags);
            beginSender = args[0].ParameterType.GetProperty("Sender", NativeFlags);
            beginService = args[0].ParameterType.GetProperty("Service", NativeFlags);
            nativeServiceInstance = beginService == null ? null : beginService.PropertyType.GetField("Instance", NativeFlags);
            beginDestination = args[1].ParameterType.GetField("SceneId", NativeFlags);
            spectatorInstance = spectator.GetField("Instance", NativeFlags);
            spectatorActive = spectator.GetField("_spectatorActive", NativeFlags);
            spectatorPending = spectator.GetField("_spectatorEndOnVotePending", NativeFlags);
            voteActive = RequireField("sceneVoteActive");
            if (beginIsServer == null || beginIsServer.PropertyType != typeof(bool) || beginSender == null || beginService == null || nativeServiceInstance == null
                || beginDestination == null || beginDestination.FieldType != typeof(string) || spectatorInstance == null
                || spectatorActive == null || spectatorActive.FieldType != typeof(bool)
                || spectatorPending == null || spectatorPending.FieldType != typeof(bool))
                throw new MissingMemberException("Native spectator scene approval contract changed.");
            Patch(spectatorBegin, "BeforeSpectatorApproval", "AfterSpectatorApproval");
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
            if (value.ApprovalHost != null && (!BossRushCoop.IsClient || !ReferenceEquals(value.ApprovalHost, hostPeer()))) return false;
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
        private static void BeforeSpectatorApproval(object __0, object __1, out ReturnLease __state)
        {
            __state = null;
            if (active == null || __0 == null || __1 == null || !BossRushCoop.IsClient
                || (bool)active.beginIsServer.GetValue(__0, null)) return;
            object peer = active.hostPeer();
            object service = active.beginService.GetValue(__0, null);
            if (peer == null || service == null || !ReferenceEquals(service, active.nativeServiceInstance.GetValue(null))
                || !ReferenceEquals(peer, active.beginSender.GetValue(__0, null))) return;
            active.OnIdentityChanged();
            // 原版死客在此 RPC 内只展示 Closure 后 return，根本不会调用 TryPerform。
            // 授权来自本次房主 BeginLoad 的真实目标，不能沿用上次加载的 launched/TTL。
            object spectator = active.spectatorInstance.GetValue(null);
            if (spectator == null || !(bool)active.spectatorActive.GetValue(spectator)
                || !(bool)active.spectatorPending.GetValue(spectator)) return;
            __state = active.Capture(active.nativeInstance.GetValue(null), active.beginDestination.GetValue(__1) as string);
            if (__state != null) { __state.ApprovalHost = peer; __state.Spectator = spectator; }
            active.spectatorApproval = __state;
        }
        private static void AfterSpectatorApproval(ReturnLease __state)
        {
            if (active == null || __state == null || !ReferenceEquals(active.spectatorApproval, __state)) return;
            active.spectatorApproval = null;
            if (!active.Current(__state, __state.Native)) return;
            object spectator = active.spectatorInstance.GetValue(null);
            if (spectator == null || !ReferenceEquals(spectator, __state.Spectator) || (bool)active.spectatorActive.GetValue(spectator)
                || (bool)active.spectatorPending.GetValue(spectator) || (bool)active.voteActive.GetValue(__state.Native)) return;
            active.lease = __state;
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
        private void OnIdentityChanged() { lease = pending = spectatorApproval = null; }
        private void OnSceneUnloaded(Scene scene)
        {
            if ((lease != null && lease.SceneHandle == scene.handle) || (pending != null && pending.SceneHandle == scene.handle)
                || (spectatorApproval != null && spectatorApproval.SceneHandle == scene.handle)) OnIdentityChanged();
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
