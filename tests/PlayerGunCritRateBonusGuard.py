"""给主角的加成不能用枪械暴击率 GunCritRateGain。

背景（2026-11-19，owner：「这游戏只有爆头才有暴击伤害」）：官方 `ItemAgent_Gun.cs:1459-1463` 先按
`CritRate × (1 + GunCritRateGain + 子弹加成)` 算暴击率，紧接着只要持枪的是主角，就把它改成
`AimingEnemyHead ? 1 : 0`。所以挂在主角身上的 GunCritRateGain 一点用都没有：收藏册竞技场系列、
词缀鹰目、变异百步穿杨与幸运星、丧尸模式暴击专注原先都写成了它，玩家拿到等于没拿。
对玩家要加强枪械暴击，用爆头伤害 `GunCritDamageGain`（进 `(1 + 值)` 乘区，Add）。近战暴击率
`MeleeCritRateGain` 官方没有改写，照常有效。

判据：编译清单里的生产源码剥注释后，字符串字面量 "GunCritRateGain" 只许出现在白名单文件里——
那些地方改的是敌人、宠物或鸭王杯机器人，官方的主角改写碰不到它们。新加的地方先确认作用对象，
真是非主角再进白名单并写明理由。
"""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
sys.path.insert(0, str(ROOT / "tests"))

from compile_list import read_compile_sources  # noqa: E402
from cs_source_util import clean_source  # noqa: E402

LITERAL = '"GunCritRateGain"'

# 文件 → 为什么允许：作用对象不是主角。
ALLOWED = {
    "ModeH/ModeHRuntimeModule_PreparedLoadouts.cs": "鸭王杯参赛机器人的预览与读数，不是主角",
}

# 曾经写错、已改成爆头伤害的玩家加成：这些文件必须继续用 GunCritDamageGain。
MUST_USE_CRIT_DAMAGE = {
    "Integration/Collectibles/CollectibleTuning.cs": '"GunCritDamageGain"',
    "Integration/AffixForge/AffixRuntimeService_Effects.cs": '"GunCritDamageGain"',
    "Integration/Mutators/MutatorDefinitions.cs": 'GetStat("GunCritDamageGain")',
    "ZombieMode/ZombieModeTuning.cs": 'public const string GunCritDamageGain = "GunCritDamageGain";',
}


def main():
    errors = []
    scanned = 0
    sources = {}
    for rel in read_compile_sources():
        path = ROOT / rel
        if not path.is_file():
            continue
        scanned += 1
        code = clean_source(path.read_text(encoding="utf-8-sig"))
        key = str(rel).replace("\\", "/")
        sources[key] = code
        if LITERAL in code and key not in ALLOWED:
            errors.append("{}: 出现 {}。主角开枪只有爆头算暴击，给玩家的加成改用 GunCritDamageGain；"
                          "确实只作用于非主角就加进白名单并写明理由".format(key, LITERAL))
    for rel, token in MUST_USE_CRIT_DAMAGE.items():
        code = sources.get(rel)
        if code is None:
            errors.append("{}: 不在编译清单里，守卫需要同步".format(rel))
        elif token not in code:
            errors.append("{}: 缺少 {}（玩家的枪械暴击加成要走爆头伤害）".format(rel, token))
    if "ZombieModeStatNames.GunCritDamageGain" not in sources.get("ZombieMode/ZombieModeRewardRuntimeModifiers.cs", ""):
        errors.append("ZombieMode/ZombieModeRewardRuntimeModifiers.cs: 暴击专注必须加 ZombieModeStatNames.GunCritDamageGain")
    for rel in ALLOWED:
        if rel not in sources:
            errors.append("白名单里的 {} 已不在编译清单里，删掉这一条".format(rel))
    if scanned == 0:
        errors.append("编译清单里一个源文件都没读到")
    if errors:
        for error in errors:
            print("PlayerGunCritRateBonusGuard: " + error)
        return 1
    print("PlayerGunCritRateBonusGuard: PASS ({} files)".format(scanned))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
