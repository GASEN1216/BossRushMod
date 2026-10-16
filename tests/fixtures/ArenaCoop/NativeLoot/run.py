"""Execute the complete installed native Loader/open IL against the real personal marker."""
from pathlib import Path
import argparse, hashlib, json, os, shutil, subprocess, tempfile
ROOT=Path(__file__).resolve().parents[4]
HERE=Path(__file__).resolve().parent
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True)
OUT=parser.parse_args().output;OUT.mkdir(parents=True,exist_ok=True)
OUT=Path(tempfile.mkdtemp(prefix='native-loot-',dir=OUT)).resolve()
workshop=Path(os.environ.get('BOSSRUSH_COOP_WORKSHOP_PATH','D:/software/steam/steamapps/workshop/content/3167020/3591341282'))
managed=Path(os.environ.get('BOSSRUSH_GAME_MANAGED',str(Path(os.environ.get('GAME_PATH','D:/software/steam/steamapps/common/Escape from Duckov'))/'Duckov_Data/Managed')))
dependency=Path(os.environ.get('BOSSRUSH_COOP_DEPENDENCY_PATH',str(workshop.parent/'3591339491')))
harmony=Path(os.environ.get('BOSSRUSH_HARMONY_DLL','D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
if os.name!='nt' or not harmony.is_file() or not (workshop/'EscapeFromDuckovCoopMod.dll').is_file():
    raise SystemExit('Requires the actual installed native COOP and Windows Harmony, not a stub-only PASS.')
compiler=sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
framework=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319'
production=ROOT/'LootAndRewards/CoopArenaPersonalLoot.cs'
shadow=ROOT/'LootAndRewards/VictoryRewardShadowCrateController.cs'
def block(signature):
    text=shadow.read_text(encoding='utf-8-sig');start=text.index(signature);end=text.index('{',start)+1;depth=1
    while depth:depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[start:end]
generated=OUT/'ShadowOwner.cs'
generated.write_text('using System;using UnityEngine;namespace BossRush { internal sealed class VictoryRewardShadowCrateController : MonoBehaviour {'+
    'private enum ShadowCrateState{Following,Materializing,Descending,Completed} private ShadowCrateState state;'+
    'private bool disposed;private GameObject ghostObject;private object ghostTransform,playerTransform,ghostRenderers,ghostAuraLight,ghostAuraFade;'+
    'private ModBehaviour owner;private Func<bool> personalOwnerCurrent;private Func<Vector3,bool> personalMaterializer;private float personalLandAt;'+
    'public void TestSetup(ModBehaviour host,Func<bool> current){owner=host;personalOwnerCurrent=current;ghostObject=new GameObject();}public void TestTick(){Update();}'+
    'private void CompleteAndLand(){}private void UpdateFollowPose(float time){}private void UpdateMaterialize(float time){}private void UpdateDescending(float time){}'+
    block('private void Update()')+block('private void CleanupAndDestroy(bool destroySelf)')+block('private void OnDestroy()')+'}}',encoding='utf-8')
landing=ROOT/'Utilities/SpawnPositionHelper.cs'
landing_source=landing.read_text(encoding='utf-8-sig')
def landing_block(signature):
    start=landing_source.index(signature);end=landing_source.index('{',start)+1;depth=1
    while depth:depth+=(landing_source[end]=='{')-(landing_source[end]=='}');end+=1
    return landing_source[start:end]
ground=OUT/'Ground.cs'
ground.write_text('using UnityEngine;using UnityEngine.AI;namespace BossRush {internal static class SpawnPositionHelper {const float DefaultLiftOffset=.15f,DefaultNavMeshSampleRadius=5f,DefaultRaycastOriginHeight=1f,DefaultRaycastMaxDistance=5f;'+
    landing_block('internal static bool TrySnapToGround(')+landing_block('private static bool TryRaycastSnapPreserveXZ(')+landing_block('private static bool TryNavMeshSnapPreserveXZ(')+'}}',encoding='utf-8')
sources=[production,HERE/'Program.cs',HERE/'Stubs.cs',generated,ground]
target=OUT/'NativeLoot.exe'
args=['/nologo','/target:exe','/langversion:7.3','/optimize+','/nostdlib+','/out:"'+str(target)+'"','/r:"'+str(harmony)+'"']
args+=['/r:"'+str(framework/name)+'"' for name in ('mscorlib.dll','System.dll','System.Core.dll')]
args+=['"'+str(p)+'"' for p in sources]
response=OUT/'compile.rsp';response.write_text('\n'.join(args),encoding='utf-8-sig')
shutil.copy2(harmony,OUT/'0Harmony.dll')
result={'sources':{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[shadow,landing,harmony,workshop/'EscapeFromDuckovCoopMod.dll']},'freshTargetPath':str(target)}
built=subprocess.run(['dotnet',str(compiler),'/noconfig','@'+str(response)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
(OUT/'compile.log').write_bytes(built.stdout);print(built.stdout.decode('utf-8',errors='replace'),flush=True)
result['compileExit']=built.returncode;result['runExit']=None
if built.returncode==0:
    result['targetSha256']=hashlib.sha256(target.read_bytes()).hexdigest()
    print('Execute fresh TargetPath: '+str(target)+'\nSHA-256: '+result['targetSha256'],flush=True)
    ran=subprocess.run([str(target),str(workshop),str(managed),str(dependency)],cwd=OUT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (OUT/'run.log').write_bytes(ran.stdout);print(ran.stdout.decode('utf-8',errors='replace'),flush=True);result['runExit']=ran.returncode
(OUT/'results.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print('Evidence: '+str(OUT/'results.json'),flush=True)
raise SystemExit(built.returncode or result['runExit'])
