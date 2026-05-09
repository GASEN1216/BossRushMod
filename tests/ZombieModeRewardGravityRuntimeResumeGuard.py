from pathlib import Path
import sys


EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print("ZombieModeRewardGravityRuntimeResumeGuard: FAIL - " + message)
    return 1


def main() -> int:
    effects = EFFECTS.read_text(encoding="utf-8")

    for token in [
        "RefreshZombieModeProjectileRewardPerformanceState",
        "IsZombieModeBattlefieldGravityAllowed",
        "IsZombieModeBattlefieldGravityRuntimeAllowed",
    ]:
        if token in effects:
            return fail("battlefield gravity should not pause/resume by performance tier -> " + token)

    for token in [
        "StartZombieModeBattlefieldGravityRuntimeIfNeeded(zombieModeRunState.RunId);",
        "if (!IsZombieModeRunValid(runId) ||",
        "while (IsZombieModeRunValid(runId)",
        "if (inst == null || inst.ZombieModeCurrentRunId != runId)",
    ]:
        if token not in effects:
            return fail("battlefield gravity should run consistently while selected -> " + token)

    print("ZombieModeRewardGravityRuntimeResumeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
