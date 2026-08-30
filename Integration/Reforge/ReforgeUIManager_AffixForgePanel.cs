// ============================================================================
// ReforgeUIManager_AffixForgePanel.cs - 词缀面板的构建代码（ReforgeUIManager 的 partial 续）
// ============================================================================
// 模块职责：
//   只放"把词缀面板的 GameObject 树搭出来"这一段。刷新、状态分流、清理留在
//   ReforgeUIManager_AffixForge.cs —— 拆分纯粹是单文件 1200 行预算所迫，职责边界为
//   「构建（本文件） / 刷新与分流（同名主文件）」。
//
// 硬约束（AGENTS 4.14 UI 共享库）：
//   1. 寄生官方 ItemDecomposeView 的 Canvas，不新建 Canvas，故不涉及 sortingOrder。
//   2. 颜色一律 BossRushUIColors，底图一律 BossRushUI.ApplyPanelSkin，
//      文本一律 ZombieModeUIHelper.CreateTMPText（内部已 GetGameFont），
//      按钮一律 ZombieModeUIHelper.CreateButton + ApplyButtonColors。
//   3. 锁定按钮回调是命名方法（GetLockButtonHandler 返回三个薄包装之一），不捕获 Item。
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BossRush
{
    public static partial class ReforgeUIManager
    {
        // ============================================================================
        // 面板构建
        // ============================================================================

        /// <summary>
        /// 在官方右侧操作列里插一块词缀面板。父节点与倾向滑块同一个
        /// （moneySlider 的祖父节点），占掉被隐藏的金钱滑块腾出的位置。
        /// </summary>
        private static void BuildAffixPanel()
        {
            if (affixPanelRoot != null)
            {
                affixPanelRoot.SetActive(true);
                return;
            }

            if (moneySlider == null || moneySlider.transform.parent == null)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [WARNING] 金钱滑块引用缺失，无法定位词缀面板父节点");
                return;
            }

            Transform moneySliderParent = moneySlider.transform.parent;
            Transform panelParent = moneySliderParent.parent;
            if (panelParent == null)
            {
                return;
            }

            // 上一次会话遗留的同名节点：引用已丢，直接拆掉重建，避免行控件引用悬空
            Transform stale = panelParent.Find(AFFIX_PANEL_NAME);
            if (stale != null)
            {
                GameObject.DestroyImmediate(stale.gameObject);
            }

            GameObject panel = ZombieModeUIHelper.CreateRect(
                AFFIX_PANEL_NAME,
                panelParent,
                new Vector2(0.5f, 0.5f),
                new Vector2(AFFIX_PANEL_WIDTH, AFFIX_ROW_HEIGHT));

            Image background = panel.AddComponent<Image>();
            background.color = BossRushUIColors.Surface;
            background.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(background, AFFIX_PANEL_CORNER_RADIUS);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = AFFIX_PANEL_SPACING;
            layout.padding = new RectOffset(AFFIX_PANEL_PADDING, AFFIX_PANEL_PADDING, AFFIX_PANEL_PADDING, AFFIX_PANEL_PADDING);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement panelLayout = panel.AddComponent<LayoutElement>();
            panelLayout.minWidth = AFFIX_PANEL_WIDTH;
            panelLayout.preferredWidth = AFFIX_PANEL_WIDTH;

            panel.transform.SetSiblingIndex(moneySliderParent.GetSiblingIndex());

            affixPanelRoot = panel;
            affixRows.Clear();

            BuildStoneRow(panel.transform);

            int maxSlots = AffixDefinitions.MaxSlots;
            for (int slotIndex = 1; slotIndex <= maxSlots; slotIndex++)
            {
                affixRows.Add(BuildAffixRow(panel.transform, slotIndex));
            }

            // probabilityText 排到面板之后，保持"面板在上、费用说明在下"的阅读顺序
            if (probabilityText != null && probabilityText.transform.parent == panelParent)
            {
                probabilityText.transform.SetSiblingIndex(panel.transform.GetSiblingIndex() + 1);
            }

            ModBehaviour.DevLog("[AffixForgeUI] 词缀面板已创建");
        }

        private static void BuildStoneRow(Transform parent)
        {
            GameObject row = ZombieModeUIHelper.CreateRect(
                "AffixStoneRow",
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(AFFIX_PANEL_WIDTH - AFFIX_PANEL_PADDING * 2, AFFIX_STONE_ROW_HEIGHT));

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = AFFIX_PANEL_SPACING;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = AFFIX_STONE_ROW_HEIGHT;
            rowLayout.preferredHeight = AFFIX_STONE_ROW_HEIGHT;

            GameObject iconObj = ZombieModeUIHelper.CreateRect(
                "StoneIcon",
                row.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(AFFIX_STONE_ICON_SIZE, AFFIX_STONE_ICON_SIZE));
            AddFixedLayoutElement(iconObj, AFFIX_STONE_ICON_SIZE, AFFIX_STONE_ICON_SIZE);

            Sprite stoneSprite = TryLoadAffixForgeStoneSprite();
            if (stoneSprite != null)
            {
                Image stoneIcon = iconObj.AddComponent<Image>();
                stoneIcon.sprite = stoneSprite;
                stoneIcon.preserveAspect = true;
                stoneIcon.raycastTarget = false;
            }
            else
            {
                // 图标缺文件时用符号兜底（照冷淬液 "❄" 的先例）
                ZombieModeUIHelper.CreateTMPText(
                    iconObj,
                    "◆",
                    AFFIX_ICON_FALLBACK_FONT_SIZE,
                    TextAlignmentOptions.Center,
                    BossRushUIColors.Accent);
            }

            GameObject countObj = ZombieModeUIHelper.CreateRect(
                "StoneCount",
                row.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(AFFIX_PANEL_WIDTH - AFFIX_STONE_ICON_SIZE - AFFIX_PANEL_PADDING * 3, AFFIX_STONE_ROW_HEIGHT));
            LayoutElement countLayout = countObj.AddComponent<LayoutElement>();
            countLayout.flexibleWidth = 1f;
            countLayout.minHeight = AFFIX_STONE_ROW_HEIGHT;

            affixStoneCountText = ZombieModeUIHelper.CreateTMPText(
                countObj,
                string.Empty,
                AFFIX_STONE_FONT_SIZE,
                TextAlignmentOptions.MidlineLeft,
                BossRushUIColors.TextPrimary);

            affixStoneContainer = row;
        }

        private static AffixRowWidgets BuildAffixRow(Transform parent, int slotIndex)
        {
            AffixRowWidgets widgets = new AffixRowWidgets();
            widgets.SlotIndex = slotIndex;

            GameObject row = ZombieModeUIHelper.CreateRect(
                "AffixRow_" + slotIndex,
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(AFFIX_PANEL_WIDTH - AFFIX_PANEL_PADDING * 2, AFFIX_ROW_HEIGHT));

            Image rowBackground = row.AddComponent<Image>();
            rowBackground.color = BossRushUIColors.SurfaceRaised;
            rowBackground.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(rowBackground, AFFIX_PANEL_CORNER_RADIUS);

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = AFFIX_PANEL_SPACING;
            layout.padding = new RectOffset(4, 4, 2, 2);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = AFFIX_ROW_HEIGHT;
            rowLayout.preferredHeight = AFFIX_ROW_HEIGHT;

            // ---- 图标 ----
            GameObject iconObj = ZombieModeUIHelper.CreateRect(
                "Icon",
                row.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(AFFIX_ICON_SIZE, AFFIX_ICON_SIZE));
            AddFixedLayoutElement(iconObj, AFFIX_ICON_SIZE, AFFIX_ICON_SIZE);

            widgets.Icon = iconObj.AddComponent<Image>();
            widgets.Icon.preserveAspect = true;
            widgets.Icon.raycastTarget = false;
            widgets.Icon.enabled = false;

            GameObject iconFallbackObj = ZombieModeUIHelper.CreateRect(
                "IconFallback",
                iconObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(AFFIX_ICON_SIZE, AFFIX_ICON_SIZE));
            widgets.IconFallbackText = ZombieModeUIHelper.CreateTMPText(
                iconFallbackObj,
                "◇",
                AFFIX_ICON_FALLBACK_FONT_SIZE,
                TextAlignmentOptions.Center,
                BossRushUIColors.TextSecondary);

            // ---- 文本列 ----
            GameObject textColumn = ZombieModeUIHelper.CreateRect(
                "Texts",
                row.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(AFFIX_PANEL_WIDTH - AFFIX_ICON_SIZE - AFFIX_LOCK_BUTTON_WIDTH - AFFIX_PANEL_PADDING * 4, AFFIX_ROW_HEIGHT));

            VerticalLayoutGroup textLayout = textColumn.AddComponent<VerticalLayoutGroup>();
            textLayout.spacing = 2;
            textLayout.childAlignment = TextAnchor.MiddleLeft;
            textLayout.childControlWidth = true;
            textLayout.childControlHeight = true;
            textLayout.childForceExpandWidth = true;
            textLayout.childForceExpandHeight = false;

            LayoutElement textColumnLayout = textColumn.AddComponent<LayoutElement>();
            textColumnLayout.flexibleWidth = 1f;
            textColumnLayout.minHeight = AFFIX_ROW_HEIGHT - 4;

            GameObject nameObj = ZombieModeUIHelper.CreateRect(
                "Name",
                textColumn.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(200f, AFFIX_NAME_FONT_SIZE + 6));
            AddFixedLayoutElement(nameObj, 0, AFFIX_NAME_FONT_SIZE + 6);
            widgets.NameText = ZombieModeUIHelper.CreateTMPText(
                nameObj,
                string.Empty,
                AFFIX_NAME_FONT_SIZE,
                TextAlignmentOptions.MidlineLeft,
                BossRushUIColors.TextPrimary);

            GameObject descObj = ZombieModeUIHelper.CreateRect(
                "Desc",
                textColumn.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(200f, AFFIX_DESC_FONT_SIZE + 8));
            AddFixedLayoutElement(descObj, 0, AFFIX_DESC_FONT_SIZE + 8);
            widgets.DescText = ZombieModeUIHelper.CreateTMPText(
                descObj,
                string.Empty,
                AFFIX_DESC_FONT_SIZE,
                TextAlignmentOptions.MidlineLeft,
                BossRushUIColors.TextSecondary);

            // ---- 锁定按钮（命名方法回调，不捕获 Item）----
            widgets.LockButton = ZombieModeUIHelper.CreateButton(
                "LockButton",
                row.transform,
                string.Empty,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(AFFIX_LOCK_BUTTON_WIDTH, AFFIX_LOCK_BUTTON_HEIGHT),
                BossRushUIColors.SurfaceRaised,
                AFFIX_LOCK_FONT_SIZE,
                new Vector2(AFFIX_LOCK_BUTTON_WIDTH - 6, AFFIX_LOCK_BUTTON_HEIGHT - 6),
                GetLockButtonHandler(slotIndex),
                true);

            if (widgets.LockButton != null)
            {
                AddFixedLayoutElement(widgets.LockButton.gameObject, AFFIX_LOCK_BUTTON_WIDTH, AFFIX_LOCK_BUTTON_HEIGHT);
                ZombieModeUIHelper.ApplyButtonColors(
                    widgets.LockButton,
                    BossRushUIColors.SurfaceRaised,
                    AffixLockHoverColor,
                    BossRushUIColors.Disabled);
                widgets.LockButtonText = widgets.LockButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            widgets.Root = row;
            return widgets;
        }

        private static void AddFixedLayoutElement(GameObject target, int width, int height)
        {
            LayoutElement element = target.AddComponent<LayoutElement>();
            if (width > 0)
            {
                element.minWidth = width;
                element.preferredWidth = width;
            }

            if (height > 0)
            {
                element.minHeight = height;
                element.preferredHeight = height;
            }
        }

    }
}
