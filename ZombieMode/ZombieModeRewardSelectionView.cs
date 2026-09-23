// ============================================================================
// ZombieModeRewardSelectionView.cs - 丧尸模式每波结束的奖励选择面板
// ============================================================================
// 模块说明：
//   从 ZombieModeRewards.cs（宿主 partial）原样拆出后按 2026-09-23 审美审查重做（UC-02 / 07 / 08 / 11 / 13 / 14 / 15 / 20 / 21 / 25 / 27 / 30）。
//   拆出的原因：宿主 partial 的行数预算（tests/modbehaviour_partial_budget.json）没有余量，视图本来就不是宿主职责（AGENTS §4.15）。
//   - 奖励卡：类别色描边 + 顶边光带 + 类别 chip（替代「[属性]」方括号）+ 收益 / 代价两行（代价 DangerText）+ 数字键角标；
//     装备类挂官方物品图标，取不到就不画图标位；Boss 节点描边升到稀有度色（收益 ≥150% 用传说色）。
//   - 同一奖励节点内的刷新 / 展开休息时长原地重排（TryRebuild）：遮罩、输入租约不重建，面板高度 EaseOut 过渡，
//     只有奖励变了才重播卡片错峰入场（UC-08）。
//   - 免费刷新次数为 0 时不挂那个按钮（AGENTS §4.14）；付费刷新钱不够照挂，点了在按钮上方就地提示「还差 N 净化点」。
//   - 数字键 1–4 选卡（面板出现后 0.35 s 才开始收键，防止波次结束时正在按武器键误选）。
//   选择 / 刷新 / 扣点的判据与状态全在宿主（SelectZombieModeReward / RefreshZombieModeRewardSelection），这里只预判余额做提示。
// ============================================================================

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public sealed class ZombieModeRewardSelectionView : MonoBehaviour
    {
        private const float PanelWidth = 840f;
        private const float KeyArmSeconds = 0.35f;
        private const float HeightTweenSeconds = 0.18f;
        private static readonly string DangerHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText);

        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;
        private bool restEditorExpanded;
        private int pendingRestSeconds;
        private TextMeshProUGUI restTitleText;

        private GameObject panel;
        private RectTransform panelRect;
        private readonly List<ZombieModeRewardType> keyOptions = new List<ZombieModeRewardType>();
        private string optionsSignature = string.Empty;
        private bool selecting;
        private bool closing;
        private float keyArmTime;
        private float heightFrom;
        private float heightTo;
        private float heightElapsed = HeightTweenSeconds;

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

        /// <summary>
        /// 同一奖励节点内的刷新、Boss 追加选择、展开 / 收起休息时长：原地重排面板，不重建遮罩与输入租约（UC-08）。
        /// 返回 false 时由宿主走旧路径（新建整个界面）。
        /// </summary>
        internal bool TryRebuild(int newRunId, bool newRestEditorExpanded)
        {
            if (closing || owner == null || newRunId != runId || panel == null)
            {
                return false;
            }

            float previousHeight = panelRect != null ? panelRect.sizeDelta.y : 0f;
            restEditorExpanded = newRestEditorExpanded;
            pendingRestSeconds = owner.GetZombieModeSelectedPreparationDuration(runId);
            selecting = false;
            panel.SetActive(false);
            Destroy(panel);
            BuildPanel(false, previousHeight);
            return true;
        }

        /// <summary>宿主关闭前先还输入（UC-07：输入与时间流速这一帧恢复，淡出只是表现）。</summary>
        internal void ReleaseInput()
        {
            closing = true;
            RestoreInputState();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.ZombieModal;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();
            BuildPanel(true, 0f);
        }

        private void BuildPanel(bool opening, float previousHeight)
        {
            bool bossNode = owner.IsZombieModeBossRewardNode(runId);
            IList<ZombieModeRewardType> options = owner.GetZombieModeRewardOptions(runId);
            string signature = BuildOptionsSignature(options);
            bool animateCards = opening || !string.Equals(signature, optionsSignature, System.StringComparison.Ordinal);
            optionsSignature = signature;

            // CanvasScaler 已经负责不同分辨率缩放；这里使用稳定的参考尺寸。遮罩只在首次打开时建。
            panel = ZombieModeUIHelper.CreateModalSurface(
                "Panel",
                transform,
                new Vector2(PanelWidth, 480f),
                BossRushUIColors.Accent,
                opening);
            panelRect = panel.GetComponent<RectTransform>();

            // ── 标题：只留波次；Boss 节点的「净化收益 / 剩余几选」挪到信息行（UC-15）。标题行不垫直角色条（UC-21）。──
            float yPos = 0f;
            float headerH = 58f;
            string title = owner.GetZombieModeRewardTitle(runId) ?? string.Empty;
            string titleExtra = string.Empty;
            int split = title.IndexOf(" | ", System.StringComparison.Ordinal);
            if (split > 0)
            {
                titleExtra = title.Substring(split + 3).Replace(" | ", " · ");
                title = title.Substring(0, split);
            }
            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", panel.transform, title, 28,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -30f), new Vector2(-48f, 44f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;
            yPos += headerH;

            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -yPos), 2f,
                new Color(BossRushUIColors.Accent.r, BossRushUIColors.Accent.g, BossRushUIColors.Accent.b, 0.6f));
            yPos += 8f;

            // ── 信息行 ──
            float infoH = 28f;
            string info = string.Format(
                L10n.T("BossRush_ZombieMode_Reward_Info"),
                owner.GetZombieModePurificationPoints(runId),
                owner.GetZombieModeRewardFreeRefreshes(runId),
                owner.GetZombieModeRewardPaidRefreshCost(runId).ToString("N0"));
            if (titleExtra.Length > 0)
            {
                info += "    " + titleExtra;
            }
            ZombieModeUIHelper.CreateText("Info", panel.transform, info, 16,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + infoH * 0.5f)), new Vector2(-48f, infoH),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            yPos += infoH + 8f;

            float previewH = 34f;
            Color accent = BossRushUIColors.Accent;
            ZombieModeUIHelper.CreateHighlightBar("NextWavePreview", panel.transform,
                owner.GetZombieModeNextWavePreviewText(runId), 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + previewH * 0.5f)), new Vector2(-48f, previewH),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary,
                new Color(accent.r, accent.g, accent.b, 0.12f));
            yPos += previewH + 12f;

            // ── 奖励卡片 ──
            keyOptions.Clear();
            bool legendary = bossNode && owner.GetZombieModeBossRewardPercent(runId) >= 150;
            if (bossNode)
            {
                // 4 选项：2×2 网格
                Vector2 cardSize = new Vector2(300f, 112f);
                for (int i = 0; i < options.Count && i < 4; i++)
                {
                    float x = (i % 2 == 0) ? -(cardSize.x * 0.5f + 8f) : (cardSize.x * 0.5f + 8f);
                    float y = yPos + (i / 2) * (cardSize.y + 12f) + cardSize.y * 0.5f;
                    CreateRewardCard(i, options[i], new Vector2(x, -y), cardSize, true, legendary, animateCards);
                }
                yPos += cardSize.y * 2f + 12f + 16f;
            }
            else
            {
                // 3 选项：横排
                Vector2 cardSize = new Vector2(250f, 124f);
                int count = Mathf.Min(options.Count, 4);
                float totalW = cardSize.x * count + 16f * Mathf.Max(0, count - 1);
                float startX = -totalW * 0.5f + cardSize.x * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    float x = startX + i * (cardSize.x + 16f);
                    CreateRewardCard(i, options[i], new Vector2(x, -(yPos + cardSize.y * 0.5f)), cardSize, false, false, animateCards);
                }
                yPos += cardSize.y + 16f;
            }

            yPos = BuildRestEditor(yPos);

            // ── 分隔线 ──
            ZombieModeUIHelper.CreateSeparator("Sep", panel.transform,
                new Vector2(0.08f, 1f), new Vector2(0.92f, 1f),
                new Vector2(0f, -yPos), 1f, BossRushUIColors.Divider);
            yPos += 14f;

            BuildRefreshRow(yPos);
            yPos += 56f + 20f;

            // 面板高度按内容算；重排时从旧高度 EaseOut 过渡，不整块跳（UC-08）。
            heightTo = yPos;
            if (opening || previousHeight <= 0f || Mathf.Approximately(previousHeight, heightTo))
            {
                panelRect.sizeDelta = new Vector2(PanelWidth, heightTo);
                heightElapsed = HeightTweenSeconds;
            }
            else
            {
                heightFrom = previousHeight;
                heightElapsed = 0f;
                panelRect.sizeDelta = new Vector2(PanelWidth, heightFrom);
            }
            keyArmTime = Time.unscaledTime + KeyArmSeconds;
            if (opening)
            {
                BossRushUI.PlayOpenAnimation(panel);
            }
        }

        private static string BuildOptionsSignature(IList<ZombieModeRewardType> options)
        {
            if (options == null || options.Count == 0)
            {
                return string.Empty;
            }
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < options.Count; i++)
            {
                builder.Append((int)options[i]).Append(',');
            }
            return builder.ToString();
        }

        private float BuildRestEditor(float yPos)
        {
            // ── 休息时间（默认折叠，仅显示当前值与“修改”） ──
            float restH = restEditorExpanded ? 116f : 50f;
            GameObject restPanel = ZombieModeUIHelper.CreateRect("RestPanel", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + restH * 0.5f)), new Vector2(-48f, restH), new Vector2(0.5f, 0.5f));
            Image restPanelImage = restPanel.AddComponent<Image>();
            restPanelImage.color = new Color(BossRushUIColors.SurfaceRaised.r, BossRushUIColors.SurfaceRaised.g, BossRushUIColors.SurfaceRaised.b, 0.55f);
            BossRushUI.ApplyPanelSkin(restPanelImage, 8, BossRushUISkinPart.Card);
            restPanelImage.raycastTarget = false;
            restTitleText = ZombieModeUIHelper.CreateText("RestTitle", restPanel.transform,
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RestTitle"), pendingRestSeconds),
                16,
                restEditorExpanded ? new Vector2(0f, 1f) : new Vector2(0f, 0.5f),
                restEditorExpanded ? new Vector2(0.72f, 1f) : new Vector2(0.72f, 0.5f),
                restEditorExpanded ? new Vector2(18f, -20f) : new Vector2(18f, 0f),
                restEditorExpanded ? new Vector2(-38f, 28f) : new Vector2(-38f, 34f),
                TextAlignmentOptions.MidlineLeft,
                BossRushUIColors.TextSecondary);

            if (!restEditorExpanded)
            {
                Button editButton = ZombieModeUIHelper.CreateButton(
                    "RestEdit", restPanel.transform,
                    L10n.T("BossRush_ZombieMode_Reward_RestEdit"),
                    new Vector2(1f, 0.5f), new Vector2(-68f, 0f),
                    new Vector2(104f, 34f), BossRushUIColors.SurfaceRaised, 15,
                    new Vector2(92f, 28f), null, true);
                BossRushUIKit.StyleSecondaryButton(editButton);
                editButton.onClick.AddListener(delegate
                {
                    if (owner != null && !closing)
                    {
                        owner.OpenZombieModePreparationDurationEditor(runId);
                    }
                });
                return yPos + restH + 8f;
            }

            float sliderWidth = Mathf.Clamp(PanelWidth - 250f, 390f, 500f);
            GameObject sliderObject = ZombieModeUIHelper.CreateRect("RestDurationSlider", restPanel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-120f, -66f), new Vector2(sliderWidth, 24f), new Vector2(0.5f, 0.5f));
            Slider slider = sliderObject.AddComponent<Slider>();
            slider.minValue = 1f;
            slider.maxValue = 20f;
            slider.wholeNumbers = true;
            slider.direction = Slider.Direction.LeftToRight;

            // 滑块上皮（UC-13）：胶囊轨道 + Accent 填充 + 圆形手柄，悬停 / 按下三态分得开。
            GameObject track = ZombieModeUIHelper.CreateRect("Track", sliderObject.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(0f, 8f), new Vector2(0.5f, 0.5f));
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(trackImage, 4, BossRushUISkinPart.ScrollHandle);

            GameObject fillArea = ZombieModeUIHelper.CreateRect("FillArea", sliderObject.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(-12f, 8f), new Vector2(0.5f, 0.5f));
            GameObject fill = ZombieModeUIHelper.CreateRect("Fill", fillArea.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = BossRushUIColors.Accent;
            BossRushUI.ApplyPanelSkin(fillImage, 4, BossRushUISkinPart.ScrollHandle);
            slider.fillRect = fill.GetComponent<RectTransform>();

            GameObject handleArea = ZombieModeUIHelper.CreateRect("HandleArea", sliderObject.transform,
                Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12f, 0f), new Vector2(0.5f, 0.5f));
            GameObject handle = ZombieModeUIHelper.CreateRect("Handle", handleArea.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(20f, 20f), new Vector2(0.5f, 0.5f));
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;
            BossRushUI.ApplyPanelSkin(handleImage, 10, BossRushUISkinPart.Button);
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handleImage;
            ColorBlock handleColors = slider.colors;
            handleColors.normalColor = BossRushUIColors.TextPrimary;
            handleColors.highlightedColor = Color.Lerp(BossRushUIColors.TextPrimary, BossRushUIColors.Accent, 0.45f);
            handleColors.pressedColor = BossRushUIColors.Accent;
            handleColors.selectedColor = BossRushUIColors.TextPrimary;
            handleColors.disabledColor = BossRushUI.GetDisabledColor(BossRushUIColors.TextPrimary);
            handleColors.colorMultiplier = 1f;
            handleColors.fadeDuration = 0.08f;
            slider.colors = handleColors;
            slider.value = Mathf.Clamp(Mathf.RoundToInt(pendingRestSeconds / 15f), 1, 20);
            slider.onValueChanged.AddListener(delegate(float value)
            {
                pendingRestSeconds = Mathf.Clamp(Mathf.RoundToInt(value) * 15, 15, 300);
                UpdatePendingRestDurationText();
            });

            ZombieModeUIHelper.CreateText("RestMin", restPanel.transform,
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RestOption"), 15), 13,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-120f - sliderWidth * 0.5f + 38f, -96f),
                new Vector2(76f, 22f), TextAlignmentOptions.MidlineLeft, BossRushUIColors.TextSecondary);
            ZombieModeUIHelper.CreateText("RestMax", restPanel.transform,
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RestOption"), 300), 13,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-120f + sliderWidth * 0.5f - 43f, -96f),
                new Vector2(86f, 22f), TextAlignmentOptions.MidlineRight, BossRushUIColors.TextSecondary);

            // 休息时长编辑器里的「确定」是这一块的主操作（AccentFill）；卡片才是整屏的主角，不另设主按钮。
            Button applyButton = ZombieModeUIHelper.CreateButton(
                "RestApply", restPanel.transform,
                L10n.T("BossRush_ZombieMode_Reward_RestApply"),
                new Vector2(1f, 1f), new Vector2(-66f, -66f),
                new Vector2(104f, 36f), BossRushUIColors.AccentFill, 15,
                new Vector2(92f, 30f), null, true);
            applyButton.onClick.AddListener(delegate
            {
                if (owner != null && !closing)
                {
                    owner.SetZombieModePreparationDuration(runId, pendingRestSeconds);
                }
            });
            return yPos + restH + 8f;
        }

        private void BuildRefreshRow(float yPos)
        {
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

            // 免费次数用完就不挂这个按钮（AGENTS §4.14「不挂灰掉的占位项」，UC-14）；次数写在按钮上。
            int freeRefreshes = owner.GetZombieModeRewardFreeRefreshes(runId);
            if (freeRefreshes > 0)
            {
                CreateRefreshButton(refreshRow.transform, "FreeRefresh",
                    string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshFree"), freeRefreshes), false, 0);
            }

            // 付费刷新：钱不够照挂，价钱写在按钮上（不够时价钱标 DangerText），点了就地提示差多少（UC-27）。
            int paidRefreshCost = owner.GetZombieModeRewardPaidRefreshCost(runId);
            bool canAffordPaidRefresh = owner.GetZombieModePurificationPoints(runId) >= paidRefreshCost;
            string price = canAffordPaidRefresh
                ? paidRefreshCost.ToString("N0")
                : "<color=" + DangerHex + ">" + paidRefreshCost.ToString("N0") + "</color>";
            CreateRefreshButton(refreshRow.transform, "PaidRefresh",
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshPaid"), price), true, paidRefreshCost);
        }

        private void CreateRefreshButton(Transform parent, string name, string text, bool paid, int cost)
        {
            float btnW = 240f;
            float btnH = 44f;
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, text,
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(btnW, btnH),
                BossRushUIColors.SurfaceRaised, 16,
                new Vector2(btnW - 14f, btnH - 8f),
                null, true);
            BossRushUIKit.StyleSecondaryButton(button);

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

            bool capturedPaid = paid;
            int capturedCost = cost;
            button.onClick.AddListener(delegate
            {
                if (owner == null || closing || selecting)
                {
                    return;
                }
                if (capturedPaid)
                {
                    int shortBy = capturedCost - owner.GetZombieModePurificationPoints(runId);
                    if (shortBy > 0)
                    {
                        ZombieModeUiNudge.Flash(button, string.Format(L10n.T("BossRush_ZombieMode_Notify_PointsShort"), shortBy));
                        return;
                    }
                }
                owner.RefreshZombieModeRewardSelection(runId, capturedPaid);
            });
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

        private void CreateRewardCard(int index, ZombieModeRewardType rewardType, Vector2 position, Vector2 size,
            bool bossNode, bool legendary, bool animate)
        {
            string category;
            string benefit;
            string cost;
            ZombieModeRewardCardText.Split(owner.GetZombieModeRewardDisplayText(runId, rewardType), out category, out benefit, out cost);
            Color accentColor = owner.GetZombieModeRewardAccentColor(rewardType);
            Color strokeTint = bossNode ? (legendary ? BossRushUIColors.RarityLegendary : BossRushUIColors.RarityEpic) : accentColor;

            // ── 卡片底板：卡片档底图 + 类别色描边（悬停提亮）+ 顶边光带 ──
            GameObject card = ZombieModeUIHelper.CreateRect("Reward_" + rewardType, panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                position, size, new Vector2(0.5f, 0.5f));
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(cardImage, 10, BossRushUISkinPart.Card);
            Image stroke = BossRushUI.ApplyPanelStroke(cardImage, 10, BossRushUISkinPart.Card,
                new Color(strokeTint.r, strokeTint.g, strokeTint.b, 0.55f));
            ZombieModeCardHover.Attach(card, stroke);

            GameObject band = ZombieModeUIHelper.CreateRect("TopBand", card.transform,
                new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0f, -1.5f), new Vector2(0f, 2f), new Vector2(0.5f, 1f));
            Image bandImage = band.AddComponent<Image>();
            bandImage.color = new Color(strokeTint.r, strokeTint.g, strokeTint.b, 0.45f);
            bandImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(bandImage, 1, BossRushUISkinPart.Hairline);

            // ── 类别 chip（替代「[属性]」方括号文字）──
            float top = 12f;
            if (!string.IsNullOrEmpty(category))
            {
                GameObject chip = ZombieModeUIHelper.CreateRect("Chip", card.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -top), new Vector2(60f, 22f), new Vector2(0f, 1f));
                Image chipImage = chip.AddComponent<Image>();
                chipImage.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.20f);
                chipImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(chipImage, 9, BossRushUISkinPart.Button);
                TextMeshProUGUI chipText = ZombieModeUIHelper.CreateText("Text", chip.transform, category, 13,
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center,
                    new Color(accentColor.r, accentColor.g, accentColor.b, 1f));
                chipText.enableAutoSizing = false;
                chipText.enableWordWrapping = false;
                chipText.overflowMode = TextOverflowModes.Overflow;
                chipText.margin = Vector4.zero;
                float chipWidth = Mathf.Clamp(Mathf.Ceil(chipText.GetPreferredValues(category, 400f, 22f).x) + 18f, 40f, size.x - 60f);
                chip.GetComponent<RectTransform>().sizeDelta = new Vector2(chipWidth, 22f);
            }

            // ── 数字键角标（UC-30）──
            GameObject key = ZombieModeUIHelper.CreateRect("Key", card.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -top), new Vector2(22f, 22f), new Vector2(1f, 1f));
            Image keyImage = key.AddComponent<Image>();
            keyImage.color = BossRushUIColors.Surface;
            keyImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(keyImage, 6, BossRushUISkinPart.Button);
            TextMeshProUGUI keyText = ZombieModeUIHelper.CreateText("Text", key.transform, (index + 1).ToString(), 13,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            keyText.enableAutoSizing = false;
            keyText.margin = Vector4.zero;

            // ── 图标（只有装备类有官方物品图标；取不到就不画这一格，整行留给文字）──
            float textLeft = 16f;
            float bodyTop = top + 22f + 8f;
            Sprite icon = owner.GetZombieModeRewardIcon(rewardType);
            if (icon != null)
            {
                float iconSize = Mathf.Min(48f, size.y - bodyTop - 12f);
                GameObject iconObject = ZombieModeUIHelper.CreateRect("Icon", card.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -bodyTop), new Vector2(iconSize, iconSize), new Vector2(0f, 1f));
                Image iconImage = iconObject.AddComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                textLeft = 14f + iconSize + 10f;
            }

            // ── 收益 / 代价两行（UC-02）：收益 TextPrimary（契约整段是交易条件，用 WarningText）；代价单独一行 DangerText ──
            bool contract = rewardType.ToString().StartsWith("Contract", System.StringComparison.Ordinal);
            float costH = string.IsNullOrEmpty(cost) ? 0f : 20f;
            float benefitH = size.y - bodyTop - 10f - costH;
            // 子物体不能叫 "Text"：ApplyButtonColors 会把名为 Text 的直接子标签改成按钮字色，契约的 WarningText 就被冲掉。
            TextMeshProUGUI benefitText = ZombieModeUIHelper.CreateText("Benefit", card.transform, benefit, 16,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2((textLeft - 14f) * 0.5f, -(bodyTop + benefitH * 0.5f)), new Vector2(-(textLeft + 14f), benefitH),
                TextAlignmentOptions.TopLeft, contract ? BossRushUIColors.WarningText : BossRushUIColors.TextPrimary);
            benefitText.margin = Vector4.zero;
            if (costH > 0f)
            {
                TextMeshProUGUI costText = ZombieModeUIHelper.CreateText("Cost", card.transform, cost, 14,
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2((textLeft - 14f) * 0.5f, 10f + costH * 0.5f), new Vector2(-(textLeft + 14f), costH),
                    TextAlignmentOptions.MidlineLeft, BossRushUIColors.DangerText);
                costText.margin = Vector4.zero;
            }

            // ── 整张卡是按钮：三态、官方音效与按下回弹走共享入口 ──
            Button button = card.AddComponent<Button>();
            button.targetGraphic = cardImage;
            ZombieModeUIHelper.SetButtonBaseColor(button, BossRushUIColors.SurfaceRaised);
            ZombieModeRewardType capturedType = rewardType;
            button.onClick.AddListener(delegate { Select(capturedType); });
            keyOptions.Add(rewardType);

            if (animate)
            {
                BossRushUIEntranceAnimation.Play(card, 0.06f + index * 0.06f, 0.24f, 16f);
            }
        }

        private void Select(ZombieModeRewardType rewardType)
        {
            if (owner == null || selecting || closing)
            {
                return;
            }

            selecting = true;
            owner.SelectZombieModeReward(runId, rewardType);
            // 成功时要么关闭（closing），要么原地重排（TryRebuild 复位 selecting）；被宿主拒绝时放开，允许再选。
            if (!closing)
            {
                selecting = false;
            }
        }

        private void Update()
        {
            if (panelRect != null && heightElapsed < HeightTweenSeconds && !BossRushUI.IsGamePaused())
            {
                heightElapsed += Time.unscaledDeltaTime;
                float t = BossRushUI.EaseOut(heightElapsed / HeightTweenSeconds);
                panelRect.sizeDelta = new Vector2(PanelWidth, Mathf.Lerp(heightFrom, heightTo, t));
            }

            // 数字键选卡（UC-30）。输入租约禁用的是游戏 InputManager，UnityEngine.Input 仍可读（与图鉴一致）。
            if (closing || selecting || owner == null || Time.unscaledTime < keyArmTime || BossRushUI.IsGamePaused())
            {
                return;
            }
            for (int i = 0; i < keyOptions.Count && i < 4; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Select(keyOptions[i]);
                    return;
                }
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
}
