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
        private const float ModalWidth = 780f;
        private const float ModalHeight = 560f;

        private GameObject _modalRoot;
        private ZombieModeUIHelper.ModalInputLease _inputLease;
        private int _selectedCandidateIndex;
        private TextMeshProUGUI _firstChoiceText;
        private TextMeshProUGUI _secondChoiceText;
        private ModeGEntryPreview _modalPreview;

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
                // 生产闸或开发闸（发布闸保持关闭时仅开发构建可见）
                if (!ModeGAvailability.IsProductionReady && !ModeGAvailability.AllowDevTestEntry)
                {
                    return false;
                }
                if (ModeGRuntimeGates.IsModeGEntryBlocked) return false;
                if (!ModeGMapSupportRegistry.IsCombatSceneSupported(
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

                ModBehaviour host = ModBehaviour.Instance;
                if (host == null) return;

                ModeGEntryPreview preview = host.GetOrCreateModeGEntryPreview();
                if (preview == null || preview.contractCandidateIds == null || preview.contractCandidateIds.Length < 2)
                {
                    host.ShowMessage(L10n.T(
                        "宿命回响入口准备失败，请稍后重试。",
                        "Fate Echo entry is not ready. Please try again later."));
                    return;
                }

                OpenConfirmPage(host, preview);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] ModeGInteractable.OnTimeOut 异常: " + e.Message);
            }
        }

        #endregion

        #region Confirm Page（确认页）

        private void OpenConfirmPage(ModBehaviour host, ModeGEntryPreview preview)
        {
            _modalPreview = preview;
            _selectedCandidateIndex = 0;

            GameObject root = new GameObject("ModeG_ConfirmPage");
            UnityEngine.Object.DontDestroyOnLoad(root);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 950;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            root.AddComponent<GraphicRaycaster>();

            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeG_Confirm", root.transform, new Vector2(ModalWidth, ModalHeight),
                new Color(0.72f, 0.53f, 0.04f, 1f));

            Transform st = surface.transform;

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
                    "九波宿敌车轮战 · 三轴反制 · 裸装入场\n消耗 1 船票 + 1 宿命回响信物（胜利返还信物）",
                    "9 waves of nemesis combat · 3 counter axes · naked entry\nCosts 1 ticket + 1 Fate Echo relic (relic refunded on victory)"),
                18f, new Vector2(0f, 148f), new Vector2(ModalWidth - 80f, 72f),
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

            _modalRoot = root;
            try
            {
                _inputLease = ZombieModeUIHelper.ClaimModalInput(root, "ModeGConfirmPage");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 确认页输入占用失败: " + e.Message);
            }
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
                if (_modalPreview != null && _modalPreview.contractCandidateIds != null
                    && _selectedCandidateIndex >= 0
                    && _selectedCandidateIndex < _modalPreview.contractCandidateIds.Length)
                {
                    host.SetModeGSelectedContractId(_modalPreview.contractCandidateIds[_selectedCandidateIndex]);
                }
                CloseModal();
                host.TryStartModeG();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [ERROR] ConfirmAndStart 异常: " + e.Message);
                CloseModal();
            }
        }

        private void CloseModal()
        {
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
            _firstChoiceText = null;
            _secondChoiceText = null;
        }

        #endregion

        private new void OnDestroy()
        {
            try { CloseModal(); } catch { /* no-throw */ }
        }
    }
}
