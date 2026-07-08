"""Guard: Snow ammo must stay a slow rolling snowball, not the old high-arc ice shell."""

from pathlib import Path
import sys


PROFILES_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
RUNTIME_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs")
AGENT_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunSnowballGuard: FAIL - " + message)
    return 1


def extract_profile_block(text: str, profile_name: str) -> str | None:
    marker = f"Id = DragonKingBossGunProfileId.{profile_name}"
    start = text.find(marker)
    if start < 0:
        return None

    block_start = text.rfind("new DragonKingBossGunShotProfile", 0, start)
    if block_start < 0:
        return None

    next_block = text.find("new DragonKingBossGunShotProfile", start + len(marker))
    if next_block < 0:
        next_block = text.find("};", start)
    if next_block < 0:
        return None

    return text[block_start:next_block]


def require(text: str, snippet: str, message: str) -> int | None:
    if snippet not in text:
        return fail(message + " -> " + snippet)
    return None


def forbid(text: str, snippet: str, message: str) -> int | None:
    if snippet in text:
        return fail(message + " -> " + snippet)
    return None


def main() -> int:
    profiles_text = PROFILES_SOURCE.read_text(encoding="utf-8-sig")
    runtime_text = RUNTIME_SOURCE.read_text(encoding="utf-8-sig")
    agent_text = AGENT_SOURCE.read_text(encoding="utf-8-sig")

    snow_block = extract_profile_block(profiles_text, "Snow")
    if snow_block is None:
        return fail("missing Snow profile")

    required_profile_snippets = [
        "Arc = DragonKingBossGunArcMode.None",
        "Gravity = 0f",
        "MaxLifetimeSeconds = 5f",
        "DamageFactor = 0.25f",
        "UseRollingSnowball = true",
        "FixedSpeed = 5.2f",
        "SplitFixedSpeed = 4.8f",
        "RollingGrowthDuration = 5f",
        "RollingStartScaleFactor = 0.72f",
        "RollingEndScaleFactor = 21.5f",
        "RollingSecondaryGrowthDuration = 2f",
        "RollingSecondaryStartScaleFactor = 1f",
        "RollingSecondaryEndScaleFactor = 5f",
        "SplitCount = 4",
        "SplitMaxLifetimeSeconds = 2f",
        "SplitDamageFactor = 0.16f",
        "SplitSpreadAngle = 360f",
        "SplitActivationDelay = 0f",
        "SplitInvulnerableDuration = 0.1f",
        "MaxGroundZonesPerShot = 4",
        "GroundZoneDuration = 1f",
        "GroundZoneAllowSecondary = false",
        "SplitExplosionRange = 1.15f",
        "SplitExplosionDamageFactor = 0.18f",
        'TrailFxPrefab = ""',
        'HitFxPrefab = ""',
        'ExplosionFxPrefab = ""',
    ]
    for snippet in required_profile_snippets:
        result = require(snow_block, snippet, "Snow rolling profile contract changed")
        if result is not None:
            return result

    for snippet in [
        "Arc = DragonKingBossGunArcMode.High",
        "ArcLift = 0.78f",
        "GroundZoneDuration = 2.4f",
        "SplitActivationDelay = 0.35f",
        "Fx_DragonGun_Snow_",
    ]:
        result = forbid(snow_block, snippet, "Snow old high-arc contract came back")
        if result is not None:
            return result

    forced_death_fx_branch = """if (profile.UseRollingSnowball &&
                secondaryProjectile &&
                (deathReason == DragonKingBossGunProjectileDeathReason.DamageReceiver ||
                 deathReason == DragonKingBossGunProjectileDeathReason.Obstacle))
            {
                return true;
            }"""
    result = forbid(
        agent_text,
        forced_death_fx_branch,
        "Snow rolling death FX must respect PlayObstacleHitFx",
    )
    if result is not None:
        return result

    for snippet in [
        "if (!isSecondary && profile.MaxLifetimeSeconds > 0f)",
        "context.distance = Mathf.Max(0.4f, context.speed * profile.MaxLifetimeSeconds)",
        "float fixedSpeed = isSecondary ? profile.SplitFixedSpeed : profile.FixedSpeed;",
        "if (profile.UseRollingSnowball)",
        "forceHorizontalScatter = true;",
    ]:
        result = require(runtime_text, snippet, "Snow runtime movement support changed")
        if result is not None:
            return result

    for snippet in [
        "private void ApplyRollingSnowballVisual(float deltaTime)",
        "profile.UseRollingSnowball",
        "Mathf.SmoothStep(0f, 1f, progress)",
        "RollingSecondaryGrowthDuration",
        "RollingSecondaryEndScaleFactor",
        "transform.localScale = rollingBaseScale * scaleFactor;",
        "projectile.radius = Mathf.Max(0.02f, rollingBaseRadius * scaleFactor);",
        "private float ResolveRollingDamageFactor()",
        "damageInfo.damageValue *= ResolveRollingDamageFactor();",
        "Mathf.Max(0.1f, profile.SplitExplosionDamageFactor) * ResolveRollingDamageFactor()",
        "if (!splitActivated && profile.UseRollingSnowball)",
        "profile.SplitInvulnerableDuration <= 0f",
        "return !secondaryProjectile &&",
        "deathReason == DragonKingBossGunProjectileDeathReason.MaxDistance",
        "deathReason == DragonKingBossGunProjectileDeathReason.DamageReceiver",
        "deathReason == DragonKingBossGunProjectileDeathReason.Obstacle",
    ]:
        result = require(agent_text, snippet, "Snow agent behavior changed")
        if result is not None:
            return result

    print("DragonKingBossGunSnowballGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
