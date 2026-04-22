"""
Guard the Rev.2 Phantom Witch telemetry contract.

This is a source-level guard for environments where the Windows compile /
runtime pipeline is unavailable. It ensures the controller implements the
minimum telemetry surface promised by the spec.
"""

from pathlib import Path
import re
import sys


ABILITY = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, pattern: str, description: str, flags: int = 0) -> str | None:
    if re.search(pattern, text, flags) is None:
        return description
    return None


def main() -> int:
    text = ABILITY.read_text(encoding="utf-8")
    missing = [
        require(text, r"stealth_exit", "missing stealth_exit telemetry"),
        require(text, r"stealth_timeout", "missing stealth_timeout telemetry"),
        require(text, r"stealth_ratio_snapshot", "missing stealth_ratio_snapshot telemetry"),
        require(text, r"phase3_motion_snapshot", "missing phase3_motion_snapshot telemetry"),
        require(text, r"minion_first_cleared", "missing minion_first_cleared telemetry"),
        require(text, r"boss_first_phase3_exit", "missing boss_first_phase3_exit telemetry"),
        require(text, r"weapon_transform_guard", "missing weapon_transform_guard telemetry"),
        require(text, r"wraith_fallback_to_sweep", "missing wraith_fallback_to_sweep telemetry"),
        require(text, r"minion_roster_desync", "missing minion_roster_desync telemetry"),
        require(text, r"Verdict:", "missing Verdict summary output"),
        require(text, r"OnDisable\s*\(", "missing OnDisable cleanup hook"),
        require(text, r"TrueStealthMaxDuration", "missing true stealth timeout enforcement"),
    ]

    missing = [item for item in missing if item is not None]
    if missing:
        return fail("PhantomWitchTelemetryGuard: FAIL | " + " | ".join(missing))

    print("PhantomWitchTelemetryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
