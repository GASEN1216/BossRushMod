"""Guard: FenHuang leap preview should cache Transform handles in preview hot paths."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/FenHuangHalberdAbilityManager.cs")


def fail(message: str) -> int:
    print("FenHuangLeapPreviewTransformCacheGuard: FAIL - " + message)
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


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    preview_start = text.find("internal class FenHuangLeapPreview : MonoBehaviour")
    if preview_start < 0:
        return fail("missing FenHuangLeapPreview class")

    preview = text[preview_start:]
    for token in [
        "private Transform cachedTransform;",
        "private Transform banIconTransform;",
        "private Transform markerLightTransform;",
        "private static Transform cachedMainCameraTransform;",
        "private Transform CachedTransform",
        "cachedTransform = transform;",
        "return cachedTransform;",
    ]:
        if token not in preview:
            return fail("missing preview Transform cache token -> " + token)

    camera_helper = extract_block(preview, "private static Camera GetCurrentCamera()")
    if camera_helper is None:
        return fail("missing GetCurrentCamera")
    if "cachedMainCameraTransform = cachedMainCamera != null ? cachedMainCamera.transform : null;" not in camera_helper:
        return fail("GetCurrentCamera should refresh cached camera Transform")

    transform_helper = extract_block(preview, "private static Transform GetCurrentCameraTransform()")
    if transform_helper is None:
        return fail("missing GetCurrentCameraTransform")
    if "return cachedMainCameraTransform;" not in transform_helper:
        return fail("GetCurrentCameraTransform should return cached camera Transform")

    update = extract_block(preview, "private void Update()")
    update_ban = extract_block(preview, "private void UpdateBanIcon(Vector3 landingPoint, bool valid)")
    init_ban = extract_block(preview, "private void InitBanIcon()")
    if update is None or update_ban is None or init_ban is None:
        return fail("missing preview update/init blocks")

    for block, label in [(update, "Update"), (update_ban, "UpdateBanIcon")]:
        for forbidden in ["banIconObject.transform", "camera.transform", "cam.transform"]:
            if forbidden in block:
                return fail(label + " still reads direct Transform -> " + forbidden)
        if "GetCurrentCameraTransform()" not in block:
            return fail(label + " should use cached camera Transform")
        if "banIconTransform" not in block:
            return fail(label + " should use cached ban icon Transform")

    for snippet in [
        "banIconTransform = banIconObject.transform;",
        "banIconTransform.SetParent(CachedTransform, false);",
    ]:
        if snippet not in init_ban:
            return fail("InitBanIcon missing cached Transform snippet -> " + snippet)

    if "markerLightTransform.position = landingPoint + Vector3.up * 0.35f;" not in preview:
        return fail("marker light should reuse cached Transform for preview position")

    print("FenHuangLeapPreviewTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
