"""
Guard the Phantom Witch boss base health tuning.

Requirements:
- boss base health must be 1000
- boss spawn wiring must continue to consume PhantomWitchConfig.BaseHealth
"""

from pathlib import Path
import re
import sys


CONFIG = Path("Integration/PhantomWitch/PhantomWitchConfig.cs")
BOSS = Path("Integration/PhantomWitch/PhantomWitchBoss.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    config_text = CONFIG.read_text(encoding="utf-8")
    boss_text = BOSS.read_text(encoding="utf-8")

    missing: list[str] = []

    if re.search(r"public\s+const\s+float\s+BaseHealth\s*=\s*1000f\s*;", config_text) is None:
        missing.append("BaseHealth is not set to 1000f")

    if "maxHealthStat.BaseValue = PhantomWitchConfig.BaseHealth;" not in boss_text:
        missing.append("boss max health stat no longer uses PhantomWitchConfig.BaseHealth")

    if "character.Health.SetHealth(PhantomWitchConfig.BaseHealth);" not in boss_text:
        missing.append("boss current health no longer uses PhantomWitchConfig.BaseHealth")

    if missing:
        return fail("PhantomWitchBaseHealthGuard: " + " | ".join(missing))

    print("PhantomWitchBaseHealthGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
