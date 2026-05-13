from pathlib import Path
import sys


EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")
EFFECT_PARTS = [
    EFFECTS,
    Path("ZombieMode/ZombieModeRewardOptionCore.cs"),
    Path("ZombieMode/ZombieModeRewardProjectileSpread.cs"),
    Path("ZombieMode/ZombieModeRewardRuntimeModifiers.cs"),
    Path("ZombieMode/ZombieModeRewardTriggerEffects.cs"),
]


def read_effects() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in EFFECT_PARTS)



def fail(message: str) -> int:
    print("ZombieModeRewardProjectilePoolGuard: FAIL - " + message)
    return 1


def main() -> int:
    effects = read_effects() if EFFECTS.exists() else ""
    if not effects:
        return fail("missing ZombieModeRewardEffects.cs")

    required_tokens = [
        "RemoveZombieModePlayerProjectileRuntime(projectile);",
        "runtime.ResetRuntimeState();",
        "public void ResetRuntimeState()",
        "elapsed = 0f;",
        "lastHelixOffset = Vector3.zero;",
        "private void OnDisable()",
        "ResetRuntimeState();",
        "ClearRuntimeConfiguration();",
        "runId = 0;",
        "helixEnabled = false;",
        "trailEnabled = false;",
    ]
    for token in required_tokens:
        if token not in effects:
            return fail("missing pooled projectile cleanup token -> " + token)

    inactive_return = (
        "if (projectile == null)\n"
        "            {\n"
        "                return;\n"
        "            }\n\n"
        "            if (!IsZombieModeActive)"
    )
    if inactive_return not in effects:
        return fail("projectile patch must clean stale runtime when ZombieMode is inactive")

    print("ZombieModeRewardProjectilePoolGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
