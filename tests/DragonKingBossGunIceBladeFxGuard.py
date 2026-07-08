"""Guard: IceBlade projectile FX must be custom and pool-safe."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")
PROFILES = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunIceBladeFxGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str | None:
    start = text.find(signature)
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


def extract_profile(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    end = text.find("new DragonKingBossGunShotProfile", start + len(signature))
    return text[start:end] if end >= 0 else text[start:]


def require(text: str, snippet: str, label: str) -> int | None:
    if snippet not in text:
        return fail(f"missing {label}: {snippet}")
    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    profiles = PROFILES.read_text(encoding="utf-8-sig")
    ice_profile = extract_profile(profiles, "Id = DragonKingBossGunProfileId.IceBlade")
    if ice_profile is None:
        return fail("missing IceBlade profile")

    for snippet, label in [
        ('TrailFxPrefab = "",', "IceBlade should not use bundle trail prefab"),
        ('HitFxPrefab = "",', "IceBlade should not use bundle hit prefab"),
        ('ExplosionFxPrefab = ""', "IceBlade should not use bundle explosion prefab"),
    ]:
        result = require(ice_profile, snippet, label)
        if result is not None:
            return result

    for snippet, label in [
        ("private TrailRenderer iceBladeTrailRenderer;", "cached IceBlade trail renderer field"),
        ("else\n            {\n                DisableIceBladeTrail();\n            }", "non-IceBlade pooled trail cleanup"),
        ("DisableIceBladeTrail();", "pooled trail disable call"),
    ]:
        result = require(text, snippet, label)
        if result is not None:
            return result

    create_trail = extract_method(text, "private void CreateIceBladeTrail()")
    if create_trail is None:
        return fail("missing CreateIceBladeTrail")

    for snippet, label in [
        ("trail.enabled = true;", "IceBlade trail enable"),
        ("trail.Clear();", "IceBlade trail clear before reuse"),
        ("trail.sharedMaterial = GetOrCreateIceMaterial();", "IceBlade shared trail material"),
        ("trail.numCornerVertices = 4;", "more polished IceBlade trail corners"),
        ("trail.numCapVertices = 4;", "more polished IceBlade trail caps"),
    ]:
        result = require(create_trail, snippet, label)
        if result is not None:
            return result

    disable_trail = extract_method(text, "private void DisableIceBladeTrail()")
    if disable_trail is None:
        return fail("missing DisableIceBladeTrail")

    for snippet, label in [
        ("if (iceBladeTrailRenderer == null)", "pooled IceBlade trail skips non-IceBlade hot path"),
        ("iceBladeTrailRenderer.Clear();", "pooled IceBlade trail clear on disable"),
        ("iceBladeTrailRenderer.enabled = false;", "pooled IceBlade trail disable"),
    ]:
        result = require(disable_trail, snippet, label)
        if result is not None:
            return result

    death_fx = extract_method(text, "private bool ShouldPlayDeathExplosionFx()")
    if death_fx is None:
        return fail("missing ShouldPlayDeathExplosionFx")

    if "DragonKingBossGunProfileId.IceBlade" in death_fx:
        return fail("IceBlade must not force old death explosion FX on obstacle/max distance")

    shatter_fx = extract_method(text, "private void SpawnIceBladeShatterEffect(")
    if shatter_fx is None:
        return fail("missing SpawnIceBladeShatterEffect")

    for snippet, label in [
        ("DragonGun_IceBladeShatterFx", "custom IceBlade shatter object"),
        ("new ParticleSystem.Burst(0f, 14)", "focused IceBlade shard burst"),
        ("ParticleSystemShapeType.Cone", "directional IceBlade shard cone"),
        ("renderer.renderMode = ParticleSystemRenderMode.Stretch;", "stretched IceBlade shard streaks"),
        ("renderer.sharedMaterial = GetOrCreateIceMaterial();", "shared IceBlade shatter material"),
        ("UnityEngine.Object.Destroy(iceFx, 0.7f);", "IceBlade shatter cleanup"),
    ]:
        result = require(shatter_fx, snippet, label)
        if result is not None:
            return result

    if "LightType.Point" in shatter_fx:
        return fail("IceBlade shatter effect should avoid per-hit lights")

    if "FineFrostMist" in shatter_fx:
        return fail("IceBlade shatter effect should avoid the extra mist particle system")

    handle_death = extract_method(text, "private void HandleDeath()")
    if handle_death is None:
        return fail("missing HandleDeath")

    for snippet, label in [
        ("profile.Id == DragonKingBossGunProfileId.IceBlade", "IceBlade custom death FX branch"),
        ("SpawnIceBladeShatterEffect(resolvedDeathPoint, deathNormal);", "IceBlade custom shatter call"),
    ]:
        result = require(handle_death, snippet, label)
        if result is not None:
            return result

    if "trail.material = GetOrCreateIceMaterial();" in text:
        return fail("IceBlade trail must use sharedMaterial to avoid material instance churn")

    if "renderer.material = GetOrCreateIceMaterial();" in text:
        return fail("IceBlade pierce particles must use sharedMaterial to avoid material instance churn")

    result = require(text, "renderer.sharedMaterial = GetOrCreateIceMaterial();", "IceBlade shared particle material")
    if result is not None:
        return result

    print("DragonKingBossGunIceBladeFxGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
