"""真实物品凭据不得单独写盘；保护资产采集、逐项返还与延期欠账边界。"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    text = (ROOT / path).read_text(encoding="utf-8")
    return re.sub(r"//[^\n]*|/\*[\s\S]*?\*/", "", text)


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
        "PetNest/PetNestPersistence.cs": [r"CollectPendingAssets\(out assetError\)[\s\S]*?_bundle.FlushPending\(\)"],
        # 天空岛纪念品（CR-2026-09-11-017）：发放前必须先立实物快照义务，落盘时四样官方资产
        # 与剧情手记进同一批；否则「已记账、物品未持久化」会在跨重启窗口里吞掉纪念品。
        "DebugAndTools/SkyIsland/SkyIslandWorldStory.cs": [
            # 负判 + 失败跳过 + 之后才发放：只核对顺序不够，快照检查必须是**承重**的。
            r"if \(!story.RequireAssetSnapshot\(out snapshotError\)\)[\s\S]{0,400}?continue;"
            r"[\s\S]*?SkyIslandItems.TryGive\(",
        ],
        "DebugAndTools/SkyIsland/SkyIslandStoryService.cs": [
            r'CharacterItem.Save\("MainCharacterItemData"\)', r'Inventory.Save\("PlayerStorage"\)',
            r"PlayerStorageBuffer.SaveBuffer\(\)", r'SavesSystem.Save<float>\("MainCharacterHealth"',
            r"assetSnapshotRequired = true;",
            r"OnPhysicalSaveSucceeded\(\) \{ owner.assetSnapshotRequired = false; \}",
        ],
        # 发放顺序：实例造出来了才记台账；只有「确实没有归属、也没有仓库缓冲回执」才回滚台账。
        "Integration/SkyIsland/SkyIslandItems.cs": [
            r"item = ItemAssetsCollection.InstantiateSync\(typeId\);[\s\S]*?recordGrant\(\)",
            r"SkyIslandInventoryTransaction.HasOwner\(item\)",
            r"SkyIslandInventoryTransaction.HasBufferReceipt\(instanceId, typeId\)",
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
