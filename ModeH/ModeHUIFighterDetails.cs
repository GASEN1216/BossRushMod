// 鸭王杯专属对照页与押品网格。官方界面没有双队属性对照和多物品押注，沿用本模式共享模态外壳。
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
        private const float ComparisonCardHeight = 390f;

        private static void CreateFighterMeasurements(Transform parent, ModeHCardData data,
            float width, float top, float availableHeight, bool compact)
        {
            float gearHeight = data.Equipment.Count > 0 ? (compact ? 40f : 68f) : 0f;
            float rowHeight = Mathf.Min(MeasurementRowHeight,
                (availableHeight - gearHeight) / Mathf.Max(1, data.Stats.Count));
            for (int i = 0; i < data.Stats.Count; i++)
            {
                ModeHStatData stat = data.Stats[i];
                float rowTop = top - i * rowHeight;
                TextMeshProUGUI label = DetailText(parent, "StatLabel_" + i, stat.Label,
                    new Vector2(-width * 0.5f, rowTop), new Vector2(width * 0.52f, rowHeight),
                    compact ? 12f : 15f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Left);
                label.margin = Vector4.zero;
                TextMeshProUGUI value = DetailText(parent, "StatValue_" + i, stat.Text,
                    new Vector2(width * 0.02f, rowTop), new Vector2(width * 0.48f, rowHeight),
                    compact ? 12f : 15f, BossRushUIColors.TextPrimary, TextAlignmentOptions.Right);
                value.margin = Vector4.zero;
                GameObject track = ZombieModeUIHelper.CreateRect("StatTrack_" + i, parent,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-width * 0.5f, rowTop - rowHeight + 2f), new Vector2(width, 2f), new Vector2(0f, 1f));
                Image trackImage = track.AddComponent<Image>();
                trackImage.color = BossRushUIColors.Stroke;
                trackImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(trackImage, 1, BossRushUISkinPart.Hairline);
                float fraction = stat.Maximum > 0f ? Mathf.Clamp01(stat.Value / stat.Maximum) : 0f;
                GameObject fill = ZombieModeUIHelper.CreateRect("Fill", track.transform,
                    Vector2.zero, new Vector2(fraction, 1f), Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
                Image fillImage = fill.AddComponent<Image>();
                fillImage.color = BossRushUIColors.Accent;
                fillImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(fillImage, 1, BossRushUISkinPart.Hairline);
            }
            float gearTop = top - rowHeight * data.Stats.Count - 8f;
            int count = data.Equipment.Count;
            float cell = Mathf.Min(compact ? 30f : 46f, (width - Mathf.Max(0, count - 1) * 4f) / Mathf.Max(1, count));
            for (int i = 0; i < count; i++)
            {
                float x = -width * 0.5f + cell * 0.5f + i * (cell + 4f);
                CreateItemIcon(parent, "Gear_" + i, data.Equipment[i],
                    new Vector2(x, gearTop - cell * 0.5f), cell, true, !compact);
            }
        }

        private static void CreateMatchComparison(Transform surface, Vector2 panelSize,
            ModeHPageContent content, float topY)
        {
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            DetailText(surface, "MatchSummary", content.Body,
                new Vector2(-width * 0.5f, topY - panelSize.y * 0.5f), new Vector2(width, 34f),
                18f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Center);
            topY -= 44f;
            if (!string.IsNullOrEmpty(content.HeadlineValue))
            {
                DetailText(surface, "MatchOdds", content.Headline + "  " + content.HeadlineValue,
                    new Vector2(-width * 0.5f, topY - panelSize.y * 0.5f), new Vector2(width, 40f),
                    24f, BossRushUIColors.WarningText, TextAlignmentOptions.Center);
                topY -= 48f;
            }
            float floor = -panelSize.y * 0.5f + GetFloorReserve(panelSize, content, GetActionBandReserve(panelSize, content));
            float height = Mathf.Max(120f, topY - floor - 10f);
            float halfWidth = (width - 24f) * 0.5f;
            CreateFighterColumn(surface, panelSize, content.PlayerFighters, L10n.T("我方", "Your team"),
                -(halfWidth + 24f) * 0.5f, halfWidth, topY, height);
            CreateFighterColumn(surface, panelSize, content.EnemyFighters, L10n.T("敌方", "Opponents"),
                (halfWidth + 24f) * 0.5f, halfWidth, topY, height);
        }

        private static void CreateFighterColumn(Transform surface, Vector2 panelSize,
            List<ModeHCardData> fighters, string title, float x, float width, float topY, float height)
        {
            GameObject column = ZombieModeUIHelper.CreateRect("FighterColumn", surface,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x, 0f), panelSize, new Vector2(0.5f, 0.5f));
            DetailText(column.transform, "Team", title,
                new Vector2(-width * 0.5f, topY - panelSize.y * 0.5f), new Vector2(width, 34f),
                21f, BossRushUIColors.Accent, TextAlignmentOptions.Left);
            topY -= 40f;
            height -= 40f;
            float totalHeight = fighters.Count * (ComparisonCardHeight + 14f);
            Transform host = CreateScrollHost(column.transform,
                new Vector2(width + ModeHUI.SafeMargin * 2f, panelSize.y),
                topY, height, totalHeight).transform;
            for (int i = 0; i < fighters.Count; i++)
            {
                ModeHCardData fighter = fighters[i];
                GameObject card = BossRushUI.CreateCard("Fighter_" + i, host,
                    new Vector2(0f, -i * (ComparisonCardHeight + 14f) - ComparisonCardHeight * 0.5f),
                    new Vector2(width - 24f, ComparisonCardHeight), BossRushUIColors.SurfaceRaised,
                    BossRushUIColors.Stroke, false);
                card.GetComponent<RectTransform>().anchorMin = card.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1f);
                float inner = width - 60f;
                CreatePortrait(card.transform, fighter, new Vector2(-inner * 0.5f + 36f, -14f), 72f);
                DetailText(card.transform, "Name", fighter.Title,
                    new Vector2(-inner * 0.5f + 88f, -14f), new Vector2(inner - 88f, 40f),
                    23f, BossRushUIColors.TextPrimary, TextAlignmentOptions.Left);
                DetailText(card.transform, "Role", fighter.Subtitle,
                    new Vector2(-inner * 0.5f + 88f, -54f), new Vector2(inner - 88f, 30f),
                    16f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Left);
                CreateFighterMeasurements(card.transform, fighter, inner, -98f, ComparisonCardHeight - 114f, false);
            }
        }

        private static void CreateItemBetGrid(Transform surface, Vector2 panelSize,
            ModeHPageContent content, float topY)
        {
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            DetailText(surface, "StakeHint", content.Body,
                new Vector2(-width * 0.5f, topY - panelSize.y * 0.5f), new Vector2(width, 56f),
                18f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Left);
            topY -= 66f;
            float floor = -panelSize.y * 0.5f + GetFloorReserve(panelSize, content, GetActionBandReserve(panelSize, content));
            const float cardHeight = 164f;
            const float gap = 12f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((width - 24f) / 180f));
            float cardWidth = (width - 24f - (columns - 1) * gap) / columns;
            int rows = (content.Cards.Count + columns - 1) / columns;
            float total = rows * (cardHeight + gap);
            Transform host = CreateScrollHost(surface, panelSize, topY, Mathf.Max(80f, topY - floor), total).transform;
            for (int i = 0; i < content.Cards.Count; i++)
            {
                ModeHCardData item = content.Cards[i];
                float x = -(width - 24f) * 0.5f + cardWidth * 0.5f + (i % columns) * (cardWidth + gap);
                GameObject card = BossRushUI.CreateCard("StakeItem_" + i, host,
                    new Vector2(x, -(i / columns) * (cardHeight + gap) - cardHeight * 0.5f),
                    new Vector2(cardWidth, cardHeight), BossRushUIColors.SurfaceRaised,
                    item.IsSelected ? BossRushUIColors.Accent : ModeHUI.ResolveRarityColor(item.GameQuality), true);
                card.GetComponent<RectTransform>().anchorMin = card.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1f);
                CreateItemIcon(card.transform, "Item", new ModeHItemIconData
                { Icon = item.Icon, Count = item.Count, Quality = item.GameQuality }, new Vector2(0f, -44f), 64f, true, false);
                DetailText(card.transform, "Name", item.Title, new Vector2(-cardWidth * 0.5f + 10f, -84f),
                    new Vector2(cardWidth - 20f, 30f), 16f, BossRushUIColors.TextPrimary, TextAlignmentOptions.Center);
                DetailText(card.transform, "Value", item.Subtitle, new Vector2(-cardWidth * 0.5f + 10f, -116f),
                    new Vector2(cardWidth - 20f, 40f), 13f, BossRushUIColors.TextSecondary, TextAlignmentOptions.Center);
                if (item.IsSelected) AddSelectedBadge(card.transform, "√");
                if (item.OnClick != null) MakeCardClickable(card.transform, new UnityEngine.Events.UnityAction(item.OnClick));
            }
        }

        private static float CreateRewardIcons(Transform surface, Vector2 panelSize, ModeHPageContent content, float topY)
        {
            if (content.RewardItems.Count == 0) return topY;
            const float size = 68f;
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(width / (size + 12f)));
            int rows = (content.RewardItems.Count + columns - 1) / columns;
            float height = Mathf.Min(2, rows) * (size + 12f);
            Transform host = CreateScrollHost(surface, panelSize, topY, height, rows * (size + 12f)).transform;
            for (int i = 0; i < content.RewardItems.Count; i++)
                CreateItemIcon(host, "Reward_" + i, content.RewardItems[i],
                    new Vector2(-width * 0.5f + size * 0.5f + 12f + (i % columns) * (size + 12f),
                        -(i / columns) * (size + 12f) - size * 0.5f), size, true, false);
            return topY - height - 16f;
        }

        private static void CreateItemIcon(Transform parent, string name, ModeHItemIconData data,
            Vector2 position, float size, bool topAnchor, bool showLabel)
        {
            GameObject tile = BossRushUI.CreateCard(name, parent, position, new Vector2(size, size),
                BossRushUIColors.Header, ModeHUI.ResolveRarityColor(data.Quality), false);
            RectTransform tileRect = tile.GetComponent<RectTransform>();
            if (topAnchor) tileRect.anchorMin = tileRect.anchorMax = new Vector2(0.5f, 1f);
            GameObject art = ZombieModeUIHelper.CreateRect("Icon", tile.transform,
                Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8f, -8f), new Vector2(0.5f, 0.5f));
            Image icon = art.AddComponent<Image>();
            icon.sprite = data.Icon != null ? data.Icon : ModeHPresentationAssetCache.GetEmblemSprite();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (data.Count > 0)
            {
                TextMeshProUGUI count = ZombieModeUIHelper.CreateText("Count", tile.transform, data.Count.ToString(),
                    size >= 60f ? 18f : 12f, new Vector2(size * 0.5f - 18f, -size * 0.5f + 18f),
                    new Vector2(32f, 32f), TextAlignmentOptions.BottomRight, BossRushUIColors.TextPrimary);
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
    }
}
