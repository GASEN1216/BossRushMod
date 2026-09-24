from pathlib import Path
import sys


CASH = Path("ZombieMode/ZombieModeCashInvestmentView.cs")


def fail(message: str) -> int:
    print("ZombieModeCashPromptButtonLayoutGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = CASH.read_text(encoding="utf-8")

    for required in [
        'CreateRect("ActionButtonRow"',
        "HorizontalLayoutGroup",
        "CreateActionButton(actionRow.transform",
        'CreateActionButton(actionRow.transform, "Confirm"',
        'CreateActionButton(actionRow.transform, "Cancel"',
        'buttonLayout.childControlWidth = false;',
        'buttonLayout.childForceExpandWidth = false;',
    ]:
        if required not in text:
            return fail("cash prompt action buttons must use a dedicated bottom row: " + required)

    # 2026-09-24 UI 共识对照审查 B-26：底栏只剩「返回 / 主操作」两颗，主操作在最右。
    # 「跳过（投入 0）」并进主按钮（金额为 0 时主按钮写「不投入，直接出发」），不再单挂一颗。
    # 布局组按子物体顺序从左到右排，所以「返回」必须先建。
    if 'CreateActionButton(actionRow.transform, "SkipZero"' in text:
        return fail("cash prompt must not bring back the separate Skip (invest 0) button; amount 0 is the primary button's default")
    cancel_at = text.find('CreateActionButton(actionRow.transform, "Cancel"')
    confirm_at = text.find('CreateActionButton(actionRow.transform, "Confirm"')
    if cancel_at > confirm_at:
        return fail("cash prompt primary (Confirm) must be the right-most button: create Cancel before Confirm")
    if '"BossRush_ZombieMode_CashPrompt_SkipZero"' not in text or "confirmLabel.text" not in text:
        return fail("cash prompt primary label must follow the amount (0 -> SkipZero wording, >0 -> Confirm wording)")

    for forbidden in [
        "float btnY =",
        "xPercent, float yOffset",
        "new Vector2(xPercent, 1f)",
    ]:
        if forbidden in text:
            return fail("cash prompt action buttons still depend on fragile top-anchored y offsets: " + forbidden)

    print("ZombieModeCashPromptButtonLayoutGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
