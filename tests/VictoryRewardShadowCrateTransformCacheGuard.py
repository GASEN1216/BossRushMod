"""Guard: victory reward shadow crate should cache active ghost/player transforms."""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/VictoryRewardShadowCrateController.cs")


def fail(message: str) -> int:
    print("VictoryRewardShadowCrateTransformCacheGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")

    for field in ("private Transform playerTransform;", "private Transform ghostTransform;"):
        if field not in text:
            return fail("missing cached field -> " + field)

    initialize = extract_method_body(text, "public bool Initialize(")
    complete = extract_method_body(text, "public void CompleteAndLand()")
    follow = extract_method_body(text, "private void UpdateFollowPose(")
    materialize = extract_method_body(text, "private void UpdateMaterialize(")
    descending = extract_method_body(text, "private void UpdateDescending(")
    resolve = extract_method_body(text, "private Vector3 ResolveAnchorPosition()")
    aura = extract_method_body(text, "private void CreateGhostAuraLight()")
    visual = extract_method_body(text, "private void ApplyGhostVisual(")
    cleanup = extract_method_body(text, "private void CleanupAndDestroy(")

    required_methods = {
        "Initialize": initialize,
        "CompleteAndLand": complete,
        "UpdateFollowPose": follow,
        "UpdateMaterialize": materialize,
        "UpdateDescending": descending,
        "ResolveAnchorPosition": resolve,
        "CreateGhostAuraLight": aura,
        "ApplyGhostVisual": visual,
        "CleanupAndDestroy": cleanup,
    }
    for name, body in required_methods.items():
        if body is None:
            return fail("missing method body -> " + name)

    required_initialize = [
        "playerTransform = player != null ? player.transform : null;",
        "ghostTransform = ghostObject.transform;",
        "ghostBaseScale = ghostTransform.localScale;",
        "ModBehaviour.DevLog(\"[BossRush] 通关奖励箱虚影已创建: pos=\" + ghostTransform.position);",
    ]
    for snippet in required_initialize:
        if snippet not in initialize:
            return fail("Initialize missing cached-transform snippet -> " + snippet)

    required_active = {
        "CompleteAndLand": ["ghostTransform.position"],
        "UpdateFollowPose": ["ghostTransform.position =", "ghostTransform.Rotate("],
        "UpdateMaterialize": ["ghostTransform.Rotate("],
        "UpdateDescending": ["ghostTransform.position", "ghostTransform.Rotate("],
        "ResolveAnchorPosition": ["playerTransform.position", "ghostTransform.position"],
        "CreateGhostAuraLight": ["auraLightObject.transform.SetParent(ghostTransform, false);"],
        "ApplyGhostVisual": ["ghostTransform.localScale ="],
        "CleanupAndDestroy": ["ghostTransform = null;", "playerTransform = null;"],
    }
    bodies = {
        "CompleteAndLand": complete,
        "UpdateFollowPose": follow,
        "UpdateMaterialize": materialize,
        "UpdateDescending": descending,
        "ResolveAnchorPosition": resolve,
        "CreateGhostAuraLight": aura,
        "ApplyGhostVisual": visual,
        "CleanupAndDestroy": cleanup,
    }
    for name, snippets in required_active.items():
        body = bodies[name]
        for snippet in snippets:
            if snippet not in body:
                return fail(f"{name} missing cached-transform snippet -> {snippet}")

    hot_methods = "\n".join([complete, follow, materialize, descending, resolve, aura, visual])
    if "ghostObject.transform" in hot_methods:
        return fail("active shadow crate methods should not use ghostObject.transform directly")
    if "player.transform.position" in resolve:
        return fail("ResolveAnchorPosition should use cached playerTransform position")

    print("VictoryRewardShadowCrateTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
