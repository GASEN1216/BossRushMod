# -*- coding: utf-8 -*-
"""F3 自动点官方对话选项的时机（2026-09-15）。

官方 DialogueUI.DoMultipleChoice（IL 实查）：先 DisplayOptions——选项控件立刻激活、choiceListFadeGroup 淡入、
Menu 获得焦点——淡入完才进 WaitForChoice，把 confirmedChoice 清成 -1、waitingForChoice 置真，逐帧等 confirmedChoice>=0。
淡入期间点的选项会被清掉，对话一直挂着。第三轮全自动验收岛内 5 条红（浮舟、晴禾、折翎、英文苇白、对话演练）都是这个；
眠苔、苇白碰巧点在淡入之后才过。同一轮还有两处读得太早 / 判得太宽：

- SKY_AUTO_LAMPS_STAGE 剧情阶段刚点完灯就判风级，fieldcraft 每 0.5 游戏秒才重采一次，读到点灯前的旧样本；
- 截图溢出探针把官方对话框里的字（中文也在内）全判红，是官方布局的误报。

守的是：
1. OfficialDialogueWaitingForChoice（F3GameplayValidationScenes.cs，与「点击继续」同属官方等待点探测；不放 runner 宿主文件，
   那份是 ModBehaviour partial、有行数预算）反射官方私有字段 waitingForChoice，取不到返回 null（不静默当成 true / false）；
2. 自动验收 dialogue_choose 只在 waiting != false 时点，点完核对被吃掉（最多 3 次），吃不掉记 choice_not_consumed；
3. Dev 演练弹出后同样等 waiting 再点，没等到记 official_not_waiting_for_choice，不去点一个必丢的选项；
4. 溢出探针对官方对话框只列进 truncated、不判红，自己的 HUD 与面板照旧判红；
5. 步骤表里 LAMPS_STAGE 在判 SKY_LAMPS_WIND 之前先等至少两个 fieldcraft 采样间隔；
6. （2026-09-15 第五轮）对话截图截在逐字显示中途：OfficialDialogueLineShown 读官方私有字段 continueIndicator（WaitForConfirm 期间才激活），
   取不到返回 null；wait_dialogue_typed（AutotestWaitDialogueTyped，在截图文件里）同时认「本句打完」与「选项在等玩家选」，只等不点，满足后再等一帧。

反向检查在内存里逐条破坏，必须转红。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

HELPER = "DebugAndTools/F3GameplayValidationScenes.cs"
ACTIONS = "DebugAndTools/F3GameplayValidationAutotestActions.cs"
DRILL = "DebugAndTools/F3GameplayValidationSkyIslandDrill.cs"
CAPTURE = "DebugAndTools/F3GameplayValidationAutotestCapture.cs"
TABLE = "Assets/Data/SkyIslandAutotest.json"
RULES = "DebugAndTools/SkyIsland/SkyIslandFieldcraftRules.cs"


def method_body(code, signature):
    start = code.find(signature)
    if start < 0:
        return None
    brace = code.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(code)):
        if code[i] == "{":
            depth += 1
        elif code[i] == "}":
            depth -= 1
            if depth == 0:
                return code[brace:i + 1]
    return None


def seconds(action):
    try:
        return float(action.split(":", 1)[1])
    except (IndexError, ValueError):
        return 0.0


def check(sources, table):
    errors = []

    helper = method_body(sources[HELPER], "private static bool? OfficialDialogueWaitingForChoice()")
    if helper is None:
        errors.append("F3GameplayValidationScenes.cs 缺少 OfficialDialogueWaitingForChoice")
    else:
        if '"waitingForChoice"' not in helper:
            errors.append("是否在等玩家选必须读官方私有字段 waitingForChoice")
        if "if (_officialWaitingForChoiceField == null) return null;" not in helper:
            errors.append("取不到 waitingForChoice 时必须返回 null，交给调用方退回旧做法，不能静默当真或当假")

    shown = method_body(sources[HELPER], "private static bool? OfficialDialogueLineShown()")
    if shown is None:
        errors.append("F3GameplayValidationScenes.cs 缺少 OfficialDialogueLineShown")
    else:
        resolve = method_body(sources[HELPER], "private static void ResolveOfficialLineFields()") or ""
        if '"continueIndicator"' not in resolve:
            errors.append("本句是否打完必须读官方私有字段 continueIndicator（WaitForConfirm 期间才激活）")
        if "if (_officialContinueIndicatorField == null) return null;" not in shown:
            errors.append("取不到 continueIndicator 时必须返回 null，交给调用方退回读 TMP，不能静默当真或当假")
    typed = method_body(sources[CAPTURE], "private IEnumerator AutotestWaitDialogueTyped(")
    if typed is None:
        errors.append("缺少 AutotestWaitDialogueTyped")
    else:
        if "OfficialDialogueLineShown()" not in typed or "OfficialDialogueWaitingForChoice() == true" not in typed:
            errors.append("wait_dialogue_typed 要同时认「本句打完」与「选项在等玩家选」")
        if ".Confirm(" in typed or "OnPointerClick" in typed:
            errors.append("wait_dialogue_typed 只等不点：打字途中点确认只会补完这句，截到的不是自然打完的画面")
        met = typed.find("if (met)")
        if met < 0 or "yield return null;" not in typed[met:]:
            errors.append("满足之后要再等一帧让状态渲染出来再截图")

    choose = method_body(sources[ACTIONS], "private IEnumerator AutotestDialogueChoose(")
    if choose is None:
        errors.append("缺少 AutotestDialogueChoose")
    else:
        poll = choose.find("waiting = OfficialDialogueWaitingForChoice();")
        gate = choose.find("if (target != null && waiting != false) break;")
        refuse = choose.find('if (waiting == false) { AutotestFail(record, "action:dialogue_choose", "official_not_waiting_for_choice:"')
        click = choose.find("target.OnPointerClick(null);")
        if not (0 <= poll < gate < refuse < click):
            errors.append("dialogue_choose 必须先等官方 waitingForChoice、没等到就记红，之后才点选项")
        if "OfficialDialogueWaitingForChoice() == true) yield return null;" not in choose or "clicks < 3" not in choose:
            errors.append("dialogue_choose 点完必须核对被官方吃掉，没吃掉再点（有上限）")
        if '"choice_not_consumed:"' not in choose:
            errors.append("点了 3 次仍没被吃掉必须记 choice_not_consumed")

    drill = method_body(sources[DRILL], "private IEnumerator RunSkyIslandDrillDialogue()")
    if drill is None:
        errors.append("缺少 RunSkyIslandDrillDialogue")
    else:
        poll = drill.find("waiting = OfficialDialogueWaitingForChoice();")
        gate = drill.find("if (target != null && waiting != false) break;")
        click_gate = drill.find("if (target != null && waiting != false)\n")
        if click_gate < 0:
            click_gate = drill.find("if (target != null && waiting != false)\r\n")
        click = drill.find("target.OnPointerClick(null);")
        if not (0 <= poll < gate < click_gate < click):
            errors.append("演练弹出后必须等官方 waitingForChoice 再点选项")
        if 'errors.Add("official_not_waiting_for_choice");' not in drill:
            errors.append("演练没等到官方在等玩家选时必须记 official_not_waiting_for_choice")

    probes = method_body(sources[CAPTURE], "private static int CollectAutotestTextProbes(")
    if probes is None:
        errors.append("缺少 CollectAutotestTextProbes")
    else:
        dialogue = probes.find("text.transform.IsChildOf(officialDialogue)) truncated.Add(path);")
        own = probes.find("else if (outside) overflowing.Add(path);")
        if not (0 <= dialogue < own):
            errors.append("溢出探针对官方对话框只列进 truncated，自己的界面照旧判红")
        if "Dialogues.DialogueUI.instance" not in probes:
            errors.append("溢出探针要按官方 DialogueUI 实例的层级认官方对话框，不按名字猜")

    interval = re.search(r"TickInterval\s*=\s*([0-9.]+)f", sources[RULES])
    tick = float(interval.group(1)) if interval else 0.5
    step = next((s for s in table.get("steps", []) if s.get("id") == "SKY_AUTO_LAMPS_STAGE"), None)
    if step is None:
        errors.append("步骤表缺少 SKY_AUTO_LAMPS_STAGE")
    else:
        actions = step.get("actions", [])
        judge = actions.index("assert:case:SKY_LAMPS_WIND") if "assert:case:SKY_LAMPS_WIND" in actions else -1
        waited = sum(seconds(a) for a in actions[:max(judge, 0)] if a.startswith("wait_real:"))
        if judge < 0 or waited < 2 * tick:
            errors.append("LAMPS_STAGE 判风级前必须先等至少两个 fieldcraft 采样间隔（%.1f 秒），现在等了 %.1f 秒" % (2 * tick, waited))
    return errors


def reverse_checks(sources, table):
    cases = (
        ("辅助不读 waitingForChoice", HELPER, '"waitingForChoice"', '"confirmed"'),
        ("取不到字段当真", HELPER, "if (_officialWaitingForChoiceField == null) return null;",
         "if (_officialWaitingForChoiceField == null) return true;"),
        ("验收见到选项就点", ACTIONS, "if (target != null && waiting != false) break;", "if (target != null) break;"),
        ("验收不核对吃掉", ACTIONS, "OfficialDialogueWaitingForChoice() == true) yield return null;", "false) yield return null;"),
        ("演练见到选项就点", DRILL, "if (target != null && waiting != false) break;", "if (target != null) break;"),
        ("演练没等到也不记红", DRILL, 'errors.Add("official_not_waiting_for_choice");', ""),
        ("官方对话框照旧判红", CAPTURE, "text.transform.IsChildOf(officialDialogue)) truncated.Add(path);",
         "text.transform.IsChildOf(officialDialogue)) overflowing.Add(path);"),
        ("本句打完不读 continueIndicator", HELPER, '"continueIndicator"', '"confirmed"'),
        ("取不到 continueIndicator 当真", HELPER, "if (_officialContinueIndicatorField == null) return null;",
         "if (_officialContinueIndicatorField == null) return true;"),
        ("等打完时顺手点确认", CAPTURE, 'probe = "tmp_fallback";', 'probe = "tmp_fallback"; DialogueUI.instance.Confirm();'),
    )
    failures = []
    for name, rel, old, new in cases:
        if old not in sources[rel]:
            failures.append("反向检查「" + name + "」找不到破坏点: " + old)
            continue
        mutated = dict(sources)
        mutated[rel] = sources[rel].replace(old, new, 1)
        if not check(mutated, table):
            failures.append("反向检查「" + name + "」没有转红")
    shortened = json.loads(json.dumps(table))
    for step in shortened.get("steps", []):
        if step.get("id") == "SKY_AUTO_LAMPS_STAGE":
            step["actions"] = [a for a in step["actions"] if not a.startswith("wait_real:")]
    if not check(sources, shortened):
        failures.append("反向检查「LAMPS_STAGE 不等新样本」没有转红")
    return failures


def main():
    sources = {rel: clean_source((ROOT / rel).read_text(encoding="utf-8")) for rel in (HELPER, ACTIONS, DRILL, CAPTURE, RULES)}
    table = json.loads((ROOT / TABLE).read_text(encoding="utf-8"))
    errors = check(sources, table) or reverse_checks(sources, table)
    if errors:
        for error in errors:
            print("FAIL: " + error)
        return 1
    print("PASS: F3 点官方对话选项等 waitingForChoice、点完核对被吃掉；演练同样等；官方对话框溢出只列不判；LAMPS_STAGE 等新风级样本；对话截图等本句打完（反向检查 11/11 转红）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
