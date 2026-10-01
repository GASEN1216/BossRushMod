from pathlib import Path
import sys


EXTRACTION = Path("ZombieMode/ZombieModeRuntimeModule_Extraction.cs")
USAGE = Path("Integration/Items/ZombieTideBeaconUsage.cs")


def fail(message: str) -> int:
    print("ZombieModeBeaconUnavailableReasonGuard: FAIL - " + message)
    return 1


def main() -> int:
    extraction = EXTRACTION.read_text(encoding="utf-8")
    usage = USAGE.read_text(encoding="utf-8")

    for token in [
        "internal string GetZombieModeBeaconUnavailableReasonKey()",
        "BossRush_ZombieMode_Notify_BeaconExtractionLocked",
        "runState.ExtractionChanneling",
    ]:
        if token not in extraction:
            return fail("missing centralized beacon unavailable reason -> " + token)

    if 'NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_BeaconNotPreparation"));' in usage:
        return fail("beacon usage still hardcodes NotPreparation for every unavailable state")

    # 2026-10-01: CanBeUsed is polled by vanilla hover / menu / shortcut UI, so it must not
    # push notifications. The mode-provided reason is shown by TryUseZombieModeBeacon instead.
    if "NotificationText.Push" in usage:
        return fail("beacon usage must not push notifications from CanBeUsed (hover spam)")

    if "NotificationText.Push(L10n.T(GetZombieModeBeaconUnavailableReasonKey()));" not in extraction:
        return fail("beacon use path must display the mode-provided unavailable reason")

    print("ZombieModeBeaconUnavailableReasonGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
