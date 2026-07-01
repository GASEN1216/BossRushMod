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
            // 预拼接的展示文本，避免在 OnGUI（每帧多次）里重复分配字符串
            public string CornerText;
            public string DetailText;
        }

        // ═══════════════════════════════════════════
        // 角落图标状态
        // ═══════════════════════════════════════════

        private static bool _cornerVisible;

        // 样式缓存
        private static GUIStyle _cornerBoxStyle;
        private static GUIStyle _cornerTextStyle;
        private static GUIStyle _detailBoxStyle;
        private static GUIStyle _detailTitleStyle;
        private static GUIStyle _detailEntryStyle;
        private static bool _stylesInitialized;

        // 分类颜色
        private static readonly Color ColorEnemyBuff = new Color(0.9f, 0.3f, 0.3f, 1f);       // 红色
        private static readonly Color ColorPlayerBoon = new Color(0.3f, 0.85f, 0.4f, 1f);      // 绿色
        private static readonly Color ColorEnvironmentRule = new Color(0.7f, 0.4f, 0.9f, 1f);  // 紫色

        // ═══════════════════════════════════════════
        // 公共方法
        // ═══════════════════════════════════════════

        /// <summary>
        /// 准备本局词条 UI（历史方法名保留给现有调用点）
        /// </summary>
        public static void ShowBanner()
        {
            _cachedInfos.Clear();

            var mutators = MutatorManager.GetActiveMutators();
            if (mutators == null || mutators.Count == 0) return;

            for (int i = 0; i < mutators.Count; i++)
            {
                string displayName = mutators[i].GetDisplayName();
                string description = mutators[i].GetDescription();
                MutatorCategory category = mutators[i].Category;
                string prefix = GetCategoryPrefix(category);

                _cachedInfos.Add(new CachedMutatorInfo
                {
                    Category = category,
                    CornerText = prefix + " " + displayName,
                    DetailText = prefix + " " + displayName + " — " + description
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
                Rect cornerRect = DrawCornerIcons();
                if (IsMouseOver(cornerRect))
                {
                    DrawHoverDetails(cornerRect);
                }
            }
        }

        // ═══════════════════════════════════════════
        // 绘制方法
        // ═══════════════════════════════════════════

        private static Rect DrawCornerIcons()
        {
            // 角落图标位于屏幕左上角（避开血条区域）
            float startX = 10f;
            float startY = Screen.height * 0.25f;
            float iconSize = 24f;
            float spacing = 4f;

            Color prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);

            // 背景
            float bgWidth = 180f;
            float bgHeight = (_cachedInfos.Count * (iconSize + spacing)) + spacing * 2;
            Rect bgRect = new Rect(startX - 4f, startY - 4f, bgWidth, bgHeight);
            GUI.Box(bgRect, GUIContent.none, _cornerBoxStyle);

            Color prevContentColor = GUI.contentColor;
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                var info = _cachedInfos[i];
                float y = startY + i * (iconSize + spacing);

                Color catColor = GetCategoryColor(info.Category);
                GUI.contentColor = catColor;

                string text = info.CornerText;

                Rect textRect = new Rect(startX, y, bgWidth - 8f, iconSize);
                GUI.Label(textRect, text, _cornerTextStyle);
            }

            GUI.contentColor = prevContentColor;
            GUI.color = prevColor;
            return bgRect;
        }

        private static void DrawHoverDetails(Rect anchorRect)
        {
            float padding = 12f;
            float titleHeight = 28f;
            float spacing = 6f;
            float panelWidth = Mathf.Min(460f, Mathf.Max(280f, Screen.width - anchorRect.xMax - 18f));
            float x = anchorRect.xMax + 8f;

            if (x + panelWidth > Screen.width - 8f)
            {
                panelWidth = Mathf.Min(460f, Mathf.Max(280f, Screen.width - 16f));
                x = Mathf.Max(8f, Screen.width - panelWidth - 8f);
            }

            float contentWidth = Mathf.Max(120f, panelWidth - padding * 2f);
            float panelHeight = padding + titleHeight + spacing;
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                panelHeight += Mathf.Max(32f, _detailEntryStyle.CalcHeight(new GUIContent(_cachedInfos[i].DetailText), contentWidth)) + spacing;
            }
            panelHeight += padding - spacing;

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

            float entryY = y + padding + titleHeight + spacing;
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                var info = _cachedInfos[i];
                float entryHeight = Mathf.Max(32f, _detailEntryStyle.CalcHeight(new GUIContent(info.DetailText), contentWidth));
                Rect entryRect = new Rect(x + padding, entryY, contentWidth, entryHeight);

                GUI.contentColor = GetCategoryColor(info.Category);
                GUI.Label(entryRect, info.DetailText, _detailEntryStyle);
                entryY += entryHeight + spacing;
            }

            GUI.contentColor = prevContentColor;
        }

        private static bool IsMouseOver(Rect rect)
        {
            Event current = Event.current;
            if (current != null)
            {
                return rect.Contains(current.mousePosition);
            }

            Vector3 mousePosition = Input.mousePosition;
            return rect.Contains(new Vector2(mousePosition.x, Screen.height - mousePosition.y));
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

        private static string GetCategoryPrefix(MutatorCategory category)
        {
            switch (category)
            {
                case MutatorCategory.EnemyBuff: return "⚔";
                case MutatorCategory.PlayerBoon: return "★";
                case MutatorCategory.EnvironmentRule: return "☠";
                default: return "•";
            }
        }

        private static void EnsureStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            // 角落背景框
            _cornerBoxStyle = new GUIStyle(GUI.skin.box);
            Texture2D cornerBg = new Texture2D(1, 1);
            cornerBg.SetPixel(0, 0, new Color(0.02f, 0.02f, 0.05f, 0.6f));
            cornerBg.Apply();
            _cornerBoxStyle.normal.background = cornerBg;

            // 角落文字
            _cornerTextStyle = new GUIStyle(GUI.skin.label);
            _cornerTextStyle.fontSize = 12;
            _cornerTextStyle.alignment = TextAnchor.MiddleLeft;
            _cornerTextStyle.normal.textColor = Color.white;

            // 悬停详情背景框
            _detailBoxStyle = new GUIStyle(GUI.skin.box);
            Texture2D detailBg = new Texture2D(1, 1);
            detailBg.SetPixel(0, 0, new Color(0.03f, 0.03f, 0.07f, 0.88f));
            detailBg.Apply();
            _detailBoxStyle.normal.background = detailBg;

            // 悬停详情标题
            _detailTitleStyle = new GUIStyle(GUI.skin.label);
            _detailTitleStyle.fontSize = 14;
            _detailTitleStyle.fontStyle = FontStyle.Bold;
            _detailTitleStyle.alignment = TextAnchor.MiddleLeft;
            _detailTitleStyle.normal.textColor = Color.white;

            // 悬停详情条目
            _detailEntryStyle = new GUIStyle(GUI.skin.label);
            _detailEntryStyle.fontSize = 13;
            _detailEntryStyle.alignment = TextAnchor.MiddleLeft;
            _detailEntryStyle.wordWrap = true;
            _detailEntryStyle.normal.textColor = Color.white;
        }
    }
}
