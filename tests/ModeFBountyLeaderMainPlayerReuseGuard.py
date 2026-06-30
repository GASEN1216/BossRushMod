"""Guard: Mode F bounty leader selection should read main player once per check."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFBounty_EquipmentAndLoot.cs")


def fail(message: str) -> int:
    print("ModeFBountyLeaderMainPlayerReuseGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private void CheckAndBroadcastLeaderChange(")
    if body is None:
        return fail("missing CheckAndBroadcastLeaderChange body")

    if body.count("CharacterMainControl.Main") != 1:
        return fail("CheckAndBroadcastLeaderChange should read CharacterMainControl.Main exactly once")

    required = [
        "CharacterMainControl mainPlayer = CharacterMainControl.Main;",
        "if (preferredLeader == mainPlayer && modeFState.PlayerBountyMarks == maxMarks && maxMarks > 0)",
        "else if (preferredLeader != null && preferredLeader != mainPlayer)",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing main-player reuse snippet -> " + snippet)

    print("ModeFBountyLeaderMainPlayerReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
