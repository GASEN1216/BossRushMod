"""Guard: NPC bubble billboarding should cache camera and bubble transforms."""

from pathlib import Path
import sys


SOURCE = Path("Common/Utils/NPCBubbleAnimator.cs")


def fail(message: str) -> int:
    print("NPCBubbleAnimatorCameraLookupGuard: FAIL - " + message)
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
    awake = extract_method_body(text, "private void Awake()")
    body = extract_method_body(text, "private void FaceCamera()")
    helper = extract_method_body(text, "private static Camera GetBillboardCamera()")
    position = extract_method_body(text, "private void UpdatePosition()")
    cached_transform = extract_method_body(text, "private Transform GetCachedTransform()")

    if awake is None:
        return fail("missing Awake body")
    if body is None:
        return fail("missing FaceCamera body")
    if helper is None:
        return fail("missing shared billboard camera helper")
    if position is None:
        return fail("missing UpdatePosition body")
    if cached_transform is None:
        return fail("missing cached transform helper")

    if "Camera.main" in body:
        return fail("FaceCamera should not read Camera.main directly")
    if "mainCamera.transform" in body:
        return fail("FaceCamera should not read Camera.transform directly per bubble")

    if text.count("Camera.main") != 1:
        return fail("Camera.main should appear exactly once in the shared helper")

    required = [
        "Camera mainCamera = GetBillboardCamera();",
        "Transform cameraTransform = cachedBillboardCameraTransform;",
        "if (cameraTransform != null)",
        "GetCachedTransform().rotation = cameraTransform.rotation;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing shared-camera FaceCamera snippet -> " + snippet)

    required_helper = [
        "private static Camera cachedBillboardCamera;",
        "private static Transform cachedBillboardCameraTransform;",
        "private static int cachedBillboardCameraFrame = -1;",
        "if (cachedBillboardCameraFrame != Time.frameCount)",
        "cachedBillboardCamera = Camera.main;",
        "cachedBillboardCameraTransform = cachedBillboardCamera != null ? cachedBillboardCamera.transform : null;",
        "cachedBillboardCameraFrame = Time.frameCount;",
        "return cachedBillboardCamera;",
    ]
    for snippet in required_helper:
        if snippet not in text:
            return fail("missing per-frame camera cache snippet -> " + snippet)

    required_transform = [
        "private Transform cachedTransform;",
        "cachedTransform = transform;",
        "private Transform GetCachedTransform()",
        "if (cachedTransform == null)",
        "cachedTransform = transform;",
        "return cachedTransform;",
    ]
    for snippet in required_transform:
        if snippet not in text:
            return fail("missing bubble Transform cache snippet -> " + snippet)

    if "transform.position" in position or "transform.rotation" in body:
        return fail("frame updates should use cached bubble Transform")

    if "GetCachedTransform().position = targetTransform.position + Vector3.up * heightOffset;" not in position:
        return fail("UpdatePosition should write through cached bubble Transform")

    print("NPCBubbleAnimatorCameraLookupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
