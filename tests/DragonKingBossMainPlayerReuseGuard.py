"""Guard: DragonKing AI setup should reuse one main-player lookup."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingBoss.cs")


def fail(message: str) -> int:
    print("DragonKingBossMainPlayerReuseGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private void DisableDragonKingOriginalAI(")
    if body is None:
        return fail("missing DisableDragonKingOriginalAI body")

    if body.count("CharacterMainControl.Main") != 1:
        return fail("DisableDragonKingOriginalAI should read CharacterMainControl.Main exactly once")

    required = [
        "if (!IsModeEActive)",
        "CharacterMainControl mainPlayer = CharacterMainControl.Main;",
        "if (mainPlayer != null && mainPlayer.mainDamageReceiver != null)",
        "aiController.searchedEnemy = mainPlayer.mainDamageReceiver;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing main-player reuse snippet -> " + snippet)

    if "CharacterMainControl.Main.mainDamageReceiver" in body:
        return fail("DisableDragonKingOriginalAI still dereferences CharacterMainControl.Main.mainDamageReceiver")

    print("DragonKingBossMainPlayerReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
