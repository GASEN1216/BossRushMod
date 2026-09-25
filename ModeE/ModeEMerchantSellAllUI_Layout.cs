// Mode E merchant runtime: ModeEMerchantSellAllUI_Layout.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using TMPro;
using HarmonyLib;
using SodaCraft.StringUtilities;

namespace BossRush
{
    internal static partial class ModeEMerchantSellAllUI
    {
        private static void CreateSellAllButton()
        {
            StockShopView shopView = StockShopView.Instance;
            if (shopView == null || shopView.Target != currentShop)
            {
                return;
            }

            try
            {
                Duckov.UI.InventoryDisplay playerInventoryDisplay = null;
                if (playerInventoryDisplayField != null)
                {
                    playerInventoryDisplay = playerInventoryDisplayField.GetValue(shopView) as Duckov.UI.InventoryDisplay;
                }

                if (playerInventoryDisplay == null && characterInventoryDisplayField != null)
                {
                    playerInventoryDisplay = characterInventoryDisplayField.GetValue(shopView) as Duckov.UI.InventoryDisplay;
                }

                if (playerInventoryDisplay == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 无法获取玩家背包 InventoryDisplay，跳过创建一键卖出按钮");
                    return;
                }

                Button sortButton = null;
                if (sortButtonField != null)
                {
                    sortButton = sortButtonField.GetValue(playerInventoryDisplay) as Button;
                }

                if (sortButton == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 无法获取整理按钮，跳过创建一键卖出按钮");
                    return;
                }

                if (sellAllButtonObject == null)
                {
                    sellAllButtonObject = UnityEngine.Object.Instantiate(
                        sortButton.gameObject,
                        sortButton.transform.parent);
                    sellAllButtonObject.name = "ModeEMerchantSellAllButton";
                }
                else if (sellAllButtonObject.transform.parent != sortButton.transform.parent)
                {
                    sellAllButtonObject.transform.SetParent(sortButton.transform.parent, false);
                }
                sellAllButtonObject.SetActive(true);

                RectTransform rt = sellAllButtonObject.GetComponent<RectTransform>();
                RectTransform sortRt = sortButton.GetComponent<RectTransform>();
                if (rt != null && sortRt != null)
                {
                    rt.anchorMin = sortRt.anchorMin;
                    rt.anchorMax = sortRt.anchorMax;
                    rt.pivot = sortRt.pivot;
                    rt.anchoredPosition = sortRt.anchoredPosition + new Vector2(0f, sortRt.sizeDelta.y + 8f);
                    rt.sizeDelta = new Vector2(sortRt.sizeDelta.x + 80f, sortRt.sizeDelta.y);
                }

                LayoutElement layoutElement = sellAllButtonObject.GetComponent<LayoutElement>();
                LayoutElement sourceLayoutElement = sortButton.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    if (sourceLayoutElement != null && sourceLayoutElement.preferredWidth > 0f)
                    {
                        layoutElement.preferredWidth = sourceLayoutElement.preferredWidth + 80f;
                    }

                    if (sourceLayoutElement != null && sourceLayoutElement.minWidth > 0f)
                    {
                        layoutElement.minWidth = sourceLayoutElement.minWidth + 80f;
                    }
                }

                ContentSizeFitter contentSizeFitter = sellAllButtonObject.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }

                sellAllButton = sellAllButtonObject.GetComponent<Button>();
                if (sellAllButton != null)
                {
                    sellAllButton.onClick.RemoveAllListeners();
                    sellAllButton.onClick.AddListener(OnSellAllButtonClicked);
                }

                sellAllButtonText = sellAllButtonObject.GetComponentInChildren<TextMeshProUGUI>();
                UpdateButtonState();

