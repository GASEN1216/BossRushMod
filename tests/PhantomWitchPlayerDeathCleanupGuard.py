"""
Guard: Phantom Witch player-death path must perform terminal runtime cleanup.

Reason:
- unlike DragonDescendant, Phantom Witch keeps a separate controller GO and shared FX refs
- if player death only marks Dead + Destroy(gameObject), the Dead-early-return OnDestroy path skips
  AI cleanup and shared reference release
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    on_player_death = extract_block(text, "public void OnPlayerDeath()")
    if not on_player_death:
        return fail("PhantomWitchPlayerDeathCleanupGuard: missing OnPlayerDeath block")

    required = [
        "CleanupControllerRuntimeState();",
        "ReleaseAssetReferenceIfNeeded();",
    ]
    for token in required:
        if token not in on_player_death:
            return fail(f"PhantomWitchPlayerDeathCleanupGuard: OnPlayerDeath missing {token}")

    helper = extract_block(text, "private void CleanupControllerRuntimeState()")
    if not helper:
        return fail("PhantomWitchPlayerDeathCleanupGuard: missing CleanupControllerRuntimeState helper")

    if "aiController.Cleanup();" not in helper:
        return fail("PhantomWitchPlayerDeathCleanupGuard: helper must cleanup aiController")

    print("PhantomWitchPlayerDeathCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
