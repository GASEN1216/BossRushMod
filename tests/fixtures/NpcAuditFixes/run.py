"""Compile production classes and verbatim transaction methods against deterministic hosts."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/NpcAuditFixes"
HASHES = {}


def source(path):
    raw = (ROOT / path).read_bytes()
    HASHES[path] = hashlib.sha256(raw).hexdigest()
    return raw.decode("utf-8-sig")


def extract(path, signature):
    text = source(path)
    start = text.index(signature)
    opening = text.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def build(name, files, generated=""):
    out = OUT / name
    out.mkdir(parents=True, exist_ok=True)
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "LangVersion": "7.3",
                       "EnableDefaultCompileItems": "false", "NoWarn": "0067;0649;0414"}.items():
        ET.SubElement(props, key).text = value
    items = ET.SubElement(project, "ItemGroup")
    for path in files:
        source(path)
        ET.SubElement(items, "Compile", Include=str(ROOT / path))
    ET.SubElement(items, "Compile", Include=str(HERE / (name + ".cs")))
    if generated:
        (out / "Extracted.cs").write_text(generated, encoding="utf-8")
        ET.SubElement(items, "Compile", Include=str(out / "Extracted.cs"))
    ET.ElementTree(project).write(out / "Regression.csproj", encoding="utf-8")
    result = subprocess.run(["dotnet", "build", str(out / "Regression.csproj"), "-c", "Release", "--nologo", "-v:q"], cwd=ROOT)
    if result.returncode:
        return result.returncode
    return subprocess.run(["dotnet", str(out / "bin/Release/net10.0/Regression.dll")], cwd=ROOT).returncode


def main():
    affinity = ["Integration/Affinity/" + file for file in (
        "AffinityManager.cs", "AffinityManagerPersistenceAndDecay.cs", "AffinityManagerStaticCacheReset.cs",
        "AffinityJsonSerializer.cs", "AffinityData.cs", "AffinityConfig.cs", "INPCAffinityConfig.cs")]
    affinity += ["Utilities/SimpleJsonHelper.cs", "Common/Data/BossRushJsonValue.cs"]
    daily = ["Integration/DailyReport/" + file for file in (
        "DailyReportService.cs", "DailyReportModels.cs", "DailyReportTuning.cs", "DailyReportCodec.cs",
        "DailyReportBounty.cs", "DailyReportPersistence.cs", "DailyReportSaveCoordinator.cs")]
    daily += ["Common/Lifecycle/BossRushSlotJsonStore.cs", "Common/Lifecycle/BossRushSaveCoordinatorEngine.cs",
              "Common/Lifecycle/BossRushSaveFileThrottle.cs", "Common/Data/BossRushJsonValue.cs",
              "Utilities/SimpleJsonHelper.cs", "ModeH/ModeHSeedStream.cs"]
    presentation = ["Integration/Affinity/INPCAffinityConfig.cs"] + ["Integration/Affinity/Core/" + file for file in (
        "INPCGiftConfig.cs", "INPCDialogueConfig.cs", "INPCShopConfig.cs", "INPCGiftContainerConfig.cs", "INPCRelationshipDialogueConfig.cs")]
    presentation += ["Integration/Affinity/NPCs/GoblinAffinityConfig.cs", "Integration/Affinity/NPCs/GoblinAffinityConfig_LanguageCache.cs", "Integration/Affinity/NPCs/NurseAffinityConfig.cs", "Integration/Dialogue/DialogueActorFactory.cs"]
    wedding = "Integration/Wedding/NPCMarriageSystem.cs"
    generated = "using System; using UnityEngine; namespace BossRush {\n" + extract(wedding, "internal static class WeddingBuildingRequirementsPatch") + "\n" + extract(wedding, "internal static class WeddingBuildingRuntimePolicy") + "\n}"
    results = {"Affinity": build("Affinity", affinity), "Daily": build("Daily", daily), "Presentation": build("Presentation", presentation, generated)}
    courier = "Integration/NPCs/Courier/"
    parts = []
    for file, signatures in {
        "StorageDepositService.cs": ["private sealed class DepositTransaction", "private static bool IsTransactionBusy", "private static DepositTransaction TryBeginTransaction", "private static bool IsCurrentTransaction", "private static void EndTransaction", "internal static bool OwnsShop", "private sealed class RetrieveAllDepositItem"],
        "StorageDepositBulkActions.cs": ["private static async UniTaskVoid RetrieveAllItemsAsync", "private static async UniTask<List<RetrieveAllDepositItem>> TryRestoreAllDepositItemsForRetrieveAll", "private static int CalculateRetrieveAllRestoredFee", "private static bool TryPayRetrieveFee", "private static void RefundRetrieveFee", "private static void CleanupRestoredRetrieveAllItems", "private static void Cleanup()"],
    }.items():
        parts += [extract(courier + file, signature) for signature in signatures]
    async_host = (HERE.parent / "SkyIslandDialogue/Stubs.cs").read_text(encoding="utf-8-sig").split("namespace UnityEngine")[0]
    generated = async_host + "\nnamespace BossRush { using UnityEngine; using Duckov.Economy; using Duckov.UI; using ItemStatsSystem; using ItemStatsSystem.Data; using Cysharp.Threading.Tasks; public static partial class StorageDepositService {\n" + "\n".join(parts) + "\n}}"
    sweep = "\n".join(extract(courier + "CourierPaidLootSweepService.cs", sig) for sig in ["public static void ReleasePendingSweepResultToPlayer", "private static void OnStartNextSweepButtonClicked()", "private static bool TryReturnResultItemsToPlayer", "private static void ShowSweepResultMailedBanner", "private static void CaptureSweepProducedItems"])
    generated += "\nnamespace BossRush {using UnityEngine; using ItemStatsSystem; using Duckov.UI; public static partial class CourierPaidLootSweepService {" + sweep + "}}"
    mail = "\n".join(extract(courier + "CourierService_CloseAndCleanup.cs", sig) for sig in ["internal static bool CanBufferItemsSilently", "internal static int BufferItemsSilently"])
    generated += "\nnamespace BossRush {using UnityEngine; using ItemStatsSystem; public static partial class CourierService {" + mail + "}}"
    generated += "\nnamespace BossRush {using UnityEngine; using ItemStatsSystem; public static partial class NPCGiftContainerService {" + extract("Integration/Affinity/Services/NPCGiftContainerService.cs", "private static void DropItemOnGround") + "}}"
    results["Transactions"] = build("Transactions", [courier + "DepositDataManager.cs", courier + "StorageDepositSingleRetrieve.cs"], generated)
    if (HERE / "Reforge.cs").exists():
        generated = "using System; using UnityEngine; using Duckov.Economy; using Duckov.UI; using ItemStatsSystem; namespace BossRush { public static partial class ReforgeSystem {\n" + extract("Integration/Reforge/ReforgeSystem.cs", "public static bool CanExecuteReforge") + "\n} public static partial class ReforgeUIManager {\n" + extract("Integration/Reforge/ReforgeUIManager_ComparisonAndState.cs", "private static void OnReforgeButtonClick()") + "\n}}"
        results["Reforge"] = build("Reforge", [], generated)
    (OUT / "source-hashes.json").write_text(json.dumps(HASHES, ensure_ascii=False, indent=2), encoding="utf-8")
    (OUT / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    print("NpcAuditFixes: " + json.dumps(results))
    return 1 if any(results.values()) else 0


if __name__ == "__main__":
    raise SystemExit(main())
