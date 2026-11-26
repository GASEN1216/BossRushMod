#!/usr/bin/env python3
"""
EquipmentRescueEvidenceGuard — 读档没装上的 Mod 装备：只按证据补回，补不回就原样保住（2026-11-26 玩家反馈）。

官方 ItemTreeData.InstantiateAsync 对查不到 prefab 的节点造 FallbackItem_<id> 空壳，挂槽时丢弃 Slot.Plug 的返回值；
空壳插不进装备槽，成了孤儿，官方下次 SaveMainCharacter 只写挂在角色身上的东西，这件装备就没了。

不变式：
1. 触发：注册失败交出空壳时（NeedsFallback 为真）记一笔，关卡初始化后检查一次；宿主迟到激活时检查一次
   （迟到 = 激活时已身处关卡场景，判定在 HostStartupIsolationGuard）。
2. 记录源：只读官方角色记录 Item/MainCharacterItemData（与反编译源一致），要求开关 LevelConfig.SaveCharacter、
   记录根 TypeID 与活角色一致；本类型从不直接写存档、不调 SaveMainCharacter。
3. 证据：只有「记录里是 Mod 物品、活角色对应槽为空、场景里有同 TypeID 的空壳孤儿」才处理；孤儿必须
   不在任何父物品、背包或槽里。没证据的不碰，避免把玩家本局卸下或卖掉的装备再变出来。孤儿扫描覆盖已加载场景里
   未激活的对象。
4. 处理顺序：从记录重建整棵子树 → 核对存档槽、关卡、主角、角色物品都没变且原父物品还在（变了就销毁重建品、
   整轮作废，不把旧档装备送给新档角色）→ 销毁旧孤儿（一份记录不留两份实物）→ 真物品先插回原槽（插得进且确实插上）、
   再进背包 → 空壳或放不回的进仓库（官方整棵序列化保存）。
5. 生命周期：订阅幂等，Shutdown 退订并作废在途任务。玩家可见提示中英双语。
每条断言都有内存变异探针，探针不转红本守卫自判失败。行为正确性仍以 Windows 编译与实机为准。
"""
import os
import re
import sys

from guard_mutation_util import REPO_ROOT, cleaned, method_body, read_raw, run_guard, squash

FILES = {
    "rescue": "Integration/BossRushEquipmentRescue.cs",
    "patch": "Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs",
}
OFFICIAL = {
    "level": ("鸭科夫源码/TeamSoda.Duckov.Core/LevelManager.cs", 'MainCharacterItemSaveKey = "MainCharacterItemData"'),
    "prefix": ("鸭科夫源码/TeamSoda.Duckov.Core/Saves/ItemSavesUtilities.cs", 'ItemPrefix = "Item/"'),
    "fallback": ("鸭科夫源码/ItemStatsSystem/ItemAssetsCollection.cs", 'string.Format("FallbackItem_{0}", typeID)'),
}

RESCUE_ORDER = [
    "if (!LevelConfig.SaveCharacter) return;",
    "int slot = SavesSystem.CurrentSlot;",
    "SavesSystem.Load<ItemTreeData>(CharacterItemSavePrefix, CharacterItemSaveKey)",
    "if (storedRoot == null || storedRoot.typeID != root.TypeID) return;",
    "CollectMissing(lookup, storedRoot, root, candidates, 0);",
    "Dictionary<int, List<Item>> orphans = FindOrphanShells();",
    "if (!orphans.TryGetValue(candidate.Entry.typeID, out pool) || pool.Count == 0) { skipped++; continue; }",
    "rebuilt = await ItemTreeData.InstantiateAsync(ExtractSubtree(lookup, candidate.Entry));",
    "if (!Alive(host, gen) || !SameContext(slot, level, main, root, candidate.LiveParent)) { if (rebuilt != null) DestroyQuietly(rebuilt);",
    "if (rebuilt == null) { skipped++; continue; }",
    "DestroyQuietly(orphan);",
    "bool real = !IsFallbackShell(rebuilt);",
    "if (real && (TryPlug(candidate.LiveParent, candidate.SlotKey, rebuilt) || ItemUtilities.SendToPlayerCharacterInventory(rebuilt, true)))",
    "ItemUtilities.SendToPlayerStorage(rebuilt, false);",
]


