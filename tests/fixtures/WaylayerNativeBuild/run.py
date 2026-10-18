"""Execute the installed complete native builder with the production permission seam."""
from pathlib import Path
import hashlib, json, os, shutil, subprocess, tempfile, sys

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent

def extract(path, signature):
    source = path.read_text(encoding='utf-8-sig')
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    pos = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[pos] == '{') - (source[pos] == '}')
        pos += 1
    return source[start:pos]

def main():
    out = ROOT / 'Build/runtime-regressions/WaylayerNativeBuild'
    out.mkdir(parents=True, exist_ok=True)
    fresh = Path(tempfile.mkdtemp(prefix='native-', dir=out))
    official = ROOT / '鸭科夫源码/ItemStatsSystem'
    generated = fresh / 'Official.cs'
    methods = '\n'.join('[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n' + extract(official/'Inventory.cs', sig) for sig in ('public bool AddAt(', 'public bool AddItem('))
    generated.write_text('using System;using UnityEngine;using ItemStatsSystem.Items;namespace ItemStatsSystem { public sealed partial class Inventory {'+methods+'}}', encoding='utf-8')
    merge = fresh/'Merge.cs'
    merge_body=extract(ROOT/'鸭科夫源码/TeamSoda.Duckov.Core/ItemUtilities.cs','public static bool AddAndMerge(').replace('<>9__0','cachedPredicate').replace('Func<Item, bool> cachedPredicate;', 'Func<Item, bool> cachedPredicate = null;')
    merge.write_text('using System;using System.Linq;using ItemStatsSystem;public static partial class ItemUtilities {'+merge_body+'}',encoding='utf-8')
    gate = fresh/'ClaimsGate.cs'
    claims = ROOT/'SkyIsland/CoopWaylayerClaims.cs'
    gate.write_text('using System;using ItemStatsSystem;using ItemStatsSystem.Items;namespace BossRush{internal sealed partial class CoopWaylayerClaims {'+'\n'.join(extract(claims,sig) for sig in ('internal sealed class Admission', 'internal static IDisposable Admit(', 'internal static bool AllowInventory(', 'internal static bool AllowSlot('))+'}}',encoding='utf-8')
    slot=fresh/'OfficialSlot.cs'
    slot.write_text('using System;using UnityEngine;namespace ItemStatsSystem.Items {public sealed partial class Slot {[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]'+extract(official/'Items/Slot.cs','public bool Plug(')+'}}',encoding='utf-8')
    harmony = Path(os.environ.get('BOSSRUSH_HARMONY_DLL', 'D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
    managed = Path(os.environ.get('BOSSRUSH_GAME_MANAGED','D:/software/steam/steamapps/common/Escape from Duckov/Duckov_Data/Managed'))
    coop = Path(os.environ.get('BOSSRUSH_COOP_DIRECTORY','D:/software/steam/steamapps/workshop/content/3167020/3591341282'))
    framework = Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319'
    compiler = sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
    target = fresh/'WaylayerNativeBuild.exe'
    production = Path(os.environ.get('WAYLAYER_NATIVE_SOURCE', str(ROOT/'SkyIsland/CoopWaylayerNativeBuildScope.cs')))
    files = [production, HERE/'Stubs.cs', HERE/'Native.cs', HERE/'Program.cs', generated,merge,gate,slot]
    args = ['/nologo','/target:exe','/nostdlib+','/langversion:7.3','/out:"'+str(target)+'"']
    args += ['/r:"'+str(p)+'"' for p in [harmony]+[framework/n for n in ('mscorlib.dll','System.dll','System.Core.dll')]]
    args += ['"'+str(p)+'"' for p in files]
    rsp = fresh/'compile.rsp'; rsp.write_text('\n'.join(args), encoding='utf-8-sig')
    built = subprocess.run(['dotnet',str(compiler),'/noconfig','@'+str(rsp)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (fresh/'compile.log').write_bytes(built.stdout)
    sha=lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
    contracts=[HERE/'run.py',claims,official/'Inventory.cs',official/'Items/Slot.cs',ROOT/'鸭科夫源码/TeamSoda.Duckov.Core/ItemUtilities.cs',harmony,compiler,managed/'TeamSoda.Duckov.Core.dll']
    result={'compile_exit':built.returncode, 'game_started':False,'player_save_read_or_written':False,'sources':{str(p):sha(p) for p in files},'contracts':{str(p):sha(p) for p in contracts},'native_sha256':sha(coop/'EscapeFromDuckovCoopMod.dll'),'api_sha256':sha(coop/'EscapeFromDuckovModApi.dll')}
    code=built.returncode
    if not code:
        shutil.copy2(harmony,fresh/'0Harmony.dll')
        ran=subprocess.run([str(target),str(coop),str(managed),str(harmony.parent)],cwd=fresh,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (fresh/'run.log').write_bytes(ran.stdout)
        result.update(run_exit=ran.returncode, target=str(target),target_sha256=sha(target))
        code=ran.returncode
        print('Execute fresh TargetPath: '+str(target)+'\nSHA-256: '+sha(target),flush=True)
        print(ran.stdout.decode('utf-8','replace'),flush=True)
    else: print(built.stdout.decode('utf-8','replace'),flush=True)
    (fresh/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    (out/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    return code
if __name__=='__main__': raise SystemExit(main())
