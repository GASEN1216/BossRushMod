"""Guard: Dragon King split activation should reuse branch-local velocity refs."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")


def fail(message: str) -> int:
    print("DragonKingSplitActivationVelocityReuseGuard: FAIL - " + message)
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
                return text[brace_start:idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "private void UpdateSplitActivation(float deltaTime)")
    if body is None:
        return fail("missing UpdateSplitActivation body")

    forbidden = [
        "directionRef(projectile) * (velocityRef(projectile).magnitude",
        "Mathf.Max(0.01f, profile.SplitInitialSpeedMult));",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("split activation still nests repeated refs in assignment -> " + snippet)

    required = [
        "Vector3 direction = directionRef(projectile);",
        "Vector3 velocity = velocityRef(projectile);",
        "float splitInitialSpeedMult = Mathf.Max(0.01f, profile.SplitInitialSpeedMult);",
        "velocityRef(projectile) = direction * (velocity.magnitude / splitInitialSpeedMult);",
        "velocityRef(projectile) = direction * (velocity.magnitude * splitInitialSpeedMult);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing branch-local split activation snippet -> " + snippet)

    print("DragonKingSplitActivationVelocityReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
