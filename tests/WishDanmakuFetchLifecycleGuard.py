from pathlib import Path
import sys


FETCH_PIPELINE = Path("Integration/WishFountain/WishFountainFetchPipeline.cs")
UI_SOURCE = Path("Integration/WishFountain/WishFountainUI.cs")
DANMAKU_VIEW = Path("Integration/WishFountain/WishFountainDanmakuView.cs")


def fail(message: str) -> int:
    print("WishDanmakuFetchLifecycleGuard: FAIL - " + message)
    return 1


def strip_line_comments(text: str) -> str:
    # Commented-out code must not satisfy (or trip) a substring check.
    return "\n".join(line.split("//", 1)[0] for line in text.splitlines())


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

    # Danmaku render lifecycle (2026-09-23 UI review UD-22): TMP ignores UI.Shadow, so the text
    # underlay comes from one shared UNDERLAY_ON material instance that OnDestroy must release.
    danmaku_code = strip_line_comments(DANMAKU_VIEW.read_text(encoding="utf-8"))
    if "AddComponent<Shadow>" in danmaku_code:
        return fail("danmaku TMP must not use UI.Shadow (no effect on TMP); use the underlay material")
    material_block = extract_block(danmaku_code, "private Material GetTextMaterial()")
    if 'EnableKeyword("UNDERLAY_ON")' not in material_block:
        return fail("danmaku text material must enable UNDERLAY_ON")
    if "fontSharedMaterial = material" not in danmaku_code:
        return fail("danmaku items must share the underlay material via fontSharedMaterial")
    destroy_block = extract_block(danmaku_code, "private void OnDestroy()")
    if "Destroy(textMaterial);" not in destroy_block:
        return fail("danmaku OnDestroy must destroy the underlay material instance")

    print("WishDanmakuFetchLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
