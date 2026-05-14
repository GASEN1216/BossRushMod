"""Guard: StorageDepositService.CloseService must keep cleanup moving after failures."""

from pathlib import Path
import sys


PARTS = [
    Path("Integration/NPCs/Courier/StorageDepositService.cs"),
    Path("Integration/NPCs/Courier/StorageDepositLifecycle.cs"),
    Path("Integration/NPCs/Courier/StorageDepositTransactions.cs"),
    Path("Integration/NPCs/Courier/StorageDepositSingleRetrieve.cs"),
    Path("Integration/NPCs/Courier/StorageDepositInventoryQuickDeposit.cs"),
    Path("Integration/NPCs/Courier/StorageDepositBulkActions.cs"),
]


def fail(message: str) -> int:
    print("StorageDepositCloseServiceGuard: FAIL - " + message)
    return 1


def read_source() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in PARTS if path.exists())


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace_start = text.find("{", start)
    if brace_start < 0:
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
    text = read_source()
    close_service = extract_method(text, "public static void CloseService()")
    reset_static = extract_method(text, "public static void ResetStaticCaches()")

    if not close_service:
        return fail("missing CloseService")
    if not reset_static:
        return fail("missing ResetStaticCaches")

    for snippet in [
        "UnregisterEvents();",
        "RestoreShopUIText();",
        "savedController.StopTalking();",
        "savedMovement.SetInService(false);",
        "ShowGoodbyeBubble(savedNPCTransform);",
        "Cleanup();",
    ]:
        if snippet not in close_service:
            return fail("CloseService missing cleanup step: " + snippet)

    if close_service.count("catch (Exception e)") < 6:
        return fail("CloseService cleanup steps must be isolated by try/catch logging")

    for snippet in [
        "关闭时解绑事件失败",
        "关闭时恢复 UI 文本失败",
        "关闭时停止对话失败",
        "关闭时恢复移动失败",
        "关闭时显示告别气泡失败",
        "关闭时清理资源失败",
    ]:
        if snippet not in close_service:
            return fail("CloseService missing warning log: " + snippet)

    for snippet in [
        "UnregisterEvents();",
        "RestoreShopUIText();",
        "Cleanup();",
        "isRetrieveAllInProgress = false;",
    ]:
        if snippet not in reset_static:
            return fail("ResetStaticCaches missing fallback cleanup token: " + snippet)

    print("StorageDepositCloseServiceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
