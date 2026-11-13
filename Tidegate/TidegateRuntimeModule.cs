using System;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// COMPAT / SCHEMA+：潮闸镇的入口、剧情存档与模块生命周期 owner。入口是基地里一个独立交互点「闸伯的竹筏」（拍板：不挤船点），
    /// 读过闸伯的信（<see cref="TidegateStory.RouteUnlocked"/>）之后、且它所在的地堡下水道子场景已加载时才挂上，闸伯本人站在筏子旁；
    /// 场景身份在模块启动时登记到多图共用的 <see cref="ModRaidSceneBridge"/>。
    /// </summary>
    internal sealed class TidegateRuntimeModule : BossRushRuntimeModuleBase
    {
        private const string BaseOutdoorScene = "Base_SceneV2";
        private ModBehaviour owner;
        private GameObject raft;
        private TidegateStoryPersistence story;
        private RaidOfficialLighting sunTemplate;
        private bool subscribed, bundleWarned, applicationQuitting, returnLightingPending;
        private float nextAttempt, nextLetterCheck;

        public override string ModuleName { get { return "Tidegate"; } }

        public override void OnAwake(ModBehaviour host)
        {
            owner = host;
            ModRaidSceneBridge.Register(TidegateRaidScene.Identity);
            story = new TidegateStoryPersistence(TidegateNoteBridge.RequestSync);
            TidegateStory.Bind(story);
            TidegateNoteBridge.EnsureRuntime();
            TidegateNoteBridge.InjectNoteKeys();
            if (!subscribed)
            {
                SceneLoader.onStartedLoadingScene += OnStartedLoading;
                SceneManager.sceneLoaded += OnSceneLoaded;
                LevelManager.OnAfterLevelInitialized += OnLevelReady;
                Application.quitting += OnApplicationQuitting;
                subscribed = true;
            }
            CaptureOutdoorSun(SceneManager.GetSceneByName(BaseOutdoorScene));
        }

        private void OnApplicationQuitting() { applicationQuitting = true; }

        private void OnStartedLoading(SceneLoadingContext context) { ClearEntry(); }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { CaptureOutdoorSun(scene); }

        private void CaptureOutdoorSun(Scene scene)
        {
            if (sunTemplate != null || !scene.isLoaded || scene.name != BaseOutdoorScene) return;
            var template = new RaidOfficialLighting();
            try
            {
                // 进入下水道时原版 MultiSceneCore 会卸载户外子场景。提前保存停用的原版方向光模板。
                template.Prepare(scene);
                sunTemplate = template;
            }
            catch (Exception e)
            {
                template.Dispose();
                Debug.LogWarning(TidegateRaidScene.LogTag + " 官方基地日光模板未能保留：" + e.Message);
            }
        }

        private void PrepareRaidLighting(RaidOfficialLighting lighting)
        {
            if (sunTemplate != null)
            {
                lighting.PrepareFromTemplate(sunTemplate);
                return;
            }
            Debug.LogWarning(TidegateRaidScene.LogTag + " 官方基地户外主光未加载，使用官方渲染组件接入天气");
            lighting.PrepareNative();
        }

        /// <summary>
        /// 回到基地后让引擎按基地自己的设置重算一次环境光（口径同天空岛 `RefreshBaseLightingAfterReturn`）：
        /// 潮闸镇的光照 owner 改过 RenderSettings 与环境球，场景卸载后引擎可能沿用那份环境球。每趟返航只做一次。
        /// </summary>
        private void OnLevelReady()
        {
            if (!returnLightingPending || owner == null) return;
            // 撤离返航时会话组件要等官方加载走完（基地就绪之后）才销毁，这一刻它还在但已收尾，按「不在出击」算。
            TidegateSession session = owner.GetComponent<TidegateSession>();
            if (session != null && !session.IsClosed) return;
            if (!SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name)) return;
            returnLightingPending = false;
            try { DynamicGI.UpdateEnvironment(); }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 返航刷新基地环境光失败: " + e.Message); }
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (owner == null) return;
            TidegateStory.Tick();
            TidegateNoteBridge.Tick();
            TidegateSession active = owner.GetComponent<TidegateSession>();
            if (active != null) { if (!active.IsClosed) returnLightingPending = true; return; }
            if (Time.unscaledTime < nextAttempt) return;
            nextAttempt = Time.unscaledTime + 1f;
            if (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || !LevelManager.LevelInited) return;
            // 在竹筏边结婚的闸伯趁还在下水道时交给婚姻接管（挪回主场景），之后玩家走出下水道也不会把配偶一起卸掉。
            TidegateZhabo.ReleaseIfMarried();
            bool inBase = SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name);
            // 阿稳送湿包裹：在基地、没有进行中的模式时送一次；失败下次进基地再试（每 30 秒最多问一次）。
            string mode;
            if (inBase && Time.unscaledTime >= nextLetterCheck && !owner.ValidationHasActiveMode(out mode))
            {
                nextLetterCheck = Time.unscaledTime + 30f;
                TidegateStory.TryDeliverLetter();
            }
            Scene target = SceneManager.GetSceneByName(TidegateEntryPoint.SceneName);
            if (!target.IsValid() || !target.isLoaded || !inBase || !TidegateStory.RouteUnlocked)
            {
                ClearEntry();
                return;
            }
            if (raft != null)
            {
                // 闸伯第一次没生成出来（官方生成返回空）就在这里补：它自己判重、判婚、30 秒退避，平时是几个字段比较。
                TidegateZhabo.Spawn(target);
                return;
            }
            if (!TidegateRaidScene.IsBundleDeployed() || !TidegateEntryRaft.IsBundleDeployed())
            {
                if (!bundleWarned)
                {
                    bundleWarned = true;
                    string missing = TidegateRaidScene.IsBundleDeployed() ? TidegateEntryRaft.BundleRelativePath : TidegateRaidScene.BundleRelativePath;
                    ModBehaviour.CriticalLog("tidegate-bundle-missing",
                        TidegateRaidScene.LogTag + " 缺少 " + missing + "，本次不开放潮闸镇入口。");
                }
                return;
            }
            try { raft = TidegateEntryRaft.Create(target, owner, PrepareRaidLighting); }
            catch (Exception e)
            {
                raft = null;
                ModBehaviour.CriticalLog("tidegate-entry-failed", TidegateRaidScene.LogTag + " 竹筏入口创建失败: " + e.Message);
                nextAttempt = Time.unscaledTime + 30f;
            }
        }

        private void ClearEntry()
        {
            TidegateZhabo.Despawn();
            if (raft == null) return;
            raft.SetActive(false);
            TidegateEntryRaft.Release(raft);
            UnityEngine.Object.Destroy(raft);
            raft = null;
        }

        public override void OnDestroy()
        {
            if (subscribed)
            {
                SceneLoader.onStartedLoadingScene -= OnStartedLoading;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                LevelManager.OnAfterLevelInitialized -= OnLevelReady;
                Application.quitting -= OnApplicationQuitting;
                subscribed = false;
            }
            ClearEntry();
            if (sunTemplate != null) { sunTemplate.Dispose(); sunTemplate = null; }
            if (owner != null)
            {
                TidegateSession session = owner.GetComponent<TidegateSession>();
                // 退游戏不派发返航；会话照常清理。
                if (session != null) session.Close(!applicationQuitting, applicationQuitting ? "application_quit" : "runtime_shutdown");
            }
            owner = null;
            ModRaidSceneBridge.Unregister(TidegateRaidScene.Identity);
            TidegateStory.Bind(null);
            if (story != null) story.Dispose();
            story = null;
            TidegateNoteBridge.ResetStaticCaches();
        }
    }

    /// <summary>
    /// 使用官方需求行（物品名、图标、消耗数量与有无颜色）。进入交互由官方拦缺件；扣物与状态变更一起由本模块完成。
    /// 官方 OnTimeOut 模式会先扣再回调；None 会写 inLevelData。改选 OnFinshed 并在我们的回调完成后主动停止，
    /// 避免两套路各扣一件或留下「用过钥匙」的场景标记。无需物品的机关仍按原版自动完成。
    /// </summary>
    public abstract class TidegateRequirementInteractable : BossRushBuildingInteractableBase
    {
        internal void BindRequirement(int itemId)
        {
            if (itemId <= 0) return;
            requireItem = true;
            requireItemId = itemId;
            requireOnce = false;
            whenToUseRequireItem = InteractableBase.WhenToUseRequireItemTypes.OnFinshed;
            finishWhenTimeOut = false;
        }

        protected override void Start()
        {
            base.Start();
            // 官方 Start 可能按位置哈希读到其他交互留下的解锁位。我们按剧情 / 本局阀门状态决定有没有需求。
            if (requireItemId > 0) requireItem = true;
        }

        protected override void OnTimeOut()
        {
            try { base.OnTimeOut(); }
            finally { if (requireItem && Interacting) StopInteract(); }
        }
    }

    // 官方查询只含角色顶层、插槽和宠物，不遍历随身容器。仅对本模块的两项实物交付，
    // 需求栏与 StartInteract 共同使用和实际扣物相同的主角物品树；宠物和仓库不能替玩家交付。
    [HarmonyPatch(typeof(InteractableBase), nameof(InteractableBase.TryGetRequiredItem), new Type[] { typeof(CharacterMainControl) })]
    internal static class TidegateRequiredItemPatch
    {
        private static bool Prefix(InteractableBase __instance, CharacterMainControl __0, ref ValueTuple<bool, Item> __result)
        {
            if (!(__instance is TidegateRequirementInteractable) || !__instance.requireItem) return true;
            Item item = TidegateInventory.FindCarried(__0, __instance.requireItemId);
            __result = new ValueTuple<bool, Item>(item != null, item);
            return false;
        }
    }

    /// <summary>竹筏组里的「交付」：读过信即显示所需怀表，交付成功后收起。</summary>
    public sealed class TidegateWatchInteractable : TidegateRequirementInteractable
    {
        internal const string NameKey = "BossRush_Tidegate_GiveWatch";
        private ModBehaviour owner;
        protected override string InteractNameKey { get { return NameKey; } }
        protected override string LogPrefix { get { return TidegateRaidScene.LogTag + " "; } }
        internal void Bind(ModBehaviour host) { owner = host; BindRequirement(BossRushItemIds.TidegateTideWatch); }

        protected override void Awake()
        {
            base.Awake();
            // 组员只能由竹筏的主交互点选中；共享建筑基类为独立交互点启用的碰撞体在此关闭。
            if (interactCollider != null) interactCollider.enabled = false;
        }

        protected override bool IsBuildingInteractable()
        {
            return owner != null && !TidegateStory.WatchDelivered;
        }

        protected override void OnInteractCompleted()
        {
            string reason;
            if (TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason))
            {
                CharacterMainControl.Main.RefreshInteractTarget();
                gameObject.SetActive(false);
            }
            else if (owner != null && reason != null)
                owner.ShowMessage(reason);
        }
    }

    /// <summary>竹筏上的交互点：「去潮闸镇」。</summary>
    public sealed class TidegateRaftInteractable : BossRushBuildingInteractableBase
    {
        internal const string NameKey = "BossRush_Tidegate_Raft";
        private ModBehaviour owner;
        private Action<RaidOfficialLighting> prepareLighting;
        protected override string InteractNameKey { get { return NameKey; } }
        protected override string LogPrefix { get { return TidegateRaidScene.LogTag + " "; } }
        internal void Bind(ModBehaviour host, Action<RaidOfficialLighting> prepareLighting)
        {
            owner = host;
            this.prepareLighting = prepareLighting;
        }

        protected override bool IsBuildingInteractable()
        {
            return owner != null && owner.GetComponent<TidegateSession>() == null && owner.GetComponent<SkyIslandSession>() == null
                && owner.GetComponent<LastShiftSession>() == null;
        }

        protected override void OnInteractCompleted()
        {
            if (owner == null) return;
            TidegateSession.Enter(owner, Report, prepareLighting);
        }

        private void Report(string message, bool error)
        {
            if (owner != null) owner.ShowMessage(message);
            if (error) Debug.LogWarning(TidegateRaidScene.LogTag + " " + message);
        }
    }
}
