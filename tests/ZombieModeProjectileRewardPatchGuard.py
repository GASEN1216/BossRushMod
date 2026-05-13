from pathlib import Path
import sys


PATCH = Path("ZombieMode/ZombieModeRewardProjectilePatch.cs")
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

COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    if not PATCH.exists():
        return fail("ZombieModeProjectileRewardPatchGuard: missing projectile patch file")
    if not EFFECTS.exists():
        return fail("ZombieModeProjectileRewardPatchGuard: missing reward effects file")

    patch = PATCH.read_text(encoding="utf-8")
    effects = read_effects()
    compile_text = COMPILE.read_text(encoding="utf-8")

    for token in [
        '[HarmonyPatch(typeof(Projectile), "Init", new System.Type[] { typeof(ProjectileContext) })]',
        "internal static class ZombieModeRewardProjectileInitPatch",
        "inst.ApplyZombieModeProjectileRewardEffects(__instance);",
    ]:
        if token not in patch:
            return fail("ZombieModeProjectileRewardPatchGuard: missing patch token -> " + token)

    if "UpdateMoveAndCheck" in patch:
        return fail("ZombieModeProjectileRewardPatchGuard: first version must not patch UpdateMoveAndCheck")

    for token in [
        "public void ApplyZombieModeProjectileRewardEffects(Projectile projectile)",
        "ProjectileContext context = projectile.context;",
        "context.fromWeaponItemID <= 0",
        "projectile.context = context;",
        "TryApplyZombieModeElementalProjectileEffect(ref context, options);",
        "context.element_Fire",
        "context.element_Ice",
        "context.element_Poison",
        "GameplayDataSettings.Buffs.Burn",
        "GameplayDataSettings.Buffs.Cold",
        "GameplayDataSettings.Buffs.Poison",
        "context.penetrate",
        "context.armorPiercing",
        "context.armorBreak",
    ]:
        if token not in effects:
            return fail("ZombieModeProjectileRewardPatchGuard: missing effect token -> " + token)

    for banned in ["context.speed", "context.explosionRange"]:
        if banned in effects:
            return fail("ZombieModeProjectileRewardPatchGuard: banned projectile mutation -> " + banned)

    for path in [
        "ZombieMode\\ZombieModeRewardEffects.cs",
        "ZombieMode\\ZombieModeRewardProjectilePatch.cs",
    ]:
        if path not in compile_text:
            return fail("ZombieModeProjectileRewardPatchGuard: missing compile entry -> " + path)

    print("ZombieModeProjectileRewardPatchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
