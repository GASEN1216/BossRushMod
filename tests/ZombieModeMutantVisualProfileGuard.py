"""Guard deterministic, one-shot ZombieMode mutant visual profiles."""

from pathlib import Path
import sys


POLLUTION = Path("ZombieMode/ZombieModePollution.cs")
RUNTIME = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")


def fail(message: str) -> int:
    print("ZombieModeMutantVisualProfileGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for idx in range(brace, len(text)):
        if text[idx] == "{":
            depth += 1
        elif text[idx] == "}":
            depth -= 1
            if depth == 0:
                return text[start : idx + 1]
    return ""


def main() -> int:
    pollution = POLLUTION.read_text(encoding="utf-8")
    runtime = RUNTIME.read_text(encoding="utf-8")
    tuning = TUNING.read_text(encoding="utf-8")
    combined = pollution + "\n" + runtime + "\n" + tuning

    required = [
        "marker.IsBoss",
        "marker.VisualIdentityApplied",
        "marker.VisualScaleRecords",
        "RestoreZombieModeVisualScale(marker)",
        "HashSet<Transform> safeVisualRoots",
        "HasZombieModeSafeVisualAncestor(root, safeVisualRoots)",
        "HasZombieModeUnsafeVisualAncestor(enemy.transform, visualRoot)",
        "current.GetComponent<Item>() != null",
        "OriginalScale = originalScale",
        "root.localScale = originalScale * visualScale",
        "ZombieModeMaxVisualScale = VisualIdentity.MaxVisualScale",
        "public const float MaxVisualScale = 3.00f;",
        "TryApplyZombieModeCustomFaceIdentity",
        "TryApplyZombieModeCustomFaceIdentityCore",
        "RestoreZombieModeCustomFaceNoThrow",
        "LogZombieModeVisualUnsupportedOnce",
        "zombieModeVisualUnsupportedLoggedRunId == marker.RunId",
        "IsZombieModeCustomFaceRefreshSafe",
        "face.mainRenderers == null",
        "face.headJoint == null || face.foreheadJoint == null",
        "face.hairPart == null",
        "face.eyePart == null",
        "face.eyebrowPart == null",
        "face.mouthPart == null",
        "face.tailPart == null",
        "face.footLPart == null",
        "face.footRPart == null",
        "face.wingLPart == null",
        "face.wingRPart == null",
        "face.eyePart.PartInstance == null",
        "part.customColorRenderers == null",
        "part.customColorRenderers.Count",
        "face.mainRenderers[i] == null",
        "skinned.rootBone == null",
        "Transform[] bones = skinned.bones",
        'nodeName.Contains("socket")',
        'nodeName.Contains("attack")',
        "Mathf.Clamp(headDelta, -0.12f, 0.12f)",
        "Mathf.Clamp(scaleFactor, 0.75f, 1.35f)",
        "Mathf.Clamp(distanceAngleDelta, -12f, 12f)",
        "Color.Lerp(originalHead.mainColor, targetColor, 0.65f)",
        "face.RefreshAll();",
        "ZombieModeFootMarkerPool.Acquire",
        "private static readonly System.Collections.Generic.Stack<GameObject> Pool",
        "meshRenderer.sharedMaterial = material",
        "ReleaseZombieModeFootMarker(marker)",
        "GetZombieModeEliteVisualColor(marker.EliteAffixes)",
    ]
    for token in required:
        if token not in combined:
            return fail("missing visual invariant -> " + token)

    forbidden = [
        "characterModel.transform.localScale",
        "LoadFromData(",
        "SwitchPart(",
        "UnityEngine.Random",
        "new Material",
        "ParticleSystem",
        "renderer.transform.localScale = renderer.transform.localScale * visualScale",
    ]
    visual_start = pollution.index("private void ApplyZombieModeMutationVisualIdentity")
    visual_end = pollution.index("private void ApplyZombieModeSpecialKindTuning", visual_start)
    visual_body = pollution[visual_start:visual_end]
    for token in forbidden:
        if token in visual_body:
            return fail("forbidden visual implementation -> " + token)

    for kind in ["Sprinter", "Exploder", "OfficialExploder", "Plague", "Summoner", "Harasser"]:
        if "ZombieModeSpecialKind." + kind not in visual_body:
            return fail("missing special visual profile -> " + kind)

    elite_color = extract_method(pollution, "private static Color GetZombieModeEliteVisualColor(")
    for affix in [
        "Commander", "Splitting", "Plague", "ToxicAura", "Regenerating",
        "Shielded", "Adaptive", "Burst", "Swift", "Frenzied",
    ]:
        if "ZombieModeEliteAffix." + affix not in elite_color:
            return fail("missing elite ability color mapping -> " + affix)

    unsafe_ancestor = extract_method(pollution, "private static bool HasZombieModeUnsafeVisualAncestor(")
    for token in [
        "current.GetComponent<Item>() != null",
        'nodeName.Contains("socket")',
        'nodeName.Contains("weapon")',
        'nodeName.Contains("handheld")',
        'nodeName.Contains("attack")',
    ]:
        if token not in unsafe_ancestor:
            return fail("unsafe visual ancestor guard is incomplete -> " + token)

    restore = extract_method(pollution, "private static bool RestoreZombieModeCustomFaceNoThrow(")
    for token in [
        "try { face.headSetting = head; } catch { restored = false; }",
        "face.eyePart.partInfo = eye",
        "face.eyebrowPart.partInfo = eyebrow",
        "face.mouthPart.partInfo = mouth",
        "try { face.RefreshAll(); } catch { restored = false; }",
    ]:
        if token not in restore:
            return fail("CustomFace rollback must isolate every restore step -> " + token)

    register = extract_method(runtime, "private ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(")
    restore_scale_idx = register.find("RestoreZombieModeVisualScale(marker)")
    release_marker_idx = register.find("ReleaseZombieModeFootMarker(marker)")
    clear_marker_idx = register.find("marker.VisualFootMarkerFallbackApplied = false")
    if min(restore_scale_idx, release_marker_idx, clear_marker_idx) < 0 or not (
            restore_scale_idx < release_marker_idx < clear_marker_idx):
        return fail("marker re-registration must release the old pooled foot marker before clearing state")

    fallback = extract_method(pollution, "private void EnsureZombieModeFootMarkerFallback(")
    acquire_idx = fallback.find("ZombieModeFootMarkerPool.Acquire")
    applied_idx = fallback.find("marker.VisualFootMarkerFallbackApplied = true")
    if acquire_idx < 0 or applied_idx < 0 or acquire_idx > applied_idx:
        return fail("foot marker may be marked applied only after a successful pool acquire")
    if "ZombieModeFootMarkerPool.Release(footMarker)" not in fallback:
        return fail("failed foot marker setup must return the acquired object to the pool")

    print("ZombieModeMutantVisualProfileGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
