"""Guard: Dragon King boss gun ammo profiles must not overwrite reforge baselines."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs")
INTEGRATION_SOURCE = Path("Integration/BossRushIntegration.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunReforgeBaselineGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    integration_text = INTEGRATION_SOURCE.read_text(encoding="utf-8-sig")

    forbidden = [
        "9.2f * profile.FireRateMult",
        "26f * profile.GunDamageMult",
        "private static DragonKingBossGunProfileId lastAppliedProfileId",
        "private static bool hasAppliedProfile",
    ]
    for snippet in forbidden:
        if snippet in text:
            return fail("found old hard-coded/global profile logic -> " + snippet)

    required = [
        "DefaultProfileDamage",
        "DefaultProfileShootSpeed",
        "appliedProfileByItemInstance",
        "statBaselineByItemInstance",
        "ClearAmmoProfileStateCaches",
        "CaptureAmmoStatBaseline",
        "ReverseStatValue",
        "ReadPositiveStatValue",
        "ShouldForceAmmoProfileApply",
        "RefreshAmmoProfileAfterRuntimeRestore",
        "TryApplyAmmoProfile(Item gunItem, DragonKingBossGunShotProfile profile, string reason)",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing reforge-safe baseline snippet -> " + snippet)

    if "DragonKingBossGunRuntime.RefreshAmmoProfileAfterRuntimeRestore" not in integration_text:
        return fail("DragonKing boss gun runtime restore hook is not registered")

    clear_scene_start = text.find("public static void ClearSceneCaches()")
    clear_ammo_start = text.find("private static void ClearAmmoProfileStateCaches()")
    if clear_scene_start < 0 or clear_ammo_start < 0 or clear_ammo_start <= clear_scene_start:
        return fail("missing separated scene cache and ammo profile cache cleanup")

    clear_scene_block = text[clear_scene_start:clear_ammo_start]
    for snippet in [
        "appliedProfileByItemInstance.Clear()",
        "statBaselineByItemInstance.Clear()",
    ]:
        if snippet in clear_scene_block:
            return fail("scene cache cleanup must not erase held-gun ammo baselines -> " + snippet)

    cleanup_start = text.find("public static void CleanupRuntime()")
    cleanup_end = text.find("/// <summary>", cleanup_start)
    if cleanup_start < 0 or cleanup_end < 0:
        return fail("missing bounded CleanupRuntime method")

    cleanup_block = text[cleanup_start:cleanup_end]
    if "ClearAmmoProfileStateCaches();" not in cleanup_block:
        return fail("CleanupRuntime must clear ammo profile state caches")

    capture_start = text.find("private static DragonKingBossGunStatBaseline CaptureAmmoStatBaseline")
    if capture_start < 0:
        return fail("missing CaptureAmmoStatBaseline method")

    cached_index = text.find("statBaselineByItemInstance.TryGetValue(itemKey, out cachedBaseline)", capture_start)
    reverse_index = text.find("appliedProfileByItemInstance.TryGetValue(itemKey, out previousProfileId)", capture_start)
    if cached_index < 0 or reverse_index < 0 or cached_index > reverse_index:
        return fail("cached baseline must be preferred before reverse-calculating from an applied profile")

    force_start = text.find("private static bool ShouldForceAmmoProfileApply")
    force_end = text.find("internal static bool TryApplyAmmoProfileFromTargetType", force_start)
    if force_start < 0 or force_end < 0:
        return fail("missing bounded ShouldForceAmmoProfileApply helper")

    force_block = text[force_start:force_end]
    if '"ShootOneBullet"' in force_block:
        return fail("shoot hot path must not force repeated stat rewrites")

    try_apply_start = text.find("internal static bool TryApplyAmmoProfile(Item gunItem, DragonKingBossGunShotProfile profile, string reason)")
    try_apply_end = text.find("private static bool ShouldForceAmmoProfileApply", try_apply_start)
    if try_apply_start < 0 or try_apply_end < 0:
        return fail("missing TryApplyAmmoProfile unchanged-profile guard")

    try_apply_block = text[try_apply_start:try_apply_end]
    for snippet in [
        "appliedProfileId == profile.Id",
        "statBaselineByItemInstance.ContainsKey(itemKey)",
        "return false;",
    ]:
        if snippet not in try_apply_block:
            return fail("TryApplyAmmoProfile must skip unchanged profile rewrites -> " + snippet)

    print("DragonKingBossGunReforgeBaselineGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
