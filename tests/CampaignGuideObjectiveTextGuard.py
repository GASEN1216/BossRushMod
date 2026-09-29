"""CampaignGuideObjectiveTextGuard: 杰夫引导（590201–590214）的「目标」行只写直接动作。

2026-09-29 owner：目标里不能把任务描述（杰夫的原话 HintCN/HintEN）再抄一遍，
也不要再挂「去试一次，回基地跟杰夫讲讲」这类旁白副行。
- CampaignGuideTable.Describe 只能按 Id 给出短目标，不读 HintCN / HintEN；
- 每条引导都有一行中英双语目标，中文不超过 30 字；
- BuildGuideBinding 的唯一任务不挂 ExtraHint。
"""

from pathlib import Path
import re
import sys

from cs_source_util import clean_source

TABLE = Path("Campaign/CampaignGuideTable.cs")
CLIENT = Path("Campaign/CampaignOfficialQuestClient.cs")
MAX_CN = 30


def fail(message: str) -> int:
    print("CampaignGuideObjectiveTextGuard: FAIL - " + message)
    return 1


def body(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    opening = text.find("{", start)
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    raw_table = TABLE.read_text(encoding="utf-8-sig")
    table = clean_source(raw_table)
    describe = body(raw_table, "internal static string Describe(Definition definition, bool done)")
    if not describe:
        return fail("CampaignGuideTable.Describe not found")
    if "HintCN" in describe or "HintEN" in describe:
        return fail("objective line must not repeat the quest description (HintCN/HintEN)")

    ids = re.findall(r"internal const string (\w+) = \"[a-z_]+\";", table)
    cases = dict(re.findall(r"case (\w+): objective = L10n\.T\(\"([^\"]+)\", \"[^\"]+\"\); break;", describe))
    for guide_id in ids:
        if guide_id not in cases:
            return fail("guide has no direct bilingual objective -> " + guide_id)
        if len(cases[guide_id]) > MAX_CN:
            return fail("objective too long (> %d chars) -> %s" % (MAX_CN, guide_id))

    client = clean_source(CLIENT.read_text(encoding="utf-8-sig"))
    guide_binding = body(client, "private OfficialQuestBinding BuildGuideBinding(")
    if "Description = () => CampaignGuideTable.Describe(guide," not in guide_binding:
        return fail("guide task must describe itself through CampaignGuideTable.Describe")
    if "ExtraHint = null" not in guide_binding or "去试一次，回基地跟杰夫讲讲" in client:
        return fail("guide task must not carry the redundant extra hint line")

    print("CampaignGuideObjectiveTextGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
