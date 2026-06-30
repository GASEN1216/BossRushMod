"""Guard: Flight Totem action should cache the character Transform in flight loops."""

from pathlib import Path
import sys


SOURCE = Path("Integration/FlightTotem/CA_Flight.cs")


def fail(message: str) -> int:
    print("FlightTotemCharacterTransformCacheGuard: FAIL - " + message)
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


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    if "private Transform cachedCharacterTransform;" not in text:
        return fail("missing cached character Transform field")

    accessor = extract_block(text, "private Transform GetCharacterTransform()")
    if accessor is None:
        return fail("missing GetCharacterTransform accessor")
    for token in [
        "cachedCharacterTransform = characterController.transform;",
        "return cachedCharacterTransform;",
    ]:
        if token not in accessor:
            return fail("GetCharacterTransform missing token -> " + token)

    for signature in [
        "protected override bool OnAbilityStart()",
        "protected override void OnAbilityStop()",
        "private void LateUpdate()",
        "private void UpdateFlightPlatform()",
    ]:
        block = extract_block(text, signature)
        if block is None:
            return fail("missing block -> " + signature)
        if "GetCharacterTransform()" not in block:
            return fail(signature + " should use cached character Transform accessor")
        if "characterController.transform" in block:
            return fail(signature + " should not use direct characterController.transform")

    print("FlightTotemCharacterTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
