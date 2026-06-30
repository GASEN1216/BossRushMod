"""Guard: SunBeamDamageTrigger should sample Time.time once per collision check."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingAbilityHelpers.cs")


def fail(message: str) -> int:
    print("DragonKingSunBeamTimeSampleGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
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
                return text[start : idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    block = extract_block(text, "private void ProcessCollision(Collider other)")
    if block is None:
        return fail("missing ProcessCollision")

    required = [
        "float currentTime = Time.time;",
        "if (currentTime - lastDamageTime < DAMAGE_INTERVAL) return;",
        "lastDamageTime = currentTime;",
    ]
    for token in required:
        if token not in block:
            return fail("ProcessCollision missing single-time-sample token -> " + token)

    if block.count("Time.time") != 1:
        return fail("ProcessCollision should read Time.time exactly once")

    if "other.GetComponentInParent<CharacterMainControl>()" not in block:
        return fail("ProcessCollision should keep CharacterMainControl fallback")

    print("DragonKingSunBeamTimeSampleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
