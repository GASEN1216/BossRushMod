"""Guard: projectile reward cleanup should only probe tracked runtime projectiles."""

from pathlib import Path
import sys


RUNTIME = Path("ZombieMode/ZombieModeRewardRuntimeModifiers.cs")
EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print("ZombieModeProjectileRuntimeTrackingGuard: FAIL - " + message)
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
                return text[brace_start:idx + 1]

    return None


def extract_class_body(text: str, class_name: str) -> str | None:
    start = text.find(f"class {class_name}")
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
                return text[brace_start:idx + 1]

    return None


def main() -> int:
    runtime = RUNTIME.read_text(encoding="utf-8-sig")
    effects = EFFECTS.read_text(encoding="utf-8-sig")
    remove_body = extract_method_body(runtime, "private void RemoveZombieModePlayerProjectileRuntime(")
    apply_body = extract_method_body(runtime, "public void ApplyZombieModeProjectileRewardEffects(")
    projectile_runtime = extract_class_body(effects, "ZombieModePlayerProjectileRuntime")

    if remove_body is None:
        return fail("missing RemoveZombieModePlayerProjectileRuntime body")
    if apply_body is None:
        return fail("missing ApplyZombieModeProjectileRewardEffects body")
    if projectile_runtime is None:
        return fail("missing ZombieModePlayerProjectileRuntime body")

    required_runtime_tokens = [
        "private readonly System.Collections.Generic.HashSet<int> zombieModePlayerProjectileRuntimeIds",
        "int projectileId = projectile.GetInstanceID();",
        "RegisterZombieModePlayerProjectileRuntime(projectileId);",
        "runtime.Initialize(",
        "projectileId);",
    ]
    for token in required_runtime_tokens:
        if token not in runtime:
            return fail("missing runtime tracking token -> " + token)

    contains_index = remove_body.find("!zombieModePlayerProjectileRuntimeIds.Contains(projectileId)")
    get_component_index = remove_body.find("projectile.GetComponent<ZombieModePlayerProjectileRuntime>()")
    if contains_index < 0:
        return fail("remove path does not early-return for untracked projectile ids")
    if get_component_index < 0:
        return fail("remove path no longer removes tracked runtime components")
    if contains_index > get_component_index:
        return fail("remove path checks GetComponent before tracked-id early return")

    required_effect_tokens = [
        "private int projectileInstanceId;",
        "private static CharacterMainControl cachedTrailPlayer;",
        "private static int cachedTrailPlayerFrame = -1;",
        "int projectileId)",
        "projectileInstanceId = projectileId;",
        "CharacterMainControl player = GetCachedTrailPlayer();",
        "private static CharacterMainControl GetCachedTrailPlayer()",
        "if (cachedTrailPlayerFrame != Time.frameCount)",
        "cachedTrailPlayer = CharacterMainControl.Main;",
        "cachedTrailPlayerFrame = Time.frameCount;",
        "return cachedTrailPlayer;",
        "private void OnDestroy()",
        "UnregisterTrackedProjectile();",
        "inst.UnregisterZombieModePlayerProjectileRuntime(projectileInstanceId);",
    ]
    for token in required_effect_tokens:
        if token not in projectile_runtime:
            return fail("missing projectile runtime deregistration token -> " + token)

    late_update = extract_method_body(projectile_runtime, "private void LateUpdate()")
    helper = extract_method_body(projectile_runtime, "private static CharacterMainControl GetCachedTrailPlayer()")
    if late_update is None:
        return fail("missing ZombieModePlayerProjectileRuntime LateUpdate body")
    if helper is None:
        return fail("missing shared trail player helper")
    if "CharacterMainControl.Main" in late_update:
        return fail("LateUpdate should not read CharacterMainControl.Main directly per active projectile")
    if projectile_runtime.count("CharacterMainControl.Main") != 1:
        return fail("CharacterMainControl.Main should appear exactly once in the shared trail helper")

    print("ZombieModeProjectileRuntimeTrackingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
