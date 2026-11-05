using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using Eflatun.SceneReference;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>一张 Mod 独立出击地图的资源与装配约定，交给 <see cref="ModRaidLease"/>。</summary>
    internal sealed class ModRaidProfile
    {
        internal ModRaidSceneIdentity Identity;
        /// <summary>相对 Mod 目录的场景包路径，例如 `Assets/arenas/tidegate_raid`。</summary>
        internal string BundleRelativePath;
        /// <summary>包内服务根（LevelConfig / MultiSceneCore / SceneLocationsProvider，必须未激活）与世界根的名字。</summary>
        internal string ServicesRootName, WorldRootName;
        internal string SpawnLocation = "StartPoints/PlayerSpawn";
        /// <summary>世界根下必须存在的节点（相对路径），激活前合同逐个检查。</summary>
        internal string[] RequiredWorldPaths = new string[0];
        /// <summary>世界根下与官方出生点同位置的标记（相对路径）。</summary>
        internal string SpawnMarkerPath = "PlayerSpawn";
        /// <summary>克隆的基地天气副本在注入前的调整（例如把室内 Volume 换成户外的）。</summary>
        internal Action<TimeOfDayConfig> AdjustTimeOfDay;
        /// <summary>场景加载 / 卸载时的附加钩子（只认本图场景），可空。</summary>
        internal Action<Scene> SceneLoaded, SceneUnloaded;
        /// <summary>玩家可见的地图名（失败提示用），取用时解析语言。</summary>
        internal Func<string> DisplayName;
    }

    /// <summary>
    /// 独立出击地图资源租约（2026-11-05 照天空岛租约抽成通用版，潮闸镇先用；天空岛仍走自己的 `SkyIslandRaidLease`）。
    /// 官方 SceneLoader 独占关卡转换，租约只预载包、在激活前补齐官方配置并验合同、等待 Scene 真正卸载后释放。
    /// 进图 / 撤离 / 返航的顺序与原版一致，理由见 `ArtSource/SkyIsland/OFFICIAL_SCENE_CONTRACT.md`。
    /// </summary>
    internal sealed class ModRaidLease
    {
        private readonly ModRaidProfile profile;
        private AssetBundle bundle;
        private bool releaseRequested, subscribed, loading, returning, initializing, returnFailed;
        private TimeOfDayConfig timeOfDay;
        private ModRaidRecovery recovery;
        private float retryAt;
        private Action completed;
        private string failureNotice;
        private System.Collections.IEnumerator preparation;
        internal bool LoadFinished { get; private set; }
        internal string Error { get; private set; }
        internal bool IsReturning { get { return returning; } }
        internal ModRaidProfile Profile { get { return profile; } }

        internal ModRaidLease(ModRaidProfile profile)
        {
            if (profile == null || profile.Identity == null || string.IsNullOrEmpty(profile.BundleRelativePath)
                || string.IsNullOrEmpty(profile.ServicesRootName) || string.IsNullOrEmpty(profile.WorldRootName))
                throw new ArgumentException("独立地图租约配置不完整");
            this.profile = profile;
        }

        private string Tag { get { return profile.Identity.LogTag; } }

        internal void SetFailureNotice(string message)
        {
            if (failureNotice == null) failureNotice = message;
        }

        /// <summary>官方在激活场景前等待玩家点击继续；这段人为等待不消耗加载超时预算。</summary>
        internal bool HasSceneLoadTimeRemaining(ref float deadline)
        {
            float now = Time.realtimeSinceStartup;
            if (loading && !releaseRequested && Error == null && SceneLoader.IsSceneLoading &&
                string.Equals(SceneLoader.LoadingComment, "Wait for click...", StringComparison.Ordinal))
                deadline = now + 120f;
            return now < deadline;
        }

        internal static bool IsBundleDeployed(ModRaidProfile profile)
        {
            try { return File.Exists(Path.Combine(ModBehaviour.GetModPath(), profile.BundleRelativePath)); }
            catch (Exception) { return false; }
        }

        internal void BeginPrepare(string modDirectory, TimeOfDayConfig template)
        {
            if (preparation != null) throw new InvalidOperationException("Raid preparation already started");
            int scene = SceneManager.GetActiveScene().handle;
            preparation = ResourceBundleLoader.Prepare(Path.Combine(modDirectory, profile.BundleRelativePath), false,
                () => releaseRequested || SceneManager.GetActiveScene().handle != scene,
                () => Prepare(modDirectory, template), 120f);
        }

        internal bool StepPreparation()
        {
            if (preparation == null) return false;
            if (preparation.MoveNext()) return true;
            (preparation as IDisposable)?.Dispose();
            preparation = null;
            if (bundle == null) throw new InvalidOperationException(profile.Identity.SceneId + " bundle preparation cancelled or failed");
            return false;
        }

        internal void Prepare(string modDirectory, TimeOfDayConfig template)
        {
            if (bundle != null || loading || releaseRequested) throw new InvalidOperationException("独立场景租约不可重复使用");
            if (template == null) throw new InvalidOperationException("官方天气配置缺失");
            // 官方 TimeOfDayConfig 与它的 TimeOfDayEntry 都是场景 MonoBehaviour：基地一卸载就被销毁。
            // 趁人还在基地克隆整棵子树并转 DontDestroyOnLoad，副本在 TryRelease 里销毁（CR-2026-09-10-003）。
            timeOfDay = UnityEngine.Object.Instantiate(template);
            timeOfDay.gameObject.name = "BossRush_" + profile.Identity.SceneName + "_TimeOfDay";
            UnityEngine.Object.DontDestroyOnLoad(timeOfDay.gameObject);
            if (profile.AdjustTimeOfDay != null) profile.AdjustTimeOfDay(timeOfDay);
            if (!ModRaidSceneBridge.Register(profile.Identity))
                throw new InvalidOperationException("官方场景引用桥接尚未就绪：" + profile.Identity.SceneId);
            string path = Path.Combine(modDirectory, profile.BundleRelativePath);
            if (!File.Exists(path)) throw new FileNotFoundException("缺少独立出击场景包，请更新 Mod 资源", path);
            bundle = ResourceBundleLoader.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException("独立场景包无法读取：" + profile.Identity.SceneId);
            string[] scenes = bundle.GetAllScenePaths();
            if (scenes.Length != 1 || !string.Equals(scenes[0], profile.Identity.ScenePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("独立场景包版本与程序不匹配：" + profile.Identity.SceneId);
            if (!subscribed)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                subscribed = true;
            }
            // 初始化令牌归租约：它的生命周期覆盖加载、返航与释放，宿主销毁后仍由 recovery 续命。
            ModRaidSceneBridge.BeginInitialization(this, profile.Identity);
            initializing = true;
        }

        /// <summary>官方合同通过后调用：解除失败信号，之后的官方等待恢复原生语义。</summary>
        internal void MarkInitialized()
        {
            if (!initializing) return;
            initializing = false;
            ModRaidSceneBridge.EndInitialization(this);
        }

        /// <summary>会话超时或取消时调用：给官方加载等待一个可观察的取消信号，避免永久停在加载界面。</summary>
        internal void Abort(string reason)
        {
            if (!initializing) return;
            ModRaidSceneBridge.AbortInitialization(this, reason);
            if (Error == null) Error = reason;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsRaidScene(scene)) return;
            if (profile.SceneLoaded != null)
            {
                try { profile.SceneLoaded(scene); }
                catch (Exception e) { Debug.LogWarning(Tag + " 场景加载钩子失败: " + e.Message); }
            }
            try
            {
                ModRaidSceneBridge.BindInitializationScene(this, scene);
                GameObject services = null, world = null;
                foreach (GameObject candidate in scene.GetRootGameObjects())
                {
                    if (candidate.name == profile.ServicesRootName) services = candidate;
                    else if (candidate.name == profile.WorldRootName) world = candidate;
                }
                if (services == null || world == null) throw new InvalidOperationException("独立场景缺少地形或关卡根节点");
                // 注入必须排在合同验证之前：timeOfDayConfig 是场景组件、startBuffPrefabs 连字段都不进包，
                // 只能由租约补齐；反过来排序合同的两条 null 判据就当场判死（CR-2026-09-10-002）。
                LevelConfig config = services.GetComponent<LevelConfig>();
                if (config == null || services.activeSelf) throw new InvalidOperationException("独立关卡必须在配置完成后激活");
                config.timeOfDayConfig = timeOfDay;
                config.startBuffPrefabs = new List<Duckov.Buffs.Buff>();
                // 官方服务一旦激活就会开始重建主角；最小装配的完整性必须在激活之前定论。
                string contractError;
                if (!ModRaidOfficialContract.VerifyBeforeActivation(profile, scene, services, world, out contractError))
                    throw new InvalidOperationException(contractError);
                if (global::AstarPath.active == null) services.AddComponent<global::AstarPath>();
                // 这个最小官方服务装配不能依赖已被销毁的会话，否则加载取消后永远无法完成官方初始化。
                if (releaseRequested) world.SetActive(true);
                services.SetActive(true);
            }
            catch (Exception e)
            {
                Error = e.Message;
                ModRaidSceneBridge.AbortInitialization(this, e.Message);
                Debug.LogError(Tag + " RAID_ASSEMBLY_FAILED " + e);
            }
        }

        internal async void BeginLoad()
        {
            loading = true;
            try
            {
                ModRaidSceneBridge.BeginLoadVisuals(this, ModRaidSceneBridge.ReferenceOf(profile.Identity));
                // clickToConinue: true 与官方出击一致（`MapSelectionView.LoadTask`）：读条结束后停在「点击继续」。
                await SceneLoader.Instance.LoadScene(profile.Identity.SceneId,
                    new MultiSceneLocation { SceneID = profile.Identity.SceneId, LocationName = profile.SpawnLocation },
                    clickToConinue: true, notifyEvacuation: false, saveToFile: true);
            }
            catch (Exception e) { Abort(e.Message); Error = e.Message; Debug.LogError(Tag + " RAID_LOAD_FAILED " + e); ReleaseStuckOfficialLoadingFlag(Tag, "load"); }
            finally
            {
                try { await ModRaidSceneBridge.EndLoadVisuals(this); }
                catch (Exception e) { Debug.LogWarning(Tag + " 入图黑幕清理失败: " + e.Message); }
                loading = false;
                LoadFinished = true;
                TryRelease();
            }
        }

        internal async void ReturnToBase(bool evacuated)
        {
            if (returning || loading || SceneLoader.IsSceneLoading || Time.unscaledTime < retryAt) return;
            if (ModRaidSceneBridge.HasPendingInitializationLoad(this)) return;
            returning = true;
            bool baseReturned = false;
            try
            {
                // 撤离照抄官方 `SceneLoaderProxy.Task`：NotifyEvacuated -> ClosureView(1s) -> 幕布换成 EvacuateScreenScene -> LoadScene。
                // 官方 LoadScene 内部还会再 notify 一次（notifyEvacuation: true），这是原版既有行为，如实照搬。
                SceneReference curtain = null;
                if (evacuated)
                {
                    try
                    {
                        if (LevelManager.Instance != null)
                            LevelManager.Instance.NotifyEvacuated(
                                new EvacuationInfo(MultiSceneCore.ActiveSubSceneID, Vector3.zero));
                        await ClosureView.ShowAndReturnTask(1f);
                        curtain = GameplayDataSettings.SceneManagement.EvacuateScreenScene;
                    }
                    catch (Exception e)
                    {
                        // 结算画面是表现层：失败也必须继续返航，不能把玩家留在图里。
                        curtain = null;
                        Debug.LogWarning(Tag + " 撤离结算画面失败，直接返航：" + e.Message);
                    }
                }
                ModRaidSceneBridge.BeginLoadVisuals(this, GameplayDataSettings.SceneManagement.BaseScene, returnFailed);
                await SceneLoader.Instance.LoadScene(GameplayDataSettings.SceneManagement.BaseScene,
                    curtain, clickToConinue: false, notifyEvacuation: evacuated, saveToFile: true);
                returnFailed = false;
                baseReturned = true;
            }
            catch (Exception e) { returnFailed = true; Error = e.Message; Debug.LogError(Tag + " RAID_RETURN_FAILED " + e); ReleaseStuckOfficialLoadingFlag(Tag, "return"); }
            finally
            {
                try { await ModRaidSceneBridge.EndLoadVisuals(this); }
                catch (Exception e) { Debug.LogWarning(Tag + " 返航黑幕清理失败: " + e.Message); }
                if (baseReturned && failureNotice != null)
                {
                    try { NotificationText.Push(failureNotice); failureNotice = null; }
                    catch (Exception e) { Debug.LogWarning(Tag + " 返航失败原因提示未显示: " + e.Message); }
                }
                returning = false; retryAt = Time.unscaledTime + 2f; TryRelease();
            }
        }

        /// <summary>
        /// 官方 SceneLoader.LoadScene 置起 IsSceneLoading 之后再抛异常时不会放下这个静态标志，之后每次 LoadScene 都早返、
        /// Esc 暂停菜单也打不开（CR-2026-09-30-007）。只在本租约自己 await 的那次官方加载以异常结束后调用。
        /// </summary>
        internal static void ReleaseStuckOfficialLoadingFlag(string tag, string where)
        {
            if (!SceneLoader.IsSceneLoading) return;
            try
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                System.Reflection.PropertyInfo property = typeof(SceneLoader).GetProperty("IsSceneLoading", flags);
                System.Reflection.MethodInfo setter = property != null ? property.GetSetMethod(true) : null;
                if (setter != null) setter.Invoke(null, new object[] { false });
                else
                {
                    System.Reflection.FieldInfo field = typeof(SceneLoader).GetField("IsSceneLoading", flags)
                        ?? typeof(SceneLoader).GetField("<IsSceneLoading>k__BackingField", flags);
                    if (field != null) field.SetValue(null, false);
                }
                Debug.LogWarning(tag + " RAID_LOADER_FLAG_RELEASED at=" + where + " cleared=" + !SceneLoader.IsSceneLoading);
            }
            catch (Exception e) { Debug.LogWarning(tag + " 官方加载标志复位失败：" + e.Message); }
        }

        internal void Release(Action callback)
        {
            if (callback != null) completed += callback;
            releaseRequested = true;
            (preparation as IDisposable)?.Dispose();
            preparation = null;
            // 加载途中被取消：官方等待必须收到取消信号，否则它会一直等一个不会到来的 LevelInited。
            if (initializing && loading) Abort("独立地图初始化已被取消");
            TryRelease();
            if (bundle != null && recovery == null)
            {
                GameObject owner = new GameObject("ModRaidRecovery_" + profile.Identity.SceneName);
                UnityEngine.Object.DontDestroyOnLoad(owner);
                recovery = owner.AddComponent<ModRaidRecovery>();
                recovery.Bind(this);
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (!IsRaidScene(scene)) return;
            if (profile.SceneUnloaded != null)
            {
                try { profile.SceneUnloaded(scene); }
                catch (Exception e) { Debug.LogWarning(Tag + " 场景卸载钩子失败: " + e.Message); }
            }
            TryRelease();
        }

        internal bool IsRaidScene(Scene scene)
        { return string.Equals(scene.path, profile.Identity.ScenePath, StringComparison.OrdinalIgnoreCase); }

        internal void PumpRelease()
        {
            if (!releaseRequested || loading || returning || SceneLoader.IsSceneLoading) return;
            if (ModRaidSceneBridge.HasPendingInitializationLoad(this)) return;
            // 返航的幕布可能已卸掉本图；失败任务仍须重试，不能仅凭本图不在就认定已回到基地。
            if (returnFailed) { ReturnToBase(false); return; }
            Scene scene = SceneManager.GetSceneByPath(profile.Identity.ScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                // 死亡任务拥有损失、墓碑与返航；即使宿主销毁也不能抢先触发二次撤离。
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null && main.Health != null && main.Health.IsDead) return;
                ReturnToBase(false);
                return;
            }
            TryRelease();
        }

        private void TryRelease()
        {
            if (!releaseRequested || loading || returning || returnFailed) return;
            if (ModRaidSceneBridge.HasPendingInitializationLoad(this)) return;
            Scene scene = SceneManager.GetSceneByPath(profile.Identity.ScenePath);
            if (scene.IsValid() && scene.isLoaded) return;
            // 令牌必须在这里归还：漏还会让下一次进图的 BeginInitialization 直接抛「上一段初始化尚未释放」。
            MarkInitialized();
            if (subscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
                // 正常卸载走 OnSceneUnloaded；异常退出（场景从没加载）走这里，钩子自己保证幂等。
                if (profile.SceneUnloaded != null)
                {
                    try { profile.SceneUnloaded(default(Scene)); }
                    catch (Exception e) { Debug.LogWarning(Tag + " 释放钩子失败: " + e.Message); }
                }
            }
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            if (timeOfDay != null) UnityEngine.Object.Destroy(timeOfDay.gameObject);
            timeOfDay = null;
            if (recovery != null) { UnityEngine.Object.Destroy(recovery.gameObject); recovery = null; }
            Action callback = completed; completed = null;
            if (callback != null) callback();
        }
    }

    /// <summary>只在宿主/会话已清理但官方加载或关卡仍存活时出现；完成官方返航后释放自己。</summary>
    internal sealed class ModRaidRecovery : MonoBehaviour
    {
        private ModRaidLease lease;
        internal void Bind(ModRaidLease value) { lease = value; }
        private void Update() { if (lease != null) lease.PumpRelease(); }
        private void OnDestroy() { lease = null; }
    }
}
