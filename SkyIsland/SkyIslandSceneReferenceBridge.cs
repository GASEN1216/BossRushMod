using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.UI;
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
        private static AccessTools.FieldRef<MultiSceneCore, Scene> activeSubSceneRef;
        private static MethodInfo localLoadedMethod;
        private static object initializationOwner;
        private static int initializationSceneHandle;
        private static string initializationFailure;
        private static AsyncOperation initializationOperation;
        private static object initializationOperationOwner;
        private static object visualLoadOwner;
        private static SceneReference visualLoadTarget;
        private static BlackScreen visualBlackScreen;
        private static int visualBlackDebt;
        private static bool preserveTargetSnapshot;

        internal static SceneReference SceneReference
        {
            get
            {
                if (!EnsureRegistered()) throw new InvalidOperationException("天空岛官方场景桥接尚未就绪");
                return sceneReference;
            }
        }

        /// <summary>
        /// 这个场景是不是天空岛。**热路径**：它挂在 `MultiSceneCore.ActiveSubSceneID` 等全局前缀补丁上，
        /// 官方地图、迷雾与各类 HUD 在任何地图都会每帧问这个 getter；而 `Scene.path` 每读一次都新建一个托管字符串。
        ///
        /// 两级短路，先比整数：官方场景全在 Build Settings 里（buildIndex ≥ 0），天空岛来自 AssetBundle（恒为 -1，
        /// 见类注释），官方地图因此一个字符串都不读；岛上认出过一次就记住句柄，之后只比句柄。
        /// 句柄缓存在每次新的初始化开始时（<see cref="BeginInitialization"/>）与关闭时作废。
        /// </summary>
        internal static bool IsScene(Scene scene)
        {
            if (!scene.IsValid() || scene.buildIndex >= 0) return false;
            if (knownSceneHandle != 0 && scene.handle == knownSceneHandle) return true;
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase)) return false;
            knownSceneHandle = scene.handle;
            return true;
        }

        /// <summary>最近一次认出的天空岛场景句柄；0 表示未知。</summary>
        private static int knownSceneHandle;

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
                InjectLocalization();
                activeSubSceneField = RequireField(typeof(MultiSceneCore), "activeSubScene");
                // 同一个字段的**读**走 FieldRef：`ActiveSubSceneID` 的 getter 是官方地图 / 迷雾 / HUD 每帧都问的热路径，
                // `FieldInfo.GetValue` 每次都把 Scene 这个 struct 装箱一次（R-9 分项）。绑定目标不变，
                // 契约检查仍由上面这行 RequireField 负责——官方改名时先在那里炸，错误信息不变。
                activeSubSceneRef = AccessTools.FieldRefAccess<MultiSceneCore, Scene>(activeSubSceneField);
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

        /// <summary>与场景注册分离；宿主切语言时可刷新名称，不重装场景补丁。</summary>
        internal static void InjectLocalization()
        {
            LocalizationHelper.InjectLocalization("BossRush_SkyIsland_SceneName", L10n.T("天空岛 · 晴岚群岛", "Sky Islands · Qinglan"));
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
            initializationOperation = null;
            initializationOperationOwner = null;
            // 新的一趟出击会加载一个新的场景实例：旧句柄即使被复用也不能再当作天空岛。
            knownSceneHandle = 0;
        }

        internal static void BindInitializationScene(object owner, Scene scene)
        {
            if (ReferenceEquals(initializationOwner, owner) && IsScene(scene)) initializationSceneHandle = scene.handle;
        }

        internal static void AbortInitialization(object owner, string reason)
        {
            if (!ReferenceEquals(initializationOwner, owner)) return;
            if (initializationFailure == null) initializationFailure = reason ?? "天空岛初始化已取消";
            // 官方事件 / 黑幕 await 也会抛出异常，此时已没有下一帧回调替我们解除激活阻塞。
            if (initializationOperation != null && !initializationOperation.isDone)
                initializationOperation.allowSceneActivation = true;
        }

        internal static void EndInitialization(object owner)
        {
            if (!ReferenceEquals(initializationOwner, owner)) return;
            initializationOwner = null;
            initializationSceneHandle = 0;
            initializationFailure = null;
            initializationOperation = null;
            initializationOperationOwner = null;
        }

        internal static bool HasPendingInitializationLoad(object owner)
        {
            return ReferenceEquals(initializationOperationOwner, owner) && initializationOperation != null && !initializationOperation.isDone;
        }

        private static UniTask LoaderNextFrame(SceneReference reference, AsyncOperation operation)
        {
            if (OwnsReference(reference) && initializationOwner != null)
            {
                if (operation != null) { initializationOperation = operation; initializationOperationOwner = initializationOwner; }
                if (initializationFailure != null)
                {
                    // Unity 不支持取消场景 AsyncOperation；不放开激活，排在它后面的返航也会永久等待。
                    if (operation != null && !operation.isDone) operation.allowSceneActivation = true;
                    throw new OperationCanceledException(initializationFailure);
                }
            }
            return UniTask.NextFrame();
        }

        private static void LoaderSetSceneActivation(AsyncOperation operation, bool allow, SceneReference reference)
        {
            // 在官方第一次关掉激活时就登记；目标已读到 0.9 时可能跳过所有早期 NextFrame。
            if (TracksLoadVisuals(reference) || (OwnsReference(reference) && initializationOwner != null))
            {
                initializationOperation = operation;
                initializationOperationOwner = TracksLoadVisuals(reference) ? visualLoadOwner : initializationOwner;
            }
            operation.allowSceneActivation = allow;
        }

        internal static void BeginLoadVisuals(object owner, SceneReference target, bool preservePreviousTargetSnapshot = false)
        {
            if (visualLoadOwner != null) throw new InvalidOperationException("上一段天空岛切图表现尚未释放");
            visualLoadOwner = owner;
            visualLoadTarget = target;
            visualBlackScreen = null;
            visualBlackDebt = 0;
            preserveTargetSnapshot = preservePreviousTargetSnapshot;
        }

        /// <summary>只补偿本租约 SceneLoader 未配对的黑幕，保留其它调用者的引用计数。</summary>
        internal static UniTask EndLoadVisuals(object owner)
        {
            if (!ReferenceEquals(visualLoadOwner, owner)) return UniTask.CompletedTask;
            // 包括返航：官方任务已结束，却仍未激活的目标只能是异常遗留，必须放行 Unity 队列。
            // 所属 owner 保留到 operation 真正完成，不能随黑幕租约清空。
            if (HasPendingInitializationLoad(owner)) initializationOperation.allowSceneActivation = true;
            int debt = visualBlackDebt;
            BlackScreen screen = visualBlackScreen;
            visualLoadOwner = null;
            visualLoadTarget = null;
            visualBlackScreen = null;
            visualBlackDebt = 0;
            preserveTargetSnapshot = false;
            UniTask result = UniTask.CompletedTask;
            if (screen != null && BlackScreen.Instance == screen)
                while (debt-- > 0) result = BlackScreen.HideAndReturnTask();
            if (shutdownRequested) Shutdown();
            return result;
        }

        private static bool TracksLoadVisuals(SceneReference reference)
        {
            return visualLoadOwner != null && visualLoadTarget != null && reference != null
                && string.Equals(reference.Guid, visualLoadTarget.Guid, StringComparison.OrdinalIgnoreCase);
        }

        private static UniTask LoaderShowBlack(AnimationCurve curve, float circle, float duration, SceneReference reference)
        {
            if (TracksLoadVisuals(reference) && BlackScreen.Instance != null)
            {
                if (visualBlackScreen != BlackScreen.Instance) visualBlackDebt = 0;
                visualBlackScreen = BlackScreen.Instance;
                visualBlackDebt++;
            }
            return BlackScreen.ShowAndReturnTask(curve, circle, duration);
        }

        private static UniTask LoaderHideBlack(AnimationCurve curve, float circle, float duration, SceneReference reference)
        {
            if (TracksLoadVisuals(reference) && visualBlackDebt > 0 && BlackScreen.Instance == visualBlackScreen)
                visualBlackDebt--;
            return BlackScreen.HideAndReturnTask(curve, circle, duration);
        }

        private static bool SaveBeforeLoadPrefix(LevelManager __instance)
        {
            // 失败返航重试时，基地可能已激活却只恢复了半个角色；继续沿用离岛前的完整快照。
            // 若尚未离开岛图，仍允许原岛上角色走官方保存。
            if (__instance != null && preserveTargetSnapshot && visualLoadOwner != null && visualLoadTarget != null)
            {
                Scene target = visualLoadTarget.LoadedScene;
                if (target.IsValid() && __instance.gameObject.scene.handle == target.handle)
                {
                    CharacterMainControl returningMain = __instance.MainCharacter;
                    return returningMain != null && returningMain.Health != null && returningMain.Health.IsDead;
                }
            }
            // 初始化失败的角色尚未完成恢复，返航应继续读取出发前的官方快照。
            if (initializationFailure == null || initializationOwner == null || __instance == null ||
                __instance.gameObject.scene.handle != initializationSceneHandle || !IsScene(__instance.gameObject.scene)) return true;
            CharacterMainControl main = __instance.MainCharacter;
            return main != null && main.Health != null && main.Health.IsDead;
        }

        private static bool LoaderLevelInitialized(SceneReference reference)
        {
            if (OwnsReference(reference) && initializationOwner != null)
            {
                if (initializationFailure != null)
                    throw new OperationCanceledException(initializationFailure);
                if (initializationSceneHandle != 0)
                {
                    Scene scene = reference.LoadedScene;
                    if (!scene.IsValid() || !scene.isLoaded || scene.handle != initializationSceneHandle)
                        throw new OperationCanceledException("天空岛初始化所属场景已卸载");
                }
            }
            return LevelManager.LevelInited;
        }

        private static IEnumerable<CodeInstruction> LoaderInitializationTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo query = AccessTools.PropertyGetter(typeof(LevelManager), "LevelInited");
            MethodInfo replacement = RequireMethod(typeof(SkyIslandSceneReferenceBridge), "LoaderLevelInitialized", typeof(SceneReference));
            MethodInfo show = RequireMethod(typeof(BlackScreen), "ShowAndReturnTask", typeof(AnimationCurve), typeof(float), typeof(float));
            MethodInfo hide = RequireMethod(typeof(BlackScreen), "HideAndReturnTask", typeof(AnimationCurve), typeof(float), typeof(float));
            MethodInfo nextFrame = RequireMethod(typeof(UniTask), "NextFrame");
            MethodInfo cancellableFrame = RequireMethod(typeof(SkyIslandSceneReferenceBridge), "LoaderNextFrame", typeof(SceneReference), typeof(AsyncOperation));
            MethodInfo activation = AccessTools.PropertySetter(typeof(AsyncOperation), "allowSceneActivation");
            MethodInfo trackedActivation = RequireMethod(typeof(SkyIslandSceneReferenceBridge), "LoaderSetSceneActivation",
                typeof(AsyncOperation), typeof(bool), typeof(SceneReference));
            FieldInfo reference = RequireField(__originalMethod.DeclaringType, "sceneReference");
            FieldInfo operation = null;
            foreach (FieldInfo field in __originalMethod.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType != typeof(AsyncOperation)) continue;
                if (operation != null) throw new InvalidOperationException("官方场景异步加载字段不唯一");
                operation = field;
            }
            if (operation == null) throw new InvalidOperationException("官方场景异步加载字段缺失");
            int replaced = 0;
            int shows = 0, hides = 0, frames = 0;
            int activations = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(activation))
                {
                    CodeInstruction activationFirst = new CodeInstruction(instruction);
                    activationFirst.opcode = OpCodes.Ldarg_0;
                    activationFirst.operand = null;
                    yield return activationFirst;
                    yield return new CodeInstruction(OpCodes.Ldfld, reference);
                    yield return new CodeInstruction(OpCodes.Call, trackedActivation);
                    activations++;
                    continue;
                }
                if (instruction.Calls(nextFrame))
                {
                    CodeInstruction frameFirst = new CodeInstruction(instruction);
                    frameFirst.opcode = OpCodes.Ldarg_0;
                    frameFirst.operand = null;
                    yield return frameFirst;
                    yield return new CodeInstruction(OpCodes.Ldfld, reference);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, operation);
                    yield return new CodeInstruction(OpCodes.Call, cancellableFrame);
                    frames++;
                    continue;
                }
                if (instruction.Calls(show) || instruction.Calls(hide))
                {
                    bool showing = instruction.Calls(show);
                    MethodInfo visual = RequireMethod(typeof(SkyIslandSceneReferenceBridge), showing ? "LoaderShowBlack" : "LoaderHideBlack",
                        typeof(AnimationCurve), typeof(float), typeof(float), typeof(SceneReference));
                    CodeInstruction visualFirst = new CodeInstruction(instruction);
                    visualFirst.opcode = OpCodes.Ldarg_0;
                    visualFirst.operand = null;
                    yield return visualFirst;
                    yield return new CodeInstruction(OpCodes.Ldfld, reference);
                    yield return new CodeInstruction(OpCodes.Call, visual);
                    if (showing) shows++; else hides++;
                    continue;
                }
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
            if (shows != 2 || hides != 2) throw new InvalidOperationException("官方场景黑幕配对契约变化");
            if (frames != 4) throw new InvalidOperationException("官方场景分帧等待契约变化");
            if (activations != 2) throw new InvalidOperationException("官方场景激活赋值契约变化");
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
            Scene active = activeSubSceneRef(core);
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
            if (registered && (visualLoadOwner != null || HasPendingInitializationLoad(initializationOperationOwner)
                || SceneLoader.IsSceneLoading || SceneManager.GetSceneByPath(ScenePath).isLoaded)) return;
            CleanupRegistration();
        }

        private static void OnSceneLoadFinished(SceneLoadingContext context)
        {
            if (shutdownRequested) Shutdown();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            // 卸载后的句柄不能再代表天空岛（见 IsScene 的句柄缓存）。
            if (knownSceneHandle != 0 && scene.handle == knownSceneHandle) knownSceneHandle = 0;
            if (shutdownRequested) Shutdown();
        }

        private static void CleanupRegistration()
        {
            registered = false;
            shutdownRequested = false;
            knownSceneHandle = 0;
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
            initializationOperation = null;
            initializationOperationOwner = null;
            visualLoadOwner = null;
            visualLoadTarget = null;
            visualBlackScreen = null;
            visualBlackDebt = 0;
            preserveTargetSnapshot = false;
        }
    }
}
