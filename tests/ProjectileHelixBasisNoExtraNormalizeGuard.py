"""Guard: helix projectile basis should avoid redundant vertical normalization."""

from pathlib import Path
import sys


DRAGON_PROJECTILE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")
ZOMBIE_EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print("ProjectileHelixBasisNoExtraNormalizeGuard: FAIL - " + message)
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


def check_helix_body(label: str, body: str | None) -> int:
    if body is None:
        return fail("missing " + label + " helix body")

    if "Vector3.Cross(forward, lateral).normalized" in body:
        return fail(label + " helix vertical basis still normalizes the cross product")

    required = [
        "forward.Normalize();",
        "lateral.Normalize();",
        "Vector3 vertical = Vector3.Cross(forward, lateral);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail(label + " helix basis missing snippet -> " + snippet)

    return 0


def main() -> int:
    dragon_text = DRAGON_PROJECTILE.read_text(encoding="utf-8-sig")
    dragon_body = extract_method_body(dragon_text, "private void ApplyHelixOffset()")
    dragon_result = check_helix_body("Dragon King projectile", dragon_body)
    if dragon_result != 0:
        return dragon_result

    zombie_text = ZOMBIE_EFFECTS.read_text(encoding="utf-8-sig")
    zombie_body = extract_method_body(zombie_text, "private void LateUpdate()")
    zombie_result = check_helix_body("Zombie reward projectile", zombie_body)
    if zombie_result != 0:
        return zombie_result

    print("ProjectileHelixBasisNoExtraNormalizeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
