from pathlib import Path
import sys


FETCH_PIPELINE = Path("Integration/WishFountain/WishFountainFetchPipeline.cs")
UI_SOURCE = Path("Integration/WishFountain/WishFountainUI.cs")


def fail(message: str) -> int:
    print("WishDanmakuFetchLifecycleGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    fetch_text = FETCH_PIPELINE.read_text(encoding="utf-8")
    ui_text = UI_SOURCE.read_text(encoding="utf-8")

    cancel_block = extract_block(fetch_text, "public static void CancelRecentWishesRequest(")
    if not cancel_block:
        return fail("missing CancelRecentWishesRequest helper")
    if "danmakuFetchSuccessWaiters -= onSuccess;" not in cancel_block:
        return fail("CancelRecentWishesRequest must remove success waiter")
    if "danmakuFetchFailureWaiters -= onFailure;" not in cancel_block:
        return fail("CancelRecentWishesRequest must remove failure waiter")

    cache_block = extract_block(fetch_text, "private static bool TryReturnRecentDanmakuResult(")
    if not cache_block:
        return fail("missing TryReturnRecentDanmakuResult block")
    if "if (lastDanmakuFetchSucceeded)" not in cache_block:
        return fail("success snapshot TTL path missing")
    if "lastDanmakuFetchFailureReason" in cache_block:
        return fail("failure TTL short-circuit should not remain in TryReturnRecentDanmakuResult")

    ui_cancel_block = extract_block(ui_text, "private void CancelDanmakuFetch()")
    if not ui_cancel_block:
        return fail("missing UI CancelDanmakuFetch block")
    if "WishFountainService.CancelRecentWishesRequest(danmakuFetchSuccessHandler, danmakuFetchFailureHandler);" not in ui_cancel_block:
        return fail("UI CancelDanmakuFetch must unregister pending waiters")

    print("WishDanmakuFetchLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