                ModBehaviour.DevLog("[ModeE] 一键卖出按钮创建成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] 创建一键卖出按钮失败: " + e.Message);
            }
        }

        private static void CreateShellBalanceText()
        {
            if (modeEShellOwner == null) return;
            StockShopView shopView = StockShopView.Instance;
            if (shopView == null || shopView.Target != currentShop) return;

            try
            {
                GameObject currencyTemplate;
                if (!TryFindModeECurrencyDisplayTemplate(shopView, out currencyTemplate))
                {
                    ModBehaviour.DevLog("[ModeE/Shell] 未找到原版现金显示容器，跳过左上角贝壳栏");
                    return;
                }
                Transform currencyParent = currencyTemplate.transform.parent;
                if (currencyParent == null) return;

                if (shellBalanceTextObject == null)
                {
                    shellBalanceTextObject = UnityEngine.Object.Instantiate(
                        currencyTemplate,
                        currencyParent);
                    shellBalanceTextObject.name = "ModeEShellBalanceDisplay";

                    CashDisplay clonedCashDisplay =
                        shellBalanceTextObject.GetComponentInChildren<CashDisplay>(true);
                    shellBalanceText = clonedCashDisplay != null && cashDisplayTextField != null
                        ? cashDisplayTextField.GetValue(clonedCashDisplay) as TextMeshProUGUI
                        : null;
                    DisableClonedModeECurrencyUpdaters(shellBalanceTextObject);
                }
                else if (shellBalanceTextObject.transform.parent != currencyParent)
                {
                    shellBalanceTextObject.transform.SetParent(currencyParent, false);
                }

                shellBalanceTextObject.name = "ModeEShellBalanceDisplay";
                shellBalanceTextObject.transform.SetSiblingIndex(
                    Mathf.Min(
                        currencyParent.childCount - 1,
                        currencyTemplate.transform.GetSiblingIndex() + 1));

                if (currencyParent.GetComponent<HorizontalOrVerticalLayoutGroup>() == null)
                {
                    RectTransform sourceRect = currencyTemplate.transform as RectTransform;
                    RectTransform targetRect = shellBalanceTextObject.transform as RectTransform;
                    if (sourceRect != null && targetRect != null)
                    {
                        targetRect.anchorMin = sourceRect.anchorMin;
                        targetRect.anchorMax = sourceRect.anchorMax;
                        targetRect.pivot = sourceRect.pivot;
                        targetRect.sizeDelta = sourceRect.sizeDelta;
                        targetRect.anchoredPosition = sourceRect.anchoredPosition +
                            new Vector2(Mathf.Abs(sourceRect.rect.width) + 8f, 0f);
                    }
                }

                if (shellBalanceText == null)
                {
                    shellBalanceText =
                        shellBalanceTextObject.GetComponentInChildren<TextMeshProUGUI>(true);
                }
                if (shellBalanceText == null)
                {
                    UnityEngine.Object.Destroy(shellBalanceTextObject);
                    shellBalanceTextObject = null;
                    return;
                }

                shellBalanceText.richText = false;
                shellBalanceText.enableAutoSizing = true;
                shellBalanceText.fontSizeMin = 12f;
                shellBalanceText.enableWordWrapping = false;
                shellBalanceText.overflowMode = TextOverflowModes.Ellipsis;

                shellBalanceHasShellIcon = TryApplyModeEShellIcon(
                    shellBalanceTextObject,
                    modeEShellOwner.CurrentModeEShellIcon);
                shellBalanceTextObject.SetActive(true);
                RectTransform currencyParentRect = currencyParent as RectTransform;
                if (currencyParentRect != null)
                {
                    LayoutRebuilder.MarkLayoutForRebuild(currencyParentRect);
                }
                UpdateShellBalanceText();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] 创建余额文本失败: " + e.Message);
            }
        }

        private static bool TryFindModeECurrencyDisplayTemplate(
            StockShopView shopView,
            out GameObject currencyTemplate)
        {
            currencyTemplate = null;
            if (shopView == null || contextualCashDisplayField == null ||
                contextualCashDisplayField.FieldType != typeof(GameObject))
            {
                return false;
            }

            ContextualMoneyAndCash[] currencyBars =
                Resources.FindObjectsOfTypeAll<ContextualMoneyAndCash>();
            for (int i = 0; i < currencyBars.Length; i++)
            {
                ContextualMoneyAndCash currencyBar = currencyBars[i];
                if (currencyBar == null || !currencyBar.gameObject.activeInHierarchy)
                {
                    continue;
                }

                GameObject cashDisplay =
                    contextualCashDisplayField.GetValue(currencyBar) as GameObject;
                if (cashDisplay == null || !cashDisplay.activeInHierarchy)
                {
                    continue;
                }

                currencyTemplate = cashDisplay;
                return true;
            }

            return false;
        }

        private static void DisableClonedModeECurrencyUpdaters(GameObject displayObject)
        {
            if (displayObject == null) return;

            CashDisplay[] cashDisplays = displayObject.GetComponentsInChildren<CashDisplay>(true);
            for (int i = 0; i < cashDisplays.Length; i++)
            {
                CashDisplay cashDisplay = cashDisplays[i];
                if (cashDisplay == null) continue;
                cashDisplay.enabled = false;
                UnityEngine.Object.Destroy(cashDisplay);
            }

            MoneyDisplay[] moneyDisplays = displayObject.GetComponentsInChildren<MoneyDisplay>(true);
            for (int i = 0; i < moneyDisplays.Length; i++)
            {
                MoneyDisplay moneyDisplay = moneyDisplays[i];
                if (moneyDisplay == null) continue;
                moneyDisplay.enabled = false;
                UnityEngine.Object.Destroy(moneyDisplay);
            }
        }

        private static bool TryApplyModeEShellIcon(GameObject displayObject, Sprite shellIcon)
        {
            if (displayObject == null || shellIcon == null) return false;

            Image[] images = displayObject.GetComponentsInChildren<Image>(true);
            Image iconImage = null;
            float smallestArea = float.MaxValue;
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image.sprite == null) continue;
                Rect rect = image.rectTransform.rect;
                float width = Mathf.Abs(rect.width);
                float height = Mathf.Abs(rect.height);
                if (width < 8f || height < 8f || width > 64f || height > 64f)
                {
                    continue;
                }

                float area = width * height;
                if (area < smallestArea)
                {
                    smallestArea = area;
                    iconImage = image;
                }
            }

            if (iconImage == null) return false;
            iconImage.sprite = shellIcon;
            iconImage.preserveAspect = true;
            iconImage.color = Color.white;
            return true;
        }

        private static void CreateLotteryButton()
        {
            if (modeEShellOwner == null || sellAllButtonObject == null) return;
            StockShopView shopView = StockShopView.Instance;
            if (shopView == null || shopView.Target != currentShop ||
                refreshCountDownTextField == null)
            {
                return;
            }

            try
            {
                TextMeshProUGUI headerAnchor = refreshCountDownTextField != null
                    ? refreshCountDownTextField.GetValue(shopView) as TextMeshProUGUI
                    : null;
                if (headerAnchor == null) return;
                RectTransform root = FindModeEHeaderActionRoot(shopView);
                if (root == null) return;

                if (lotteryButtonObject == null)
                {
                    lotteryButtonObject = CreateModeELotteryButtonObject(root);
                    if (lotteryButtonObject == null) return;
                }
                else if (lotteryButtonObject.transform.parent != root)
                {
                    lotteryButtonObject.transform.SetParent(root, false);
                }

                lotteryButtonObject.SetActive(true);
                lotteryButton = lotteryButtonObject.GetComponent<Button>();
                lotteryButtonText = lotteryButtonObject.GetComponentInChildren<TextMeshProUGUI>();
                if (lotteryButton == null || lotteryButtonText == null)
                {
                    lotteryButtonObject.SetActive(false);
                    return;
                }

                lotteryButton.onClick.RemoveAllListeners();
                lotteryButton.onClick.AddListener(OnLotteryButtonClicked);
                lotteryButtonText.alignment = TextAlignmentOptions.Center;
                lotteryButtonText.enableAutoSizing = true;
                lotteryButtonText.fontSize = 24f;
                lotteryButtonText.fontSizeMin = 18f;
                lotteryButtonText.fontSizeMax = 24f;
                lotteryButtonText.enableWordWrapping = false;
                lotteryButtonText.overflowMode = TextOverflowModes.Ellipsis;

                RectTransform targetRect = lotteryButtonObject.GetComponent<RectTransform>();
                if (targetRect != null)
                {
                    PositionModeELotteryButtonOutsideShop(
                        root,
                        headerAnchor.rectTransform,
                        targetRect);
                }

                UpdateLotteryButtonState();
            }
            catch (Exception e)
            {
                RestoreModeELotteryCountdownLayout();
                ModBehaviour.DevLog("[ModeE/Lottery] 创建抽奖按钮失败: " + e.Message);
            }
        }

        private static void PositionModeELotteryButtonOutsideShop(
            RectTransform root,
            RectTransform refreshCountDown,
            RectTransform targetRect)
        {
            if (root == null || refreshCountDown == null || targetRect == null) return;

            RectTransform countdownRow = refreshCountDown.parent as RectTransform;
            if (countdownRow == null) countdownRow = refreshCountDown;
            Bounds countdownBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                root,
                countdownRow);
            if (!CaptureModeELotteryCountdownLayout(countdownRow)) return;

            const float buttonWidth = 256f;
            const float buttonHeight = 54f;
            const float countdownButtonGap = 8f;
            float buttonCenterX = countdownBounds.center.x;
            float buttonCenterY = countdownBounds.center.y;
            float countdownWidth = Mathf.Max(countdownBounds.size.x, buttonWidth);
            float countdownHeight = Mathf.Max(countdownBounds.size.y, 1f);
            float countdownCenterY = buttonCenterY + buttonHeight * 0.5f +
                countdownButtonGap + countdownHeight * 0.5f;

            countdownRow.SetParent(root, false);
            countdownRow.anchorMin = new Vector2(0.5f, 1f);
            countdownRow.anchorMax = new Vector2(0.5f, 1f);
            countdownRow.pivot = new Vector2(0.5f, 0.5f);
            countdownRow.sizeDelta = new Vector2(countdownWidth, countdownHeight);
            countdownRow.anchoredPosition = new Vector2(
                buttonCenterX - root.rect.center.x,
                countdownCenterY - root.rect.yMax);
            countdownRow.localRotation = Quaternion.identity;
            countdownRow.localScale = Vector3.one;
            countdownRow.SetAsLastSibling();

            targetRect.anchorMin = new Vector2(0.5f, 1f);
            targetRect.anchorMax = new Vector2(0.5f, 1f);
            targetRect.pivot = new Vector2(0.5f, 0.5f);
            targetRect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            targetRect.anchoredPosition = new Vector2(
                buttonCenterX - root.rect.center.x,
                buttonCenterY - root.rect.yMax);
            targetRect.SetAsLastSibling();
        }

        private static bool CaptureModeELotteryCountdownLayout(RectTransform countdownRow)
        {
            if (countdownRow == null) return false;
            if (lotteryCountDownLayoutCaptured)
            {
                return lotteryCountDownRow == countdownRow;
            }

            lotteryCountDownRow = countdownRow;
            lotteryCountDownOriginalParent = countdownRow.parent;
            if (lotteryCountDownOriginalParent == null)
            {
                lotteryCountDownRow = null;
                return false;
            }

            lotteryCountDownOriginalSiblingIndex = countdownRow.GetSiblingIndex();
            lotteryCountDownOriginalAnchorMin = countdownRow.anchorMin;
            lotteryCountDownOriginalAnchorMax = countdownRow.anchorMax;
            lotteryCountDownOriginalPivot = countdownRow.pivot;
            lotteryCountDownOriginalAnchoredPosition = countdownRow.anchoredPosition;
            lotteryCountDownOriginalSizeDelta = countdownRow.sizeDelta;
            lotteryCountDownOriginalLocalRotation = countdownRow.localRotation;
            lotteryCountDownOriginalLocalScale = countdownRow.localScale;
            lotteryCountDownOriginalActiveSelf = countdownRow.gameObject.activeSelf;
            lotteryCountDownLayoutCaptured = true;
            return true;
        }

        private static void RestoreModeELotteryCountdownLayout()
        {
            if (!lotteryCountDownLayoutCaptured) return;

            try
            {
                if (lotteryCountDownRow != null && lotteryCountDownOriginalParent != null)
                {
                    lotteryCountDownRow.SetParent(lotteryCountDownOriginalParent, false);
                    lotteryCountDownRow.anchorMin = lotteryCountDownOriginalAnchorMin;
                    lotteryCountDownRow.anchorMax = lotteryCountDownOriginalAnchorMax;
                    lotteryCountDownRow.pivot = lotteryCountDownOriginalPivot;
                    lotteryCountDownRow.anchoredPosition = lotteryCountDownOriginalAnchoredPosition;
                    lotteryCountDownRow.sizeDelta = lotteryCountDownOriginalSizeDelta;
                    lotteryCountDownRow.localRotation = lotteryCountDownOriginalLocalRotation;
                    lotteryCountDownRow.localScale = lotteryCountDownOriginalLocalScale;
                    lotteryCountDownRow.gameObject.SetActive(lotteryCountDownOriginalActiveSelf);
                    int siblingIndex = Mathf.Clamp(
                        lotteryCountDownOriginalSiblingIndex,
                        0,
                        Mathf.Max(0, lotteryCountDownOriginalParent.childCount - 1));
                    lotteryCountDownRow.SetSiblingIndex(siblingIndex);
                    RectTransform originalParentRect =
                        lotteryCountDownOriginalParent as RectTransform;
                    if (originalParentRect != null)
                    {
                        LayoutRebuilder.MarkLayoutForRebuild(originalParentRect);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Lottery] 恢复刷新倒计时布局失败: " + e.Message);
            }

            lotteryCountDownRow = null;
            lotteryCountDownOriginalParent = null;
            lotteryCountDownOriginalSiblingIndex = 0;
            lotteryCountDownOriginalAnchorMin = Vector2.zero;
            lotteryCountDownOriginalAnchorMax = Vector2.zero;
            lotteryCountDownOriginalPivot = Vector2.zero;
            lotteryCountDownOriginalAnchoredPosition = Vector2.zero;
            lotteryCountDownOriginalSizeDelta = Vector2.zero;
            lotteryCountDownOriginalLocalRotation = Quaternion.identity;
            lotteryCountDownOriginalLocalScale = Vector3.one;
            lotteryCountDownOriginalActiveSelf = false;
            lotteryCountDownLayoutCaptured = false;
        }

        private static RectTransform FindModeEHeaderActionRoot(StockShopView shopView)
        {
            // Header containers clip children outside their single-line bounds.
            // The view root is the nearest stable overlay that remains visible below the countdown.
            return shopView != null
                ? shopView.transform as RectTransform
                : null;
        }

        private static GameObject CreateModeELotteryButtonObject(Transform parent)
        {
            if (parent == null || sellAllButton == null || sellAllButtonText == null)
            {
                return null;
            }

            GameObject buttonObject = UnityEngine.Object.Instantiate(
                sellAllButtonObject,
                parent);
            buttonObject.name = "ModeELotteryButton";
            buttonObject.SetActive(false);

            Button targetButton = buttonObject.GetComponent<Button>();
            if (targetButton != null)
            {
                targetButton.onClick.RemoveAllListeners();
                Navigation navigation = targetButton.navigation;
                navigation.mode = Navigation.Mode.None;
                targetButton.navigation = navigation;
            }

            LayoutElement layoutElement = buttonObject.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.ignoreLayout = true;
            }
            ContentSizeFitter contentSizeFitter = buttonObject.GetComponent<ContentSizeFitter>();
            if (contentSizeFitter != null)
            {
                contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }

            return buttonObject;
        }

        private static void OnLotteryButtonClicked()
        {
            if (modeEShellOwner == null || currentShop == null ||
                modeEShellUiBindingID <= 0L)
            {
                return;
            }

            modeEShellOwner.BuyModeELotteryAsync(
                currentShop,
                modeEShellUiBindingID).Forget();
        }

        private static void UpdateLotteryButtonState()
        {
            if (lotteryButton == null || lotteryButtonText == null ||
                modeEShellOwner == null || currentShop == null)
            {
                return;
            }

            int price;
            bool ready = modeEShellOwner.TryGetModeELotteryUiState(currentShop, out price);
            bool enough = ready && modeEShellOwner.CurrentModeEShellBalance >= price;
            bool interactable = enough && !modeEShellOwner.IsModeEShellTransactionGateBusy;
            string displayText = ready
                ? L10n.T("抽奖 ", "Lottery ") + price.ToString("N0") +
                    L10n.T(" 贝壳", " Shells")
                : L10n.T("抽奖载入中", "Lottery loading");
            ApplyButtonState(
                lotteryButton,
                lotteryButtonObject,
                lotteryButtonText,
                displayText,
                interactable);
        }

        private static void UpdateShellBalanceText()
        {
            if (shellBalanceText == null || modeEShellOwner == null) return;
            string amount = modeEShellOwner.CurrentModeEShellBalance.ToString("N0");
            shellBalanceText.text = shellBalanceHasShellIcon
                ? amount
                : L10n.T("贝壳 ", "Shells ") + amount;
        }

    }
}
