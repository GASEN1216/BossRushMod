"""
Guard: Phantom Witch scythe scene-setup path must restore its shared asset
reference after Phantom Witch static cache force cleanup.

Reason:
- OnSceneLoaded_Integration clears Phantom Witch static cache before the scythe
  scene-setup hook runs
- ForceCleanup resets PhantomWitchAssetManager reference count to zero, but the
  persistent scythe bootstrap instance can still believe it already holds a ref
- if scene setup does not re-establish that shared ref, boss death / player
  death can clear shared VFX materials while lingering scythe or boss effects
  are still rendering
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

    helper = extract_block(text, "private void EnsurePhantomWitchScytheSharedAssetReference()")
    if not helper:
        return fail(
            "PhantomWitchScytheSceneRefRestoreGuard: missing EnsurePhantomWitchScytheSharedAssetReference helper"
        )

    helper_required = [
        "PhantomWitchAssetManager.HasActiveReferences",
        "PhantomWitchAssetManager.AddReference();",
        "phantomWitchScytheSharedAssetReferenceHeld = true;",
    ]
    for token in helper_required:
        if token not in helper:
            return fail(
                "PhantomWitchScytheSceneRefRestoreGuard: helper missing " + token
            )

    init_block = extract_block(text, "private void InitializePhantomWitchScytheSystem()")
    if not init_block:
        return fail("PhantomWitchScytheSceneRefRestoreGuard: missing InitializePhantomWitchScytheSystem block")

    if "EnsurePhantomWitchScytheSharedAssetReference();" not in init_block:
        return fail(
            "PhantomWitchScytheSceneRefRestoreGuard: init must call EnsurePhantomWitchScytheSharedAssetReference()"
        )

    setup_block = extract_block(text, "private void SetupPhantomWitchScytheForScene(Scene scene)")
    if not setup_block:
        return fail("PhantomWitchScytheSceneRefRestoreGuard: missing SetupPhantomWitchScytheForScene block")

    if "EnsurePhantomWitchScytheSharedAssetReference();" not in setup_block:
        return fail(
            "PhantomWitchScytheSceneRefRestoreGuard: scene setup must call EnsurePhantomWitchScytheSharedAssetReference()"
        )

    print("PhantomWitchScytheSceneRefRestoreGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
