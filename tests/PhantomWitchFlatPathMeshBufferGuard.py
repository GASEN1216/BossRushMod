"""Guard: Phantom Witch flat path meshes should reuse geometry buffers across SetPath calls."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAssetManager_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("PhantomWitchFlatPathMeshBufferGuard: FAIL - " + message)
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
    path_mesh = extract_block(text, "internal sealed class PhantomWitchFlatPathMesh")
    if path_mesh is None:
        return fail("missing PhantomWitchFlatPathMesh")

    required_fields = [
        "private Vector3[] verticesBuffer;",
        "private Vector2[] uvBuffer;",
        "private int[] trianglesBuffer;",
    ]
    for token in required_fields:
        if token not in path_mesh:
            return fail("missing reusable mesh buffer field -> " + token)

    set_path = extract_method_body(path_mesh, "internal void SetPath(")
    if set_path is None:
        return fail("missing SetPath")
    if "BuildPathMesh(points, width);" not in set_path:
        return fail("SetPath should use the instance mesh-buffer builder")
    if "BuildPathMesh(mesh, points, width)" in set_path:
        return fail("SetPath should not call the old static builder")

    build = extract_method_body(path_mesh, "private void BuildPathMesh(")
    if build is None:
        return fail("missing instance BuildPathMesh")

    required_build_tokens = [
        "Mesh targetMesh = mesh;",
        "int vertexCount = pointCount * 2;",
        "int triangleCount = (pointCount - 1) * 6;",
        "if (verticesBuffer == null || verticesBuffer.Length != vertexCount || uvBuffer == null || uvBuffer.Length != vertexCount)",
        "verticesBuffer = new Vector3[vertexCount];",
        "uvBuffer = new Vector2[vertexCount];",
        "if (trianglesBuffer == null || trianglesBuffer.Length != triangleCount)",
        "trianglesBuffer = new int[triangleCount];",
        "Vector3[] vertices = verticesBuffer;",
        "Vector2[] uv = uvBuffer;",
        "int[] triangles = trianglesBuffer;",
    ]
    for token in required_build_tokens:
        if token not in build:
            return fail("BuildPathMesh missing reusable-buffer token -> " + token)

    forbidden_old_allocations = [
        "new Vector3[pointCount * 2]",
        "new Vector2[pointCount * 2]",
        "new int[(pointCount - 1) * 6]",
    ]
    for token in forbidden_old_allocations:
        if token in build:
            return fail("BuildPathMesh should not allocate from pointCount directly each call -> " + token)

    print("PhantomWitchFlatPathMeshBufferGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
