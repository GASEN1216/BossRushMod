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
                canvas.sortingOrder = 10;  // 降低层级，避免遮挡鼠标光标
                bossPoolCanvas.AddComponent<CanvasScaler>();
                bossPoolCanvas.AddComponent<GraphicRaycaster>();
                UnityEngine.Object.DontDestroyOnLoad(bossPoolCanvas);

                // 创建半透明背景
                GameObject bgObj = new GameObject("Background");
                bgObj.transform.SetParent(bossPoolCanvas.transform, false);
                Image bgImage = bgObj.AddComponent<Image>();
                bgImage.color = new Color(0f, 0f, 0f, 0.7f);
                RectTransform bgRect = bgObj.GetComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = Vector2.zero;
                bgRect.offsetMax = Vector2.zero;

                // 创建主面板（加大尺寸）
                bossPoolPanel = new GameObject("Panel");
                bossPoolPanel.transform.SetParent(bossPoolCanvas.transform, false);
                Image panelImage = bossPoolPanel.AddComponent<Image>();
                panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
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

                // 创建底部按钮
                CreateBottomButtons(bossPoolPanel.transform);

                // 填充 Boss 列表
                PopulateBossList();

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
            titleText.text = L10n.T("Boss池设置", "Boss Pool Settings");
            titleText.fontSize = 24;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;
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
                    btnText.text = "X";
                    btnText.fontSize = 20;
                }

                closeBtn.onClick.AddListener(() => CloseBossPoolWindow());
            }
        }

        /// <summary>
        /// 创建工具栏
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

            Button buttonPrefab = GameplayDataSettings.UIPrefabs.Button;
            if (buttonPrefab != null)
            {
                // 全选按钮
                selectAllButton = UnityEngine.Object.Instantiate(buttonPrefab, toolbar.transform);
                LayoutElement le1 = selectAllButton.gameObject.AddComponent<LayoutElement>();
                le1.preferredWidth = 100f;
                le1.preferredHeight = 35f;
                selectAllButtonText = selectAllButton.GetComponentInChildren<TextMeshProUGUI>();
                if (selectAllButtonText != null) selectAllButtonText.text = L10n.T("全选", "Select All");
                selectAllButton.onClick.AddListener(() => OnSelectAllButtonClicked());

                // 全不选按钮
                deselectAllButton = UnityEngine.Object.Instantiate(buttonPrefab, toolbar.transform);
                LayoutElement le2 = deselectAllButton.gameObject.AddComponent<LayoutElement>();
                le2.preferredWidth = 100f;
                le2.preferredHeight = 35f;
                TextMeshProUGUI txt2 = deselectAllButton.GetComponentInChildren<TextMeshProUGUI>();
                if (txt2 != null) txt2.text = L10n.T("全不选", "Deselect All");
                deselectAllButton.onClick.AddListener(() => DisableAllBosses());

                // 分隔空间
                GameObject spacer = new GameObject("Spacer");
                spacer.transform.SetParent(toolbar.transform, false);
                LayoutElement spacerLE = spacer.AddComponent<LayoutElement>();
                spacerLE.flexibleWidth = 1f;

                // 无间炼狱因子按钮（最右边）
                infiniteHellFactorButton = UnityEngine.Object.Instantiate(buttonPrefab, toolbar.transform);
                LayoutElement le3 = infiniteHellFactorButton.gameObject.AddComponent<LayoutElement>();
                le3.preferredWidth = 160f;
                le3.preferredHeight = 35f;
                infiniteHellFactorButtonText = infiniteHellFactorButton.GetComponentInChildren<TextMeshProUGUI>();
                if (infiniteHellFactorButtonText != null) infiniteHellFactorButtonText.text = L10n.T("无间炼狱因子", "Infinite Hell Factor");
                infiniteHellFactorButton.onClick.AddListener(() => OnInfiniteHellFactorButtonClicked());
            }
        }
    }
}
