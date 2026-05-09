from pathlib import Path
import sys


EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")
USAGE = Path("Integration/Items/ZombieTideBeaconUsage.cs")


def fail(message: str) -> int:
    print("ZombieModeBeaconUnavailableReasonGuard: FAIL - " + message)
    return 1


def main() -> int:
    extraction = EXTRACTION.read_text(encoding="utf-8")
    usage = USAGE.read_text(encoding="utf-8")

    for token in [
        "public string GetZombieModeBeaconUnavailableReasonKey()",
        "BossRush_ZombieMode_Notify_BeaconExtractionLocked",
        "zombieModeRunState.ExtractionChanneling",
    ]:
        if token not in extraction:
            return fail("missing centralized beacon unavailable reason -> " + token)

    if 'NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_BeaconNotPreparation"));' in usage:
        return fail("beacon usage still hardcodes NotPreparation for every unavailable state")

    if "L10n.T(inst.GetZombieModeBeaconUnavailableReasonKey())" not in usage:
        return fail("beacon usage must display the mode-provided unavailable reason")

    print("ZombieModeBeaconUnavailableReasonGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
