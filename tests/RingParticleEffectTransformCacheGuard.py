"""Guard: shared ring particle effects should cache their own Transform in frame loops."""

from pathlib import Path
import sys


SOURCE = Path("Common/Effects/RingParticleEffect.cs")


def fail(message: str) -> int:
    print("RingParticleEffectTransformCacheGuard: FAIL - " + message)
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

    if "private Transform cachedTransform;" not in text:
        return fail("missing cached Transform field")

    awake = extract_method_body(text, "private void Awake()")
    if awake is None or "cachedTransform = transform;" not in awake:
        return fail("Awake should seed cachedTransform")

    update = extract_method_body(text, "private void Update()")
    if update is None:
        return fail("missing Update")

    required_update_tokens = [
        "Transform effectTransform = cachedTransform;",
        "effectTransform = transform;",
        "cachedTransform = effectTransform;",
        "effectTransform.position = followTarget.position + FollowOffset;",
    ]
    for token in required_update_tokens:
        if token not in update:
            return fail("Update missing cached-transform token -> " + token)

    if "transform.position" in update:
        return fail("Update should not use direct transform.position")

    create_emitter = extract_method_body(text, "private ParticleSystem CreateEmitter(")
    if create_emitter is None:
        return fail("missing CreateEmitter")
    if "Transform effectTransform = cachedTransform != null ? cachedTransform : transform;" not in create_emitter:
        return fail("CreateEmitter should reuse cached Transform as parent")
    if "emitterObj.transform.SetParent(effectTransform);" not in create_emitter:
        return fail("CreateEmitter should parent to cached Transform")

    print("RingParticleEffectTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
