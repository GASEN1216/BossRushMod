"""Guard: Phantom Witch expand arcs should not allocate path point arrays every frame."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAssetManager_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("PhantomWitchExpandArcPointBufferGuard: FAIL - " + message)
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
    arc = extract_block(text, "internal sealed class PhantomWitchExpandArc")
    if arc is None:
        return fail("missing PhantomWitchExpandArc")

    if "private Vector3[] pathPointsBuffer;" not in arc:
        return fail("missing reusable path point buffer")

    configure = extract_method_body(arc, "public void Configure(")
    if configure is None:
        return fail("missing Configure")
    if "this.pathPointsBuffer = null;" not in configure:
        return fail("Configure should reset reusable path point buffer")

    update = extract_method_body(arc, "private void Update()")
    if update is None:
        return fail("missing Update")

    required_update_tokens = [
        "int pointCount = segments + 1;",
        "Vector3[] points = pathPointsBuffer;",
        "if (points == null || points.Length != pointCount)",
        "points = new Vector3[pointCount];",
        "pathPointsBuffer = points;",
        "pathMesh.SetPath(points, width);",
    ]
    for token in required_update_tokens:
        if token not in update:
            return fail("Update missing reusable-buffer token -> " + token)

    if "new Vector3[segments + 1]" in update:
        return fail("Update should not allocate path points directly from segments each frame")

    print("PhantomWitchExpandArcPointBufferGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
