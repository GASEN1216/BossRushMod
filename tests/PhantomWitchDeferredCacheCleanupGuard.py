"""
Guard: Phantom Witch shared VFX cache cleanup must wait until active FX roots are gone.

Reason:
- player death can destroy shared quad materials while lingering FX roots still render this frame
- that produces the magenta/purple rectangle symptom seen after dying to Phantom Witch
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAssetManager.cs")
SOURCE_PARTS = [
    SOURCE,
    Path("Integration/PhantomWitch/PhantomWitchAssetManager_RuntimeComponents.cs"),
]


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
    text = "\n".join(path.read_text(encoding="utf-8") for path in SOURCE_PARTS)

    if "pendingCacheCleanup" not in text:
        return fail("PhantomWitchDeferredCacheCleanupGuard: missing pendingCacheCleanup state")

    clear_block = extract_block(text, "public static void ClearCache()")
    if not clear_block:
        return fail("PhantomWitchDeferredCacheCleanupGuard: missing ClearCache block")

    required_clear_tokens = [
        "PhantomWitchFxRuntime.HasActiveRoots",
        "pendingCacheCleanup = true;",
        "FinalizeCacheCleanup();",
    ]
    for token in required_clear_tokens:
        if token not in clear_block:
            return fail(f"PhantomWitchDeferredCacheCleanupGuard: ClearCache missing {token}")

    if "private static void FinalizeCacheCleanup()" not in text:
        return fail("PhantomWitchDeferredCacheCleanupGuard: missing FinalizeCacheCleanup helper")

    if "internal static bool HasActiveRoots => activeRootCount > 0;" not in text:
        return fail("PhantomWitchDeferredCacheCleanupGuard: missing HasActiveRoots property")

    adjust_block = extract_block(text, "internal static void AdjustActiveRootCount(int delta)")
    if not adjust_block:
        return fail("PhantomWitchDeferredCacheCleanupGuard: missing AdjustActiveRootCount block")

    if "TryFinalizePendingCacheCleanup();" not in adjust_block:
        return fail("PhantomWitchDeferredCacheCleanupGuard: active root count updates must try pending cleanup finalization")

    if "internal static void TryFinalizePendingCacheCleanup()" not in text:
        return fail("PhantomWitchDeferredCacheCleanupGuard: missing TryFinalizePendingCacheCleanup helper")

    print("PhantomWitchDeferredCacheCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
