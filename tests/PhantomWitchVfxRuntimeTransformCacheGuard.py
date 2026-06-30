"""Guard: Phantom Witch lightweight VFX runtime components should cache Transform handles."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign_RuntimeComponents.cs")
RUNTIME = Path("Integration/PhantomWitch/PhantomWitchAssetManager_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("PhantomWitchVfxRuntimeTransformCacheGuard: FAIL - " + message)
    return 1


def extract_class(text: str, class_name: str) -> str | None:
    start = text.find("class " + class_name)
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
                return text[start : idx + 1]

    return None


def require_cached_transform(text: str, class_name: str) -> str | None:
    block = extract_class(text, class_name)
    if block is None:
        return f"missing class {class_name}"

    if " : PhantomWitchRuntimeComponentBase" not in block:
        return f"{class_name} should inherit PhantomWitchRuntimeComponentBase"
    if "CachedTransform" not in block:
        return f"{class_name} should use CachedTransform"

    if "transform." in block or "transform," in block:
        return f"{class_name} still uses direct transform in hot/runtime paths"

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    runtime_text = RUNTIME.read_text(encoding="utf-8-sig")

    runtime = extract_class(runtime_text, "PhantomWitchFxRuntime")
    if runtime is None:
        return fail("missing PhantomWitchFxRuntime")
    for token in [
        "private static Transform cachedMainCameraTransform",
        "internal static Transform CurrentCameraTransform",
        "cachedMainCameraTransform = cachedMainCamera != null ? cachedMainCamera.transform : null;",
        "return cachedMainCameraTransform;",
    ]:
        if token not in runtime:
            return fail("PhantomWitchFxRuntime missing camera Transform cache token -> " + token)

    base = extract_class(text, "PhantomWitchRuntimeComponentBase")
    if base is None:
        return fail("missing PhantomWitchRuntimeComponentBase")
    for token in [
        "private Transform cachedTransform;",
        "protected Transform CachedTransform",
        "cachedTransform = transform;",
        "return cachedTransform;",
    ]:
        if token not in base:
            return fail("base cached transform missing token -> " + token)

    for class_name in [
        "PhantomWitchBillboard",
        "PhantomWitchWarpQuad",
        "PhantomWitchTendrilMover",
        "PhantomWitchVerticalLineDrift",
        "PhantomWitchPulseScale",
        "PhantomWitchRealmRuneFlashSpawner",
    ]:
        error = require_cached_transform(text, class_name)
        if error is not None:
            return fail(error)

    print("PhantomWitchVfxRuntimeTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
