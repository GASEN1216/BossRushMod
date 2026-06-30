"""Guard: carried Boss Rush lootboxes should cache Transform access in LateUpdate."""

from pathlib import Path
import sys


SOURCE = Path("Interactables/BossRushLootboxInteractables.cs")


def fail(message: str) -> int:
    print("BossRushCarryInteractableTransformCacheGuard: FAIL - " + message)
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
    carry = extract_block(text, "public class BossRushCarryInteractable")
    if carry is None:
        return fail("missing BossRushCarryInteractable")

    required_fields = [
        "private Transform cachedTransform = null;",
        "private Transform carrierTransform = null;",
    ]
    for token in required_fields:
        if token not in carry:
            return fail("missing cached field -> " + token)

    awake = extract_method_body(carry, "protected override void Awake()")
    if awake is None or "this.cachedTransform = transform;" not in awake:
        return fail("Awake should seed cachedTransform")

    start_carry = extract_method_body(carry, "private void StartPseudoCarry(")
    if start_carry is None:
        return fail("missing StartPseudoCarry")
    if "this.carrierTransform = character.transform;" not in start_carry:
        return fail("StartPseudoCarry should cache the carrier Transform")

    stop_carry = extract_method_body(carry, "private void StopPseudoCarry()")
    if stop_carry is None:
        return fail("missing StopPseudoCarry")
    if "this.carrierTransform = null;" not in stop_carry:
        return fail("StopPseudoCarry should clear the cached carrier Transform")

    late_update = extract_method_body(carry, "private void LateUpdate()")
    if late_update is None:
        return fail("missing LateUpdate")

    required_late_update = [
        "Transform currentCarrierTransform = this.carrierTransform;",
        "currentCarrierTransform = this.carrier.transform;",
        "this.carrierTransform = currentCarrierTransform;",
        "Vector3 forward = currentCarrierTransform.forward;",
        "targetPos = currentCarrierTransform.position + forward * this.carryOffset.z + Vector3.up * this.carryOffset.y;",
        "Transform selfTransform = this.cachedTransform;",
        "selfTransform = transform;",
        "this.cachedTransform = selfTransform;",
        "selfTransform.position = targetPos;",
    ]
    for token in required_late_update:
        if token not in late_update:
            return fail("LateUpdate missing cached-transform token -> " + token)

    forbidden_late_update = [
        "this.carrier.transform.forward",
        "this.carrier.transform.position",
        "base.transform.position = targetPos",
    ]
    for token in forbidden_late_update:
        if token in late_update:
            return fail("LateUpdate should not use direct transform access -> " + token)

    print("BossRushCarryInteractableTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
