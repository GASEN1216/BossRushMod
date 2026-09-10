using System;
using System.Collections.Generic;
using BossRush.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>COMPAT：正式船点入口与地图快捷键的唯一 owner，独立于 F3 的开发开关。</summary>
    internal sealed class SkyIslandRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private SkyIslandDepartureInteractable departure;
        private List<InteractableBase> boatGroup;
        private Canvas sign;
        private bool subscribed;
        private bool announced;
        private bool bundleWarned;
        private int attempts;
        private float nextAttempt;
        // 基地判定的场景缓存，见 InBaseHubScene()。0 不是合法的 Scene.handle，可直接当「未缓存」。
        private int cachedSceneHandle;
        private bool cachedBaseHub;

        public override string ModuleName { get { return "SkyIsland"; } }

        public override void OnAwake(ModBehaviour host)
        {
            owner = host;
            SkyIslandSceneReferenceBridge.EnsureRegistered();
            if (!subscribed)
            {
                LevelManager.OnAfterLevelInitialized += OnLevelReady;
                SceneLoader.onStartedLoadingScene += OnStartedLoading;
                subscribed = true;
            }
        }

        public override void OnStart() { SkyIslandSceneReferenceBridge.EnsureRegistered(); ScheduleEntry(); }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            if (SceneRuntimeGate.IsModResourceScene(context.Scene)) return;
            ScheduleEntry();
        }

        private void OnLevelReady() { ScheduleEntry(); }

        private void OnStartedLoading(SceneLoadingContext context)
        {
            ClearEntry();
            announced = false;
            attempts = 0;
            cachedSceneHandle = 0;
        }

        private void ScheduleEntry()
        {
            attempts = 12;
            nextAttempt = Time.unscaledTime + 0.5f;
            cachedSceneHandle = 0;
        }

        /// <summary>
        /// 当前活动场景是不是基地。
        ///
        /// `Scene.name` **每次调用都会新建一个托管字符串**，而这里是每帧路径（模块 OnUpdate 在
        /// 所有场景都跑），直接调等于每帧产生垃圾（AGENTS 4.12；口径同
        /// `CampaignFinalBoss.IsCampaignArenaSceneCached` 的场景代数缓存）。
        ///
        /// 缓存键用 `Scene.handle`（结构体里的整数，不分配）。句柄在场景卸载后可能被复用，
        /// 因此 `ScheduleEntry`（切图完成 / 关卡就绪）与 `OnStartedLoading`（开始切图）
        /// 两处都会把缓存作废，句柄比较只负责兜住「没有任何回调却换了活动场景」的情形。
        /// </summary>
        private bool InBaseHubScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.handle != cachedSceneHandle)
            {
                cachedSceneHandle = active.handle;
                cachedBaseHub = SceneRuntimeGate.IsBaseHubSceneName(active.name);
            }
            return cachedBaseHub;
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (owner == null) return;
            // 岛上不再有自绘地图，也就不需要任何输入处理：
            // 官方地图由玩家自己绑定的地图键开合（`CharacterInputControl.OnUIMapInput`）。
            if (owner.GetComponent<SkyIslandSession>() != null) return;
            if (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || !LevelManager.LevelInited) return;
            if (!InBaseHubScene())
            {
                ClearEntry();
                announced = false;
                return;
            }
            if (departure != null || attempts <= 0 || Time.unscaledTime < nextAttempt) return;
            // 缺场景包时不挂船点选项、不立招牌、不发公告：与其让玩家点进去才失败，不如根本不出现这个入口。
            if (!SkyIslandRaidLease.IsBundleDeployed())
            {
                attempts = 0;
                if (!bundleWarned)
                {
                    bundleWarned = true;
                    ModBehaviour.CriticalLog("sky-island-bundle-missing",
                        "[SkyIsland] 缺少 " + SkyIslandRaidLease.BundleRelativePath + "，本次不开放天空岛航路入口。");
                }
                return;
            }
            nextAttempt = Time.unscaledTime + 1;
            attempts--;
            bool boatSeen = false;
            foreach (InteractableBase candidate in UnityEngine.Object.FindObjectsOfType<InteractableBase>(true))
            {
                if (!owner.IsBaseHubBoatInteractable(candidate)) continue;
                boatSeen = true;
                boatGroup = NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(candidate, "[SkyIsland]");
                departure = NPCInteractionGroupHelper.AddSubInteractable<SkyIslandDepartureInteractable>(
                    candidate.transform, "BossRush_SkyIsland_Departure", boatGroup, value => value.Bind(owner));
                if (departure == null) continue;
                CreateSign(candidate.transform);
                if (!announced)
                {
                    announced = true;
                    owner.ShowMessage(L10n.T("天空岛航路已开放：在基地船点选择「前往天空岛」。",
                        "The Sky Islands are open: choose Depart for Sky Islands at the base boat."));
                }
                return;
            }
            if (attempts != 0) return;
            // 「没找到船点」绝大多数时候**不是故障**：官方出击船点是
            // `Base_SceneV2_Sub_01` 的 `Envir/Prfb_BoatBetweenBaseAndFarm/Interact`
            // （同层还有官方自己的 `Interact_Challenge` / `Interact_SnowChallenge`），
            // 而那是一张按需加载的子场景——玩家站在 `Base_SceneV2` 主城区时它根本没加载，
            // 12 次重试自然全空。旧代码在这里发 CRITICAL，等于每次回基地都误报一次
            // （CR-2026-09-10-005：2026-09-10 的三份 Player.log 里都有，实际功能没坏）。
            // 走到码头时子场景加载会触发 `OnSceneLoaded` → `ScheduleEntry` 重新武装，
            // 那一轮才是真正该出结论的时机。
            if (!boatSeen)
            {
                ModBehaviour.DevLog("[SkyIsland] 基地船点所在子场景尚未加载，暂不挂航路入口；走到码头会自动重试。");
                return;
            }
            // 找到了船点却挂不上去才是真故障：分组准备或子交互创建失败。
            ModBehaviour.CriticalLog("sky-island-entry-missing",
                "[SkyIsland] 已找到基地船点但航路子交互注入失败，请检查交互分组注入。回到基地后会重试。");
        }

        private void CreateSign(Transform boat)
        {
            if (sign != null) return;
            sign = BossRushUI.CreateCanvasRoot("SkyIslandDepartureSign", BossRushUILayers.WorldOverlay, false);
            sign.renderMode = RenderMode.WorldSpace;
            sign.transform.SetParent(boat, false);
            sign.transform.localPosition = new Vector3(0, 2.4f, 0);
            sign.transform.localScale = Vector3.one * 0.006f;
            RectTransform rect = sign.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(620, 130);
            GameObject label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(sign.transform, false);
            TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(text);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            // 招牌不再是一块远远就亮着的黄字：主标题用正文色、副标题降一级，走近船点才浮现
            // （SkyIslandProximityLabel）。远处靠官方交互标记与首次到基地时那条公告指路就够了，
            // 常驻的浮空字正是网游式头顶标语的来源。
            text.text = L10n.T("天空岛 · 晴岚群岛", "Sky Islands · Qinglan") + "\n<size=62%><color=#" +
                ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary) + ">" +
                L10n.T("与船点互动即可出发", "Interact with the boat to depart") + "</color></size>";
            text.fontSize = 34;
            text.alignment = TextAlignmentOptions.Center;
            text.color = BossRushUIColors.TextPrimary;
            text.raycastTarget = false;
            SkyIslandProximityLabel.Attach(sign.gameObject, 9f, 16f);
        }

        public override void OnLateUpdate()
        {
            if (sign != null && GameCamera.Instance != null && GameCamera.Instance.renderCamera != null)
                sign.transform.rotation = GameCamera.Instance.renderCamera.transform.rotation;
        }

        private void ClearEntry()
        {
            if (boatGroup != null && departure != null) boatGroup.Remove(departure);
            boatGroup = null;
            if (departure != null) UnityEngine.Object.Destroy(departure.gameObject);
            departure = null;
            if (sign != null) UnityEngine.Object.Destroy(sign.gameObject);
            sign = null;
        }

        public override void OnDestroy()
        {
            if (subscribed)
            {
                LevelManager.OnAfterLevelInitialized -= OnLevelReady;
                SceneLoader.onStartedLoadingScene -= OnStartedLoading;
                subscribed = false;
            }
            ClearEntry();
            if (owner != null)
            {
                SkyIslandSession session = owner.GetComponent<SkyIslandSession>();
                if (session != null) session.Close(true, "runtime_shutdown");
            }
            owner = null;
            SkyIslandSceneReferenceBridge.Shutdown();
            // 子系统静态缓存的唯一清理 owner 是模块 OnDestroy（设计复审 D-4）：
            // 物资池与档次染色块跨出击复用，只在模块销毁时释放。
            SkyIslandLootPools.ResetStaticCaches();
            SkyIslandEnemyTiers.ResetStaticCaches();
            SkyIslandStormBoss.ResetStaticCaches();
            SkyIslandItems.ResetStaticCaches();
            // 群岛耗材找 owner 用的静态引用：会话销毁时 owner 自己会清，模块销毁再兜一次。
            SkyIslandFieldcraft.ResetStaticCaches();
            // 面板插图同样是跨出击复用的静态缓存：运行时 new 出来的 Texture/Sprite
            // 必须显式 Destroy，只置 null 是丢给 UnloadUnusedAssets 碰运气。
            SkyIslandUiArt.ResetStaticCaches();
        }
    }

    public sealed class SkyIslandDepartureInteractable : BossRushBuildingInteractableBase
    {
        private ModBehaviour owner;
        protected override string InteractNameKey
        {
            get
            {
                const string key = "BossRush_SkyIsland_Departure";
                LocalizationHelper.InjectLocalization(key, L10n.T("前往天空岛 · 晴岚群岛", "Depart for Sky Islands · Qinglan"));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIsland] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIslandDeparture]"; } }
        internal void Bind(ModBehaviour host) { owner = host; }
        protected override bool IsBuildingInteractable() { return owner != null && owner.GetComponent<SkyIslandSession>() == null; }
        protected override void OnInteractCompleted()
        {
            if (owner != null) SkyIslandSession.Enter(owner, Report);
        }
        private void Report(string message, bool error)
        {
            if (owner != null) owner.ShowMessage(message);
            if (error) Debug.LogWarning("[SkyIsland] " + message);
        }
    }
}
