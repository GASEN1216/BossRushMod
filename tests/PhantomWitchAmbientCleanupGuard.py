"""
Guard: Phantom Witch ambient presence must destroy its owned child objects before
releasing materials.

Reason:
- cold halo / heartbeat quads own renderers that keep referencing the material
- destroying the material first can leave magenta rectangles when those quads
  survive for another frame on the boss object
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAmbientPresence.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    if "CleanupOwnedObjects" not in text:
        return fail("PhantomWitchAmbientCleanupGuard: missing CleanupOwnedObjects helper")

    on_destroy = extract_block(text, "private void OnDestroy()")
    if not on_destroy:
        return fail("PhantomWitchAmbientCleanupGuard: missing OnDestroy block")

    if "CleanupOwnedObjects();" not in on_destroy:
        return fail("PhantomWitchAmbientCleanupGuard: OnDestroy must call CleanupOwnedObjects() before destroying materials")

    helper = extract_block(text, "private void CleanupOwnedObjects()")
    if not helper:
        return fail("PhantomWitchAmbientCleanupGuard: missing CleanupOwnedObjects block")

    required = [
        "Destroy(veilParticles.gameObject)",
        "Destroy(groundMistParticles.gameObject)",
        "Destroy(coldHaloTransform.gameObject)",
        "Destroy(heartbeatTransform.gameObject)",
    ]
    for token in required:
        if token not in helper:
            return fail(f"PhantomWitchAmbientCleanupGuard: helper missing {token}")

    print("PhantomWitchAmbientCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
