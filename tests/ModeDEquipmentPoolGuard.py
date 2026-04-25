"""
Guard: Mode D/E/F 共享发装池应使用放宽后的排除标签、去重入池，并将面具池视为标签并集。

要求：
- InitializeModeDItemPools 必须使用专门的装备池排除标签帮助方法，而不是 BuildGeneralLootExcludeTags(tagsData, true)
- 装备池排除标签不得再包含 Character / DestroyOnLootBox / DontDropOnDeadInSlot
- Tag 搜索结果加入各池时必须统一去重，不能继续直接 AddRange(ids)
- 面具池必须独立并集 Mask / FaceMask / Headset 三类标签，而不是 Mask 缺失时才回退 FaceMask
"""

from pathlib import Path
import sys


MODE_D = Path("ModeD/ModeD.cs")
MODE_D_EQUIPMENT = Path("ModeD/ModeDEquipment.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    mode_d_text = MODE_D.read_text(encoding="utf-8")
    equipment_text = MODE_D_EQUIPMENT.read_text(encoding="utf-8")

    exclude_body = extract_method_body(
        mode_d_text,
        "private List<Duckov.Utilities.Tag> BuildModeDEquipmentPoolExcludeTags(",
    )
    if exclude_body is None:
        return fail("ModeDEquipmentPoolGuard: missing BuildModeDEquipmentPoolExcludeTags helper")

    forbidden_exclude_snippets = [
        "tagsData.Character",
        "tagsData.DestroyOnLootBox",
        "tagsData.DontDropOnDeadInSlot",
    ]
    for snippet in forbidden_exclude_snippets:
        if snippet in exclude_body:
            return fail(
                "ModeDEquipmentPoolGuard: equipment-pool exclude helper still excludes " + snippet
            )

    required_exclude_snippets = [
        "tagsData.LockInDemoTag",
        "TryFindQuestTag(tagsData)",
    ]
    for snippet in required_exclude_snippets:
        if snippet not in exclude_body:
            return fail(
                "ModeDEquipmentPoolGuard: equipment-pool exclude helper missing " + snippet
            )

    init_body = extract_method_body(
        mode_d_text,
        "private void InitializeModeDItemPools()",
    )
    if init_body is None:
        return fail("ModeDEquipmentPoolGuard: missing InitializeModeDItemPools body")

    if "BuildGeneralLootExcludeTags(tagsData, true)" in init_body:
        return fail(
            "ModeDEquipmentPoolGuard: InitializeModeDItemPools still uses BuildGeneralLootExcludeTags(tagsData, true)"
        )

    if "BuildModeDEquipmentPoolExcludeTags(tagsData)" not in init_body:
        return fail(
            "ModeDEquipmentPoolGuard: InitializeModeDItemPools does not use BuildModeDEquipmentPoolExcludeTags(tagsData)"
        )

    if "private static void AddDistinctItemIds(List<int> targetPool, int[] ids)" not in mode_d_text:
        return fail("ModeDEquipmentPoolGuard: missing AddDistinctItemIds helper")

    if "AddRange(ids)" in init_body:
        return fail(
            "ModeDEquipmentPoolGuard: InitializeModeDItemPools still directly AddRange(ids) instead of deduping"
        )

    required_mask_union_snippets = [
        'Duckov.Utilities.Tag maskTag = FindTagByNameInInit("Mask");',
        'Duckov.Utilities.Tag faceMaskTag = FindTagByNameInInit("FaceMask");',
        'Duckov.Utilities.Tag headsetTag = FindTagByNameInInit("Headset");',
        "SearchItemsByTag(maskTag, excludeArray)",
        "SearchItemsByTag(faceMaskTag, excludeArray)",
        "SearchItemsByTag(headsetTag, excludeArray)",
    ]
    for snippet in required_mask_union_snippets:
        if snippet not in init_body:
            return fail("ModeDEquipmentPoolGuard: mask-pool union missing snippet -> " + snippet)

    if 'if (maskTag == null) maskTag = FindTagByNameInInit("FaceMask");' in init_body:
        return fail(
            "ModeDEquipmentPoolGuard: mask pool still uses fallback semantics instead of Mask/FaceMask union"
        )

    accessory_body = extract_method_body(
        equipment_text,
        "private void InitializeAccessoryPool()",
    )
    if accessory_body is None:
        return fail("ModeDEquipmentPoolGuard: missing InitializeAccessoryPool body")

    if "modeDAccessoryPool.AddRange(accessoryIds);" in accessory_body:
        return fail(
            "ModeDEquipmentPoolGuard: accessory pool still directly AddRange(accessoryIds) instead of deduping"
        )

    if "AddDistinctItemIds(modeDAccessoryPool, accessoryIds);" not in accessory_body:
        return fail(
            "ModeDEquipmentPoolGuard: accessory pool does not use AddDistinctItemIds(modeDAccessoryPool, accessoryIds)"
        )

    print("ModeDEquipmentPoolGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
