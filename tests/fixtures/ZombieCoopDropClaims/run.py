"""Run current shared-drop claims with official pickup bodies and installed COOP hook IL."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent


def member(path, signature):
    source = path.read_text(encoding='utf-8-sig')
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main(out):
    out.mkdir(parents=True, exist_ok=True)
    fresh = Path(tempfile.mkdtemp(prefix='drop-claims-', dir=out))
    official = ROOT / '鸭科夫源码'
    if not official.is_dir():
        official = Path('D:/code/ykf/BossRushMod/鸭科夫源码')
    core = official / 'TeamSoda.Duckov.Core'
    generated = 'using System;using System.Linq;using System.Reflection;using UnityEngine;using UnityEngine.SceneManagement;using ItemStatsSystem;using Random=UnityEngine.Random;using Object=UnityEngine.Object;\n'
    noinline = '[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]'
    generated += 'public partial class CharacterItemControl {' + noinline + member(core / 'CharacterItemControl.cs', 'public bool PickupItem(Item item)') + '}\n'
    generated += 'public partial class CharacterMainControl {' + noinline + member(core / 'CharacterMainControl.cs', 'public bool PickupItem(Item item)') + '}\n'
    generated += 'public static partial class ItemUtilities {' + member(core / 'ItemUtilities.cs', 'public static bool SendToPlayerCharacterInventory(Item item, bool dontMerge = false)') + '}\n'
    merge = member(core / 'ItemUtilities.cs', 'public static bool AddAndMerge(this Inventory inventory, Item item, int preferedFirstPosition = 0)')
    # Decompiled display-class cache names are not valid C# identifiers. Normalize only
    # that compiler temporary, including its implicit null initialization.
    merge = merge.replace('Func<Item, bool> <>9__0;', 'Func<Item, bool> cachedMergePredicate = null;').replace('<>9__0', 'cachedMergePredicate')
    generated += 'public static partial class ItemUtilities {' + noinline + merge + '}\n'
    generated += 'namespace ItemStatsSystem {public sealed partial class Item {' + member(official / 'ItemStatsSystem/Item.cs', 'public void Combine(Item incomingItem)') + member(official / 'ItemStatsSystem/Item.cs', 'private void OnDestroy()') + '}}\n'
    generated += 'namespace ItemStatsSystem {public static class ItemTreeExtensions {' + member(official / 'ItemStatsSystem/ItemTreeExtensions.cs', 'public static void DestroyTree(this Item item)') + '}}\n'
    generated += 'public static partial class ItemExtensions {' + noinline + member(core / 'ItemExtensions.cs', 'public static DuckovItemAgent Drop(this Item item, Vector3 pos, bool createRigidbody, Vector3 dropDirection, float randomAngle)') + '}\n'
    transport = ROOT / 'Common/Coop/BossRushCoopTransport.cs'
    generated += 'namespace BossRush {internal static class BossRushCoopTransport {'
    for signature in ('private sealed class NetworkDropScope', 'private sealed class NetworkDropLease',
                      'internal static IDisposable BeginNetworkItemDropScope()',
                      'internal static void ProtectNetworkDropPickup(',
                      'internal static object CaptureNetworkDrop(', 'internal static bool CompleteNetworkDrop(',
                      'internal static object Read(', 'private static Type FindType('):
        generated += member(transport, signature) + '\n'
    generated += '}}'
    production = fresh / 'OfficialPickup.cs'
    production.write_text(generated, encoding='utf-8')
    sources = [production, HERE / 'Program.cs', HERE / 'Stubs.cs', HERE / 'NativePickup.cs'] + [ROOT / path for path in (
        'ZombieMode/ZombieModeCoopDropClaims.cs', 'Utilities/CoopPersonalDelivery.cs',
        'Common/Coop/BossRushCoopJson.cs', 'Common/Data/BossRushJsonValue.cs', 'Utilities/SimpleJsonHelper.cs')]
    workshop = Path(os.environ.get('BOSSRUSH_COOP_WORKSHOP_PATH', 'D:/software/steam/steamapps/workshop/content/3167020/3591341282'))
    managed = Path(os.environ.get('BOSSRUSH_GAME_MANAGED', 'D:/software/steam/steamapps/common/Escape from Duckov/Duckov_Data/Managed'))
    dependency = Path(os.environ.get('BOSSRUSH_COOP_DEPENDENCY_PATH', str(workshop.parent / '3591339491')))
    harmony = Path(os.environ.get('BOSSRUSH_HARMONY_DLL', 'D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
    native = workshop / 'EscapeFromDuckovCoopMod.dll'
    if os.name != 'nt' or not native.is_file() or not harmony.is_file() or not managed.is_dir():
        raise SystemExit('Requires Windows and actual installed COOP/game/Harmony; no stub-only PASS.')
    framework = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319'
    compiler = sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
    target = fresh / 'ZombieCoopDropClaims.exe'
    args = ['/nologo', '/target:exe', '/langversion:7.3', '/optimize-', '/nostdlib+', '/out:"' + str(target) + '"', '/r:"' + str(harmony) + '"']
    args += ['/r:"' + str(framework / name) + '"' for name in ('mscorlib.dll', 'System.dll', 'System.Core.dll')]
    args += ['"' + str(path) + '"' for path in sources]
    response = fresh / 'compile.rsp'
    response.write_text('\n'.join(args), encoding='utf-8-sig')
    shutil.copy2(harmony, fresh / '0Harmony.dll')
    sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    originals = [core / (name + '.cs') for name in ('CharacterItemControl', 'CharacterMainControl', 'ItemUtilities', 'ItemExtensions')] + [official / 'ItemStatsSystem/Item.cs', official / 'ItemStatsSystem/ItemTreeExtensions.cs']
    result = dict(freshTargetPath=str(target), sources={str(path): sha(path) for path in sources + originals + [transport]}, nativePath=str(native), nativeSha256=sha(native), harmonySha256=sha(harmony))
    built = subprocess.run(['dotnet', str(compiler), '/noconfig', '@' + str(response)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (fresh / 'compile.log').write_bytes(built.stdout)
    result['compileExit'] = built.returncode
    print(built.stdout.decode('utf-8', errors='replace'), flush=True)
    if built.returncode == 0:
        result['targetSha256'] = sha(target)
        ran = subprocess.run([str(target), str(workshop), str(managed), str(dependency)], cwd=fresh, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (fresh / 'run.log').write_bytes(ran.stdout)
        result['runExit'] = ran.returncode
        print('Execute fresh TargetPath: ' + str(target) + '\nSHA-256: ' + result['targetSha256'], flush=True)
        print(ran.stdout.decode('utf-8', errors='replace'), flush=True)
    (out / 'results.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return built.returncode or result.get('runExit', 1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT / 'Build/runtime-regressions/ZombieCoopDropClaims')
    raise SystemExit(main(parser.parse_args().output.resolve()))
