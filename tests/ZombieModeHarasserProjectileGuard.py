"""Guard: ZombieMode Harasser must fire a projectile before creating its slow zone."""

from pathlib import Path
import sys


RUNTIME = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")
COMPONENTS = Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("ZombieModeHarasserProjectileGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
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


def extract_case_until_break(body: str, case_token: str) -> str | None:
    start = body.find(case_token)
    if start < 0:
        return None

    end = body.find("break;", start + len(case_token))
    if end < 0:
        return None

    return body[start : end + len("break;")]


def main() -> int:
    runtime = RUNTIME.read_text(encoding="utf-8-sig")
    components = COMPONENTS.read_text(encoding="utf-8-sig")

    special_body = extract_method_body(runtime, "private void TryExecuteZombieModeSpecialSkill(")
    if special_body is None:
        return fail("missing TryExecuteZombieModeSpecialSkill body")

    harasser_case = extract_case_until_break(special_body, "case ZombieModeSpecialKind.Harasser:")
    if harasser_case is None:
        return fail("missing Harasser special case")

    if "StartZombieModeTelegraphedPlayerSlow(" in harasser_case:
        return fail("Harasser case reverted to direct player slow telegraph")

    for token in [
        "StartZombieModeHarasserProjectile(",
        "ZombieModeTuning.HarasserProjectileSpeed",
        "ZombieModeTuning.HarasserProjectileDamage",
        "ZombieModeTuning.HarasserProjectileLifetimeSeconds",
        "ZombieModeTuning.HarasserSlowRadius",
        "ZombieModeTuning.HarasserSlowPercent",
        "ZombieModeTuning.HarasserSlowDurationSeconds",
    ]:
        if token not in harasser_case:
            return fail("Harasser case missing projectile token -> " + token)

    projectile_helper = extract_method_body(runtime, "private void StartZombieModeHarasserProjectile(")
    if projectile_helper is None:
        return fail("missing StartZombieModeHarasserProjectile helper")
    for token in [
        "new GameObject(\"ZombieMode_HarasserProjectile\")",
        "ConfigureZombieModeHarasserProjectileVisual(projectile);",
        "ZombieModeHarasserProjectileRuntime runtime = projectile.AddComponent<ZombieModeHarasserProjectileRuntime>();",
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, projectile, runtime, null);",
    ]:
        if token not in projectile_helper:
            return fail("Harasser projectile helper missing token -> " + token)

    visual_helper = extract_method_body(runtime, "private static void ConfigureZombieModeHarasserProjectileVisual(")
    if visual_helper is None:
        return fail("missing Harasser projectile visual helper")
    for token in [
        "projectile.AddComponent<TrailRenderer>()",
        "projectile.AddComponent<LineRenderer>()",
        "projectile.AddComponent<Light>()",
        "ZombieModeHarasserProjectileMaterialOwner",
    ]:
        if token not in visual_helper:
            return fail("Harasser projectile visual is not readable enough -> " + token)

    impact_helper = extract_method_body(runtime, "public void TryExecuteZombieModeHarasserProjectileImpact(")
    if impact_helper is None:
        return fail("missing Harasser projectile impact helper")
    for token in [
        "DealZombieModeRuntimeAreaDamageToPlayer(runId, source, impactPosition, 0.85f, damage);",
        "SpawnZombieModeSlowZone(runId, impactPosition, slowRadius, slowPercent, slowDuration);",
    ]:
        if token not in impact_helper:
            return fail("Harasser impact helper missing token -> " + token)

    slow_zone = extract_method_body(runtime, "private void SpawnZombieModeSlowZone(")
    if slow_zone is None:
        return fail("missing Harasser slow-zone helper")
    for token in [
        "\"ZombieMode_HarasserSlowZone\"",
        "ZombieModeAreaTickRuntime runtime = zone.AddComponent<ZombieModeAreaTickRuntime>();",
        "slowPercent",
    ]:
        if token not in slow_zone:
            return fail("Harasser slow-zone helper missing token -> " + token)

    for token in [
        "public sealed class ZombieModeHarasserProjectileRuntime : ZombieModeTimedRunScopedRuntime",
        "float step = speed * Time.unscaledDeltaTime;",
        "ResolveImpact(inst, player.transform.position);",
        "inst.TryExecuteZombieModeHarasserProjectileImpact(",
    ]:
        if token not in components:
            return fail("Harasser projectile runtime missing token -> " + token)

    harasser_runtime_start = components.find(
        "public sealed class ZombieModeHarasserProjectileRuntime : ZombieModeTimedRunScopedRuntime"
    )
    harasser_runtime_text = components[harasser_runtime_start:] if harasser_runtime_start >= 0 else ""
    stopping = extract_method_body(harasser_runtime_text, "protected override void OnRuntimeStopping(")
    if stopping is None:
        return fail("missing Harasser projectile stopping path")
    if "ResolveImpact(" in stopping:
        return fail("Harasser projectile timeout must count as a successful dodge, not an invisible explosion")
    for token in ["if (expired && !resolved)", "Destroy(gameObject);"]:
        if token not in stopping:
            return fail("Harasser projectile timeout cleanup missing token -> " + token)

    if "if (IsPlayerHit(player))" not in harasser_runtime_text:
        return fail("projectile must verify an actual player hit at the old target point")
    if "ResolveImpact(inst, transform.position);" in harasser_runtime_text:
        return fail("reaching the old target point must not create an invisible explosion")

    if "class ZombieModeHarasserProjectileMaterialOwner" not in components or "Destroy(trailMaterial)" not in components:
        return fail("Harasser projectile materials must be released when the projectile is destroyed")

    print("ZombieModeHarasserProjectileGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
