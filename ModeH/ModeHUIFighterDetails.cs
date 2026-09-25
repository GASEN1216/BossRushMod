// 鸭王杯专属对照页、押品网格与结算页主体。官方界面没有双队属性对照和多物品押注，沿用本模式共享模态外壳。
//
// 2026-09-25 owner 实测赛前看盘 / 押物品 / 结算三页全乱：根因是官方 ScrollRect prefab 的 content 自带竖排布局，
// 手动摆放的卡片被压成一列小圆点、属性与装备挤到左边被裁掉（StripLayoutControllers）。本轮顺带重排：
//   - 看盘页一屏放下双方：场次与赔率合成一行；每名选手一张横卡（立绘 + 名字 + 右侧装备图标 / 下方属性格），
//     双方卡型一致，放得下两行属性用高卡，否则一行八项的矮卡，再放不下才滚动；
//   - 押物品页是整卡可点的物品格，选中是主色描边 + 角标；
//   - 结算页按内容收高：奖品一排居中，战报单按行数收高，不再配一整块空框。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class ModeHStatData
    {
        public string Label;
        public string Text;
        public float Value;
        public float Maximum;
    }

    internal sealed class ModeHItemIconData
    {
        public Sprite Icon;
        public string Label;
        public int Count = 1;
        public int Quality;
    }

    internal static partial class ModeHUIPages
    {
        private const float MeasurementRowHeight = 25f;

        #region 滚动容器

        /// <summary>
        /// 摘掉官方 prefab 自带的布局组件（竖排 / 网格 / 自适应高度）。克隆体上的组件、不是资源；
        /// 同帧紧接着要写尺寸与子物体位置，延迟 Destroy 来不及，只能 DestroyImmediate（与 CodexView 同一处理）。
        /// </summary>
        private static void StripLayoutControllers(GameObject content)
        {
            if (content == null) return;
            LayoutGroup[] groups = content.GetComponents<LayoutGroup>();
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] != null) UnityEngine.Object.DestroyImmediate(groups[i]);
            }
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null) UnityEngine.Object.DestroyImmediate(fitter);
        }

        /// <summary>把已建好的滚动区收成 <paramref name="height"/> 高、顶边仍在 <paramref name="topY"/>（内容比视口矮时用）。</summary>
        private static void ShrinkScrollHost(GameObject host, float topY, float height, float offsetX)
        {
            if (host == null) return;
            ScrollRect scroll = host.GetComponentInParent<ScrollRect>();
            if (scroll == null) return;
            RectTransform rect = scroll.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            rect.anchoredPosition = new Vector2(offsetX, topY - height * 0.5f);
            RectTransform content = host.GetComponent<RectTransform>();
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }

        #endregion

        #region 属性与装备

        /// <summary>
        /// 选手属性：数字 + 同尺度细条（双方按同一最大值归一，见 NormalizeFighterStatScales）。
        /// <paramref name="columns"/> = 1 是选人卡里的竖排清单；大于 1 是看盘横卡里的属性格：
        /// 四列时一格「标签 数字」同一行，八列时标签在上、数字在下（英文标签放不进 70 像素的一行）。
        /// <paramref name="drawGear"/> 为 false 时装备由调用方画（看盘横卡把装备放在名字那一行的右边）。
        /// </summary>
        private static void CreateFighterMeasurements(Transform parent, ModeHCardData data,
            float width, float top, float availableHeight, bool compact, int columns = 1, bool drawGear = true,
            Color? barColor = null)
        {
            int statCount = data.Stats.Count;
            columns = Mathf.Clamp(columns, 1, Mathf.Max(1, statCount));
            int rows = (statCount + columns - 1) / columns;
            float gearHeight = drawGear && data.Equipment.Count > 0 ? (compact ? 40f : 68f) : 0f;
            bool grid = columns > 1;
            bool stacked = columns > 4;
            float gap = grid ? StatCellGap : 0f;
            float cellWidth = (width - (columns - 1) * gap) / columns;
            float rowHeight = grid
                ? (stacked ? StatStackedRowHeight : StatGridRowHeight)
                : Mathf.Min(MeasurementRowHeight, (availableHeight - gearHeight) / Mathf.Max(1, rows));
            float labelSize = compact ? 12f : (stacked ? 12f : (grid ? 14f : 15f));
            float valueSize = compact ? 12f : (grid ? 15f : 15f);
            Color fillColor = barColor.HasValue ? barColor.Value : BossRushUIColors.Accent;
            for (int i = 0; i < statCount; i++)
            {
                ModeHStatData stat = data.Stats[i];
                float left = -width * 0.5f + (i % columns) * (cellWidth + gap);
                float rowTop = top - (i / columns) * rowHeight;
                TextMeshProUGUI label;
                TextMeshProUGUI value;
                if (stacked)
                {
                    label = DetailText(parent, "StatLabel_" + i, stat.Label,
                        new Vector2(left, rowTop), new Vector2(cellWidth, 22f),
                        labelSize, BossRushUIColors.TextSecondary, TextAlignmentOptions.Left);
                    value = DetailText(parent, "StatValue_" + i, stat.Text,
                        new Vector2(left, rowTop - 19f), new Vector2(cellWidth, 24f),
                        valueSize, BossRushUIColors.TextPrimary, TextAlignmentOptions.Left);
                }
                else
                {
                    label = DetailText(parent, "StatLabel_" + i, stat.Label,
                        new Vector2(left, rowTop), new Vector2(cellWidth * 0.55f, rowHeight - 4f),
                        labelSize, BossRushUIColors.TextSecondary, TextAlignmentOptions.Left);
                    value = DetailText(parent, "StatValue_" + i, stat.Text,
                        new Vector2(left + cellWidth * 0.45f, rowTop), new Vector2(cellWidth * 0.55f, rowHeight - 4f),
                        valueSize, BossRushUIColors.TextPrimary, TextAlignmentOptions.Right);
                }
                label.margin = Vector4.zero;
                value.margin = Vector4.zero;
                GameObject track = ZombieModeUIHelper.CreateRect("StatTrack_" + i, parent,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(left, rowTop - rowHeight + (stacked ? 3f : (grid ? 5f : 2f))), new Vector2(cellWidth, 2f), new Vector2(0f, 1f));
                Image trackImage = track.AddComponent<Image>();
                trackImage.color = grid ? BossRushUIColors.Divider : BossRushUIColors.Stroke;
                trackImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(trackImage, 1, BossRushUISkinPart.Hairline);
                float fraction = stat.Maximum > 0f ? Mathf.Clamp01(stat.Value / stat.Maximum) : 0f;
                GameObject fill = ZombieModeUIHelper.CreateRect("Fill", track.transform,
                    Vector2.zero, new Vector2(fraction, 1f), Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
                Image fillImage = fill.AddComponent<Image>();
                fillImage.color = fillColor;
                fillImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(fillImage, 1, BossRushUISkinPart.Hairline);
            }
            if (!drawGear) return;
            float gearTop = top - rowHeight * rows - 8f;
            int count = data.Equipment.Count;
            float cell = Mathf.Min(compact ? 30f : 46f, (width - Mathf.Max(0, count - 1) * 4f) / Mathf.Max(1, count));
            for (int i = 0; i < count; i++)
            {
                float x = -width * 0.5f + cell * 0.5f + i * (cell + 4f);
                CreateItemIcon(parent, "Gear_" + i, data.Equipment[i],
                    new Vector2(x, gearTop - cell * 0.5f), cell, true, !compact);
            }
        }

        #endregion

        #region 看盘页：双方对照

        /// <summary>
        /// 赛前双方对照：页头下一行「第 N 场 / 6 · 胜利返还倍率 xN」，再一行本场规则小字；
        /// 下面左右两列（我方 / 敌方），中缝一枚 VS，每列列头写合计战力，列里每名选手一张横卡。
        /// </summary>
        private static void CreateMatchComparison(Transform surface, Vector2 panelSize,
            ModeHPageContent content, float topY)
        {
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            string info = MatchInfoOpen + (content.Body ?? string.Empty) + MatchInfoClose;
            if (!string.IsNullOrEmpty(content.HeadlineValue))
            {
                info += MatchInfoDivider + MatchOddsOpen + content.Headline + "  " + content.HeadlineValue + MatchOddsClose;
            }
            TextMeshProUGUI summary = DetailText(surface, "MatchSummary", info,
                new Vector2(-width * 0.5f, topY - panelSize.y * 0.5f), new Vector2(width, MatchInfoHeight),
                18f, BossRushUIColors.TextPrimary, TextAlignmentOptions.Center);
            summary.enableAutoSizing = false;
            summary.enableWordWrapping = false;
            topY -= MatchInfoHeight + 4f;
            if (!string.IsNullOrEmpty(content.MatchNote))
            {
                TextMeshProUGUI note = DetailText(surface, "MatchNote", content.MatchNote,
                    new Vector2(-width * 0.5f + 80f, topY - panelSize.y * 0.5f), new Vector2(width - 160f, 26f),
                    15f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Center);
                note.enableAutoSizing = false;
                float noteHeight = BossRushUI.MeasureTextHeight(note, width - 160f, 26f);
                note.rectTransform.sizeDelta = new Vector2(width - 160f, noteHeight);
                topY -= noteHeight + 6f;
            }
            topY -= 6f;

            float floor = -panelSize.y * 0.5f + GetFloorReserve(panelSize, content, GetActionBandReserve(panelSize, content));
            float height = Mathf.Max(160f, topY - floor - 6f);
            float columnWidth = (width - VersusGap) * 0.5f;
            // 两边同一种卡型：放得下两行属性用高卡，否则一行八项的矮卡，再放不下才滚动
            int most = Mathf.Max(content.PlayerFighters.Count, content.EnemyFighters.Count);
            float cardsHeight = height - ColumnHeaderHeight;
            bool tall = most * FighterCardTall + Mathf.Max(0, most - 1) * FighterCardGap <= cardsHeight;
            CreateFighterColumn(surface, panelSize, content.PlayerFighters, L10n.T("我方", "Your side"),
                content.PlayerSideNote, -(columnWidth + VersusGap) * 0.5f, columnWidth, topY, height, tall,
                BossRushUIColors.Accent);
            CreateFighterColumn(surface, panelSize, content.EnemyFighters, L10n.T("敌方", "Opponents"),
                content.EnemySideNote, (columnWidth + VersusGap) * 0.5f, columnWidth, topY, height, tall,
                EnemySideColor);
            TextMeshProUGUI versus = DetailText(surface, "Versus", "VS",
                new Vector2(-VersusGap * 0.5f, topY - panelSize.y * 0.5f), new Vector2(VersusGap, ColumnHeaderHeight - 8f),
                20f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Center);
            versus.fontStyle = FontStyles.Bold;
            versus.enableAutoSizing = false;
        }

        /// <summary>一列：列头（队名 + 右侧合计战力 + 一道细分隔线），下面每名选手一张横卡；放不下才进滚动区。</summary>
        private static void CreateFighterColumn(Transform surface, Vector2 panelSize,
            List<ModeHCardData> fighters, string title, string sideNote, float x, float width, float topY, float height,
            bool tall, Color sideColor)
        {
            GameObject column = ZombieModeUIHelper.CreateRect("FighterColumn", surface,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x, 0f), panelSize, new Vector2(0.5f, 0.5f));
            float headerTop = topY - panelSize.y * 0.5f;
            TextMeshProUGUI team = DetailText(column.transform, "Team", title,
                new Vector2(-width * 0.5f, headerTop), new Vector2(width * 0.5f, 32f),
                21f, sideColor, TextAlignmentOptions.MidlineLeft);
            team.enableAutoSizing = false;
            if (!string.IsNullOrEmpty(sideNote))
            {
                TextMeshProUGUI note = DetailText(column.transform, "TeamNote", sideNote,
                    new Vector2(0f, headerTop), new Vector2(width * 0.5f, 32f),
                    15f, BossRushUIColors.TextSecondary, TextAlignmentOptions.MidlineRight);
                note.enableAutoSizing = false;
            }
            GameObject rule = ZombieModeUIHelper.CreateSeparator("TeamRule", column.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, headerTop - 36f), 1f,
                BossRushUIColors.Divider);
            RectTransform ruleRect = rule.GetComponent<RectTransform>();
            ruleRect.sizeDelta = new Vector2(width, ruleRect.sizeDelta.y);

            topY -= ColumnHeaderHeight;
            height -= ColumnHeaderHeight;
            float cardHeight = tall ? FighterCardTall : FighterCardShort;
            float totalHeight = fighters.Count * (cardHeight + FighterCardGap) - FighterCardGap;
            Transform host = column.transform;
            float cardWidth = width;
            float firstTop = topY - panelSize.y * 0.5f;
            if (totalHeight > height)
            {
                host = CreateScrollHost(column.transform,
                    new Vector2(width + ModeHUI.SafeMargin * 2f, panelSize.y),
                    topY, height, totalHeight).transform;
                cardWidth = width - 24f;
                firstTop = 0f;
            }
            for (int i = 0; i < fighters.Count; i++)
            {
                ModeHCardData fighter = fighters[i];
                if (fighter == null) continue;
                GameObject card = BossRushUI.CreateCard("Fighter_" + i, host,
                    new Vector2(0f, firstTop - i * (cardHeight + FighterCardGap) - cardHeight * 0.5f),
                    new Vector2(cardWidth, cardHeight), BossRushUIColors.SurfaceRaised, sideColor, true);
                RectTransform cardRect = card.GetComponent<RectTransform>();
                cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 1f);
                BuildFighterRowCard(card.transform, fighter, cardWidth, tall, sideColor);
            }
        }

        /// <summary>一名选手的横卡：左上立绘，右边名字与定位，同一行最右是装备图标；下面是属性格。</summary>
        private static void BuildFighterRowCard(Transform card, ModeHCardData fighter, float cardWidth, bool tall, Color sideColor)
        {
            float inner = cardWidth - FighterCardPadding * 2f;
            float left = -inner * 0.5f;
            CreatePortrait(card, fighter, new Vector2(left + FighterPortraitSize * 0.5f, -FighterCardPadding),
                FighterPortraitSize);

            int gearCount = fighter.Equipment.Count;
            float nameLeft = left + FighterPortraitSize + 12f;
            float gearCell = FighterGearSize;
            float gearRoom = inner - FighterPortraitSize - 12f - FighterNameMinWidth - 12f;
            if (gearCount > 0 && gearCount * (gearCell + FighterGearGap) - FighterGearGap > gearRoom)
            {
                gearCell = Mathf.Max(24f, (gearRoom + FighterGearGap) / gearCount - FighterGearGap);
            }
            float gearWidth = gearCount > 0 ? gearCount * (gearCell + FighterGearGap) - FighterGearGap : 0f;
            float nameWidth = inner - FighterPortraitSize - 12f - (gearCount > 0 ? gearWidth + 12f : 0f);

            TextMeshProUGUI name = DetailText(card, "Name", fighter.Title,
                new Vector2(nameLeft, -FighterCardPadding - 2f), new Vector2(nameWidth, 30f),
                20f, BossRushUIColors.TextPrimary, TextAlignmentOptions.Left);
            name.enableAutoSizing = false;
            name.enableWordWrapping = false;
            TextMeshProUGUI role = DetailText(card, "Role", fighter.Subtitle,
                new Vector2(nameLeft, -FighterCardPadding - 30f), new Vector2(nameWidth, 24f),
                14f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Left);
            role.enableAutoSizing = false;
            role.enableWordWrapping = false;

            float gearCenterY = -FighterCardPadding - FighterPortraitSize * 0.5f;
            for (int i = 0; i < gearCount; i++)
            {
                float gx = inner * 0.5f - gearWidth + gearCell * 0.5f + i * (gearCell + FighterGearGap);
                CreateItemIcon(card, "Gear_" + i, fighter.Equipment[i], new Vector2(gx, gearCenterY), gearCell, true, false);
            }

            float statsTop = -FighterCardPadding - FighterPortraitSize - 10f;
            CreateFighterMeasurements(card, fighter, inner, statsTop, 0f, false, tall ? 4 : 8, false, sideColor);
        }

        #endregion

        #region 押物品页

        /// <summary>
        /// 押物品选择页：一句说明，下面是整卡可点的物品格（官方图标 + 右下数量 + 名字一行 + 估值小字）。
        /// 选中格描边换主色、底色染一点主色、右上角「√ 已押上」。格数随身上物品变化，放不下就滚动。
        /// </summary>
        private static void CreateItemBetGrid(Transform surface, Vector2 panelSize,
            ModeHPageContent content, float topY)
        {
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            TextMeshProUGUI hint = DetailText(surface, "StakeHint", content.Body,
                new Vector2(-width * 0.5f, topY - panelSize.y * 0.5f), new Vector2(width, 34f),
                17f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Center);
            hint.enableAutoSizing = false;
            topY -= 46f;
            if (content.Cards.Count == 0) return;

            float floor = -panelSize.y * 0.5f + GetFloorReserve(panelSize, content, GetActionBandReserve(panelSize, content));
            float gridWidth = width - 24f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((gridWidth + ItemCardGap) / (ItemCardMinWidth + ItemCardGap)));
            float cardWidth = (gridWidth - (columns - 1) * ItemCardGap) / columns;
            int rows = (content.Cards.Count + columns - 1) / columns;
            float total = rows * (ItemCardHeight + ItemCardGap) - ItemCardGap + 8f;
            Transform host = CreateScrollHost(surface, panelSize, topY, Mathf.Max(ItemCardHeight + 8f, topY - floor), total).transform;
            for (int i = 0; i < content.Cards.Count; i++)
            {
                ModeHCardData item = content.Cards[i];
                if (item == null) continue;
                float x = -gridWidth * 0.5f + cardWidth * 0.5f + (i % columns) * (cardWidth + ItemCardGap);
                GameObject card = BossRushUI.CreateCard("StakeItem_" + i, host,
                    new Vector2(x, -4f - (i / columns) * (ItemCardHeight + ItemCardGap) - ItemCardHeight * 0.5f),
                    new Vector2(cardWidth, ItemCardHeight), BossRushUIColors.SurfaceRaised,
                    item.IsSelected ? BossRushUIColors.Accent : ModeHUI.ResolveRarityColor(item.GameQuality), true);
                RectTransform cardRect = card.GetComponent<RectTransform>();
                cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 1f);
                CreateItemIcon(card.transform, "Item", new ModeHItemIconData
                { Icon = item.Icon, Count = item.Count, Quality = item.GameQuality },
                    new Vector2(0f, -14f - ItemIconSize * 0.5f), ItemIconSize, true, false);
                TextMeshProUGUI itemName = DetailText(card.transform, "Name", item.Title,
                    new Vector2(-cardWidth * 0.5f + 12f, -24f - ItemIconSize), new Vector2(cardWidth - 24f, 26f),
                    16f, BossRushUIColors.TextPrimary, TextAlignmentOptions.Center);
                itemName.enableWordWrapping = false;
                itemName.enableAutoSizing = false;
                TextMeshProUGUI worth = DetailText(card.transform, "Value", item.Subtitle,
                    new Vector2(-cardWidth * 0.5f + 12f, -50f - ItemIconSize), new Vector2(cardWidth - 24f, 40f),
                    13f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Top);
                worth.enableAutoSizing = false;
                if (item.IsSelected) AddSelectedBadge(card.transform, item.SelectedBadge);
                if (item.OnClick != null)
                    MakeCardClickable(card.transform, new UnityEngine.Events.UnityAction(item.OnClick), item.IsSelected);
            }
        }

        #endregion

        #region 结算页

        /// <summary>结算页主体：奖品一排居中 → 按行数收高的战报单 → （有的话）二选一卡片紧跟在下面。</summary>
        private static void CreateSettlementBody(Transform surface, Vector2 panelSize, ModeHPageContent content, float cursorY)
        {
            cursorY = CreateRewardIcons(surface, panelSize, content, cursorY);
            float bottom = CreateReportSheet(surface, panelSize, content, cursorY,
                content.Cards.Count > 0 ? 224f : float.PositiveInfinity);
            if (content.Cards.Count > 0)
            {
                CreateCardGrid(surface, panelSize, content, bottom - CardGap, false);
            }
        }

        /// <summary>本场收获：一排居中的物品格（图标 + 右下数量，不写名字：2026-09-25 owner「结算页少放文字」）；超过两排才滚动。</summary>
        private static float CreateRewardIcons(Transform surface, Vector2 panelSize, ModeHPageContent content, float topY)
        {
            int count = content.RewardItems.Count;
            if (count == 0) return topY;
            const float size = 72f;
            const float gap = 14f;
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((width - 20f + gap) / (size + gap)));
            int rows = (count + columns - 1) / columns;
            float height = Mathf.Min(2, rows) * (size + gap) - gap;
            bool scroll = rows > 2;
            Transform host = scroll
                ? CreateScrollHost(surface, panelSize, topY, height, rows * (size + gap)).transform
                : surface;
            float originY = scroll ? 0f : topY - panelSize.y * 0.5f;
            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int inRow = Mathf.Min(columns, count - row * columns);
                float startX = -((inRow - 1) * (size + gap)) * 0.5f;
                CreateItemIcon(host, "Reward_" + i, content.RewardItems[i],
                    new Vector2(startX + (i % columns) * (size + gap), originY - row * (size + gap) - size * 0.5f),
                    size, true, false);
            }
            return topY - height - 18f;
        }

        /// <summary>
        /// 战报单：与 CreateLineList 同一套行（两栏键值、段落、自动处理说明），但卡片按行数收高——
        /// 两三行战报不再配一整块空框（2026-09-25 owner 实测）。行多到放不下时照旧滚动。返回战报单下沿。
        /// </summary>
        private static float CreateReportSheet(Transform surface, Vector2 panelSize, ModeHPageContent content,
            float topY, float maximumHeight)
        {
            bool lead = content.ResultTone == ModeHResultTone.None && !string.IsNullOrEmpty(content.Body);
            if (!lead && content.Lines.Count == 0 && content.NoteLines.Count == 0) return topY;
            float available = Mathf.Min(maximumHeight, Mathf.Max(48f,
                topY - (-panelSize.y * 0.5f + GetFloorReserve(panelSize, content, GetActionBandReserve(panelSize, content)))));
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;

            GameObject sheet = BossRushUI.CreateCard("ModeH_LineSheet", surface,
                new Vector2(0f, topY - available * 0.5f), new Vector2(width, available),
                BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, false);
            Image sheetImage = sheet.GetComponent<Image>();
            if (sheetImage != null) sheetImage.raycastTarget = false;
            GameObject host = CreateScrollHost(surface, panelSize, topY, available, available);

            float rowWidth = width - 56f;
            bool animate = !content.Refresh;
            float used = ListPadding;
            int index = 0;
            if (lead) used = CreateListRow(host.transform, content.Body, rowWidth, used, index++, animate, RowKind.Lead);
            for (int i = 0; i < content.Lines.Count; i++)
            {
                used = CreateListRow(host.transform, content.Lines[i], rowWidth, used, index++, animate, RowKind.Line);
            }
            for (int i = 0; i < content.NoteLines.Count; i++)
            {
                used = CreateListRow(host.transform, content.NoteLines[i], rowWidth, used, index++, animate, RowKind.Note);
            }
            float fitted = used - LineRowGap + ListPadding;
            if (fitted < available)
            {
                ShrinkScrollHost(host, topY, fitted, 0f);
                RectTransform sheetRect = sheet.GetComponent<RectTransform>();
                sheetRect.sizeDelta = new Vector2(width, fitted);
                sheetRect.anchoredPosition = new Vector2(0f, topY - fitted * 0.5f);
                return topY - fitted;
            }
            host.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fitted);
            return topY - available;
        }

        /// <summary>
        /// 面板尺寸：结算页按内容估高（横幅 + 奖品排 + 战报行 + 动作带），夹在 [ReportMinHeight, ReportPanelSize.y]；
        /// 有二选一卡片时用满高。其余页固定主面板。估高只影响面板外框，行多时战报单自己会滚动。
        /// </summary>
        internal static Vector2 ResolvePanelSize(ModeHPage page, ModeHPageContent content)
        {
            if (page != ModeHPage.Settlement) return ModeHUI.MainPanelSize;
            Vector2 size = ModeHUI.ReportPanelSize;
            if (content == null || content.Cards.Count > 0) return size;
            bool result = content.ResultTone != ModeHResultTone.None;
            float header = result && ModeHPresentationAssetCache.GetBannerSprite() != null
                ? HeroInset + ResultHeroHeight + 16f
                : ModeHUI.SafeMargin + 76f + (result ? ResultCardHeight + 16f : 0f);
            float rewards = 0f;
            if (content.RewardItems.Count > 0)
            {
                int columns = Mathf.Max(1, Mathf.FloorToInt((size.x - ModeHUI.SafeMargin * 2f - 20f + 14f) / 86f));
                int rows = Mathf.Min(2, (content.RewardItems.Count + columns - 1) / columns);
                rewards = rows * 86f - 14f + 18f;
            }
            float sheet = 0f;
            int lines = content.Lines.Count + content.NoteLines.Count
                + (!result && !string.IsNullOrEmpty(content.Body) ? 1 : 0);
            if (lines > 0)
            {
                sheet = ListPadding * 2f;
                for (int i = 0; i < content.Lines.Count; i++) sheet += EstimateRowHeight(content.Lines[i]);
                for (int i = 0; i < content.NoteLines.Count; i++) sheet += EstimateRowHeight(content.NoteLines[i]);
                if (!result && !string.IsNullOrEmpty(content.Body)) sheet += EstimateRowHeight(content.Body);
                sheet -= LineRowGap;
            }
            float floor = GetFloorReserve(size, content, GetActionBandReserve(size, content)) + 12f;
            float height = Mathf.Clamp(Mathf.Ceil(header + rewards + sheet + floor), ReportMinHeight, size.y);
            return new Vector2(size.x, height);
        }

        /// <summary>估一行战报的高（单行 32；很长的一句按折成两三行估，只用于面板外框）。</summary>
        private static float EstimateRowHeight(string line)
        {
            int length = line != null ? line.Length : 0;
            int wraps = length > 90 ? 2 : (length > 46 ? 1 : 0);
            return LineHeight + wraps * 22f + LineRowGap;
        }

        #endregion

        #region 物品格与文字

        private static void CreateItemIcon(Transform parent, string name, ModeHItemIconData data,
            Vector2 position, float size, bool topAnchor, bool showLabel)
        {
            GameObject tile = BossRushUI.CreateCard(name, parent, position, new Vector2(size, size),
                BossRushUIColors.Header, ModeHUI.ResolveRarityColor(data.Quality), false);
            RectTransform tileRect = tile.GetComponent<RectTransform>();
            if (topAnchor) tileRect.anchorMin = tileRect.anchorMax = new Vector2(0.5f, 1f);
            // 品质色描边：图标格一眼分出档次（深色格 + 统一灰边时，金色与白色物品看起来一样）
            Transform stroke = tile.transform.Find("Stroke");
            Image strokeImage = stroke != null ? stroke.GetComponent<Image>() : null;
            if (strokeImage != null && data.Quality > 2) strokeImage.color = ModeHUI.ResolveRarityColor(data.Quality);
            GameObject art = ZombieModeUIHelper.CreateRect("Icon", tile.transform,
                Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8f, -8f), new Vector2(0.5f, 0.5f));
            Image icon = art.AddComponent<Image>();
            icon.sprite = data.Icon != null ? data.Icon : ModeHPresentationAssetCache.GetEmblemSprite();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (data.Count > 0)
            {
                TextMeshProUGUI count = ZombieModeUIHelper.CreateText("Count", tile.transform, data.Count.ToString(),
                    size >= 60f ? 18f : 12f, new Vector2(size * 0.5f - 18f, -size * 0.5f + 16f),
                    new Vector2(32f, 28f), TextAlignmentOptions.BottomRight, BossRushUIColors.TextPrimary);
                count.enableAutoSizing = false;
                BossRushUI.ApplyGameFont(count);
                BossRushUIKit.ApplyWorldTextOutline(count);
            }
            if (showLabel) DetailText(tile.transform, "Slot", data.Label,
                new Vector2(-size * 0.5f - 5f, -size - 2f), new Vector2(size + 10f, 24f),
                11f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Center);
        }

        private static TextMeshProUGUI DetailText(Transform parent, string name, string value, Vector2 position,
            Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            GameObject root = ZombieModeUIHelper.CreateRect(name, parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position, size, new Vector2(0f, 1f));
            TextMeshProUGUI label = ZombieModeUIHelper.CreateTMPText(root, value ?? string.Empty, fontSize, alignment, color);
            BossRushUI.ApplyGameFont(label);
            label.raycastTarget = false;
            return label;
        }

        #endregion

        #region 常量

        /// <summary>看盘页场次 + 赔率那一行（24 号赔率字，单行至少 1.45×24+4≈39）。</summary>
        private const float MatchInfoHeight = 40f;
        /// <summary>两列中缝（放一枚 VS）。</summary>
        private const float VersusGap = 56f;
        /// <summary>列头：队名 21 号一行 + 细分隔线。</summary>
        private const float ColumnHeaderHeight = 44f;
        private const float FighterCardPadding = 14f;
        private const float FighterPortraitSize = 52f;
        /// <summary>高卡：立绘行 + 两行四列属性；矮卡：立绘行 + 一行八列（标签在上、数字在下）。</summary>
        private const float FighterCardTall = 144f;
        private const float FighterCardShort = 130f;
        private const float FighterCardGap = 12f;
        private const float FighterGearSize = 38f;
        private const float FighterGearGap = 6f;
        /// <summary>名字区最窄宽度：装备多时先缩图标，名字不让位到读不出来。</summary>
        private const float FighterNameMinWidth = 150f;
        /// <summary>属性格：四列一格高 30（14/15 号字一行 + 细条）；八列一格高 46（标签一行 + 数字一行 + 细条）。</summary>
        private const float StatGridRowHeight = 30f;
        private const float StatStackedRowHeight = 46f;
        private const float StatCellGap = 12f;
        private const float ItemCardHeight = 176f;
        private const float ItemCardMinWidth = 172f;
        private const float ItemCardGap = 14f;
        private const float ItemIconSize = 72f;
        /// <summary>结算页收高的下限（横幅 + 一两行战报 + 动作带也不显得局促）。</summary>
        private const float ReportMinHeight = 470f;
        /// <summary>敌方的强调色（竖条、属性条）：暖红，和我方主色青一眼分开。</summary>
        private static readonly Color EnemySideColor = BossRushUIColors.DangerText;
        private static readonly string MatchInfoOpen =
            "<size=18><color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary) + ">";
        private const string MatchInfoClose = "</color></size>";
        private static readonly string MatchInfoDivider =
            "<size=18><color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary) + ">　·　</color></size>";
        private static readonly string MatchOddsOpen =
            "<size=24><color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText) + ">";
        private const string MatchOddsClose = "</color></size>";

        #endregion
    }
}
