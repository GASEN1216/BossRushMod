"""NpcDialogueHumanWordGuard — NPC 对白与气泡里不许出现「人类」/ human。

2026-11-25 文案优化方案 P2/P3-3 第 4 项：《逃离鸭科夫》里全员是鸭子和变异体，叮当的台词池曾混进 6 处
「最喜欢的人类」「又是人类」，玩家一看就出戏。改写后清零，这里防回归。

只扫 NPC 自己说出口的话，不扫系统说明、Wiki 或代码注释（那里可能合理地提到「人类」这个词）：
  - `Integration/Affinity/NPCs/` 下叮当、羽织好感配置的全部 C# 字符串字面量（注释先剥掉）；
  - `Assets/Data/DuckNpcs.json` 每位永久 NPC 的全部台词字符串（id、场景等非台词字段不看）。
中文拦「人类」，英文拦整词 human / humans；整词边界不误伤 humane、humanity。
"""
from pathlib import Path
import json
import re
import sys

from cs_source_util import strip_comments

ROOT = Path(__file__).resolve().parents[1]
CS_FILES = [
    ROOT / "Integration" / "Affinity" / "NPCs" / "GoblinAffinityConfig.cs",
    ROOT / "Integration" / "Affinity" / "NPCs" / "GoblinAffinityConfig_Persona.cs",
    ROOT / "Integration" / "Affinity" / "NPCs" / "NurseAffinityConfig.cs",
]
DUCK_NPCS = ROOT / "Assets" / "Data" / "DuckNpcs.json"

STRING_LITERAL = re.compile(r'"((?:[^"\\\n]|\\.)*)"')
BANNED = [
    ("人类", re.compile("人类")),
    ("human", re.compile(r"\bhumans?\b", re.IGNORECASE)),
]
# DuckNpcs.json 里不是台词的字符串字段：值是标识、场景名或资源名，不会被玩家读到。
NON_DIALOGUE_KEYS = {"id", "scenes", "scene", "prefab", "model", "icon", "presetName", "baseCharacter"}


def scan_text(text, where, errors):
    for word, pattern in BANNED:
        if pattern.search(text):
            errors.append("%s 出现「%s」：%s" % (where, word, text[:80]))


def scan_cs(path, errors):
    source = strip_comments(path.read_text(encoding="utf-8-sig"))
    found = 0
    for line_no, line in enumerate(source.splitlines(), 1):
        for m in STRING_LITERAL.finditer(line):
            found += 1
            scan_text(m.group(1), "%s:%d" % (path.relative_to(ROOT).as_posix(), line_no), errors)
    return found


def walk_json(node, trail, npc_id, errors, counter):
    if isinstance(node, dict):
        for key, value in node.items():
            if key in NON_DIALOGUE_KEYS:
                continue
            walk_json(value, trail + [str(key)], npc_id, errors, counter)
    elif isinstance(node, list):
        for i, value in enumerate(node):
            walk_json(value, trail + [str(i)], npc_id, errors, counter)
    elif isinstance(node, str):
        counter[0] += 1
        scan_text(node, "DuckNpcs.json %s/%s" % (npc_id, "/".join(trail)), errors)


def main():
    errors = []
    cs_strings = 0
    for path in CS_FILES:
        if not path.is_file():
            errors.append("缺少文件：" + path.relative_to(ROOT).as_posix())
            continue
        cs_strings += scan_cs(path, errors)

    data = json.loads(DUCK_NPCS.read_text(encoding="utf-8-sig"))
    npcs = data.get("npcs") if isinstance(data, dict) else data
    if not isinstance(npcs, list) or not npcs:
        errors.append("DuckNpcs.json 里找不到 npcs 数组")
        npcs = []
    counter = [0]
    for npc in npcs:
        if isinstance(npc, dict):
            walk_json(npc, [], npc.get("id", "?"), errors, counter)

    if cs_strings < 500 or counter[0] < 500:
        errors.append("扫到的字符串太少（C# %d 条、JSON %d 条），文件结构变了就同步本守卫" % (cs_strings, counter[0]))

    if errors:
        print("NpcDialogueHumanWordGuard: FAIL")
        for e in errors[:40]:
            print("  - " + e)
        return 1
    print("NpcDialogueHumanWordGuard: PASS（叮当 / 羽织字符串 %d 条、永久 NPC 字符串 %d 条，无「人类」/ human）" % (cs_strings, counter[0]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
