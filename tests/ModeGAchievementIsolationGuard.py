#!/usr/bin/env python3
"""
ModeGAchievementIsolationGuard — Mode G 成就隔离守卫（规格 §20 第 27 条）。

不变式：
- 成就伤害窗口不写 IsActive 本身：Mode G region 内禁 `IsActive =` 赋值；
  OnPlayerHurtForAchievement 门控为 `!IsActive && !IsModeGAchievement
  DamageWindowActiveSafe()` 只读组合；
- 窗口语义冻结：IsModeGAchievementDamageWindowActive =
  lifecyclePhase == Active && IsCombatPhase(combatPhase)
  （仅 Active + Fighting/LastStand），no-throw 异常 false；
  AchievementTriggers 侧 Safe 包装 catch -> false（保 Legacy 行为）；
- payload 冻结三元组：ReportModeGBossKillAchievement(int token,
  string bossType, bool wasFlawlessAtDeath)，ModeGAchievementReportKey
  (token, bossType, wasFlawlessAtDeath) 每 session 窄去重一次
  （HashSet.Add 失败即 return）；
- 延迟入口不重读 HasTakenDamage：上报方法体以传入的死亡时刻快照
  wasFlawlessAtDeath 为准；方法体内禁 HasTakenDamage 读取；
- 开局基准归零：BeginModeGAchievementSession 必须把 HasTakenDamage 置 false
  （Mode G 不经过 BeginAchievementSession，跨局残留会永久锁死 flawless），
  且禁止调用 Legacy 的 ResetSessionStats（会连带重置 ArenaEnterTime /
  HasUsedHealItem / HasPickedUpItem，污染 Legacy/Mode D/无间炼狱语义）；
- session 闸门：modeGAchievementSessionActive + achievementSystemInitialized
  双前置，全程 try/catch no-throw。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRIGGERS = os.path.join(REPO_ROOT, "Achievement", "AchievementTriggers.cs")
STATE = os.path.join(REPO_ROOT, "ModeG", "ModeGStateModel.cs")


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
    triggers = read(TRIGGERS, errors)
    state = read(STATE, errors)

    if triggers:
        checks = [
            ("ReportSignature",
             r"internal void ReportModeGBossKillAchievement\(int token, "
             r"string bossType, bool wasFlawlessAtDeath\)",
             "payload 冻结签名 (token, bossType, wasFlawlessAtDeath)"),
            ("SessionGate",
             r"if \(!modeGAchievementSessionActive\) return;"
             r"[\s\S]{0,80}?if \(!achievementSystemInitialized\) return;",
             "session + 初始化双闸门"),
            ("TripletDedup",
             r"ModeGAchievementReportKey key = new ModeGAchievementReportKey"
             r"\(token, bossType, wasFlawlessAtDeath\);"
             r"[\s\S]{0,100}?if \(!modeGCountedAchievementReports\.Add\(key\)\)"
             r"[\s\S]{0,40}?return;",
             "三元组窄去重（每 session 一次）"),
            ("SnapshotConsumption",
             r"if \(wasFlawlessAtDeath\)",
             "无伤成就消费死亡时刻快照（传入值）"),
            ("SafeWindowWrapper",
             r"private static bool IsModeGAchievementDamageWindowActiveSafe\(\)"
             r"[\s\S]{0,200}?return ModeGRuntimeGates\.IsModeGAchievementDamageWindowActive;"
             r"[\s\S]{0,80}?catch[\s\S]{0,40}?return false;",
             "窗口查询 Safe 包装 no-throw（异常 false）"),
            ("HurtGateReadOnly",
             r"if \(!IsActive && !IsModeGAchievementDamageWindowActiveSafe\(\)\) return;",
             "OnHurt 门控：只读 IsActive + 窗口（不写 IsActive）"),
            ("SessionFlawlessBaseline",
             r"internal void BeginModeGAchievementSession\(\)"
             r"[\s\S]{0,900}?AchievementTracker\.HasTakenDamage = false;"
             r"[\s\S]{0,120}?modeGCountedAchievementReports\.Clear\(\);",
             "开局归零 flawless 基准（HasTakenDamage）后再清去重集"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, triggers):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # 不写 IsActive：Mode G region 代码体（剥注释）禁 `IsActive =` 赋值
        region = re.search(
            r"#region Mode G[\s\S]*?#endregion", triggers, re.IGNORECASE)
        if not region:
            # 备用锚点：从 ReportModeGBossKillAchievement 文档注释到 region 结束
            region = re.search(
                r"Mode G Boss 击杀成就上报入口[\s\S]*?#endregion", triggers)
        if region:
            body = strip_comments(region.group(0))
            if re.search(r"IsActive\s*=[^=]", body):
                errors.append("[NoIsActiveWrite] Mode G region 出现 IsActive 赋值")
        else:
            errors.append("[RegionNotFound] 未找到 Mode G 成就 region 锚点")

        # 延迟入口不重读 HasTakenDamage：上报方法体内禁 HasTakenDamage
        m = re.search(
            r"internal void ReportModeGBossKillAchievement\([\s\S]*?\n        \}",
            triggers)
        if m:
            body = strip_comments(m.group(0))
            if "HasTakenDamage" in body:
                errors.append("[NoReread] 上报方法体内重读 HasTakenDamage")
        else:
            errors.append("[ReportBody] ReportModeGBossKillAchievement 方法体未找到")

        # 开局基准归零不得借道 Legacy 整体 reset（会连带清 Legacy/Mode D 的会话字段）
        begin = re.search(
            r"internal void BeginModeGAchievementSession\([\s\S]*?\n        \}",
            triggers)
        if begin:
            body = strip_comments(begin.group(0))
            if "ResetSessionStats" in body:
                errors.append(
                    "[SessionNoLegacyReset] Mode G session 调用 Legacy ResetSessionStats")
        else:
            errors.append(
                "[SessionBody] BeginModeGAchievementSession 方法体未找到")

    if state:
        checks = [
            ("WindowSemantics",
             r"public static bool IsModeGAchievementDamageWindowActive"
             r"[\s\S]{0,400}?state\.lifecyclePhase == ModeGLifecyclePhase\.Active"
             r"[\s\S]{0,80}?&& ModeGPhaseGuards\.IsCombatPhase\(state\.combatPhase\)",
             "窗口语义：仅 Active + Fighting/LastStand"),
            ("WindowNoThrow",
             r"public static bool IsModeGAchievementDamageWindowActive"
             r"[\s\S]{0,500}?catch[\s\S]{0,40}?return false;",
             "窗口查询 no-throw（异常 false）"),
            ("UniqueConsumerNote",
             r"消费点唯一：AchievementTriggers\.OnPlayerHurtForAchievement",
             "窗口消费点唯一（契约注释）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, state):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if errors:
        print("ModeGAchievementIsolationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGAchievementIsolationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
