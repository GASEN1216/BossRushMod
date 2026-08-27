#!/usr/bin/env python3
"""
ModeHVirtualLoadoutGuard — Mode H 虚拟整备守卫（设计提案 §17.7、§26.1）。

不变式：
- LoadoutKits.json schema、自洽 contentSignature、稳定 kitId 唯一；
- isStarterKit / starterOrder 存在且 starter 顺序唯一；
- 每个生产原型至少两件槽位不冲突的 starter kit；
- 空整备合法（不得强制至少一件）；
- gameQuality 落在 Q1-Q8，解析区间同样落在 Q1-Q8；
- 只允许 PrimaryWeapon/SecondaryWeapon/MeleeWeapon/Armor/Helmat 五个槽位；
- 每名选手最多 4 件 kit；
- 枪械槽必须冻结弹药（固定 ammoTypeId 或按口径解析 + 数量）；
- 禁用类型断言存在，含 controlMindType != none 武器（§17.6.5）；
- 只有 kit applicator 可以访问 owner 标记的临时选手 slots/inventory；
  kit registry 与其它 Mode H 文件不得引用 CharacterMainControl.Main / PlayerStorage / 玩家 ItemTreeData。
"""
import io
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_canonical_json import content_signature  # noqa: E402
from modeh_guard_util import contains_symbol, read_text  # noqa: E402

KITS_JSON = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "LoadoutKits.json")
KIT_REGISTRY = os.path.join(REPO_ROOT, "ModeH", "ModeHLoadoutKitRegistry.cs")
KIT_APPLICATOR = os.path.join(REPO_ROOT, "ModeH", "ModeHLoadoutKitApplicator.cs")
CONFIG = os.path.join(REPO_ROOT, "ModeH", "ModeHConfig.cs")

ALLOWED_SLOTS = ["Armor", "Helmat", "MeleeWeapon", "PrimaryWeapon", "SecondaryWeapon"]
ARCHETYPES = ["assault", "finisher", "ranged", "sustain", "tank"]
WEAPON_SLOTS = ["PrimaryWeapon", "SecondaryWeapon"]
REQUIRED_KIT_FIELDS = [
    "kitId", "isStarterKit", "starterOrder", "replaceSlot", "typeId", "gameQuality",
    "ammoTypeId", "ammoCount", "compatibleArchetypeIds", "compatibleProfileIds",
]


