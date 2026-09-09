using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Eflatun.SceneReference;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// WIRE+：将 bundle 场景接入官方按名称加载的 SceneLoader。
    /// BuildIndex 始终保留 Unity 返回的 -1；不向全局 Entries 注入会匹配所有 -1 的条目。
    /// 单 Scene 同时承载 MultiSceneCore 与世界，官方 LoadAndTeleport 仍走原实现。
    /// </summary>
    internal static class SkyIslandSceneReferenceBridge
    {
        internal const string SceneId = "BossRush_SkyIsland";
        internal const string ScenePath = "Assets/SkyIsland/SkyIslandRaid.unity";
        internal const string SceneName = "SkyIslandRaid";
        internal const string SpawnLocation = "StartPoints/PlayerSpawn";
        private const string SceneGuid = "9b118e66b9444c87aaa49a5e36ba8c31";
        private const string HarmonyId = "com.bossrush.skyisland.scene";
        private static bool registered, addedGuid, addedPath, subscribed, shutdownRequested;
        private static IDictionary<string, string> guidMap, pathMap;
        private static SceneReference sceneReference;
        private static SceneInfoEntry sceneInfo;
        private static Harmony harmony;
        private static FieldInfo activeSubSceneField, cachedEntryField, loadingField, activeObjectsField, loadedEventField;
        private static MethodInfo localLoadedMethod;
        private static object initializationOwner;
        private static int initializationSceneHandle;
        private static string initializationFailure;

        internal static SceneReference SceneReference
        {
            get
            {
                if (!EnsureRegistered()) throw new InvalidOperationException("天空岛官方场景桥接尚未就绪");
                return sceneReference;
            }
        }

        internal static bool IsScene(Scene scene)
        {
            return scene.IsValid() && string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsActiveScene
        {
            get { return IsScene(SceneManager.GetActiveScene()); }
        }

        internal static bool EnsureRegistered()
        {
            if (registered) { shutdownRequested = false; return true; }
            try
            {
                guidMap = SceneGuidToPathMapProvider.SceneGuidToPathMap as IDictionary<string, string>;
                pathMap = SceneGuidToPathMapProvider.ScenePathToGuidMap as IDictionary<string, string>;
                if (guidMap == null || pathMap == null) throw new InvalidOperationException("Eflatun 场景路径表不可扩展");
                string current;
                if (guidMap.TryGetValue(SceneGuid, out current) && !string.Equals(current, ScenePath, StringComparison.Ordinal))
                    throw new InvalidOperationException("天空岛场景 GUID 已被其它路径占用");
                if (pathMap.TryGetValue(ScenePath, out current) && !string.Equals(current, SceneGuid, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("天空岛场景路径已被其它 GUID 占用");
                if (!guidMap.ContainsKey(SceneGuid)) { guidMap.Add(SceneGuid, ScenePath); addedGuid = true; }
                if (!pathMap.ContainsKey(ScenePath)) { pathMap.Add(ScenePath, SceneGuid); addedPath = true; }
                sceneReference = new SceneReference(SceneGuid);
                sceneInfo = new SceneInfoEntry(SceneId, sceneReference);
                FieldInfo name = RequireField(typeof(SceneInfoEntry), "displayName");
                name.SetValue(sceneInfo, "BossRush_SkyIsland_SceneName");
                LocalizationHelper.InjectLocalization("BossRush_SkyIsland_SceneName", L10n.T("天空岛 · 晴岚群岛", "Sky Islands · Qinglan"));
                activeSubSceneField = RequireField(typeof(MultiSceneCore), "activeSubScene");
                cachedEntryField = RequireField(typeof(MultiSceneCore), "cachedSubsceneEntry");
                loadingField = RequireField(typeof(MultiSceneCore), "isLoading");
                activeObjectsField = RequireField(typeof(MultiSceneCore), "setActiveWithSceneObjects");
                loadedEventField = RequireField(typeof(MultiSceneCore), "OnSubSceneLoaded");
                localLoadedMethod = RequireMethod(typeof(MultiSceneCore), "LocalOnSubSceneLoaded", typeof(Scene));
                harmony = new Harmony(HarmonyId);
                Patch(AccessTools.PropertyGetter(typeof(SceneReference), "UnsafeReason"), "ReferenceSafetyPrefix");
                Patch(RequireMethod(typeof(SceneInfoCollection), "GetSceneInfo", typeof(string)), "SceneInfoPrefix");
                Patch(RequireMethod(typeof(SceneInfoCollection), "GetSceneID", typeof(SceneReference)), "SceneIdPrefix");
                Patch(RequireMethod(typeof(SceneInfoCollection), "GetSceneID", typeof(int)), "SceneIdByBuildIndexPrefix");
                Patch(AccessTools.PropertyGetter(typeof(MultiSceneCore), "SceneInfo"), "CoreInfoPrefix");
                Patch(AccessTools.PropertyGetter(typeof(MultiSceneCore), "DisplayName"), "CoreNamePrefix");
                Patch(AccessTools.PropertyGetter(typeof(MultiSceneCore), "DisplaynameRaw"), "CoreRawNamePrefix");
                Patch(AccessTools.PropertyGetter(typeof(MultiSceneCore), "MainSceneID"), "CoreMainIdPrefix");
                Patch(AccessTools.PropertyGetter(typeof(MultiSceneCore), "ActiveSubSceneID"), "CoreActiveIdPrefix");
                Patch(RequireMethod(typeof(MultiSceneCore), "LoadSubScene", typeof(SceneReference), typeof(bool)), "LoadSelfPrefix");
                Patch(RequireMethod(typeof(MultiSceneCore), "GetSetActiveWithSceneParent", typeof(int)), "ActiveParentPrefix");
                Patch(RequireMethod(typeof(SceneLocationsProvider), "GetProviderOfScene", typeof(SceneReference)), "ProviderReferencePrefix");
                Patch(RequireMethod(typeof(SceneLocationsProvider), "GetProviderOfScene", typeof(Scene)), "ProviderScenePrefix");
                Patch(RequireMethod(typeof(SceneLocationsProvider), "GetLocation", typeof(SceneReference), typeof(string)), "LocationReferencePrefix");
                Patch(RequireMethod(typeof(SceneLocationsProvider), "GetLocation", typeof(string), typeof(string)), "LocationIdPrefix");
                Patch(RequireMethod(typeof(LevelManager), "NotifySaveBeforeLoadScene", typeof(bool)), "SaveBeforeLoadPrefix");
                MethodInfo loader = RequireMethod(typeof(SceneLoader), "LoadScene", typeof(SceneReference), typeof(SceneReference),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(MultiSceneLocation), typeof(bool), typeof(bool));
                var stateMachine = (AsyncStateMachineAttribute)Attribute.GetCustomAttribute(loader, typeof(AsyncStateMachineAttribute));
                if (stateMachine == null) throw new InvalidOperationException("官方场景加载状态机契约缺失");
                MethodInfo moveNext = RequireMethod(stateMachine.StateMachineType, "MoveNext");
                RequireField(stateMachine.StateMachineType, "sceneReference");
                harmony.Patch(moveNext, transpiler: new HarmonyMethod(RequireMethod(typeof(SkyIslandSceneReferenceBridge),
                    "LoaderInitializationTranspiler", typeof(IEnumerable<CodeInstruction>), typeof(MethodBase))));
                if (!subscribed)
                {
                    SceneLoader.onFinishedLoadingScene += OnSceneLoadFinished;
                    SceneManager.sceneUnloaded += OnSceneUnloaded;
                    subscribed = true;
                }
                registered = true;
                Debug.Log("[SkyIsland] 官方 SceneReference 桥接已就绪，BuildIndex 保留 -1，目标=" + ScenePath);
                return true;
            }
            catch (Exception e)
            {
                CleanupRegistration();
                ModBehaviour.CriticalLog("sky-island-scene-bridge", "[SkyIsland] 官方场景桥接失败: " + e.Message);
                return false;
            }
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameters)
        {
            MethodInfo method = AccessTools.Method(type, name, parameters);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static void Patch(MethodInfo target, string prefix)
        {
            if (target == null) throw new MissingMethodException("天空岛桥接目标不存在: " + prefix);
            MethodInfo prefixMethod = AccessTools.Method(typeof(SkyIslandSceneReferenceBridge), prefix);
            if (prefixMethod == null) throw new MissingMethodException(typeof(SkyIslandSceneReferenceBridge).FullName, prefix);
            harmony.Patch(target, prefix: new HarmonyMethod(prefixMethod));
        }

        private static bool OwnsReference(SceneReference reference)
        {
            return registered && reference != null && string.Equals(reference.Guid, SceneGuid, StringComparison.OrdinalIgnoreCase);
        }

        private static bool OwnsCore(MultiSceneCore core)
        {
            return registered && core != null && IsScene(core.gameObject.scene);
        }

        internal static void BeginInitialization(object owner)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (initializationOwner != null && !ReferenceEquals(initializationOwner, owner))
                throw new InvalidOperationException("上一段天空岛初始化尚未释放");
            initializationOwner = owner;
            initializationSceneHandle = 0;
            initializationFailure = null;
        }

        internal static void BindInitializationScene(object owner, Scene scene)
        {
            if (ReferenceEquals(initializationOwner, owner) && IsScene(scene)) initializationSceneHandle = scene.handle;
        }

        internal static void AbortInitialization(object owner, string reason)
        {
            if (!ReferenceEquals(initializationOwner, owner)) return;
            if (initializationFailure == null) initializationFailure = reason ?? "天空岛初始化已取消";
        }

        internal static void EndInitialization(object owner)
        {
            if (!ReferenceEquals(initializationOwner, owner)) return;
            initializationOwner = null;
            initializationSceneHandle = 0;
            initializationFailure = null;
        }

        private static bool SaveBeforeLoadPrefix(LevelManager __instance)
        {
            // 初始化失败的角色尚未完成恢复，返航应继续读取出发前的官方快照。
            if (initializationFailure == null || initializationOwner == null || __instance == null ||
                __instance.gameObject.scene.handle != initializationSceneHandle || !IsScene(__instance.gameObject.scene)) return true;
            CharacterMainControl main = __instance.MainCharacter;
            return main != null && main.Health != null && main.Health.IsDead;
        }

        private static bool LoaderLevelInitialized(SceneReference reference)
        {
            if (OwnsReference(reference) && initializationOwner != null && initializationSceneHandle != 0)
            {
                Scene scene = reference.LoadedScene;
                if (initializationFailure != null)
                    throw new OperationCanceledException(initializationFailure);
                if (!scene.IsValid() || !scene.isLoaded || scene.handle != initializationSceneHandle)
                    throw new OperationCanceledException("天空岛初始化所属场景已卸载");
            }
            return LevelManager.LevelInited;
        }

        private static IEnumerable<CodeInstruction> LoaderInitializationTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo query = AccessTools.PropertyGetter(typeof(LevelManager), "LevelInited");
            MethodInfo replacement = RequireMethod(typeof(SkyIslandSceneReferenceBridge), "LoaderLevelInitialized", typeof(SceneReference));
            FieldInfo reference = RequireField(__originalMethod.DeclaringType, "sceneReference");
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!instruction.Calls(query)) { yield return instruction; continue; }
                // 保留原指令的跳转标签和异常块边界，只给这一次官方等待补充取消信号。
                CodeInstruction first = new CodeInstruction(instruction);
                first.opcode = OpCodes.Ldarg_0;
                first.operand = null;
                yield return first;
                yield return new CodeInstruction(OpCodes.Ldfld, reference);
                yield return new CodeInstruction(OpCodes.Call, replacement);
                replaced++;
            }
            if (replaced != 1) throw new InvalidOperationException("官方关卡初始化等待契约变化：匹配 " + replaced + " 处");
        }

        private static bool ReferenceSafetyPrefix(SceneReference __instance, ref SceneReferenceUnsafeReason __result)
        {
            if (!OwnsReference(__instance)) return true;
            __result = SceneReferenceUnsafeReason.None;
            return false;
        }

        private static bool SceneInfoPrefix(string sceneID, ref SceneInfoEntry __result)
        {
            if (!registered || !string.Equals(sceneID, SceneId, StringComparison.Ordinal)) return true;
            __result = sceneInfo;
            return false;
        }

        private static bool SceneIdPrefix(SceneReference sceneRef, ref string __result)
        {
            if (!OwnsReference(sceneRef)) return true;
            __result = SceneId;
            return false;
        }

        /// <summary>
        /// 官方小地图用 `GetSceneID(GetActiveScene().buildIndex)` 反查当前地图身份
        /// （`MiniMapView.CeneterPlayer` 与无参的 `TryConvertWorldToMinimapPosition`）。
        /// bundle 场景的 buildIndex 恒为 -1，而天空岛刻意没有注入全局 entries，原实现只会返回 null。
        ///
        /// 判定条件是「**当前活动场景**就是天空岛」，不是「buildIndex == -1」——
        /// 后者会把所有自定义 bundle 场景一并匹配掉，正是这个桥一开始要避免的事。
        /// </summary>
        private static bool SceneIdByBuildIndexPrefix(int buildIndex, ref string __result)
        {
            if (!registered || buildIndex >= 0 || !IsActiveScene) return true;
            __result = SceneId;
            return false;
        }

        private static bool CoreInfoPrefix(MultiSceneCore __instance, ref SceneInfoEntry __result)
        {
            if (!OwnsCore(__instance)) return true;
            __result = sceneInfo;
            return false;
        }

        private static bool CoreNamePrefix(MultiSceneCore __instance, ref string __result)
        {
            if (!OwnsCore(__instance)) return true;
            __result = sceneInfo.DisplayName;
            return false;
        }

        private static bool CoreRawNamePrefix(MultiSceneCore __instance, ref string __result)
        {
            if (!OwnsCore(__instance)) return true;
            __result = sceneInfo.DisplayNameRaw;
            return false;
        }

        private static bool CoreMainIdPrefix(ref string __result)
        {
            if (!OwnsCore(MultiSceneCore.Instance)) return true;
            __result = SceneId;
            return false;
        }

        private static bool CoreActiveIdPrefix(ref string __result)
        {
            MultiSceneCore core = MultiSceneCore.Instance;
            if (!OwnsCore(core)) return true;
            Scene active = (Scene)activeSubSceneField.GetValue(core);
            __result = !core.IsLoading && IsScene(active) && active.isLoaded ? SceneId : null;
            return false;
        }

        private static bool LoadSelfPrefix(MultiSceneCore __instance, SceneReference targetScene, ref UniTask<bool> __result)
        {
            if (!OwnsCore(__instance) || !OwnsReference(targetScene)) return true;
            __result = ActivateSelf(__instance);
            return false;
        }

        private static async UniTask<bool> ActivateSelf(MultiSceneCore core)
        {
            Scene scene = core.gameObject.scene;
            if (!scene.isLoaded || !IsActiveScene || SceneManager.GetActiveScene().handle != scene.handle ||
                SceneLoader.IsSceneLoading || core.IsLoading) return false;
            Scene previous = (Scene)activeSubSceneField.GetValue(core);
            if (previous.IsValid() && previous.handle == scene.handle && cachedEntryField.GetValue(core) != null) return true;
            SubSceneEntry entry = core.SubScenes == null ? null : core.SubScenes.Find(value => value != null && value.sceneID == SceneId);
            if (entry == null) throw new InvalidOperationException("天空岛 MultiSceneCore 未配置自身 SubSceneEntry");
            if (FindProvider(scene) == null) throw new InvalidOperationException("天空岛缺少真实 SceneLocationsProvider");
            object previousEntry = cachedEntryField.GetValue(core);
            bool completed = false;
            loadingField.SetValue(core, true);
            try
            {
                activeSubSceneField.SetValue(core, scene);
                cachedEntryField.SetValue(core, entry);
                localLoadedMethod.Invoke(core, new object[] { scene });
                MultiSceneCore.SetVisited(SceneId);
                await UniTask.NextFrame();
                if (!OwnsCore(core) || !IsActiveScene || SceneManager.GetActiveScene().handle != scene.handle || SceneLoader.IsSceneLoading)
                    return false;
                // 对应官方 LoadSubScene 的完成顺序：先解除 loading，之后才广播，墓碑和场景服务由官方订阅者恢复。
                loadingField.SetValue(core, false);
                var callback = loadedEventField.GetValue(null) as Action<MultiSceneCore, Scene>;
                if (callback != null)
                {
                    foreach (Action<MultiSceneCore, Scene> listener in callback.GetInvocationList())
                    {
                        try { listener(core, scene); }
                        catch (Exception e) { Debug.LogWarning("[SkyIsland] OnSubSceneLoaded 接收者异常: " + e.Message); }
                    }
                }
                completed = true;
                Debug.Log("[SkyIsland] 官方单场景子区就绪: " + scene.path);
                return true;
            }
            finally
            {
                if (core != null)
                {
                    loadingField.SetValue(core, false);
                    if (!completed)
                    {
                        activeSubSceneField.SetValue(core, previous);
                        cachedEntryField.SetValue(core, previousEntry);
                    }
                }
            }
        }

        private static bool ActiveParentPrefix(MultiSceneCore __instance, int sceneBuildIndex, ref Transform __result)
        {
            if (!OwnsCore(__instance) || !IsActiveScene ||
                SceneManager.GetActiveScene().handle != __instance.gameObject.scene.handle ||
                sceneBuildIndex != __instance.gameObject.scene.buildIndex) return true;
            var objects = activeObjectsField.GetValue(__instance) as Dictionary<int, GameObject>;
            if (objects == null) throw new InvalidOperationException("天空岛官方 scene owner 表不存在");
            GameObject parent;
            if (!objects.TryGetValue(sceneBuildIndex, out parent) || parent == null)
            {
                parent = new GameObject(SceneId);
                parent.transform.SetParent(__instance.transform, false);
                objects[sceneBuildIndex] = parent;
            }
            parent.SetActive(true);
            __result = parent.transform;
            return false;
        }

        private static SceneLocationsProvider FindProvider(Scene scene)
        {
            if (!IsScene(scene) || !scene.isLoaded) return null;
            foreach (SceneLocationsProvider provider in SceneLocationsProvider.ActiveProviders)
                if (provider != null && provider.gameObject.scene.handle == scene.handle && IsScene(provider.gameObject.scene)) return provider;
            return null;
        }

        private static bool ProviderReferencePrefix(SceneReference sceneReference, ref SceneLocationsProvider __result)
        {
            if (!OwnsReference(sceneReference)) return true;
            __result = FindProvider(sceneReference.LoadedScene);
            return false;
        }

        private static bool ProviderScenePrefix(Scene scene, ref SceneLocationsProvider __result)
        {
            if (!registered || !IsScene(scene)) return true;
            __result = FindProvider(scene);
            return false;
        }

        private static bool LocationReferencePrefix(SceneReference scene, string name, ref Transform __result)
        {
            if (!OwnsReference(scene)) return true;
            SceneLocationsProvider provider = FindProvider(scene.LoadedScene);
            __result = provider == null ? null : provider.GetLocation(name);
            return false;
        }

        private static bool LocationIdPrefix(string sceneID, string name, ref Transform __result)
        {
            if (!registered || !string.Equals(sceneID, SceneId, StringComparison.Ordinal)) return true;
            SceneLocationsProvider provider = FindProvider(sceneReference.LoadedScene);
            __result = provider == null ? null : provider.GetLocation(name);
            return false;
        }

        internal static void Shutdown()
        {
            shutdownRequested = true;
            // 官方 async 状态机仍会读取 Name；未退出目标 Scene 前不能移除它正在使用的路径和前缀。
            if (registered && (SceneLoader.IsSceneLoading || SceneManager.GetSceneByPath(ScenePath).isLoaded)) return;
            CleanupRegistration();
        }

        private static void OnSceneLoadFinished(SceneLoadingContext context)
        {
            if (shutdownRequested) Shutdown();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            if (shutdownRequested) Shutdown();
        }

        private static void CleanupRegistration()
        {
            registered = false;
            shutdownRequested = false;
            if (subscribed)
            {
                SceneLoader.onFinishedLoadingScene -= OnSceneLoadFinished;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }
            if (harmony != null)
            {
                try { harmony.UnpatchAll(HarmonyId); }
                catch (Exception e) { Debug.LogWarning("[SkyIsland] SceneReference 补丁退订失败: " + e.Message); }
                harmony = null;
            }
            string current;
            if (addedGuid && guidMap != null && guidMap.TryGetValue(SceneGuid, out current) && current == ScenePath) guidMap.Remove(SceneGuid);
            if (addedPath && pathMap != null && pathMap.TryGetValue(ScenePath, out current) && current == SceneGuid) pathMap.Remove(ScenePath);
            addedGuid = addedPath = false;
            guidMap = pathMap = null;
            sceneReference = null;
            sceneInfo = null;
            initializationOwner = null;
            initializationSceneHandle = 0;
            initializationFailure = null;
        }
    }
}
