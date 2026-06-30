"""Guard: zombie support projectile callers should not pre-normalize directions."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModeRewardTriggerEffects.cs")


def fail(message: str) -> int:
    print("ZombieModeSupportProjectileDirectionGuard: FAIL - " + message)
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
    hurt_body = extract_method_body(
        text,
        "private void HandleZombieModeOptionHealthHurt(",
    )
    if hurt_body is None:
        return fail("missing HandleZombieModeOptionHealthHurt body")

    spawn_body = extract_method_body(
        text,
        "private bool TrySpawnZombieModePlayerSupportProjectile(Vector3 origin, Vector3 direction, float damageFactor, float distanceFactor)",
    )
    if spawn_body is None:
        return fail("missing TrySpawnZombieModePlayerSupportProjectile body")

    forbidden = [
        "(nearest.transform.position - victim.transform.position).normalized",
        "baseDirection.Normalize();",
        "returnDirection.normalized",
    ]
    for snippet in forbidden:
        if snippet in hurt_body:
            return fail("support projectile caller still pre-normalizes -> " + snippet)

    required = [
        "Vector3 direction = nearest.transform.position - victim.transform.position;",
        "Vector3 leftDirection = Quaternion.Euler(0f, -20f, 0f) * baseDirection;",
        "Vector3 rightDirection = Quaternion.Euler(0f, 20f, 0f) * baseDirection;",
        "TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, returnDirection, 0.45f, 0.70f)",
        "direction = direction.sqrMagnitude > 0.001f ? direction.normalized : player.transform.forward;",
    ]
    combined = hurt_body + "\n" + spawn_body
    for snippet in required:
        if snippet not in combined:
            return fail("missing raw direction snippet -> " + snippet)

    print("ZombieModeSupportProjectileDirectionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
