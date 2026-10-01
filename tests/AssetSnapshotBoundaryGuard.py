"""真实物品凭据不得单独写盘；保护资产采集、逐项返还与延期欠账边界。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    text = (ROOT / path).read_text(encoding="utf-8")
    return clean_source(text)


def main():
    errors = []
    checks = {
        "ModeH/ModeHSaveFlushCoordinator.cs": [
            r"!journalPending && !_saveFileRequired && !_journalAssetPending",
            r"RefreshAssetCache\(out error\)",
            r"SavesSystem.SaveFile\(false\);[\s\S]*?_saveFileRequired = false;",
            r"CollectAssetSnapshot\(journal, out error\)[\s\S]*?StageWrite\(journal, out error\)[\s\S]*?FlushBatch\(out error, true\)",
        ],
        "ModeH/ModeHInventoryPersistenceBridge.cs": [r'inventory.Save\("PlayerStorage"\)'],
        "ModeH/ModeHWarehouseStakeJournalStorageBuffer.cs": [
            r"journal.slotId", r"PlayerStorageBuffer.SaveBuffer\(\)",
            r"current != _active.inventoryPostDigest", r"ModeHItemTreeNormalizer.TryRestore",
            r'HasAppliedReceipt\("escrow_return", i\)', r"PlayerStorageBuffer.Buffer.Add\(trees\[i\]\)",
        ],
        "PetNest/PetNestSaveCoordinator.cs": [
            r'CharacterItem.Save\("MainCharacterItemData"\)', r'Inventory.Save\("PlayerStorage"\)',
            r"PlayerStorageBuffer.SaveBuffer\(\)", r"EconomyManager.Instance.GenerateSaveData\(\)",
            r"CollectPendingAssets\(out error\)[\s\S]*?Bundle.FlushPending\(\)",
        ],
        "PetNest/PetNestExpeditionService.cs": [r"RequireAssetSnapshot\(out assetError\)[\s\S]*?GrantRewards\(r\)"],
        "PetNest/PetNestPersistence.cs": [r"CollectPendingAssets\(out assetError\)[\s\S]*?_bundle.FlushPending\(true\)"],
        # 天空岛纪念品（CR-2026-09-11-017）：发放前必须先立实物快照义务，落盘时四样官方资产
        # 与剧情手记进同一批；否则「已记账、物品未持久化」会在跨重启窗口里吞掉纪念品。
        "SkyIsland/SkyIslandWorldStoryRewards.cs": [
            # 负判 + 失败跳过 + 之后才发放：只核对顺序不够，快照检查必须是**承重**的。
            r"if \(!story.BeginKeepsakeDelivery\(all\[i\]\.NoteId, out snapshotError\)\)[\s\S]{0,400}?continue;"
            r"[\s\S]*?SkyIslandItems.TryGiveWithReceipt\(",
            r"finally\s*\{\s*story.EndKeepsakeDelivery\(all\[i\]\.NoteId, delivered, buffered\);\s*\}",
        ],
        "SkyIsland/SkyIslandStoryService.cs": [
            r'CharacterItem.Save\("MainCharacterItemData"\)', r'Inventory.Save\("PlayerStorage"\)',
            r"PlayerStorageBuffer.SaveBuffer\(\)", r'SavesSystem.Save<float>\("MainCharacterHealth"',
            r"assetSnapshotRequired = true;",
            # 物理保存成功才清三份采集义务：实物快照、现金、官方任务交付资产（CR-2026-09-27-403）。
            r"OnPhysicalSaveSucceeded\(\)\s*\{\s*owner.assetSnapshotRequired = false;\s*owner.cashSnapshotRequired = false;\s*owner.officialQuestAssetCollector = null;\s*owner.keepsakeAssetCollector = null;\s*owner.raidAssetOwner = null;\s*\}",
            r"HasSnapshotObligation\s*\{\s*get \{ return assetSnapshotRequired \|\| cashSnapshotRequired \|\| officialQuestAssetCollector != null \|\| keepsakeAssetCollector != null \|\| raidAssetOwner != null;\s*\}\s*\}",
            # CR-2026-09-30-005：撤离 / 倒下结算把随出击记录放进待写批次，会话随即提前写盘；
            # 同批必须采集那位出击角色的物品与血量（照官方 SaveMainCharacter），且排在 typed pending 之前。
            r"if \(stored\)\s*\{\s*raidHeldNotes.Clear\(\);[\s\S]{0,120}?if \(keep\) raidAssetOwner = CharacterMainControl.Main;",
            r"if \(!owner.CollectPendingCash\(\)\)[\s\S]{0,200}?CharacterMainControl raider = owner.raidAssetOwner;\s*if \(raider != null\)[\s\S]{0,600}?"
            r'raider.CharacterItem.Save\("MainCharacterItemData"\);\s*SavesSystem.Save<float>\("MainCharacterHealth", raider.Health.CurrentHealth\);',
            r"cashPaidPendingAction = null;\s*raidAssetOwner = null;",
            r"HasSnapshotObligation \{ get \{ return owner.HasSnapshotObligation; \} \}",
            r"if \(store.HasPendingWrite \|\| coordinator.HasDeferredFlush \|\| HasSnapshotObligation\)",
            r"if \(rewardCommitting \|\| officialDeliveryActive \|\| keepsakeDeliveryActive\) return false;",
            r"if \(delivered && buffered\)[\s\S]{0,250}?keepsakeAssetCollector = OfficialQuestItems.AssetCollector\(true\);[\s\S]{0,120}?if \(raidHeldNotes.Remove\(noteId\)\) store.Store\(Current.Copy\(\)\);",
            r"officialQuestAssetCollector == null && keepsakeAssetCollector != null && !keepsakeAssetCollector\(\)",
            r"BeforeCollectSaveData = CollectPendingCash",
            r"if \(!owner.CollectPendingCash\(\)\)",
            # 出击图（天空岛）里没有基地仓库：永久记录随这一趟结算（owner 2026-09-14「随撤离一起存」）。
            # 写盘编码必须剥掉暂不入档的记录，否则任何一次写盘（含官方收集存档）都会把它和出击前的背包一起存下。
            r"Encode = EncodeForSave",
            r"if \(RaidHeldCosts\)[\s\S]{0,200}?raidHeldNotes.Add\(noteId\);[\s\S]{0,40}?return true;",
            r"if \(!raidHeldNotes.Contains\(id\)\) kept.Add\(id\);",
        ],
        "SkyIsland/SkyIslandSession.cs": [
            r"story.RaidHeldCosts = true;",
            # CR-2026-09-30-002：回主菜单也会卸载岛场景，去留改由 KeepsRaidHeldRecords 只认撤离与倒下。
            r'story.SettleRaidHeld\(KeepsRaidHeldRecords\(reason\)\)[\s\S]{0,200}?SkyIslandStorySaveRecovery.CloseOrRetain\(story\)',
        ],
        # 发放顺序：实例造出来了才记台账；只有「确实没有归属、也没有仓库缓冲回执」才回滚台账。
        "Integration/SkyIsland/SkyIslandItems.cs": [
            r"item = ItemAssetsCollection.InstantiateSync\(typeId\);[\s\S]*?recordGrant\(\)",
            r"SkyIslandInventoryTransaction.HasOwner\(item\)",
            r"SkyIslandInventoryTransaction.HasBufferReceipt\(instanceId, typeId\)",
            r"buffered = SkyIslandInventoryTransaction.HasBufferReceipt\(instanceId, typeId\);",
        ],
        # 共享落盘引擎：快照必须排在 typed pending 之前，pending 又排在物理 SaveFile 之前。
        "Common/Lifecycle/BossRushSaveCoordinatorEngine.cs": [
            r"_source.CollectSnapshot\(out snapshotError\)[\s\S]*?_source.FlushPending\(\)"
            r"[\s\S]*?SavesSystem.SaveFile\(false\);",
        ],
    }
    for path, patterns in checks.items():
        for pattern in patterns:
            if not re.search(pattern, read(path)):
                errors.append(path + ": 缺少资产屏障 " + pattern)
    if errors:
        print("AssetSnapshotBoundaryGuard: FAIL\n" + "\n".join(errors))
        return 1
    print("AssetSnapshotBoundaryGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
