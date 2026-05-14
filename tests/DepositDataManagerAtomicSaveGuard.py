"""Guard: DepositDataManager save/load must use committed snapshots with backup recovery."""

from pathlib import Path
import sys


SOURCE = Path("Integration/NPCs/Courier/DepositDataManager.cs")


def fail(message: str) -> int:
    print("DepositDataManagerAtomicSaveGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def require_in_order(block: str, tokens: list[str], label: str) -> int:
    cursor = -1
    for token in tokens:
        next_index = block.find(token, cursor + 1)
        if next_index < 0:
            return fail(label + " missing ordered token: " + token)
        cursor = next_index
    return 0


def main() -> int:
    if not SOURCE.exists():
        return fail("missing source")

    text = SOURCE.read_text(encoding="utf-8")

    for token in [
        'KEY_COMMIT_GENERATION = "BossRush_Deposit_CommitGeneration"',
        'KEY_ITEMS_GENERATION = "BossRush_Deposit_ItemsGeneration"',
        'KEY_TIMES_GENERATION = "BossRush_Deposit_TimesGeneration"',
        'KEY_VALUES_GENERATION = "BossRush_Deposit_ValuesGeneration"',
        'KEY_BACKUP_ITEMS = "BossRush_Deposit_Backup_Items"',
        'KEY_BACKUP_COMMIT_GENERATION = "BossRush_Deposit_Backup_CommitGeneration"',
    ]:
        if token not in text:
            return fail("missing snapshot key token: " + token)

    load = extract_method(text, "public static void Load()")
    save = extract_method(text, "public static void Save()")
    snapshot = extract_method(text, "private static bool IsCommittedDepositSnapshot(")
    backup = extract_method(text, "private static void SaveBackupFromCurrentCommittedData()")
    writer = extract_method(text, "private static void SaveDepositListsWithGeneration(")

    for block, label in [
        (load, "Load"),
        (save, "Save"),
        (snapshot, "IsCommittedDepositSnapshot"),
        (backup, "SaveBackupFromCurrentCommittedData"),
        (writer, "SaveDepositListsWithGeneration"),
    ]:
        if not block:
            return fail("missing method: " + label)

    result = require_in_order(
        load,
        [
            "TryLoadCommittedDepositLists(out items, out times, out values)",
            "TryLoadBackupDepositLists(out items, out times, out values)",
            "TryLoadLegacyMinimumDepositLists(out items, out times, out values)",
            "RebuildCacheFromLists(items, times, values, recoveredFromBackup || repairedFromLegacy);",
            "if (recoveredFromBackup || repairedFromLegacy)",
            "Save();",
        ],
        "Load recovery path",
    )
    if result:
        return result

    result = require_in_order(
        save,
        [
            "SaveBackupFromCurrentCommittedData();",
            "long generation = CreateNextDepositSaveGeneration(KEY_COMMIT_GENERATION);",
            "SaveDepositListsWithGeneration(",
            "KEY_COMMIT_GENERATION",
        ],
        "Save commit path",
    )
    if result:
        return result

    for token in [
        "items.Count != times.Count || times.Count != values.Count",
        "itemsGeneration == commitGeneration",
        "timesGeneration == commitGeneration",
        "valuesGeneration == commitGeneration",
    ]:
        if token not in snapshot:
            return fail("snapshot validation missing token: " + token)

    result = require_in_order(
        writer,
        [
            "SavesSystem.SaveGlobal<List<ItemTreeData>>(itemsKey, items);",
            "SavesSystem.SaveGlobal<long>(itemsGenerationKey, generation);",
            "SavesSystem.SaveGlobal<List<long>>(timesKey, times);",
            "SavesSystem.SaveGlobal<long>(timesGenerationKey, generation);",
            "SavesSystem.SaveGlobal<List<int>>(valuesKey, values);",
            "SavesSystem.SaveGlobal<long>(valuesGenerationKey, generation);",
            "SavesSystem.SaveGlobal<long>(commitGenerationKey, generation);",
        ],
        "Save writer commit marker must be last",
    )
    if result:
        return result

    print("DepositDataManagerAtomicSaveGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
