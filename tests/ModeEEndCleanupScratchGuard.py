"""Guard: Mode E end cleanup should reuse a scratch list instead of allocating ToArray."""

from pathlib import Path
import sys


SOURCES = [
    Path("ModeE/ModeE.cs"),
    Path("ModeE/ModeELifecycle.cs"),
]


def fail(message: str) -> int:
    print("ModeEEndCleanupScratchGuard: FAIL - " + message)
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
    body = extract_method_body(text, "public void EndModeE(bool showEndMessage = true)")
    if body is None:
        return fail("missing EndModeE body")

    if "modeEAliveEnemies.ToArray()" in body:
        return fail("EndModeE still allocates an enemy array snapshot")

    required = [
        "modeEEndCleanupEnemyScratch",
        "modeEEndCleanupEnemyScratch.Clear();",
        "modeEEndCleanupEnemyScratch.Add(modeEAliveEnemies[i]);",
        "for (int i = modeEEndCleanupEnemyScratch.Count - 1; i >= 0; i--)",
        "CharacterMainControl enemy = modeEEndCleanupEnemyScratch[i];",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing Mode E cleanup scratch snippet -> " + snippet)

    print("ModeEEndCleanupScratchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
