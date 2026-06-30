"""Guard: zombie runtime skill dispatch should reuse marker.Owner before GetComponent."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")


def fail(message: str) -> int:
    print("ZombieModeRuntimeSkillOwnerReuseGuard: FAIL - " + message)
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
    body = extract_method_body(text, "internal void TryExecuteZombieModeEnemyRuntimeSkill(")
    if body is None:
        return fail("missing TryExecuteZombieModeEnemyRuntimeSkill body")

    required = [
        "CharacterMainControl character = marker.Owner;",
        "if (character == null)",
        "character = marker.GetComponent<CharacterMainControl>();",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing owner-first runtime skill snippet -> " + snippet)

    direct_lookup = "CharacterMainControl character = marker.GetComponent<CharacterMainControl>();"
    if direct_lookup in body:
        return fail("runtime skill still starts with direct GetComponent lookup")

    print("ZombieModeRuntimeSkillOwnerReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
