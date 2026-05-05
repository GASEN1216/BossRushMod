"""ZombieModeWindowsVerificationScriptGuard: ensure the Windows goal verification entry stays wired."""

from pathlib import Path
import sys


SCRIPT = Path("test_zombiemode_goal_windows.bat")
DOC = Path("docs/2026-05-03_末日丧尸模式_goal执行文档.md")


def fail(message: str) -> int:
    print("ZombieModeWindowsVerificationScriptGuard: FAIL - " + message)
    return 1


def main() -> int:
    if not SCRIPT.is_file():
        return fail("missing root-level Windows verification script")

    script = SCRIPT.read_text(encoding="utf-8")
    for required in [
        "call compile_official.bat",
        "call test_bossrush_official.bat",
        "Manual in-game smoke required",
        "Use Zombie Tide Invitation",
        "Reach wave 5 boss",
        "latest Player.log",
        "production readiness still requires the manual smoke",
    ]:
        if required not in script:
            return fail("missing script requirement: " + required)

    if "exit /b 0" not in script:
        return fail("script must exit cleanly after printing manual smoke requirements")

    if not DOC.is_file():
        return fail("missing goal document")
    doc = DOC.read_text(encoding="utf-8")
    if "test_zombiemode_goal_windows.bat" not in doc:
        return fail("goal document must mention the Windows verification script")

    print("ZombieModeWindowsVerificationScriptGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
