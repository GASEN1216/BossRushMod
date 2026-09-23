"""Guard: Mode F kill reward bubble should not allocate a temporary parts list."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFUI_KillRewardBubble.cs")


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

    # 2026-09-23 审美审查 UB-23：段落改由 JoinModeFRewardPart 接（两段一行、全角空格分隔），
    # 收益不再用纯红（读起来像扣血）。拼接仍不许建临时集合——接段 helper 的方法体一起查。
    required = [
        "JoinModeFRewardPart(result, ref parts,",
        "if (parts == 0)",
        "Mathf.RoundToInt(healAmount)",
        "Mathf.RoundToInt(maxHealthGain)",
        '"悬赏印记 " + RichWarningTag + "+1</color>"',
        '"Bounty " + RichWarningTag + "+1</color>"',
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing allocation-free reward text snippet -> " + snippet)
    if "<color=red>" in body:
        return fail("kill reward gains must not use pure red (reads as damage) -> <color=red>")

    join = extract_method_body(text, "private static string JoinModeFRewardPart(")
    if join is None:
        return fail("missing JoinModeFRewardPart body")
    for snippet in forbidden:
        if snippet in join:
            return fail("reward part join still allocates temporary collection -> " + snippet)

    print("ModeFKillRewardBubbleNoListGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
