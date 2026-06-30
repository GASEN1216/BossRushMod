// ============================================================================
// MutatorUI.cs - 每局变异词条 UI 展示
// ============================================================================
// 模块说明：
//   开局时在屏幕上方短暂显示本局词条（3秒），
//   战斗中在 UI 角落保留小图标提示。
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
        // ═══════════════════════════════════════════
        // 开局横幅状态
        // ═══════════════════════════════════════════

        private static bool _bannerVisible;
        private static float _bannerStartTime;
        private const float BannerDuration = 4f;       // 横幅显示时长（秒）
        private const float BannerFadeOutDuration = 1f; // 淡出时长（秒）

        // 缓存的词条信息（避免每帧调用 GetDisplayName）
        private static readonly List<CachedMutatorInfo> _cachedInfos = new List<CachedMutatorInfo>();

        private struct CachedMutatorInfo
        {
            public MutatorCategory Category;
            // 预拼接的展示文本，避免在 OnGUI（每帧多次）里重复分配字符串
            public string BannerText;
            public string CornerText;
        }

        // ═══════════════════════════════════════════
        // 角落图标状态
        // ═══════════════════════════════════════════

        private static bool _cornerVisible;

        // 样式缓存
        private static GUIStyle _bannerBoxStyle;
        private static GUIStyle _bannerTitleStyle;
        private static GUIStyle _bannerEntryStyle;
        private static GUIStyle _cornerBoxStyle;
        private static GUIStyle _cornerTextStyle;
        private static bool _stylesInitialized;

        // 分类颜色
        private static readonly Color ColorEnemyBuff = new Color(0.9f, 0.3f, 0.3f, 1f);       // 红色
        private static readonly Color ColorLootChange = new Color(1f, 0.8f, 0.2f, 1f);        // 金色
        private static readonly Color ColorEnvironmentRule = new Color(0.7f, 0.4f, 0.9f, 1f);  // 紫色

        // ═══════════════════════════════════════════
        // 公共方法
        // ═══════════════════════════════════════════

        /// <summary>
        /// 显示开局词条横幅（在 RollAndApply 后调用）
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
                    BannerText = prefix + " " + displayName + " — " + description,
                    CornerText = prefix + " " + displayName
                });
            }

            _bannerVisible = true;
            _bannerStartTime = Time.time;
            _cornerVisible = true;
        }

        /// <summary>
        /// 隐藏所有 UI（模式结束时调用）
        /// </summary>
        public static void HideAll()
        {
            _bannerVisible = false;
            _cornerVisible = false;
            _cachedInfos.Clear();
        }

        /// <summary>
        /// 在 OnGUI 中调用：绘制词条 UI
        /// </summary>
        public static void DrawGUI()
        {
            if (!MutatorManager.IsActive && !_bannerVisible) return;
            if (_cachedInfos.Count == 0) return;

            EnsureStyles();

            // 绘制开局横幅（带淡出）
            if (_bannerVisible)
            {
                float elapsed = Time.time - _bannerStartTime;
                if (elapsed > BannerDuration + BannerFadeOutDuration)
                {
                    _bannerVisible = false;
                }
                else
                {
                    float alpha = 1f;
                    if (elapsed > BannerDuration)
                    {
                        alpha = 1f - ((elapsed - BannerDuration) / BannerFadeOutDuration);
                    }
                    DrawBanner(alpha);
                }
            }

            // 绘制角落图标（持续显示直到模式结束）
            if (_cornerVisible && MutatorManager.IsActive)
            {
                DrawCornerIcons();
            }
        }

        // ═══════════════════════════════════════════
        // 绘制方法
        // ═══════════════════════════════════════════

        private static void DrawBanner(float alpha)
        {
            // 横幅位于屏幕上方居中
            float bannerWidth = 400f;
            float entryHeight = 28f;
            float titleHeight = 32f;
            float padding = 12f;
            float bannerHeight = titleHeight + padding + (_cachedInfos.Count * entryHeight) + padding;

            float x = (Screen.width - bannerWidth) * 0.5f;
            float y = Screen.height * 0.12f;

            Color prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            // 背景框
            Rect bgRect = new Rect(x, y, bannerWidth, bannerHeight);
            GUI.Box(bgRect, GUIContent.none, _bannerBoxStyle);

            // 标题
            Rect titleRect = new Rect(x, y + padding * 0.5f, bannerWidth, titleHeight);
            GUI.Label(titleRect, L10n.T("⚡ 本局变异词条", "⚡ Mutators Active"), _bannerTitleStyle);

            // 词条列表
            float entryY = y + titleHeight + padding;
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                var info = _cachedInfos[i];
                Color catColor = GetCategoryColor(info.Category);

                // 保存并设置颜色
                Color prevContent = GUI.contentColor;
                GUI.contentColor = new Color(catColor.r, catColor.g, catColor.b, alpha);

                string text = info.BannerText;

                Rect entryRect = new Rect(x + padding, entryY, bannerWidth - padding * 2, entryHeight);
                GUI.Label(entryRect, text, _bannerEntryStyle);

                GUI.contentColor = prevContent;
                entryY += entryHeight;
            }

            GUI.color = prevColor;
        }

        private static void DrawCornerIcons()
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

            GUI.contentColor = Color.white;
            GUI.color = prevColor;
        }

        // ═══════════════════════════════════════════
        // 辅助方法
        // ═══════════════════════════════════════════

        private static Color GetCategoryColor(MutatorCategory category)
        {
            switch (category)
            {
                case MutatorCategory.EnemyBuff: return ColorEnemyBuff;
                case MutatorCategory.LootChange: return ColorLootChange;
                case MutatorCategory.EnvironmentRule: return ColorEnvironmentRule;
                default: return Color.white;
            }
        }

        private static string GetCategoryPrefix(MutatorCategory category)
        {
            switch (category)
            {
                case MutatorCategory.EnemyBuff: return "⚔";
                case MutatorCategory.LootChange: return "✦";
                case MutatorCategory.EnvironmentRule: return "☠";
                default: return "•";
            }
        }

        private static void EnsureStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            // 横幅背景框
            _bannerBoxStyle = new GUIStyle(GUI.skin.box);
            Texture2D bannerBg = new Texture2D(1, 1);
            bannerBg.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.1f, 0.85f));
            bannerBg.Apply();
            _bannerBoxStyle.normal.background = bannerBg;

            // 横幅标题
            _bannerTitleStyle = new GUIStyle(GUI.skin.label);
            _bannerTitleStyle.fontSize = 18;
            _bannerTitleStyle.fontStyle = FontStyle.Bold;
            _bannerTitleStyle.alignment = TextAnchor.MiddleCenter;
            _bannerTitleStyle.normal.textColor = Color.white;

            // 横幅词条条目
            _bannerEntryStyle = new GUIStyle(GUI.skin.label);
            _bannerEntryStyle.fontSize = 14;
            _bannerEntryStyle.alignment = TextAnchor.MiddleLeft;
            _bannerEntryStyle.normal.textColor = Color.white;

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
        }
    }
}
