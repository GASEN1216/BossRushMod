// ============================================================================
// MutatorUI.cs - 每局变异词条 UI 展示
// ============================================================================
// 模块说明：
//   开局后在屏幕左侧常驻本局词条列表，鼠标悬停某一行时在右侧展开详细说明。
//
//   实现说明：本文件原为 IMGUI（OnGUI + GUIStyle）。IMGUI 不随 CanvasScaler
//   缩放，高分屏上字号偏小、且与 Mod 其余界面观感割裂，因此改为 uGUI。
//   Canvas 走 BossRushUI.CreateCanvasRoot（interactive=true：本界面需要接收
//   悬停），面板与行套共享圆角皮肤，字体走统一的游戏字体回退。
//
//   悬停用 EventTrigger 而不是每帧 Rect.Contains：uGUI 的射线检测已经处理了
//   遮挡与缩放，自己算坐标会在非 1080p 下错位。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 变异词条 UI 展示（uGUI 实现）
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

        // 分类颜色
        private static readonly Color ColorEnemyBuff = new Color(0.96f, 0.33f, 0.28f, 1f);
        private static readonly Color ColorPlayerBoon = new Color(0.30f, 0.84f, 0.62f, 1f);
        private static readonly Color ColorEnvironmentRule = new Color(1f, 0.72f, 0.25f, 1f);
        private static readonly Color ColorPanel = new Color(0.035f, 0.045f, 0.055f, 0.91f);
        private static readonly Color ColorRow = new Color(0.12f, 0.14f, 0.16f, 0.10f);
        private static readonly Color ColorRowHover = new Color(0.20f, 0.23f, 0.26f, 0.42f);
        private static readonly Color ColorMutedText = new Color(0.72f, 0.76f, 0.79f, 1f);

        // 布局常量（1920x1080 参考分辨率下的设计值，由 CanvasScaler 统一缩放）
        private const float PanelWidth = 224f;
        private const float PanelPadding = 8f;
        private const float HeaderHeight = 23f;
        private const float RowHeight = 29f;
        private const float RowSpacing = 3f;
        private const float PanelLeftMargin = 16f;
        private const float PanelTopOffset = -240f;
        private const float DetailWidth = 320f;

        private static bool _cornerVisible;

        private static Canvas _canvas;
        private static GameObject _panelRoot;
        private static GameObject _detailRoot;
        private static TextMeshProUGUI _detailTitleText;
        private static TextMeshProUGUI _detailBodyText;
        private static readonly List<Image> _rowBackgrounds = new List<Image>();
        private static int _hoveredIndex = -1;

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

                _cachedInfos.Add(new CachedMutatorInfo
                {
                    Category = category,
                    DisplayName = displayName,
                    Description = description,
                    CategoryLabel = GetCategoryLabel(category)
                });
            }

            _cornerVisible = true;
            DestroyCanvas();
        }

        /// <summary>
        /// 隐藏所有 UI（模式结束时调用）
        /// </summary>
        public static void HideAll()
        {
            _cornerVisible = false;
            _cachedInfos.Clear();
            _hoveredIndex = -1;
            DestroyCanvas();
        }

        /// <summary>
        /// 每帧调用：维护词条 UI 的可见性与悬停详情。
        ///
        /// 模态界面与自定义弹层会关闭 gameplay 输入；图片查看器则是把 timeScale
        /// 压到 0 但不申请输入租约，所以两条都要判。抑制期间只隐藏 Canvas 并清掉
        /// 悬停，缓存的词条列表在界面关闭后原样回来。
        /// </summary>
        public static void Tick()
        {
            if (!MutatorManager.IsActive || _cachedInfos.Count == 0 || !_cornerVisible)
            {
                SetCanvasVisible(false);
                return;
            }

            bool suppressed;
            try
            {
                suppressed = !InputManager.InputActived || Time.timeScale <= 0f;
            }
            catch
            {
                suppressed = true;
            }

            if (suppressed)
            {
                SetHoveredIndex(-1);
                SetCanvasVisible(false);
                return;
            }

            EnsureCanvas();
            SetCanvasVisible(true);
        }

        /// <summary>
        /// 释放 Canvas 与缓存。切场景 / OnDestroy 路径调用。
        /// </summary>
        public static void ResetStaticCaches()
        {
            _cornerVisible = false;
            _cachedInfos.Clear();
            _hoveredIndex = -1;
            DestroyCanvas();
        }

        // ═══════════════════════════════════════════
        // Canvas 构建
        // ═══════════════════════════════════════════

        private static void EnsureCanvas()
        {
            if (_canvas != null && _panelRoot != null)
            {
                return;
            }

            DestroyCanvas();

            try
            {
                _canvas = BossRushUI.CreateCanvasRoot("BossRush_MutatorOverlay", BossRushUILayers.HudOverlay, true);
                UnityEngine.Object.DontDestroyOnLoad(_canvas.gameObject);

                float panelHeight = PanelPadding * 2f + HeaderHeight +
                                    _cachedInfos.Count * RowHeight +
                                    Mathf.Max(0, _cachedInfos.Count - 1) * RowSpacing;

                _panelRoot = ZombieModeUIHelper.CreateRect(
                    "MutatorPanel",
                    _canvas.transform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(PanelLeftMargin, PanelTopOffset),
                    new Vector2(PanelWidth, panelHeight),
                    new Vector2(0f, 1f));
                Image panelImage = _panelRoot.AddComponent<Image>();
                panelImage.color = ColorPanel;
                BossRushUI.ApplyPanelSkin(panelImage, 10);

                BuildHeader();
                BuildRows();
                BuildDetailPanel();
                SetHoveredIndex(-1);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MutatorUI] 创建变异词条 UI 失败: " + e.Message);
                DestroyCanvas();
            }
        }

        private static void BuildHeader()
        {
            TextMeshProUGUI header = ZombieModeUIHelper.CreateText(
                "Header",
                _panelRoot.transform,
                L10n.T("本局变异", "ACTIVE MUTATORS"),
                13f,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(PanelPadding, -PanelPadding),
                new Vector2(-PanelPadding * 2f, HeaderHeight),
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            header.raycastTarget = false;

            TextMeshProUGUI count = ZombieModeUIHelper.CreateText(
                "Count",
                _panelRoot.transform,
                _cachedInfos.Count.ToString(),
                13f,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-PanelPadding, -PanelPadding),
                new Vector2(-PanelPadding * 2f, HeaderHeight),
                TextAlignmentOptions.MidlineRight,
                ColorMutedText);
            count.raycastTarget = false;
        }

        private static void BuildRows()
        {
            _rowBackgrounds.Clear();
            for (int i = 0; i < _cachedInfos.Count; i++)
            {
                CachedMutatorInfo info = _cachedInfos[i];
                float top = -(PanelPadding + HeaderHeight + i * (RowHeight + RowSpacing));

                GameObject row = ZombieModeUIHelper.CreateRect(
                    "Row_" + i,
                    _panelRoot.transform,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(PanelPadding, top),
                    new Vector2(-PanelPadding * 2f, RowHeight),
                    new Vector2(0f, 1f));
                Image rowImage = row.AddComponent<Image>();
                rowImage.color = ColorRow;
                BossRushUI.ApplyPanelSkin(rowImage, 4);
                _rowBackgrounds.Add(rowImage);

                // 分类色竖条：沿用奖励卡/模态标题的 accent rail 视觉语言。
                GameObject accent = ZombieModeUIHelper.CreateRect(
                    "Accent",
                    row.transform,
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(3f, 0f),
                    new Vector2(3f, -6f),
                    new Vector2(0f, 0.5f));
                Image accentImage = accent.AddComponent<Image>();
                accentImage.color = GetCategoryColor(info.Category);
                accentImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(accentImage, 2);

                TextMeshProUGUI nameText = ZombieModeUIHelper.CreateText(
                    "Name",
                    row.transform,
                    info.DisplayName,
                    13f,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(11f, 0f),
                    new Vector2(-58f, 0f),
                    TextAlignmentOptions.MidlineLeft,
                    Color.white);
                nameText.raycastTarget = false;
                nameText.enableWordWrapping = false;
                nameText.overflowMode = TextOverflowModes.Ellipsis;

                TextMeshProUGUI categoryText = ZombieModeUIHelper.CreateText(
                    "Category",
                    row.transform,
                    info.CategoryLabel,
                    11f,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(-6f, 0f),
                    new Vector2(-8f, 0f),
                    TextAlignmentOptions.MidlineRight,
                    GetCategoryColor(info.Category));
                categoryText.raycastTarget = false;

                AttachHoverHandler(row, i);
            }
        }

        /// <summary>
        /// 用 EventTrigger 挂悬停回调。索引按值捕获，不能直接用循环变量。
        /// </summary>
        private static void AttachHoverHandler(GameObject row, int index)
        {
            EventTrigger trigger = row.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry();
            enter.eventID = EventTriggerType.PointerEnter;
            int enterIndex = index;
            enter.callback.AddListener(delegate { SetHoveredIndex(enterIndex); });
            trigger.triggers.Add(enter);

            EventTrigger.Entry exit = new EventTrigger.Entry();
            exit.eventID = EventTriggerType.PointerExit;
            int exitIndex = index;
            exit.callback.AddListener(delegate
            {
                if (_hoveredIndex == exitIndex)
                {
                    SetHoveredIndex(-1);
                }
            });
            trigger.triggers.Add(exit);
        }

        private static void BuildDetailPanel()
        {
            _detailRoot = ZombieModeUIHelper.CreateRect(
                "Detail",
                _canvas.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(PanelLeftMargin + PanelWidth + 8f, PanelTopOffset),
                new Vector2(DetailWidth, 148f),
                new Vector2(0f, 1f));
            Image detailImage = _detailRoot.AddComponent<Image>();
            detailImage.color = BossRushUIColors.Surface;
            detailImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(detailImage, 10);

            _detailTitleText = ZombieModeUIHelper.CreateText(
                "DetailTitle",
                _detailRoot.transform,
                string.Empty,
                14f,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(10f, -8f),
                new Vector2(-20f, 24f),
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            _detailTitleText.raycastTarget = false;

            _detailBodyText = ZombieModeUIHelper.CreateText(
                "DetailBody",
                _detailRoot.transform,
                string.Empty,
                12f,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(10f, -36f),
                new Vector2(-20f, -44f),
                TextAlignmentOptions.TopLeft,
                ColorMutedText);
            _detailBodyText.raycastTarget = false;
            _detailBodyText.enableWordWrapping = true;
        }

        // ═══════════════════════════════════════════
        // 悬停状态
        // ═══════════════════════════════════════════

        private static void SetHoveredIndex(int index)
        {
            _hoveredIndex = index;

            for (int i = 0; i < _rowBackgrounds.Count; i++)
            {
                Image background = _rowBackgrounds[i];
                if (background == null)
                {
                    continue;
                }
                background.color = i == index ? ColorRowHover : ColorRow;
            }

            if (_detailRoot == null)
            {
                return;
            }

            bool show = index >= 0 && index < _cachedInfos.Count;
            if (_detailRoot.activeSelf != show)
            {
                _detailRoot.SetActive(show);
            }
            if (!show)
            {
                return;
            }

            CachedMutatorInfo info = _cachedInfos[index];
            if (_detailTitleText != null)
            {
                _detailTitleText.text = info.DisplayName;
                _detailTitleText.color = GetCategoryColor(info.Category);
            }
            if (_detailBodyText != null)
            {
                _detailBodyText.text = info.Description;
            }
        }

        private static void SetCanvasVisible(bool visible)
        {
            if (_canvas == null)
            {
                return;
            }

            if (_canvas.gameObject.activeSelf != visible)
            {
                _canvas.gameObject.SetActive(visible);
            }
        }

        private static void DestroyCanvas()
        {
            if (_canvas != null)
            {
                try { UnityEngine.Object.Destroy(_canvas.gameObject); }
                catch (Exception e) { Debug.LogWarning("[MutatorUI] 销毁变异词条 UI 失败: " + e.Message); }
            }

            _canvas = null;
            _panelRoot = null;
            _detailRoot = null;
            _detailTitleText = null;
            _detailBodyText = null;
            _rowBackgrounds.Clear();
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
    }
}
