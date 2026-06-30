"""Guard: Phantom Witch ambient presence should reuse cached transforms in per-frame VFX paths."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAmbientPresence.cs")


def fail(message: str) -> int:
    print("PhantomWitchAmbientTransformCacheGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str | None:
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

    for token in [
        "private Transform cachedTransform;",
        "private Transform CachedTransform",
        "cachedTransform = transform;",
        "return cachedTransform;",
    ]:
        if token not in text:
            return fail("missing self Transform cache token -> " + token)

    update_cold_halo = extract_method(text, "private void UpdateColdHalo()")
    if update_cold_halo is None:
        return fail("missing UpdateColdHalo")
    if "Transform cameraTransform = PhantomWitchFxRuntime.CurrentCameraTransform;" not in update_cold_halo:
        return fail("UpdateColdHalo should reuse cached camera Transform")
    if "GetCloseCameraHaloBonus(cameraTransform)" not in update_cold_halo:
        return fail("UpdateColdHalo should pass camera Transform to distance helper")
    if "camera.transform" in update_cold_halo:
        return fail("UpdateColdHalo still reads camera.transform")

    close_bonus = extract_method(text, "private float GetCloseCameraHaloBonus(Transform cameraTransform)")
    if close_bonus is None:
        return fail("GetCloseCameraHaloBonus should accept Transform")
    if "cameraTransform.position - CachedTransform.position" not in close_bonus:
        return fail("close camera bonus should use cached self Transform position")
    for forbidden in ["Camera camera", "camera.transform", "transform.position"]:
        if forbidden in close_bonus:
            return fail("close camera bonus still contains direct camera/self Transform access -> " + forbidden)

    update_heartbeat = extract_method(text, "private void UpdateHeartbeat()")
    if update_heartbeat is None:
        return fail("missing UpdateHeartbeat")
    if "Transform cameraTransform = PhantomWitchFxRuntime.CurrentCameraTransform;" not in update_heartbeat:
        return fail("UpdateHeartbeat should reuse cached camera Transform")
    if "camera.transform" in update_heartbeat:
        return fail("UpdateHeartbeat still reads camera.transform")

    print("PhantomWitchAmbientTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