def check(raw):
    src = cleaned(raw)
    rescue, patch = src["rescue"], src["patch"]
    errors = []

    for token in ('CharacterItemSavePrefix = "Item/"', 'CharacterItemSaveKey = "MainCharacterItemData"',
                  'FallbackNamePrefix = "FallbackItem_"'):
        if token not in rescue:
            errors.append("[记录源] 常量与官方不一致或缺失: " + token)

    needs = squash(method_body(patch, "internal static bool NeedsFallback(int typeID)"))
    gate = needs.find("if (!BossRushDynamicItemRegistry.IsBossRushDynamicItemType(typeID) || Ensure(typeID)) return false;")
    note = needs.find("BossRushEquipmentRescue.NoteUnresolvedLoad();")
    if gate < 0 or note < gate or needs.find("return true;", note) < 0:
        errors.append("[触发] NeedsFallback 交出空壳前必须记一笔 NoteUnresolvedLoad")

    body = squash(method_body(rescue, "private static async UniTask RescueMainCharacterAsync(ModBehaviour host, int gen, string reason)"))
    at = -1
    for token in RESCUE_ORDER:
        found = body.find(token, at + 1)
        if found < 0:
            errors.append("[顺序] RescueMainCharacterAsync 缺少或顺序不对: " + token)
            continue
        at = found

    orphans = squash(method_body(rescue, "private static Dictionary<int, List<Item>> FindOrphanShells()"))
    for token in ("if (item == null || !item.gameObject.scene.IsValid()) continue;",
                  "if (!BossRushDynamicItemRegistry.IsBossRushDynamicItemType(item.TypeID) || !IsFallbackShell(item)) continue;",
                  "if (item.ParentObject != null || item.InInventory != null || item.PluggedIntoSlot != null) continue;"):
        if token not in orphans:
            errors.append("[证据] 孤儿判定缺少条件: " + token)
    if "return item != null && item.name == FallbackNamePrefix + item.TypeID;" not in squash(
            method_body(rescue, "private static bool IsFallbackShell(Item item)")):
        errors.append("[证据] 空壳只认官方 FallbackItem_<TypeID> 命名")

    if "FindObjectsByType<Item>(FindObjectsInactive.Include, FindObjectsSortMode.None);" not in orphans:
        errors.append("[证据] 孤儿扫描必须覆盖已加载场景里未激活的对象")

    same = squash(method_body(rescue, "private static bool SameContext(int slot, LevelManager level, CharacterMainControl main, Item root, Item liveParent)"))
    for token in ("if (SavesSystem.CurrentSlot != slot) return false;",
                  "if (level == null || !ReferenceEquals(LevelManager.Instance, level)) return false;",
                  "if (main == null || !ReferenceEquals(level.MainCharacter, main)) return false;",
                  "if (root == null || !ReferenceEquals(main.CharacterItem, root)) return false;",
                  "return liveParent != null;"):
        if token not in same:
            errors.append("[身份] 重建完成后必须核对存档槽、关卡、主角、角色物品与原父物品: " + token)

    collect = squash(method_body(rescue, "private static void CollectMissing("))
    if "if (liveChild.TypeID == child.typeID) CollectMissing(lookup, child, liveChild, output, depth + 1); continue;" not in collect:
        errors.append("[证据] 槽里换了别的物品时不得当成候选")
    if "if (slot != null && BossRushDynamicItemRegistry.IsBossRushDynamicItemType(child.typeID))" not in collect:
        errors.append("[证据] 只有记录里的 Mod 物品才是候选")

    plug = squash(method_body(rescue, "private static bool TryPlug(Item parent, string slotKey, Item item)"))
    if "if (slot == null || slot.Content != null || !slot.CanPlug(item)) return false;" not in plug \
            or "return slot.Plug(item, out unplugged) && ReferenceEquals(slot.Content, item);" not in plug:
        errors.append("[处理] 插回原槽必须先查空槽与 CanPlug，并确认真的插上了")

    for forbidden in ("SavesSystem.Save", "SaveMainCharacter", "SavesSystem.SaveFile"):
        if forbidden in rescue:
            errors.append("[记录源] 装备抢救不得直接写存档: " + forbidden)

    attach = squash(method_body(rescue, "internal static void Attach(ModBehaviour host, bool characterAlreadyLoaded)"))
    if "if (!levelSubscribed)" not in attach or "LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized;" not in attach \
            or 'if (characterAlreadyLoaded) Schedule("late-activation");' not in attach:
        errors.append("[生命周期] Attach 必须幂等订阅关卡事件，迟到激活时检查一次")
    shutdown = squash(method_body(rescue, "internal static void Shutdown()"))
    if "LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized;" not in shutdown or "generation++;" not in shutdown:
        errors.append("[生命周期] Shutdown 必须退订并作废在途任务")

    if len(re.findall(r"NotificationText\.Push\(string\.Format\(L10n\.T\(", rescue)) != 2:
        errors.append("[文案] 两条玩家提示都要走 L10n.T 中英双语")
    return errors


