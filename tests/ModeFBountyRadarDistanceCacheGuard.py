"""Guard: Mode F bounty radar should reuse cached distance squares for display text."""

from pathlib import Path
import sys


SOURCES = [
    Path("ModeF/ModeFUI.cs"),
    Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs"),
]


def fail(message: str) -> int:
    print("ModeFBountyRadarDistanceCacheGuard: FAIL - " + message)
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
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = "\n".join(path.read_text(encoding="utf-8") for path in SOURCES)
    body = extract_method_body(text, "private void UpdateModeFBountyRadarEntry(")
    if body is None:
        return fail("missing UpdateModeFBountyRadarEntry body")

    if "Vector3.Distance(" in body:
        return fail("entry update still recalculates Vector3.Distance")

    required = [
        "public float displayDistanceSqr;",
        "target.displayDistanceSqr",
        "float displayDistanceSqr",
        "Mathf.Sqrt(displayDistanceSqr)",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing cached display-distance snippet -> " + snippet)

    print("ModeFBountyRadarDistanceCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
