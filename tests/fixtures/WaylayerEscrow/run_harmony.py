"""Patch production journal hooks with installed Harmony; framework process, fake native inventory only."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess,tempfile

def main(root,out,production):
    root,out=Path(root),Path(out)
    here=Path(__file__).resolve().parent
    fresh=Path(tempfile.mkdtemp(prefix='harmony-',dir=out))
    harmony=Path(os.environ.get('BOSSRUSH_HARMONY_DLL','D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
    framework=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319'
    if os.name!='nt' or not harmony.is_file():
        raise RuntimeError('Requires Windows and installed Harmony; no stub-only hook PASS')
    compiler=sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
    files=[production,out/"PersonalDelivery.cs",here/"ClaimCases.cs",here/"PublicationCases.cs"]+[here/n for n in ('Stubs.cs','Program.cs','Persistence.cs','JournalCases.cs','CrashCases.cs','HookCases.cs')]
    files += [root/p for p in ('SkyIsland/CoopWaylayerJournal.cs','SkyIsland/CoopWaylayerClaims.cs','SkyIsland/CoopWaylayerJournalHooks.cs','Common/Coop/BossRushCoopJson.cs','Common/Data/BossRushJsonValue.cs','Utilities/SimpleJsonHelper.cs','Common/Lifecycle/BossRushSlotJsonStore.cs','Common/Lifecycle/BossRushSaveCoordinatorEngine.cs','Common/Lifecycle/BossRushSaveFileThrottle.cs')]
    target=fresh/'WaylayerHarmony.exe'
    args=['/nologo','/target:exe','/nostdlib+','/langversion:7.3','/define:WAYLAYER_HARMONY_FIXTURE','/out:"'+str(target)+'"']
    args+=['/r:"'+str(p)+'"' for p in [harmony]+[framework/n for n in ('mscorlib.dll','System.dll','System.Core.dll')]]
    args+=['"'+str(p)+'"' for p in files]
    rsp=fresh/'compile.rsp';rsp.write_text('\n'.join(args),encoding='utf-8-sig')
    built=subprocess.run(['dotnet',str(compiler),'/noconfig','@'+str(rsp)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (fresh/'compile.log').write_bytes(built.stdout)
    sha=lambda p:hashlib.sha256(Path(p).read_bytes()).hexdigest()
    result={'compileExit':built.returncode,'runExit':None,'freshTargetPath':str(target),'targetSha256':sha(target) if built.returncode==0 else None,'sources':{str(p):sha(p) for p in files},'harmony':str(harmony),'harmonySha256':sha(harmony),'gameStarted':False,'playerSaveReadOrWritten':False}
    if built.returncode==0:
        shutil.copy2(harmony,fresh/'0Harmony.dll')
        ran=subprocess.run([str(target)],cwd=fresh,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (fresh/'run.log').write_bytes(ran.stdout);result['runExit']=ran.returncode
        print('Execute fresh TargetPath: '+str(target)+'\nSHA-256: '+result['targetSha256'],flush=True)
        print(ran.stdout.decode('utf-8','replace'),flush=True)
    else: print(built.stdout.decode('utf-8','replace'),flush=True)
    (fresh/'results.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    (out/'harmony-results.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    return built.returncode or result['runExit']
