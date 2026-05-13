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
        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color PanelOuterColor = new Color(0.12f, 0.16f, 0.24f, 0.98f);
        private static readonly Color PanelBorderColor = new Color(0.22f, 0.30f, 0.44f, 0.45f);
        private static readonly Color PanelInnerColor = new Color(0.10f, 0.12f, 0.16f, 0.98f);
        private static readonly Color HeaderColor = new Color(0.14f, 0.20f, 0.32f, 1.00f);
        private static readonly Color AccentLineColor = new Color(0.35f, 0.55f, 0.85f, 0.70f);

        // 奖励卡片
        private static readonly Color RewardCardColor = new Color(0.12f, 0.16f, 0.22f, 0.98f);
        private static readonly Color RewardCardAccentColor = new Color(0.44f, 0.82f, 0.92f, 0.95f);
        private static readonly Color RewardCardHoverColor = new Color(0.18f, 0.24f, 0.32f, 1.00f);

        // 免费刷新
        private static readonly Color FreeRefreshColor = new Color(0.14f, 0.36f, 0.28f, 1.00f);
        private static readonly Color FreeRefreshHoverColor = new Color(0.20f, 0.48f, 0.36f, 1.00f);
        private static readonly Color FreeRefreshDisabledColor = new Color(0.18f, 0.20f, 0.20f, 0.70f);
        // 付费刷新
        private static readonly Color PaidRefreshColor = new Color(0.38f, 0.30f, 0.14f, 1.00f);
        private static readonly Color PaidRefreshHoverColor = new Color(0.50f, 0.40f, 0.20f, 1.00f);

        private static readonly Color InfoTextColor = new Color(0.72f, 0.78f, 0.86f, 0.95f);

        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
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

            // ── 全屏遮罩 ──
            GameObject backdrop = ZombieModeUIHelper.CreateRect("Backdrop", transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, Vector2.zero);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = BackdropColor;
            backdropImage.raycastTarget = true;

            // ── 外框 ──
            GameObject outer = ZombieModeUIHelper.CreateRect("PanelOuter", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(860f, 540f), new Vector2(0.5f, 0.5f));
            Image outerImage = outer.AddComponent<Image>();
            outerImage.color = PanelOuterColor;

            // ── 亮边层 ──
            GameObject borderGlow = ZombieModeUIHelper.CreateRect("BorderGlow", outer.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image borderImg = borderGlow.AddComponent<Image>();
            borderImg.color = PanelBorderColor;

            // ── 主面板 ──
            GameObject panel = ZombieModeUIHelper.CreateRect("Panel", borderGlow.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = PanelInnerColor;

            // ── 标题栏 ──
            float yPos = 0f;
            float headerH = 64f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = HeaderColor;

            ZombieModeUIHelper.CreateText("Title", header.transform,
                owner.GetZombieModeRewardTitle(runId), 26,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, Color.white);
            yPos += headerH;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -yPos), 2f, AccentLineColor);
            yPos += 6f;

            // ── 信息栏 ──
            float infoH = 34f;
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

            // ── 奖励卡片 ──
            IList<ZombieModeRewardType> options = owner.GetZombieModeRewardOptions(runId);
            bool bossNode = owner.IsZombieModeBossRewardNode(runId);
            float cardW = bossNode ? 220f : 240f;
            float cardH = bossNode ? 100f : 120f;

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

            // ── 分隔线 ──
            ZombieModeUIHelper.CreateSeparator("Sep", panel.transform,
                new Vector2(0.08f, 1f), new Vector2(0.92f, 1f),
                new Vector2(0f, -yPos), 1f, new Color(0.25f, 0.35f, 0.50f, 0.35f));
            yPos += 14f;

            // ── 刷新按钮行（固定底部） ──
            GameObject refreshRow = ZombieModeUIHelper.CreateRect("RefreshRow", panel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 42f), new Vector2(-40f, 56f), new Vector2(0.5f, 0.5f));
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
                hasFreeRefresh, false);
            CreateStyledRefreshButton(refreshRow.transform, "PaidRefresh",
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshPaid"), owner.GetZombieModeRewardPaidRefreshCost(runId).ToString("N0")),
                PaidRefreshColor, PaidRefreshHoverColor, FreeRefreshDisabledColor,
                true, true);
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
            GameObject topAccent = ZombieModeUIHelper.CreateRect("TopAccent", card.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -2f), new Vector2(0f, 4f), new Vector2(0.5f, 1f));
            Image topAccentImg = topAccent.AddComponent<Image>();
            topAccentImg.color = RewardCardAccentColor;
            topAccentImg.raycastTarget = false;

            // ── 文本 ──
            ZombieModeUIHelper.CreateText("Text", card.transform, text, 18,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, -4f), new Vector2(-18f, -14f),
                TextAlignmentOptions.Center, Color.white);

            // ── 按钮 ──
            Button button = card.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = RewardCardColor;
            colors.highlightedColor = RewardCardHoverColor;
            colors.pressedColor = RewardCardColor * 0.85f;
            colors.selectedColor = RewardCardHoverColor;
            colors.disabledColor = RewardCardColor * 0.6f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = cardImage;

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
            bool interactable, bool paid)
        {
            float btnW = 240f;
            float btnH = 44f;
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, text,
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(btnW, btnH),
                interactable ? baseColor : disabledColor, 16,
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

            Image image = button.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = interactable ? baseColor : disabledColor;
            colors.highlightedColor = hoverColor;
            colors.pressedColor = baseColor * 0.85f;
            colors.selectedColor = hoverColor;
            colors.disabledColor = disabledColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = image;

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

            // ── 全屏遮罩 ──
            GameObject backdrop = ZombieModeUIHelper.CreateRect("Backdrop", transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, Vector2.zero);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = new Color(0f, 0f, 0f, 0.72f);
            backdropImage.raycastTarget = true;

            // ── 外框 ──
            GameObject outer = ZombieModeUIHelper.CreateRect("PanelOuter", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 620f), new Vector2(0.5f, 0.5f));
            Image outerImage = outer.AddComponent<Image>();
            outerImage.color = new Color(0.12f, 0.16f, 0.24f, 0.98f);

            // ── 亮边层 ──
            GameObject borderGlow = ZombieModeUIHelper.CreateRect("BorderGlow", outer.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image borderImg = borderGlow.AddComponent<Image>();
            borderImg.color = new Color(0.22f, 0.30f, 0.44f, 0.45f);

            // ── 主面板 ──
            GameObject panel = ZombieModeUIHelper.CreateRect("Panel", borderGlow.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

            // ── 标题栏 ──
            float headerH = 64f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = new Color(0.14f, 0.20f, 0.32f, 1.00f);

            string titleKey = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_TempNurse"
                : "BossRush_ZombieMode_Npc_TempMerchant";
            ZombieModeUIHelper.CreateText("Title", header.transform,
                L10n.T(titleKey), 26,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(700f, 60f),
                TextAlignmentOptions.Center, Color.white);

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -headerH), 2f, new Color(0.35f, 0.55f, 0.85f, 0.70f));

            // ── 副标题 ──
            ZombieModeUIHelper.CreateText(
                "Subtitle",
                panel.transform,
                string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                    ? "治疗 / 解毒 / 止血"
                    : "丧尸模式终端分类抽取",
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
            bodyImage.color = new Color(0.06f, 0.08f, 0.12f, 0.60f);

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
            for (int i = 0; i < stock.Length && i < ZombieModeNpcCatalog.MaxMerchantStockButtons; i++)
            {
                ZombieModeNpcCatalog.MerchantStockEntry entry = stock[i];
                int index = i;
                int remaining = owner != null ? owner.GetZombieModeNpcServiceRemaining(runId, serviceType, index) : 0;
                int price = owner != null ? owner.GetZombieModeNpcServicePrice(runId, entry.BasePrice) : entry.BasePrice;
                string label = L10n.T(entry.DisplayKey) +
                    "\n<size=80%>" + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price) +
                    "  " + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining) + "</size>";
                CreateServiceButton(parent, "Merchant_" + i, label, Vector2.zero, remaining > 0, delegate
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
            for (int i = 0; i < services.Length; i++)
            {
                ZombieModeNpcCatalog.NurseServiceEntry entry = services[i];
                int index = i;
                int remaining = owner != null ? owner.GetZombieModeNpcServiceRemaining(runId, serviceType, index) : 0;
                int price = owner != null ? owner.GetZombieModeNpcServicePrice(runId, entry.BasePrice) : entry.BasePrice;
                string label = L10n.T(entry.ServiceKey) +
                    "\n<size=80%>" + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price) +
                    "  " + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining) + "</size>";
                CreateServiceButton(parent, "Nurse_" + i, label, Vector2.zero, remaining > 0, delegate
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

        private void CreateServiceButton(Transform parent, string name, string text, Vector2 position, bool interactable, UnityEngine.Events.UnityAction action)
        {
            Color normalColor = interactable ? new Color(0.12f, 0.18f, 0.26f, 0.98f) : new Color(0.14f, 0.14f, 0.14f, 0.70f);
            Color hoverColor = new Color(0.18f, 0.28f, 0.38f, 1.00f);
            Color accentColor = interactable ? new Color(0.44f, 0.82f, 0.92f, 0.95f) : new Color(0.30f, 0.30f, 0.30f, 0.70f);

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

            // 悬停色
            ColorBlock colors = button.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = hoverColor;
            colors.pressedColor = normalColor * 0.85f;
            colors.selectedColor = hoverColor;
            colors.disabledColor = normalColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = image;

            // 顶部高亮条
            GameObject accent = ZombieModeUIHelper.CreateRect(
                "Accent",
                obj.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -2f),
                new Vector2(0f, 4f),
                new Vector2(0.5f, 1f));
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
            Color closeNormal = new Color(0.35f, 0.16f, 0.18f, 1.00f);
            Color closeHover = new Color(0.48f, 0.22f, 0.24f, 1.00f);

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

            Image btnImage = button.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = closeNormal;
            colors.highlightedColor = closeHover;
            colors.pressedColor = closeNormal * 0.85f;
            colors.selectedColor = closeHover;
            colors.disabledColor = closeNormal * 0.6f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = btnImage;

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
