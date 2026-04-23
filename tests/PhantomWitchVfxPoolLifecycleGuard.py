"""
Guard: pooled Phantom Witch VFX must track only active roots and must rebuild
clean roots instead of reusing partially destroyed hierarchies.

Reason:
- low-end detail selection depends on active FX root count
- pooled disabled roots must not stay counted as active
- pooled roots must be cleaned before storage and rebuilt on reuse
- cache cleanup must also destroy pooled instances, not only shared materials
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs")
ASSET = Path("Integration/PhantomWitch/PhantomWitchAssetManager.cs")


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
    redesign_text = SOURCE.read_text(encoding="utf-8")
    asset_text = ASSET.read_text(encoding="utf-8")

    get_or_build = extract_block(redesign_text, "private static GameObject GetOrBuildVfx(string key, Vector3 position, float duration, System.Action<GameObject> builder)")
    if not get_or_build:
        return fail("PhantomWitchVfxPoolLifecycleGuard: missing GetOrBuildVfx block")

    required_helpers = [
        "TryAcquireCleanPooledRoot",
        "IsReusablePooledRoot",
        "CleanupRootForPooling",
    ]
    for helper in required_helpers:
        if helper not in redesign_text:
            return fail(f"PhantomWitchVfxPoolLifecycleGuard: missing helper {helper}")

    if "TryAcquireCleanPooledRoot" not in get_or_build:
        return fail("PhantomWitchVfxPoolLifecycleGuard: GetOrBuildVfx must acquire a clean pooled root")

    if "builder(root);" not in get_or_build:
        return fail("PhantomWitchVfxPoolLifecycleGuard: GetOrBuildVfx must rebuild the root before scheduling recycle")

    recycler_update = extract_block(redesign_text, "private void Update()")
    if not recycler_update or "CleanupRootForPooling(gameObject);" not in recycler_update:
        return fail("PhantomWitchVfxPoolLifecycleGuard: recycler must clean the root before pooling it")

    clear_cache = extract_block(redesign_text, "internal static void ClearCache()")
    if not clear_cache:
        return fail("PhantomWitchVfxPoolLifecycleGuard: missing ClearCache block")

    if "VfxPools.Clear();" not in clear_cache:
        return fail("PhantomWitchVfxPoolLifecycleGuard: ClearCache must clear VfxPools")

    if "Destroy(pooledRoot);" not in clear_cache and "Object.Destroy(pooledRoot);" not in clear_cache:
        return fail("PhantomWitchVfxPoolLifecycleGuard: ClearCache must destroy pooled root instances")

    on_enable = extract_block(asset_text, "private void OnEnable()")
    on_disable = extract_block(asset_text, "private void OnDisable()")
    if not on_enable or not on_disable:
        return fail("PhantomWitchVfxPoolLifecycleGuard: FxRootTracker must implement OnEnable and OnDisable")

    if "PhantomWitchFxRuntime.AdjustActiveRootCount(1);" not in on_enable:
        return fail("PhantomWitchVfxPoolLifecycleGuard: OnEnable must increment active FX root count")

    if "PhantomWitchFxRuntime.AdjustActiveRootCount(-1);" not in on_disable:
        return fail("PhantomWitchVfxPoolLifecycleGuard: OnDisable must decrement active FX root count")

    print("PhantomWitchVfxPoolLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
