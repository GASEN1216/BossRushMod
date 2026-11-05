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
    /// <summary>一张 Mod 独立出击地图的官方身份：sceneID、包内唯一 Scene 路径与 Eflatun GUID。</summary>
    internal sealed class ModRaidSceneIdentity
    {
        internal readonly string SceneId, ScenePath, SceneName, SceneGuid, DisplayNameKey, LogTag;
        private readonly Action injectLocalization;
        // 以下运行时状态只由 ModRaidSceneBridge 读写。
        internal SceneReference Reference;
        internal SceneInfoEntry Info;
        internal bool Registered, AddedGuid, AddedPath, ShutdownRequested;
        /// <summary>最近一次认出的本图场景句柄；0 表示未知。</summary>
        internal int KnownSceneHandle;

        /// <param name="injectLocalization">把 <paramref name="displayNameKey"/> 注入当前语言的地图名；宿主切语言时客户端自己再调一次。</param>
        internal ModRaidSceneIdentity(string sceneId, string scenePath, string sceneGuid, string displayNameKey, Action injectLocalization, string logTag)
        {
            if (string.IsNullOrEmpty(sceneId) || string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(sceneGuid))
                throw new ArgumentException("独立场景身份不完整");
            SceneId = sceneId;
            ScenePath = scenePath;
            SceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            SceneGuid = sceneGuid;
            DisplayNameKey = displayNameKey;
            this.injectLocalization = injectLocalization;
            LogTag = logTag ?? "[RaidScene]";
        }

        internal void InjectLocalization()
        {
            if (injectLocalization != null) injectLocalization();
        }
    }

    /// <summary>
    /// WIRE+：把 bundle 场景接入官方按名称加载的 SceneLoader，多张 Mod 地图共用一份补丁（2026-11-05 由天空岛桥抽出）。
    /// BuildIndex 始终保留 Unity 返回的 -1；不向全局 Entries 注入会匹配所有 -1 的条目，每个前缀只接管已登记身份自己的
    /// GUID / sceneID / 场景实例，其它地图原样放行。单 Scene 同时承载 MultiSceneCore 与世界，官方 LoadAndTeleport 仍走原实现。
    /// 官方加载状态机上的转译补丁只能装一次（第二份会找不到已被替换的 LevelInited 调用），所以客户端只登记身份、不得自己再装。
    /// 同一时刻只有一段 Mod 出击加载在跑，初始化令牌与黑幕租约因此是全局单份，附带所属身份。
    /// </summary>
    internal static class ModRaidSceneBridge
    {
        private const string HarmonyId = "com.bossrush.raidscenes.scene";
        private static readonly List<ModRaidSceneIdentity> identities = new List<ModRaidSceneIdentity>();
        private static bool patched, subscribed;
        private static IDictionary<string, string> guidMap, pathMap;
        private static Harmony harmony;
        private static FieldInfo activeSubSceneField, cachedEntryField, loadingField, activeObjectsField, loadedEventField, displayNameField;
        private static AccessTools.FieldRef<MultiSceneCore, Scene> activeSubSceneRef;
        private static MethodInfo localLoadedMethod;
        private static object initializationOwner;
        private static ModRaidSceneIdentity initializationIdentity;
        private static int initializationSceneHandle;
        private static string initializationFailure;
        private static AsyncOperation initializationOperation;
        private static object initializationOperationOwner;
        private static object visualLoadOwner;
        private static SceneReference visualLoadTarget;
        private static BlackScreen visualBlackScreen;
        private static int visualBlackDebt;
        private static bool preserveTargetSnapshot;

        internal static SceneReference ReferenceOf(ModRaidSceneIdentity identity)
        {
            if (identity == null || !identity.Registered) return null;
            return identity.Reference;
        }

        /// <summary>
        /// 这个场景是不是该身份的图。**热路径**：经 <see cref="Find(Scene)"/> 挂在 `MultiSceneCore.ActiveSubSceneID` 等全局前缀补丁上，
        /// 官方地图、迷雾与各类 HUD 在任何地图都会每帧问；而 `Scene.path` 每读一次都新建一个托管字符串。
        /// 两级短路，先比整数：官方场景全在 Build Settings 里（buildIndex ≥ 0），Mod 地图来自 AssetBundle（恒为 -1），
        /// 官方地图因此一个字符串都不读；认出过一次就记住句柄，之后只比句柄。
        /// 句柄缓存在新的初始化开始时（<see cref="BeginInitialization"/>）、场景卸载与注销时作废。
        /// </summary>
        internal static bool IsScene(ModRaidSceneIdentity identity, Scene scene)
        {
            if (identity == null || !scene.IsValid() || scene.buildIndex >= 0) return false;
            if (identity.KnownSceneHandle != 0 && scene.handle == identity.KnownSceneHandle) return true;
            if (!string.Equals(scene.path, identity.ScenePath, StringComparison.OrdinalIgnoreCase)) return false;
            identity.KnownSceneHandle = scene.handle;
            return true;
        }

        /// <summary>场景属于哪一个已登记身份；官方场景与未登记的 bundle 场景返回 null。</summary>
        private static ModRaidSceneIdentity Find(Scene scene)
        {
            if (!scene.IsValid() || scene.buildIndex >= 0) return null;
            for (int i = 0; i < identities.Count; i++)
            {
                ModRaidSceneIdentity identity = identities[i];
                if (identity.KnownSceneHandle != 0 && scene.handle == identity.KnownSceneHandle) return identity;
            }
            for (int i = 0; i < identities.Count; i++)
                if (IsScene(identities[i], scene)) return identities[i];
            return null;
        }

        private static ModRaidSceneIdentity FindReference(SceneReference reference)
        {
            if (reference == null || identities.Count == 0) return null;
            string guid = reference.Guid;
            for (int i = 0; i < identities.Count; i++)
                if (string.Equals(guid, identities[i].SceneGuid, StringComparison.OrdinalIgnoreCase)) return identities[i];
            return null;
        }

        private static ModRaidSceneIdentity FindSceneId(string sceneID)
        {
            for (int i = 0; i < identities.Count; i++)
                if (string.Equals(sceneID, identities[i].SceneId, StringComparison.Ordinal)) return identities[i];
            return null;
        }

        /// <summary>当前活动场景是哪张已登记的 Mod 地图；不在 Mod 地图上返回 null。</summary>
        internal static ModRaidSceneIdentity ActiveIdentity
        {
            get { return identities.Count == 0 ? null : Find(SceneManager.GetActiveScene()); }
        }

        internal static bool Register(ModRaidSceneIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException("identity");
            if (identity.Registered) { identity.ShutdownRequested = false; return true; }
            try
            {
                EnsurePatched();
                foreach (ModRaidSceneIdentity other in identities)
                    if (string.Equals(other.SceneId, identity.SceneId, StringComparison.Ordinal) ||
                        string.Equals(other.SceneGuid, identity.SceneGuid, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(other.ScenePath, identity.ScenePath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("独立场景身份与 " + other.SceneId + " 冲突");
                string current;
                if (guidMap.TryGetValue(identity.SceneGuid, out current) && !string.Equals(current, identity.ScenePath, StringComparison.Ordinal))
                    throw new InvalidOperationException("场景 GUID 已被其它路径占用：" + identity.SceneId);
                if (pathMap.TryGetValue(identity.ScenePath, out current) && !string.Equals(current, identity.SceneGuid, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("场景路径已被其它 GUID 占用：" + identity.SceneId);
                if (!guidMap.ContainsKey(identity.SceneGuid)) { guidMap.Add(identity.SceneGuid, identity.ScenePath); identity.AddedGuid = true; }
                if (!pathMap.ContainsKey(identity.ScenePath)) { pathMap.Add(identity.ScenePath, identity.SceneGuid); identity.AddedPath = true; }
                identity.Reference = new SceneReference(identity.SceneGuid);
                identity.Info = new SceneInfoEntry(identity.SceneId, identity.Reference);
                displayNameField.SetValue(identity.Info, identity.DisplayNameKey);
                identity.InjectLocalization();
                identity.KnownSceneHandle = 0;
                identity.ShutdownRequested = false;
                identity.Registered = true;
                identities.Add(identity);
                Debug.Log(identity.LogTag + " 官方 SceneReference 桥接已就绪，BuildIndex 保留 -1，目标=" + identity.ScenePath);
                return true;
            }
            catch (Exception e)
            {
                RemoveIdentity(identity);
                if (identities.Count == 0) CleanupPatches();
                ModBehaviour.CriticalLog("raid-scene-bridge-" + identity.SceneId, identity.LogTag + " 官方场景桥接失败: " + e.Message);
                return false;
            }
        }

        private static void EnsurePatched()
        {
            if (patched) return;
            guidMap = SceneGuidToPathMapProvider.SceneGuidToPathMap as IDictionary<string, string>;
            pathMap = SceneGuidToPathMapProvider.ScenePathToGuidMap as IDictionary<string, string>;
            if (guidMap == null || pathMap == null) throw new InvalidOperationException("Eflatun 场景路径表不可扩展");
            displayNameField = RequireField(typeof(SceneInfoEntry), "displayName");
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
            try
            {
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
                harmony.Patch(moveNext, transpiler: new HarmonyMethod(RequireMethod(typeof(ModRaidSceneBridge),
                    "LoaderInitializationTranspiler", typeof(IEnumerable<CodeInstruction>), typeof(MethodBase))));
            }
            catch
            {
                try { harmony.UnpatchAll(HarmonyId); }
                catch (Exception e) { Debug.LogWarning("[RaidScene] 补丁回滚失败: " + e.Message); }
                harmony = null;
                throw;
            }
            if (!subscribed)
            {
                SceneLoader.onFinishedLoadingScene += OnSceneLoadFinished;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                subscribed = true;
            }
            patched = true;
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
            if (target == null) throw new MissingMethodException("独立场景桥接目标不存在: " + prefix);
            MethodInfo prefixMethod = AccessTools.Method(typeof(ModRaidSceneBridge), prefix);
            if (prefixMethod == null) throw new MissingMethodException(typeof(ModRaidSceneBridge).FullName, prefix);
            harmony.Patch(target, prefix: new HarmonyMethod(prefixMethod));
        }

        private static bool OwnsCore(MultiSceneCore core, out ModRaidSceneIdentity identity)
        {
            identity = core == null || identities.Count == 0 ? null : Find(core.gameObject.scene);
            return identity != null;
        }

        internal static void BeginInitialization(object owner, ModRaidSceneIdentity identity)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (identity == null) throw new ArgumentNullException("identity");
            if (initializationOwner != null && !ReferenceEquals(initializationOwner, owner))
                throw new InvalidOperationException("上一段独立地图初始化尚未释放");
            initializationOwner = owner;
            initializationIdentity = identity;
            initializationSceneHandle = 0;
            initializationFailure = null;
            initializationOperation = null;
            initializationOperationOwner = null;
            // 新的一趟出击会加载一个新的场景实例：旧句柄即使被复用也不能再当作本图。
            identity.KnownSceneHandle = 0;
        }

        internal static void BindInitializationScene(object owner, Scene scene)
        {
            if (ReferenceEquals(initializationOwner, owner) && IsScene(initializationIdentity, scene)) initializationSceneHandle = scene.handle;
        }

        internal static void AbortInitialization(object owner, string reason)
        {
            if (!ReferenceEquals(initializationOwner, owner)) return;
            if (initializationFailure == null) initializationFailure = reason ?? "独立地图初始化已取消";
            // 官方事件 / 黑幕 await 也会抛出异常，此时已没有下一帧回调替我们解除激活阻塞。
            if (initializationOperation != null && !initializationOperation.isDone)
                initializationOperation.allowSceneActivation = true;
        }

        internal static void EndInitialization(object owner)
        {
            if (!ReferenceEquals(initializationOwner, owner)) return;
            initializationOwner = null;
            initializationIdentity = null;
            initializationSceneHandle = 0;
            initializationFailure = null;
            initializationOperation = null;
            initializationOperationOwner = null;
        }

        internal static bool HasPendingInitializationLoad(object owner)
        {
            return ReferenceEquals(initializationOperationOwner, owner) && initializationOperation != null && !initializationOperation.isDone;
        }

        /// <summary>这次官方加载的目标是不是正在初始化的那张 Mod 地图。</summary>
        private static bool InitializingReference(SceneReference reference)
        {
            return initializationOwner != null && initializationIdentity != null && FindReference(reference) == initializationIdentity;
        }

        private static UniTask LoaderNextFrame(SceneReference reference, AsyncOperation operation)
        {
            if (InitializingReference(reference))
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
            if (TracksLoadVisuals(reference) || InitializingReference(reference))
            {
                initializationOperation = operation;
                initializationOperationOwner = TracksLoadVisuals(reference) ? visualLoadOwner : initializationOwner;
            }
            operation.allowSceneActivation = allow;
        }

        internal static void BeginLoadVisuals(object owner, SceneReference target, bool preservePreviousTargetSnapshot = false)
        {
            if (visualLoadOwner != null) throw new InvalidOperationException("上一段独立地图切图表现尚未释放");
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
            RetryPendingShutdowns();
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
            // 失败返航重试时，基地可能已激活却只恢复了半个角色；继续沿用离开 Mod 地图前的完整快照。
            // 若尚未离开 Mod 地图，仍允许原图上的角色走官方保存。
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
                __instance.gameObject.scene.handle != initializationSceneHandle || !IsScene(initializationIdentity, __instance.gameObject.scene)) return true;
            CharacterMainControl main = __instance.MainCharacter;
            return main != null && main.Health != null && main.Health.IsDead;
        }

        private static bool LoaderLevelInitialized(SceneReference reference)
        {
            if (InitializingReference(reference))
            {
                if (initializationFailure != null)
                    throw new OperationCanceledException(initializationFailure);
                if (initializationSceneHandle != 0)
                {
                    Scene scene = reference.LoadedScene;
                    if (!scene.IsValid() || !scene.isLoaded || scene.handle != initializationSceneHandle)
                        throw new OperationCanceledException("独立地图初始化所属场景已卸载");
                }
            }
            return LevelManager.LevelInited;
        }

        private static IEnumerable<CodeInstruction> LoaderInitializationTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo query = AccessTools.PropertyGetter(typeof(LevelManager), "LevelInited");
            MethodInfo replacement = RequireMethod(typeof(ModRaidSceneBridge), "LoaderLevelInitialized", typeof(SceneReference));
            MethodInfo show = RequireMethod(typeof(BlackScreen), "ShowAndReturnTask", typeof(AnimationCurve), typeof(float), typeof(float));
            MethodInfo hide = RequireMethod(typeof(BlackScreen), "HideAndReturnTask", typeof(AnimationCurve), typeof(float), typeof(float));
            MethodInfo nextFrame = RequireMethod(typeof(UniTask), "NextFrame");
            MethodInfo cancellableFrame = RequireMethod(typeof(ModRaidSceneBridge), "LoaderNextFrame", typeof(SceneReference), typeof(AsyncOperation));
            MethodInfo activation = AccessTools.PropertySetter(typeof(AsyncOperation), "allowSceneActivation");
            MethodInfo trackedActivation = RequireMethod(typeof(ModRaidSceneBridge), "LoaderSetSceneActivation",
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
                    MethodInfo visual = RequireMethod(typeof(ModRaidSceneBridge), showing ? "LoaderShowBlack" : "LoaderHideBlack",
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
            if (FindReference(__instance) == null) return true;
            __result = SceneReferenceUnsafeReason.None;
            return false;
        }

        private static bool SceneInfoPrefix(string sceneID, ref SceneInfoEntry __result)
        {
            ModRaidSceneIdentity identity = FindSceneId(sceneID);
            if (identity == null) return true;
            __result = identity.Info;
            return false;
        }

        private static bool SceneIdPrefix(SceneReference sceneRef, ref string __result)
        {
            ModRaidSceneIdentity identity = FindReference(sceneRef);
            if (identity == null) return true;
            __result = identity.SceneId;
            return false;
        }

        /// <summary>
        /// 官方小地图用 `GetSceneID(GetActiveScene().buildIndex)` 反查当前地图身份
        /// （`MiniMapView.CeneterPlayer` 与无参的 `TryConvertWorldToMinimapPosition`）。
        /// bundle 场景的 buildIndex 恒为 -1，而 Mod 地图刻意没有注入全局 entries，原实现只会返回 null。
        ///
        /// 判定条件是「**当前活动场景**就是某张已登记的 Mod 地图」，不是「buildIndex == -1」——
        /// 后者会把所有自定义 bundle 场景一并匹配掉，正是这个桥一开始要避免的事。
        /// </summary>
        private static bool SceneIdByBuildIndexPrefix(int buildIndex, ref string __result)
        {
            if (identities.Count == 0 || buildIndex >= 0) return true;
            ModRaidSceneIdentity identity = ActiveIdentity;
            if (identity == null) return true;
            __result = identity.SceneId;
            return false;
        }

        private static bool CoreInfoPrefix(MultiSceneCore __instance, ref SceneInfoEntry __result)
        {
            ModRaidSceneIdentity identity;
            if (!OwnsCore(__instance, out identity)) return true;
            __result = identity.Info;
            return false;
        }

        private static bool CoreNamePrefix(MultiSceneCore __instance, ref string __result)
        {
            ModRaidSceneIdentity identity;
            if (!OwnsCore(__instance, out identity)) return true;
            __result = identity.Info.DisplayName;
            return false;
        }

        private static bool CoreRawNamePrefix(MultiSceneCore __instance, ref string __result)
        {
            ModRaidSceneIdentity identity;
            if (!OwnsCore(__instance, out identity)) return true;
            __result = identity.Info.DisplayNameRaw;
            return false;
        }

        private static bool CoreMainIdPrefix(ref string __result)
        {
            ModRaidSceneIdentity identity;
            if (!OwnsCore(MultiSceneCore.Instance, out identity)) return true;
            __result = identity.SceneId;
            return false;
        }

        private static bool CoreActiveIdPrefix(ref string __result)
        {
            MultiSceneCore core = MultiSceneCore.Instance;
            ModRaidSceneIdentity identity;
            if (!OwnsCore(core, out identity)) return true;
            Scene active = activeSubSceneRef(core);
            __result = !core.IsLoading && IsScene(identity, active) && active.isLoaded ? identity.SceneId : null;
            return false;
        }

        private static bool LoadSelfPrefix(MultiSceneCore __instance, SceneReference targetScene, ref UniTask<bool> __result)
        {
            ModRaidSceneIdentity identity;
            if (!OwnsCore(__instance, out identity) || FindReference(targetScene) != identity) return true;
            __result = ActivateSelf(__instance, identity);
            return false;
        }

        private static bool IsActiveScene(ModRaidSceneIdentity identity)
        {
            return IsScene(identity, SceneManager.GetActiveScene());
        }

        private static async UniTask<bool> ActivateSelf(MultiSceneCore core, ModRaidSceneIdentity identity)
        {
            Scene scene = core.gameObject.scene;
            if (!scene.isLoaded || !IsActiveScene(identity) || SceneManager.GetActiveScene().handle != scene.handle ||
                SceneLoader.IsSceneLoading || core.IsLoading) return false;
            Scene previous = (Scene)activeSubSceneField.GetValue(core);
            if (previous.IsValid() && previous.handle == scene.handle && cachedEntryField.GetValue(core) != null) return true;
            SubSceneEntry entry = core.SubScenes == null ? null : core.SubScenes.Find(value => value != null && value.sceneID == identity.SceneId);
            if (entry == null) throw new InvalidOperationException(identity.SceneId + " 的 MultiSceneCore 未配置自身 SubSceneEntry");
            if (FindProvider(identity, scene) == null) throw new InvalidOperationException(identity.SceneId + " 缺少真实 SceneLocationsProvider");
            object previousEntry = cachedEntryField.GetValue(core);
            bool completed = false;
            loadingField.SetValue(core, true);
            try
            {
                activeSubSceneField.SetValue(core, scene);
                cachedEntryField.SetValue(core, entry);
                localLoadedMethod.Invoke(core, new object[] { scene });
                MultiSceneCore.SetVisited(identity.SceneId);
                await UniTask.NextFrame();
                if (core == null || !identity.Registered || !IsActiveScene(identity) ||
                    SceneManager.GetActiveScene().handle != scene.handle || SceneLoader.IsSceneLoading)
                    return false;
                // 对应官方 LoadSubScene 的完成顺序：先解除 loading，之后才广播，墓碑和场景服务由官方订阅者恢复。
                loadingField.SetValue(core, false);
                var callback = loadedEventField.GetValue(null) as Action<MultiSceneCore, Scene>;
                if (callback != null)
                {
                    foreach (Action<MultiSceneCore, Scene> listener in callback.GetInvocationList())
                    {
                        try { listener(core, scene); }
                        catch (Exception e) { Debug.LogWarning(identity.LogTag + " OnSubSceneLoaded 接收者异常: " + e.Message); }
                    }
                }
                completed = true;
                Debug.Log(identity.LogTag + " 官方单场景子区就绪: " + scene.path);
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
            ModRaidSceneIdentity identity;
            if (!OwnsCore(__instance, out identity) || !IsActiveScene(identity) ||
                SceneManager.GetActiveScene().handle != __instance.gameObject.scene.handle ||
                sceneBuildIndex != __instance.gameObject.scene.buildIndex) return true;
            var objects = activeObjectsField.GetValue(__instance) as Dictionary<int, GameObject>;
            if (objects == null) throw new InvalidOperationException(identity.SceneId + " 的官方 scene owner 表不存在");
            GameObject parent;
            if (!objects.TryGetValue(sceneBuildIndex, out parent) || parent == null)
            {
                parent = new GameObject(identity.SceneId);
                parent.transform.SetParent(__instance.transform, false);
                objects[sceneBuildIndex] = parent;
            }
            parent.SetActive(true);
            __result = parent.transform;
            return false;
        }

        private static SceneLocationsProvider FindProvider(ModRaidSceneIdentity identity, Scene scene)
        {
            if (!IsScene(identity, scene) || !scene.isLoaded) return null;
            foreach (SceneLocationsProvider provider in SceneLocationsProvider.ActiveProviders)
                if (provider != null && provider.gameObject.scene.handle == scene.handle && IsScene(identity, provider.gameObject.scene)) return provider;
            return null;
        }

        private static bool ProviderReferencePrefix(SceneReference sceneReference, ref SceneLocationsProvider __result)
        {
            ModRaidSceneIdentity identity = FindReference(sceneReference);
            if (identity == null) return true;
            __result = FindProvider(identity, sceneReference.LoadedScene);
            return false;
        }

        private static bool ProviderScenePrefix(Scene scene, ref SceneLocationsProvider __result)
        {
            ModRaidSceneIdentity identity = identities.Count == 0 ? null : Find(scene);
            if (identity == null) return true;
            __result = FindProvider(identity, scene);
            return false;
        }

        private static bool LocationReferencePrefix(SceneReference scene, string name, ref Transform __result)
        {
            ModRaidSceneIdentity identity = FindReference(scene);
            if (identity == null) return true;
            SceneLocationsProvider provider = FindProvider(identity, scene.LoadedScene);
            __result = provider == null ? null : provider.GetLocation(name);
            return false;
        }

        private static bool LocationIdPrefix(string sceneID, string name, ref Transform __result)
        {
            ModRaidSceneIdentity identity = FindSceneId(sceneID);
            if (identity == null) return true;
            SceneLocationsProvider provider = FindProvider(identity, identity.Reference.LoadedScene);
            __result = provider == null ? null : provider.GetLocation(name);
            return false;
        }

        /// <summary>
        /// 注销一张图。官方 async 状态机仍会读取 Name；未退出目标 Scene 前不能移除它正在使用的路径和前缀，
        /// 推迟到切图完成或场景卸载时再试。最后一张图注销后才撤掉全部补丁。
        /// </summary>
        internal static void Unregister(ModRaidSceneIdentity identity)
        {
            if (identity == null || !identity.Registered) return;
            identity.ShutdownRequested = true;
            if (visualLoadOwner != null || HasPendingInitializationLoad(initializationOperationOwner)
                || SceneLoader.IsSceneLoading || SceneManager.GetSceneByPath(identity.ScenePath).isLoaded) return;
            RemoveIdentity(identity);
            if (identities.Count == 0) CleanupPatches();
        }

        private static void RetryPendingShutdowns()
        {
            for (int i = identities.Count - 1; i >= 0; i--)
                if (i < identities.Count && identities[i].ShutdownRequested) Unregister(identities[i]);
        }

        private static void OnSceneLoadFinished(SceneLoadingContext context)
        {
            RetryPendingShutdowns();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            // 卸载后的句柄不能再代表任何一张 Mod 地图（见 IsScene 的句柄缓存）。
            for (int i = 0; i < identities.Count; i++)
                if (identities[i].KnownSceneHandle != 0 && scene.handle == identities[i].KnownSceneHandle) identities[i].KnownSceneHandle = 0;
            RetryPendingShutdowns();
        }

        private static void RemoveIdentity(ModRaidSceneIdentity identity)
        {
            identities.Remove(identity);
            string current;
            if (identity.AddedGuid && guidMap != null && guidMap.TryGetValue(identity.SceneGuid, out current) && current == identity.ScenePath)
                guidMap.Remove(identity.SceneGuid);
            if (identity.AddedPath && pathMap != null && pathMap.TryGetValue(identity.ScenePath, out current) && current == identity.SceneGuid)
                pathMap.Remove(identity.ScenePath);
            identity.AddedGuid = identity.AddedPath = false;
            identity.Registered = false;
            identity.ShutdownRequested = false;
            identity.KnownSceneHandle = 0;
            identity.Reference = null;
            identity.Info = null;
            if (ReferenceEquals(initializationIdentity, identity))
            {
                initializationOwner = null;
                initializationIdentity = null;
                initializationSceneHandle = 0;
                initializationFailure = null;
                initializationOperation = null;
                initializationOperationOwner = null;
            }
        }

        private static void CleanupPatches()
        {
            patched = false;
            if (subscribed)
            {
                SceneLoader.onFinishedLoadingScene -= OnSceneLoadFinished;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }
            if (harmony != null)
            {
                try { harmony.UnpatchAll(HarmonyId); }
                catch (Exception e) { Debug.LogWarning("[RaidScene] SceneReference 补丁退订失败: " + e.Message); }
                harmony = null;
            }
            guidMap = pathMap = null;
            initializationOwner = null;
            initializationIdentity = null;
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
