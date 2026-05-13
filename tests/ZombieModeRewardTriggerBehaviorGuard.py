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
    print(message)
    return 1


def extract_between(text: str, start_token: str, end_token: str) -> str:
    start = text.find(start_token)
    if start < 0:
        return ""
    end = text.find(end_token, start + len(start_token))
    if end < 0:
        return text[start:]
    return text[start:end]


def main() -> int:
    if not EFFECTS.exists():
        return fail("ZombieModeRewardTriggerBehaviorGuard: missing ZombieModeRewardEffects.cs")

    effects = read_effects()

    required_tokens = [
        "1.5f + 0.25f * (stacks - 1)",
        "0.30f + 0.10f * (stacks - 1)",
        "Mathf.Max(20, 40 - 6 * (stacks - 1))",
        "TriggerZombieModeDoomPulse",
        "for (int i = 0; i < 3; i++)",
        "info.damagePoint = position;",
        "info.damageNormal =",
        "info.isFromBuffOrEffect = true;",
        "zombieModeOptionExplosionSkipLogTime",
    ]
    for token in required_tokens:
        if token not in effects:
            return fail("ZombieModeRewardTriggerBehaviorGuard: missing trigger behavior token -> " + token)

    if "40 - (stacks - 1) * 10" in effects:
        return fail("ZombieModeRewardTriggerBehaviorGuard: doom pulse still uses old interval formula")

    if "0.30f * Mathf.Min(3, options.TriggerCritBurstStacks)" in effects:
        return fail("ZombieModeRewardTriggerBehaviorGuard: crit burst still uses flat stacked multiplier")

    if "CreateZombieModeOptionExplosion(runId, victim.transform.position, 4f + stacks, 45f * stacks);" in effects:
        return fail("ZombieModeRewardTriggerBehaviorGuard: doom pulse still uses single explosion at victim")

    hurt_handler = extract_between(
        effects,
        "private void HandleZombieModeOptionHealthHurt",
        "private void HandleZombieModeOptionHealthDead",
    )
    if not hurt_handler:
        return fail("ZombieModeRewardTriggerBehaviorGuard: missing health hurt option handler")
    if "IsZombieModePlayerProjectileDamage(damageInfo)" not in hurt_handler:
        return fail("ZombieModeRewardTriggerBehaviorGuard: projectile support rewards must use projectile damage predicate")
    if "if (damageInfo.fromWeaponItemID > 0)" in hurt_handler:
        return fail("ZombieModeRewardTriggerBehaviorGuard: projectile support rewards still trigger from raw weapon id")

    print("ZombieModeRewardTriggerBehaviorGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
