#!/usr/bin/env python3
"""
ModeHActionLayoutGuard — Mode H 动作区布局守卫。

背景（CR-2026-09-04）：
Mode H 的模态 surface（ZombieModeUIHelper.CreateModalSurface）上**没有任何**
Mask / RectMask2D，越界的子物体不会被裁掉，而是照常绘制到面板外、屏幕外。
而 `CreateActions` 原本是无界的单行居中平铺：赔率页把「押品格」也塞进动作行，
格数 = 仓库前 40 格的非空格数，仓库里有 4 件东西就把排在最后的「锁盘」推出屏幕。
赔率页是 `ClaimModalInput` 的 timeScale=0 模态页、没有关闭按钮、
`OddsPreview` 唯一的玩家侧出边就是锁盘 —— 玩家只能弃局。

不变式：
1. 两处动作区（ModeHUIPages.CreateActions / ModeHRecoveryPanel.RebuildActions）
   都必须按 `MaxSingleRowActions` 换行，不得再出现无界单行平铺；
2. 押品格必须进 `RealStakeSlots`（独立滚动区），不得进 `page.Actions`；
3. 押品格渲染必须有滚动兜底（ScrollRect + RectMask2D），不得硬截断；
4. 卡片网格必须避让底部动作行（共用 `ActionBandReserve` 常量），
   否则入口页第 2 行选秀卡会压在按钮底下。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

GUARD = "ModeHActionLayoutGuard"

UI_PAGES = os.path.join(REPO_ROOT, "ModeH", "ModeHUIPages.cs")
RECOVERY = os.path.join(REPO_ROOT, "ModeH", "ModeHRecoveryPanel.cs")
MATCH_FLOW = os.path.join(REPO_ROOT, "ModeH", "ModeHRuntimeModule_MatchFlow.cs")


def read_text(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            return handle.read()
    except OSError:
        return None


def strip_cs_comments(code):
    code = re.sub(r"/\*.*?\*/", "", code, flags=re.S)
    code = re.sub(r"//[^\n]*", "", code)
    return code


def load(path, errors):
    text = read_text(path)
    if text is None:
        errors.append("[File] 缺少 " + os.path.relpath(path, REPO_ROOT))
        return None
    return strip_cs_comments(text)


def extract_method(code, signature_pattern):
    """从签名匹配处截到同缩进的收尾大括号。够用即可，不做完整 C# 解析。"""
    match = re.search(signature_pattern, code)
    if match is None:
        return None
    tail = code[match.end():]
    end = tail.find("\n        }")
    if end < 0:
        return tail
    return tail[:end]


def check_actions_wrap(errors):
    """两处动作区都必须换行，不得无界单行。"""
    pages = load(UI_PAGES, errors)
    recovery = load(RECOVERY, errors)

    if pages is not None:
        if "MaxSingleRowActions" not in pages:
            errors.append(
                "[换行] ModeHUIPages 必须定义并使用 MaxSingleRowActions "
                "作为单行动作区的按钮数上限")
        body = extract_method(pages, r"private static void CreateActions\(")
        if body is None:
            errors.append("[换行] 找不到 ModeHUIPages.CreateActions")
        else:
            if "MaxSingleRowActions" not in body:
                errors.append(
                    "[换行] CreateActions 未按 MaxSingleRowActions 分行；"
                    "无界单行平铺会把最后一颗按钮推出屏幕且不会被裁剪")
            if "% perRow" not in body or "/ perRow" not in body:
                errors.append(
                    "[换行] CreateActions 必须按 perRow 取列/行（i % perRow、i / perRow）")
            if re.search(r"startX \+ i \* \(ActionSize\.x", body):
                errors.append(
                    "[换行] CreateActions 仍在用 `startX + i * step` 的无界单行公式")

    if recovery is not None:
        body = extract_method(recovery, r"private void RebuildActions\(|private void RebuildActions")
        if body is None:
            errors.append("[换行] 找不到 ModeHRecoveryPanel.RebuildActions")
        else:
            if "MaxSingleRowActions" not in body:
                errors.append(
                    "[换行] 恢复壳 RebuildActions 未按 MaxSingleRowActions 分行；"
                    "恢复壳是应急界面，按钮被推出屏幕等于补救入口消失")
            if re.search(r"startX \+ i \* \(ActionSize\.x", body):
                errors.append(
                    "[换行] 恢复壳 RebuildActions 仍在用无界单行公式")


def check_stake_slots_not_in_actions(errors):
    """押品格必须走独立滚动区。"""
    pages = load(UI_PAGES, errors)
    flow = load(MATCH_FLOW, errors)

    if pages is not None:
        if "public List<ModeHActionData> RealStakeSlots" not in pages:
            errors.append(
                "[押品格] ModeHPageContent 必须提供 RealStakeSlots 承载押品格，"
                "它的数量随玩家仓库变化、无上界")
        if "CreateRealStakeSlots" not in pages:
            errors.append("[押品格] 缺少 CreateRealStakeSlots 渲染函数")
        else:
            body = extract_method(pages, r"private static void CreateRealStakeSlots\(")
            if body is None:
                errors.append("[押品格] 找不到 CreateRealStakeSlots 方法体")
            else:
                if "ScrollRect" not in body or "RectMask2D" not in body:
                    errors.append(
                        "[押品格] CreateRealStakeSlots 必须提供 ScrollRect + RectMask2D 滚动兜底，"
                        "不得硬截断（仓库可占满 40 格）")

    if flow is not None:
        body = extract_method(flow, r"private void AppendRealStakeLinesAndActions\(")
        if body is None:
            errors.append("[押品格] 找不到 AppendRealStakeLinesAndActions")
        else:
            if "page.RealStakeSlots.Add" not in body:
                errors.append(
                    "[押品格] 押品格必须写入 page.RealStakeSlots")
            if "page.Actions.Add" in body:
                errors.append(
                    "[押品格] 押品格不得写入 page.Actions —— 那是有界的单行动作区，"
                    "会把「锁盘」挤出屏幕")


def check_card_grid_avoids_action_band(errors):
    """卡片网格必须避让动作行。"""
    pages = load(UI_PAGES, errors)
    if pages is None:
        return

    if "ActionBandReserve" not in pages:
        errors.append(
            "[卡片] 必须用统一的 ActionBandReserve 常量表示底部动作行保留高度")
        return

    body = extract_method(pages, r"private static void CreateCardGrid\(")
    if body is None:
        errors.append("[卡片] 找不到 CreateCardGrid")
        return
    if "ActionBandReserve" not in body:
        errors.append(
            "[卡片] CreateCardGrid 必须按 ActionBandReserve 计算可用高度；"
            "否则入口页 5 张选秀卡的第 2 行会画在动作按钮底下")
    if "maxRows" not in body:
        errors.append(
            "[卡片] CreateCardGrid 必须按可用高度推出 maxRows 并据此加列，"
            "不能固定 3 列无脑往下堆")

    # 行列表与卡片网格必须共用同一个保留高度常量，避免两处漂移
    if re.search(r"ModeHUI\.SafeMargin \+ 96f", body):
        errors.append(
            "[卡片] 不得就地重写保留高度字面量，必须引用 ActionBandReserve")


def main():
    errors = []
    check_actions_wrap(errors)
    check_stake_slots_not_in_actions(errors)
    check_card_grid_avoids_action_band(errors)

    if errors:
        print("{}: FAIL ({} errors)".format(GUARD, len(errors)))
        for line in errors:
            print("  - " + line)
        return 1
    print(GUARD + ": PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
