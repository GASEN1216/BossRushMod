"""Execute unchanged content snapshot methods against in-memory item/UI substitutes."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/CoopContent"
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source

hashes = []
def extract(path, signature):
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    result = source[start:end]
    hashes.append(dict(path=path, signature=signature, sha256=hashlib.sha256(result.encode()).hexdigest()))
    return result

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    content = "Integration/Coop/BossRushCoopContentRuntimeModule.cs"
    affix = "Integration/AffixForge/AffixItemData.cs"
    reforge = "Integration/Reforge/ReforgeDataPersistenceCleanup.cs"
    code = ["using System; using System.Collections.Generic; using Duckov.Utilities; using ItemStatsSystem; using UnityEngine; namespace BossRush {"]
    code += ["[Serializable] " + extract(content, signature) for signature in ("internal sealed class BossRushCoopItemVariable", "internal sealed class BossRushCoopItemState", "internal sealed class BossRushCoopSummonPacket")]
    code += ["internal partial class BossRushCoopContentRuntimeModule { internal const string SnapshotKey=\"BossRush.Content.v1\"; private readonly Dictionary<Item,string> applied=new Dictionary<Item,string>(); public void Apply(Item item, IReadOnlyDictionary<string,string> data) { OnSnapshotApplied(item,data); }"]
    code += [extract(content, signature) for signature in ("private void OnSnapshotApplied(", "internal static bool IsContentKey(", "internal static bool Validate(")]
    code += [extract(content, signature) for signature in ("private sealed class SummonOwner", "internal static int LocalSummonCount(", "internal static bool RequestSummon(", "private static void NotifySummonRejected(", "private static bool CurrentSummon(", "private static bool CurrentSummonWorld(", "private void RefreshSummonAsset(", "internal static bool IsSummonOwnerCurrent(", "internal static void TrackSummon(", "internal static bool IsSummonOwnedBy(", "internal static void PruneSummonOwners(", "private void OnSummon(", "private void OnSummonAck(", "private void OnSummonCount(")]
    code += [extract(content, signature).replace("public override void", "public void") for signature in ("private void ClearCoopContentState(", "private void OnSessionChanged(", "private void RefreshSummonScene(", "public override void OnSceneLoaded(", "private static void OnSnapshotRequested(")]
    code += ["} internal static partial class ReforgeDataPersistence { const string MODIFIER_PREFIX=\"RF_MOD_\", STAT_PREFIX=\"RF_STAT_\", VARIABLE_PREFIX=\"RF_VAR_\";"]
    code += [extract(reforge, signature) for signature in ("internal static void PrepareNetworkRestore(", "private static bool TryParseReforgeDataKey(")]
    code += ["} internal static partial class AffixItemData { const string KEY_VERSION=\"AFX_V\", KEY_CAPACITY=\"AFX_CAP\", PREFIX_SLOT=\"AFX_SLOT_\", PREFIX_LOCK=\"AFX_LOCK_\", PREFIX_NAME=\"AFX_NAME_\"; const char SLOT_SEPARATOR=':';"]
    code += [extract(affix, signature) for signature in ("internal static void ApplyNetworkSnapshot(", "internal static bool IsValidNetworkVariable(", "public static bool IsAffixVariableKey(", "public static string SlotKey(", "public static string NameKey(", "public static string LockKey(", "private static int ClampSlotIndex(")]
    code += [extract(affix, signature) for signature in ("public static bool EnsureInitialized(", "private static void EnsureSlotEntries(", "public static int GetSchemaVersion(", "public static int GetCapacity(", "public static bool ClearSlot(", "public static bool TryReadSlot(", "internal static bool TryDecodeSlot(", "public static bool StampNameKV(", "public static bool WriteSlot(", "internal static string EncodeSlot(")]
    code += ["}", extract(affix, "public struct AffixSlotView")]
    code += [extract("ModeF/ModeFRuntimeModule_Coop.cs", "internal sealed class CoopModeFPersonal")]
    code += [extract("ModeE/ModeERuntimeModule_Coop.cs", "internal sealed class CoopModeEPersonal")]
    code += [extract("ModeE/ModeERuntimeModule_CoopConsumables.cs", "internal sealed class CoopModeEUtilityRequest")]
    code += [extract("ModeE/ModeERuntimeModule_CoopCoal.cs", "internal sealed class CoopModeECoalRequest")]
    code += [extract("ModeF/ModeFRuntimeModule_CoopFortifications.cs", "internal sealed class CoopModeFFortificationRequest")]
    code += ["internal sealed partial class ModeFRuntimeModule {"]
    code += [extract("ModeF/ModeFRuntimeModule_CoopFortifications.cs", "private bool ApplyCoopFortificationReceipt(")]
    code += [extract("ModeF/ModeFRuntimeModule_CoopFortifications.cs", "private void RefundCancelledCoopFortifications(")]
    code += ["} internal sealed partial class ModeERuntimeModule {"]
    code += [extract("ModeE/ModeELotteryAndHiring.cs", "private int GetCurrentModeEBossHirePrice(")]
    code += [extract("ModeE/ModeERuntimeModule_CoopConsumables.cs", "private bool ApplyCoopUtilityReceipt(")]
    code += [extract("ModeE/ModeERuntimeModule_CoopCoal.cs", "private bool ApplyCoopCoalReceipt(")]
    code += [extract("ModeE/ModeERuntimeModule_CoopConsumables.cs", "private void RefundCancelledCoopUtilities(")]
    code += [extract("ModeE/ModeERuntimeModule_CoopCoal.cs", "private void RefundCancelledCoopCoal(")]
    code += ["} internal sealed partial class PetNestCoopRuntimeModule {"]
    code += [extract("PetNest/PetNestCoopRuntimeModule.cs", signature) for signature in ("private void FinishSpawn(", "private bool IsSpawnCurrent(", "private static bool Current(", "private static bool CurrentWorld(", "private static bool ValidPet(", "private void OnState(", "private void BindReplica(", "private void OnAiSpawned(", "private void OnRequest(", "private void OnRelease(", "private bool AcceptRequestSequence(", "private void OnAward(", "private void Clear(", "private void RefreshRewardRun(", "private static PetNestCoopPacket Packet(", "internal static void RequestLocalDeployment(")]
    code += ["}}"]
    code += ["namespace BossRush { internal partial class SummonAbilityBase : UnityEngine.MonoBehaviour {", extract("Common/Equipment/EquipmentAbilityAction.cs", "protected override void OnStop()").replace("protected override", "protected virtual"), "} }"]
    for cls, path in (("FrostmourneAction", "Integration/Frostmourne/FrostmourneAction.cs"), ("SummonStaffAction", "Integration/NewWeapons/SummonStaff/SummonStaffAction.cs")):
        code += ["namespace BossRush { internal partial class "+cls+" {"]
        moved = ("internal static bool TryGetSummonOwner(", "private void OnCoopSummonResult(", "protected override void OnStop(", "protected override void OnAbilityStop(")
        code += [extract("Integration/Frostmourne/FrostmourneAction_Coop.cs" if cls == "FrostmourneAction" and signature in moved else path, signature) for signature in ("internal static bool TryGetSummonOwner(", "protected override bool OnAbilityStart(", "protected override void OnAbilityUpdate(", "private void OnCoopSummonResult(", "protected override void OnStop(", "protected override void OnAbilityStop(")]
        if cls == "SummonStaffAction": code += [extract(path, "internal void CancelPendingSummons(")]
        code += ["} }"]
    code += ["namespace BossRush {", "[Serializable] " + extract("PetNest/PetNestCoopRuntimeModule.cs", "internal sealed class PetNestCoopPacket"), "}"]
    generated = OUT / "Production.cs"
    generated.write_text("\n".join(code), encoding="utf-8")
    (OUT / "source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    files = [generated, HERE / "Stubs.cs", HERE / "Program.cs"]
    files += [ROOT / p for p in ("Common/Coop/BossRushCoopJson.cs", "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs")]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + ''.join('<Compile Include="' + escape(str(path)) + '" />' for path in files) + '</ItemGroup></Project>'
    project_path = OUT / "Regression.csproj"
    project_path.write_text(project, encoding="utf-8")
    sys.path.insert(0, str(ROOT / "tools"))
    from run_runtime_regressions import run_project_fixture
    result, output = run_project_fixture(project_path, OUT, ROOT)
    print(output)
    return result

if __name__ == "__main__":
    raise SystemExit(main())
