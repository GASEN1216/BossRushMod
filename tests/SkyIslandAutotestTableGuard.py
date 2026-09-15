# -*- coding: utf-8 -*-
"""全自动实机验收的步骤表守卫（Assets/Data/SkyIslandAutotest.json，2026-09-14）。

步骤表是数据：以后加步骤只改 JSON。游戏侧开跑时 F3AutotestJudges.ValidateTable 会再核一遍结构，可那要等 owner 进游戏按按钮
才知道——名字写错一个字，岛上那一步就白跑一趟，结构错了整轮在读表那一步就停。这里在提交前把能离线核的全核掉：

1. 结构（与 ValidateTable 同口径）：阶段、步骤 id、预算、动词与断言名只认 F3AutotestJudges 的名单；每步至少一条断言或一张截图
   （AssertingVerbs 里自带判定的动作算断言），class=shot 的步骤必须有截图；第一个 story 阶段以 reset 开头。
2. 名单与实现一致：名单里的每个动词在 RunAutotestVerb 有分派，每个断言名在 EvaluateAutotestAssert 有 case。
3. 清单编号：天空岛人工清单 2.10–2.19（含 2.16 的 D1–D6）每一行都在覆盖表里，引用的编号都真实存在；GameplayCoverage.json 里
   全部 M_SKY_ISLAND_* 都有归类（新增人工用例就在覆盖表里补一行）。清单文档是 local-only，不在本机时记 PARTIAL。
   「只能人工」理由必须写明手感 / 声音 / 好不好玩。
4. 证据：assert:case 只认 AutotestCaseDelegate 映射到的用例；覆盖表证据认 GameplayCoverage.json 已登记的自动项；
   offline: 证据的路径真实存在，且只挂在自动断言行上。
5. 名字对得上生产：瞬移与交互的标记在场景几何表里；跟居民说话前瞬移到的正是这位居民的站位（SkyIslandResidents）；采集点挂在瞬移到的
   标记上；谜题、灯、遭遇、剧情动作与旗标、耗材效果、物品 TypeID、字幕键、颜色 token、HUD / 世界物体名、面板与字幕文字
   （中英任一写法出现在天空岛生产文案里）都核一遍。
反向检查在内存里逐条破坏步骤表或名单，必须转红。
"""
import copy
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import strip_comments  # noqa: E402

TABLE = "Assets/Data/SkyIslandAutotest.json"
JUDGES = "DebugAndTools/F3GameplayValidationAutotestJudges.cs"
ACTIONS = "DebugAndTools/F3GameplayValidationAutotestActions.cs"
ASSERTS = "DebugAndTools/F3GameplayValidationAutotestAsserts.cs"
CHECKLIST = "docs/制作教程/天空岛/天空岛_待人工验证清单.md"
COVERAGE = "Assets/Data/GameplayCoverage.json"
GEOMETRY = "ArtSource/SkyIsland/Validation/sky_island_geometry.json"
WORLD = "Assets/Data/SkyIsland/World.json"
SKY_DIR = "DebugAndTools/SkyIsland"

BOSS_KINDS = ("storm", "foreman", "stargazer")
WORLD_ALIASES = {"gnat", "ground_ring", "gather_glow", "echo_ring"}
DYNAMIC_PREFIXES = ("制作 ", "Make ", "还不会做 ", "Not yet: ")
LABEL_ASSERTS = {"choice_present", "choice_absent", "body_contains", "body_absent", "caption_contains", "objective_contains",
                 "dialogue_line_contains"}
TYPE_ASSERTS = {"pack_count_ge", "pack_delta", "pack_delta_ge", "crate_has"}
NUM, INT = "num", "int"
VERB_ARGS = {
    "wait_real": [NUM], "wait_panel": [NUM, ("true", "false")], "wait_dialogue": [NUM, ("true", "false")], "wait_dialogue_typed": [NUM],
    "wait_quiet": [NUM, ("true", "false")], "dialogue_advance": [NUM], "dialogue_choose": [INT, NUM], "choose": [INT, NUM],
    "hover": [INT], "clear_nearby": [NUM, NUM], "kill_nearby": [NUM, NUM], "invincible": [("on", "off", "restore")],
    "night": [("on", "off", "restore"), NUM], "set_health": [NUM], "spawn_gnats": [INT, NUM], "echo_hurt": [NUM, NUM],
    "frame": [NUM], "use_compass": [], "click_close": [], "close_panel": [], "open_map": [], "close_view": [],
    "open_modeg_confirm": [], "close_modeg_confirm": [], "reachability": [], "encounter_cap": [],
    "wait_boss": [BOSS_KINDS, NUM, ("optional",)], "boss_hurt": [BOSS_KINDS, NUM, NUM],
    # 只有带档案的头目 / 岛主有专属掉落：噬风不走这条（没有 SkyIslandBossProfile）。
    "loot_boss": [("foreman", "stargazer"), NUM],
}
# 这些动作之后官方对话换了一句、换了一段或关掉了：再截对话图 / 再单次推进之前要重新 wait_dialogue_typed
# （2026-09-15 第五轮：官方逐字显示 40 字/秒，截图截在半句上；打字途中的单次推进只把这句补完、不翻页）。
DIALOGUE_RESETTERS = {"interact", "wait_dialogue", "dialogue_advance", "dialogue_choose", "teleport", "teleport_view",
                      "close_panel", "choose", "choose_label", "click_close"}


