"""Guard ZombieMode choice UI pause/cursor state and HUD requested offsets."""

from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
cash = Path("ZombieMode/ZombieModeCashInvestmentView.cs")
extraction = Path("ZombieMode/ZombieModeExtractionController.cs")
HUD = Path("ZombieMode/ZombieModeHudController.cs")
UI_HELPER = Path("ZombieMode/ZombieModeUIHelper.cs")


def fail(message: str) -> int:
    print("ZombieModeChoiceUiPauseAndLayoutGuard: FAIL - " + message)
    return 1


def extract_block(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    entry = ENTRY.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    cash_text = cash.read_text(encoding="utf-8")
    extraction_text = extraction.read_text(encoding="utf-8")
    hud = HUD.read_text(encoding="utf-8")
    helper = UI_HELPER.read_text(encoding="utf-8")

    for snippet in [
        "internal sealed class ModalInputLease",
        "_modalInputLeaseCount",
        "_modalPreviousTimeScale",
        "_modalPreviousCursorVisible",
        "_modalPreviousCursorLockState",
        "Time.timeScale = 0f;",
        "Cursor.visible = true;",
        "Cursor.lockState = CursorLockMode.None;",
        "InputManager.DisableInput(inputToken);",
        "InputManager.ActiveInput(lease.InputToken);",
        "Time.timeScale = _modalPreviousTimeScale;",
        "Cursor.visible = _modalPreviousCursorVisible;",
        "Cursor.lockState = _modalPreviousCursorLockState;",
        "internal static bool IsModalInputPaused",
        "internal static void EnforceModalInputPause()",
    ]:
        if snippet not in helper:
            return fail("ZombieModeUIHelper modal input lease missing: " + snippet)

    tick_method = extract_block(entry, "private void TickZombieMode(float deltaTime)")
    if not tick_method:
        return fail("TickZombieMode not found")
    if "ZombieModeUIHelper.IsModalInputPaused" not in tick_method:
        return fail("TickZombieMode must not advance ZombieMode timers while modal UI pauses time")

    late_update = extract_block(Path("ModBehaviour.cs").read_text(encoding="utf-8"), "void LateUpdate()")
    if not late_update:
        return fail("ModBehaviour LateUpdate not found")
    if "ZombieModeUIHelper.EnforceModalInputPause();" not in late_update:
        return fail("ZombieMode modal UI pause must be enforced in LateUpdate while the UI is open")

    for text, class_name in [
        (entry, "ZombieModeStarterChoiceView"),
        (rewards, "ZombieModeRewardSelectionView"),
        (cash_text, "ZombieModeCashInvestmentView"),
        (extraction_text, "ZombieModeExtractionOpportunityView"),
        (rewards, "ZombieModeTemporaryNpcServiceView"),
    ]:
        class_text = extract_block(text, "public sealed class " + class_name)
        if not class_text:
            return fail(class_name + " not found")
        for snippet in [
            "ZombieModeUIHelper.ModalInputLease inputLease",
            "ZombieModeUIHelper.ClaimModalInput(gameObject",
            "inputLease.Release();",
            "RestoreInputState();",
        ]:
            if snippet not in class_text:
                return fail(class_name + " missing shared pause/cursor/input handling: " + snippet)

    for snippet in [
        "new Vector2(24f, -294f)",
        "new Vector2(-408f, -24f)",
        "new Vector2(0f, 156f)",
    ]:
        if snippet not in hud:
            return fail("HUD requested screen offset missing: " + snippet)

    print("ZombieModeChoiceUiPauseAndLayoutGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
