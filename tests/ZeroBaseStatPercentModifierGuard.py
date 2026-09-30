"""基础值为 0 的角色属性不能挂百分比修饰器。

背景（2026-09-30 owner 实测）：龙王之冕写的是「GunCritDamageGain PercentageAdd +0.15」，
属性栏照常显示「+15%」，爆头伤害却一点没加。官方 `Stat.Recalculate` 里 PercentageAdd / PercentageMultiply
是在已有结果上乘 `(1 + 值)`，而角色的暴击类属性基础值是 0（官方按 `(1 + GunCritDamageGain)` 乘进爆头伤害），
0 乘什么都是 0。赤龙首同一写法、同一个坑。这类加成必须用 `ModifierType.Add`
（与变异词条「致命一击」的 `Add +0.5` = +50% 暴伤同一口径）。

判据：编译清单里的生产源码，剥注释后按语句切分；同一条语句里同时出现下列属性键的字符串字面量
和 `ModifierType.Percentage`，即判红。属性键由变量传入的调用点查不到，靠审查。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
sys.path.insert(0, str(ROOT / "tests"))

from compile_list import read_compile_sources  # noqa: E402
from cs_source_util import clean_source  # noqa: E402

# 角色身上基础值为 0、只能靠 Add 叠上去的属性（官方公式都是 1 + 值 或直接相加）。
ZERO_BASE_STATS = ("GunCritDamageGain", "MeleeCritDamageGain", "GunCritRateGain", "MeleeCritRateGain")
KEY_RE = re.compile(r'"(' + "|".join(ZERO_BASE_STATS) + r')"')


def main():
    errors = []
    scanned = 0
    for rel in read_compile_sources():
        path = ROOT / rel
        if not path.is_file():
            continue
        scanned += 1
        code = clean_source(path.read_text(encoding="utf-8-sig"))
        for statement in code.split(";"):
            if "ModifierType.Percentage" not in statement:
                continue
            match = KEY_RE.search(statement)
            if match:
                errors.append("{}: {} 挂了百分比修饰器（基础值为 0，乘了等于没加，改用 ModifierType.Add）".format(
                    rel, match.group(1)))
    if scanned == 0:
        errors.append("编译清单里一个源文件都没读到")
    if errors:
        for error in errors:
            print("ZeroBaseStatPercentModifierGuard: " + error)
        return 1
    print("ZeroBaseStatPercentModifierGuard: PASS ({} files)".format(scanned))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
