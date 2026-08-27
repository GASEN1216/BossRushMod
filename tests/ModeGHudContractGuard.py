#!/usr/bin/env python3
"""
ModeGHudContractGuard — Mode G 战斗 HUD 呈现契约守卫（规格 §15）。

背景：§15 逐条规定了战斗 HUD 必须呈现的内容，但实现长期只显示
「幕/波次 + 轴名 + Resolve + 目标数」，玩家看不到反制方向、被点名弹药、
被封锁武器系和双门槛进度，导致三轴玩法实际不可操作。本守卫冻结修复后的契约。

不变式：
- HUD 只消费 ModeGRuntimeModule.BuildHudModel() 的只读视图，不反向读取
  RunState/遥测/自适应对象（呈现层与结算口径单点化）；
- 进度数值来自 ModeGAxisProgress，与破解结算同一口径；百分比向下取整，
  保证「显示达标」不早于「实际达标」；
- 必须能呈现：距离方向（贴近/拉开）、被点名弹药名与违禁状态、被封锁武器系、
  双门槛进度、宿敌名称与 Rank、本局契约短标题；
- 「本波无反制 / 本波挑战无效 / 宿敌未学会新弹药」三种不可得分状态必须显式呈现，
  不得显示仍可得分的假进度；
- 宿敌波与弹药轴必须可同时呈现（弹药轴波恒为宿敌波，早期实现把轴名整体
  替换成「宿敌降临」，使「弹药点名」在 HUD 上永不可达）；
- Last Stand 只作为短时倒计时附加行，不构成第二份常驻目标清单；
- 所有玩家可见文本走 BossRush_ModeG_* 本地化 key，并已在注入器登记。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HUD = os.path.join(REPO_ROOT, "ModeG", "ModeGHUD.cs")
PUBLIC_API = os.path.join(
    REPO_ROOT, "ModeG", "ModeGRuntimeModule_PublicApiAndShutdown.cs")
INJECTOR = os.path.join(REPO_ROOT, "Localization", "LocalizationInjector.cs")

# §15 要求的 HUD 文本 key（必须在 HUD 引用且在注入器登记）
REQUIRED_HUD_KEYS = [
    "BossRush_ModeG_Hud_Counter",
    "BossRush_ModeG_Hud_NoCounter",
    "BossRush_ModeG_Hud_Invalid",
    "BossRush_ModeG_Hud_NoAmmoCandidate",
    "BossRush_ModeG_Hud_NeedClose",
    "BossRush_ModeG_Hud_NeedFar",
    "BossRush_ModeG_Hud_ContribWord",
    "BossRush_ModeG_Hud_WillBreak",
    "BossRush_ModeG_Hud_BanPrefix",
    "BossRush_ModeG_Hud_BanClean",
    "BossRush_ModeG_Hud_BanViolated",
    "BossRush_ModeG_Hud_FamilyGun",
    "BossRush_ModeG_Hud_FamilyMelee",
    "BossRush_ModeG_Hud_LockedSuffix",
    "BossRush_ModeG_Hud_LastStand",
    "BossRush_ModeG_Hud_Intermission",
    "BossRush_ModeG_Hud_CalmGate",
    "BossRush_ModeG_Hud_NextWave",
]

# §3.1 入场前强制披露 key（确认页必须在扣除入场物品前展示）
REQUIRED_ENTRY_KEYS = [
    "BossRush_ModeG_Entry_DeathRule",
    "BossRush_ModeG_Entry_LoadoutHint",
]


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def strip_comments(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def main():
    errors = []
    hud = read(HUD, errors)
    api = read(PUBLIC_API, errors)
    injector = read(INJECTOR, errors)
    interactable = read(os.path.join(REPO_ROOT, "ModeG", "ModeGInteractable.cs"), errors)

    if hud:
        checks = [
            ("ViewModelStruct",
             r"internal struct ModeGHudModel",
             "HUD 只读视图模型存在"),
            ("ObjectiveStates",
             r"enum ModeGObjectiveState"
             r"[\s\S]{0,900}?NoCounter"
             r"[\s\S]{0,400}?Invalid"
             r"[\s\S]{0,400}?NoAmmoCandidate"
             r"[\s\S]{0,400}?AmmoViolated",
             "四种不可得分/违规状态已建模"),
            ("BuildFromModel",
             r"BuildStatusText\(_module\.BuildHudModel\(\)\)",
             "HUD 只消费 BuildHudModel 视图"),
            ("FloorPercent",
             r"Mathf\.FloorToInt\(current \* 100f\)",
             "进度百分比向下取整（显示达标不早于实际达标）"),
            ("NemesisIdentity",
             r"m\.nemesisName[\s\S]{0,400}?BossRush_ModeG_RankWord",
             "宿敌名称与 Rank 同时呈现"),
            ("ContractTitle",
             r"m\.contractTitle",
             "本局契约短标题呈现"),
            ("LastStandOverlay",
             r"if \(m\.lastStandActive\)"
             r"[\s\S]{0,400}?BossRush_ModeG_Hud_LastStand",
             "Last Stand 作为附加倒计时行"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, hud):
                errors.append("[{}] 不满足: {}".format(name, desc))

        stripped = strip_comments(hud)

        # 宿敌波与轴名必须共存：轴名不得被「宿敌」整体替换
        if re.search(r"if\s*\(\s*(wave|m)\.isNemesisWave\s*\)\s*\{[^}]*?\}\s*else\s*\{"
                     r"[^}]*?GetAxisDisplayName", stripped):
            errors.append("[NemesisHidesAxis] 宿敌波不得整体替换轴名，"
                          "否则「弹药点名」在 HUD 上永不可达")

        # 呈现层不得反向读取结算对象
        for token in ["_module.Telemetry", "_module.Adaptive",
                      "_module.SpawnTransaction", "_module.WavePlan"]:
            if token in stripped:
                errors.append("[HudReachesIntoRuntime] HUD 仍反向读取结算对象: " + token)

        for key in REQUIRED_HUD_KEYS:
            if key not in hud:
                errors.append("[HudKey] HUD 未呈现 §15 要求内容，缺 key: " + key)

    if api:
        checks = [
            ("BuildHudModel", r"public ModeGHudModel BuildHudModel\(\)",
             "视图模型唯一构建点在 RuntimeModule"),
            ("SharedDistanceProgress",
             r"ModeGAdaptiveCombat\.EvaluateDistanceProgress\(_telemetry, _activeDistanceVerdict\)",
             "距离进度复用结算口径"),
            ("SharedAttributeProgress",
             r"_adaptive\.EvaluateAttributeProgress\(_telemetry\)",
             "属性进度复用结算口径"),
            ("SharedAttributePrediction",
             r"ModeGAdaptiveCombat\.PredictAttributeLockFamily\(_telemetry\)",
             "休整预告复用属性封锁纯函数（不独立推断）"),
            ("InvalidFailClosed",
             r"_telemetry\.IsTelemetryDegraded \|\| _telemetry\.ContaminatedByCharacterSwitch",
             "污染/溢出统一判为本波挑战无效"),
            ("ViolationAnnounce",
             r"private void TickAmmoViolationAnnounce\(\)"
             r"[\s\S]{0,600}?_ammoViolationAnnounced = true;",
             "弹药违规一次性播报"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, api):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if interactable:
        for key in REQUIRED_ENTRY_KEYS:
            if key not in interactable:
                errors.append("[EntryDisclosure] 确认页缺 §3.1 强制披露 key: " + key)

    if injector:
        for key in REQUIRED_HUD_KEYS + REQUIRED_ENTRY_KEYS:
            if 'InjectModeGString("' + key + '"' not in injector:
                errors.append("[MissingInjection] 本地化未注入 key: " + key)

    if errors:
        print("ModeGHudContractGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGHudContractGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
