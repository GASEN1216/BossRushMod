"""
Guard: FenHuang leap preview should not call Camera.main from multiple visual paths.

Reason:
- The invalid landing marker can orient the ban icon in UpdatePreview and again
  in Update during the same frame.
- Camera.main may search tagged cameras in Unity; repeated visual-path lookups
  are avoidable without changing preview behavior.

Requirement:
- FenHuangLeapPreview must use a same-frame GetCurrentCamera helper.
- Camera.main may appear only inside that helper.
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/DragonKing/Weapons/FenHuangHalberdAbilityManager.cs")


def fail(message: str) -> int:
    print("FenHuangLeapPreviewCameraCacheGuard: FAIL - " + message)
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
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    preview_start = text.find("internal class FenHuangLeapPreview : MonoBehaviour")
    if preview_start < 0:
        return fail("missing FenHuangLeapPreview class")
    preview_text = text[preview_start:]

    if preview_text.count("Camera.main") != 1:
        return fail("Camera.main should appear exactly once inside the preview camera helper")

    helper_body = extract_method_body(preview_text, "private static Camera GetCurrentCamera()")
    if helper_body is None:
        return fail("missing GetCurrentCamera helper")

    required = [
        "private static Camera cachedMainCamera;",
        "private static int cachedMainCameraFrame = -1;",
        "if (cachedMainCameraFrame != Time.frameCount)",
        "cachedMainCamera = Camera.main;",
        "cachedMainCameraFrame = Time.frameCount;",
        "return cachedMainCamera;",
    ]
    for snippet in required:
        if snippet not in preview_text:
            return fail("missing camera cache snippet -> " + snippet)

    update_body = extract_method_body(preview_text, "private void Update()")
    ban_body = extract_method_body(preview_text, "private void UpdateBanIcon(Vector3 landingPoint, bool valid)")
    if update_body is None:
        return fail("missing Update body")
    if ban_body is None:
        return fail("missing UpdateBanIcon body")

    if "Camera.main" in update_body or "Camera.main" in ban_body:
        return fail("visual paths still call Camera.main directly")

    if (
        re.search(r"Camera\s+cam\s*=\s*GetCurrentCamera\s*\(\s*\)\s*;", update_body) is None
        and "Transform cameraTransform = GetCurrentCameraTransform();" not in update_body
    ):
        return fail("Update does not use the cached current camera helper")

    if (
        re.search(r"Camera\s+cam\s*=\s*GetCurrentCamera\s*\(\s*\)\s*;", ban_body) is None
        and "Transform cameraTransform = GetCurrentCameraTransform();" not in ban_body
    ):
        return fail("UpdateBanIcon does not use the cached current camera helper")

    print("FenHuangLeapPreviewCameraCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
