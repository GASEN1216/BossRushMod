"""Guard: FenHuang leap preview should avoid Vector3.Distance in hot preview math."""

from pathlib import Path
import sys


RUNTIME = Path("Integration/DragonKing/Weapons/FenHuangHalberdRuntime.cs")
MANAGER = Path("Integration/DragonKing/Weapons/FenHuangHalberdAbilityManager.cs")


def fail(message: str) -> int:
    print("FenHuangPreviewDistanceGuard: FAIL - " + message)
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
    runtime_text = RUNTIME.read_text(encoding="utf-8-sig")
    manager_text = MANAGER.read_text(encoding="utf-8-sig")

    get_aim_point = extract_method_body(runtime_text, "public static Vector3 GetAimPoint(CharacterMainControl character)")
    if get_aim_point is None:
        return fail("missing GetAimPoint body")

    update_preview = extract_method_body(manager_text, "private void UpdatePreview(bool requireFreshValidation)")
    if update_preview is None:
        return fail("missing UpdatePreview body")

    if "Vector3.Distance(" in get_aim_point:
        return fail("GetAimPoint still uses Vector3.Distance")

    if "Vector3.Distance(" in update_preview:
        return fail("UpdatePreview still uses Vector3.Distance")

    required = [
        (get_aim_point, "Vector3 aimDistanceDelta = aimPoint - origin;"),
        (get_aim_point, "aimDistanceDelta.y = 0f;"),
        (get_aim_point, "float distance = Mathf.Sqrt(aimDistanceDelta.sqrMagnitude);"),
        (update_preview, "Vector3 horizontalDelta = resolvedLandingPoint - previewOrigin;"),
        (update_preview, "horizontalDelta.y = 0f;"),
        (update_preview, "bool hasEnoughHorizontalDistance = horizontalDelta.sqrMagnitude > 0.09f;"),
        (update_preview, "previewValid = !hitObstacle && hasEnoughHorizontalDistance && IsLandingPointValid(previewLandingPoint);"),
    ]
    for body, snippet in required:
        if snippet not in body:
            return fail("missing squared preview-distance snippet -> " + snippet)

    print("FenHuangPreviewDistanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