def read(rel):
    path = ROOT / rel
    return path.read_text(encoding="utf-8-sig") if path.is_file() else None


def blank_strings(code):
    out, i, n = list(code), 0, len(code)
    while i < n:
        if code[i] in "\"'":
            quote, i = code[i], i + 1
            while i < n and code[i] != quote and code[i] != "\n":
                if code[i] == "\\" and i + 1 < n:
                    out[i] = out[i + 1] = " "
                    i += 2
                    continue
                out[i] = " "
                i += 1
            i += 1
            continue
        i += 1
    return "".join(out)


def method_body(code, name):
    blank = blank_strings(code)
    match = re.search(r"\b" + name + r"\s*\([^)]*\)\s*\{", blank)
    if not match:
        return ""
    i, depth = match.end(), 1
    while i < len(blank) and depth:
        depth += (blank[i] == "{") - (blank[i] == "}")
        i += 1
    return code[match.end():i]


def string_array(code, name):
    match = re.search(r"\b" + name + r"\s*=\s*\{(.*?)\};", code, re.S)
    return re.findall(r'"([^"]*)"', match.group(1)) if match else []


def enum_members(code, name):
    match = re.search(r"\benum\s+" + name + r"\s*(?::\s*\w+\s*)?\{(.*?)\}", code, re.S)
    return set(re.findall(r"\b([A-Z]\w*)\b", match.group(1))) if match else set()


def is_checklist_id(cid):
    if not isinstance(cid, str) or not cid:
        return False
    if cid.startswith("M_SKY_ISLAND_"):
        tail = cid[len("M_SKY_ISLAND_"):]
        return len(tail) == 2 and tail.isdigit() and int(tail) >= 1
    parts = cid.split(".")
    if len(parts) != 3 or parts[0] != "2" or not parts[1].isdigit():
        return False
    last = parts[2]
    if len(last) == 2 and last[0] == "D" and last[1] in "123456789":
        return parts[1] == "16"
    digits = len(last) - len(last.lstrip("0123456789"))
    return digits > 0 and (digits == len(last) or (digits == len(last) - 1 and "a" <= last[-1] <= "z"))


