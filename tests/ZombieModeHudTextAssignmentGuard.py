"""Guard: Zombie Mode HUD should avoid redundant TMP assignments."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModeHudController.cs")


def fail(message: str) -> int:
    print("ZombieModeHudTextAssignmentGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    required = [
        "private string lastMainText;",
        "private string lastSafeZoneText;",
        "private string lastStageText;",
        "SetTextIfChanged(mainText, inst.GetZombieModeHudMainText(RunId), ref lastMainText);",
        "SetTextIfChanged(safeZoneText, inst.GetZombieModeHudSafeZoneText(RunId), ref lastSafeZoneText);",
        "SetTextIfChanged(stageText, inst.GetZombieModeHudStageText(RunId), ref lastStageText);",
        "private static void SetTextIfChanged(TextMeshProUGUI target, string value, ref string lastValue)",
        "private void SetSafeZoneColorIfChanged(Color value)",
    ]
    for needle in required:
        if needle not in text:
            return fail(f"missing expected HUD assignment cache pattern: {needle}")

    forbidden = [
        "mainText.text = inst.GetZombieModeHudMainText(RunId);",
        "safeZoneText.text = inst.GetZombieModeHudSafeZoneText(RunId);",
        "stageText.text = inst.GetZombieModeHudStageText(RunId);",
    ]
    for needle in forbidden:
        if needle in text:
            return fail(f"still has direct repeated TMP assignment: {needle}")

    print("ZombieModeHudTextAssignmentGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
