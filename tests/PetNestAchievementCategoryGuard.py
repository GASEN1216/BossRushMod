#!/usr/bin/env python3
"""
PetNestAchievementCategoryGuard — 驯养成就分类守卫（实施计划 步骤 12）。

不变式：
- `AchievementCategory` 的新分类必须**追加到枚举末尾**：分类排序与存档都依赖
  int 值，插在中间会让老档里已解锁成就的分类整体错位；
- 既有七个分类的相对顺序不得改动；
- `Taming` 是最后一项；
- 驯养成就走 Register 注册且都用 AchievementCategory.Taming；
- 成就 id 一律以 `petnest_` 前缀，便于与其它分类区分与未来批量处理；
- 成就解锁走 BossRushAchievementManager.TryUnlock（幂等，不重复发奖）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    read_petnest,
    read_text,
    repo_path,
    report,
    strip_cs_comments,
)

GUARD = "PetNestAchievementCategoryGuard"

# 冻结前缀：既有七项 + Taming（遗种巢）。
# 本 guard 守的是「Taming 的 int 值不许漂移」，不是「Taming 永远是最后一项」——
# 后续系统会继续往末尾追加（2026-08-30 已追加 Codex 图鉴）。断言因此改为：
# 前缀必须逐字相等，且 Taming 之后只允许新增、不允许插入或重排。
FROZEN_PREFIX = [
    "Basic", "Flawless", "Speedrun", "BossKill",
    "Cumulative", "Special", "Ultimate", "Taming",
]

EXPECTED_ACHIEVEMENTS = [
    "petnest_first_hatch",
    "petnest_lineage_10",
    "petnest_lineage_30",
    "petnest_shiny",
    "petnest_memorial",
]


def main():
    errors = []

    defs = read_text(repo_path("Achievement", "BossRushAchievementDef.cs"))
    if defs is None:
        return report(GUARD, ["[File] 缺少 Achievement/BossRushAchievementDef.cs"])
    dcode = strip_cs_comments(defs)

    block = re.search(r"public enum AchievementCategory[\s\S]*?\n    \}", dcode)
    if block is None:
        errors.append("[枚举] 无法解析 AchievementCategory")
    else:
        names = re.findall(r"^\s*(\w+)\s*,?\s*$", block.group(0), re.MULTILINE)
        # 去掉声明行与花括号残留
        names = [n for n in names if n not in ("public", "enum", "AchievementCategory")]
        if names[:len(FROZEN_PREFIX)] != FROZEN_PREFIX:
            errors.append("[枚举稳定性] 冻结前缀必须是 " + repr(FROZEN_PREFIX)
                          + "，当前是 " + repr(names[:len(FROZEN_PREFIX)])
                          + "。老档里已解锁成就的分类靠 int 值定位，插入或重排会整体错位。")
        if len(names) < len(FROZEN_PREFIX):
            errors.append("[枚举稳定性] 分类被删除：" + repr(names))

    manager = read_text(repo_path("Achievement", "BossRushAchievementManager.cs"))
    if manager is None:
        errors.append("[File] 缺少 Achievement/BossRushAchievementManager.cs")
    else:
        mcode = strip_cs_comments(manager)
        for achievement in EXPECTED_ACHIEVEMENTS:
            if '"' + achievement + '"' not in mcode:
                errors.append("[注册] 缺少驯养成就: " + achievement)
        taming_count = mcode.count("AchievementCategory.Taming")
        if taming_count < len(EXPECTED_ACHIEVEMENTS):
            errors.append("[注册] 驯养成就必须都用 AchievementCategory.Taming，当前只有 "
                          + str(taming_count) + " 处")

    stats = read_petnest("PetNestMuseumStats.cs")
    if stats is None:
        errors.append("[File] 缺少 PetNest/PetNestMuseumStats.cs")
    else:
        scode = strip_cs_comments(stats)
        for achievement in EXPECTED_ACHIEVEMENTS:
            if 'TryUnlock("' + achievement + '")' not in scode:
                errors.append("[解锁] 缺少解锁调用: " + achievement)
        if "BossRushAchievementManager.TryUnlock(" not in scode:
            errors.append("[解锁] 必须走 BossRushAchievementManager.TryUnlock（幂等）")
        # 解锁判据必须来自统计而不是散落的临时计数
        for source in ["UnlockedLineageCount", "ShinyCount", "MemorialCount"]:
            if source not in scode:
                errors.append("[判据] 成就判据必须来自统计查询: " + source)

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
