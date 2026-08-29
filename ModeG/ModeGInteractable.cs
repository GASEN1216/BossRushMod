using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// Mode G 场内交互入口（规格 §15 重写版）。
    ///
    /// 契约：
    /// - 继承 InteractableBase（MonoBehaviour 链），可被 AddComponent&lt;ModeGInteractable&gt;()（任务 #7 接线）；
    /// - IsInteractable 统一受 Availability（生产闸/开发闸）、MapSupportRegistry、
    ///   PresentationAssetCache 预检与 IsModeGEntryBlocked 门控；
    /// - OnTimeOut 只创建/复用不可变 ModeGEntryPreview 并打开确认页；
    ///   不引用 ConfigureBossRushMode/StartFirstWave（Legacy 推进路径隔离）；
    /// - 确认页确认后唯一调用 host.TryStartModeG()。
    /// </summary>
    public sealed class ModeGInteractable : InteractableBase
    {
        private static ModeGInteractable _activeConfirmation;
        private const float ModalWidth = 780f;
        private const float ModalHeight = 560f;

        private GameObject _modalRoot;
        private ZombieModeUIHelper.ModalInputLease _inputLease;
        private int _selectedCandidateIndex;
        private TextMeshProUGUI _firstChoiceText;
        private TextMeshProUGUI _secondChoiceText;
        private ModeGEntryPreview _modalPreview;
        private ModBehaviour _entryHost;
        private bool _confirmed;
        private bool _autoPresenter;

        internal static bool IsConfirmationOpen
        {
            get { return _activeConfirmation != null && _activeConfirmation._modalRoot != null; }
        }

        internal static bool LastConfirmationAttemptedStart { get; private set; }

        /// <summary>
        /// 自动船票入场与路牌入口共用同一确认页。自动流程只负责创建短命
        /// presenter，不直接选择契约或扣除物品。
        /// </summary>
        internal static bool TryOpenConfirmation(ModBehaviour host)
        {
            LastConfirmationAttemptedStart = false;
            if (host == null || _activeConfirmation != null) return false;
            GameObject obj = null;
            try
            {
                if (!ModeGAvailability.IsProductionReady && !ModeGAvailability.AllowDevTestEntry)
                    return false;
                if (ModeGRuntimeGates.IsModeGEntryBlocked) return false;
                if (!ModeGMapSupportRegistry.IsVerifiedSceneName(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) return false;
                if (!ModeGPresentationAssetCache.TryPreflight()) return false;

                ModeGEntryPreview preview = host.GetOrCreateModeGEntryPreview();
                if (preview == null || preview.contractCandidateIds == null
                    || preview.contractCandidateIds.Length < 2
                    || !host.IsModeGEntryPreviewValidForCurrentScene(preview))
                {
                    return false;
                }

                obj = new GameObject("ModeG_AutoConfirmPresenter");
                UnityEngine.Object.DontDestroyOnLoad(obj);
                ModeGInteractable presenter = obj.AddComponent<ModeGInteractable>();
                presenter._autoPresenter = true;
                _activeConfirmation = presenter;
                return presenter.OpenConfirmPage(host, preview);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 自动入口确认页创建失败: " + e.Message);
                _activeConfirmation = null;
                try { if (obj != null) UnityEngine.Object.Destroy(obj); }
                catch (Exception cleanupException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 自动确认页对象清理失败: "
                        + cleanupException.Message);
                }
                return false;
            }
        }

        #region InteractableBase Overrides

        protected override void Awake()
        {
            try
            {
                overrideInteractName = true;
                _overrideInteractNameKey = "BossRush_ModeG_Preview";
                InteractName = "BossRush_ModeG_Preview";
                interactCollider = GetComponent<Collider>();
            }
            catch { }
            try { base.Awake(); } catch { }
            try { MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                overrideInteractName = true;
                _overrideInteractNameKey = "BossRush_ModeG_Preview";
                InteractName = "BossRush_ModeG_Preview";
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            try
            {
                // 正式入口或显式开发入口均可见；两个开关都关闭时隐藏。
                if (!ModeGAvailability.IsProductionReady && !ModeGAvailability.AllowDevTestEntry)
                {
                    return false;
                }
                if (ModeGRuntimeGates.IsModeGEntryBlocked) return false;
                if (!ModeGMapSupportRegistry.IsVerifiedSceneName(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
                {
                    return false;
                }
                // 展示资源预检（fail-closed；缓存结果，bundle 每 runtime 一次加载）
                return ModeGPresentationAssetCache.TryPreflight();
            }
            catch { return false; }
        }

        /// <summary>
        /// 交互触发：只创建/复用不可变 preview 并打开确认页（规格 §15）。
        /// </summary>
        protected override void OnTimeOut()
        {
            try
            {
                if (_modalRoot != null) return; // 确认页已打开（幂等）
                if (!IsInteractable()) return;

                ModBehaviour host = ModBehaviour.Instance;
                if (host == null) return;

                ModeGEntryPreview preview = host.GetOrCreateModeGEntryPreview();
                if (preview == null || preview.contractCandidateIds == null
                    || preview.contractCandidateIds.Length < 2
                    || !host.IsModeGEntryPreviewValidForCurrentScene(preview))
                {
                    host.ShowMessage(L10n.T(
                        "宿命回响入口准备失败，请稍后重试。",
                        "Fate Echo entry is not ready. Please try again later."));
                    return;
                }

                if (!OpenConfirmPage(host, preview))
                {
                    host.ShowMessage(L10n.T(
                        "宿命回响确认页无法安全暂停战斗，请稍后重试。",
                        "Fate Echo could not safely pause combat for confirmation. Please try again."));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] ModeGInteractable.OnTimeOut 异常: " + e.Message);
                try { CloseModal(); }
                catch (Exception closeException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 异常确认页关闭失败: " + closeException.Message);
                }
            }
        }

        #endregion

        #region Confirm Page（确认页）

        private bool OpenConfirmPage(ModBehaviour host, ModeGEntryPreview preview)
        {
            _activeConfirmation = this;
            _entryHost = host;
            _confirmed = false;
            _modalPreview = preview;
            _selectedCandidateIndex = -1;

            GameObject root = new GameObject("ModeG_ConfirmPage");
            UnityEngine.Object.DontDestroyOnLoad(root);
            _modalRoot = root;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.ModeGEntry;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            root.AddComponent<GraphicRaycaster>();

            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeG_Confirm", root.transform, new Vector2(ModalWidth, ModalHeight),
                new Color(0.72f, 0.53f, 0.04f, 1f));

            Transform st = surface.transform;

            try
            {
                Sprite banner = ModeGPresentationAssetCache.GetBannerSprite();
                if (banner != null)
                {
                    GameObject bannerObj = ZombieModeUIHelper.CreateRect(
                        "Banner", st, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0f, 170f), new Vector2(700f, 150f), new Vector2(0.5f, 0.5f));
                    Image bannerImage = bannerObj.AddComponent<Image>();
                    bannerImage.sprite = banner;
                    bannerImage.preserveAspect = true;
                    bannerImage.color = new Color(1f, 1f, 1f, 0.28f);
                    bannerImage.raycastTarget = false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 确认页横幅加载失败: " + e.Message);
            }

            // 标题
            ZombieModeUIHelper.CreateText("Title", st,
                L10n.T("<color=#B8860B>宿命回响</color>", "<color=#B8860B>Fate Echo</color>"),
                34f, new Vector2(0f, 225f), new Vector2(ModalWidth - 60f, 60f),
                TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);

            // 徽记（展示缓存提供；缺失时静默跳过）
            try
            {
                Sprite emblem = ModeGPresentationAssetCache.GetEmblemSprite();
                if (emblem != null)
                {
                    GameObject emblemObj = ZombieModeUIHelper.CreateRect(
                        "Emblem", st, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(0f, -96f), new Vector2(84f, 84f), new Vector2(0.5f, 0.5f));
                    Image emblemImage = emblemObj.AddComponent<Image>();
                    emblemImage.sprite = emblem;
                    emblemImage.raycastTarget = false;
                }
            }
            catch { /* 展示资源缺失不影响确认页 */ }

            // 规则说明
            ZombieModeUIHelper.CreateText("Rules", st,
                L10n.T(
                    "携带自己的装备挑战九波；后续波次会针对你上一波的距离、弹药和伤害类型\n改变打法破解反制可获得 Resolve；第 9 波胜利后按 Resolve 发放 6-10 件 Q5-Q8 奖励并返还信物\n消耗 1 船票 + 1 宿命回响信物",
                    "Bring your loadout through 9 waves; later waves counter your previous range, ammo, and damage style\nAdapt to break counters and earn Resolve; victory grants 6-10 Q5-Q8 items and refunds the relic\nCosts 1 ticket + 1 Fate Echo relic"),
                16f, new Vector2(0f, 148f), new Vector2(ModalWidth - 80f, 78f),
                TextAlignmentOptions.Center, ZombieModeUIHelper.TextSecondaryColor);

            // 契约二选一
            ZombieModeUIHelper.CreateText("ContractHeader", st,
                L10n.T("选择你的宿命契约：", "Choose your Fate Contract:"),
                20f, new Vector2(0f, 92f), new Vector2(ModalWidth - 80f, 30f),
                TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);

            ModeGFateContract.ContractDef first = ModeGFateContract.GetById(preview.contractCandidateIds[0]);
            ModeGFateContract.ContractDef second = ModeGFateContract.GetById(preview.contractCandidateIds[1]);

            Button firstButton = ZombieModeUIHelper.CreateButton(
                "Contract_0", st, BuildChoiceLabel(first, true),
                new Vector2(0.5f, 0.5f), new Vector2(-180f, 18f), new Vector2(330f, 96f),
                ZombieModeUIHelper.ModalSurfaceColor, 17f, new Vector2(310f, 88f),
                () => SelectCandidate(0), true);
            Button secondButton = ZombieModeUIHelper.CreateButton(
                "Contract_1", st, BuildChoiceLabel(second, false),
                new Vector2(0.5f, 0.5f), new Vector2(180f, 18f), new Vector2(330f, 96f),
                ZombieModeUIHelper.ModalSurfaceColor, 17f, new Vector2(310f, 88f),
                () => SelectCandidate(1), true);
            ZombieModeUIHelper.ApplyButtonColors(firstButton,
                ZombieModeUIHelper.ModalSurfaceColor, ZombieModeUIHelper.WarningHoverColor,
                ZombieModeUIHelper.DisabledColor);
            ZombieModeUIHelper.ApplyButtonColors(secondButton,
                ZombieModeUIHelper.ModalSurfaceColor, ZombieModeUIHelper.WarningHoverColor,
                ZombieModeUIHelper.DisabledColor);

            Transform firstTextTransform = firstButton.transform.Find("Text");
            Transform secondTextTransform = secondButton.transform.Find("Text");
            if (firstTextTransform != null) _firstChoiceText = firstTextTransform.GetComponent<TextMeshProUGUI>();
            if (secondTextTransform != null) _secondChoiceText = secondTextTransform.GetComponent<TextMeshProUGUI>();

            // 宿敌回响（有活跃宿敌时显示）
            string nemesisLine = string.Empty;
            try
            {
                if (ModeGNemesisPersistence.HasActiveNemesis())
                {
                    ModeGNemesisPersistence.NemesisRecordDto nemesis = ModeGNemesisPersistence.LoadOrInit();
                    if (nemesis != null && !string.IsNullOrEmpty(nemesis.bossPresetKey))
                    {
                        nemesisLine = L10n.T("宿敌回响：", "Nemesis echo: ")
                            + ModeGEncounterVariation.GetManagedBossDisplayName(nemesis.bossPresetKey);
                    }
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(nemesisLine))
            {
                ZombieModeUIHelper.CreateText("Nemesis", st, nemesisLine,
                    17f, new Vector2(0f, -62f), new Vector2(ModalWidth - 80f, 28f),
                    TextAlignmentOptions.Center, new Color(1f, 0.55f, 0f, 1f));
            }

            // 下一枚印章目标（契约图鉴进度；宿敌行存在时下移）
            try
            {
                string sealLine = ModeGRecapPanel.ComposeEntrySealLine();
                if (!string.IsNullOrEmpty(sealLine))
                {
                    ZombieModeUIHelper.CreateText("SealGoal", st, sealLine,
                        15f, new Vector2(0f, string.IsNullOrEmpty(nemesisLine) ? -62f : -96f),
                        new Vector2(ModalWidth - 80f, 26f),
                        TextAlignmentOptions.Center, ZombieModeUIHelper.TextSecondaryColor);
                }
            }
            catch { /* 呈现失败不影响确认页 */ }

            // 强制披露（规格 §3.1）：死亡损失遵循当前地图规则 + 高 Resolve 备装建议。
            // 两行都必须在扣除入场物品前对玩家可见，不得省略。
            ZombieModeUIHelper.CreateText("Disclosure", st,
                L10n.T("BossRush_ModeG_Entry_DeathRule") + "\n"
                    + L10n.T("BossRush_ModeG_Entry_LoadoutHint"),
                14f, new Vector2(0f, -130f), new Vector2(ModalWidth - 80f, 40f),
                TextAlignmentOptions.Center, ZombieModeUIHelper.TextSecondaryColor);

            // 立即迎战 / 放弃
            Button startButton = ZombieModeUIHelper.CreateButton(
                "Start", st, L10n.T("立即迎战", "Fight Now"),
                new Vector2(0.5f, 0.5f), new Vector2(-120f, -180f), new Vector2(220f, 56f),
                ZombieModeUIHelper.SuccessColor, 22f, new Vector2(200f, 48f),
                () => ConfirmAndStart(host), true);
            ZombieModeUIHelper.ApplyButtonColors(startButton,
                ZombieModeUIHelper.SuccessColor, ZombieModeUIHelper.SuccessHoverColor,
                ZombieModeUIHelper.DisabledColor);

            Button cancelButton = ZombieModeUIHelper.CreateButton(
                "Cancel", st, L10n.T("放弃挑战", "Abandon"),
                new Vector2(0.5f, 0.5f), new Vector2(120f, -180f), new Vector2(220f, 56f),
                ZombieModeUIHelper.DangerColor, 22f, new Vector2(200f, 48f),
                CloseModal, true);
            ZombieModeUIHelper.ApplyButtonColors(cancelButton,
                ZombieModeUIHelper.DangerColor, ZombieModeUIHelper.DangerHoverColor,
                ZombieModeUIHelper.DisabledColor);

            try
            {
                _inputLease = ZombieModeUIHelper.ClaimModalInput(root, "ModeGConfirmPage");
                if (_inputLease == null || !ZombieModeUIHelper.IsModalInputPaused)
                {
                    throw new InvalidOperationException("Mode G confirmation could not acquire modal pause");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 确认页输入占用失败: " + e.Message);
                CloseModal();
                return false;
            }
            return true;
        }

        private static string BuildChoiceLabel(ModeGFateContract.ContractDef def, bool selected)
        {
            string marker = selected ? "▶ " : string.Empty;
            return marker + def.GetDisplayName() + "\n<size=13>" + def.GetDescription() + "</size>";
        }

        private void SelectCandidate(int index)
        {
            if (_modalPreview == null || index < 0
                || _modalPreview.contractCandidateIds == null
                || index >= _modalPreview.contractCandidateIds.Length)
            {
                return;
            }
            try
            {
                _selectedCandidateIndex = index;
                if (_firstChoiceText != null)
                {
                    _firstChoiceText.text = BuildChoiceLabel(
                        ModeGFateContract.GetById(_modalPreview.contractCandidateIds[0]), index == 0);
                }
                if (_secondChoiceText != null)
                {
                    _secondChoiceText.text = BuildChoiceLabel(
                        ModeGFateContract.GetById(_modalPreview.contractCandidateIds[1]), index == 1);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 契约选择异常: " + e.Message);
            }
        }

        /// <summary>
        /// 确认：记录所选契约后唯一调用 TryStartModeG()。
        /// </summary>
        private void ConfirmAndStart(ModBehaviour host)
        {
            try
            {
                if (_modalPreview == null || _selectedCandidateIndex < 0
                    || _selectedCandidateIndex >= _modalPreview.contractCandidateIds.Length)
                {
                    host.ShowMessage(L10n.T("请先选择一个宿命契约。", "Choose a Fate Contract first."));
                    return;
                }
                if (_modalPreview != null && _modalPreview.contractCandidateIds != null
                    && _selectedCandidateIndex >= 0
                    && _selectedCandidateIndex < _modalPreview.contractCandidateIds.Length)
                {
                    host.SetModeGSelectedContractId(_modalPreview.contractCandidateIds[_selectedCandidateIndex]);
                }
                _confirmed = true;
                LastConfirmationAttemptedStart = true;
                CloseModal();
                bool started = host.TryStartModeG();
                if (!started)
                {
                    host.TryRefundModeGPendingPrepaidTicket();
                }
                else
                {
                    BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] ConfirmAndStart 异常: " + e.Message);
                CloseModal();
            }
        }

        private void CloseModal()
        {
            bool wasConfirmed = _confirmed;
            try
            {
                if (_inputLease != null)
                {
                    _inputLease.Release();
                    _inputLease = null;
                }
            }
            catch { }
            try
            {
                if (_modalRoot != null) UnityEngine.Object.Destroy(_modalRoot);
            }
            catch { }
            _modalRoot = null;
            _modalPreview = null;
            ModBehaviour entryHost = _entryHost;
            _entryHost = null;
            _firstChoiceText = null;
            _secondChoiceText = null;
            if (!wasConfirmed)
            {
                // Map selection may already have charged the ticket; direct teleport has no
                // prepaid ownership and therefore remains a no-op here.
                try
                {
                    if (entryHost != null) entryHost.TryRefundModeGPendingPrepaidTicket();
                }
                catch (Exception refundException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 关闭确认页退回预扣船票失败: "
                        + refundException.Message);
                }
            }
            if (ReferenceEquals(_activeConfirmation, this)) _activeConfirmation = null;
            if (_autoPresenter)
            {
                _autoPresenter = false;
                try { UnityEngine.Object.Destroy(gameObject); }
                catch (Exception destroyException)
                {
                    ModBehaviour.DevLog("[ModeG] [WARNING] 自动确认页销毁失败: "
                        + destroyException.Message);
                }
            }
        }

        #endregion

        protected override void OnDestroy()
        {
            // 清理顺序不变：先关自己的确认页（释放输入租约 + 退回预扣船票），
            // 再交回基类。官方 InteractableBase.OnDestroy 是 protected virtual
            // （Interacting 时 StopInteract），此前用 new 隐藏它，一旦本组件挂到
            // 带碰撞体的实体上，销毁时官方交互就停不下来。
            try { CloseModal(); } catch { /* no-throw */ }
            try { base.OnDestroy(); } catch { /* no-throw */ }
        }
    }

    /// <summary>
    /// Mode G 局内「放弃挑战」确认页（短命 presenter，形态复刻入场确认页的 auto-presenter）。
    ///
    /// 契约：
    /// - 只由 ModeGEntry 的快捷键轮询创建，run 进行中场内没有可交互实体（IsModeGEntryBlocked
    ///   会挡掉 ModeGInteractable），因此不做成 InteractableBase；
    /// - 与入场确认页共用 ClaimModalInput 的时停语义，同一时刻只允许一个实例；
    /// - 确认分支唯一调用 module.End(ModeGExitReason.ManualExit)，连胜清零由
    ///   ModeGCleanupController 的既有 ManualExit 分支消费，本类不碰任何存档；
    /// - 放弃不退还船票与信物（既定规则），页面必须强制披露。
    /// </summary>
    internal sealed class ModeGAbandonPresenter : MonoBehaviour
    {
        private const float ModalWidth = 720f;
        private const float ModalHeight = 380f;

        private static ModeGAbandonPresenter _active;

        private GameObject _modalRoot;
        private ZombieModeUIHelper.ModalInputLease _inputLease;
        private ModeGRuntimeModule _module;

        /// <summary>确认页是否已打开（轮询侧防重入）。</summary>
        internal static bool IsOpen
        {
            get { return _active != null && _active._modalRoot != null; }
        }

        /// <summary>打开弃局确认页；已有实例或缺少运行中的 run 时返回 false。</summary>
        internal static bool TryOpen(ModeGRuntimeModule module)
        {
            if (module == null || _active != null) return false;
            GameObject host = null;
            try
            {
                ModeGRunState state = module.State;
                if (state == null || !state.IsActive) return false;

                host = new GameObject("ModeG_AbandonConfirmPresenter");
                UnityEngine.Object.DontDestroyOnLoad(host);
                ModeGAbandonPresenter presenter = host.AddComponent<ModeGAbandonPresenter>();
                presenter._module = module;
                _active = presenter;
                return presenter.OpenPage();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 放弃确认页创建失败: " + e.Message);
                _active = null;
                try { if (host != null) UnityEngine.Object.Destroy(host); }
                catch { /* 宿主已被销毁：清理路径不再二次报错 */ }
                return false;
            }
        }

        private bool OpenPage()
        {
            GameObject root = new GameObject("ModeG_AbandonPage");
            UnityEngine.Object.DontDestroyOnLoad(root);
            _modalRoot = root;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.ModeGEntry;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            root.AddComponent<GraphicRaycaster>();

            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeG_Abandon", root.transform, new Vector2(ModalWidth, ModalHeight),
                ZombieModeUIHelper.DangerColor);
            Transform st = surface.transform;

            ZombieModeUIHelper.CreateText("Title", st,
                L10n.T("放弃宿命回响挑战？", "Abandon the Fate Echo run?"),
                26f, new Vector2(0f, 118f), new Vector2(ModalWidth - 80f, 44f),
                TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);

            // §3.1 强制披露：放弃的全部代价一次讲清，不得只写「确认放弃」
            ZombieModeUIHelper.CreateText("Disclosure", st,
                L10n.T(
                    "放弃后本局进度作废：已消耗的船票与信物不返还，契约连胜清零。",
                    "Abandoning voids this run: the ticket and relic are not refunded, "
                    + "and your contract win streak resets."),
                16f, new Vector2(0f, 40f), new Vector2(ModalWidth - 100f, 70f),
                TextAlignmentOptions.Center, ZombieModeUIHelper.TextSecondaryColor);

            Button keepButton = ZombieModeUIHelper.CreateButton(
                "Keep", st, L10n.T("继续战斗", "Keep Fighting"),
                new Vector2(0.5f, 0.5f), new Vector2(-120f, -110f), new Vector2(220f, 56f),
                ZombieModeUIHelper.SuccessColor, 20f, new Vector2(200f, 48f),
                Close, true);
            ZombieModeUIHelper.ApplyButtonColors(keepButton,
                ZombieModeUIHelper.SuccessColor, ZombieModeUIHelper.SuccessHoverColor,
                ZombieModeUIHelper.DisabledColor);

            Button abandonButton = ZombieModeUIHelper.CreateButton(
                "Abandon", st, L10n.T("确认放弃", "Abandon Run"),
                new Vector2(0.5f, 0.5f), new Vector2(120f, -110f), new Vector2(220f, 56f),
                ZombieModeUIHelper.DangerColor, 20f, new Vector2(200f, 48f),
                ConfirmAbandon, true);
            ZombieModeUIHelper.ApplyButtonColors(abandonButton,
                ZombieModeUIHelper.DangerColor, ZombieModeUIHelper.DangerHoverColor,
                ZombieModeUIHelper.DisabledColor);

            try
            {
                _inputLease = ZombieModeUIHelper.ClaimModalInput(root, "ModeGAbandonPage");
                if (_inputLease == null || !ZombieModeUIHelper.IsModalInputPaused)
                {
                    throw new InvalidOperationException("Mode G abandon page could not acquire modal pause");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 放弃确认页输入占用失败: " + e.Message);
                Close();
                return false;
            }
            return true;
        }

        private void ConfirmAbandon()
        {
            ModeGRuntimeModule module = _module;
            Close();
            try
            {
                // End 幂等：终局横幅、连胜清零与关停由 End -> Cleanup -> UpdateModeG 的既有链承接
                if (module != null) module.End(ModeGExitReason.ManualExit);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] 放弃挑战终局失败: " + e.Message);
            }
        }

        private void Close()
        {
            try
            {
                if (_inputLease != null)
                {
                    _inputLease.Release();
                    _inputLease = null;
                }
            }
            catch { /* 租约已被宿主回收：继续走完销毁，不得中断 */ }
            try { if (_modalRoot != null) UnityEngine.Object.Destroy(_modalRoot); }
            catch { /* 面板已随场景销毁：置空即可 */ }
            _modalRoot = null;
            _module = null;
            if (ReferenceEquals(_active, this)) _active = null;
            try { UnityEngine.Object.Destroy(gameObject); }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 放弃确认页销毁失败: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            // 与 Close 同款兜底：presenter 被外部销毁时也必须还掉输入租约，否则时停不解除
            try
            {
                if (_inputLease != null)
                {
                    _inputLease.Release();
                    _inputLease = null;
                }
            }
            catch { /* 租约已被宿主回收 */ }
            try { if (_modalRoot != null) UnityEngine.Object.Destroy(_modalRoot); }
            catch { /* 面板已随场景销毁 */ }
            _modalRoot = null;
            _module = null;
            if (ReferenceEquals(_active, this)) _active = null;
        }
    }
}
