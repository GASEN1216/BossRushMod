"""Guard: forced Zombie purification collection should not allocate an array snapshot."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombiePurificationPointController.cs")


def fail(message: str) -> int:
    print("ZombieModePurificationForceCollectNoArrayGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8")
    body = extract_method_body(text, "private void ForceCollectZombieModePendingPurificationStars(")
    if body is None:
        return fail("missing ForceCollectZombieModePendingPurificationStars body")

    if ".ToArray()" in body:
        return fail("force collection still allocates a pending-star array snapshot")

    required = [
        "for (int i = zombieModeRunState.PendingPurificationStars.Count - 1; i >= 0; i--)",
        "ZombiePurificationStar star = zombieModeRunState.PendingPurificationStars[i];",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing reverse-index collection snippet -> " + snippet)

    print("ZombieModePurificationForceCollectNoArrayGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
