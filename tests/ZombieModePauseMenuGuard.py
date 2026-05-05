"""ZombieModePauseMenuGuard: Esc/PauseMenu must pause ZombieMode timers and hide HUD."""
from pathlib import Path
import re
import sys


def fail(msg: str) -> int:
    print(msg)
    return 1


def main() -> int:
    entry_text = Path("ZombieMode/ZombieModeEntry.cs").read_text(encoding="utf-8")
    if "internal bool IsZombieModeGamePaused()" not in entry_text:
        return fail("ZombieModePauseMenuGuard: missing shared PauseMenu pause helper")
    if "PauseMenu.Instance != null && PauseMenu.Instance.Shown" not in entry_text:
        return fail("ZombieModePauseMenuGuard: pause helper does not check PauseMenu.Instance.Shown")
    tick_match = re.search(r"private void TickZombieMode\(float deltaTime\)\s*\{(?P<body>.*?)\n        \}", entry_text, re.S)
    if not tick_match:
        return fail("ZombieModePauseMenuGuard: TickZombieMode body not found")
    tick_body = tick_match.group("body")
    if "IsZombieModeGamePaused()" not in tick_body:
        return fail("ZombieModePauseMenuGuard: TickZombieMode does not stop while PauseMenu is shown")

    hud_text = Path("ZombieMode/ZombieModeHudController.cs").read_text(encoding="utf-8")
    if "SetPauseMenuHidden" not in hud_text:
        return fail("ZombieModePauseMenuGuard: HUD has no explicit PauseMenu hide path")
    if "IsZombieModeGamePaused()" not in hud_text:
        return fail("ZombieModePauseMenuGuard: HUD does not query ZombieMode pause state")
    if not re.search(r"canvas\.enabled\s*=\s*!hidden", hud_text):
        return fail("ZombieModePauseMenuGuard: HUD canvas is not disabled when PauseMenu is shown")

    wave_text = Path("ZombieMode/ZombieModeWaveController.cs").read_text(encoding="utf-8")
    if "PreparationTimer -= Time.unscaledDeltaTime" in wave_text:
        return fail("ZombieModePauseMenuGuard: preparation countdown bypasses caller pause gate")
    if "PreparationTimer -= deltaTime" not in wave_text:
        return fail("ZombieModePauseMenuGuard: preparation countdown should consume the TickZombieMode deltaTime")

    extraction_text = Path("ZombieMode/ZombieModeExtractionController.cs").read_text(encoding="utf-8")
    if "float remaining = zombieModeRunState.BeaconChannelDuration" not in extraction_text:
        return fail("ZombieModePauseMenuGuard: beacon channel should be remaining-time based")
    if "remaining -= Time.unscaledDeltaTime" not in extraction_text:
        return fail("ZombieModePauseMenuGuard: beacon/extraction countdown should decrement only when not paused")
    if "float deadline = Time.unscaledTime + zombieModeRunState.BeaconChannelDuration" in extraction_text:
        return fail("ZombieModePauseMenuGuard: beacon channel still uses unpaused wall-clock deadline")
    if "float remaining = ZombieModeTuning.ExtractionCountdownSeconds" not in extraction_text:
        return fail("ZombieModePauseMenuGuard: extraction countdown should be remaining-time based")
    if "remaining -= Time.unscaledDeltaTime" not in extraction_text:
        return fail("ZombieModePauseMenuGuard: extraction countdown should decrement only when not paused")
    if "IsZombieModeGamePaused()" not in extraction_text:
        return fail("ZombieModePauseMenuGuard: extraction countdown does not pause on PauseMenu")
    if "float deadline = Time.unscaledTime + ZombieModeTuning.ExtractionCountdownSeconds" in extraction_text:
        return fail("ZombieModePauseMenuGuard: extraction countdown still uses unpaused wall-clock deadline")
    if "IsZombieModeGamePaused()" not in wave_text:
        return fail("ZombieModePauseMenuGuard: settlement wait does not pause on PauseMenu")
    if "float remaining = ZombieModeTuning.SettlementMaxWaitSeconds" not in wave_text:
        return fail("ZombieModePauseMenuGuard: settlement wait should be remaining-time based")
    if "float deadline = Time.unscaledTime + ZombieModeTuning.SettlementMaxWaitSeconds" in wave_text:
        return fail("ZombieModePauseMenuGuard: settlement wait still uses unpaused wall-clock deadline")

    return 0


if __name__ == "__main__":
    sys.exit(main())