def load_context():
    ctx = {}
    judges = strip_comments(read(JUDGES))
    for key, name in (("verbs", "ActionVerbs"), ("asserts", "AssertNames"), ("whens", "StageWhens"),
                      ("classes", "CoverageClasses"), ("manual_keywords", "ManualReasonKeywords"), ("asserting", "AssertingVerbs")):
        ctx[key] = string_array(judges, name)
    actions, asserts = strip_comments(read(ACTIONS)), strip_comments(read(ASSERTS))
    ctx["verb_cases"] = set(re.findall(r'case "(\w+)":', method_body(actions, "RunAutotestVerb")))
    ctx["assert_cases"] = set(re.findall(r'case "(\w+)":', method_body(asserts, "EvaluateAutotestAssert")))
    ctx["case_map"] = set(re.findall(r'case "([A-Z0-9_]+)":', method_body(asserts, "AutotestCaseDelegate")))
    ctx["caption_keys"] = set(re.findall(r'case "(\w+)":', method_body(actions, "AutotestCaption")))
    ctx["color_tokens"] = set(re.findall(r'case "([\w.]+)":', method_body(asserts, "TryAutotestColorToken")))
    coverage = json.loads(read(COVERAGE))
    ctx["registered"] = {case for feature in coverage["features"] for case in feature.get("automatic", [])}
    ctx["manual_m"] = {case["id"] for feature in coverage["features"] for case in feature.get("manual", [])
                       if str(case.get("id", "")).startswith("M_SKY_ISLAND_")}
    sky = {path.name: strip_comments(path.read_text(encoding="utf-8-sig")) for path in (ROOT / SKY_DIR).glob("*.cs")}
    ctx["story_actions"] = enum_members(sky["SkyIslandStoryRules.cs"], "SkyIslandStoryAction")
    ctx["story_flags"] = enum_members(sky["SkyIslandStoryRules.cs"], "SkyIslandStoryFlag")
    ctx["buffs"] = enum_members(sky["SkyIslandFieldcraftRules.cs"], "SkyIslandFieldBuff")
    ctx["lamps"] = set(re.findall(r'Lamp\("(Light_\w+)"', sky["SkyIslandLights.cs"]))
    ctx["gather"] = dict(re.findall(r'Node\("(\w+)",\s*"(\w+)"', sky["SkyIslandFieldcraftRules.cs"]))
    ctx["puzzles"] = set(re.findall(r'Puzzle\("(\w+)"', sky["SkyIslandPuzzles.cs"]))
    encounters = dict(re.findall(r'Encounter\("(\w+)",\s*"(\w+)"', sky["SkyIslandContent.cs"]))
    for row in json.loads(read(WORLD)).get("encounters", []):
        encounters[row["id"]] = row["marker"]
    ctx["encounters"] = set(encounters)
    geometry = json.loads(read(GEOMETRY))
    ctx["markers"] = {row["name"] for row in geometry.get("markers", []) if isinstance(row, dict) and row.get("name")} | set(encounters.values())
    arrays = {name: re.findall(r'"([^"]*)"', body) for name, body in re.findall(r"string\[\]\s+(\w+)\s*=\s*\{([^}]*)\}", sky["SkyIslandResidents.cs"])}
    ids = next((values for values in arrays.values() if values and all(v.startswith("sky_") for v in values)), [])
    marks = arrays.get("Markers", [])
    ctx["residents"] = dict(zip(ids, marks)) if ids and len(ids) == len(marks) else {}
    ctx["type_ids"] = {int(v) for v in re.findall(r"\bSkyIsland\w+\s*=\s*(\d+)\s*;", read("Config/ConfigItemIds.cs"))}
    literals = set()
    for code in sky.values():
        literals.update(re.findall(r'"((?:[^"\\\n]|\\.)*)"', code))
    ctx["literals"] = literals
    ctx["literal_blob"] = "\n".join(literals)
    ctx["prefix_literals"] = sorted(lit for lit in literals if re.fullmatch(r"SkyIsland\w*_", lit))
    doc = read(CHECKLIST)
    if doc is None:
        ctx["doc_ids"] = None
    else:
        # 行首编号后面可能跟着「🤖（部分）」一类标注，编号本身到空白或竖线为止。
        ids = set(re.findall(r"^\|\s*(2\.1[0-9]\.\d+[a-z]?)(?=[\s|])[^|\n]*\|", doc, re.M))
        ids.update("2.16." + d for d in re.findall(r"^\|\s*(D[1-9])(?=[\s|])[^|\n]*\|", doc, re.M))
        ctx["doc_ids"] = ids
    return ctx


def is_num(text, integer=False):
    try:
        if integer:
            int(text)
        else:
            float(text)
        return True
    except ValueError:
        return False


