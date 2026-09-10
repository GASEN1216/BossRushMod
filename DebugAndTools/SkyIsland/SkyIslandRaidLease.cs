using System;
using System.IO;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using Eflatun.SceneReference;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>独立关卡资源租约。官方 SceneLoader 独占关卡转换，租约只预载包并等待 Scene 真正卸载。</summary>
    internal sealed class SkyIslandRaidLease
    {
        internal const string BundleRelativePath = "Assets/arenas/sky_island_raid";
        private AssetBundle bundle;
        private bool releaseRequested, subscribed, loading, returning, initializing;
        private TimeOfDayConfig timeOfDay;
        private SkyIslandRaidRecovery recovery;
        private float retryAt;
        private Action completed;
        internal bool LoadFinished { get; private set; }
        internal string Error { get; private set; }
        internal bool IsReturning { get { return returning; } }

        /// <summary>
        /// 场景包是否已随 Mod 部署。入口在挂交互之前先问一次：缺包时不挂船点选项、不立招牌、不发公告，
        /// 免得玩家点进去才在 Prepare 里撞见 FileNotFound。
        /// </summary>
        internal static bool IsBundleDeployed()
        {
            try { return File.Exists(Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath)); }
            catch (Exception) { return false; }
        }

        internal void Prepare(string modDirectory, TimeOfDayConfig template)
        {
            if (bundle != null || loading || releaseRequested) throw new InvalidOperationException("独立场景租约不可重复使用");
            if (template == null) throw new InvalidOperationException("天空岛官方天气配置缺失");
            // 官方 `TimeOfDayConfig` 与它的 5 个 `TimeOfDayEntry` 都是**场景 MonoBehaviour**，不是资产：
            // 官方每张图（Base 与各出击图一致）的层级都是 `LevelConfig/TimeOfDayConfig/TimeOfDay_*`。
            // 直接把基地那一份带到岛上，基地场景一卸载它就被销毁，注入进去的是已销毁引用，
            // Unity 的 `== null` 判它为空——合同会说「天气未注入」，玩家永远进不去岛（CR-2026-09-10-003）。
            // 所以趁人还在基地克隆整棵子树并转 DontDestroyOnLoad：子物体之间的引用由 Instantiate
            // 负责重映射，跨出子树的只剩 VolumeProfile 这类真资产，本来就是共享的。
            // 副本的销毁在 TryRelease（所有退出路径的唯一收口）。
            timeOfDay = UnityEngine.Object.Instantiate(template);
            timeOfDay.gameObject.name = "BossRush_SkyIslandTimeOfDay";
            UnityEngine.Object.DontDestroyOnLoad(timeOfDay.gameObject);
            if (!SkyIslandSceneReferenceBridge.EnsureRegistered())
                throw new InvalidOperationException("天空岛官方场景引用桥接尚未就绪");
            string path = Path.Combine(modDirectory, BundleRelativePath);
            if (!File.Exists(path)) throw new FileNotFoundException("缺少天空岛独立出击场景包，请更新 Mod 资源", path);
            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException("天空岛独立场景包无法读取");
            string[] scenes = bundle.GetAllScenePaths();
            if (scenes.Length != 1 || !string.Equals(scenes[0], SkyIslandSceneReferenceBridge.ScenePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("天空岛独立场景包版本与程序不匹配");
            if (!subscribed)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                subscribed = true;
            }
            // 初始化令牌归租约：它的生命周期覆盖加载、返航与释放，宿主销毁后仍由 recovery 续命。
            SkyIslandSceneReferenceBridge.BeginInitialization(this);
            initializing = true;
        }

        /// <summary>官方合同通过后调用：解除失败信号，之后的官方等待恢复原生语义。</summary>
        internal void MarkInitialized()
        {
            if (!initializing) return;
            initializing = false;
            SkyIslandSceneReferenceBridge.EndInitialization(this);
        }

        /// <summary>会话超时或取消时调用：给官方加载等待一个可观察的取消信号，避免永久停在加载界面。</summary>
        internal void Abort(string reason)
        {
            if (!initializing) return;
            SkyIslandSceneReferenceBridge.AbortInitialization(this, reason);
            if (Error == null) Error = reason;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsRaidScene(scene)) return;
            // 官方爆炸的遮挡射线写死在 y=0.5，天空岛的岛面在 0–62 米，不改就等于全岛爆炸穿墙。
            // 补丁只认这一个场景句柄，登记与撤销都跟着 raid 场景的实际生命周期走。
            SkyIslandExplosionObstaclePatch.Arm(scene);
            try
            {
                GameObject services = null, world = null;
                foreach (GameObject candidate in scene.GetRootGameObjects())
                {
                    if (candidate.name == "SkyIslandLevel") services = candidate;
                    else if (candidate.name == "SkyIslandWorld") world = candidate;
                }
                if (services == null || world == null) throw new InvalidOperationException("独立场景缺少地形或关卡根节点");
                SkyIslandSceneReferenceBridge.BindInitializationScene(this, scene);
                // 注入必须排在合同验证之前。官方 TimeOfDayConfig 是场景组件（见 Prepare 的注释），作者工程造不出；
                // startBuffPrefabs 更是连字段都没进包（UnityPy 读回包内 LevelConfig：
                // timeOfDayConfig = {FileID 0, PathID 0}，startBuffPrefabs 整个字段缺席）。
                // 这两项只能由租约在这里补齐，反过来排序合同的两条 null 判据就当场判死，
                // 玩家看到的是「天空岛创建失败：…」而包本身完全正常（CR-2026-09-10-002）。
                LevelConfig config = services.GetComponent<LevelConfig>();
                if (config == null || services.activeSelf) throw new InvalidOperationException("独立关卡必须在配置完成后激活");
                config.timeOfDayConfig = timeOfDay;
                config.startBuffPrefabs = new List<Duckov.Buffs.Buff>();
                // 官方服务一旦激活就会开始重建主角；最小装配的完整性必须在激活之前定论。
                string contractError;
                if (!SkyIslandOfficialContract.VerifyBeforeActivation(scene, services, world, out contractError))
                    throw new InvalidOperationException(contractError);
                if (global::AstarPath.active == null) services.AddComponent<global::AstarPath>();
                // 这个最小官方服务装配不能依赖已被销毁的 Session，否则加载取消后永远无法完成官方初始化。
                if (releaseRequested) world.SetActive(true);
                services.SetActive(true);
            }
            catch (Exception e)
            {
                Error = e.Message;
                SkyIslandSceneReferenceBridge.AbortInitialization(this, e.Message);
                Debug.LogError("[SkyIsland] RAID_ASSEMBLY_FAILED " + e);
            }
        }

        internal async void BeginLoad()
        {
            loading = true;
            try
            {
                // clickToConinue: true 与官方出击一致（`MapSelectionView.LoadTask`）：
                // 读条结束后停在「点击继续」，玩家点了才真正进图。用 false 会把人直接甩进战斗区，
                // 和原版任何一张地图的进入手感都不一样。
                await SceneLoader.Instance.LoadScene(SkyIslandSceneReferenceBridge.SceneId,
                    new MultiSceneLocation { SceneID = SkyIslandSceneReferenceBridge.SceneId, LocationName = "StartPoints/PlayerSpawn" },
                    clickToConinue: true, notifyEvacuation: false, saveToFile: true);
            }
            catch (Exception e) { Error = e.Message; Debug.LogError("[SkyIsland] RAID_LOAD_FAILED " + e); }
            finally
            {
                loading = false;
                LoadFinished = true;
                TryRelease();
            }
        }

        internal async void ReturnToBase(bool evacuated)
        {
            if (returning || loading || SceneLoader.IsSceneLoading || Time.unscaledTime < retryAt) return;
            returning = true;
            try
            {
                // 原生撤离事件、角色保存、幕布切图、基地角色重建按官方完整顺序执行。
                //
                // 撤离这一段照抄官方 `SceneLoaderProxy.Task` 的顺序：
                //   NotifyEvacuated -> ClosureView(1s) -> 幕布换成 EvacuateScreenScene -> LoadScene
                // 少了前三步，玩家撤离时看到的是一张普通读条，而不是原版每张图都有的撤离结算画面。
                // NotifyEvacuated 顺带把主角设为无敌，结算那一秒里不会被打死。
                // 官方自己也会在 LoadScene 内部再 notify 一次（notifyEvacuation: true），
                // 这个「两次」是原版既有行为，订阅方本来就要容忍，这里如实照搬。
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
                        Debug.LogWarning("[SkyIsland] 撤离结算画面失败，直接返航：" + e.Message);
                    }
                }
                await SceneLoader.Instance.LoadScene(GameplayDataSettings.SceneManagement.BaseScene,
                    curtain, clickToConinue: false, notifyEvacuation: evacuated, saveToFile: true);
            }
            catch (Exception e) { Error = e.Message; Debug.LogError("[SkyIsland] RAID_RETURN_FAILED " + e); }
            finally { returning = false; retryAt = Time.unscaledTime + 2f; TryRelease(); }
        }

        internal void Release(Action callback)
        {
            if (callback != null) completed += callback;
            releaseRequested = true;
            // 加载途中被取消：官方等待必须收到取消信号，否则它会一直等一个不会到来的 LevelInited。
            if (initializing && loading) Abort("天空岛初始化已被取消");
            TryRelease();
            if (bundle != null && recovery == null)
            {
                GameObject owner = new GameObject("SkyIslandRaidRecovery");
                UnityEngine.Object.DontDestroyOnLoad(owner);
                recovery = owner.AddComponent<SkyIslandRaidRecovery>();
                recovery.Bind(this);
            }
        }
        private void OnSceneUnloaded(Scene scene)
        {
            if (!IsRaidScene(scene)) return;
            SkyIslandExplosionObstaclePatch.Disarm();
            TryRelease();
        }
        internal static bool IsRaidScene(Scene scene)
        { return string.Equals(scene.path, SkyIslandSceneReferenceBridge.ScenePath, StringComparison.OrdinalIgnoreCase); }

        internal void PumpRelease()
        {
            if (!releaseRequested || loading || returning || SceneLoader.IsSceneLoading) return;
            Scene scene = SceneManager.GetSceneByPath(SkyIslandSceneReferenceBridge.ScenePath);
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
            if (!releaseRequested || loading || returning) return;
            Scene scene = SceneManager.GetSceneByPath(SkyIslandSceneReferenceBridge.ScenePath);
            if (scene.IsValid() && scene.isLoaded) return;
            // 令牌必须在这里归还：漏还会让下一次进岛的 BeginInitialization 直接抛「上一段初始化尚未释放」。
            MarkInitialized();
            if (subscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }
            // 场景已确认不在（上面的 isLoaded 早返），这里是撤销遮挡补丁的兜底路径：
            // 正常走 OnSceneUnloaded，异常退出走这里，两处都是幂等的 bool 写。
            SkyIslandExplosionObstaclePatch.Disarm();
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            // 天气副本是 DontDestroyOnLoad 的，不会随任何场景卸载消失，必须自己收；
            // Unity 的 != null 对已销毁对象为 false，重复释放天然幂等。
            if (timeOfDay != null) UnityEngine.Object.Destroy(timeOfDay.gameObject);
            timeOfDay = null;
            if (recovery != null) { UnityEngine.Object.Destroy(recovery.gameObject); recovery = null; }
            Action callback = completed; completed = null;
            if (callback != null) callback();
        }
    }

    /// <summary>只在宿主/会话已清理但官方加载或关卡仍存活时出现；完成官方返航后释放自己。</summary>
    internal sealed class SkyIslandRaidRecovery : MonoBehaviour
    {
        private SkyIslandRaidLease lease;
        internal void Bind(SkyIslandRaidLease value) { lease = value; }
        private void Update() { if (lease != null) lease.PumpRelease(); }
        private void OnDestroy() { lease = null; }
    }
}
