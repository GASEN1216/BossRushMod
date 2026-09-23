"""
Guard: 阿稳寄存「全部丢弃」必须先过确认框（2026-09-23 审美审查 UA-29）。

旧写法：寄存商店里一条下划线富文本链接，点一下就 DiscardAllItems()，全部寄存物永久删除，没有任何确认。
现在：点击只弹确认框（OriginalConfirmDialogueAdapter.ExecuteOverActiveView，不关商店、默认选中「取消」），
玩家确认后重新校验会话代号、服务状态与写入状态，才真正丢弃。

本守卫锁住三件事：
1. 点击入口 OnDiscardAllClicked 不直接调 DiscardAllItems()，而是走确认协程；
2. 确认协程用 destructive=true 的不关 View 入口，且在 Confirmed 之后、DiscardAllItems() 之前重新校验；
3. 全部丢弃的调用点只有确认协程这一处（防止别处绕过确认）。
"""

from pathlib import Path
import re
import sys


BULK = Path("Integration/NPCs/Courier/StorageDepositBulkActions.cs")
SERVICE = Path("Integration/NPCs/Courier/StorageDepositService.cs")
COURIER_DIR = Path("Integration/NPCs/Courier")


def fail(message: str) -> int:
    print("StorageDepositDiscardConfirmGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        ch = text[index]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def main() -> int:
    if not BULK.exists() or not SERVICE.exists():
        return fail("缺少寄存服务源文件")

    bulk = strip_comments(BULK.read_text(encoding="utf-8"))

    click = extract_method(bulk, "private static void OnDiscardAllClicked()")
    if not click:
        return fail("缺少 OnDiscardAllClicked")
    if "DiscardAllItems()" in click:
        return fail("OnDiscardAllClicked 仍直接调用 DiscardAllItems()，全部丢弃没有确认")
    if "ConfirmDiscardAllAsync(" not in click:
        return fail("OnDiscardAllClicked 没有走确认协程 ConfirmDiscardAllAsync")
    if "discardConfirmPending" not in click:
        return fail("OnDiscardAllClicked 缺少确认框重入保护 discardConfirmPending")

    confirm = extract_method(bulk, "private static async UniTaskVoid ConfirmDiscardAllAsync(")
    if not confirm:
        return fail("缺少确认协程 ConfirmDiscardAllAsync")
    if "OriginalConfirmDialogueAdapter.ExecuteOverActiveView(" not in confirm:
        return fail("确认协程必须用不关商店的 OriginalConfirmDialogueAdapter.ExecuteOverActiveView")
    call = confirm[confirm.find("ExecuteOverActiveView("):]
    call = call[:call.find(";")]
    if not re.search(r",\s*true\s*\)\s*$", call):
        return fail("全部丢弃是不可逆操作：ExecuteOverActiveView 的 destructive 参数必须为 true（默认选中取消）")

    confirmed_at = confirm.find("result.Confirmed")
    discard_at = confirm.find("DiscardAllItems()")
    if confirmed_at < 0 or discard_at < 0 or discard_at < confirmed_at:
        return fail("DiscardAllItems() 必须在确认结果 result.Confirmed 判定之后")
    recheck = confirm[confirmed_at:discard_at]
    for token in ("depositSessionGeneration", "isServiceActive", "IsTransactionBusy", "DepositDataManager.CanWrite"):
        if token not in recheck:
            return fail("确认之后、丢弃之前必须重新校验 " + token)
    if "discardConfirmPending = false;" not in confirm[confirm.find("finally"):]:
        return fail("确认协程必须在 finally 里复位 discardConfirmPending")

    # 全部丢弃的调用点只有确认协程一处
    callers = []
    for path in sorted(COURIER_DIR.glob("*.cs")):
        text = strip_comments(path.read_text(encoding="utf-8", errors="ignore"))
        for match in re.finditer(r"(?<![A-Za-z_])DiscardAllItems\(\)\s*;", text):
            callers.append((path.name, match.start()))
    if len(callers) != 1:
        return fail("DiscardAllItems() 的调用点必须只有确认协程一处，当前: " + str(callers))
    name, pos = callers[0]
    if name != BULK.name or not (bulk.find(confirm) <= pos <= bulk.find(confirm) + len(confirm)):
        return fail("DiscardAllItems() 的唯一调用点不在确认协程里")

    service = SERVICE.read_text(encoding="utf-8")
    reset = extract_method(service, "public static void ResetStaticCaches()")
    if "discardConfirmPending = false;" not in reset:
        return fail("ResetStaticCaches 必须复位 discardConfirmPending")

    print("StorageDepositDiscardConfirmGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
