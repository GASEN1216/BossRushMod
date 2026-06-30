"""Guard: Mode F fortification frame updates should share a cached camera helper."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFFortifications.cs")


def fail(message: str) -> int:
    print("ModeFFortificationCameraCacheGuard: FAIL - " + message)
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
    placement = extract_method_body(text, "internal void UpdateFortPlacementMode()")
    repair = extract_method_body(text, "internal void UpdateModeFRepairSelection()")
    helper = extract_method_body(text, "private static Camera GetModeFInteractionCamera()")
    if placement is None:
        return fail("missing UpdateFortPlacementMode body")
    if repair is None:
        return fail("missing UpdateModeFRepairSelection body")
    if helper is None:
        return fail("missing GetModeFInteractionCamera helper")

    if "Camera.main" in placement or "Camera.main" in repair:
        return fail("frame update paths still call Camera.main directly")

    if "Camera cam = GetModeFInteractionCamera();" not in placement:
        return fail("placement update does not use shared camera helper")
    if "Camera cam = GetModeFInteractionCamera();" not in repair:
        return fail("repair selection update does not use shared camera helper")

    required = [
        "private static Camera modeFInteractionCachedMainCamera;",
        "private static int modeFInteractionCachedMainCameraFrame = -1;",
        "if (GameCamera.Instance != null && GameCamera.Instance.renderCamera != null)",
        "if (modeFInteractionCachedMainCameraFrame != Time.frameCount || modeFInteractionCachedMainCamera == null)",
        "modeFInteractionCachedMainCamera = Camera.main;",
        "modeFInteractionCachedMainCameraFrame = Time.frameCount;",
        "return modeFInteractionCachedMainCamera;",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing interaction camera cache snippet -> " + snippet)

    print("ModeFFortificationCameraCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
