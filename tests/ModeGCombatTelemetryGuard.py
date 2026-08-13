#!/usr/bin/env python3
"""
ModeGCombatTelemetryGuard — Mode G 战斗遥测守卫（规格 §20 第 20 条）。

不变式：
- 三 combat 委托（Health.OnHurt / ItemAgent_Gun.OnMainCharacterShootEvent /
  LevelManager.OnControllingCharacterChanged）+ 独立 Health.OnDead run owner，
  各自私有 owner bool，幂等订阅精确退订；
- ModeGDirectDamageClassifier 五条件全判据（登记 Boss/玩家来源/非 buff/
  damageValue>0/damageType==normal），纯函数无状态；
- PhaseProxy terminal-only：OnDead run owner 只处理已登记主 Boss，
  阶段代理不进主计分/死亡通道；
- 开火先 Armed ban（exact TargetBulletID 比较）后 bounded 累计（容量守卫）；
- 预分配缓存 32/32/64/3/3（+128 宿敌 key 容量）冻结；
- 禁 SetTargetBulletType/TakeOutAllBullets/禁弹 Harmony：ModeG/ 剥注释后
  无此两类调用，且无 HarmonyPatch(ItemAgent_Gun) 弹道 patch。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")
TELEMETRY = os.path.join(MODEG_DIR, "ModeGCombatTelemetry.cs")


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
    tele = read(TELEMETRY, errors)

    if tele:
        checks = [
            ("ThreeCombatDelegates",
             r"Health\.OnHurt \+= HandleOnHurt;"
             r"[\s\S]{0,200}?ItemAgent_Gun\.OnMainCharacterShootEvent \+= HandleOnShoot;"
             r"[\s\S]{0,200}?LevelManager\.OnControllingCharacterChanged \+= HandleControllingCharacterChanged;",
             "三 combat 委托一次订阅"),
            ("CombatOwnerBool",
             r"if \(_combatSubscribed\) return;",
             "combat 订阅 owner bool 幂等"),
            ("DeadOwnerBool",
             r"if \(_deadSubscribed\) return;",
             "OnDead 独立 owner bool 幂等"),
            ("ExactUnsubscribe",
             r"Health\.OnHurt -= HandleOnHurt;"
             r"[\s\S]{0,200}?ItemAgent_Gun\.OnMainCharacterShootEvent -= HandleOnShoot;"
             r"[\s\S]{0,200}?LevelManager\.OnControllingCharacterChanged -= HandleControllingCharacterChanged;",
             "三 combat 委托精确退订"),
            ("ClassifierStatic",
             r"public static class ModeGDirectDamageClassifier",
             "直伤分类器纯静态（无状态）"),
            ("ClassifierFiveCriteria",
             r"if \(!isRegisteredBoss\) return ModeGDirectDamageClass\.NotScoreable;"
             r"[\s\S]*?!ReferenceEquals\(info\.fromCharacter, player\)"
             r"[\s\S]*?if \(info\.isFromBuffOrEffect\)"
             r"[\s\S]*?if \(info\.damageValue <= 0f\)"
             r"[\s\S]*?if \(info\.damageType != DamageTypes\.normal\)",
             "五条件判据全在且顺序冻结"),
            ("PhaseProxyTerminalOnly",
             r"private void HandleOnDead\(Health health, DamageInfo info\)"
             r"[\s\S]{0,300}?if \(!_state\.IsRegisteredBossHealth\(health\)\) return;",
             "OnDead run owner 只处理已登记主 Boss（PhaseProxy 不进通道）"),
            ("ArmedBanExactCompare",
             r"if \(_armedBanAmmoTypeId != 0 && ammoTypeId == _armedBanAmmoTypeId\)",
             "开火先 Armed ban（exact TargetBulletID 比较）"),
            ("BoundedAccumulation",
             r"_ammoShotCount\.Count < AmmoCacheCapacity \|\| _ammoShotCount\.ContainsKey\(ammoTypeId\)",
             "弹药累计 bounded（容量守卫，overflow 关分）"),
            ("CapAmmo", r"public const int AmmoCacheCapacity = 32;", "弹药威胁容量 32"),
            ("CapProjectile", r"public const int ProjectileThreatCountCap = 32;", "ShotCount 钳制上限 32"),
            ("CapWeaponFamily", r"public const int WeaponFamilyCacheCapacity = 64;", "weapon-family 容量 64"),
            ("CapBoss", r"public const int BossCacheCapacity = 3;", "主 Boss 缓存容量 3"),
            ("CapNamedAmmo", r"public const int NamedAmmoCapacity = 3;", "已点名弹种容量 3"),
            ("CapNemesisKeys", r"public const int CompletedNemesisKeysCapacity = 128;",
             "completedNemesisKeys 容量 128"),
            ("ThreatKeys",
             r'private const string ConstKey_DamageMultiplier = "damageMultiplier";'
             r'[\s\S]*?private const string ConstKey_ExplosionRange = "ExplosionRange";'
             r'[\s\S]*?private const string ConstKey_ExplosionDamage = "ExplosionDamage";',
             "弹药 Constants 真实 key 冻结"),
            ("ThreatFormula",
             r"double directThreat = bulletDamage \* gun\.BulletDamageMultiplier \* gun\.CharacterDamageMultiplier;",
             "directThreat 公式冻结"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, tele):
                errors.append("[{}] 不满足: {}".format(name, desc))

    # 禁项：ModeG/ 剥注释后禁 SetTargetBulletType/TakeOutAllBullets/禁弹 Harmony
    forbidden = ["SetTargetBulletType", "TakeOutAllBullets"]
    for f in sorted(os.listdir(MODEG_DIR)):
        if not f.endswith(".cs"):
            continue
        code = strip_comments(read(os.path.join(MODEG_DIR, f), errors))
        for token in forbidden:
            if token in code:
                errors.append("[Forbidden{}] ModeG/{} 使用被禁 API".format(token, f))
        if re.search(r"\[HarmonyPatch\(typeof\(ItemAgent_Gun\)", code):
            errors.append("[ForbiddenBulletHarmony] ModeG/{} 含弹道 Harmony patch".format(f))

    if errors:
        print("ModeGCombatTelemetryGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGCombatTelemetryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
