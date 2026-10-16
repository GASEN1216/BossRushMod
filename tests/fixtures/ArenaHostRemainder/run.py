"""Full production Arena spawn, cache and loot templates, plus original lifecycle."""
from pathlib import Path
import sys, json, hashlib, subprocess, os, tempfile, shutil
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
OUT=Path(os.environ.get('BOSSRUSH_FIXTURE_OUTPUT',str(ROOT/'Build/runtime-regressions/ArenaHostRemainder')));OUT.mkdir(parents=True,exist_ok=True)
OUT=Path(tempfile.mkdtemp(prefix='fresh-',dir=OUT))
sys.path.insert(0,str(ROOT/'tests'))
from ArchitectureStructureGuard import extract_method_body
production=[ROOT/'WavesArena'/('WavesArenaRuntimeModule_'+name+'.cs') for name in ('LegacySpawn','LootTemplates','CharacterRegistry','EnemyMaintenance')]
production.append(ROOT/'Integration/Mutators/MutatorBossRegenRuntime.cs')
lifecycle=ROOT/'WavesArena/WavesArenaRuntimeModule.cs'
source=lifecycle.read_text(encoding='utf8')
parts=[]
for sig in ('public override void OnAwake(ModBehaviour owner)','public override void OnDestroy()'):
    parts.append(sig+extract_method_body(source,sig))
reward=ROOT/'WavesArena/WavesArenaRuntimeModule_CoopRewards.cs'
reward_source=reward.read_text(encoding='utf8')
parts.append('private void CleanupCoopArenaRewards()'+extract_method_body(reward_source,'private void CleanupCoopArenaRewards()'))
parts.append('private static void CancelCoopArenaReward(CoopArenaReward reward)'+extract_method_body(reward_source,'private static void CancelCoopArenaReward(CoopArenaReward reward)'))
(OUT/'Lifecycle.cs').write_text('namespace BossRush { internal sealed partial class WavesArenaRuntimeModule {\n'+'\n'.join(parts)+'\n}}',encoding='utf8')
spawner=ROOT/'WavesArena/WavesArenaSpawnerControl.cs'
spawner_source=spawner.read_text(encoding='utf8')
helper='private bool DeferNativeFactoryCleanup(CharacterMainControl character, NativeArenaCleanupKind kind)'
start=spawner_source.index('    internal enum NativeArenaCleanupKind')
factory_source='using System; using System.Collections.Generic; using System.Reflection; using HarmonyLib; using UnityEngine;\nnamespace BossRush {\ninternal sealed partial class WavesArenaRuntimeModule {\n'+helper+extract_method_body(spawner_source,helper)+'\n}\n'+spawner_source[start:]
(OUT/'NativeFactory.cs').write_text(factory_source,encoding='utf8')
sources=[ROOT/'tests/fixtures/CoopOfflineStubs.cs']+production+[HERE/'Stubs.cs',HERE/'Async.cs',HERE/'Program.cs',HERE/'NativeFactoryCases.cs',OUT/'Lifecycle.cs',OUT/'NativeFactory.cs']
hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in production+[lifecycle,spawner,reward]}
(OUT/'production-sha256.json').write_text(json.dumps(hashes,indent=2),encoding='utf8')
harmony=Path(os.environ.get('BOSSRUSH_HARMONY_DLL','D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
compiler=sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
framework=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319'
target=OUT/'ArenaHostRemainder.exe'
managed=Path(os.environ.get('BOSSRUSH_GAME_MANAGED',str(Path(os.environ.get('GAME_PATH','D:/software/steam/steamapps/common/Escape from Duckov'))/'Duckov_Data/Managed')))
if not harmony.is_file() or not (managed/'TeamSoda.Duckov.Core.dll').is_file():
    raise SystemExit('Requires actual Windows Harmony/game DLLs; no stub-only binding PASS.')
args=['/nologo','/target:exe','/langversion:7.3','/optimize+','/nostdlib+','/nowarn:0649,0414,0067,0169',f'/out:"{target}"',f'/r:"{harmony}"']
args += [f'/r:"{framework/name}"' for name in ('mscorlib.dll','System.dll','System.Core.dll')]
args += [f'"{p}"' for p in sources]
response=OUT/'compile.rsp';response.write_text('\n'.join(args),encoding='utf-8-sig')
shutil.copy2(harmony,OUT/'0Harmony.dll')
built=subprocess.run(['dotnet',str(compiler),'/noconfig','@'+str(response)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
(OUT/'compile.log').write_bytes(built.stdout);print(built.stdout.decode('utf-8',errors='replace'),flush=True)
result={'compileExit':built.returncode,'runExit':None,'freshTargetPath':str(target),'productionSha256':hashes}
result['harmonySha256']=hashlib.sha256(harmony.read_bytes()).hexdigest()
result['nativeCorePath']=str(managed/'TeamSoda.Duckov.Core.dll')
result['nativeCoreSha256']=hashlib.sha256((managed/'TeamSoda.Duckov.Core.dll').read_bytes()).hexdigest()
result['fixtureSourceSha256']={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (HERE/'run.py',HERE/'Program.cs',HERE/'NativeFactoryCases.cs',HERE/'Stubs.cs',HERE/'Async.cs')}
if built.returncode==0:
    result['targetSha256']=hashlib.sha256(target.read_bytes()).hexdigest()
    print('Execute fresh TargetPath: '+str(target)+'\nSHA-256: '+result['targetSha256'],flush=True)
    environment=os.environ.copy();environment['BOSSRUSH_GAME_MANAGED']=str(managed)
    ran=subprocess.run([str(target)],cwd=OUT,env=environment,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (OUT/'run.log').write_bytes(ran.stdout);print(ran.stdout.decode('utf-8',errors='replace'),flush=True)
    result['runExit']=ran.returncode
(OUT/'results.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf8')
print('Evidence: '+str(OUT/'results.json'),flush=True)
raise SystemExit(built.returncode or result['runExit'])
