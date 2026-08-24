#!/usr/bin/env python3
"""
ModeGAdaptiveCombatGuard — 三轴自适应战斗守卫（规格 §20 第 21 条）。

不变式：
- 九波轴固定三幕循环：GetAxisForWave 0-based 1/4/7=距离、2/5/8=弹药、3/6=属性，
  其余 None（与规格 1-based 2/5/8、3/6/9、4/7 等价）；
- Resolve 上限 11 = 距离 3 + 弹药 3 + 属性 2 + Last Stand 3；
- 能力快照常量逐项冻结：13m 分界 / 极端带 18m/8m / 破解双门槛 35%+20% /
  Close+15%/+10% / Far+15%/+10% / 弹药 5 发样本 / clamp 10% /
  属性 -25% / Last Stand 12s / 复仇回血 20%；
- Close/Far 真实影响验证：适应方法实际消费常量施加 Stat Modifier，
  最大生命按原生命比例同步 CurrentHealth，部分失败局部回滚；
- 幕次伤害同样进入 Adaptive Modifier 记录并验证 Gun/Melee 两项 Stat；
  整套波次强化必须在槽提交前成功，否则局部回滚并拒绝候选；
- 属性仅 -25% PercentageMultiply：AttributeLockSource 专属 PercentageMultiply，
  其余加成 PercentageAdd；
- 弹药 5 发样本与威胁公式：样本不足返回 -1，加权抽取走确定性随机；
- Last Stand 武装条件：恰剩 1 名且开战提交 >=2（RuntimeModule 消费 12s 常量）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ADAPTIVE = os.path.join(REPO_ROOT, "ModeG", "ModeGAdaptiveCombat.cs")
MODULE = os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule.cs")
ROUTING = os.path.join(REPO_ROOT, "ModeG", "ModeGDeathRouting.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []
    ad = read(ADAPTIVE, errors)
    module = read(MODULE, errors)
    routing = read(ROUTING, errors)

    if ad:
        checks = [
            ("AxisScheduleDistance",
             r"case 1:\s*case 4:\s*case 7:\s*return ModeGCounterAxis\.Distance;",
             "距离轴波 2/5/8（1-based）"),
            ("AxisScheduleAmmo",
             r"case 2:\s*case 5:\s*case 8:\s*return ModeGCounterAxis\.Ammo;",
             "弹药轴波 3/6/9（1-based）"),
            ("AxisScheduleAttribute",
             r"case 3:\s*case 6:\s*return ModeGCounterAxis\.Attribute;",
             "属性轴波 4/7（1-based）"),
            ("AxisDefaultNone",
             r"default:\s*return ModeGCounterAxis\.None;",
             "其余波无轴（三幕循环固定）"),
            ("ResolveCaps",
             r"public const int MaxResolveDistance = 3;"
             r"[\s\S]*?public const int MaxResolveAmmo = 3;"
             r"[\s\S]*?public const int MaxResolveAttribute = 2;"
             r"[\s\S]*?public const int MaxResolveLastStand = 3;"
             r"[\s\S]*?public const int MaxResolveTotal = 11;",
             "Resolve 上限 3+3+2+3=11"),
            ("TotalResolveGate",
             r"if \(TotalResolve >= MaxResolveTotal\) return false;",
             "总上限 11 钳制"),
            ("DistanceBoundary", r"public const float DistanceBoundaryMeters = 13f;", "13m 分界"),
            ("ExtremeFar", r"public const float ExtremeFarMeters = 18f;", "极端远距 18m"),
            ("ExtremeClose", r"public const float ExtremeCloseMeters = 8f;", "极端近距 8m"),
            ("BreakShare", r"public const float DistanceBreakDamageShare = 0\.35f;",
             "破解门槛占比 35%"),
            ("BreakContribution",
             r"public const float DistanceBreakHealthContribution = 0\.20f;",
             "破解门槛血量贡献 20%"),
            ("CloseAdaptation",
             r"public const float CloseAdaptationMeleeDamageBonus = 0\.15f;"
             r"[\s\S]*?public const float CloseAdaptationMaxHealthBonus = 0\.10f;",
             "Close 适应 +15%/+10%"),
            ("FarAdaptation",
             r"public const float FarAdaptationMoveSpeedBonus = 0\.15f;"
             r"[\s\S]*?public const float FarAdaptationShootSpeedBonus = 0\.10f;",
             "Far 适应 +15%/+10%"),
            ("AmmoMinSamples", r"public const int AmmoAxisMinSamples = 5;", "弹药样本门槛 5 发"),
            ("AmmoClamp", r"public const float AmmoBanClampShare = 0\.10f;", "违规 clamp 10%"),
            ("AttributeLock", r"public const float AttributeLockValue = -0\.25f;",
             "属性封锁 -25%"),
            ("LastStandDuration", r"public const float LastStandDurationSeconds = 12f;",
             "Last Stand 12 秒"),
            ("RevengeHeal", r"public const float RevengeHealShare = 0\.20f;", "复仇回血 20%"),
            ("ActMultiplierArrays",
             r"private static readonly float\[\] ActDamageBonus = \{ 0\.00f, 0\.20f, 0\.45f \};"
             r"[\s\S]*?private static readonly float\[\] ActHealthBonus = \{ 0\.00f, 0\.10f, 0\.20f \};",
             "幕倍率 I/II/III 冻结"),
            # Close/Far 真实影响验证（常量被实际消费）
            ("CloseRealEffect",
             r"public bool ApplyCloseAdaptation\(CharacterMainControl boss\)"
             r"[\s\S]{0,500}?TryAddVerifiedStatModifier\(\s*boss, StatMeleeDamage,"
             r" CloseAdaptationMeleeDamageBonus, AdaptationSource\)"
             r"[\s\S]{0,220}?TryApplyMaxHealthBonus\(boss, CloseAdaptationMaxHealthBonus,"
             r" AdaptationSource",
             "Close 适应真实施加近战伤害与 MaxHealth Modifier"),
            ("FarRealEffect",
             r"public bool ApplyFarAdaptation\(CharacterMainControl boss\)"
             r"[\s\S]{0,600}?TryAddVerifiedStatModifier\(\s*boss, StatWalkSpeed, FarAdaptationMoveSpeedBonus"
             r"[\s\S]{0,300}?TryAddVerifiedStatModifier\(\s*boss, StatGunShootSpeed, FarAdaptationShootSpeedBonus",
             "Far 适应真实施加移速与射速"),
            ("MaxHealthRatioSync",
             r"private bool TryApplyMaxHealthBonus\("
             r"[\s\S]{0,1800}?float ratio = Mathf\.Clamp01\(beforeCurrent / beforeMax\);"
             r"\s*character\.Health\.SetHealth\(afterMax \* ratio\);",
             "最大生命 Modifier 后按原生命比例同步 CurrentHealth"),
            ("ActHealthModifier",
             r"public bool ApplyActHealthBonus\(CharacterMainControl boss, int actIndex\)"
             r"[\s\S]{0,180}?TryApplyMaxHealthBonus\(boss, bonus, ActHealthSource",
             "幕次生命使用 MaxHealth Modifier"),
            ("ActDamageTrackedModifier",
             r"public bool ApplyActDamageBonus\(CharacterMainControl boss, int actIndex\)"
             r"[\s\S]{0,500}?TryAddVerifiedStatModifier\(\s*boss, StatGunDamage, bonus, ActDamageSource\)"
             r"[\s\S]{0,260}?TryAddVerifiedStatModifier\(\s*boss, StatMeleeDamage, bonus, ActDamageSource\)"
             r"[\s\S]{0,260}?RollbackModifiersFrom\(checkpoint\);",
             "幕次伤害进入可验证、可局部回滚的 Adaptive Modifier 事务"),
            ("NemesisRankModifier",
             r"public bool ApplyNemesisRank\(CharacterMainControl nemesis, int rank\)"
             r"[\s\S]{0,500}?TryApplyMaxHealthBonus\(\s*nemesis, RankHealthBonus\[rank\], NemesisSource",
             "宿敌 Rank 生命使用 MaxHealth Modifier"),
            ("TemperamentDomain",
             r"SelectNemesisTemperament\(ulong runSeed\)"
             r"[\s\S]{0,220}?DomainConstants\.Temperament"
             r"[\s\S]{0,160}?NextInt\(ref state, 3\)",
             "宿敌性格使用独立确定性 domain"),
            ("TemperamentEffects",
             r"ModeGNemesisTemperament\.Hunter[\s\S]{0,420}?HunterMoveSpeedBonus"
             r"[\s\S]{0,500}?ModeGNemesisTemperament\.Suppressor"
             r"[\s\S]{0,420}?SuppressorDamageBonus"
             r"[\s\S]{0,500}?BulwarkMaxHealthBonus",
             "Hunter/Suppressor/Bulwark 三种性格均有真实 Stat 效果"),
            ("LocalModifierRollback",
             r"private void RollbackModifiersFrom\(int startIndex\)"
             r"[\s\S]{0,1200}?_modifierRecords\.RemoveAt\(i\);",
             "单 Boss Stat 失败仅回滚本次新增 Modifier"),
            # 弹药样本门槛 + 确定性加权抽取
            ("AmmoSampleGate",
             r"if \(!telemetry\.IsAmmoSampleValid\) return -1;"
             r"[\s\S]{0,100}?if \(telemetry\.TotalAmmoSamples < AmmoAxisMinSamples\) return -1;",
             "生成污染或样本不足均 fail-closed"),
            ("AmmoDeterministicSelect",
             r"ulong state = ModeGDeterministicRandom\.SeedDomain\(runSeed,"
             r"[\s\S]{0,200}?DomainConstants\.AmmoBan"
             r"[\s\S]{0,200}?ModeGDeterministicRandom\.WeightedSelect\(ref state, weights\);",
             "弹药禁令走确定性随机（禁 System.Random）"),
            # 属性仅 PercentageMultiply
            ("AttributeOnlyMultiply",
             r"ModifierType type = \(source == AttributeLockSource\)\s*"
             r"\?\s*ModifierType\.PercentageMultiply\s*:\s*ModifierType\.PercentageAdd;",
             "属性封锁专属 PercentageMultiply（-25%），其余 PercentageAdd"),
            ("RestoreModifiers",
             r"public void RestoreAllModifiers\(\)[\s\S]{0,200}?"
             r"RuntimeStatModifierTracker\.RemoveAll\(_modifierRecords",
             "End/清理路径原值恢复"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, ad):
                errors.append("[{}] 不满足: {}".format(name, desc))

        if ad.count(".Health.AddHealth(") != 1 or not re.search(
                r"public void ApplyRevengeBuff\([\s\S]{0,260}?\.Health\.AddHealth\(", ad):
            errors.append("[NoFakeMaxHealth] AddHealth 只能用于 Last Stand 复仇回血，"
                          "Close/幕次/Rank/Bulwark 必须使用 MaxHealth Modifier")

    if routing:
        if not re.search(
                r"return aliveBossCount == 1 && committedCount > 1;", routing):
            errors.append("[LastStandArmCondition] Last Stand 武装条件（恰剩 1 名且提交 >=2）不满足")

    if module:
        if "_state.lastStandTimer = ModeGAdaptiveCombat.LastStandDurationSeconds;" not in module:
            errors.append("[LastStandTimer] RuntimeModule 未消费 LastStandDurationSeconds 常量")
        if not re.search(
                r"if \(axis == ModeGCounterAxis\.Distance\)"
                r"[\s\S]{0,260}?distanceVerdict = _lastTerminalDistance;", module):
            errors.append("[DistanceTerminalEcho] 距离轴未消费上一署名波 terminal distance")
        if "distanceVerdict = ModeGAdaptiveCombat.EvaluateDistanceAxis(_telemetry)" in module:
            errors.append("[DistanceWholeWaveGuess] 距离轴仍用上一整波命中分布替代 terminal distance")
        if not re.search(
                r"if \(_state\.lastStandActive\)"
                r"[\s\S]{0,420}?_lastTerminalFamily != ModeGDirectDamageClass\.NotScoreable"
                r"[\s\S]{0,180}?RecordLastStandResolve\(\)", module):
            errors.append("[LastStandResolveOnExecution] Last Stand Resolve 未限定为计时内可计分终结")
        timeout = re.search(
            r"if \(_state\.lastStandTimer <= 0f\)([\s\S]{0,260}?)\n\s*\}", module)
        if not timeout or "ApplyRevengeToSurvivor();" not in timeout.group(1):
            errors.append("[LastStandTimeoutRevenge] Last Stand 超时未触发复仇强化")
        elif "RecordLastStandResolve" in timeout.group(1):
            errors.append("[LastStandTimeoutResolve] Last Stand 超时错误授予 Resolve")
        if not re.search(
                r"PrepareNextAmmoBanIfNeeded\(\);"
                r"[\s\S]{0,420}?_state\.combatPhase = ModeGCombatPhase\.Intermission;", module):
            errors.append("[AmmoBanPublishBeforeIntermission] 禁令未在休整开始前完成选择/公示/武装")
        if not re.search(
                r"ModeGAdaptiveCombat\.CalmGateSeconds"
                r"[\s\S]{0,220}?_state\.intermissionTimer = ModeGAdaptiveCombat\.CalmGateSeconds;", module):
            errors.append("[CalmGate] 学习波前最后 2 秒开火未重置停火倒计时")
        if not re.search(
                r"isAmmoSamplingWave && _telemetry\.ShotSequence != spawningGuardShotSequence"
                r"[\s\S]{0,180}?_telemetry\.InvalidateAmmoSample\(\);", module):
            errors.append("[SpawningGuard] 学习波生成期预射未单向关闭样本")
        if not re.search(
                r"resolved = _telemetry\.ArmedBanAmmoTypeId > 0"
                r"[\s\S]{0,160}?_telemetry\.ArmedBanViolationCount == 0"
                r"[\s\S]{0,160}?_lastTerminalFamily != ModeGDirectDamageClass\.NotScoreable;", module):
            errors.append("[AmmoResolve] 弹药轴未按有效禁令+零违规+直接终结结算")
        if not re.search(
                r"if \(!ApplyWaveModifiers\(boss, wave, distanceVerdict, out modifierCheckpoint\)\)"
                r"[\s\S]{0,500}?continue;"
                r"[\s\S]{0,220}?if \(!_spawnTransaction\.TryCommit\(slotIndex, key, boss\)\)", module):
            errors.append("[PreCommitCapabilityGate] 波次强化未在槽提交前完整验证并拒绝不兼容候选")
        if not re.search(
                r"private bool ApplyWaveModifiers\("
                r"[\s\S]{0,1600}?_adaptive\.ApplyActDamageBonus\(boss, wave\.actIndex\)"
                r"[\s\S]{0,800}?_adaptive\.RollbackToCheckpoint\(modifierCheckpoint\);",
                module):
            errors.append("[WaveModifierTransaction] 整套波次强化未统一进入局部回滚事务")

    if errors:
        print("ModeGAdaptiveCombatGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGAdaptiveCombatGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