def check_official():
    """反编译源只读、不进公开仓库：本机有时核对常量，没有时如实标 SKIP。"""
    notes = []
    for key, (path, token) in OFFICIAL.items():
        full = os.path.join(REPO_ROOT, path)
        if not os.path.exists(full):
            notes.append("SKIP 官方源核对（本机没有 %s）" % path)
            continue
        if token not in read_raw(path):
            notes.append("FAIL 官方源已变: %s 里找不到 %s" % (path, token))
    return notes


PROBES = [
    ("rescue", 'CharacterItemSaveKey = "MainCharacterItemData"', 'CharacterItemSaveKey = "MainCharacterItem"', "存档键写错", "[记录源]"),
    ("patch", "BossRushEquipmentRescue.NoteUnresolvedLoad();", "", "注册失败不再记一笔", "[触发]"),
    ("rescue", "if (!LevelConfig.SaveCharacter) return;", "", "不看 SaveCharacter 开关", "[顺序]"),
    ("rescue", "if (storedRoot == null || storedRoot.typeID != root.TypeID) return;", "if (storedRoot == null) return;",
     "不核对记录根", "[顺序]"),
    ("rescue", "if (!orphans.TryGetValue(candidate.Entry.typeID, out pool) || pool.Count == 0)",
     "if (!orphans.TryGetValue(candidate.Entry.typeID, out pool))", "没有孤儿证据也处理", "[顺序]"),
    ("rescue", "                DestroyQuietly(orphan);\n", "", "重建后不销毁旧孤儿", "[顺序]"),
    ("rescue", "ItemUtilities.SendToPlayerStorage(rebuilt, false);", "DestroyQuietly(rebuilt);", "空壳不进仓库保住", "[顺序]"),
    ("rescue", "if (item.ParentObject != null || item.InInventory != null || item.PluggedIntoSlot != null) continue;",
     "if (item.ParentObject != null) continue;", "背包 / 槽里的空壳也当孤儿", "[证据]"),
    ("rescue", "if (liveChild.TypeID == child.typeID) CollectMissing(lookup, child, liveChild, output, depth + 1);",
     "CollectMissing(lookup, child, liveChild, output, depth + 1);", "槽里换了东西也往下找", "[证据]"),
    ("rescue", "return slot.Plug(item, out unplugged) && ReferenceEquals(slot.Content, item);",
     "slot.Plug(item, out unplugged); return true;", "插槽结果不核对", "[处理]"),
    ("rescue", "                bool real = !IsFallbackShell(rebuilt);\n",
     "                bool real = !IsFallbackShell(rebuilt);\n                SavesSystem.SaveFile(false);\n", "直接写存档", "[记录源]"),
    ("rescue", " || !SameContext(slot, level, main, root, candidate.LiveParent)", "", "重建完成后不核对身份", "[顺序]"),
    ("rescue", "if (SavesSystem.CurrentSlot != slot) return false;", "", "换了存档槽也照放", "[身份]"),
    ("rescue", "if (main == null || !ReferenceEquals(level.MainCharacter, main)) return false;", "", "换了主角也照放", "[身份]"),
    ("rescue", "return liveParent != null;", "return true;", "原父物品没了也照放", "[身份]"),
    ("rescue", "FindObjectsInactive.Include", "FindObjectsInactive.Exclude", "孤儿扫描漏掉未激活对象", "[证据]"),
    ("rescue", "LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized;", "", "不退订关卡事件", "[生命周期]"),
    ("rescue", 'if (characterAlreadyLoaded) Schedule("late-activation");', "", "迟到激活不检查", "[生命周期]"),
]


if __name__ == "__main__":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    notes = check_official()
    for line in notes:
        print("  - " + line)
    code = run_guard("EquipmentRescueEvidenceGuard", FILES, check, PROBES)
    sys.exit(1 if any(line.startswith("FAIL") for line in notes) else code)
