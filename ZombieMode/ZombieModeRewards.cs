using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BossRush.Utils;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string ZombieModeAttributeMaxHealthKey = "MaxHealth";
        private const string ZombieModeAttributeMoveSpeedKey = "MoveSpeed";
        private const string ZombieModeAttributeWalkSpeedKey = "WalkSpeed";
        private const string ZombieModeAttributeRunSpeedKey = "RunSpeed";
        private const string ZombieModeAttributeMeleeDamageKey = "MeleeDamageMultiplier";
        private const string ZombieModeAttributeRangedDamageKey = "GunDamageMultiplier";
        private const string ZombieModeAttributeReloadSpeedKey = "ReloadSpeedMultiplier";
        private const string ZombieModeAttributeDamageReductionKey = "ElementFactor_Physics";
        private const int ZombieModeContractGearDealMinQuality = 5;

        private GameObject zombieModeRewardUiRoot;
    }
    public sealed class ZombieModeRewardSelectionView : MonoBehaviour
    {
        // ==================== 配色方案 ====================
        private static readonly Color HeaderColor = new Color(0.09f, 0.115f, 0.13f, 0.12f);
        private static readonly Color AccentLineColor = new Color(0.44f, 0.82f, 0.92f, 0.80f);

        // 奖励卡片
        private static readonly Color RewardCardColor = new Color(0.12f, 0.16f, 0.22f, 0.62f);
        private static readonly Color RewardCardAccentColor = new Color(0.44f, 0.82f, 0.92f, 0.95f);
        private static readonly Color RewardCardHoverColor = new Color(0.18f, 0.24f, 0.32f, 0.90f);

        // 免费刷新
        private static readonly Color FreeRefreshColor = new Color(0.14f, 0.36f, 0.28f, 1.00f);
        private static readonly Color FreeRefreshHoverColor = new Color(0.20f, 0.48f, 0.36f, 1.00f);
        private static readonly Color FreeRefreshDisabledColor = new Color(0.18f, 0.20f, 0.20f, 0.70f);
        // 付费刷新
        private static readonly Color PaidRefreshColor = new Color(0.38f, 0.30f, 0.14f, 1.00f);
        private static readonly Color PaidRefreshHoverColor = new Color(0.50f, 0.40f, 0.20f, 1.00f);
        private static readonly Color RestOptionColor = new Color(0.10f, 0.20f, 0.24f, 0.96f);
        private static readonly Color RestOptionHoverColor = new Color(0.16f, 0.36f, 0.40f, 1.00f);
        private static readonly Color RestOptionSelectedColor = new Color(0.12f, 0.52f, 0.48f, 1.00f);

        private static readonly Color InfoTextColor = new Color(0.72f, 0.78f, 0.86f, 0.95f);

        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;
        private bool restEditorExpanded;
        private int pendingRestSeconds;
        private TextMeshProUGUI restTitleText;

        public void Initialize(int newRunId, ModBehaviour newOwner, bool newRestEditorExpanded)
        {
            runId = newRunId;
            owner = newOwner;
            restEditorExpanded = newRestEditorExpanded;
            pendingRestSeconds = owner != null
                ? owner.GetZombieModeSelectedPreparationDuration(runId)
                : 45;
            Build();
            ClaimInputAndPause();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            bool bossNode = owner.IsZombieModeBossRewardNode(runId);
            // CanvasScaler 已经负责不同分辨率缩放；这里使用稳定的参考尺寸，
            // 避免把物理像素宽高直接当成 1920x1080 参考坐标后造成布局忽大忽小。
            float panelWidth = 840f;
            float panelHeight = bossNode
                ? (restEditorExpanded ? 565f : 510f)
                : (restEditorExpanded ? 480f : 430f);
            GameObject panel = ZombieModeUIHelper.CreateModalSurface(
                "Panel",
                transform,
                new Vector2(panelWidth, panelHeight),
                RewardCardAccentColor);

            // ── 标题栏 ──
            float yPos = 0f;
            float headerH = 56f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = HeaderColor;

            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", header.transform,
                owner.GetZombieModeRewardTitle(runId), 26,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);
            titleText.fontStyle = FontStyles.Bold;
            yPos += headerH;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -yPos), 2f, AccentLineColor);
            yPos += 6f;

            // ── 信息栏 ──
            float infoH = 30f;
            ZombieModeUIHelper.CreateText("Info", panel.transform,
                string.Format(
                    L10n.T("BossRush_ZombieMode_Reward_Info"),
                    owner.GetZombieModePurificationPoints(runId),
                    owner.GetZombieModeRewardFreeRefreshes(runId),
                    owner.GetZombieModeRewardPaidRefreshCost(runId).ToString("N0")),
                16,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + infoH * 0.5f)), new Vector2(-40f, infoH),
                TextAlignmentOptions.Center, InfoTextColor);
            yPos += infoH + 10f;

            float previewH = 34f;
            ZombieModeUIHelper.CreateHighlightBar("NextWavePreview", panel.transform,
                owner.GetZombieModeNextWavePreviewText(runId), 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + previewH * 0.5f)), new Vector2(-40f, previewH),
                TextAlignmentOptions.Center, RewardCardAccentColor,
                new Color(0.055f, 0.12f, 0.13f, 0.22f));
            yPos += previewH + 8f;

            // ── 奖励卡片 ──
            IList<ZombieModeRewardType> options = owner.GetZombieModeRewardOptions(runId);
            float cardW = bossNode ? 220f : 240f;
            float cardH = bossNode ? 88f : 104f;

            if (bossNode)
            {
                // 4 选项：2×2 网格
                Vector2[] positions = new Vector2[]
                {
                    new Vector2(-120f, -(yPos + cardH * 0.5f)),
                    new Vector2(120f, -(yPos + cardH * 0.5f)),
                    new Vector2(-120f, -(yPos + cardH + 12f + cardH * 0.5f)),
                    new Vector2(120f, -(yPos + cardH + 12f + cardH * 0.5f))
                };
                for (int i = 0; i < options.Count && i < positions.Length; i++)
                {
                    CreateRewardCard("Reward_" + options[i].ToString(), panel.transform,
                        owner.GetZombieModeRewardDisplayText(runId, options[i]),
                        positions[i], new Vector2(cardW, cardH), options[i]);
                }
                yPos += cardH * 2f + 12f + 16f;
            }
            else
            {
                // 3 选项：横排
                float totalW = cardW * options.Count + 16f * (options.Count - 1);
                float startX = -totalW * 0.5f + cardW * 0.5f;
                for (int i = 0; i < options.Count; i++)
                {
                    float x = startX + i * (cardW + 16f);
                    CreateRewardCard("Reward_" + options[i].ToString(), panel.transform,
                        owner.GetZombieModeRewardDisplayText(runId, options[i]),
                        new Vector2(x, -(yPos + cardH * 0.5f)), new Vector2(cardW, cardH), options[i]);
                }
                yPos += cardH + 16f;
            }

            // ── 休息时间（默认折叠，仅显示当前值与“修改”） ──
            float restH = restEditorExpanded ? 116f : 48f;
            GameObject restPanel = ZombieModeUIHelper.CreateRect("RestPanel", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + restH * 0.5f)), new Vector2(-40f, restH), new Vector2(0.5f, 0.5f));
            Image restPanelImage = restPanel.AddComponent<Image>();
            restPanelImage.color = new Color(0.055f, 0.15f, 0.17f, 0.32f);
            restPanelImage.raycastTarget = false;
            restTitleText = ZombieModeUIHelper.CreateText("RestTitle", restPanel.transform,
                string.Format(
                    L10n.T("BossRush_ZombieMode_Reward_RestTitle"),
                    pendingRestSeconds),
                16,
                restEditorExpanded ? new Vector2(0f, 1f) : new Vector2(0f, 0.5f),
                restEditorExpanded ? new Vector2(0.72f, 1f) : new Vector2(0.72f, 0.5f),
                restEditorExpanded ? new Vector2(18f, -19f) : new Vector2(18f, 0f),
                restEditorExpanded ? new Vector2(-38f, 28f) : new Vector2(-38f, 34f),
                TextAlignmentOptions.MidlineLeft,
                InfoTextColor);

            if (!restEditorExpanded)
            {
                Button editButton = ZombieModeUIHelper.CreateButton(
                    "RestEdit", restPanel.transform,
                    L10n.T("BossRush_ZombieMode_Reward_RestEdit"),
                    new Vector2(1f, 0.5f), new Vector2(-68f, 0f),
                    new Vector2(104f, 34f), RestOptionColor, 15,
                    new Vector2(92f, 28f), null, true);
                ZombieModeUIHelper.ApplyButtonColors(editButton, RestOptionColor, RestOptionHoverColor, FreeRefreshDisabledColor);
                editButton.onClick.AddListener(delegate
                {
                    if (owner != null)
                    {
                        owner.OpenZombieModePreparationDurationEditor(runId);
                    }
                });
            }
            else
            {
                float sliderWidth = Mathf.Clamp(panelWidth - 250f, 390f, 500f);
                GameObject sliderObject = ZombieModeUIHelper.CreateRect("RestDurationSlider", restPanel.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-120f, -66f), new Vector2(sliderWidth, 24f), new Vector2(0.5f, 0.5f));
                Slider slider = sliderObject.AddComponent<Slider>();
                slider.minValue = 1f;
                slider.maxValue = 20f;
                slider.wholeNumbers = true;
                slider.direction = Slider.Direction.LeftToRight;

                GameObject track = ZombieModeUIHelper.CreateRect("Track", sliderObject.transform,
                    new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                    Vector2.zero, new Vector2(0f, 8f), new Vector2(0.5f, 0.5f));
                Image trackImage = track.AddComponent<Image>();
                trackImage.color = new Color(0.06f, 0.09f, 0.12f, 0.95f);

                GameObject fillArea = ZombieModeUIHelper.CreateRect("FillArea", sliderObject.transform,
                    new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                    Vector2.zero, new Vector2(-12f, 8f), new Vector2(0.5f, 0.5f));
                GameObject fill = ZombieModeUIHelper.CreateRect("Fill", fillArea.transform,
                    new Vector2(0f, 0f), new Vector2(1f, 1f),
                    Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
                Image fillImage = fill.AddComponent<Image>();
                fillImage.color = RestOptionSelectedColor;
                slider.fillRect = fill.GetComponent<RectTransform>();

                GameObject handleArea = ZombieModeUIHelper.CreateRect("HandleArea", sliderObject.transform,
                    Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12f, 0f), new Vector2(0.5f, 0.5f));
                GameObject handle = ZombieModeUIHelper.CreateRect("Handle", handleArea.transform,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    Vector2.zero, new Vector2(20f, 20f), new Vector2(0.5f, 0.5f));
                Image handleImage = handle.AddComponent<Image>();
                handleImage.color = RewardCardAccentColor;
                slider.handleRect = handle.GetComponent<RectTransform>();
                slider.targetGraphic = handleImage;
                slider.value = Mathf.Clamp(Mathf.RoundToInt(pendingRestSeconds / 15f), 1, 20);
                slider.onValueChanged.AddListener(delegate(float value)
                {
                    pendingRestSeconds = Mathf.Clamp(Mathf.RoundToInt(value) * 15, 15, 300);
                    UpdatePendingRestDurationText();
                });

                ZombieModeUIHelper.CreateText("RestMin", restPanel.transform,
                    string.Format(L10n.T("BossRush_ZombieMode_Reward_RestOption"), 15), 12,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-120f - sliderWidth * 0.5f, -94f),
                    new Vector2(76f, 20f), TextAlignmentOptions.MidlineLeft, InfoTextColor);
                ZombieModeUIHelper.CreateText("RestMax", restPanel.transform,
                    string.Format(L10n.T("BossRush_ZombieMode_Reward_RestOption"), 300), 12,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-120f + sliderWidth * 0.5f, -94f),
                    new Vector2(86f, 20f), TextAlignmentOptions.MidlineRight, InfoTextColor);

                Button applyButton = ZombieModeUIHelper.CreateButton(
                    "RestApply", restPanel.transform,
                    L10n.T("BossRush_ZombieMode_Reward_RestApply"),
                    new Vector2(1f, 1f), new Vector2(-66f, -66f),
                    new Vector2(104f, 36f), RestOptionSelectedColor, 15,
                    new Vector2(92f, 30f), null, true);
                ZombieModeUIHelper.ApplyButtonColors(applyButton, RestOptionSelectedColor, RestOptionHoverColor, FreeRefreshDisabledColor);
                applyButton.onClick.AddListener(delegate
                {
                    if (owner != null)
                    {
                        owner.SetZombieModePreparationDuration(runId, pendingRestSeconds);
                    }
                });
            }
            yPos += restH + 8f;

            // ── 分隔线 ──
            ZombieModeUIHelper.CreateSeparator("Sep", panel.transform,
                new Vector2(0.08f, 1f), new Vector2(0.92f, 1f),
                new Vector2(0f, -yPos), 1f, new Color(0.25f, 0.35f, 0.50f, 0.35f));
            yPos += 14f;

            // ── 刷新按钮行（固定底部） ──
            GameObject refreshRow = ZombieModeUIHelper.CreateRect("RefreshRow", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + 28f)), new Vector2(-40f, 56f), new Vector2(0.5f, 0.5f));
            HorizontalLayoutGroup refreshLayout = refreshRow.AddComponent<HorizontalLayoutGroup>();
            refreshLayout.spacing = 30f;
            refreshLayout.childAlignment = TextAnchor.MiddleCenter;
            refreshLayout.childControlWidth = false;
            refreshLayout.childControlHeight = false;
            refreshLayout.childForceExpandWidth = false;
            refreshLayout.childForceExpandHeight = false;

            bool hasFreeRefresh = owner.GetZombieModeRewardFreeRefreshes(runId) > 0;
            CreateStyledRefreshButton(refreshRow.transform, "FreeRefresh",
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshFree"), owner.GetZombieModeRewardFreeRefreshes(runId)),
                FreeRefreshColor, FreeRefreshHoverColor, FreeRefreshDisabledColor,
                hasFreeRefresh, true, false);
            int paidRefreshCost = owner.GetZombieModeRewardPaidRefreshCost(runId);
            bool canAffordPaidRefresh = owner.GetZombieModePurificationPoints(runId) >= paidRefreshCost;
            CreateStyledRefreshButton(refreshRow.transform, "PaidRefresh",
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshPaid"), paidRefreshCost.ToString("N0")),
                PaidRefreshColor, PaidRefreshHoverColor, FreeRefreshDisabledColor,
                true, canAffordPaidRefresh, true);
        }

        private void UpdatePendingRestDurationText()
        {
            if (restTitleText != null)
            {
                restTitleText.text = string.Format(
                    L10n.T("BossRush_ZombieMode_Reward_RestTitle"),
                    pendingRestSeconds);
            }
        }

        private void CreateRewardCard(string name, Transform parent, string text, Vector2 position, Vector2 size, ZombieModeRewardType rewardType)
        {
            // ── 卡片底板 ──
            GameObject card = ZombieModeUIHelper.CreateRect(name, parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                position, size, new Vector2(0.5f, 0.5f));
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = RewardCardColor;

            // ── 顶部高亮条 ──
            GameObject sideAccent = ZombieModeUIHelper.CreateRect("SideAccent", card.transform,
                new Vector2(0f, 0.18f), new Vector2(0f, 0.82f),
                new Vector2(2f, 0f), new Vector2(4f, 0f), new Vector2(0f, 0.5f));
            Image sideAccentImage = sideAccent.AddComponent<Image>();
            sideAccentImage.color = RewardCardAccentColor;
            sideAccentImage.raycastTarget = false;

            // ── 文本 ──
            ZombieModeUIHelper.CreateText("Text", card.transform, text, 18,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, -4f), new Vector2(-18f, -14f),
                TextAlignmentOptions.Center, Color.white);

            // ── 按钮 ──
            Button button = card.AddComponent<Button>();
            ZombieModeUIHelper.ApplyButtonColors(
                button,
                RewardCardColor,
                RewardCardHoverColor,
                RewardCardColor * 0.6f);

            button.onClick.AddListener(delegate
            {
                if (owner != null)
                {
                    owner.SelectZombieModeReward(runId, rewardType);
                }
            });
        }

        private void CreateStyledRefreshButton(Transform parent, string name, string text,
            Color baseColor, Color hoverColor, Color disabledColor,
            bool interactable, bool affordable, bool paid)
        {
            float btnW = 240f;
            float btnH = 44f;
            Color visibleColor = interactable
                ? (affordable ? baseColor : new Color(0.30f, 0.18f, 0.14f, 0.92f))
                : disabledColor;
            Color visibleHoverColor = affordable ? hoverColor : new Color(0.42f, 0.24f, 0.18f, 1f);
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, text,
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(btnW, btnH),
                visibleColor, 16,
                new Vector2(btnW - 14f, btnH - 8f),
                null, interactable);

            LayoutElement layoutElement = button.gameObject.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = button.gameObject.AddComponent<LayoutElement>();
            }
            layoutElement.minWidth = btnW;
            layoutElement.preferredWidth = btnW;
            layoutElement.minHeight = btnH;
            layoutElement.preferredHeight = btnH;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            ZombieModeUIHelper.ApplyButtonColors(button, visibleColor, visibleHoverColor, disabledColor);

            if (interactable)
            {
                bool capturedPaid = paid;
                button.onClick.AddListener(delegate
                {
                    if (owner != null)
                    {
                        owner.RefreshZombieModeRewardSelection(runId, capturedPaid);
                    }
                });
            }
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "RewardSelection");
        }

        private void RestoreInputState()
        {
            if (inputLease != null)
            {
                inputLease.Release();
                inputLease = null;
            }
        }

        private void OnDestroy()
        {
            RestoreInputState();
        }
    }

    public sealed class ZombieModeTemporaryNpcInteractable : InteractableBase
    {
        private int runId;
        private string serviceType = string.Empty;

        public void Initialize(int newRunId, string newServiceType)
        {
            runId = newRunId;
            serviceType = newServiceType ?? string.Empty;
            ApplyInteractName();
        }

        protected override void Awake()
        {
            ApplyInteractName();
            try
            {
                interactCollider = GetComponent<Collider>();
                interactMarkerOffset = new Vector3(0f, 1.4f, 0f);
                NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[ZombieMode] TemporaryNpc");
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc Awake collider 获取失败: " + e.Message);
            }

            try { base.Awake(); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.Awake 失败: " + e.Message); }
        }

        protected override void Start()
        {
            try { base.Start(); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.Start 失败: " + e.Message); }
            ApplyInteractName();
        }

        protected override bool IsInteractable()
        {
            return ModBehaviour.Instance != null && runId > 0;
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.OnInteractStart 失败: " + e.Message);
            }

            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.OpenZombieModeTemporaryNpcServiceUi(runId, serviceType);
            }
        }

        protected override void OnTimeOut()
        {
            try
            {
                base.OnTimeOut();
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.OnTimeOut 失败: " + e.Message);
            }
        }

        private void ApplyInteractName()
        {
            string key = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_InteractNurse"
                : "BossRush_ZombieMode_Npc_InteractMerchant";
            try
            {
                overrideInteractName = true;
                _overrideInteractNameKey = key;
                InteractName = key;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc InteractName 设置失败: " + e.Message);
            }
        }
    }

    public sealed class ZombieModeTemporaryNpcServiceView : MonoBehaviour
    {
        private int runId;
        private ModBehaviour owner;
        private string serviceType = string.Empty;
        private ZombieModeUIHelper.ModalInputLease inputLease;

        public void Initialize(int newRunId, ModBehaviour newOwner, string newServiceType)
        {
            runId = newRunId;
            owner = newOwner;
            serviceType = newServiceType ?? string.Empty;
            Build();
            ClaimInputAndPause();
        }

        private void Build()
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30500;
            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            Color serviceAccent = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? new Color(0.30f, 0.76f, 0.58f, 1f)
                : new Color(0.82f, 0.66f, 0.30f, 1f);
            GameObject panel = ZombieModeUIHelper.CreateModalSurface(
                "Panel",
                transform,
                new Vector2(820f, 620f),
                serviceAccent);

            // ── 标题栏 ──
            float headerH = 64f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = ZombieModeUIHelper.ModalHeaderColor;

            string titleKey = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_TempNurse"
                : "BossRush_ZombieMode_Npc_TempMerchant";
            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", header.transform,
                L10n.T(titleKey), 26,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(700f, 60f),
                TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);
            titleText.fontStyle = FontStyles.Bold;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -headerH), 2f, serviceAccent);

            // ── 副标题 ──
            int purificationPoints = owner != null ? owner.GetZombieModePurificationPoints(runId) : 0;
            string subtitleKey = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_NurseSubtitle"
                : "BossRush_ZombieMode_Npc_MerchantSubtitle";
            ZombieModeUIHelper.CreateText(
                "Subtitle",
                panel.transform,
                string.Format(L10n.T(subtitleKey), purificationPoints.ToString("N0")),
                15,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH + 22f)), new Vector2(-40f, 30f),
                TextAlignmentOptions.Center,
                new Color(0.62f, 0.70f, 0.82f, 0.90f));

            Transform body = CreateScrollableBody(panel.transform);
            if (string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal))
            {
                BuildNurseServices(body);
            }
            else
            {
                BuildMerchantStock(body);
            }

            CreateCloseButton(panel.transform);
        }

        private Transform CreateScrollableBody(Transform parent)
        {
            // 使用 anchor-based 布局适配新的分层面板
            // 标题栏64 + 装饰线2 + 副标题30 + 间距 = 约 110px 顶部偏移
            // 底部留 70px 给关闭按钮
            GameObject body = ZombieModeUIHelper.CreateRect(
                "Body",
                parent,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f));
            RectTransform bodyRect = body.GetComponent<RectTransform>();
            bodyRect.offsetMin = new Vector2(20f, 70f);   // 底部留给关闭按钮
            bodyRect.offsetMax = new Vector2(-20f, -110f); // 顶部留给标题栏+副标题

            Image bodyImage = body.AddComponent<Image>();
            bodyImage.color = new Color(0.06f, 0.08f, 0.12f, 0.14f);

            ScrollRect scrollRect = body.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            GameObject viewport = ZombieModeUIHelper.CreateRect(
                "Viewport",
                body.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.offsetMin = new Vector2(10f, 10f);
            viewportRect.offsetMax = new Vector2(-10f, -10f);
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            Mask viewportMask = viewport.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            GameObject content = ZombieModeUIHelper.CreateRect(
                "Content",
                viewport.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f));
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            return content.transform;
        }

        private void BuildMerchantStock(Transform parent)
        {
            GridLayoutGroup grid = parent.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.cellSize = new Vector2(168f, 102f);
            grid.spacing = new Vector2(12f, 12f);
            grid.childAlignment = TextAnchor.UpperCenter;

            ContentSizeFitter fitter = parent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ZombieModeNpcCatalog.MerchantStockEntry[] stock = owner != null
                ? owner.GetZombieModeMerchantStock(runId, serviceType)
                : new ZombieModeNpcCatalog.MerchantStockEntry[0];
            int availablePurificationPoints = owner != null ? owner.GetZombieModePurificationPoints(runId) : 0;
            for (int i = 0; i < stock.Length && i < ZombieModeNpcCatalog.MaxMerchantStockButtons; i++)
            {
                ZombieModeNpcCatalog.MerchantStockEntry entry = stock[i];
                int index = i;
                int remaining = owner != null ? owner.GetZombieModeNpcServiceRemaining(runId, serviceType, index) : 0;
                int price = owner != null ? owner.GetZombieModeNpcServicePrice(runId, entry.BasePrice) : entry.BasePrice;
                bool affordable = owner != null && availablePurificationPoints >= price;
                string label = L10n.T(entry.DisplayKey) +
                    "\n<size=80%>" + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price) +
                    "  " + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining) + "</size>";
                if (remaining > 0 && !affordable)
                {
                    label += "\n<color=#D98B79><size=75%>" + L10n.T("BossRush_ZombieMode_Notify_NpcServiceNoPoints") + "</size></color>";
                }
                CreateServiceButton(parent, "Merchant_" + i, label, Vector2.zero, remaining > 0, affordable, delegate
                {
                    if (owner != null && owner.TryPurchaseZombieModeMerchantStock(runId, serviceType, index))
                    {
                        Rebuild();
                    }
                });
            }
        }

        private void BuildNurseServices(Transform parent)
        {
            VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 14f;
            layout.padding = new RectOffset(18, 18, 6, 6);

            ContentSizeFitter fitter = parent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ZombieModeNpcCatalog.NurseServiceEntry[] services = owner != null
                ? owner.GetZombieModeNurseServices(runId, serviceType)
                : new ZombieModeNpcCatalog.NurseServiceEntry[0];
            int availablePurificationPoints = owner != null ? owner.GetZombieModePurificationPoints(runId) : 0;
            for (int i = 0; i < services.Length; i++)
            {
                ZombieModeNpcCatalog.NurseServiceEntry entry = services[i];
                int index = i;
                int remaining = owner != null ? owner.GetZombieModeNpcServiceRemaining(runId, serviceType, index) : 0;
                int price = owner != null ? owner.GetZombieModeNpcServicePrice(runId, entry.BasePrice) : entry.BasePrice;
                bool affordable = owner != null && availablePurificationPoints >= price;
                string label = L10n.T(entry.ServiceKey) +
                    "\n<size=80%>" + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price) +
                    "  " + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining) + "</size>";
                if (remaining > 0 && !affordable)
                {
                    label += "\n<color=#D98B79><size=75%>" + L10n.T("BossRush_ZombieMode_Notify_NpcServiceNoPoints") + "</size></color>";
                }
                CreateServiceButton(parent, "Nurse_" + i, label, Vector2.zero, remaining > 0, affordable, delegate
                {
                    if (owner != null && owner.TryUseZombieModeNurseService(runId, serviceType, index))
                    {
                        Rebuild();
                    }
                });
            }
        }

        private void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            Build();
        }

        private void CreateServiceButton(Transform parent, string name, string text, Vector2 position, bool interactable, bool affordable, UnityEngine.Events.UnityAction action)
        {
            Color normalColor = !interactable
                ? new Color(0.14f, 0.14f, 0.14f, 0.70f)
                : (affordable ? new Color(0.12f, 0.18f, 0.26f, 0.60f) : new Color(0.28f, 0.16f, 0.13f, 0.64f));
            Color hoverColor = affordable
                ? new Color(0.18f, 0.28f, 0.38f, 0.90f)
                : new Color(0.40f, 0.23f, 0.18f, 0.88f);
            Color accentColor = !interactable
                ? new Color(0.30f, 0.30f, 0.30f, 0.70f)
                : (affordable ? new Color(0.44f, 0.82f, 0.92f, 0.95f) : new Color(0.86f, 0.48f, 0.34f, 0.92f));

            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(168f, 102f));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            LayoutElement layoutElement = obj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 168f;
            layoutElement.preferredHeight = 102f;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
            Image image = obj.AddComponent<Image>();
            image.color = normalColor;
            Button button = obj.AddComponent<Button>();
            button.interactable = interactable;

            ZombieModeUIHelper.ApplyButtonColors(button, normalColor, hoverColor, normalColor);

            // 顶部高亮条
            GameObject accent = ZombieModeUIHelper.CreateRect(
                "Accent",
                obj.transform,
                new Vector2(0f, 0.20f),
                new Vector2(0f, 0.80f),
                new Vector2(2f, 0f),
                new Vector2(4f, 0f),
                new Vector2(0f, 0.5f));
            Image accentImage = accent.AddComponent<Image>();
            accentImage.color = accentColor;
            accentImage.raycastTarget = false;
            ZombieModeUIHelper.CreateText("Text", obj.transform, text, 14, Vector2.zero, new Vector2(154f, 86f), TextAlignmentOptions.Center, Color.white);
            if (interactable && action != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private void CreateCloseButton(Transform parent)
        {
            Color closeNormal = new Color(0.18f, 0.21f, 0.22f, 1.00f);
            Color closeHover = new Color(0.27f, 0.32f, 0.34f, 1.00f);

            // 固定到面板底部
            Button button = ZombieModeUIHelper.CreateButton(
                "Close",
                parent,
                L10n.T("BossRush_ZombieMode_Npc_Close"),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 36f),
                new Vector2(180f, 44f),
                closeNormal,
                17,
                new Vector2(168f, 36f),
                null,
                true);

            ZombieModeUIHelper.ApplyButtonColors(button, closeNormal, closeHover, closeNormal * 0.6f);

            button.onClick.AddListener(delegate
            {
                RestoreInputState();
                Destroy(gameObject);
            });
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "TemporaryNpcService");
        }

        private void RestoreInputState()
        {
            if (inputLease != null)
            {
                inputLease.Release();
                inputLease = null;
            }
        }

        private void OnDestroy()
        {
            RestoreInputState();
        }

    }
}
