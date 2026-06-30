"""Guard: Mode F bounty radar should skip the leader before regular visibility checks."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs")


def fail(message: str) -> int:
    print("ModeFBountyRadarLeaderSkipBeforeVisibilityGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "private void UpdateModeFBountyRadarUI()")
    if body is None:
        return fail("missing UpdateModeFBountyRadarUI body")

    leader_skip = "if (object.ReferenceEquals(boss, leader))"
    visible_check = "if (IsModeFBountyRadarTargetVisible(radarCamera, bossPos))"
    leader_index = body.find(leader_skip)
    visible_index = body.find(visible_check)
    if leader_index < 0:
        return fail("missing regular-loop leader skip")
    if visible_index < 0:
        return fail("missing regular-loop visibility check")
    if leader_index > visible_index:
        return fail("regular-loop leader skip still runs after visibility projection")

    print("ModeFBountyRadarLeaderSkipBeforeVisibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