class Checker:
    def __init__(self, table, ctx):
        self.table, self.ctx, self.errors, self.warnings = table, ctx, [], []

    def err(self, text):
        self.errors.append(text)

    def label_text(self, where, text):
        alternatives = text.split("|")
        if text and any(a == "" for a in alternatives):
            self.err("%s：文字「%s」里有空的备选（多写了 |）" % (where, text))
        for alternative in alternatives:
            probe = alternative
            for prefix in DYNAMIC_PREFIXES:
                if probe.startswith(prefix):
                    probe = probe[len(prefix):]
            if probe and probe in self.ctx["literal_blob"]:
                return
        if text:
            self.err("%s：文字「%s」的中英写法都不在天空岛生产文案里（写错字的话岛上那一步白跑）" % (where, text))

    def marker(self, where, name):
        if name not in self.ctx["markers"]:
            self.err("%s：标记 %s 不在场景几何表（%s 的 markers）里" % (where, name, GEOMETRY))

    def object_name(self, where, name):
        if not name:
            self.err(where + "：物体名为空")
            return
        if name in self.ctx["literals"] or name in WORLD_ALIASES:
            return
        if name.endswith("_Completed") and name[:-len("_Completed")] in self.ctx["markers"]:
            return
        for prefix in self.ctx["prefix_literals"]:
            rest = name[len(prefix):]
            if name.startswith(prefix) and (rest in self.ctx["markers"] or rest in self.ctx["gather"]):
                return
        self.err("%s：物体名 %s 不是天空岛生产代码建出来的名字" % (where, name))

    def type_id(self, where, text):
        if not is_num(text, True) or int(text) not in self.ctx["type_ids"]:
            self.err("%s：TypeID %s 不是天空岛物品（Config/ConfigItemIds.cs 的 SkyIsland*）" % (where, text))

    def positional(self, where, args, spec):
        if len(args) > len(spec):
            self.err("%s：参数多了（%s）" % (where, ":".join(args)))
        for value, kind in zip(args, spec):
            if kind == NUM and not is_num(value):
                self.err("%s：参数 %s 应为数字" % (where, value))
            elif kind == INT and not is_num(value, True):
                self.err("%s：参数 %s 应为整数" % (where, value))
            elif isinstance(kind, tuple) and value not in kind:
                self.err("%s：参数 %s 只能是 %s" % (where, value, "/".join(kind)))

    def verb(self, where, verb, args, last_teleport):
        ctx, a0 = self.ctx, (args[0] if args else "")
        if verb in VERB_ARGS:
            self.positional(where, args, VERB_ARGS[verb])
        elif verb == "teleport":
            self.marker(where, a0)
            self.positional(where, args[1:], [NUM, NUM, NUM])
            return a0
        elif verb == "teleport_view":
            # 取景瞬移：目标可以是标记，也可以是生产代码建出来的物体（SkyIslandGather_A2）。
            if a0 not in ctx["markers"]:
                self.object_name(where, a0)
            self.positional(where, args[1:], [NUM, NUM, NUM])
            return a0
        elif verb == "ring_replay":
            self.object_name(where, a0)
            self.positional(where, args[1:], [])
        elif verb == "interact":
            if a0 == "resident":
                rid = args[1] if len(args) > 1 else ""
                stand = ctx["residents"].get(rid)
                if stand is None:
                    self.err("%s：居民 %s 不在 SkyIslandResidents 里" % (where, rid))
                elif last_teleport is not None and last_teleport != stand:
                    self.err("%s：跟 %s 说话前瞬移到了 %s，可 ta 站在 %s（SkyIslandResidents.Markers）" % (where, rid, last_teleport, stand))
            elif a0 == "gather":
                gid = args[1] if len(args) > 1 else ""
                if gid not in ctx["gather"]:
                    self.err("%s：采集点 %s 不在 SkyIslandFieldcraftRules 的节点表里" % (where, gid))
                elif last_teleport is not None and last_teleport != ctx["gather"][gid]:
                    self.err("%s：采集点 %s 挂在 %s，瞬移到的却是 %s" % (where, gid, ctx["gather"][gid], last_teleport))
            elif a0 == "completed":
                self.marker(where, args[1] if len(args) > 1 else "")
            elif a0 not in ("pigeon", "departure"):
                self.marker(where, a0)
                if len(args) > 1:
                    self.err("%s：交互目标参数多了（%s）" % (where, ":".join(args)))
                if last_teleport is not None and last_teleport != a0:
                    self.err("%s：交互 %s 之前瞬移到的是 %s" % (where, a0, last_teleport))
        elif verb in ("give", "give_if_missing"):
            self.type_id(where, a0)
            self.positional(where, args[1:], [INT])
        elif verb == "use_item":
            self.type_id(where, a0)
            self.positional(where, args[1:], [("heal",)])
        elif verb == "use_buff":
            if a0 not in ctx["buffs"]:
                self.err("%s：耗材效果 %s 不在 SkyIslandFieldBuff 里" % (where, a0))
            self.positional(where, args[1:], [("true", "false")])
        elif verb == "puzzle_solve":
            if a0 not in ctx["puzzles"]:
                self.err("%s：谜题 %s 不在 SkyIslandPuzzles 里" % (where, a0))
            self.positional(where, args[1:], [("0", "1")])
        elif verb == "caption":
            if a0 not in ctx["caption_keys"]:
                self.err("%s：字幕键 %s 不在 AutotestCaption 里" % (where, a0))
            self.positional(where, args[1:], [("true", "false")])
        elif verb in ("wait_object", "loot"):
            self.object_name(where, a0)
            self.positional(where, args[1:], [NUM, ("true", "false")] if verb == "wait_object" else [NUM])
        elif verb == "wait_alpha":
            self.object_name(where, a0)
            self.positional(where, args[1:], [NUM, NUM, ("true", "false")])
        elif verb == "choose_label":
            self.label_text(where, a0)
            self.positional(where, args[1:], [NUM, ("soft",)])
        elif verb == "wait_caption":
            self.label_text(where, a0)
            self.positional(where, args[1:], [NUM, ("optional",)])
        return last_teleport

    def assertion(self, where, args):
        ctx = self.ctx
        name, rest = args[0], args[1:]
        r0 = rest[0] if rest else ""
        if name == "case":
            if r0 not in ctx["case_map"]:
                self.err("%s：assert:case:%s 没有映射（AutotestCaseDelegate 里没有这个用例）" % (where, r0))
        elif name in LABEL_ASSERTS:
            self.label_text(where, r0)
        elif name in ("flag", "no_flag"):
            if r0 not in ctx["story_flags"]:
                self.err("%s：剧情旗标 %s 不在 SkyIslandStoryFlag 里" % (where, r0))
        elif name in TYPE_ASSERTS:
            self.type_id(where, r0)
            self.positional(where, rest[1:], [INT])
        elif name == "crate_has_any":
            for part in r0.split("+"):
                self.type_id(where, part)
        elif name == "near":
            self.marker(where, r0)
            self.positional(where, rest[1:], [NUM])
        elif name in ("object_present", "object_absent", "object_static", "alpha_le"):
            self.object_name(where, r0)
            self.positional(where, rest[1:], [NUM])
        elif name == "visible_min":
            self.object_name(where, r0)
            self.positional(where, rest[1:], [NUM])
            # 圆环（撤离环、噬风预警圈）量的是沿环带的覆盖率（F3AutotestJudges.JudgeRingCoverage），不是 Weber 对比度：
            # 碎成几段的环也要判红，阈值至少一半（2026-09-15 第四轮：旧口径 0.15 被广场日照石面骗过）。
            if r0 in ("ground_ring", "echo_ring") and len(rest) > 1:
                try:
                    if float(rest[1]) < 0.5:
                        self.err("%s：圆环 %s 的可见度是环带覆盖率，阈值至少 0.5（现在 %s）" % (where, r0, rest[1]))
                except ValueError:
                    pass
        elif name == "contrast_min":
            base, _, part = r0.partition("#")
            if part not in ("", "first", "last"):
                self.err("%s：取色路径 %s 的 # 后只能是 first / last" % (where, r0))
            for segment in base.split("/"):
                if segment not in ctx["literals"]:
                    self.err("%s：取色路径 %s 的一段 %s 不是天空岛界面里建出来的物体名" % (where, r0, segment))
            self.positional(where, rest[1:], [NUM])
        elif name == "row_contrast_min":
            self.positional(where, rest, [INT, INT, NUM])
        elif name == "color_equals":
            for token in rest:
                if token not in ctx["color_tokens"]:
                    self.err("%s：颜色 token %s 不在 TryAutotestColorToken 里" % (where, token))
        elif name == "extraction_open":
            self.positional(where, rest, [("bell", "wind", "star", "dock"), ("true", "false")])
        elif name in ("wind_gale", "chilled"):
            self.positional(where, rest, [("true", "false")])
        elif name == "language_is":
            self.positional(where, rest, [("zh", "en")])
        elif name == "log_contains":
            if ":".join(rest) not in ctx["literal_blob"]:
                self.err("%s：日志文字 %s 不在天空岛生产代码里" % (where, ":".join(rest)))
        elif name in ("echo_starts", "killed_ge", "gnats_alive_ge", "choice_count_le"):
            self.positional(where, rest, [INT])
        elif name == "health_ge":
            self.positional(where, rest, [NUM])
        elif name == "boss_alive":
            self.positional(where, rest, [BOSS_KINDS, NUM, ("true", "false")])

    def run(self):
        ctx, table = self.ctx, self.table
        verbs, asserts = set(ctx["verbs"]), set(ctx["asserts"])
        for verb in sorted(verbs - {"assert", "shot", "burst"} - ctx["verb_cases"]):
            self.err("动词 %s 在 F3AutotestJudges.ActionVerbs 里，RunAutotestVerb 却没有分派" % verb)
        for verb in sorted(ctx["verb_cases"] - verbs):
            self.err("RunAutotestVerb 分派了 %s，名单 ActionVerbs 里却没有（步骤表用不了）" % verb)
        for name in sorted(asserts - ctx["assert_cases"]):
            self.err("断言 %s 在 AssertNames 里，EvaluateAutotestAssert 却没有 case" % name)
        for verb in sorted(set(ctx["asserting"]) - verbs):
            self.err("AssertingVerbs 里的 %s 不是动词" % verb)

        stage_ids, first_story = set(), True
        for stage in table.get("stages", []):
            sid = stage.get("id")
            where = "阶段 %s" % sid
            if not sid or sid in stage_ids:
                self.err("阶段 id 缺失或重复：%s" % sid)
            stage_ids.add(sid)
            apply = stage.get("apply", [])
            if stage.get("when") not in ctx["whens"]:
                self.err("%s：when=%s 不在 StageWhens 里" % (where, stage.get("when")))
            if stage.get("when") != "story":
                if apply:
                    self.err(where + "：只有 story 阶段能带推进动作")
                continue
            if first_story and (not apply or apply[0] != "reset"):
                self.err(where + "：第一个 story 阶段必须以 reset 开头（从新档起推）")
            first_story = False
            for op in apply:
                if op == "reset":
                    continue
                verb, _, arg = op.partition(":")
                if not arg or ":" in arg:
                    self.err("%s：推进动作参数不对：%s" % (where, op))
                elif verb == "clear" and arg not in ctx["encounters"]:
                    self.err("%s：遭遇 %s 不在内容表里" % (where, arg))
                elif verb == "story" and arg not in ctx["story_actions"]:
                    self.err("%s：剧情动作 %s 不在 SkyIslandStoryAction 里" % (where, arg))
                elif verb == "lamp" and arg not in ctx["lamps"]:
                    self.err("%s：风晶灯 %s 不在 SkyIslandLights 里" % (where, arg))
                elif verb not in ("clear", "story", "lamp", "note"):
                    self.err("%s：推进动作 %s 不认识" % (where, op))
        if first_story:
            self.err("步骤表没有 story 阶段")

        step_ids = set()
        steps = table.get("steps", [])
        for step in steps:
            sid = step.get("id") or "(no id)"
            where = "步骤 " + sid
            if not sid.startswith("SKY_AUTO_") or sid in step_ids:
                self.err(where + "：id 必须以 SKY_AUTO_ 开头且不重复")
            step_ids.add(sid)
            if not step.get("title"):
                self.err(where + "：缺标题")
            if step.get("stage") not in stage_ids:
                self.err("%s：阶段 %s 不存在" % (where, step.get("stage")))
            if step.get("class") not in ("auto", "shot"):
                self.err(where + "：class 只能是 auto / shot")
            budget = step.get("budget", 60)
            if not isinstance(budget, (int, float)) or not 0 < budget <= 600:
                self.err(where + "：预算必须在 (0, 600] 秒")
            if not step.get("checklist"):
                self.err(where + "：没有清单编号")
            for cid in step.get("checklist", []):
                if not is_checklist_id(cid):
                    self.err("%s：清单编号 %s 格式不对" % (where, cid))
            counted, shots, last_teleport, typed = 0, 0, None, False
            for action in step.get("actions", []):
                parts = action.split(":")
                verb, args = parts[0], parts[1:]
                spot = "%s「%s」" % (where, action)
                if verb not in verbs:
                    self.err(spot + "：未知动词")
                    continue
                if verb == "shot" and len(args) >= 3 and args[2] == "Dialogue" and not typed:
                    self.err(spot + "：官方对话截图前要先 wait_dialogue_typed（官方逐字显示 40 字/秒，直接截会截在半句上）")
                if verb == "dialogue_advance" and args and is_num(args[0]) and float(args[0]) < 1 and not typed:
                    self.err(spot + "：单次推进（不到 1 秒只点一次确认）前要先 wait_dialogue_typed，否则这一下只是把正在打字的这句补完、不翻页")
                if verb == "wait_dialogue_typed":
                    typed = True
                elif verb in DIALOGUE_RESETTERS:
                    typed = False
                if verb == "assert":
                    counted += 1
                    if not args or args[0] not in asserts:
                        self.err(spot + "：未知断言名")
                    else:
                        self.assertion(spot, args)
                elif verb in ("shot", "burst"):
                    shots += 1
                    if len(args) < 2 or args[1] not in ("ui", "world"):
                        self.err(spot + "：截图种类只能是 ui / world")
                    if verb == "burst":
                        self.positional(spot, args[2:], [INT, NUM])
                else:
                    if verb in ctx["asserting"]:
                        counted += 1
                    last_teleport = self.verb(spot, verb, args, last_teleport)
            if counted + shots == 0:
                self.err(where + "：既没有断言也没有截图（每个自动步骤至少一条断言或一张截图）")
            if step.get("class") == "shot" and shots == 0:
                self.err(where + "：class=shot 却没有截图")

        covered = {}
        for row in table.get("coverage", []):
            cid = row.get("checklist")
            where = "覆盖行 %s" % cid
            if not is_checklist_id(cid):
                self.err(where + "：编号格式不对")
                continue
            if cid in covered:
                self.err(where + "：重复")
            covered[cid] = row
            kind = row.get("class")
            if kind not in ctx["classes"]:
                self.err(where + "：class 只能是 auto / shot / manual")
                continue
            if kind == "manual":
                if not any(word in (row.get("reason") or "") for word in ctx["manual_keywords"]):
                    self.err(where + "：「只能人工」的理由必须写明是手感、声音还是好不好玩")
                continue
            evidence = row.get("evidence", [])
            if not evidence:
                self.err(where + "：没有证据")
            has_shot = False
            for item in evidence:
                if item.startswith("SKY_AUTO_"):
                    step = next((s for s in steps if s.get("id") == item), None)
                    if step is None:
                        self.err("%s：证据步骤 %s 不存在" % (where, item))
                    elif any(a.split(":")[0] in ("shot", "burst") for a in step.get("actions", [])):
                        has_shot = True
                elif item.startswith("offline:"):
                    if kind != "auto":
                        self.err(where + "：offline 证据只能挂在自动断言行上（截图行要有真截图）")
                    if not (ROOT / item[len("offline:"):]).exists():
                        self.err("%s：离线证据 %s 的路径不存在" % (where, item))
                elif item not in ctx["registered"]:
                    self.err("%s：证据用例 %s 没登记进 GameplayCoverage.json" % (where, item))
            if kind == "shot" and not has_shot:
                self.err(where + "：截图行必须引用至少一个带截图的步骤")
        for step in steps:
            for cid in step.get("checklist", []):
                if is_checklist_id(cid) and cid not in covered:
                    self.err("步骤 %s 引用的清单编号 %s 不在覆盖表里" % (step.get("id"), cid))
        doc_ids = ctx["doc_ids"]
        for cid in sorted(covered):
            if cid.startswith("M_SKY_ISLAND_"):
                if cid not in ctx["manual_m"]:
                    self.err("覆盖行 %s：GameplayCoverage.json 里没有这个人工用例" % cid)
            elif doc_ids is not None and cid not in doc_ids:
                self.err("覆盖行 %s：天空岛人工清单里没有这个编号" % cid)
        if doc_ids is not None:
            for cid in sorted(doc_ids - set(covered)):
                self.err("天空岛人工清单 %s 没有归类（自动断言 / 截图给 AI 看 / 只能人工）" % cid)
        for cid in sorted(ctx["manual_m"] - set(covered)):
            self.err("GameplayCoverage.json 的人工用例 %s 没有在步骤表覆盖表里归类（自动断言 / 截图给 AI 看 / 只能人工）" % cid)
        return self.errors, self.warnings


