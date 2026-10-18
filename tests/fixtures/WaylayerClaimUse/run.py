"""Replay complete installed official use methods with production private-original gates."""
from pathlib import Path
import hashlib,json,os,re,shutil,subprocess,tempfile
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).resolve().parent

def main():
 out=ROOT/'Build/runtime-regressions/WaylayerClaimUse';out.mkdir(parents=True,exist_ok=True);fresh=Path(tempfile.mkdtemp(prefix='use-',dir=out))
 managed=Path(os.environ.get('BOSSRUSH_GAME_MANAGED',r'D:\software\steam\steamapps\common\Escape from Duckov\Duckov_Data\Managed'))
 harmony=Path(os.environ.get('BOSSRUSH_HARMONY_DLL',r'D:\software\steam\steamapps\workshop\content\3167020\3588386576\0Harmony.dll'))
 compiler=sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1];framework=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319'
 production=Path(os.environ.get('WAYLAYER_USE_SOURCE',str(ROOT/'SkyIsland/CoopWaylayerClaimUseHooks.cs')))
 claims=ROOT/'SkyIsland/CoopWaylayerClaims.cs';raw=claims.read_text(encoding='utf-8-sig');sig='internal static bool Locked(Item item)';start=raw.index(sig);pos=raw.index('{',start)+1;depth=1
 while depth:depth+=(raw[pos]=='{')-(raw[pos]=='}');pos+=1
 journal=ROOT/'SkyIsland/CoopWaylayerJournal.cs';lock_key=re.search(r'internal const string ClaimLockKey = [^;]+;',journal.read_text(encoding='utf-8-sig')).group(0)
 gate=fresh/'ClaimsGate.cs';gate.write_text('using ItemStatsSystem;namespace BossRush{internal static class CoopWaylayerJournal{'+lock_key+'}internal static partial class CoopWaylayerClaims{'+raw[start:pos]+'}}',encoding='utf-8')
 target=fresh/'WaylayerClaimUse.exe';files=[production,ROOT/'Config/ConfigItemIds.cs',ROOT/'Integration/SkyIsland/SkyIslandFieldcraftUsage.cs',HERE/'Stubs.cs',HERE/'Native.cs',HERE/'Program.cs',gate]
 args=['/nologo','/nostdlib+','/target:exe','/langversion:7.3','/out:"'+str(target)+'"',*['/r:"'+str(x)+'"' for x in [harmony,*[framework/n for n in ('mscorlib.dll','System.dll','System.Core.dll')]]],*['"'+str(x)+'"' for x in files]]
 rsp=fresh/'compile.rsp';rsp.write_text('\n'.join(args),encoding='utf-8-sig')
 sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest();inputs={str(x):sha(x) for x in [*files,claims,journal,ROOT/'Integration/SkyIsland/SkyIslandItems.cs',HERE/'run.py',compiler,harmony,managed/'ItemStatsSystem.dll',managed/'TeamSoda.Duckov.Core.dll']}
 built=subprocess.run(['dotnet',str(compiler),'/noconfig','@'+str(rsp)],capture_output=True);(fresh/'compile.log').write_bytes(built.stdout+built.stderr);code=built.returncode
 result=dict(compile_exit=code,inputs=inputs,game_started=False,player_save_read_or_written=False)
 if not code:
  shutil.copy2(harmony,fresh/'0Harmony.dll');run=subprocess.run([str(target),str(managed)],cwd=fresh,capture_output=True,timeout=60);(fresh/'run.log').write_bytes(run.stdout+run.stderr);code=run.returncode
  result.update(run_exit=code,target=str(target),target_sha256=sha(target));print('Execute fresh TargetPath: '+str(target)+'\nSHA-256: '+sha(target));print((run.stdout+run.stderr).decode('utf-8','replace'))
 else:print((built.stdout+built.stderr).decode('utf-8','replace'))
 result['inputs_unchanged']=all(sha(p)==h for p,h in inputs.items());(fresh/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8');(out/'result.json').write_text(json.dumps(result,indent=2),encoding='utf-8');return code or (not result['inputs_unchanged'])
if __name__=='__main__':raise SystemExit(main())
