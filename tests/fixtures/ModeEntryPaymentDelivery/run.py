"""Execute production starter delivery, entry, selected-cost receipt and refund transaction."""
from pathlib import Path
import hashlib
import json
import os
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get("BOSSRUSH_FIXTURE_OUT", str(ROOT / "Build/runtime-regressions/ModeEntryPaymentDelivery")))
# A sparse, byte-for-byte pre-fix copy permits red/green checks without touching the shared checkout.
SOURCE_ROOT = Path(os.environ.get("BOSSRUSH_ENTRY_SOURCE_ROOT", str(ROOT)))
sys.path.insert(0, str(ROOT / "tests/fixtures/ModeDEntryOwnership"))
from run import member


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def source(path):
        p = SOURCE_ROOT / path
        if not p.is_file():
            p = ROOT / path
        hashes[path] = hashlib.sha256(p.read_bytes()).hexdigest()
        return p.read_text(encoding="utf-8-sig")

    maps = source("MapSelection/BossRushMapSelectionHelper.cs")
    d = source("ModeD/ModeDEquipment_StarterKit.cs")
    h = source("ModeH/ModeHEntry.cs")
    code = ["using System; using System.Linq; using System.Collections.Generic; using UnityEngine; using ItemStatsSystem; using Duckov.Economy; using Duckov.UI; using Object = UnityEngine.Object; namespace BossRush { partial class BossRushMapSelectionHelper {"]
    asset = member(source("MapSelection/BossRushMapEntryAssetLease.cs"), "public static partial class BossRushMapSelectionHelper")
    code.append(asset[asset.index("{")+1:asset.rindex("}")])
    code += [member(maps, s) for s in (
        "private enum BossRushEntryFlowSource", "public static void MarkEntryFlowFromMapSelectionUi(BossRushPendingEntryKind kind)",
        "public static void MarkEntryFlowFromDirectTeleport(BossRushPendingEntryKind kind)",
        "public static void MarkTargetSceneLoadStarted()", "internal static void NotePaidCost(Cost cost)",
        "public static bool HasPendingPrepaidTicket()", "public static bool HasPendingModeHEntryIntent()",
        "public static int FreezeModeHEntryIntent(string targetSceneName, string targetSceneId)",
        "public static bool TryMatchModeHSceneIntent(", "public static void ClearPendingEntryFlowState()",
        "public static void ClearPendingMapEntry()", "internal static void CancelUnstartedMapSelection()",
        "public static void SetPendingMapEntryIndex(int index)", "private static void FallbackToDirectTeleport()")]
    capture = "internal static void CaptureModeHPayment(Cost cost)"
    # Compatibility adapter only for testing the old code; it never stands in for a production body.
    code.append(member(maps, capture) if capture in maps else capture + " { }")
    code += ["} internal static class BossRushMapEntrySelectionPatch {", member(maps, "internal static void RecordSelection(MapSelectionEntry mapSelectionEntry)"), "}"]
    code += ["internal static partial class ModeHEntry {", member(h, "internal static bool TryEnter(ModBehaviour owner, string sceneName, string sceneId, out string reasonId)"),
             member(h, "internal static bool ResolveTargetMap(string sceneName, out ModeHSupportedMap map)"),
             member(h, "internal static void CancelPendingEntry()"), "} internal sealed partial class ModeDItemPool {"]
    code += [member(d, s) for s in ("internal bool GivePlayerStarterKit()", "private void GiveRandomWeapon(CharacterMainControl character)")]
    check = "private static bool IsStarterWeaponOnPlayer(Item weapon, CharacterMainControl character)"
    if check in d:
        code.append(member(d, check))
    code += ["} }"]
    # Real official Combine/AddAndMerge order, including the target write before source decrement.
    code += ["namespace ItemStatsSystem { public sealed partial class Item {", member(source("鸭科夫源码/ItemStatsSystem/Item.cs"), "public void Combine(Item incomingItem)"), "} }"]
    combine = member(source("鸭科夫源码/TeamSoda.Duckov.Core/ItemUtilities.cs"), "public static bool AddAndMerge(this Inventory inventory, Item item, int preferedFirstPosition = 0)")
    combine = combine.replace("<>9__0", "cachedPredicate").replace("Func<Item, bool> cachedPredicate;", "Func<Item, bool> cachedPredicate = null;")
    code += ["public static partial class ItemUtilities {", combine, "}"]
    generated = OUT / "Extracted.cs"
    generated.write_text("\n".join(code), encoding="utf-8")
    linked = []
    for path in ("ModeH/ModeHTicketRefund.cs", "ModeH/ModeHAvailability.cs", "SkyIsland/SkyIslandInventoryTransaction.cs"):
        source(path)
        p = SOURCE_ROOT / path
        linked.append(p if p.is_file() else ROOT / path)
    files = [generated, HERE / "Stubs.cs", HERE / "Program.cs"] + linked
    project = OUT / "EntryPayments.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
        '<LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414;0169</NoWarn></PropertyGroup><ItemGroup>'
        + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
        + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps(hashes, ensure_ascii=False, indent=2), encoding="utf-8")
    sys.path.insert(0, str(ROOT / "tools"))
    from run_runtime_regressions import run_project_fixture
    result, output = run_project_fixture(project, OUT, ROOT)
    print(output)
    return result


if __name__ == "__main__":
    raise SystemExit(main())