def check(table, ctx):
    return Checker(table, ctx).run()


def step_of(table, sid):
    return next(step for step in table["steps"] if step["id"] == sid)


def replace_action(sid, old, new):
    def mutate(table, ctx):
        actions = step_of(table, sid)["actions"]
        actions[actions.index(old)] = new
    return mutate


def set_row(cid, key, value):
    def mutate(table, ctx):
        next(row for row in table["coverage"] if row["checklist"] == cid)[key] = value
    return mutate


def drop_row(cid):
    def mutate(table, ctx):
        table["coverage"] = [row for row in table["coverage"] if row["checklist"] != cid]
    return mutate


def set_actions(sid, actions):
    def mutate(table, ctx):
        step_of(table, sid)["actions"] = actions
    return mutate


def set_stage_apply(stage_id, old, new):
    def mutate(table, ctx):
        apply = next(s for s in table["stages"] if s["id"] == stage_id)["apply"]
        apply[apply.index(old)] = new
    return mutate


def drop_case(key, value):
    def mutate(table, ctx):
        ctx[key] = set(ctx[key]) - {value}
    return mutate


def drop_before(sid, anchor):
    """删掉 anchor 前面紧挨着的那条 wait_dialogue_typed；不是它就当锚点过期。"""
    def mutate(table, ctx):
        actions = step_of(table, sid)["actions"]
        i = actions.index(anchor)
        if i == 0 or actions[i - 1].split(":")[0] != "wait_dialogue_typed":
            raise ValueError(anchor)
        del actions[i - 1]
    return mutate


