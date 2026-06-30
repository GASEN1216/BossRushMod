"""Guard: Dragon King projectile movement should reuse speed computed during direction update."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")


def fail(message: str) -> int:
    print("DragonKingProjectileSpeedReuseGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")
    manual_body = extract_method_body(text, "private void ManualMoveAndCheck()")
    if manual_body is None:
        return fail("missing ManualMoveAndCheck body")

    update_body = extract_method_body(text, "private float UpdateDirectionAndVelocity(float deltaTime)")
    if update_body is None:
        return fail("UpdateDirectionAndVelocity must return current speed")

    if "float distanceThisFrame = velocityRef(projectile).magnitude * deltaTime;" in manual_body:
        return fail("ManualMoveAndCheck still recomputes velocity magnitude for frame distance")

    required_manual = [
        "float currentSpeed = UpdateDirectionAndVelocity(deltaTime);",
        "float distanceThisFrame = currentSpeed * deltaTime;",
    ]
    for snippet in required_manual:
        if snippet not in manual_body:
            return fail("missing speed reuse snippet in ManualMoveAndCheck -> " + snippet)

    forbidden_update = [
        "private void UpdateDirectionAndVelocity(float deltaTime)",
        "direction = velocity.normalized;",
    ]
    for snippet in forbidden_update:
        if snippet in update_body:
            return fail("UpdateDirectionAndVelocity still uses old speed/direction path -> " + snippet)

    required_update = [
        "float currentSpeed;",
        "currentSpeed = Mathf.Max(6f, velocity.magnitude);",
        "currentSpeed = velocity.magnitude;",
        "direction = currentSpeed > 0.00001f ? velocity / currentSpeed : Vector3.zero;",
        "return currentSpeed;",
    ]
    for snippet in required_update:
        if snippet not in update_body:
            return fail("missing current-speed snippet in UpdateDirectionAndVelocity -> " + snippet)

    print("DragonKingProjectileSpeedReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
