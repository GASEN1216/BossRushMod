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
        'CreateActionButton(actionRow.transform, "SkipZero"',
        'CreateActionButton(actionRow.transform, "Cancel"',
        'buttonLayout.childControlWidth = false;',
        'buttonLayout.childForceExpandWidth = false;',
    ]:
        if required not in text:
            return fail("cash prompt action buttons must use a dedicated bottom row: " + required)

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