PROBES = (
    ("未知动词", replace_action("SKY_AUTO_BASE_PREV_QUIT_LOG", "assert:prev_log_quit", "asert:prev_log_quit")),
    ("步骤既无断言也无截图", set_actions("SKY_AUTO_REAL_CHARM", ["wait_real:1"])),
    ("跟居民说话前瞬移错站位", replace_action("SKY_AUTO_REAL_RESIDENT_TALK", "teleport:EnemySpawn_B:2.5:0", "teleport:POI_B:2.5:0")),
    ("选项文字写错字", replace_action("SKY_AUTO_REAL_PIGEON_LETTER", "choose_label:收下这封信|Keep the letter", "choose_label:收下这封新|Keep the latter")),
    ("标记名写错", replace_action("SKY_AUTO_REAL_DOCK_PANEL", "teleport:Search_A:1.6:0", "teleport:Search_AA:1.6:0")),
    ("TypeID 不是天空岛物品", replace_action("SKY_AUTO_REAL_CONSUMABLES", "give:500071:2", "give:500871:2")),
    ("assert:case 引用没映射的用例", replace_action("SKY_AUTO_LAND_HUD_CARD", "assert:case:SKY_STORY_OBJECTIVE", "assert:case:SKY_STORY_OBJECTIV")),
    ("人工理由不是手感 / 声音 / 好不好玩", set_row("2.13.2", "reason", "做不了")),
    ("截图步骤没有截图", set_actions("SKY_AUTO_REAL_COMPASS", ["teleport:PlayerSpawn:0:0", "use_compass"])),
    ("剧情动作名写错", set_stage_apply("both_beacons", "story:RepairWindBeacon", "story:RepairWindBeakon")),
    ("风晶灯 id 写错", set_stage_apply("ending_lamps", "lamp:Light_E", "lamp:Light_Z")),
    ("截图行挂离线证据", set_row("2.10.3", "evidence", ["SKY_AUTO_REAL_COMPASS", "offline:tests/SkyIslandFieldcraftGuard.py"])),
    ("离线证据路径不存在", set_row("2.11.4", "evidence", ["offline:tests/NoSuchAutotestGuard.py"])),
    ("采集点与瞬移标记对不上", replace_action("SKY_AUTO_REAL_GATHER_DOCK", "interact:gather:A1", "interact:gather:B1")),
    ("剧情旗标写错", replace_action("SKY_AUTO_NEW_S4_GUARD", "assert:flag:Telescope", "assert:flag:Telescop")),
    ("第一个 story 阶段不从 reset 起", set_stage_apply("new_save", "reset", "clear:D")),
    ("名单里的断言没实现", drop_case("assert_cases", "wind_gale")),
    ("步骤引用的清单编号没有覆盖行", drop_row("2.14.1")),
    ("箱子备选 TypeID 写错", replace_action("SKY_AUTO_END_ECHO_CLEAR", "assert:crate_has_any:500082+500071+500081", "assert:crate_has_any:500082+500071+500999")),
    ("物体名写错", replace_action("SKY_AUTO_REAL_LANTERN", "assert:object_present:SkyIslandLanternLight", "assert:object_present:SkyIslandLanternLite")),
    ("人工用例没有归类", drop_row("M_SKY_ISLAND_15")),
    ("Boss 种类写错", replace_action("SKY_AUTO_REAL_BOSS_FOREMAN", "wait_boss:foreman:30", "wait_boss:formean:30")),
    ("圆环覆盖率阈值放宽回旧口径", replace_action("SKY_AUTO_LAND_DOCK_WORLD", "assert:visible_min:ground_ring:0.6", "assert:visible_min:ground_ring:0.15")),
    ("取景瞬移的采集点写错", replace_action("SKY_AUTO_REAL_GATHER_GLOW_DAY", "teleport_view:SkyIslandGather_A2:11:0:2", "teleport_view:SkyIslandGather_A9:11:0:2")),
    ("环重播的物体名写错", replace_action("SKY_AUTO_BEACONS_EXITS", "ring_replay:SkyIslandExtractionRing_Region_D", "ring_replay:SkyIslandExtractionRing_Region_X")),
    ("对话截图前不等这句打完", drop_before("SKY_AUTO_BEACONS_FUZHOU", "shot:fuzhou_line3:ui:Dialogue")),
    ("单次推进落在逐字显示中途", drop_before("SKY_AUTO_END_BELLKEEPER", "dialogue_advance:0.4")),
    ("等打完的超时不是数字", replace_action("SKY_AUTO_ALT_BELLKEEPER", "wait_dialogue_typed:10", "wait_dialogue_typed:soon")),
    ("对话关键句的文字写错", replace_action("SKY_AUTO_END_BELLKEEPER", "assert:dialogue_line_contains:它会回来找你|it will come looking for you",
                                    "assert:dialogue_line_contains:它会回来找您|it will come looking for us")),
)


