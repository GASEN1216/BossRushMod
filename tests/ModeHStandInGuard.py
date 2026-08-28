#!/usr/bin/env python3
"""
ModeHStandInGuard — Mode H ERROR 完整互换守卫（设计提案 §17.6.5、§26.1）。

不变式：
- Harmony 补丁**恰好**只有 CA_ControlOtherCharacter.CanMove 与 CanRun 两个 postfix；
- 不存在对 CanUseHand / CanControlAim 的补丁（原版 false 是第二道保险）；
- 两个 postfix 都是 no-throw、零分配快路径，fail-closed 到原版 false，
  且不返回 false、不写 __result 之外的任何状态；
- 快路径先读静态 ModeHRuntimeGates.IsModeHStandInActive，再做 O(1) 身份查询；
- IsModeHStandInActive 只有一个写入点（SetStandInActive），且在全部退出路径清零；
- ModeHStandInPerformer 只写移动意图：不引用瞄准/攻击/技能/交互/Inventory API；
- 半径、重设间隔、越界倍率与连续失败上限四个常量存在且被引用；
- 表演失败只停演，绝不升级为技术中止；
- 互换恢复顺序固定（停表演 -> 清门 -> 确认控制目标 -> 受控选手回 scav
  -> 恢复 team -> 恢复 invincible -> 恢复位置），且是幂等入口；
- 互换期间接受原版把阵营改写为 Teams.all，不新增第三个补丁去抑制击杀计数/经验。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import contains_symbol, read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
PATCHES = os.path.join(MODEH_DIR, "ModeHHarmonyPatches.cs")
PERFORMER = os.path.join(MODEH_DIR, "ModeHStandInPerformer.cs")
CONTROL = os.path.join(MODEH_DIR, "ModeHCombatControl.cs")
GATES = os.path.join(MODEH_DIR, "ModeHRuntimeGates.cs")
CONFIG = os.path.join(MODEH_DIR, "ModeHConfig.cs")

FORBIDDEN_PATCH_TARGETS = ["CanUseHand", "CanControlAim"]

# 表演层禁止触碰的能力（§17.6.5 部件三）
FORBIDDEN_PERFORMER_SYMBOLS = [
    "Inventory", "PlayerStorage", "ItemTreeData", "CharacterItem",
    "Aim", "Shoot", "Attack", "UseSkill", "Interact", "TryPlug", "Slots",
    "AICharacterController", "searchedEnemy", "SetNoticedToTarget",
]


def check_patches(errors):
    source = read_text(PATCHES)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHHarmonyPatches.cs")
        return
    code = strip_cs_comments(source)

    targets = re.findall(r'HarmonyPatch\(typeof\((\w+)\),\s*"(\w+)"\)', code)
    if sorted(targets) != [("CA_ControlOtherCharacter", "CanMove"),
                           ("CA_ControlOtherCharacter", "CanRun")]:
        errors.append("[Patch] 补丁目标必须恰好是 CA_ControlOtherCharacter 的 CanMove 与 CanRun，"
                      "实际: " + str(sorted(targets)))

    postfix_count = len(re.findall(r"\[HarmonyPostfix\]", code))
    if postfix_count != 2:
        errors.append("[Patch] postfix 数量必须恰好为 2，实际 {}".format(postfix_count))
    if re.search(r"\[HarmonyPrefix\]", code) or re.search(r"\[HarmonyTranspiler\]", code):
        errors.append("[Patch] 不得使用 prefix 或 transpiler")

    for forbidden in FORBIDDEN_PATCH_TARGETS:
        if forbidden in code:
            errors.append("[Patch] 不得对 {} 打补丁（原版 false 是第二道保险）".format(forbidden))

    checks = [
        (r"ref bool __result", "只读改 ref bool __result"),
        (r"if \(!ModeHRuntimeGates\.IsModeHStandInActive\) return false;", "零分配静态快门优先"),
        (r"ModeHRuntimeGates\.IsModeHStandInBody\(go\.GetInstanceID\(\)\)", "O(1) 身份查询"),
        (r"internal static bool ShouldUnfreeze\(CA_ControlOtherCharacter instance\)",
         "两个 postfix 共用同一个门实现"),
        (r"catch \(Exception e\)", "no-throw 包裹"),
        (r"return false;", "异常 fail-closed 到原版 false"),
        (r"ModeHConfig\.DiagnosticLogIntervalSeconds", "5 秒限频告警"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Patch] 不满足: " + desc)

    # postfix 体内不得出现 __result = false（不得把原版 true 改成 false）
    for body in re.findall(r"public static void Postfix\([\s\S]*?\n        \}", code):
        if "__result = false" in body:
            errors.append("[Patch] postfix 不得把结果改为 false")
        if "__result = true;" not in body:
            errors.append("[Patch] postfix 必须只在通过门后置 true")

    # 快路径不得分配
    gate = re.search(r"internal static bool ShouldUnfreeze\([\s\S]*?\n        \}", code)
    if gate:
        body = gate.group(0)
        if re.search(r"\bnew\b", body):
            errors.append("[Patch] 热路径不得分配对象")
        if re.search(r"List<|Dictionary<|HashSet<", body):
            errors.append("[Patch] 热路径不得建集合")


def check_gate(errors):
    source = read_text(GATES)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHRuntimeGates.cs")
        return
    code = strip_cs_comments(source)

    if not re.search(r"public static bool IsModeHStandInActive", code):
        errors.append("[Gate] 缺少 IsModeHStandInActive")
    if not re.search(r"public static void SetStandInActive\(bool active, int bodyInstanceId\)", code):
        errors.append("[Gate] 缺少唯一写入点 SetStandInActive")
    if not re.search(r"public static bool IsModeHStandInBody\(int instanceId\)", code):
        errors.append("[Gate] 缺少 O(1) 身份查询 IsModeHStandInBody")

    # _standInActive 只允许在 SetStandInActive 内被赋值
    assignments = re.findall(r"_standInActive\s*=", code)
    if len(assignments) != 1:
        errors.append("[Gate] _standInActive 只能有一个写入点，实际 {} 处".format(len(assignments)))

    # 静态复位必须清零
    reset = re.search(r"public static void ResetStaticCaches\(\)[\s\S]*?\n        \}", code)
    if reset and "SetStandInActive(false, 0);" not in reset.group(0):
        errors.append("[Gate] ResetStaticCaches 必须清零看台解冻门")


def check_performer(errors):
    source = read_text(PERFORMER)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHStandInPerformer.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public bool TryStart\(", "启动入口"),
        (r"public void Tick\(float deltaTime\)", "每帧推进"),
        (r"public void Stop\(\)", "停演入口"),
        (r"ModeHConfig\.StandInRadiusMeters", "引用半径常量"),
        (r"ModeHConfig\.StandInRepathIntervalSeconds", "引用重设间隔常量"),
        (r"ModeHConfig\.StandInLeashMultiplier", "引用越界倍率常量"),
        (r"ModeHConfig\.StandInMaxConsecutiveFailures", "引用连续失败上限常量"),
        (r"_body\.SetMoveInput\(", "只写移动意图"),
        (r"if \(!ModeHRuntimeGates\.IsModeHStandInActive\)", "启动前必须已解冻"),
        (r"public static string ResolvePatternId\(string temperamentId\)", "底色到表演模式映射"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Performer] 不满足: " + desc)

    for forbidden in FORBIDDEN_PERFORMER_SYMBOLS:
        if contains_symbol(code, forbidden):
            errors.append("[Performer] 表演层不得引用: " + forbidden)

    # 六种表演模式齐全
    for pattern_id in ["rail_charge", "wall_hug", "slow_circle",
                       "anchor_stand", "erratic_dart", "gate_pace"]:
        if '"{}"'.format(pattern_id) not in code:
            errors.append("[Performer] 缺少表演模式: " + pattern_id)

    # 停演不得升级为技术中止
    if contains_symbol(code, "TechnicalAbort"):
        errors.append("[Performer] 表演失败绝不升级为技术中止")

    # 不使用全局随机
    if re.search(r"UnityEngine\.Random|Random\.Range|Random\.value", code):
        errors.append("[Performer] 不得使用全局随机（表演必须可重放）")


def check_swap_order(errors):
    source = read_text(CONTROL)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHCombatControl.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public bool TryBeginErrorSwap\(", "互换入口"),
        (r"public void RestoreErrorSwap\(\)", "幂等恢复入口"),
        (r"_playerBody\.ControlOtherCharacter\(_controlledFighter, -1f\);",
         "必须传 -1f 无限时长，不得改传正数再乘算"),
        (r"ModeHConfig\.ErrorControlSwitchDeadlineSeconds", "2 秒 deadline 常量"),
        (r"private static bool IsControllingCharacter\(CharacterMainControl target\)",
         "独立确认控制目标，不只信任原版回调"),
        (r"ModeHEventRouter\.SetErrorSwapControlledParticipant\(", "遥测归属映射"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Swap] 不满足: " + desc)

    # 切换成功后的顺序：先中立无敌，再解冻移动，最后启动表演
    handover = re.search(
        r"private void CompleteSwapHandover\(\)[\s\S]*?\n        \}", code)
    if not handover:
        errors.append("[Swap] 缺少 CompleteSwapHandover")
    else:
        body = handover.group(0)
        order = [
            body.find("TrySetTeam(_playerBody, Teams.middle)"),
            body.find("TrySetInvincible(_playerBody, true)"),
            body.find("ModeHRuntimeGates.SetStandInActive("),
            body.find("_standInPerformer.TryStart("),
        ]
        if any(i < 0 for i in order) or order != sorted(order):
            errors.append("[Swap] 顺序必须是 中立 -> 无敌 -> 解冻 -> 启动表演，不可颠倒")

    # 恢复顺序
    restore = re.search(r"public void RestoreErrorSwap\(\)[\s\S]*?\n        \}", code)
    if not restore:
        errors.append("[Swap] 缺少 RestoreErrorSwap")
    else:
        body = restore.group(0)
        order = [
            body.find("_standInPerformer.Stop();"),
            body.find("ModeHRuntimeGates.SetStandInActive(false, 0);"),
            body.find("TryRestoreControllingCharacter"),
            body.find("TrySetTeam(_controlledFighter, Teams.scav)"),
            body.find("TrySetTeam(_playerBody, _playerOriginalTeam)"),
            body.find("TrySetInvincible(_playerBody, _playerOriginalInvincible)"),
            body.find("TrySetPosition(_playerBody, _playerOriginalPosition)"),
        ]
        if any(i < 0 for i in order) or order != sorted(order):
            errors.append(
                "[Swap] 恢复顺序必须是 停表演 -> 清门 -> 确认控制目标 -> 受控选手回 scav "
                "-> 恢复 team -> 恢复 invincible -> 恢复位置")
        # 持有物由原版 SwitchToWeaponBeforeUse 自动恢复，这里不得抢先改写
        if "ChangeHoldItem" in body or "SwitchToWeaponBeforeUse" in body:
            errors.append("[Swap] 持有物恢复必须交给原版，不得抢先改写")

    # RestoreAll 必须调用互换恢复（统一幂等入口）
    restore_all = re.search(r"public void RestoreAll\(\)[\s\S]*?\n        \}", code)
    if restore_all and "RestoreErrorSwap();" not in restore_all.group(0):
        errors.append("[Swap] 统一还原入口必须包含互换恢复")

    # 不得新增第三个补丁去抑制击杀计数或经验
    for forbidden in ["SavesCounter", "EXPManager", "AddKillCount", "AddExp"]:
        if contains_symbol(code, forbidden):
            errors.append(
                "[Swap] 接受原版击杀计数/经验语义，不得新增抑制逻辑: " + forbidden)


def check_config(errors):
    config = read_text(CONFIG)
    if config is None:
        errors.append("[File] 缺少 ModeH/ModeHConfig.cs")
        return
    frozen = [
        (r"public const float StandInRadiusMeters = 3\.0f;", "看台半径 3.0"),
        (r"public const float StandInRepathIntervalSeconds = 0\.5f;", "重设间隔 0.5 秒"),
        (r"public const int StandInMaxConsecutiveFailures = 3;", "连续失败上限 3"),
        (r"public const float ErrorControlSwitchDeadlineSeconds = 2f;", "控制切换 deadline 2 秒"),
        (r"public const float ErrorTriggerChance = 0\.08f;", "ERROR 触发概率 8%"),
    ]
    for pattern, desc in frozen:
        if not re.search(pattern, config):
            errors.append("[Config] 未冻结: " + desc)


def check_no_extra_patches(errors):
    """Mode H 首发唯一允许的 Harmony 新增就是那两个 postfix。"""
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs") or name == "ModeHHarmonyPatches.cs":
            continue
        text = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        if re.search(r"\[HarmonyPatch", text) or re.search(r"\[HarmonyPostfix\]", text) \
                or re.search(r"\[HarmonyPrefix\]", text):
            errors.append("[Patch] {} 不得新增 Harmony 补丁".format(name))


def main():
    errors = []
    check_patches(errors)
    check_gate(errors)
    check_performer(errors)
    check_swap_order(errors)
    check_config(errors)
    check_no_extra_patches(errors)

    if errors:
        print("ModeHStandInGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHStandInGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
