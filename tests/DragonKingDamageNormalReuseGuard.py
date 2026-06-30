"""Guard: Dragon King AoE hit paths should not normalize damage normals twice."""

from pathlib import Path
import sys


RUNTIME = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs")
ZONES = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs")


def fail(message: str) -> int:
    print("DragonKingDamageNormalReuseGuard: FAIL - " + message)
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


def main() -> int:
    runtime_text = RUNTIME.read_text(encoding="utf-8-sig")
    zones_text = ZONES.read_text(encoding="utf-8-sig")

    apply_radius = extract_method_body(runtime_text, "internal static void ApplyRadiusDamage(")
    if apply_radius is None:
        return fail("missing ApplyRadiusDamage body")

    tick_zone = extract_method_body(zones_text, "private void TickZone()")
    if tick_zone is None:
        return fail("missing TickZone body")

    if "(receiver.transform.position - position).normalized" in apply_radius:
        return fail("ApplyRadiusDamage still normalizes before CreateDamageInfo")

    if "(receiver.transform.position - transform.position).normalized" in tick_zone:
        return fail("TickZone still normalizes before CreateDamageInfo")

    required = [
        (runtime_text, "private const float DamageNormalFallbackSqr = 0.0000000001f;"),
        (apply_radius, "Vector3 damageNormal = receiver.transform.position - position;"),
        (apply_radius, "if (damageNormal.sqrMagnitude <= DamageNormalFallbackSqr)"),
        (zones_text, "private const float DamageNormalFallbackSqr = 0.0000000001f;"),
        (tick_zone, "Vector3 damageNormal = receiver.transform.position - transform.position;"),
        (tick_zone, "if (damageNormal.sqrMagnitude <= DamageNormalFallbackSqr)"),
        (runtime_text, "damageInfo.damageNormal = normal.normalized;"),
    ]
    for body, snippet in required:
        if snippet not in body:
            return fail("missing raw-normal reuse snippet -> " + snippet)

    print("DragonKingDamageNormalReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
