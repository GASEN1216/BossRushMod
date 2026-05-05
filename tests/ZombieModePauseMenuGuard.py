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
    if "IsZombieModeRuntimePaused()" not in entry_text:
        return fail("ZombieModePauseMenuGuard: missing shared runtime pause helper")
    runtime_pause_match = re.search(r"internal bool IsZombieModeRuntimePaused\(\)\s*\{(?P<body>.*?)\n        \}", entry_text, re.S)
    if not runtime_pause_match:
        return fail("ZombieModePauseMenuGuard: runtime pause helper body not found")
    runtime_pause_body = runtime_pause_match.group("body")
    if "ZombieModeUIHelper.IsModalInputPaused" not in runtime_pause_body:
        return fail("ZombieModePauseMenuGuard: runtime pause helper must include ZombieMode modal pause")
    if "IsZombieModeGamePaused()" not in runtime_pause_body:
        return fail("ZombieModePauseMenuGuard: runtime pause helper must include PauseMenu pause")
    if "IsZombieModeRuntimePaused()" not in tick_body:
        return fail("ZombieModePauseMenuGuard: TickZombieMode must use shared runtime pause helper")

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
    if "IsZombieModeRuntimePaused()" not in extraction_text:
        return fail("ZombieModePauseMenuGuard: extraction countdown does not use shared runtime pause helper")
    if "float deadline = Time.unscaledTime + ZombieModeTuning.ExtractionCountdownSeconds" in extraction_text:
        return fail("ZombieModePauseMenuGuard: extraction countdown still uses unpaused wall-clock deadline")
    if "IsZombieModeRuntimePaused()" not in wave_text:
        return fail("ZombieModePauseMenuGuard: settlement wait does not use shared runtime pause helper")
    if "float remaining = ZombieModeTuning.SettlementMaxWaitSeconds" not in wave_text:
        return fail("ZombieModePauseMenuGuard: settlement wait should be remaining-time based")
    if "float deadline = Time.unscaledTime + ZombieModeTuning.SettlementMaxWaitSeconds" in wave_text:
        return fail("ZombieModePauseMenuGuard: settlement wait still uses unpaused wall-clock deadline")

    boss_text = Path("ZombieMode/ZombieModeBossController.cs").read_text(encoding="utf-8")
    timed_runtime_match = re.search(r"public abstract class ZombieModeTimedRunScopedRuntime\b(?P<body>.*?)(?=\n    public sealed class ZombieModeAreaTickRuntime)", boss_text, re.S)
    if not timed_runtime_match:
        return fail("ZombieModePauseMenuGuard: timed run-scoped runtime base not found")
    timed_runtime_body = timed_runtime_match.group("body")
    for snippet in (
        "IsZombieModeRuntimePaused()",
        "runtimePauseStartTime",
        "runtimeEndTime += pausedDuration",
        "OnRuntimeResumedAfterPause(inst, pausedDuration)",
    ):
        if snippet not in timed_runtime_body:
            return fail("ZombieModePauseMenuGuard: timed runtime base does not freeze unscaled timers during PauseMenu -> " + snippet)

    area_runtime_match = re.search(r"public sealed class ZombieModeAreaTickRuntime\b(?P<body>.*?)(?=\n    public sealed class ZombieModeBossShieldRuntime)", boss_text, re.S)
    if not area_runtime_match:
        return fail("ZombieModePauseMenuGuard: area tick runtime not found")
    area_runtime_body = area_runtime_match.group("body")
    if "OnRuntimeResumedAfterPause" not in area_runtime_body or "nextTickTime += pausedDuration" not in area_runtime_body:
        return fail("ZombieModePauseMenuGuard: area tick runtime must delay next damage tick after PauseMenu")

    pollution_text = Path("ZombieMode/ZombieModePollution.cs").read_text(encoding="utf-8")
    for class_name, time_field in (
        ("ZombieModeTelegraphedAreaDamageRuntime", "triggerTime += pausedDuration"),
        ("ZombieModeTelegraphedPlayerSlowRuntime", "triggerTime += pausedDuration"),
    ):
        class_match = re.search(r"public sealed class " + class_name + r"\b(?P<body>.*?)(?=\n    public sealed class )", pollution_text, re.S)
        if not class_match:
            return fail("ZombieModePauseMenuGuard: " + class_name + " not found")
        class_body = class_match.group("body")
        if "OnRuntimeResumedAfterPause" not in class_body or time_field not in class_body:
            return fail("ZombieModePauseMenuGuard: " + class_name + " must delay telegraph trigger after PauseMenu")

    threat_match = re.search(r"public sealed class ZombieModeThreatRuntime\b(?P<body>.*?)(?=\n    public sealed class ZombieModeCommanderAuraRuntime)", pollution_text, re.S)
    if not threat_match:
        return fail("ZombieModePauseMenuGuard: threat runtime not found")
    threat_body = threat_match.group("body")
    if "IsZombieModeRuntimePaused()" not in threat_body or "nextSkillTime += pausedDuration" not in threat_body:
        return fail("ZombieModePauseMenuGuard: threat runtime must freeze skill cooldowns during PauseMenu")
    if "IsZombieModeRuntimePaused()" not in pollution_text:
        return fail("ZombieModePauseMenuGuard: pollution damage path must consult runtime pause")

    return 0


if __name__ == "__main__":
    sys.exit(main())
