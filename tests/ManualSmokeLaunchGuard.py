"""
Guard: the manual smoke helper must launch Duckov through Steam.

Launching Duckov.exe directly triggers Steamworks RestartAppIfNecessary and
opens a fatal-error window before gameplay can start. Manual Runtime_Smoke must
therefore use the Steam app id launch path.
"""

from pathlib import Path
import sys


SCRIPT = Path("test_bossrush_smoke_manual.bat")
STEAM_APP_ID_DECL = 'set "steam_app_id=3167020"'
STEAM_APP_URL = "steam://rungameid/%steam_app_id%"


def fail(message: str) -> int:
    print("ManualSmokeLaunchGuard: " + message)
    return 1


def main() -> int:
    if not SCRIPT.exists():
        return fail("missing test_bossrush_smoke_manual.bat")

    text = SCRIPT.read_text(encoding="utf-8", errors="ignore")
    lower = text.lower()

    if STEAM_APP_ID_DECL not in lower:
        return fail("manual smoke helper must declare Steam app id 3167020")

    if STEAM_APP_URL not in lower:
        return fail("manual smoke helper must launch via steam://rungameid/%STEAM_APP_ID%")

    if 'start "" "%game_exe%"' in lower:
        return fail("manual smoke helper must not launch Duckov.exe directly")

    print("ManualSmokeLaunchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
