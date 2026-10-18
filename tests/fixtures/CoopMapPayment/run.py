"""Execute production payment owner with full installed native vote/load IL and official Cost/ConsumeItems."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / 'tests/fixtures/ModeDEntryOwnership'))
from run import member


def main(out):
    out.mkdir(parents=True, exist_ok=True)
    fresh = Path(tempfile.mkdtemp(prefix='map-payment-', dir=out))
    hashes = {}

    def read(rel):
        path = ROOT / rel
        hashes[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
        return path.read_text(encoding='utf-8-sig')

    read('tests/fixtures/CoopMapPayment/run.py')
    read('tests/fixtures/ModeDEntryOwnership/run.py')
    maps = read('MapSelection/BossRushMapSelectionHelper.cs')
    official = '鸭科夫源码/TeamSoda.Duckov.Core/'
    economy = read(official + 'Duckov/Economy/EconomyManager.cs')
    items = read(official + 'ItemUtilities.cs')
    code = ['using System;using System.Collections.Generic;using System.Linq;using UnityEngine;using ItemStatsSystem;using Duckov.Economy;using Duckov.UI;using UnityEngine.EventSystems;using Cysharp.Threading.Tasks;using HarmonyLib;using Object=UnityEngine.Object;']
    code += ['namespace BossRush {public static partial class BossRushMapSelectionHelper {']
    code += [member(maps, s) for s in (
        'private enum BossRushEntryFlowSource',
        'public static void MarkEntryFlowFromMapSelectionUi(BossRushPendingEntryKind kind)',
        'public static void MarkEntryFlowFromDirectTeleport(BossRushPendingEntryKind kind)',
        'public static void MarkTargetSceneLoadStarted()', 'internal static bool HasCoopOwnedMapTarget(string sceneId)', 'internal static void CaptureModeHPayment(Cost cost)',
        'internal static void NotePaidCost(Cost cost)', 'public static bool HasPendingPrepaidTicket()',
        'public static bool HasPendingModeHEntryIntent()', 'public static int FreezeModeHEntryIntent(',
        'public static void ClearPendingEntryFlowState()', 'public static void ClearPendingMapEntry()',
        'internal static void CancelUnstartedMapSelection()', 'public static void SetPendingMapEntryIndex(int index)')]
    code += ['} internal static class BossRushMapEntrySelectionPatch {', member(maps, 'internal static bool BeforeNotify(MapSelectionEntry mapSelectionEntry)'), member(maps, 'internal static void RecordSelection(MapSelectionEntry mapSelectionEntry)'), '}']
    code += [member(maps, 'internal static class BossRushMapEntryPointerSelectionPatch')]
    h = read('ModeH/ModeHEntry.cs')
    code += ['internal static class ModeHEntry {', member(h, 'internal static void CancelPendingEntry()'),
             member(h, 'internal static void AbortAndRefund(ModBehaviour owner, string reasonId, bool exitScene, Action<ModeHRefundOutcome> beforeExit)'), '}}']
    code += ['namespace Duckov.Economy {public partial struct Cost {', member(read(official + 'Duckov/Economy/Cost.cs'), 'public bool Pay('), '} public static class EconomyManager {']
    code += ['public static long Money,Cash;public static event Action<Cost> OnCostPaid;public static int Attempts; public static bool Pay(long amount,bool a,bool b){Attempts++;return !Probe.RejectMoney;}']
    code += [member(economy, 'public static bool Pay(Cost cost,'), member(economy, 'public static bool IsEnough(Cost cost,'), '}}']
    consume = member(items, 'internal static bool ConsumeItems(Cost cost)')
    consume = consume.replace('ItemUtilities.<>c__DisplayClass31_0', 'ConsumeRoot').replace('ItemUtilities.<>c__DisplayClass31_1', 'ConsumeEntry').replace('CS$<>8__locals1', 'root').replace('CS$<>8__locals2', 'entry')
    merge = member(items, 'public static bool AddAndMerge(this Inventory inventory, Item item, int preferedFirstPosition = 0)')
    merge = merge.replace('<>9__0', 'cachedPredicate').replace('Func<Item, bool> cachedPredicate;', 'Func<Item, bool> cachedPredicate = null;')
    code += ['public static partial class ItemUtilities {',
             'private sealed class ConsumeRoot {public List<Item> detachedItems;} private sealed class ConsumeEntry {public ConsumeRoot root;public Cost.ItemEntry cur;}',
             'public sealed class ItemDurabilityComparer:IComparer<Item>{public static readonly ItemDurabilityComparer Instance=new ItemDurabilityComparer(); public int Compare(Item a,Item b){return 0;}}',
             'public static event Action OnPlayerItemOperation;public static List<Item> FindAllBelongsToPlayer(Func<Item,bool> filter){return CharacterMainControl.Main.CharacterItem.Inventory.Where(filter).ToList();} public static int Count(List<Item> items){return items.Sum(x=>x==null?0:x.StackCount);} public static int GetItemCount(int id){return Count(FindAllBelongsToPlayer(x=>x!=null&&x.TypeID==id));}',
             consume, merge, '}']
    code += ['namespace ItemStatsSystem {public sealed partial class Item {', member(read('鸭科夫源码/ItemStatsSystem/Item.cs'), 'public void Combine(Item incomingItem)'), '}}']
    code += ['namespace Duckov.UI {public sealed partial class MapSelectionEntry { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]', member(read(official + 'Duckov/UI/MapSelectionEntry.cs'), 'public void OnPointerClick('), '} public sealed partial class MapSelectionView { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]', member(read(official + 'Duckov/UI/MapSelectionView.cs'), 'internal void NotifyEntryClicked('), '}}']
    generated = fresh / 'ActualBodies.cs'
    generated.write_text('\n'.join(code), encoding='utf-8')
    sources = [generated, HERE / 'Stubs.cs', HERE / 'NativeScene.cs', HERE / 'Program.cs']
    sources += list(HERE.glob('ReceiptCases.cs'))
    for rel in ('Common/Coop/BossRushCoopMapPaymentCompatibility.cs', 'MapSelection/BossRushCoopMapPaymentReceipt.cs',
                'MapSelection/BossRushMapEntryAssetLease.cs', 'ModeH/ModeHTicketRefund.cs', 'ModeH/ModeHAvailability.cs', 'SkyIsland/SkyIslandInventoryTransaction.cs'):
        read(rel)
        sources.append(ROOT / rel)
    workshop = Path(os.environ.get('BOSSRUSH_COOP_WORKSHOP_PATH', 'D:/software/steam/steamapps/workshop/content/3167020/3591341282'))
    game = Path(os.environ.get('BOSSRUSH_GAME_MANAGED', 'D:/software/steam/steamapps/common/Escape from Duckov/Duckov_Data/Managed'))
    dependency = workshop.parent / '3591339491'
    harmony = Path(os.environ.get('BOSSRUSH_HARMONY_DLL', str(workshop.parent / '3588386576/0Harmony.dll')))
    if os.name != 'nt' or not harmony.is_file() or not (workshop / 'EscapeFromDuckovCoopMod.dll').is_file():
        raise SystemExit('Requires Windows and actual installed COOP/game/Harmony; no stub-only PASS.')
    shutil.copy2(harmony, fresh / '0Harmony.dll')
    framework = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319'
    compiler = Path('C:/Program Files/dotnet/sdk/8.0.302/Roslyn/bincore/csc.dll')
    target = fresh / 'CoopMapPayment.exe'
    args = ['/nologo', '/target:exe', '/langversion:7.3', '/optimize-', '/nostdlib+', '/out:"' + str(target) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll')]
    args += ['"' + str(path) + '"' for path in sources]
    response = fresh / 'compile.rsp'
    response.write_text('\n'.join(args), encoding='utf-8-sig')
    sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    for path in sources + [workshop / 'EscapeFromDuckovCoopMod.dll', game / 'TeamSoda.Duckov.Core.dll', harmony]:
        hashes[str(path)] = sha(path)
    receipt = dict(inputs=hashes, target=str(target))
    built = subprocess.run(['dotnet', str(compiler), '/noconfig', '@' + str(response)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (fresh / 'compile.log').write_bytes(built.stdout)
    receipt['compile_exit'] = built.returncode
    print(built.stdout.decode('utf-8', errors='replace'), flush=True)
    if built.returncode == 0:
        receipt['target_sha256'] = sha(target)
        ran = subprocess.run([str(target), str(workshop), str(game), str(dependency)], cwd=fresh, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (fresh / 'run.log').write_bytes(ran.stdout)
        receipt['run_exit'] = ran.returncode
        print(ran.stdout.decode('utf-8', errors='replace'), flush=True)
    receipt['inputs_unchanged'] = all(sha(Path(p)) == digest for p, digest in hashes.items())
    (fresh / 'receipt.json').write_text(json.dumps(receipt, indent=2), encoding='utf-8')
    (out / 'results.json').write_text(json.dumps(receipt, indent=2), encoding='utf-8')
    print('Fresh evidence:', fresh)
    return built.returncode or receipt.get('run_exit', 1) or (0 if receipt['inputs_unchanged'] else 1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT / 'Build/runtime-regressions/CoopMapPayment')
    raise SystemExit(main(parser.parse_args().output.resolve()))
