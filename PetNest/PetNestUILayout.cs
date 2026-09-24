// ============================================================================
// PetNestUILayout.cs - 遗种巢主面板的两栏与分区画法（2026-09-24 交互重排）
// ============================================================================
// 与 PetNestUI.cs 是同一个 partial 类，拆开只为单文件行数预算（tests/LargeFileBudgetGuard.py）。
// 只在 PetNestUI 已建好的 surface 里摆内容：**不创建 canvas、不碰 sortingOrder**。
//
// 画法对照主流做法（owner：「一股脑把所有功能都做成按钮丢出来」）：
//   - 巢页 = 列表 + 详情（Apple Split View / 宝可梦 HOME 盒子）：左栏出战席位格 + 容量行 + 紧凑行，
//     行上没有按钮；右栏是当前崽的详情，底栏只放作用在它身上的操作，危险操作靠左、主操作最右；
//   - 分区行：按钮跟着它作用的那一行走（凝蛋按钮在遗魂进度那一行里，不再堆进底部动作条）；
//   - 分段按钮（远征目的地）、对比卡（风险档，参照定价页的档位对比）、头像小卡（远征选崽）。
// 选中态一律 WarningText 描边（与 2026-09-23 之前的卡片选中口径一致）。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class PetNestUI
    {
        #region 常量与状态

        // 巢页两栏的几何（面板局部坐标，正文区 y 从 BodyTop=220 到 -352）
        private const float NestListWidth = 440f;
        private const float NestListCenterX = -340f;       // 左栏 x ∈ [-560, -120]
        private const float NestSlotHeight = 80f;
        private const float NestCapacityHeight = 40f;
        private const float NestGap = 6f;
        private const float NestRowWidth = 404f;           // 440 - 滚动条 20 - 内边距 16
        private const float NestDetailWidth = 664f;
        private const float NestDetailCenterX = 228f;      // 右栏 x ∈ [-104, 560]
        private const float NestFooterHeight = 52f;
        private const float NestDetailTextWidth = 604f;    // 664 - 16 - 滚动条 20 - 内边距 16 - 余量 8
        private const float NestPortraitSize = 112f;

        // 行（分区里的一项）：图框、按钮列、单行字的框高（≥ 字号 × 1.45 + 4，避开 TMP Ellipsis 整行清空）
        private const float RowIconSize = 64f;
        private const float CompactIconSize = 56f;
        private const float RowActionWidth = 170f;
        private const float RowTitleHeight = 34f;
        private const float RowSubtitleHeight = 28f;

        // 远征选崽的头像小卡：4 列 × 262 + 3 × 12 = 1084
        private static readonly Vector2 StripCellSize = new Vector2(262f, 68f);

        private GameObject _nestRoot;
        private Transform _nestSlotRoot;
        private Transform _nestCapacityRoot;
        private Transform _nestListRoot;
        private Transform _nestDetailRoot;
        private Transform _nestFooterRoot;

        #endregion

        #region 巢页两栏

        /// <summary>建巢页的两栏骨架（只建一次，切页时显隐）。</summary>
        private void BuildNestBody(Transform surface)
        {
            _nestRoot = ZombieModeUIHelper.CreateRect("NestBody", surface, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));

            float slotCenter = BodyTop - NestSlotHeight * 0.5f;
            _nestSlotRoot = CreateFixedColumn(_nestRoot.transform, "NestSlot",
                new Vector2(NestListCenterX, slotCenter), new Vector2(NestListWidth, NestSlotHeight));
            float capacityCenter = BodyTop - NestSlotHeight - NestGap - NestCapacityHeight * 0.5f;
            _nestCapacityRoot = ZombieModeUIHelper.CreateRect("NestCapacity", _nestRoot.transform,
                new Vector2(0.5f, 0.5f), new Vector2(NestListWidth, NestCapacityHeight)).transform;
            ((RectTransform)_nestCapacityRoot).anchoredPosition = new Vector2(NestListCenterX, capacityCenter);

            float listTop = BodyTop - NestSlotHeight - NestCapacityHeight - NestGap * 2f;
            float listHeight = listTop - (BodyTop - TotalBodyHeight);
            _nestListRoot = CreateScrollList(_nestRoot.transform, "NestList",
                new Vector2(NestListCenterX, listTop - listHeight * 0.5f), new Vector2(NestListWidth, listHeight));

            GameObject panel = ZombieModeUIHelper.CreateRect("NestDetail", _nestRoot.transform,
                new Vector2(0.5f, 0.5f), new Vector2(NestDetailWidth, TotalBodyHeight));
            panel.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(NestDetailCenterX, BodyTop - TotalBodyHeight * 0.5f);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = BossRushUIColors.SurfaceRaised;
            panelImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(panelImage, 12, BossRushUISkinPart.Card);
            BossRushUI.ApplyPanelStroke(panelImage, 12, BossRushUISkinPart.Card, BossRushUIColors.Stroke);

            float half = TotalBodyHeight * 0.5f;
            float scrollHeight = TotalBodyHeight - 10f - (NestFooterHeight + 22f);
            _nestDetailRoot = CreateScrollList(panel.transform, "NestDetailScroll",
                new Vector2(0f, half - 10f - scrollHeight * 0.5f), new Vector2(NestDetailWidth - 16f, scrollHeight));
            ZombieModeUIHelper.CreateSeparator("NestFooterRule", panel.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -half + NestFooterHeight + 22f), 1f, BossRushUIColors.Divider);
            GameObject footer = ZombieModeUIHelper.CreateRect("NestFooter", panel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(NestDetailWidth - 32f, NestFooterHeight));
            footer.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -half + 12f + NestFooterHeight * 0.5f);
            HorizontalLayoutGroup row = footer.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            _nestFooterRoot = footer.transform;

            _nestRoot.SetActive(false);
        }

        /// <summary>不滚动的纵向容器（出战席位格）。</summary>
        private static Transform CreateFixedColumn(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject column = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            column.GetComponent<RectTransform>().anchoredPosition = position;
            VerticalLayoutGroup layout = column.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return column.transform;
        }

        private void SetNestBodyVisible(bool visible)
        {
            if (_nestRoot != null && _nestRoot.activeSelf != visible) _nestRoot.SetActive(visible);
        }

        /// <summary>画巢页：出战席位格、容量行、崽列表、右栏详情与底栏。</summary>
        private void RenderNest(PetNestNestView view)
        {
            if (view.Slot != null) SpawnRow(_nestSlotRoot, view.Slot, NestListWidth, true);
            RenderCapacityRow(view);

            for (int i = 0; i < view.Rows.Count; i++)
            {
                GameObject row = SpawnRow(_nestListRoot, view.Rows[i], NestRowWidth, true);
                if (row != null) _entranceTargets.Add(row);
            }
            if (view.Rows.Count == 0 && !string.IsNullOrEmpty(view.EmptyText))
            {
                SpawnLine(_nestListRoot, view.EmptyText, 17f, BossRushUIColors.TextSecondary, NestRowWidth);
            }

            RenderDetail(view.Detail ?? new PetNestDetailData());
        }

        /// <summary>容量一行：左边「巢 12 / 24」，右边「选择 / 完成」（iOS 照片的批量入口位置）。</summary>
        private void RenderCapacityRow(PetNestNestView view)
        {
            bool hasMode = !string.IsNullOrEmpty(view.ModeLabel) && view.OnMode != null;
            TextMeshProUGUI capacity = ZombieModeUIHelper.CreateText(
                "Capacity", _nestCapacityRoot, view.CapacityText ?? string.Empty, 16f,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(hasMode ? -56f : 0f, 0f), new Vector2(hasMode ? -120f : -8f, 0f),
                TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
            capacity.enableWordWrapping = false;
            capacity.overflowMode = TextOverflowModes.Ellipsis;
            capacity.enableAutoSizing = false;
            capacity.fontSize = 16f;
            BossRushUI.ApplyGameFont(capacity);
            _spawned.Add(capacity.gameObject);
            if (!hasMode) return;

            Button mode = ZombieModeUIHelper.CreateButton(
                "Mode", _nestCapacityRoot, view.ModeLabel,
                new Vector2(1f, 0.5f), new Vector2(-54f, 0f), new Vector2(104f, 36f),
                BossRushUIColors.SurfaceRaised, 17f, new Vector2(96f, 32f),
                new UnityEngine.Events.UnityAction(view.OnMode), true);
            BossRushUIKit.StyleSecondaryButton(mode);
            _spawned.Add(mode.gameObject);
        }

        /// <summary>右栏：头部（立绘、名字、改名、副标题）+ 失败提示 + 分区 + 底栏。</summary>
        private void RenderDetail(PetNestDetailData detail)
        {
            SpawnDetailHeader(detail);
            if (!string.IsNullOrEmpty(PetNestUIPages.LastFailureText))
            {
                SpawnLine(_nestDetailRoot, PetNestUIPages.LastFailureText, 17f, BossRushUIColors.DangerText, NestDetailTextWidth);
            }
            for (int i = 0; i < detail.Sections.Count; i++)
            {
                TextMeshProUGUI header = SpawnLine(_nestDetailRoot, detail.Sections[i].Key, 17f,
                    BossRushUIColors.Accent, NestDetailTextWidth);
                header.fontStyle = FontStyles.Bold;
                SpawnLine(_nestDetailRoot, detail.Sections[i].Value ?? string.Empty, 16f,
                    BossRushUIColors.TextSecondary, NestDetailTextWidth);
            }
            RenderFooter(detail);
        }

        private void SpawnDetailHeader(PetNestDetailData detail)
        {
            GameObject header = ZombieModeUIHelper.CreateRect("DetailHeader", _nestDetailRoot,
                new Vector2(0.5f, 0.5f), new Vector2(NestDetailTextWidth, NestPortraitSize));
            _spawned.Add(header);
            bool hasIcon = detail.Icon != null;
            if (hasIcon)
            {
                CreateIconFrame(header.transform, "Portrait", detail.Icon,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, NestPortraitSize,
                    detail.Shiny ? BossRushUIColors.RarityLegendary : BossRushUIColors.Stroke, false);
            }
            float left = hasIcon ? NestPortraitSize + 16f : 0f;
            float renameWidth = detail.OnRename != null ? 96f : 0f;
            float textWidth = NestDetailTextWidth - left;

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "DetailTitle", header.transform, detail.Title ?? string.Empty, 26f,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(left, -4f), new Vector2(textWidth - renameWidth - 8f, 38f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(left, -4f);
            title.margin = Vector4.zero;
            title.fontStyle = FontStyles.Bold;
            title.enableAutoSizing = false;
            title.fontSize = 26f;
            title.enableWordWrapping = false;
            title.overflowMode = TextOverflowModes.Ellipsis;
            BossRushUI.ApplyGameFont(title);
            if (detail.Shiny) PetNestShinyTextShimmer.Attach(title);

            if (detail.OnRename != null)
            {
                // 改名是低频操作：名字旁一颗小号次级按钮，不和出战、放生挤在一排
                Button rename = ZombieModeUIHelper.CreateButton(
                    "Rename", header.transform, L10n.T("改名", "Rename"),
                    new Vector2(1f, 1f), new Vector2(-48f, -22f), new Vector2(96f, 34f),
                    BossRushUIColors.SurfaceRaised, 16f, new Vector2(88f, 30f),
                    new UnityEngine.Events.UnityAction(detail.OnRename), true);
                BossRushUIKit.StyleSecondaryButton(rename);
            }

            float height = 46f;
            if (!string.IsNullOrEmpty(detail.Subtitle))
            {
                TextMeshProUGUI subtitle = ZombieModeUIHelper.CreateText(
                    "DetailSubtitle", header.transform, detail.Subtitle, 17f,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(left, -height), new Vector2(textWidth, 28f),
                    TextAlignmentOptions.TopLeft, BossRushUIColors.Accent);
                subtitle.rectTransform.pivot = new Vector2(0f, 1f);
                subtitle.rectTransform.anchoredPosition = new Vector2(left, -height);
                BossRushUI.ApplyGameFont(subtitle);
                height += BossRushUI.MeasureTextHeight(subtitle, textWidth, 28f);
            }
            SetLayoutHeight(header, Mathf.Max(hasIcon ? NestPortraitSize : 0f, height) + 6f);
        }

        /// <summary>
        /// 底栏：危险操作（DangerText 描边）靠左，中间是弹性空白（有状态字时写在这里），其余靠右、主操作最右。
        /// 没有按钮时整条只是一行状态字（「远征中 · 剩余 8 分钟」），不挂灰掉的占位按钮（§4.14）。
        /// </summary>
        private void RenderFooter(PetNestDetailData detail)
        {
            List<PetNestActionData> left = new List<PetNestActionData>();
            List<PetNestActionData> right = new List<PetNestActionData>();
            for (int i = 0; i < detail.Actions.Count; i++)
            {
                PetNestActionData action = detail.Actions[i];
                if (action == null) continue;
                if (action.IsDanger && !action.IsPrimary) left.Add(action);
                else if (!action.IsPrimary) right.Add(action);
            }
            for (int i = 0; i < detail.Actions.Count; i++)
            {
                if (detail.Actions[i] != null && detail.Actions[i].IsPrimary) right.Add(detail.Actions[i]);
            }

            for (int i = 0; i < left.Count; i++) SpawnFooterButton(left[i]);

            TextMeshProUGUI status = ZombieModeUIHelper.CreateText(
                "FooterStatus", _nestFooterRoot, detail.FooterText ?? string.Empty, 16f,
                Vector2.zero, new Vector2(100f, NestFooterHeight),
                left.Count + right.Count > 0 ? TextAlignmentOptions.Center : TextAlignmentOptions.Left,
                BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(status);
            status.enableAutoSizing = true;
            status.fontSizeMin = 13f;
            status.fontSizeMax = 16f;
            LayoutElement flexible = status.gameObject.AddComponent<LayoutElement>();
            flexible.flexibleWidth = 1f;
            flexible.minWidth = 0f;
            _spawned.Add(status.gameObject);
            if (detail.LiveFooter != null)
            {
                _liveBodies.Add(status);
                _liveBodySources.Add(detail.LiveFooter);
            }

            for (int i = 0; i < right.Count; i++) SpawnFooterButton(right[i]);
        }

        private void SpawnFooterButton(PetNestActionData action)
        {
            Button button = CreateActionButton(_nestFooterRoot, "FooterAction", action, new Vector2(160f, NestFooterHeight - 4f));
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            float width = 150f;
            if (label != null)
            {
                label.enableAutoSizing = false;
                label.fontSize = 18f;
                width = Mathf.Clamp(label.GetPreferredValues(label.text).x + 44f, 120f, 240f);
                // 量完宽度再打开自动缩字：超过 240 的长英文标签缩小，而不是被 Ellipsis 整行清空
                label.enableAutoSizing = true;
            }
            LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            _spawned.Add(button.gameObject);
        }

        #endregion

        #region 分区

        /// <summary>一个分区：区头 + 说明 + 按排法铺开的项。</summary>
        private void SpawnSection(Transform parent, PetNestSection section, float width)
        {
            if (section == null) return;
            if (!string.IsNullOrEmpty(section.Title)) SpawnSectionHeader(parent, section.Title, width);
            if (!string.IsNullOrEmpty(section.Caption))
            {
                SpawnLine(parent, section.Caption, 16f, BossRushUIColors.TextSecondary, width);
            }
            if (section.Items.Count == 0) return;
            switch (section.Layout)
            {
                case PetNestSectionLayout.Segments:
                    SpawnSegments(parent, section.Items, width);
                    break;
                case PetNestSectionLayout.Columns:
                    SpawnColumns(parent, section.Items, width);
                    break;
                case PetNestSectionLayout.Strip:
                    SpawnStrip(parent, section.Items, width);
                    break;
                default:
                    for (int i = 0; i < section.Items.Count; i++)
                    {
                        GameObject row = SpawnRow(parent, section.Items[i], width, false);
                        if (row != null) _entranceTargets.Add(row);
                    }
                    break;
            }
        }

        /// <summary>
        /// 一行：左侧图框、标题 / 副标题 / 进度条 / 正文从左上往下排，右侧一颗行内按钮或勾选框。
        /// 点行本身 = 选中 / 勾选（按钮在更上层，点按钮不会触发行）。高度按正文实测撑开，不截断。
        /// </summary>
        private GameObject SpawnRow(Transform parent, PetNestCardData data, float width, bool compact)
        {
            if (data == null) return null;

            Color chromaA, chromaB;
            bool chroma = TryParseHexColor(data.ChromaHexA, out chromaA) & TryParseHexColor(data.ChromaHexB, out chromaB);
            Color accent = chroma
                ? chromaA
                : (data.Shiny
                    ? BossRushUIColors.RarityLegendary
                    : (data.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.Accent));

            GameObject card = BossRushUI.CreateCard("Row", parent, Vector2.zero, new Vector2(width, 80f),
                BossRushUIColors.SurfaceRaised, accent, true);
            _spawned.Add(card);
            if (chroma)
            {
                AddIdentityRail(card.transform, "Row_Accent2", 8f, chromaB);
                if (data.Shiny) AddIdentityRail(card.transform, "Row_ShinyMark", 13f, BossRushUIColors.RarityLegendary);
            }
            // 选中态用描边点出来（当前崽 / 批量已勾选），否则玩家无从判断「作用在哪只」
            if (data.Selected) SetCardStroke(card, BossRushUIColors.WarningText);
            if (data.OnCardClick != null) MakeCardClickable(card, data.OnCardClick);

            float iconSize = compact ? CompactIconSize : RowIconSize;
            bool hasIcon = data.Icon != null;
            if (hasIcon)
            {
                Color frameStroke = data.Shiny ? BossRushUIColors.RarityLegendary : (chroma ? chromaA : BossRushUIColors.Stroke);
                CreateIconFrame(card.transform, "Portrait", data.Icon,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -10f),
                    iconSize, frameStroke, data.IconLocked);
            }

            bool hasAction = !string.IsNullOrEmpty(data.ActionLabel) && data.OnClick != null;
            float left = hasIcon ? 20f + iconSize + 14f : 22f;
            float right = hasAction ? RowActionWidth + 30f : (data.Checkbox ? 56f : 16f);
            float textWidth = Mathf.Max(80f, width - left - right);

            float y = compact ? 8f : 12f;
            TextMeshProUGUI title = CreateRowText(card.transform, "Title", data.Title, compact ? 18f : 20f,
                BossRushUIColors.TextPrimary, left, y, textWidth, RowTitleHeight, true);
            if (data.Shiny) PetNestShinyTextShimmer.Attach(title);
            y += compact ? 30f : RowTitleHeight;

            if (!string.IsNullOrEmpty(data.Subtitle))
            {
                CreateRowText(card.transform, "Subtitle", data.Subtitle, compact ? 15f : 16f,
                    data.IsDanger ? BossRushUIColors.DangerText : BossRushUIColors.Accent,
                    left, y, textWidth, RowSubtitleHeight, false);
                y += RowSubtitleHeight;
            }
            if (data.Progress >= 0f)
            {
                CreateProgressBar(card.transform, data.Progress, data.ProgressText, left, y + 4f, textWidth);
                y += 26f;
            }
            if (!string.IsNullOrEmpty(data.Body))
            {
                TextMeshProUGUI body = ZombieModeUIHelper.CreateText(
                    "Body", card.transform, data.Body, 15f,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(left, -y), new Vector2(textWidth, 26f),
                    TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
                body.rectTransform.pivot = new Vector2(0f, 1f);
                body.rectTransform.anchoredPosition = new Vector2(left, -y);
                BossRushUI.ApplyGameFont(body);
                y += BossRushUI.MeasureTextHeight(body, textWidth, 26f);
                if (data.LiveBody != null)
                {
                    _liveBodies.Add(body);
                    _liveBodySources.Add(data.LiveBody);
                }
            }
            float height = Mathf.Max(hasIcon ? iconSize + 20f : 0f, y + (compact ? 8f : 12f));
            SetLayoutHeight(card, height);

            if (data.Checkbox) CreateCheckbox(card.transform, data.Selected);
            if (hasAction)
            {
                // 行内按钮一律次级样式（深底 + 描边：普通 Accent、危险 DangerText），同一屏不堆色块（UA-23）
                Button action = ZombieModeUIHelper.CreateButton(
                    "RowAction", card.transform, data.ActionLabel,
                    new Vector2(1f, 0.5f), new Vector2(-16f - RowActionWidth * 0.5f, 0f), new Vector2(RowActionWidth, 44f),
                    BossRushUIColors.SurfaceRaised, 17f, new Vector2(RowActionWidth - 10f, 40f),
                    new UnityEngine.Events.UnityAction(data.OnClick), true);
                AddButtonStroke(action, data.IsDanger ? BossRushUIColors.DangerText : BossRushUIColors.Accent);
            }
            return card;
        }

        /// <summary>行里的一段单行字：从左上往下排，关自动缩字、放不下用省略号（框高按字号留足，不会整行清空）。</summary>
        private static TextMeshProUGUI CreateRowText(Transform parent, string name, string text, float fontSize,
            Color color, float left, float top, float width, float height, bool bold)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                name, parent, text ?? string.Empty, fontSize,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(left, -top), new Vector2(width, height),
                TextAlignmentOptions.Left, color);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(left, -top);
            label.margin = Vector4.zero;
            label.enableAutoSizing = false;
            label.fontSize = fontSize;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            if (bold) label.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(label);
            return label;
        }

        /// <summary>
        /// 进度条：细轨 + 填充（Filled 必须配 sprite，否则 fillAmount 失效——同好感度条的坑），右侧写数字。
        /// 满了用 Success（「已达成」的状态色），没满用 Accent。
        /// </summary>
        private static void CreateProgressBar(Transform parent, float progress, string text, float left, float top, float width)
        {
            float textWidth = string.IsNullOrEmpty(text) ? 0f : 96f;
            float barWidth = Mathf.Max(60f, Mathf.Min(360f, width - textWidth - 12f));
            GameObject track = ZombieModeUIHelper.CreateRect("ProgressTrack", parent,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, -top - 7f),
                new Vector2(barWidth, 8f), new Vector2(0f, 0.5f));
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Surface;
            trackImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(trackImage, 3, BossRushUISkinPart.Hairline);

            GameObject fillObject = ZombieModeUIHelper.CreateRect("ProgressFill", track.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image fill = fillObject.AddComponent<Image>();
            fill.sprite = BossRushUI.GetSolidSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = Mathf.Clamp01(progress);
            fill.color = progress >= 1f ? BossRushUIColors.Success : BossRushUIColors.Accent;
            fill.raycastTarget = false;

            if (textWidth <= 0f) return;
            CreateRowText(parent, "ProgressText", text, 15f, BossRushUIColors.TextSecondary,
                left + barWidth + 12f, top - 7f, textWidth, 26f, false);
        }

        /// <summary>
        /// 分段按钮（远征目的地）：等宽并排，选中的一颗压一点 Accent 底色并描 WarningText 边，其余是次级按钮。
        /// </summary>
        private void SpawnSegments(Transform parent, List<PetNestCardData> items, float width)
        {
            GameObject row = ZombieModeUIHelper.CreateRect("Segments", parent, new Vector2(0.5f, 0.5f), new Vector2(width, 50f));
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            SetLayoutHeight(row, 50f);
            _spawned.Add(row);

            for (int i = 0; i < items.Count; i++)
            {
                PetNestCardData item = items[i];
                if (item == null) continue;
                Color fill = item.Selected
                    ? Color.Lerp(BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, 0.22f)
                    : BossRushUIColors.SurfaceRaised;
                Button segment = ZombieModeUIHelper.CreateButton(
                    "Segment", row.transform, item.Title,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 50f),
                    fill, 18f, new Vector2(190f, 46f),
                    item.OnCardClick != null ? new UnityEngine.Events.UnityAction(item.OnCardClick) : null,
                    item.OnCardClick != null);
                if (item.Selected)
                {
                    AddButtonStroke(segment, BossRushUIColors.WarningText);
                    TextMeshProUGUI label = segment.GetComponentInChildren<TextMeshProUGUI>();
                    if (label != null) label.fontStyle = FontStyles.Bold;
                }
                else
                {
                    BossRushUIKit.StyleSecondaryButton(segment);
                }
            }
        }

        /// <summary>
        /// 对比卡（远征风险档）：等宽三列并排，写时长、死亡率、产出，点卡片选中。
        /// 参照定价页的档位对比——三档放在一起才比得出来，旧写法是上下三张长卡、每张一个「出发」。
        /// </summary>
        private void SpawnColumns(Transform parent, List<PetNestCardData> items, float width)
        {
            int count = items.Count;
            float gap = 12f;
            float columnWidth = (width - gap * (count - 1)) / count;
            GameObject row = ZombieModeUIHelper.CreateRect("Columns", parent, new Vector2(0.5f, 0.5f), new Vector2(width, 160f));
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = gap;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            _spawned.Add(row);

            float tallest = 0f;
            List<GameObject> cards = new List<GameObject>(count);
            for (int i = 0; i < count; i++)
            {
                PetNestCardData item = items[i];
                GameObject card = BossRushUI.CreateCard("Column", row.transform, Vector2.zero, new Vector2(columnWidth, 160f),
                    BossRushUIColors.SurfaceRaised, item.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.Accent, true);
                if (item.Selected) SetCardStroke(card, BossRushUIColors.WarningText);
                if (item.OnCardClick != null) MakeCardClickable(card, item.OnCardClick);

                float textWidth = columnWidth - 40f;
                float y = 14f;
                CreateRowText(card.transform, "Title", item.Title, 22f, BossRushUIColors.TextPrimary, 22f, y, textWidth, 36f, true);
                y += 36f;
                CreateRowText(card.transform, "Subtitle", item.Subtitle, 16f,
                    item.IsDanger ? BossRushUIColors.DangerText : BossRushUIColors.Accent, 22f, y, textWidth, 28f, false);
                y += 32f;
                if (!string.IsNullOrEmpty(item.Body))
                {
                    TextMeshProUGUI body = ZombieModeUIHelper.CreateText(
                        "Body", card.transform, item.Body, 15f,
                        new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(22f, -y), new Vector2(textWidth, 26f),
                        TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
                    body.rectTransform.pivot = new Vector2(0f, 1f);
                    body.rectTransform.anchoredPosition = new Vector2(22f, -y);
                    BossRushUI.ApplyGameFont(body);
                    y += BossRushUI.MeasureTextHeight(body, textWidth, 26f);
                }
                tallest = Mathf.Max(tallest, y + 16f);
                cards.Add(card);
                _entranceTargets.Add(card);
            }
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, tallest);
            }
            SetLayoutHeight(row, tallest);
        }

        /// <summary>头像小卡网格（远征选崽）：4 列，一格 = 头像 + 名字 + 等级，点一下选中。</summary>
        private void SpawnStrip(Transform parent, List<PetNestCardData> items, float width)
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt((width + GridSpacing) / (StripCellSize.x + GridSpacing)));
            int rows = (items.Count + columns - 1) / columns;
            GameObject grid = ZombieModeUIHelper.CreateRect("Strip", parent, new Vector2(0.5f, 0.5f),
                new Vector2(width, StripCellSize.y));
            GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = StripCellSize;
            layout.spacing = new Vector2(GridSpacing, GridSpacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;
            layout.childAlignment = TextAnchor.UpperLeft;
            SetLayoutHeight(grid, rows * StripCellSize.y + Mathf.Max(0, rows - 1) * GridSpacing);
            _spawned.Add(grid);

            for (int i = 0; i < items.Count; i++)
            {
                PetNestCardData item = items[i];
                GameObject cell = BossRushUI.CreateCard("Chip", grid.transform, Vector2.zero, StripCellSize,
                    BossRushUIColors.SurfaceRaised, item.Shiny ? BossRushUIColors.RarityLegendary : BossRushUIColors.Accent, true);
                if (item.Selected) SetCardStroke(cell, BossRushUIColors.WarningText);
                if (item.OnCardClick != null) MakeCardClickable(cell, item.OnCardClick);
                float left = 16f;
                if (item.Icon != null)
                {
                    CreateIconFrame(cell.transform, "Portrait", item.Icon,
                        new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), 50f,
                        item.Shiny ? BossRushUIColors.RarityLegendary : BossRushUIColors.Stroke, false);
                    left = 74f;
                }
                float textWidth = StripCellSize.x - left - 10f;
                TextMeshProUGUI name = CreateRowText(cell.transform, "Name", item.Title, 17f, BossRushUIColors.TextPrimary,
                    left, 8f, textWidth, 30f, true);
                if (item.Shiny) PetNestShinyTextShimmer.Attach(name);
                CreateRowText(cell.transform, "Level", item.Subtitle, 14f, BossRushUIColors.TextSecondary,
                    left, 36f, textWidth, 26f, false);
                _entranceTargets.Add(cell);
            }
        }

        #endregion

        #region 卡片小件

        /// <summary>改 CreateCard 建的 Stroke 子物体的颜色（选中态）。</summary>
        private static void SetCardStroke(GameObject card, Color color)
        {
            Transform stroke = card.transform.Find("Stroke");
            Image strokeImage = stroke != null ? stroke.GetComponent<Image>() : null;
            if (strokeImage != null) strokeImage.color = color;
        }

        /// <summary>
        /// 卡片本身可点：走共享按钮入口（UA-16），悬停向 Accent 微微提亮、按下压暗，带官方 hover / click 音效与按下回弹。
        /// 卡上的按钮在更上层，点按钮不会触发卡片。
        /// </summary>
        private static void MakeCardClickable(GameObject card, Action onClick)
        {
            Image surface = card.GetComponent<Image>();
            Button cardButton = card.AddComponent<Button>();
            cardButton.targetGraphic = surface;
            Navigation navigation = cardButton.navigation;
            navigation.mode = Navigation.Mode.None;
            cardButton.navigation = navigation;
            Color rest = BossRushUIColors.SurfaceRaised;
            ZombieModeUIHelper.ApplyButtonColors(cardButton, rest,
                Color.Lerp(rest, BossRushUIColors.Accent, 0.14f), rest);
            cardButton.onClick.AddListener(new UnityEngine.Events.UnityAction(onClick));
        }

        /// <summary>第二 / 第三条身份色条：与 CreateCard 的强调竖条同形，挨着排。</summary>
        private static void AddIdentityRail(Transform card, string name, float x, Color color)
        {
            GameObject rail = ZombieModeUIHelper.CreateRect(
                name, card,
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(x, 0f), new Vector2(4f, -12f), new Vector2(0f, 0.5f));
            Image railImage = rail.AddComponent<Image>();
            railImage.color = color;
            BossRushUI.ApplyPanelSkin(railImage, 2, BossRushUISkinPart.Hairline);
            railImage.raycastTarget = false;
        }

        /// <summary>
        /// 批量放生的勾选框（UA-18）：行右侧居中 26 见方，勾上 = Danger 底 + 白色「√」，没勾 = 深底 + 描边。
        /// 不吃点击：点行本身就是勾选 / 取消。√ 是 GBK 收录字符。
        /// </summary>
        private static void CreateCheckbox(Transform card, bool ticked)
        {
            GameObject box = ZombieModeUIHelper.CreateRect(
                "Checkbox", card, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f), new Vector2(26f, 26f), new Vector2(1f, 0.5f));
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = ticked ? BossRushUIColors.Danger : BossRushUIColors.Surface;
            boxImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(boxImage, 6, BossRushUISkinPart.Button);
            BossRushUI.ApplyPanelStroke(boxImage, 6, BossRushUISkinPart.Button,
                ticked ? BossRushUIColors.DangerText : BossRushUIColors.Stroke);
            if (!ticked) return;
            TextMeshProUGUI mark = ZombieModeUIHelper.CreateText(
                "Tick", box.transform, "√", 18f,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            mark.margin = Vector4.zero;
            mark.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(mark);
        }

        private static bool TryParseHexColor(string hex, out Color color)
        {
            color = Color.white;
            int r, g, b;
            if (!PetNestChroma.TryParseHex(hex, out r, out g, out b)) return false;
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }

        #endregion

        #region 滚动位置

        /// <summary>同一页重绘前记下三个滚动区的位置（分页内容、巢列表、巢详情）。</summary>
        private float[] CaptureScroll()
        {
            Transform[] roots = { _contentRoot, _nestListRoot, _nestDetailRoot };
            float[] positions = new float[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                RectTransform rect = roots[i] as RectTransform;
                positions[i] = rect != null ? rect.anchoredPosition.y : 0f;
            }
            return positions;
        }

        /// <summary>
        /// 同页重绘（出战、勾选、放生后）留在原来看的位置，按新内容高度夹住；切页（positions 为 null）回到顶部。
        /// </summary>
        private void RestoreScroll(float[] positions)
        {
            Transform[] roots = { _contentRoot, _nestListRoot, _nestDetailRoot };
            for (int i = 0; i < roots.Length; i++)
            {
                RectTransform rect = roots[i] as RectTransform;
                if (rect == null || rect.parent == null) continue;
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                if (positions == null)
                {
                    ScrollRect scroll = rect.parent.GetComponent<ScrollRect>();
                    if (scroll != null) scroll.verticalNormalizedPosition = 1f;
                    continue;
                }
                RectTransform viewport = rect.parent as RectTransform;
                float maxScroll = Mathf.Max(0f, rect.rect.height - (viewport != null ? viewport.rect.height : 0f));
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, Mathf.Clamp(positions[i], 0f, maxScroll));
            }
        }

        #endregion
    }
}
