from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
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

CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    effects = read_effects() if EFFECTS.exists() else ""
    cleanup = CLEANUP.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")

    for token in [
        "public sealed class ZombieModeOptionRuntimeState",
        "public readonly ZombieModeOptionRuntimeState OptionRuntime",
        "public readonly List<ZombieModeAttributeModifierRecord> ModifierRecords",
        "public readonly List<ZombieModeAttributeModifierRecord> GuardianShieldRecords",
        "public void Reset()",
    ]:
        if token not in models:
            return fail("ZombieModeRewardCleanupGuard: model missing token -> " + token)

    for token in [
        "private UnityEngine.Events.UnityAction<Health> zombieModeOptionPlayerHealthChangeHandler;",
        "private bool zombieModeOptionRuntimeCleanupRegistered;",
        "private void RemoveZombieModeOptionRuntimeEffects()",
        "private void UnregisterZombieModeOptionPlayerHealthListener()",
        "RuntimeStatModifierTracker.RemoveAll",
        "GuardianShieldRecords",
        "OnHealthChange.RemoveListener",
        "UnregisterZombieModeOptionPlayerHealthListener",
        "zombieModeRunState.OptionRuntime.Reset();",
    ]:
        if token not in effects:
            return fail("ZombieModeRewardCleanupGuard: effects missing cleanup token -> " + token)

    if "RegisterZombieModeRunOnlyObject(zombieModeRunState.RunId, ZombieModeRunOnlyObjectKind.EventListener, null, zombieModeOptionPlayerHealth, RemoveZombieModeOptionRuntimeEffects)" in effects:
        return fail("ZombieModeRewardCleanupGuard: option listener cleanup must not register full runtime cleanup")

    remove_index = cleanup.find("RemoveZombieModeOptionRuntimeEffects();")
    invalidate_index = cleanup.find("InvalidateZombieModeRun();")
    if remove_index < 0:
        return fail("ZombieModeRewardCleanupGuard: cleanup does not remove option runtime effects")
    if invalidate_index < 0 or remove_index > invalidate_index:
        return fail("ZombieModeRewardCleanupGuard: option cleanup must run before InvalidateZombieModeRun")

    for token in [
        "HandleZombieModeOptionHealthHurt(runId, health, damageInfo, victim, marker);",
        "HandleZombieModeOptionHealthDead(runId, health, damageInfo, character, marker);",
    ]:
        if token not in waves:
            return fail("ZombieModeRewardCleanupGuard: wave controller missing option hook -> " + token)

    if "Health.OnHurt +=" in effects or "Health.OnDead +=" in effects:
        return fail("ZombieModeRewardCleanupGuard: option effects must reuse existing wave hooks")

    print("ZombieModeRewardCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
