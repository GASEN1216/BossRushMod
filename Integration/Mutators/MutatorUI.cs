// ============================================================================
// MutatorUI.cs - 每局变异词条 UI 展示
// ============================================================================
// 模块说明：
//   开局后在 UI 左侧保留本局词条简要提示，
//   鼠标悬停左侧提示时显示详细效果，移开后自动隐藏。
//   使用 IMGUI 实现，无需额外 Canvas 或 Prefab。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 变异词条 UI 展示（IMGUI 实现，轻量无依赖）
    /// </summary>
    public static class MutatorUI
    {
        // 缓存的词条信息（避免每帧调用 GetDisplayName）
        private static readonly List<CachedMutatorInfo> _cachedInfos = new List<CachedMutatorInfo>();

        private struct CachedMutatorInfo
        {
            public MutatorCategory Category;
            public string DisplayName;
            public string Description;
            public string CategoryLabel;
        }

        // ═══════════════════════════════════════════
        // 角落图标状态
        // ═══════════════════════════════════════════

        private static bool _cornerVisible;

        // 样式缓存
        private static GUIStyle _cornerHeaderStyle;
        private static GUIStyle _cornerCountStyle;
        private static GUIStyle _cornerCategoryStyle;
        private static GUIStyle _cornerTextStyle;
        private static GUIStyle _detailBoxStyle;
        private static GUIStyle _detailTitleStyle;
        private static GUIStyle _detailCategoryStyle;
        private static GUIStyle _detailNameStyle;
        private static GUIStyle _detailDescriptionStyle;
        private static Rect _detailHoverRect;
        private static Vector2 _detailScrollPosition;
        private static bool _stylesInitialized;

        // 分类颜色
        private static readonly Color ColorEnemyBuff = new Color(0.96f, 0.33f, 0.28f, 1f);
        private static readonly Color ColorPlayerBoon = new Color(0.30f, 0.84f, 0.62f, 1f);
        private static readonly Color ColorEnvironmentRule = new Color(1f, 0.72f, 0.25f, 1f);
        private static readonly Color ColorPanel = new Color(0.035f, 0.045f, 0.055f, 0.91f);
        private static readonly Color ColorRow = new Color(0.12f, 0.14f, 0.16f, 0.10f);
        private static readonly Color ColorRowHover = new Color(0.20f, 0.23f, 0.26f, 0.42f);
        private static readonly Color ColorMutedText = new Color(0.72f, 0.76f, 0.79f, 1f);

        // ═══════════════════════════════════════════
        // 公共方法
        // ═══════════════════════════════════════════

        /// <summary>
        /// 准备本局词条 UI（历史方法名保留给现有调用点）
        /// </summary>
        public static void ShowBanner()
        {
            _cachedInfos.Clear();
            _detailScrollPosition = Vector2.zero;

            var mutators = MutatorManager.GetActiveMutators();
            if (mutators == null || mutators.Count == 0) return;

            for (int i = 0; i < mutators.Count; i++)
            {
                string displayName = mutators[i].GetDisplayName();
                string description = mutators[i].GetDescription();
                MutatorCategory category = mutators[i].Category;

                _cachedInfos.Add(new CachedMutatorInfo
                {
                    Category = category,
                    DisplayName = displayName,
                    Description = description,
                    CategoryLabel = GetCategoryLabel(category)
                });
            }

            _cornerVisible = true;
        }

        /// <summary>
        /// 隐藏所有 UI（模式结束时调用）
        /// </summary>
        public static void HideAll()
        {
            _cornerVisible = false;
            _cachedInfos.Clear();
            _detailHoverRect = Rect.zero;
            _detailScrollPosition = Vector2.zero;
        }

        /// <summary>
        /// 在 OnGUI 中调用：绘制词条 UI
        /// </summary>
        public static void DrawGUI()
        {
            if (!MutatorManager.IsActive) return;
            if (_cachedInfos.Count == 0) return;

            EnsureStyles();

            // 绘制角落图标（持续显示直到模式结束）
            if (_cornerVisible)
            {
                int hoveredIndex;
                Rect cornerRect = DrawCornerIcons(out hoveredIndex);
                if (IsMouseOver(cornerRect) ||
                    (_detailHoverRect.width > 0f && IsMouseOver(_detailHoverRect)))
                {
                    _detailHoverRect = DrawHoverDetails(cornerRect, hoveredIndex);
                }
                else
                {
                    _detailHoverRect = Rect.zero;
                }
            }
        }

        // ═══════════════════════════════════════════
        // 绘制方法
        // ═══════════════════════════════════════════

        private static Rect DrawCornerIcons(out int hoveredIndex)
        {
            const float panelPadding = 8f;
            const float headerHeight = 23f;
            const float rowHeight = 29f;
            const float rowSpacing = 3f;
            float panelWidth = Mathf.Clamp(Screen.width * 0.12f, 188f, 224f);
            float panelHeight = panelPadding * 2f + headerHeight +
                                _cachedInfos.Count * rowHeight +
                                Mathf.Max(0, _cachedInfos.Count - 1) * rowSpacing;
            float startX = Mathf.Max(12f, Screen.width * 0.008f);
            float desiredY = Screen.height * 0.24f;
            float startY = Mathf.Clamp(desiredY, 88f, Mathf.Max(88f, Screen.height - panelHeight - 16f));
            Rect panelRect = new Rect(startX, startY, panelWidth, panelHeight);
            hoveredIndex = -1;

            Color prevColor = GUI.color;
            Color prevContentColor = GUI.contentColor;
            GUI.color = Color.white;
            Rect headerRect = new Rect(
                panelRect.x + panelPadding,
                panelRect.y + panelPadding - 1f,
                panelRect.width - panelPadding * 2f,
                headerHeight);
            GUI.contentColor = Color.white;
            GUI.Label(headerRect, L10n.T("本局变异", "ACTIVE MUTATORS"), _cornerHeaderStyle);
            GUI.contentColor = ColorMutedText;
            GUI.Label(headerRect, _cachedInfos.Count.ToString(), _cornerCountStyle);
            FillRect(
                new Rect(headerRect.x, headerRect.yMax - 1f, Mathf.Min(58f, headerRect.width), 1f),
                new Color(ColorEnvironmentRule.r, ColorEnvironmentRule.g, ColorEnvironmentRule.b, 0.72f));

            Vector2 mousePosition = GetGuiMousePosition();
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                var info = _cachedInfos[i];
                float y = headerRect.yMax + i * (rowHeight + rowSpacing);
                Rect rowRect = new Rect(
                    panelRect.x + panelPadding,
                    y,
                    panelRect.width - panelPadding * 2f,
                    rowHeight);
                bool hovered = rowRect.Contains(mousePosition);
                if (hovered)
                {
                    hoveredIndex = i;
                }

                Color catColor = GetCategoryColor(info.Category);
                if (hovered)
                {
                    FillRect(rowRect, ColorRowHover);
                }
                FillRect(
                    new Rect(rowRect.x, rowRect.y + 5f, 2f, rowRect.height - 10f),
                    new Color(catColor.r, catColor.g, catColor.b, 0.78f));

                Rect categoryRect = new Rect(rowRect.x + 9f, rowRect.y, 47f, rowRect.height);
                GUI.contentColor = catColor;
                GUI.Label(categoryRect, info.CategoryLabel, _cornerCategoryStyle);

                Rect textRect = new Rect(categoryRect.xMax + 3f, rowRect.y, rowRect.xMax - categoryRect.xMax - 9f, rowRect.height);
                GUI.contentColor = Color.white;
                GUI.Label(textRect, info.DisplayName, _cornerTextStyle);
            }

            GUI.contentColor = prevContentColor;
            GUI.color = prevColor;
            return panelRect;
        }

        private static Rect DrawHoverDetails(Rect anchorRect, int hoveredIndex)
        {
            float padding = 12f;
            float titleHeight = 27f;
            float spacing = 7f;
            float panelWidth = Mathf.Min(480f, Mathf.Max(300f, Screen.width - anchorRect.xMax - 20f));
            float x = anchorRect.xMax + 8f;

            if (x + panelWidth > Screen.width - 8f)
            {
                panelWidth = Mathf.Min(460f, Mathf.Max(280f, Screen.width - 16f));
                x = Mathf.Max(8f, Screen.width - panelWidth - 8f);
            }

            float contentWidth = Mathf.Max(120f, panelWidth - padding * 2f);
            float entriesHeight = CalculateDetailsHeight(contentWidth, spacing);

            float fixedHeight = padding + titleHeight + spacing + padding;
            float panelHeight = Mathf.Min(fixedHeight + entriesHeight, Mathf.Max(180f, Screen.height - 16f));

            float maxY = Mathf.Max(8f, Screen.height - panelHeight - 8f);
            float y = Mathf.Clamp(anchorRect.yMin, 8f, maxY);

            Color prevColor = GUI.color;
            Color prevContentColor = GUI.contentColor;

            Rect bgRect = new Rect(x, y, panelWidth, panelHeight);
            GUI.color = new Color(1f, 1f, 1f, 0.95f);
            GUI.Box(bgRect, GUIContent.none, _detailBoxStyle);
            GUI.color = prevColor;

            Rect titleRect = new Rect(x + padding, y + padding * 0.5f, contentWidth, titleHeight);
            GUI.contentColor = Color.white;
            GUI.Label(titleRect, L10n.T("本局变异词条", "Mutators Active"), _detailTitleStyle);

            Rect scrollViewport = new Rect(
                x + padding,
                y + padding + titleHeight + spacing,
                contentWidth,
                Mathf.Max(80f, panelHeight - fixedHeight));
            float scrollContentWidth = entriesHeight > scrollViewport.height
                ? Mathf.Max(120f, contentWidth - 16f)
                : contentWidth;
            if (scrollContentWidth < contentWidth)
            {
                entriesHeight = CalculateDetailsHeight(scrollContentWidth, spacing);
            }
            _detailScrollPosition = GUI.BeginScrollView(
                scrollViewport,
                _detailScrollPosition,
                new Rect(0f, 0f, scrollContentWidth, entriesHeight),
                false,
                entriesHeight > scrollViewport.height);

            float entryY = 0f;
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                var info = _cachedInfos[i];
                float descriptionHeight = _detailDescriptionStyle.CalcHeight(
                    new GUIContent(info.Description),
                    scrollContentWidth - 20f);
                float entryHeight = Mathf.Max(55f, 35f + descriptionHeight);
                Rect entryRect = new Rect(0f, entryY, scrollContentWidth, entryHeight);
                Color categoryColor = GetCategoryColor(info.Category);

                FillRect(entryRect, i == hoveredIndex ? ColorRowHover : ColorRow);
                FillRect(
                    new Rect(entryRect.x, entryRect.y + 5f, 2f, entryRect.height - 10f),
                    new Color(categoryColor.r, categoryColor.g, categoryColor.b, 0.82f));
                FillRect(
                    new Rect(entryRect.x + 10f, entryRect.yMax - 1f, entryRect.width - 10f, 1f),
                    new Color(1f, 1f, 1f, 0.08f));

                Rect categoryRect = new Rect(entryRect.x + 10f, entryRect.y + 5f, 52f, 20f);
                GUI.contentColor = categoryColor;
                GUI.Label(categoryRect, info.CategoryLabel, _detailCategoryStyle);

                Rect nameRect = new Rect(categoryRect.xMax + 4f, entryRect.y + 5f, entryRect.xMax - categoryRect.xMax - 14f, 20f);
                GUI.contentColor = Color.white;
                GUI.Label(nameRect, info.DisplayName, _detailNameStyle);

                Rect descriptionRect = new Rect(entryRect.x + 10f, entryRect.y + 28f, entryRect.width - 20f, descriptionHeight);
                GUI.contentColor = ColorMutedText;
                GUI.Label(descriptionRect, info.Description, _detailDescriptionStyle);
                entryY += entryHeight + spacing;
            }

            GUI.EndScrollView();

            GUI.contentColor = prevContentColor;

            // Include the small gap between the compact list and detail panel so the
            // hover state remains stable while the cursor moves into the scroll area.
            Rect hoverRect = bgRect;
            if (hoverRect.xMin > anchorRect.xMax)
            {
                hoverRect.xMin = anchorRect.xMax;
            }
            return hoverRect;
        }

        private static float CalculateDetailsHeight(float contentWidth, float spacing)
        {
            float height = 0f;
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                float descriptionHeight = _detailDescriptionStyle.CalcHeight(
                    new GUIContent(_cachedInfos[i].Description),
                    contentWidth - 20f);
                height += Mathf.Max(55f, 35f + descriptionHeight);
                if (i < _cachedInfos.Count - 1)
                {
                    height += spacing;
                }
            }

            return height;
        }

        private static Vector2 GetGuiMousePosition()
        {
            Event current = Event.current;
            if (current != null)
            {
                return current.mousePosition;
            }

            Vector3 mousePosition = Input.mousePosition;
            return new Vector2(mousePosition.x, Screen.height - mousePosition.y);
        }

        private static void FillRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = previousColor;
        }

        private static bool IsMouseOver(Rect rect)
        {
            return rect.Contains(GetGuiMousePosition());
        }

        // ═══════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════

        private static Color GetCategoryColor(MutatorCategory category)
        {
            switch (category)
            {
                case MutatorCategory.EnemyBuff: return ColorEnemyBuff;
                case MutatorCategory.PlayerBoon: return ColorPlayerBoon;
                case MutatorCategory.EnvironmentRule: return ColorEnvironmentRule;
                default: return Color.white;
            }
        }

        private static string GetCategoryLabel(MutatorCategory category)
        {
            switch (category)
            {
                case MutatorCategory.EnemyBuff: return L10n.T("敌方", "ENEMY");
                case MutatorCategory.PlayerBoon: return L10n.T("增益", "BOON");
                case MutatorCategory.EnvironmentRule: return L10n.T("规则", "RULE");
                default: return L10n.T("其他", "OTHER");
            }
        }

        private static void EnsureStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _cornerHeaderStyle = new GUIStyle(GUI.skin.label);
            _cornerHeaderStyle.fontSize = 11;
            _cornerHeaderStyle.fontStyle = FontStyle.Bold;
            _cornerHeaderStyle.alignment = TextAnchor.MiddleLeft;
            _cornerHeaderStyle.normal.textColor = Color.white;

            _cornerCountStyle = new GUIStyle(_cornerHeaderStyle);
            _cornerCountStyle.fontSize = 11;
            _cornerCountStyle.alignment = TextAnchor.MiddleRight;

            _cornerCategoryStyle = new GUIStyle(GUI.skin.label);
            _cornerCategoryStyle.fontSize = 10;
            _cornerCategoryStyle.fontStyle = FontStyle.Bold;
            _cornerCategoryStyle.alignment = TextAnchor.MiddleLeft;
            _cornerCategoryStyle.clipping = TextClipping.Clip;

            // 角落文字
            _cornerTextStyle = new GUIStyle(GUI.skin.label);
            _cornerTextStyle.fontSize = 12;
            _cornerTextStyle.fontStyle = FontStyle.Bold;
            _cornerTextStyle.alignment = TextAnchor.MiddleLeft;
            _cornerTextStyle.clipping = TextClipping.Clip;
            _cornerTextStyle.normal.textColor = Color.white;

            // 悬停详情背景框
            _detailBoxStyle = new GUIStyle(GUI.skin.box);
            Texture2D detailBg = new Texture2D(1, 1);
            detailBg.SetPixel(0, 0, new Color(ColorPanel.r, ColorPanel.g, ColorPanel.b, 0.97f));
            detailBg.Apply();
            _detailBoxStyle.normal.background = detailBg;

            // 悬停详情标题
            _detailTitleStyle = new GUIStyle(GUI.skin.label);
            _detailTitleStyle.fontSize = 14;
            _detailTitleStyle.fontStyle = FontStyle.Bold;
            _detailTitleStyle.alignment = TextAnchor.MiddleLeft;
            _detailTitleStyle.normal.textColor = Color.white;

            _detailCategoryStyle = new GUIStyle(_cornerCategoryStyle);
            _detailCategoryStyle.fontSize = 10;

            _detailNameStyle = new GUIStyle(GUI.skin.label);
            _detailNameStyle.fontSize = 13;
            _detailNameStyle.fontStyle = FontStyle.Bold;
            _detailNameStyle.alignment = TextAnchor.MiddleLeft;
            _detailNameStyle.clipping = TextClipping.Clip;
            _detailNameStyle.normal.textColor = Color.white;

            _detailDescriptionStyle = new GUIStyle(GUI.skin.label);
            _detailDescriptionStyle.fontSize = 12;
            _detailDescriptionStyle.alignment = TextAnchor.UpperLeft;
            _detailDescriptionStyle.wordWrap = true;
            _detailDescriptionStyle.normal.textColor = ColorMutedText;
        }
    }
}
