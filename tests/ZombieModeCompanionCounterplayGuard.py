"""ZombieModeCompanionCounterplayGuard — 精英反制词条只认玩家本人（2026-10-17）。

玩家反馈带崽打丧尸「一滴血一滴血地掉」：精英的「刚硬」（远程 ×0.1）与「反制」（同类连击后减伤 60%）
是逼玩家换武器的反制词条，旧判据 IsPlayerSideAttack 把遗种巢的崽也算进去。崽不会换武器，
对这类精英几乎打不出伤害；它的命中还会替玩家攒「反制」计数，让精英转头减玩家自己的伤害。

断言：
1. 两条反制词条的判据是 IsPlayerOwnHit，而 IsPlayerOwnHit 只认主角；
2. 护盾吸收与护盾光环仍走 IsPlayerSideAttack（精英本身的防御对崽照常生效，不放宽）。
"""

import re
import sys
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def main():
    errors = []
    damage = read("ZombieMode/ZombieModeDamageRuntime.cs")
    tuning = read("ZombieMode/ZombieModeRuntimeModule_PollutionTuning.cs")

    own = re.search(r"internal static bool IsPlayerOwnHit\(CharacterMainControl source\)\s*\{([^{}]*)\}", damage)
    if own is None:
        errors.append("[判据] 缺少 ZombieModeDamageRuntime.IsPlayerOwnHit")
    else:
        body = re.sub(r"\s+", " ", own.group(1)).strip()
        if body != "return source != null && source.IsMainCharacter;":
            errors.append("[判据] IsPlayerOwnHit 只能认主角，不得把随从算进来: " + body)

    # 断言整个 if 条件（CR-2026-10-18-021）：只查子串时改成 `IsPlayerOwnHit(x) || IsPlayerSideAttack(x)` 也能过。
    expected_conditions = {
        "Stalwart": "ZombieModeDamageRuntime.IsPlayerOwnHit(damageInfo.fromCharacter) && !IsZombieModeDamageFromMeleeWeapon(damageInfo)",
        "Adaptive": "ZombieModeDamageRuntime.IsPlayerOwnHit(damageInfo.fromCharacter)",
    }
    for affix, expected in expected_conditions.items():
        clause = re.search(r"if \(marker\.EliteAffixes\.Contains\(ZombieModeEliteAffix\." + affix + r"\)\s*&&([\s\S]*?)\)\s*\{", tuning)
        if clause is None:
            errors.append("[反制] 找不到「" + affix + "」词条判定")
            continue
        condition = re.sub(r"\s+", " ", clause.group(1)).strip()
        if condition != expected:
            errors.append("[反制] 「" + affix + "」必须只对玩家本人的命中生效（IsPlayerOwnHit），条件应为 `"
                          + expected + "`，实际 `" + condition + "`")

    reduce = re.search(r"internal static void ReduceFinalDamage\([\s\S]*?\n        \}", damage)
    if reduce is None or "if (IsPlayerSideAttack(info.fromCharacter))" not in reduce.group(0):
        errors.append("[防御] 护盾吸收与护盾光环仍须按玩家方（含随从）结算，不得一并放宽")

    if errors:
        print("ZombieModeCompanionCounterplayGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1
    print("ZombieModeCompanionCounterplayGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
