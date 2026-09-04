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
