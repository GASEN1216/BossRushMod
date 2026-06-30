"""Guard: Phantom Witch flat-distance threshold checks should avoid sqrt."""

from pathlib import Path
import sys


RUNTIME_TICKS = Path("Integration/PhantomWitch/PhantomWitchAbilityController_RuntimeTicks.cs")
STEALTH_ATTACKS = Path("Integration/PhantomWitch/PhantomWitchAbilityController_StealthAndAttacks.cs")
MOVEMENT_DAMAGE = Path("Integration/PhantomWitch/PhantomWitchAbilityController_MovementAndDamage.cs")


def fail(message: str) -> int:
    print("PhantomWitchFlatDistanceSqrGuard: FAIL - " + message)
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


def require(body: str, label: str, snippets: list[str]) -> int:
    for snippet in snippets:
        if snippet not in body:
            return fail(label + " missing squared flat-distance snippet -> " + snippet)

    return 0


def main() -> int:
    runtime_text = RUNTIME_TICKS.read_text(encoding="utf-8-sig")
    movement_text = MOVEMENT_DAMAGE.read_text(encoding="utf-8-sig")
    stealth_text = STEALTH_ATTACKS.read_text(encoding="utf-8-sig")

    movement_body = extract_method_body(movement_text, "private float GetFlatDistanceSqr(Vector3 a, Vector3 b)")
    if movement_body is None:
        return fail("missing GetFlatDistanceSqr helper")

    if "Vector3.Distance(" in movement_body:
        return fail("flat-distance helper still uses Vector3.Distance")

    helper_result = require(
        movement_body,
        "GetFlatDistanceSqr",
        [
            "a.y = 0f;",
            "b.y = 0f;",
            "return (a - b).sqrMagnitude;",
        ],
    )
    if helper_result != 0:
        return helper_result

    tick_heal_body = extract_method_body(runtime_text, "private void TickMinionHealBonus()")
    if tick_heal_body is None:
        return fail("missing TickMinionHealBonus body")

    tick_harass_body = extract_method_body(runtime_text, "private void TickHarassMinionPressure()")
    if tick_harass_body is None:
        return fail("missing TickHarassMinionPressure body")

    sweep_body = extract_method_body(stealth_text, "private IEnumerator ExecuteScytheSweep()")
    if sweep_body is None:
        return fail("missing ExecuteScytheSweep body")

    combined = tick_heal_body + "\n" + tick_harass_body + "\n" + sweep_body
    if "GetFlatDistance(" in combined:
        return fail("threshold checks still call GetFlatDistance")

    checks = [
        (
            tick_heal_body,
            "TickMinionHealBonus",
            [
                "float sustainProximityRadius = PhantomWitchConfig.SustainProximityRadius;",
                "float sustainProximityRadiusSqr = sustainProximityRadius * sustainProximityRadius;",
                "GetFlatDistanceSqr(minion.transform.position, bossCharacter.transform.position) <= sustainProximityRadiusSqr",
            ],
        ),
        (
            tick_harass_body,
            "TickHarassMinionPressure",
            [
                "float harassMinionPressureRadius = PhantomWitchConfig.HarassMinionPressureRadius;",
                "float harassMinionPressureRadiusSqr = harassMinionPressureRadius * harassMinionPressureRadius;",
                "GetFlatDistanceSqr(minion.transform.position, target.transform.position) > harassMinionPressureRadiusSqr",
            ],
        ),
        (
            sweep_body,
            "ExecuteScytheSweep",
            [
                "float scytheSweepTeleportDistance = PhantomWitchConfig.ScytheSweepRadius + 0.6f;",
                "float scytheSweepTeleportDistanceSqr = scytheSweepTeleportDistance * scytheSweepTeleportDistance;",
                "GetFlatDistanceSqr(bossCharacter.transform.position, target.transform.position) > scytheSweepTeleportDistanceSqr",
            ],
        ),
    ]
    for body, label, snippets in checks:
        result = require(body, label, snippets)
        if result != 0:
            return result

    print("PhantomWitchFlatDistanceSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
