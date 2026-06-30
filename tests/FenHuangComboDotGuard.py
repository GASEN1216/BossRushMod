"""Guard: FenHuang combo melee filtering should avoid Vector3.Angle per hit."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/FenHuangComboPatchesAndFx.cs")


def fail(message: str) -> int:
    print("FenHuangComboDotGuard: FAIL - " + message)
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
    body = extract_method_body(
        text,
        "private static void DealHalberdDamage(ItemAgent_MeleeWeapon melee, Item item, CharacterMainControl holder, int step)",
    )
    if body is None:
        return fail("missing DealHalberdDamage body")

    if "Vector3.Angle(" in body:
        return fail("DealHalberdDamage still uses Vector3.Angle per hit")

    required = [
        "float frontDot = Vector3.Dot(hitDirection, aimDirection);",
        "if (frontDot <= 0f &&",
        "hitDistance >= 0.5f + damageReceiverRadius)",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing dot hit-filter snippet -> " + snippet)

    print("FenHuangComboDotGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
