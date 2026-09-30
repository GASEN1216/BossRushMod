# -*- coding: utf-8 -*-
u"""NPC 交互组只留一个头顶气泡；运行时挂的官方任务给予者点了要能打开任务页（owner 2026-09-30）。

1. 官方只在宿主 Awake 里关掉组员的交互气泡、把偏移对齐宿主；运行时后加的组员赶不上。
   `NPCInteractionGroupHelper.AddSubInteractable` 必须自己补：setup 之前继承宿主偏移，setup 之后
   `MarkerActive = false`，两步都在重新激活（触发 Awake / Start）之前。
2. `AddComponent<QuestGiver>` 拿到字段初值 `finishWhenTimeOut = true`、读条 0 秒：任务页开出来的下一帧交互就
   「读条完成」自动结束，官方 OnInteractStop 把页关掉，玩家看到的是点了没反应。setup 必须写 false；
   偏移不再写死 0.1 米（「!」会挂在腰上、交互气泡落在脚边）。

反向检查在内存里恢复错误写法，确认每条都抓得住。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HELPER = ROOT / "Integration/Utils/NPCInteractionGroupHelper.cs"
GIVERS = ROOT / "SkyIsland/SkyIslandOfficialQuestGivers.cs"


def block(text, signature):
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    depth = 0
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return ""


def strip_comments(text):
    return re.sub(r"//[^\n]*", "", text)


def check(helper, givers):
    errors = []
    add = strip_comments(block(helper, "public static T AddSubInteractable<T>("))
    inherit = add.find("component.interactMarkerOffset = owner.interactMarkerOffset;")
    setup = add.find("setup?.Invoke(component);")
    hide = add.find("component.MarkerActive = false;")
    reactivate = add.find("childObj.SetActive(true);")
    if min(inherit, setup, hide, reactivate) < 0 or not (inherit < setup < hide < reactivate):
        errors.append("AddSubInteractable 必须先继承宿主偏移、再 setup、再关组员气泡，最后才重新激活")
    if "ReferenceEquals(field.GetValue(candidate), groupList)" not in strip_comments(block(helper, "private static InteractableBase FindGroupOwner(")):
        errors.append("宿主必须按「持有同一份组员清单」认，不能随便取 parent 上第一个交互体")
    attach = strip_comments(block(givers, "private static bool Attach(Transform parent, List<InteractableBase> group, int giverId)"))
    if "component.finishWhenTimeOut = false;" not in attach:
        errors.append("运行时挂的 QuestGiver 必须 finishWhenTimeOut = false，否则任务页开一帧就被关掉")
    if "interactMarkerOffset = new Vector3(0f, 0.1f, 0f)" in attach:
        errors.append("QuestGiver 偏移不得写死 0.1 米：应继承宿主，让「!」在宿主气泡正上方")
    return errors


def main():
    helper = HELPER.read_text(encoding="utf-8-sig")
    givers = GIVERS.read_text(encoding="utf-8-sig")
    errors = check(helper, givers)
    probes = [
        (helper, "                component.MarkerActive = false;\n", "", "helper"),
        (helper, "                    component.interactMarkerOffset = owner.interactMarkerOffset;\n", "", "helper"),
        (givers, "                component.finishWhenTimeOut = false;\n", "", "givers"),
        (givers, "                component.finishWhenTimeOut = false;\n",
         "                component.interactMarkerOffset = new Vector3(0f, 0.1f, 0f);\n                component.finishWhenTimeOut = false;\n", "givers"),
    ]
    for text, old, new, which in probes:
        norm = text.replace("\r\n", "\n")
        if old not in norm:
            errors.append("反向探针锚点不存在：" + old.strip())
            continue
        broken = norm.replace(old, new, 1)
        result = check(broken, givers.replace("\r\n", "\n")) if which == "helper" else check(helper.replace("\r\n", "\n"), broken)
        if not result:
            errors.append("反向探针没转红：" + old.strip())
    if errors:
        for e in errors:
            print("FAIL " + e)
        return 1
    print("PASS NPCGroupMarkerAndQuestGiverGuard: one marker per group, quest page stays open")
    return 0


if __name__ == "__main__":
    sys.exit(main())