def main():
    errors = []

    if not os.path.exists(KITS_JSON):
        print("ModeHVirtualLoadoutGuard: FAIL (1 errors)")
        print("  - 缺少 Assets/Data/ModeH/LoadoutKits.json")
        return 1

    with io.open(KITS_JSON, "r", encoding="utf-8") as fh:
        try:
            document = json.load(fh)
        except ValueError as exc:
            print("ModeHVirtualLoadoutGuard: FAIL (1 errors)")
            print("  - LoadoutKits.json 解析失败: {}".format(exc))
            return 1

    if document.get("schemaVersion") != 1:
        errors.append("[Schema] schemaVersion 必须为 1")
    if not document.get("documentId"):
        errors.append("[Schema] 缺少稳定 documentId")
    declared = document.get("contentSignature", "")
    computed = content_signature(document)
    if declared != computed:
        errors.append("[Signature] contentSignature 不自洽: 声明={} 计算={}".format(
            declared or "(空)", computed))

    if document.get("maxKitsPerFighter") != 4:
        errors.append("[Cap] maxKitsPerFighter 必须为 4")
    allowed = document.get("allowedSlots") or []
    if sorted(allowed) != sorted(ALLOWED_SLOTS):
        errors.append("[Slots] allowedSlots 必须恰好是五个冻结槽位")
    forbidden_rules = " ".join(document.get("forbiddenRules") or [])
    if "controlMindType" not in forbidden_rules:
        errors.append("[Forbidden] 未声明禁止 controlMindType 武器")

    kits = document.get("kits") or []
    if not kits:
        errors.append("[Kits] kits 不得为空")

    kit_ids = set()
    starter_orders = set()
    starter_slots_by_archetype = {a: set() for a in ARCHETYPES}

    for kit in kits:
        kit_id = kit.get("kitId")
        for field in REQUIRED_KIT_FIELDS:
            if field not in kit:
                errors.append("[Kit] {} 缺少字段: {}".format(kit_id, field))
        if not kit_id or kit_id in kit_ids:
            errors.append("[Kit] kitId 缺失或重复: " + str(kit_id))
            continue
        kit_ids.add(kit_id)

        slot = kit.get("replaceSlot")
        if slot not in ALLOWED_SLOTS:
            errors.append("[Kit] {} 槽位不在白名单: {}".format(kit_id, slot))

        quality = kit.get("gameQuality")
        if not isinstance(quality, int) or quality < 1 or quality > 8:
            errors.append("[Kit] {} gameQuality 必须在 Q1-Q8".format(kit_id))

        type_id = kit.get("typeId", 0)
        resolve_tags = kit.get("resolveTags") or []
        if not (isinstance(type_id, int) and type_id > 0) and not resolve_tags:
            errors.append("[Kit] {} 既没有固定 typeId 也没有解析标签".format(kit_id))
        if resolve_tags:
            min_q = kit.get("resolveMinQuality")
            max_q = kit.get("resolveMaxQuality")
            ordinal = kit.get("resolveOrdinal")
            if not isinstance(min_q, int) or not isinstance(max_q, int) \
                    or min_q < 1 or max_q > 8 or min_q > max_q:
                errors.append("[Kit] {} 解析品质区间非法".format(kit_id))
            if not isinstance(ordinal, int) or ordinal < 0:
                errors.append("[Kit] {} resolveOrdinal 非法".format(kit_id))

        if slot in WEAPON_SLOTS:
            ammo_ok = (kit.get("ammoTypeId", 0) > 0) or kit.get("resolveAmmoByCaliber") is True
            if not ammo_ok or kit.get("ammoCount", 0) <= 0:
                errors.append("[Kit] {} 枪械槽必须冻结兼容弹药及数量".format(kit_id))

        if kit.get("isStarterKit"):
            order = kit.get("starterOrder")
            if not isinstance(order, int) or order <= 0 or order in starter_orders:
                errors.append("[Kit] {} starterOrder 非法或重复".format(kit_id))
            else:
                starter_orders.add(order)
            compatible = kit.get("compatibleArchetypeIds") or ARCHETYPES
            for archetype in compatible:
                if archetype in starter_slots_by_archetype:
                    starter_slots_by_archetype[archetype].add(slot)

    for archetype in ARCHETYPES:
        slots = starter_slots_by_archetype[archetype]
        if len(slots) < 2:
            errors.append("[Starter] 原型 {} 的槽位不冲突 starter kit 少于 2 件".format(archetype))

    registry = read_text(KIT_REGISTRY)
    if registry is None:
        errors.append("[Code] 缺少 ModeH/ModeHLoadoutKitRegistry.cs")
    else:
        checks = [
            (r"public static bool ValidateSelection\(", "kit 选择校验入口"),
            (r"kit_slot_conflict", "槽位冲突拒绝"),
            (r"kit_count_exceeded", "数量上限拒绝"),
            (r"MaxKitsPerFighter", "引用 4 件上限常量"),
            (r"MinStarterKitsPerArchetype", "引用 starter 覆盖常量"),
            (r"if \(kitIds == null \|\| kitIds\.Count == 0\) return true;", "空整备合法"),
            (r"ItemAssetsCollection\.Search\(filter\)", "按官方检索确定性解析 typeId"),
            (r"sorted\.Sort\(\);", "候选按 typeId 升序后取 ordinal"),
            (r"resolve_quality_out_of_range", "解析品质越界 fail-closed"),
        ]
        for pattern, desc in checks:
            if not re.search(pattern, registry):
                errors.append("[Code] 不满足: " + desc)
        for forbidden in ["CharacterMainControl.Main", "PlayerStorage", "ItemTreeData"]:
            if contains_symbol(registry, forbidden):
                errors.append("[Isolation] kit registry 不得引用玩家资产符号: " + forbidden)

    applicator = read_text(KIT_APPLICATOR)
    if applicator is not None:
        if contains_symbol(applicator, "PlayerStorage") or contains_symbol(applicator, "ItemTreeData"):
            errors.append("[Isolation] kit applicator 不得引用 PlayerStorage / 玩家 ItemTreeData")
        if contains_symbol(applicator, "CharacterMainControl.Main"):
            errors.append("[Isolation] kit applicator 不得解析 CharacterMainControl.Main")
        if "controlMindType" not in applicator:
            errors.append("[Forbidden] kit applicator 缺少 controlMindType 武器禁入断言")

    config = read_text(CONFIG)
    if config:
        if not re.search(r"public const int MaxKitsPerFighter = 4;", config):
            errors.append("[Config] MaxKitsPerFighter 未冻结为 4")
        if not re.search(r"public const int MinStarterKitsPerArchetype = 2;", config):
            errors.append("[Config] MinStarterKitsPerArchetype 未冻结为 2")
        if not re.search(r"public const int MinGameQuality = 1;", config) \
                or not re.search(r"public const int MaxGameQuality = 8;", config):
            errors.append("[Config] 品质区间未冻结为 Q1-Q8")

    if errors:
        print("ModeHVirtualLoadoutGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHVirtualLoadoutGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
