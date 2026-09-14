# -*- coding: utf-8 -*-
u"""天空岛选项必须先判断再挂，而不是先挂上再在回调里拒绝。

## 背景（2026-09-13）

owner 的原话是「选项太多，一上来这么多个太杂」。查下来比反馈说的更糟：**六个选项构造器
没有一个带条件**，`choices.Add` 一律执行，能不能做全丢进 `Select` 回调里判。于是——

- 新档走进归航钟庭就看得见「敲响归航钟」，点下去回一句「先恢复两端航标」；
- 交还种植记录之后，「交还种植记录」还挂在晴禾那儿，点了回「这段群岛见闻已经完成」；
- 群岛手记首页 6 项全部无条件，点进去是 20 行 `□ …（尚未收录）` 空占位。

`SkyIslandWorldStory.Add` 上那条「交还种植记录后，这一项也不该再挂在选项里」的注释
**写了三个月、没有对应代码**。本守卫就是让这句注释从此有牙。

## 钉住的四条

1. **单一事实来源**：能不能挂只问 `SkyIslandStoryRules.CanApply`，它与 `TryApply` 共用
   `Describe`，所以「挂不挂得出来」与「点了会不会被拒」不可能分叉。
2. **裸 `Add` 只有一个调用点**，就在 `AddIf` 里面。任何剧情动作都绕不过门。
3. **不挂灰掉的占位项**：做不了就整条不挂，「还差什么」进正文（`Hint`）。
   灰项和挂满一样吵，而且玩家还是会去点。
4. **已完成的动作不得再挂**：`CanApply` 在 `source.Has(flag)` 时返回 false，
   且**不给 blocker**——「已经完成」不是引导，是噪声。

反向检查在内存里恢复每一种旧写法，确保断言真的抓得住。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source

SKY = "DebugAndTools/SkyIsland/"
WORLD = SKY + "SkyIslandWorldStory.cs"
RULES = SKY + "SkyIslandStoryRules.cs"
# 2026-09-14 B 轮：噬风·回响的「引风」选项与它点下去那一次（选项接线与会话接线各在一个 partial 文件里）。
WORLD_ECHO = SKY + "SkyIslandWorldStoryEcho.cs"
SESSION_ECHO = SKY + "SkyIslandSessionEcho.cs"
PATHS = [WORLD, RULES, WORLD_ECHO, SESSION_ECHO]

# 裸 `Add(choices, ...)`：前面不能是 `f`（AddIf）也不能是 `.`（choices.Add）。
RAW_ADD = re.compile(r"(?<![A-Za-z0-9_.])Add\(choices")


def body_of(source, signature, closer):
    """取一个方法体：从签名到它自己那层大括号闭合。"""
    if signature not in source:
        return None
    i = source.index(signature)
    j = source.index("{", i)
    depth = 0
    k = j
    while k < len(source):
        if source[k] == "{":
            depth += 1
        elif source[k] == "}":
            depth -= 1
            if depth == 0:
                return source[i:k + 1]
        k += 1
    return None


def check(sources):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    world = clean_source(sources[WORLD])
    rules = clean_source(sources[RULES])

    # ---- 1) AddIf 是唯一的门，走 CanApply ----
    add_if = body_of(world, "private void AddIf(", "}")
    if add_if is None:
        require(False, WORLD + " 找不到 AddIf：选项的「先判断再挂」就是它实现的")
        add_if = ""
    require("SkyIslandStoryRules.CanApply(" in add_if,
            WORLD + " 的 AddIf 没有走 SkyIslandStoryRules.CanApply："
                    "另立判据的话，「挂不挂得出来」和「点了会不会被拒」迟早分叉")
    require("Hint(" in add_if,
            WORLD + " 的 AddIf 在判不过时没有 Hint(blocker)："
                    "前置没满足要把「还差什么」收进正文，那句话本身就是引导")

    # ---- 2) 不许挂灰掉的占位项 ----
    # 灰项和挂满一样吵，而且玩家还是会去点它。判不过就整条不挂。
    for token in ("interactable", "enabled = false", "Disabled", "grey", "gray"):
        require(token not in add_if,
                WORLD + " 的 AddIf 出现了 " + token + "：判不过就**整条不挂**，"
                        "不要留一个灰掉的占位按钮——灰项和挂满一样吵")

    # ---- 3) 裸 Add 只许有一个调用点，就在 AddIf 里 ----
    raw_sites = RAW_ADD.findall(world)
    inside = RAW_ADD.findall(add_if)
    require(len(raw_sites) == 1 and len(inside) == 1,
            WORLD + " 里 `Add(choices, ...)` 有 " + str(len(raw_sites)) + " 个调用点"
                    "（AddIf 内 " + str(len(inside)) + " 个）。剧情动作**只能**经 AddIf 挂："
                    "裸 Add 就是「先挂上再在回调里拒绝」的老路，"
                    "晴禾的「交还种植记录」交还之后还挂着就是这么来的。")

    # ---- 4) 具名挑战项：了结之后不再挂 ----
    challenge_sites = world.count("choices.Add(Challenge(")
    require(challenge_sites == world.count("if (ChallengeAvailable("),
            WORLD + " 挂 Challenge 的地方（" + str(challenge_sites) + " 处）与 "
                    "ChallengeAvailable 判断的数量对不上：和解**或**战胜之后再挂挑战项，"
                    "点了只会回一句拒绝")
    avail = body_of(world, "private bool ChallengeAvailable(", "}") or ""
    for token in ("ZhelingResolved", "BellKeeperResolved"):
        require(token in avail,
                WORLD + " 的 ChallengeAvailable 没读 " + token + "："
                        "了结与否只有这一个事实源，不许另立判据")

    # ---- 5) 噬风：没打 + 航标没亮，两种都不挂死按钮 ----
    storm = body_of(world, "private void StormChoice(", "}") or ""
    require("if (session.StormResolved) return;" in storm,
            WORLD + " 的 StormChoice 打完还挂：点了只会回一句「风已经散了」")
    require("Hint(" in storm and "if (!session.BothBeaconsLit)" in storm,
            WORLD + " 的 StormChoice 在航标没亮时没有早退 + Hint："
                    "「还差什么」要进正文，不是留一个点不动的按钮")

    # ---- 6) 规则侧：已完成 = 不挂，且不给 blocker ----
    can_apply = body_of(rules, "internal static bool CanApply(", "}")
    if can_apply is None:
        require(False, RULES + " 找不到 CanApply")
        can_apply = ""
    require("if (source.Has(flag)) return false;" in can_apply,
            RULES + " 的 CanApply 没有在「已经做完」时返回 false："
                    "这正是 SkyIslandWorldStory.Add 上那条写了三个月没实现的注释")
    require("blocker = null;" in can_apply,
            RULES + " 的 CanApply 没把 blocker 先置空：已完成时不该给引导语，"
                    "「这段群岛见闻已经完成」不是引导，是噪声")
    # 已完成的早退必须排在 required 之前，否则做完了还会冒出一句「还差什么」。
    if "if (source.Has(flag)) return false;" in can_apply and "required != null" in can_apply:
        require(can_apply.index("if (source.Has(flag)) return false;")
                < can_apply.index("required != null"),
                RULES + " 的 CanApply 把「已完成」判在 required 之后：做完了还会冒出一句「还差什么」")

    # ---- 7) 两条路径都只认一份 Describe ----
    try_apply = body_of(rules, "internal static bool TryApply(", "}") or ""
    for name, body in (("TryApply", try_apply), ("CanApply", can_apply)):
        require("Describe(source, action," in body,
                RULES + " 的 " + name + " 没有走 Describe：判断与执行必须共用同一份判据")

    # ---- 8) 手记分层：首页不许再平铺 ----
    hub = body_of(world, "private void OpenJournal()", "}") or ""
    require(hub.count("choices.Add(") == 2,
            WORLD + " 的手记首页有 " + str(hub.count("choices.Add(")) + " 项，应为 2 项子页入口。"
                    "旧版 6 项平铺，新档点进去是 20 行「尚未收录」空占位")
    for sub in ("private void OpenJournalPeople()", "private void OpenJournalIsles()"):
        body = body_of(world, sub, "}") or ""
        require("BackToJournal()" in body,
                WORLD + " 的 " + sub + " 没有「返回手记」：二级子菜单必须能回上一层")

    # ---- 9) 噬风·回响：「引风」先判断再挂，挂与点共用同一份判据（五项全在规则里） ----
    world_echo = clean_source(sources[WORLD_ECHO])
    session_echo = clean_source(sources[SESSION_ECHO])
    echo_choice = body_of(world_echo, "private void StormEchoChoice(", "}") or ""
    gate_at = echo_choice.find("if (!session.CanSummonStormEcho(out blocker))")
    add_at = echo_choice.find("choices.Add(")
    require(0 <= gate_at < add_at,
            WORLD_ECHO + " 的引风选项没有先问 session.CanSummonStormEcho 再挂：退回了「先挂上再在回调里拒绝」")
    require("Hint(blocker);" in echo_choice[gate_at:add_at] if gate_at >= 0 and add_at > gate_at else False,
            WORLD_ECHO + " 的引风选项挂不出来时没有 Hint(blocker)：「还差什么」要进正文")
    require("session.TryBeginStormEcho(out message)" in echo_choice,
            WORLD_ECHO + " 的引风选项点下去没有交给会话的 TryBeginStormEcho")
    for token in ("interactable", "enabled = false", "Disabled", "grey", "gray"):
        require(token not in echo_choice, WORLD_ECHO + " 的引风选项出现了 " + token + "：挂不出来就整条不挂")
    search_e = world.split('case "Search_E":', 1)[1].split("case ", 1)[0] if 'case "Search_E":' in world else ""
    require("StormEchoChoice(choices);" in search_e,
            WORLD + " 的鸣风栈道装置（Search_E）没有挂引风选项")
    can = body_of(session_echo, "internal bool CanSummonStormEcho(out string blocker)", "}") or ""
    require("SkyIslandStoryRules.CanSummonStormEcho(" in can,
            SESSION_ECHO + " 的 CanSummonStormEcho 没有走 SkyIslandStoryRules.CanSummonStormEcho：挂与点会分叉")
    begin = body_of(session_echo, "internal bool TryBeginStormEcho(out string message)", "}") or ""
    judged = begin.find("if (!CanSummonStormEcho(out blocker))")
    reserved = begin.find("TryReserve(")
    require(0 <= judged < reserved,
            SESSION_ECHO + " 的 TryBeginStormEcho 没有先过同一份判据再预留风晶")
    rule = body_of(rules, "internal static bool CanSummonStormEcho(", "}") or ""
    for token in ("data.StormResolved", "data.Has(SkyIslandStoryFlag.Ending)", "if (usedThisRaid) return false;",
                  "coreCarried", "windcrystals < StormEchoWindcrystalCost"):
        require(token in rule, RULES + " 的 CanSummonStormEcho 缺一项判据：" + token)
    return errors


def main():
    sources = {}
    for rel in PATHS:
        path = ROOT / rel
        if not path.is_file():
            print("SkyIslandChoiceGateGuard: FAIL - 找不到 " + rel)
            return 1
        sources[rel] = path.read_text(encoding="utf-8-sig")

    errors = check(sources)

    probes = [
        # 退回「先挂上再在回调里拒绝」
        (WORLD, "SkyIslandStoryRules.CanApply(story.Current, action, out blocker)", "true"),
        # 判不过改挂灰项
        (WORLD, "            Hint(blocker);", "            Add(choices, label, action);"),
        # 晴禾的交还项退回裸 Add（就是本轮查出来的那处真实遗漏）
        (WORLD, 'AddIf(choices, L10n.T("交还种植记录"', 'Add(choices, L10n.T("交还种植记录"'),
        # 挑战项不再判「已了结」
        (WORLD, 'if (ChallengeAvailable("Zheling"))', "if (true)"),
        (WORLD, "if (string.Equals(id, \"Zheling\", StringComparison.Ordinal)) return !data.ZhelingResolved;",
                "if (string.Equals(id, \"Zheling\", StringComparison.Ordinal)) return true;"),
        # 噬风打完还挂
        (WORLD, "if (session.StormResolved) return;", ""),
        # 手记首页退回平铺（多挂一项就该红）
        (WORLD, 'presentation.Show(L10n.T("群岛手记", "Archipelago journal"),',
                'choices.Add(BackToJournal()); '
                'presentation.Show(L10n.T("群岛手记", "Archipelago journal"),'),
        # 子页回不去
        (WORLD, "choices.Add(BackToJournal());", ""),
        # 已完成还能挂
        (RULES, "if (source.Has(flag)) return false;", ""),
        # 已完成时还给一句「还差什么」
        (RULES, "        blocker = null;", "        blocker = string.Empty;"),
        # CanApply 另立判据，不走 Describe
        (RULES, "if (!Describe(source, action, out flag, out required, out message)) return false;\n            if (source.Has(flag)) return false;",
                "if (source.Has(flag)) return false;"),
        # 噬风·回响：引风选项不判就挂 / 挂不出来不留「还差什么」/ 会话另立判据 / 点下去不先判 / 规则漏掉「本趟一次」
        (WORLD_ECHO, "if (!session.CanSummonStormEcho(out blocker))", "if (false)"),
        (WORLD_ECHO, "                Hint(blocker);\n", ""),
        (SESSION_ECHO, "return SkyIslandStoryRules.CanSummonStormEcho(", "return true || SkyIslandStoryRules.CanSummonStormEchoX("),
        (SESSION_ECHO, "if (!CanSummonStormEcho(out blocker))", "if (false)"),
        (RULES, "if (usedThisRaid) return false;", ""),
        (WORLD, "StormEchoChoice(choices); break;", "break;"),
    ]
    for path, before, after in probes:
        if before not in sources[path]:
            errors.append(u"反向检查锚点失效：" + path + " -> " + before[:60])
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        if not check(altered):
            errors.append(u"未拦截旧写法：" + path + " -> " + before[:60])

    if errors:
        print("SkyIslandChoiceGateGuard: FAIL\n  " + "\n  ".join(errors))
        return 1
    print("SkyIslandChoiceGateGuard: PASS（先判断再挂 + 不挂灰项 + 已完成不再挂 + "
          "手记两层；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
