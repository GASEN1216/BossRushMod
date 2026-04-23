"""
Guard: the player-side Phantom Witch scythe system must hold its own shared
asset reference instead of relying on the boss lifecycle.

Reason:
- scythe swing / curse visuals reuse PhantomWitchAssetManager and
  PhantomWitchVfxRedesign shared materials
- boss death currently decrements the only asset reference, which can clear
  shared caches while the player is still holding and using 噬魂挽歌
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs")


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

    field = "private static bool phantomWitchScytheSharedAssetReferenceHeld = false;"
    if field not in text:
        return fail("PhantomWitchScytheSharedAssetReferenceGuard: missing shared asset reference state field")

    init_block = extract_block(text, "private void InitializePhantomWitchScytheSystem()")
    if not init_block:
        return fail("PhantomWitchScytheSharedAssetReferenceGuard: missing InitializePhantomWitchScytheSystem block")

    init_required = [
        "if (!phantomWitchScytheSharedAssetReferenceHeld)",
        "PhantomWitchAssetManager.AddReference();",
        "phantomWitchScytheSharedAssetReferenceHeld = true;",
    ]
    for token in init_required:
        if token not in init_block:
            return fail(f"PhantomWitchScytheSharedAssetReferenceGuard: init missing {token}")

    cleanup_block = extract_block(text, "private void CleanupPhantomWitchScytheSystem()")
    if not cleanup_block:
        return fail("PhantomWitchScytheSharedAssetReferenceGuard: missing CleanupPhantomWitchScytheSystem block")

    cleanup_required = [
        "if (phantomWitchScytheSharedAssetReferenceHeld)",
        "PhantomWitchAssetManager.ClearCache();",
        "phantomWitchScytheSharedAssetReferenceHeld = false;",
    ]
    for token in cleanup_required:
        if token not in cleanup_block:
            return fail(f"PhantomWitchScytheSharedAssetReferenceGuard: cleanup missing {token}")

    print("PhantomWitchScytheSharedAssetReferenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
