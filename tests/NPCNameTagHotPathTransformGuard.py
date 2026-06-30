"""Guard: support NPC name tag refresh should reuse sampled Transform data."""

from pathlib import Path
import sys


HELPER = Path("Integration/Utils/NPCNameTagHelper.cs")


def fail(message: str) -> int:
    print("NPCNameTagHotPathTransformGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
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
                return text[start : idx + 1]

    return None


def extract_method_body(text: str, signature: str) -> str | None:
    block = extract_block(text, signature)
    if block is None:
        return None

    brace_start = block.find("{")
    return block[brace_start:] if brace_start >= 0 else None


def main() -> int:
    text = HELPER.read_text(encoding="utf-8-sig")

    if "private static Transform _cachedCameraTransform;" not in text:
        return fail("missing cached camera Transform field")

    reset = extract_method_body(text, "public static void ResetStaticCaches()")
    refresh = extract_method_body(text, "private static void RefreshCachedCamera()")
    update = extract_method_body(text, "private static void UpdateOriginalHealthBarInstance(")
    should_show_block = extract_block(text, "private static bool ShouldShowOriginalHealthBarName(")
    should_show = extract_method_body(text, "private static bool ShouldShowOriginalHealthBarName(")
    rotate = extract_method_body(text, "public static void UpdateNameTagRotation(")

    if reset is None or "_cachedCameraTransform = null;" not in reset:
        return fail("ResetStaticCaches should clear cached camera Transform")

    if refresh is None:
        return fail("missing RefreshCachedCamera")
    if "_cachedCameraTransform = _cachedCamera != null ? _cachedCamera.transform : null;" not in refresh:
        return fail("RefreshCachedCamera should refresh cached camera Transform with Camera.main")

    if rotate is None or "nameTagObject.transform.rotation = _cachedCameraTransform.rotation;" not in rotate:
        return fail("legacy name tag rotation should reuse cached camera Transform")

    if update is None:
        return fail("missing UpdateOriginalHealthBarInstance")
    required_update_tokens = [
        "Vector3 targetPosition = entry.Target.position;",
        "Transform cameraTransform = _cachedCameraTransform;",
        "bool shouldShow = ShouldShowOriginalHealthBarName(entry.Target, targetPosition, cameraTransform);",
        "targetPosition + Vector3.up * GetAdjustedOriginalHealthBarNameHeight(entry.Height)",
    ]
    for token in required_update_tokens:
        if token not in update:
            return fail("UpdateOriginalHealthBarInstance missing token -> " + token)

    if "entry.Target.position + Vector3.up" in update:
        return fail("UpdateOriginalHealthBarInstance should reuse sampled targetPosition")
    if "_cachedCamera.transform" in update:
        return fail("UpdateOriginalHealthBarInstance should use cached camera Transform")

    if should_show_block is None or should_show is None:
        return fail("missing ShouldShowOriginalHealthBarName")
    if "Transform cameraTransform" not in should_show_block or "Vector3 targetPosition" not in should_show_block:
        return fail("ShouldShowOriginalHealthBarName should accept sampled target/camera data")
    if "target.position" in should_show or "_cachedCamera.transform" in should_show:
        return fail("ShouldShowOriginalHealthBarName should not resample transforms")

    print("NPCNameTagHotPathTransformGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
