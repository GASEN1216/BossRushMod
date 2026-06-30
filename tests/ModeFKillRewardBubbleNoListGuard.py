"""Guard: Mode F kill reward bubble should not allocate a temporary parts list."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs")


def fail(message: str) -> int:
    print("ModeFKillRewardBubbleNoListGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private string BuildModeFKillRewardBubbleText(")
    if body is None:
        return fail("missing BuildModeFKillRewardBubbleText body")

    forbidden = [
        "List<string>",
        "new List",
        "parts.Add",
        "parts.ToArray",
        "string.Join",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("kill reward bubble still allocates temporary collection -> " + snippet)

    required_order = [
        "if (healAmount > 0.01f)",
        "if (maxHealthGain > 0.01f)",
        "if (isBountyBoss)",
        'return L10n.T("奖励已结算", "Reward applied");',
    ]

    previous = -1
    for snippet in required_order:
        current = body.find(snippet)
        if current < 0:
            return fail("missing expected reward branch snippet -> " + snippet)
        if current <= previous:
            return fail("reward branch order changed around -> " + snippet)
        previous = current

    required = [
        'result + " | " +',
        "hasPart",
        "Mathf.RoundToInt(healAmount)",
        "Mathf.RoundToInt(maxHealthGain)",
        '"悬赏印记 <color=red>+1</color>"',
        '"Bounty <color=red>+1</color>"',
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing allocation-free reward text snippet -> " + snippet)

    print("ModeFKillRewardBubbleNoListGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
