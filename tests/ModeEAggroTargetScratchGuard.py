"""Guard: Mode E aggro item target collection should reuse a scratch list."""

from pathlib import Path
import sys


SOURCE = Path("ModeE/ModeERespawnItems.cs")


def fail(message: str) -> int:
    print("ModeEAggroTargetScratchGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8")

    range_body = extract_method_body(text, "private List<CharacterMainControl> GetEnemyBossesInRange(")
    all_body = extract_method_body(text, "private List<CharacterMainControl> GetAllEnemyBosses()")
    if range_body is None or all_body is None:
        return fail("missing Mode E aggro target collection methods")

    for name, body in (("range", range_body), ("all", all_body)):
        if "new List<CharacterMainControl>(modeEAliveEnemies.Count)" in body:
            return fail(name + " target collection still allocates a list")
        if "modeEAggroTargetScratch.Clear();" not in body:
            return fail(name + " target collection does not clear shared scratch")
        if "return modeEAggroTargetScratch;" not in body:
            return fail(name + " target collection does not return shared scratch")

    if "private readonly List<CharacterMainControl> modeEAggroTargetScratch" not in text:
        return fail("missing shared Mode E aggro target scratch field")

    print("ModeEAggroTargetScratchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
