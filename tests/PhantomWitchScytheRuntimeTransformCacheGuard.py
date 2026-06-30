"""Guard: Phantom Witch scythe runtime VFX should cache self Transform in frame loops."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheAction_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("PhantomWitchScytheRuntimeTransformCacheGuard: FAIL - " + message)
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


def require_cached_field(block: str, class_name: str) -> int | None:
    if "private Transform cachedTransform;" not in block:
        return fail(class_name + " missing cached Transform field")
    awake = extract_method_body(block, "private void Awake()")
    if awake is None or "cachedTransform = transform;" not in awake:
        return fail(class_name + " Awake should seed cachedTransform")
    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    spin = extract_block(text, "internal sealed class PhantomWitchRingSpin")
    if spin is None:
        return fail("missing PhantomWitchRingSpin")
    result = require_cached_field(spin, "PhantomWitchRingSpin")
    if result is not None:
        return result
    spin_update = extract_method_body(spin, "private void Update()")
    if spin_update is None:
        return fail("missing PhantomWitchRingSpin.Update")
    for token in [
        "Transform spinTransform = cachedTransform;",
        "spinTransform = transform;",
        "cachedTransform = spinTransform;",
        "spinTransform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);",
    ]:
        if token not in spin_update:
            return fail("PhantomWitchRingSpin.Update missing cached-transform token -> " + token)
    if "transform.Rotate" in spin_update:
        return fail("PhantomWitchRingSpin.Update should not use direct transform.Rotate")

    ring_pulse = extract_block(text, "internal sealed class PhantomWitchRingPulse")
    if ring_pulse is None:
        return fail("missing PhantomWitchRingPulse")
    result = require_cached_field(ring_pulse, "PhantomWitchRingPulse")
    if result is not None:
        return result
    ring_configure = extract_method_body(ring_pulse, "public void Configure(")
    if ring_configure is None or "this.initialScale = pulseTransform.localScale;" not in ring_configure:
        return fail("PhantomWitchRingPulse.Configure should read localScale through cached Transform")
    ring_update = extract_method_body(ring_pulse, "private void Update()")
    if ring_update is None:
        return fail("missing PhantomWitchRingPulse.Update")
    if "pulseTransform.localScale = initialScale * factor;" not in ring_update:
        return fail("PhantomWitchRingPulse.Update should write localScale through cached Transform")
    if "transform.localScale" in ring_pulse:
        return fail("PhantomWitchRingPulse should not use direct transform.localScale")

    core_pulse = extract_block(text, "internal sealed class PhantomWitchCorePulse")
    if core_pulse is None:
        return fail("missing PhantomWitchCorePulse")
    result = require_cached_field(core_pulse, "PhantomWitchCorePulse")
    if result is not None:
        return result
    if core_pulse.count("this.initialScale = pulseTransform.localScale;") < 2:
        return fail("PhantomWitchCorePulse Configure overloads should read localScale through cached Transform")
    core_update = extract_method_body(core_pulse, "private void Update()")
    if core_update is None:
        return fail("missing PhantomWitchCorePulse.Update")
    if "pulseTransform.localScale = initialScale * factor;" not in core_update:
        return fail("PhantomWitchCorePulse.Update should write localScale through cached Transform")
    if "transform.localScale" in core_pulse:
        return fail("PhantomWitchCorePulse should not use direct transform.localScale")

    print("PhantomWitchScytheRuntimeTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
