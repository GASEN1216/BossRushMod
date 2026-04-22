"""
Guard for Phantom Witch accessibility consistency.

The ambient presence component should not expose a public method whose
parameter type is an internal enum.
"""

from pathlib import Path
import re
import sys


AMBIENT = Path("Integration/PhantomWitch/PhantomWitchAmbientPresence.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = AMBIENT.read_text(encoding="utf-8")

    public_signature = re.search(
        r"public\s+void\s+SetDetailLevel\s*\(\s*PhantomWitchFxDetailLevel\s+level\s*\)",
        text,
    )
    if public_signature is not None:
        return fail("PhantomWitchAccessibilityGuard: SetDetailLevel must not be public while PhantomWitchFxDetailLevel is internal")

    internal_signature = re.search(
        r"internal\s+void\s+SetDetailLevel\s*\(\s*PhantomWitchFxDetailLevel\s+level\s*\)",
        text,
    )
    if internal_signature is None:
        return fail("PhantomWitchAccessibilityGuard: missing internal SetDetailLevel signature")

    print("PhantomWitchAccessibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
