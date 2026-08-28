#!/usr/bin/env python3
"""
PetNestPetProxyBridgeGuard — 遗种巢捡漏背包借席桥守卫（实施计划 步骤 0 / 步骤 6）。

不变式：
- 全案唯一反射写点：只有 PetNestPetProxyBridge 允许触碰
  LevelManager 的 petCharacter 私有字段；其余 PetNest 文件一律不得出现该字段名；
- 占席 / 还席成对：TryBorrowSeat 与 ReleaseSeat 都存在，还席走 finally 兜底；
- 让位规则存在且成文：基地图不借席 + 席位被他方随从占用时不抢；
- 借席不夺席：必须记录原占位者并在还席时写回；
- 还席前必须核对席位仍是自己借出去的那只随从，否则不覆盖；
- fail-closed：反射解析失败 / LevelManager 缺失一律返回 false，不抛异常；
- 反射目标字段名以常量登记，供版本升级检查单复查。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    PETNEST_DIR,
    read_petnest,
    read_text,
    report,
    strip_cs_comments,
)

GUARD = "PetNestPetProxyBridgeGuard"
BRIDGE_FILE = "PetNestPetProxyBridge.cs"


def main():
    errors = []

    bridge = read_petnest(BRIDGE_FILE)
    if bridge is None:
        return report(GUARD, ["[File] 缺少 PetNest/" + BRIDGE_FILE])
    code = strip_cs_comments(bridge)

    # 1. 反射目标以常量登记
    if not re.search(r'internal const string PetCharacterFieldName = "petCharacter";', code):
        errors.append("[反射] 缺少常量登记 PetCharacterFieldName = \"petCharacter\"")
    if not re.search(r"AccessTools\.Field\(typeof\(LevelManager\), PetCharacterFieldName\)", code):
        errors.append("[反射] 必须通过 AccessTools.Field(typeof(LevelManager), PetCharacterFieldName) 解析")

    # 2. 反射解析 fail-closed
    if not re.search(r"if \(_petCharacterField == null\)", code):
        errors.append("[fail-closed] 字段解析失败必须显式分支并降级")
    if not re.search(r"_petCharacterField\.FieldType != typeof\(CharacterMainControl\)", code):
        errors.append("[fail-closed] 必须校验字段类型未被官方改动")
    if not re.search(r'yieldReason = "reflection_unavailable";', code):
        errors.append("[fail-closed] 反射不可用时必须给出 reflection_unavailable 让席原因")

    # 3. 占席 / 还席成对
    if not re.search(r"internal static bool TryBorrowSeat\(CharacterMainControl companion, out string yieldReason\)", code):
        errors.append("[成对] 缺少 TryBorrowSeat(CharacterMainControl, out string)")
    if not re.search(r"internal static void ReleaseSeat\(\)", code):
        errors.append("[成对] 缺少 ReleaseSeat()")
    if not re.search(r"finally\s*\{[\s\S]{0,300}?_seatBorrowed = false;", code):
        errors.append("[成对] ReleaseSeat 必须在 finally 中清状态，异常也要还原")

    # 4. 让位规则成文
    if not re.search(r'yieldReason = "base_level_official_pet_priority";', code):
        errors.append("[让位] 缺少基地图让位规则（官方 PetHouse 优先）")
    if not re.search(r'yieldReason = "seat_held_by_other_companion";', code):
        errors.append("[让位] 缺少「席位已被他方随从占用则不抢」规则")

    # 5. 借席不夺席：记录原占位者并写回
    if not re.search(r"_previousOccupant = current;", code):
        errors.append("[还原] 借席时必须记录原占位者")
    if not re.search(r"field\.SetValue\(level, _previousOccupant\);", code):
        errors.append("[还原] 还席时必须把原占位者写回")

    # 6. 还席前核对身份
    if not re.search(r"if \(current == _borrowedFor\)", code):
        errors.append("[还原] 还席前必须核对席位仍是自己借出去的那只随从")

    # 7. 全案唯一反射写点：其余 PetNest 文件不得出现 petCharacter 字段名
    for name in sorted(os.listdir(PETNEST_DIR)):
        if not name.endswith(".cs") or name == BRIDGE_FILE:
            continue
        other = read_text(os.path.join(PETNEST_DIR, name))
        if other is None:
            continue
        if '"petCharacter"' in strip_cs_comments(other):
            errors.append("[唯一反射写点] " + name + " 不得直接触碰 petCharacter 私有字段")

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
