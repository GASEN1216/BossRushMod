"""Guard Mode E lottery pool reuse, bounded hot path, transaction, and UI lifecycle."""

from pathlib import Path
import sys


FEATURE = Path("ModeE/ModeELotteryAndHiring.cs")
SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print("ModeELotteryGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for idx in range(brace, len(text)):
        if text[idx] == "{":
            depth += 1
        elif text[idx] == "}":
            depth -= 1
            if depth == 0:
                return text[start : idx + 1]
    return ""


def main() -> int:
    feature = FEATURE.read_text(encoding="utf-8")
    support = SUPPORT.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")

    if "ModeE\\ModeELotteryAndHiring.cs" not in compile_text:
        return fail("new Mode E feature source is missing from compile_official.bat")

    build = extract_method(feature, "private void BuildModeELotteryPoolState(")
    for token in [
        "shop.entries",
        "ItemAssetsCollection.GetMetaData(entry.ItemTypeID).quality",
        "SortedDictionary<int, List<int>>",
        "List<int> prices = new List<int>();",
        "prices.Add(price);",
        "prices.Sort();",
        "int medianPrice = prices[prices.Count / 2];",
        "Price = medianPrice",
        "pair.Value.ToArray()",
    ]:
        if token not in build:
            return fail("lottery must cache existing shop entries by quality -> " + token)
    if "(totalPrice + pricedItemCount - 1L) / pricedItemCount" in build or "averagePrice" in build:
        return fail("lottery price must use the median, not the arithmetic mean")
    price_check = build.find("TryGetModeEShellPrice(shop, entry.ItemTypeID, out price)")
    candidate_add = build.find("bucket.Add(entry.ItemTypeID);")
    if price_check < 0 or candidate_add < 0 or price_check >= candidate_add:
        return fail("lottery candidates must have a valid shell price before entering the pool")
    if "new StockShop" in feature or "AddComponent<StockShop>" in feature:
        return fail("lottery must not register a hidden or additional StockShop")

    weights = [
        [4, 9, 18, 27, 23, 12, 5, 2],
        [3, 7, 15, 24, 24, 15, 8, 4],
        [2, 5, 11, 20, 25, 18, 12, 7],
        [1, 3, 8, 15, 23, 20, 18, 12],
    ]
    if any(sum(anchor) != 100 for anchor in weights):
        return fail("quality anchors must each total 100")
    if not all(weights[idx + 1][7] > weights[idx][7] for idx in range(3)):
        return fail("Q8 probability must increase at every time anchor")
    if not all(weights[idx + 1][0] < weights[idx][0] for idx in range(3)):
        return fail("time progression must move probability away from Q1")
    for token in [
        "ModeELotteryQualityWeightAnchors",
        "elapsedMinutes",
        "Mathf.Lerp(",
        "state.TypeIDsByQuality.TryGetValue",
        "UnityEngine.Random.Range(0, bucket.Length)",
    ]:
        if token not in feature:
            return fail("two-stage time-weighted roll missing -> " + token)

    buy = extract_method(feature, "internal async UniTask<bool> BuyModeELotteryAsync(")
    for token in [
        "TryAcquireModeEShellTransaction(shop, false, out owner)",
        "ItemAssetsCollection.InstantiateAsync(itemTypeID)",
        "TryDebitModeEShell(lotteryPrice, owner.TransactionID)",
        "MarkModeEShellTransactionCommitted(owner.TransactionID)",
        "HandleCommittedModeEShellDeliveryRemainder(",
        "PushModeELotteryRewardNotification(capturedDisplayName)",
        "RefundIfDebited(owner.TransactionID)",
        'ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, "Lottery finally")',
    ]:
        if token not in buy:
            return fail("lottery must reuse the shell transaction boundary -> " + token)

    for token in [
        "BuildModeELotteryPoolState(shop, merchantGeneration);",
        "CreateLotteryButton();",
        "UpdateLotteryButtonState();",
        "refreshCountDownTextField",
        '"refreshCountDown"',
        "PositionModeELotteryButtonOutsideShop(",
        "FindModeEHeaderActionRoot(shopView)",
        "return shopView != null",
        "? shopView.transform as RectTransform",
        "RectTransformUtility.CalculateRelativeRectTransformBounds(",
        "RectTransform countdownRow = refreshCountDown.parent as RectTransform;",
        "CaptureModeELotteryCountdownLayout(countdownRow)",
        "countdownRow.SetParent(root, false);",
        "const float buttonWidth = 256f;",
        "const float buttonHeight = 54f;",
        "float buttonCenterX = countdownBounds.center.x;",
        "float buttonCenterY = countdownBounds.center.y;",
        "float countdownCenterY = buttonCenterY + buttonHeight * 0.5f +",
        "countdownButtonGap + countdownHeight * 0.5f",
        "lotteryCountDownOriginalActiveSelf = countdownRow.gameObject.activeSelf;",
        "lotteryCountDownRow.gameObject.SetActive(lotteryCountDownOriginalActiveSelf);",
        "LayoutRebuilder.MarkLayoutForRebuild(originalParentRect);",
        "RestoreModeELotteryCountdownLayout();",
        "CreateModeELotteryButtonObject(root)",
        'buttonObject.name = "ModeELotteryButton";',
        "UnityEngine.Object.Instantiate(",
        "sellAllButtonObject,",
        "targetRect.anchorMin = new Vector2(0.5f, 1f);",
        "targetRect.anchorMax = new Vector2(0.5f, 1f);",
        "targetRect.sizeDelta = new Vector2(buttonWidth, buttonHeight);",
        'L10n.T("抽奖 ", "Lottery ")',
        "lotteryButtonObject.SetActive(false);",
        "Resources.FindObjectsOfTypeAll<ContextualMoneyAndCash>()",
        'typeof(ContextualMoneyAndCash).GetField(',
        '"cashDisplay"',
        "GetComponentInChildren<CashDisplay>(true)",
        "TryApplyModeEShellIcon(",
        "modeEShellOwner.CurrentModeEShellIcon",
        "currencyTemplate.transform.GetSiblingIndex() + 1",
    ]:
        if token not in support:
            return fail("lottery cache/UI lifecycle missing -> " + token)

    shell_ui = extract_method(support, "private static void CreateShellBalanceText()")
    lottery_ui = extract_method(support, "private static void CreateLotteryButton()")
    lottery_factory = extract_method(
        support, "private static GameObject CreateModeELotteryButtonObject(")
    if "TryFindModeECurrencyDisplayTemplate(" not in shell_ui:
        return fail("shell balance must reuse the original top-left currency bar")
    if "TryEnsureModeEHeaderActionRow(" in support or "ModeEHeaderActionRow" in support:
        return fail("shop header action row must not return after moving shells to the currency bar")
    for token in [
        "enableWordWrapping = false;",
        "lotteryButtonText.fontSize = 24f;",
        "lotteryButtonText.fontSizeMin = 18f;",
        "lotteryButtonText.fontSizeMax = 24f;",
    ]:
        if token not in lottery_ui:
            return fail("lottery command text must use the enlarged single-line style -> " + token)
    if "sellAllButtonObject" not in lottery_factory or "new GameObject" in lottery_factory:
        return fail("lottery button must preserve the visible sell-all button hierarchy")
    if "CalculateModeERefreshHeaderVisualBounds(" in support:
        return fail("lottery placement must not merge unrelated top-level TMP text")
    if "ForceMeshUpdate(" in support or "GetComponentsInChildren<TextMeshProUGUI>" in support:
        return fail("lottery placement must not scan or rebuild the full shop text tree")
    root_method = extract_method(
        support, "private static RectTransform FindModeEHeaderActionRoot(")
    if "refreshCountDown.parent" in root_method or "current.parent" in root_method:
        return fail("lottery button must use the unclipped StockShopView overlay root")

    print("ModeELotteryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