def main():
    table = json.loads(read(TABLE))
    ctx = load_context()
    errors, warnings = check(table, ctx)
    probes_ok = 0
    for label, mutate in PROBES:
        mutated_table, mutated_ctx = copy.deepcopy(table), dict(ctx)
        try:
            mutate(mutated_table, mutated_ctx)
        except (StopIteration, ValueError, KeyError):
            errors.append("反向检查的锚点过期（步骤表改了之后要同步本守卫）：" + label)
            continue
        probe_errors, _ = check(mutated_table, mutated_ctx)
        if probe_errors:
            probes_ok += 1
        else:
            errors.append("反向检查失效，破坏之后仍然全绿：" + label)
    if ctx["doc_ids"] is None:
        doc_probe = True
    else:
        mutated = copy.deepcopy(table)
        step_of(mutated, "SKY_AUTO_REAL_CHARM")["checklist"].append("2.17.99")
        mutated["coverage"].append({"checklist": "2.17.99", "class": "auto", "evidence": ["SKY_AUTO_REAL_CHARM"]})
        doc_probe = bool(check(mutated, ctx)[0])
        if not doc_probe:
            errors.append("反向检查失效：清单里不存在的编号没被拦下")
    if errors:
        print("SkyIslandAutotestTableGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    for warning in warnings:
        print("  WARN " + warning)
    partial = "" if ctx["doc_ids"] is not None else "；PARTIAL：天空岛人工清单不在本机（local-only），没核编号是否存在与清单是否全覆盖"
    print("SkyIslandAutotestTableGuard: PASS（%d 个阶段 / %d 步 / %d 条覆盖行；名单与实现一致、名字对得上生产；%d 条反向检查全部转红%s）"
          % (len(table["stages"]), len(table["steps"]), len(table["coverage"]), probes_ok + (1 if ctx["doc_ids"] is not None else 0), partial))
    return 0


if __name__ == "__main__":
    sys.exit(main())
