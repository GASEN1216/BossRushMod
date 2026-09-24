using System;
using Duckov.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 创建 Boss 池 UI（使用官方 Prefab）
        /// </summary>
        private void CreateBossPoolUI()
        {
            try
            {
                // 创建 Canvas
                bossPoolCanvas = new GameObject("BossPoolCanvas");
                Canvas canvas = bossPoolCanvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = BossRushUILayers.Panel;
                // 此前只 AddComponent 不配置，CanvasScaler 默认 ConstantPixelSize，
                // 4K 屏上 550x650 的面板会缩成一小块。
                CanvasScaler bossPoolScaler = bossPoolCanvas.AddComponent<CanvasScaler>();
                ZombieModeUIHelper.ConfigureCanvasScaler(bossPoolScaler);
                bossPoolCanvas.AddComponent<GraphicRaycaster>();
                UnityEngine.Object.DontDestroyOnLoad(bossPoolCanvas);

                // 遮罩：共享 Backdrop token + 暗角与淡入（BossRushUI.CreateBackdrop 自带 StyleBackdrop）
                BossRushUI.CreateBackdrop(bossPoolCanvas.transform);

                // 创建主面板（加大尺寸）。底色走 Surface token：旧版 (0.15,0.15,0.15) 中性灰和全 Mod 的蓝灰不是一个色系（UB-26）。
                bossPoolPanel = new GameObject("Panel");
                bossPoolPanel.transform.SetParent(bossPoolCanvas.transform, false);
                Image panelImage = bossPoolPanel.AddComponent<Image>();
                panelImage.color = BossRushUIColors.Surface;
                BossRushUI.ApplyFramedPanelSkin(panelImage, 14, BossRushUISkinPart.Panel);
                RectTransform panelRect = bossPoolPanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(550f, 650f);  // 加大面板尺寸

                // 创建标题
                CreateTitleBar(bossPoolPanel.transform);

                // 创建工具栏（全选/全不选按钮）
                CreateToolbar(bossPoolPanel.transform);

                // 创建滚动列表
                CreateScrollView(bossPoolPanel.transform);

                // 创建统计信息
                CreateStatsBar(bossPoolPanel.transform);

                // 创建底部按钮（列表内容由 OpenBossPoolWindow 的 ShowBossPoolTab 填）
                CreateBottomButtons(bossPoolPanel.transform);

                DevLog("[BossRush] Boss 池 UI 创建完成");
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] CreateBossPoolUI 失败: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        /// <summary>
        /// 创建标题栏
        /// </summary>
        private void CreateTitleBar(Transform parent)
        {
            GameObject titleBar = new GameObject("TitleBar");
            titleBar.transform.SetParent(parent, false);
            RectTransform titleRect = titleBar.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, 0f);
            titleRect.sizeDelta = new Vector2(0f, 50f);

            // 标题文本
            GameObject titleTextObj = new GameObject("TitleText");
            titleTextObj.transform.SetParent(titleBar.transform, false);
            TextMeshProUGUI titleText = titleTextObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(titleText);
            titleText.text = L10n.T("Boss池设置", "Boss Pool Settings");
            titleText.fontSize = 28;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = BossRushUIColors.TextPrimary;
            RectTransform titleTextRect = titleTextObj.GetComponent<RectTransform>();
            titleTextRect.anchorMin = Vector2.zero;
            titleTextRect.anchorMax = Vector2.one;
            titleTextRect.offsetMin = new Vector2(10f, 0f);
            titleTextRect.offsetMax = new Vector2(-40f, 0f);

            // 关闭按钮（使用官方 Button prefab）
            Button buttonPrefab = GameplayDataSettings.UIPrefabs.Button;
            if (buttonPrefab != null)
            {
                Button closeBtn = UnityEngine.Object.Instantiate(buttonPrefab, titleBar.transform);
                RectTransform closeBtnRect = closeBtn.GetComponent<RectTransform>();
                closeBtnRect.anchorMin = new Vector2(1f, 0.5f);
                closeBtnRect.anchorMax = new Vector2(1f, 0.5f);
                closeBtnRect.pivot = new Vector2(1f, 0.5f);
                closeBtnRect.anchoredPosition = new Vector2(-10f, 0f);
                closeBtnRect.sizeDelta = new Vector2(35f, 35f);

                // 设置按钮文本
                TextMeshProUGUI btnText = closeBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = "×";   // 乘号（GBK 有字形），不再拿字母 X 当关闭图标（UB-26）
                    btnText.fontSize = 22;
                }

                closeBtn.onClick.AddListener(() => SaveAndCloseBossPoolWindow());
            }
        }

        /// <summary>
        /// 工具栏（A-28）：左边两个页签「出场 Boss / 无间炼狱因子」（分段按钮，选中态看得见），右边是当前页的列表头操作——
        /// 开关页「全选 / 全不选」，因子页「全部恢复默认」（危险次级，点了先确认）。旧版一颗「无间炼狱因子」点了变「返回」、
        /// 原「全选」位变「重置」，一点就复位全部因子、没有确认。按钮一律走共享按钮（质感层、音效、三态）。
        /// </summary>
        private void CreateToolbar(Transform parent)
        {
            GameObject toolbar = new GameObject("Toolbar");
            toolbar.transform.SetParent(parent, false);
            HorizontalLayoutGroup hlg = toolbar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(10, 10, 5, 5);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            RectTransform toolbarRect = toolbar.GetComponent<RectTransform>();
            toolbarRect.anchorMin = new Vector2(0f, 1f);
            toolbarRect.anchorMax = new Vector2(1f, 1f);
            toolbarRect.pivot = new Vector2(0.5f, 1f);
            toolbarRect.anchoredPosition = new Vector2(0f, -50f);
            toolbarRect.sizeDelta = new Vector2(0f, 45f);

            bossTabButton = CreateBossPoolToolbarButton(toolbar.transform, L10n.T("出场 Boss", "Boss Pool"), 120f, () => ShowBossPoolTab(false));
            infiniteHellFactorButton = CreateBossPoolToolbarButton(toolbar.transform, L10n.T("无间炼狱因子", "Hell Factors"), 150f, () => ShowBossPoolTab(true));

            GameObject spacer = new GameObject("Spacer");
            spacer.transform.SetParent(toolbar.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            selectAllButton = CreateBossPoolToolbarButton(toolbar.transform, L10n.T("全选", "Select All"), 90f, EnableAllBosses);
            deselectAllButton = CreateBossPoolToolbarButton(toolbar.transform, L10n.T("全不选", "Deselect All"), 90f, DisableAllBosses);
            resetFactorsButton = CreateBossPoolToolbarButton(toolbar.transform, L10n.T("全部恢复默认", "Reset All"), 130f, RequestResetAllBossFactors);
            IntegrationUIFeedback.StyleDangerSecondary(resetFactorsButton);
        }

        /// <summary>工具栏上的一颗次级按钮（共享按钮 + LayoutElement 定宽）。</summary>
        private static Button CreateBossPoolToolbarButton(Transform parent, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            Button button = ZombieModeUIHelper.CreateButton("ToolbarButton", parent, label, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(width, 35f), BossRushUIColors.SurfaceRaised, 16f, new Vector2(width - 8f, 31f), onClick, true);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 35f;
            BossRushUIKit.StyleSecondaryButton(button);
            return button;
        }

        /// <summary>列表行底：Card 档圆角 + SurfaceRaised。开关行与因子行共用（UB-26）。</summary>
        private static Image AddBossPoolRowBackground(GameObject row)
        {
            Image background = row.AddComponent<Image>();
            background.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(background, 8, BossRushUISkinPart.Card);
            return background;
        }

        /// <summary>
        /// 开关行的三态：与共享按钮同一套绝对色（Graphic 置白、ColorBlock 承担底色），悬停提亮、按下压暗，
        /// 并挂共享的按钮手感（官方悬停 / 点击音效）。Toggle 不是 Button，走不了 ApplyButtonColors，这里照它的口径写一份。
        /// </summary>
        private static void ApplyBossPoolRowColors(Toggle toggle, Image background)
        {
            Color normal = BossRushUIColors.SurfaceRaised;
            background.color = Color.white;
            toggle.targetGraphic = background;
            ColorBlock colors = toggle.colors;
            colors.normalColor = normal;
            colors.highlightedColor = BossRushUI.GetHoverColor(normal);
            colors.pressedColor = BossRushUI.GetPressedColor(normal);
            colors.selectedColor = normal;
            colors.disabledColor = BossRushUI.GetDisabledColor(normal);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            toggle.colors = colors;
            toggle.transition = Selectable.Transition.ColorTint;
            background.CrossFadeColor(normal, 0f, true, true);
            BossRushButtonFeel.Attach(toggle);
        }

        /// <summary>
        /// 清空列表行：先摘下再销毁。Destroy 要到帧末才生效，旧行留在布局里会让内容高度翻倍一帧、滚动位置跳（UB-27）。
        /// </summary>
        private void ClearBossPoolContent()
        {
            for (int i = bossPoolContent.childCount - 1; i >= 0; i--)
            {
                Transform child = bossPoolContent.GetChild(i);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        /// <summary>列表重建后立即排版并写回滚动位置：开关列表与因子列表行数、行高相同，切换前后停在同一处。</summary>
        private void RestoreBossPoolScroll(float normalizedPosition)
        {
            if (bossPoolScrollRect == null || bossPoolContent == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(bossPoolContent);
            bossPoolScrollRect.verticalNormalizedPosition = Mathf.Clamp01(normalizedPosition);
        }

        /// <summary>丢掉对 Boss 池界面的全部引用（关闭淡出与卸载共用）。不销毁物体。模态租约与取消键在这里一起还（A-43）。</summary>
        private void ReleaseBossPoolUIReferences()
        {
            PetNestCancelKey cancelKey = bossPoolCanvas != null ? bossPoolCanvas.GetComponent<PetNestCancelKey>() : null;
            if (cancelKey != null) cancelKey.Detach();
            if (bossPoolModalLease != null) bossPoolModalLease.Release();
            bossPoolModalLease = null;
            bossPoolCanvas = null;
            bossPoolPanel = null;
            bossPoolContent = null;
            bossPoolScrollRect = null;
            bossToggles.Clear();
            bossFactorSelectors.Clear();
            statsText = null;
            selectAllButton = null;
            deselectAllButton = null;
            infiniteHellFactorButton = null;
            bossTabButton = null;
            resetFactorsButton = null;
            isInfiniteHellFactorMode = false;
        }
    }
}
