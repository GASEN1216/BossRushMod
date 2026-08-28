#!/usr/bin/env python3
"""
PetNestRevealIdempotencyGuard — 遗种巢演出层幂等守卫（实施计划 步骤 11）。

不变式：
- 演出层**只回放已 commit 的结果**：两个 View 都不得出现任何 roll 符号
  （Random.value / Random.Range / Roll*）与任何写档符号（Commit / Store / SavesSystem /
  StageCommit）。演出中断（切图、关面板、宿主销毁）不得影响已落档的结果；
- 唯一允许的服务层写调用是 MarkRevealed——那是"翻完牌把记录移出待翻列表"，不是结算；
- 演出可中断且幂等：Play/Stop 成对，Stop 幂等，OnDestroy 清实例；
- 演出层用自建节奏，不复用官方 LotteryBox / DeathLottery 本体
  （它们的奖池与开启全是私有序列化字段，注入不进自定义内容）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import read_petnest, report, strip_cs_comments  # noqa: E402

GUARD = "PetNestRevealIdempotencyGuard"

VIEWS = ["PetNestHatchRevealView.cs", "PetNestExpeditionRevealView.cs"]

FORBIDDEN_ROLL = ["Random.value", "Random.Range", "RollNewPet", "RollCashReward", "RollLoot"]
FORBIDDEN_WRITE = ["StageCommit(", "PetNestService.Commit(", "SavesSystem",
                   "PetNestPersistence", "TryAddPet(", "TrySpendSouls(", "AddSouls("]
# 官方演出本体：只借节奏语言，不复用实现
FORBIDDEN_OFFICIAL = ["LotteryBox", "DeathLottery", "ItemPicker"]


def check_view(errors, name):
    text = read_petnest(name)
    if text is None:
        errors.append("[File] 缺少 PetNest/" + name)
        return
    code = strip_cs_comments(text)

    for symbol in FORBIDDEN_ROLL:
        if symbol in code:
            errors.append("[只回放] " + name + " 不得 roll: " + symbol)
    for symbol in FORBIDDEN_WRITE:
        if symbol in code:
            errors.append("[只回放] " + name + " 不得写档: " + symbol)
    for symbol in FORBIDDEN_OFFICIAL:
        if symbol in code:
            errors.append("[自建节奏] " + name + " 不得复用官方演出本体: " + symbol)

    # 可中断且幂等
    if "internal static void Stop()" not in code:
        errors.append("[幂等] " + name + " 缺少 Stop() 中断入口")
    if "if (_instance == null) return;" not in code:
        errors.append("[幂等] " + name + " 的 Stop 必须幂等早返")
    if not re.search(r"private void OnDestroy\(\)[\s\S]{0,200}?_instance = null;", code):
        errors.append("[幂等] " + name + " 的 OnDestroy 必须清实例引用")
    if "internal static void ResetStaticCaches()" not in code:
        errors.append("[清理] " + name + " 缺少 ResetStaticCaches()")

    # 必须接管输入：raycaster 关掉的话遮罩只是"看起来"挡住了，
    # 下面仍然活着的面板照样能被盲点到（孵化时会静默连吞第二枚蛋）
    if "BossRushUILayers.PetNestModal, true)" not in code:
        errors.append("[输入] " + name + " 的 canvas 必须 interactive=true，否则遮罩不拦点击")
    if "ZombieModeUIHelper.ClaimModalInput(" not in code:
        errors.append("[输入] " + name + " 必须占用模态输入 lease")
    if "_modalLease.Release();" not in code:
        errors.append("[输入] " + name + " 的模态 lease 必须成对释放")
    if not re.search(r"private void OnDestroy\(\)[\s\S]{0,200}?ReleaseLease\(\)", code):
        errors.append("[输入] " + name + " 的 OnDestroy 必须兜底释放 lease")

    # 必须可跳过：全屏遮罩 + 不可跳过 + 多张牌 = 最长 27 秒不能操作
    if '"Skip"' not in code:
        errors.append("[可跳过] " + name + " 必须提供跳过入口")


def check_hatch_specific(errors):
    code = strip_cs_comments(read_petnest("PetNestHatchRevealView.cs") or "")
    # 六段节奏成文
    for const in ["BeginSeconds", "RollBeginSeconds", "RollStepSeconds",
                  "RollStepCount", "ShowResultSeconds", "PickupSeconds"]:
        if const not in code:
            errors.append("[六段] 孵化演出缺少节奏常量: " + const)
    # 结果只读
    if "_result.Pet" not in code:
        errors.append("[只回放] 孵化演出必须读服务层给的结果快照")


def check_expedition_specific(errors):
    code = strip_cs_comments(read_petnest("PetNestExpeditionRevealView.cs") or "")
    # 唯一允许的写调用
    if "PetNestExpeditionService.MarkRevealed(record, out reason)" not in code:
        errors.append("[翻牌] 翻完必须调 MarkRevealed 把记录移出待翻列表")
    if "PetNestExpeditionService.TrySettle" in code:
        errors.append("[只回放] 翻牌层不得触发结算，结算发生在回基地扫描时")
    # 待翻列表来源
    if "PetNestExpeditionService.GetPendingReveals()" not in code:
        errors.append("[翻牌] 待翻记录必须来自 GetPendingReveals()")
    # 死亡率刻在牌面上
    if "record.deathRate" not in code:
        errors.append("[明示] 翻牌必须显示出发时固化的死亡率")


def check_scene_stop(errors):
    """演出宿主是 DontDestroyOnLoad，切图必须显式停，否则会跟着过图盖住战斗。"""
    module = read_petnest("PetNestRuntimeModule.cs")
    if module is None:
        errors.append("[File] 缺少 PetNest/PetNestRuntimeModule.cs")
        return
    code = strip_cs_comments(module)
    for token in ["PetNestExpeditionRevealView.Stop()", "PetNestHatchRevealView.Stop()"]:
        if token not in code:
            errors.append("[切图] 离开基地必须停演出: " + token)


def main():
    errors = []
    for name in VIEWS:
        check_view(errors, name)
    check_scene_stop(errors)
    check_hatch_specific(errors)
    check_expedition_specific(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
