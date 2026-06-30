"""Guard: rewarded projectile runtime should cache Transform access in LateUpdate."""

from pathlib import Path
import sys


EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print("ZombieModeProjectileRuntimeTransformCacheGuard: FAIL - " + message)
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
    text = EFFECTS.read_text(encoding="utf-8-sig")
    runtime = extract_block(text, "public sealed class ZombieModePlayerProjectileRuntime")
    if runtime is None:
        return fail("missing ZombieModePlayerProjectileRuntime")

    if "private Transform cachedTransform;" not in runtime:
        return fail("missing cached Transform field")

    awake = extract_method_body(runtime, "private void Awake()")
    if awake is None or "cachedTransform = transform;" not in awake:
        return fail("Awake should seed the cached Transform")

    late_update = extract_method_body(runtime, "private void LateUpdate()")
    if late_update is None:
        return fail("missing LateUpdate")

    required = [
        "Transform projectileTransform = cachedTransform;",
        "projectileTransform = transform;",
        "cachedTransform = projectileTransform;",
        "Vector3 forward = projectileTransform.forward;",
        "projectileTransform.position += offset - lastHelixOffset;",
        "inst.DealZombieModeExplosionAreaDamage(runId, player, projectileTransform.position, trailRadius, trailDamage, false);",
    ]
    for token in required:
        if token not in late_update:
            return fail("LateUpdate missing cached transform token -> " + token)

    if "transform.forward" in late_update or "transform.position" in late_update:
        return fail("LateUpdate should not use direct transform access")

    print("ZombieModeProjectileRuntimeTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
