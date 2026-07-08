"""Guard: Dragon King boss gun Nano ammo should stay a fast low-damage micro-needle round."""

from pathlib import Path
import sys


PROFILE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
PROJECTILE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunNanoNeedleGuard: FAIL - " + message)
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

    nano = extract_profile(profile_text, "Nano")
    if nano is None:
        return fail("missing Nano profile")

    required_profile_snippets = [
        "Scale = 0.24f",
        "SpeedFactor = 1.75f",
        "DistanceFactor = 1.2f",
        "DamageFactor = 0.58f",
        "TraceAbility = 0f",
        "Element = ElementTypes.poison",
        "Pierce = 3",
        "Bounce = 3",
        "MarkPerHit = 1",
        "MaxMarksPerTargetPerShot = 1",
        "FireRateMult = 2.2f",
        "GunDamageMult = 0.15f",
        "OverrideCapacity = 36",
        "OverrideReloadTime = 2.45f",
        "OverrideBulletDistance = 28f",
        "TrailFxPrefab = \"\"",
        "HitFxPrefab = \"\"",
        "ExplosionFxPrefab = \"\"",
    ]
    for snippet in required_profile_snippets:
        if snippet not in nano:
            return fail("Nano profile lost micro-needle invariant -> " + snippet)

    forbidden_profile_snippets = [
        "UseSticky = true",
        "UseGroundZone = true",
        "UseSplit = true",
        "SplitCount =",
        "SplitTraceAbility =",
        "SplitOrbitRadius =",
        "SecondaryMarkPerHit =",
        "MaxSecondaryMarksPerTargetPerShot =",
    ]
    for snippet in forbidden_profile_snippets:
        if snippet in nano:
            return fail("Nano profile should no longer use swarm/sticky logic -> " + snippet)

    if "profile.Id == DragonKingBossGunProfileId.Energy &&" not in projectile_text:
        return fail("split retarget logic should stay scoped to Energy rounds")

    if "SplitRefreshNearestTraceTarget" in profile_text or "ShouldRefreshTraceTarget()" in projectile_text:
        return fail("obsolete Nano-specific split retarget hooks should be removed")

    print("DragonKingBossGunNanoNeedleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
