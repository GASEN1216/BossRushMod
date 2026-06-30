"""Guard: Dragon Descendant hot distance math should avoid redundant square roots."""

from pathlib import Path
import sys


CONTROLLER = Path("Integration/DragonDescendant/DragonDescendantAbilities.cs")
PROJECTILES = Path("Integration/DragonDescendant/DragonDescendantAbilities_ProjectilesAndGrenades.cs")


def fail(message: str) -> int:
    print("DragonDescendantDistanceMathGuard: FAIL - " + message)
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
    controller_text = CONTROLLER.read_text(encoding="utf-8-sig")
    leash_body = extract_method_body(controller_text, "private bool IsPlayerOutOfLeashRange()")
    if leash_body is None:
        return fail("missing IsPlayerOutOfLeashRange body")

    if "Vector3.Distance(" in leash_body:
        return fail("Mode E leash check still uses Vector3.Distance")

    leash_required = [
        "float leashDistance = DragonDescendantConfig.LeashDistance;",
        "float leashDistanceSqr = leashDistance * leashDistance;",
        "Vector3 leashDelta = bossCharacter.transform.position - playerCharacter.transform.position;",
        "return leashDelta.sqrMagnitude > leashDistanceSqr;",
    ]
    for snippet in leash_required:
        if snippet not in leash_body:
            return fail("missing squared leash snippet -> " + snippet)

    projectile_text = PROJECTILES.read_text(encoding="utf-8-sig")
    throw_body = extract_method_body(
        projectile_text,
        "private Vector3 CalculateThrowVelocity(Vector3 start, Vector3 target, float verticalSpeed)",
    )
    if throw_body is None:
        return fail("missing CalculateThrowVelocity body")

    forbidden = [
        "Vector3.Distance(horizontalStart, horizontalTarget)",
        "(horizontalTarget - horizontalStart).normalized",
        "float horizontalSpeed = horizontalDistance / totalTime;",
    ]
    for snippet in forbidden:
        if snippet in throw_body:
            return fail("grenade throw velocity still repeats horizontal distance math -> " + snippet)

    throw_required = [
        "Vector3 horizontalDelta = target - start;",
        "horizontalDelta.y = 0f;",
        "float horizontalDistanceSqr = horizontalDelta.sqrMagnitude;",
        "Vector3 horizontalVelocity = horizontalDistanceSqr > 0.0000000001f ? horizontalDelta / totalTime : Vector3.zero;",
        "return horizontalVelocity + Vector3.up * verticalSpeed;",
    ]
    for snippet in throw_required:
        if snippet not in throw_body:
            return fail("missing grenade throw velocity snippet -> " + snippet)

    print("DragonDescendantDistanceMathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
