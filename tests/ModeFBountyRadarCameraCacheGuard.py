"""Guard: Mode F bounty radar should cache Camera.main fallback per frame."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs")
STATE = Path("ModeF/ModeFUI.cs")


def fail(message: str) -> int:
    print("ModeFBountyRadarCameraCacheGuard: FAIL - " + message)
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
    source_text = SOURCE.read_text(encoding="utf-8-sig")
    state_text = STATE.read_text(encoding="utf-8-sig")
    helper = extract_method_body(source_text, "private Camera GetModeFBountyRadarCamera()")
    update_ui = extract_method_body(source_text, "private void UpdateModeFBountyRadarUI()")
    if helper is None:
        return fail("missing GetModeFBountyRadarCamera helper")
    if update_ui is None:
        return fail("missing UpdateModeFBountyRadarUI body")

    if "Camera.main" in update_ui:
        return fail("radar refresh path should use camera helper, not Camera.main directly")

    required_state = [
        "private static Camera modeFBountyRadarCachedMainCamera = null;",
        "private static int modeFBountyRadarCachedMainCameraFrame = -1;",
    ]
    for snippet in required_state:
        if snippet not in state_text:
            return fail("missing radar camera cache field -> " + snippet)

    required_helper = [
        "if (GameCamera.Instance != null && GameCamera.Instance.renderCamera != null)",
        "if (modeFBountyRadarCachedMainCameraFrame != Time.frameCount || modeFBountyRadarCachedMainCamera == null)",
        "modeFBountyRadarCachedMainCamera = Camera.main;",
        "modeFBountyRadarCachedMainCameraFrame = Time.frameCount;",
        "return modeFBountyRadarCachedMainCamera;",
    ]
    for snippet in required_helper:
        if snippet not in helper:
            return fail("missing radar camera cache snippet -> " + snippet)

    print("ModeFBountyRadarCameraCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
