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
- Close/Far 真实影响验证：适应方法实际消费常量施加 Stat/血量；
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
             r"TryAddStatModifier\(boss, StatMeleeDamage, CloseAdaptationMeleeDamageBonus"
             r"[\s\S]{0,300}?boss\.Health\.AddHealth\(boss\.Health\.MaxHealth \* CloseAdaptationMaxHealthBonus\);",
             "Close 适应真实施加近战伤害与血量"),
            ("FarRealEffect",
             r"TryAddStatModifier\(boss, StatWalkSpeed, FarAdaptationMoveSpeedBonus"
             r"[\s\S]{0,300}?TryAddStatModifier\(boss, StatGunShootSpeed, FarAdaptationShootSpeedBonus",
             "Far 适应真实施加移速与射速"),
            # 弹药样本门槛 + 确定性加权抽取
            ("AmmoSampleGate",
             r"if \(telemetry\.TotalAmmoSamples < AmmoAxisMinSamples\) return -1;",
             "样本不足 fail-closed"),
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

    if routing:
        if not re.search(
                r"return aliveBossCount == 1 && committedCount > 1;", routing):
            errors.append("[LastStandArmCondition] Last Stand 武装条件（恰剩 1 名且提交 >=2）不满足")

    if module:
        if "_state.lastStandTimer = ModeGAdaptiveCombat.LastStandDurationSeconds;" not in module:
            errors.append("[LastStandTimer] RuntimeModule 未消费 LastStandDurationSeconds 常量")

    if errors:
        print("ModeGAdaptiveCombatGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGAdaptiveCombatGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
