"""Guard: Dragon King boss gun Rocket profile must airburst into multiple parabolic rockets."""

from pathlib import Path
import sys


PROFILE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
PROJECTILE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")
RUNTIME_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs")
RUNTIME_PROJECTILE_SOURCE = Path(
    "Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs"
)


def fail(message: str) -> int:
    print("DragonKingBossGunRocketSplitGuard: FAIL - " + message)
    return 1


def extract_profile(text: str, profile_id: str) -> str | None:
    marker = f"Id = DragonKingBossGunProfileId.{profile_id}"
    marker_index = text.find(marker)
    if marker_index < 0:
        return None

    start = text.rfind("new DragonKingBossGunShotProfile", 0, marker_index)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    profile_text = PROFILE_SOURCE.read_text(encoding="utf-8-sig")
    projectile_text = PROJECTILE_SOURCE.read_text(encoding="utf-8-sig")
    runtime_text = RUNTIME_SOURCE.read_text(encoding="utf-8-sig")
    runtime_projectile_text = RUNTIME_PROJECTILE_SOURCE.read_text(encoding="utf-8-sig")

    rocket = extract_profile(profile_text, "Rocket")
    if rocket is None:
        return fail("missing Rocket profile")

    required_profile_snippets = [
        "UseNativeProjectile = true",
        "UseSplit = true",
        "SplitOnAirburst = true",
        "SplitCount = 6",
        "SplitSpeedFactor = 0.12f",
        "SplitPattern = DragonKingBossGunSplitPattern.Radial",
        "SplitGravity = 24f",
        "SplitExplosionRange = 1.8f",
        "SplitExplosionDamageFactor = 1.45f",
    ]
    for snippet in required_profile_snippets:
        if snippet not in rocket:
            return fail("Rocket profile lost split invariant -> " + snippet)

    required_projectile_snippets = [
        "private void SuppressNativeExplosionForAirburst()",
        "context.explosionRange = 0f;",
        "context.explosionDamage = 0f;",
        "SuppressNativeExplosionForAirburst();",
        "private float ResolveAirburstDistance()",
        "sourceGun.Holder.GetCurrentAimPoint();",
        "Physics.SphereCastNonAlloc(",
        "HandleHit(raycastBuffer[i])",
        "bool secondaryCollisionExplosion =",
        "deathReason == DragonKingBossGunProjectileDeathReason.DamageReceiver",
        "deathReason == DragonKingBossGunProjectileDeathReason.Obstacle",
        "!isNativeExplosion && secondaryProjectile && secondaryCollisionExplosion && profile.SplitExplosionRange > 0f",
    ]
    for snippet in required_projectile_snippets:
        if snippet not in projectile_text:
            return fail("airburst must suppress the primary native explosion -> " + snippet)

    required_runtime_snippets = [
        "private const int NativeRocketBulletTypeId = 326;",
        "nativeRocketProjectileLookupAttempted",
        "nativeRocketProjectileLookupAttempted = false;",
        "if (nativeRocketProjectileLookupAttempted)",
        "TryCacheNativeRocketProjectileFromItemCollection()",
        "TryCacheNativeRocketProjectileFromGunSetting(allGunSettings[i])",
        "gunSetting.TargetBulletID == NativeRocketBulletTypeId",
        "preferredBullet.TypeID == NativeRocketBulletTypeId",
        "loadedBullet.TypeID == NativeRocketBulletTypeId",
        "bool useNative = profile != null && profile.UseNativeProjectile && isSecondary;",
        "bool usesNativeVisual = profile != null && profile.UseNativeProjectile && secondaryProjectile;",
        "!usesNativeVisual && profile.Element == ElementTypes.fire",
        "float fireFxLifetime = Mathf.Clamp(fxDuration, 0.2f, 2f);",
        "UnityEngine.Object.Destroy(fireFx, fireFxLifetime);",
        "if (isSecondary && profile.SplitGravity > 0f)",
        "context.traceAbility = 0f;",
        "Vector3 axis = (useParabolicScatter || forceHorizontalScatter)",
        "? Vector3.up",
        "radialBase = Vector3.ProjectOnPlane(forward, Vector3.up);",
        "float downwardBias = UnityEngine.Random.Range(0.03f, 0.12f);",
        "dir = (dir + Vector3.down * downwardBias).normalized;",
        "bool suppressPrimaryAirburstExplosion = !secondaryProjectile &&",
        "deathReason == DragonKingBossGunProjectileDeathReason.Airburst",
        "context.explosionRange = Mathf.Max(0.3f, profile.SplitExplosionRange);",
        "context.explosionDamage = context.damage * Mathf.Max(0.1f, profile.SplitExplosionDamageFactor);",
    ]
    combined_runtime_text = runtime_text + "\n" + runtime_projectile_text + "\n" + projectile_text
    for snippet in required_runtime_snippets:
        if snippet not in combined_runtime_text:
            return fail("split rockets must keep swapped native secondary visuals -> " + snippet)

    forbidden_snippets = [
        "SplitFuseTime",
        "DragonKingBossGunProjectileDeathReason.TimedFuse",
        "private const float SplitCollisionGraceTime",
        "splitFuseTimer",
        "private bool UsesFixedSplitFuse",
        "private void MoveFixedSplitFuseProjectile(",
        "profile.SplitFuseTime > 0f && profile.SplitGravity > 0f",
        "IsLikelyNativeRocketProjectile",
        "Resources.FindObjectsOfTypeAll<Projectile>()",
        'name.IndexOf("Rocket"',
        'name.IndexOf("RPG"',
        'name.IndexOf("Missile"',
    ]
    for snippet in forbidden_snippets:
        if snippet in combined_runtime_text:
            return fail("split rockets must rely on collision instead of fixed fuse/grace window -> " + snippet)

    print("DragonKingBossGunRocketSplitGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
