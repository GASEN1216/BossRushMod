"""生产保护+既有生命setter链，真实已安装Harmony，Windows隔离.NET Framework进程。"""
from pathlib import Path
import hashlib, os, shutil, subprocess, tempfile
ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
if os.name != 'nt': raise SystemExit('CoopAiPrediction requires Windows .NET Framework and installed Harmony.')
harmony = Path(os.environ.get('BOSSRUSH_HARMONY_DLL', 'D:/software/steam/steamapps/workshop/content/3167020/3588386576/0Harmony.dll'))
if not harmony.is_file(): raise SystemExit('Missing BOSSRUSH_HARMONY_DLL: ' + str(harmony))
OUT = ROOT / 'Build/runtime-regressions/CoopAiPrediction'
OUT.mkdir(parents=True, exist_ok=True)
fresh = Path(tempfile.mkdtemp(prefix='run-', dir=OUT)); shutil.copy2(harmony, fresh/'0Harmony.dll')
sdk = subprocess.check_output(['dotnet','--list-sdks'], text=True).strip().splitlines()[-1]
compiler = Path(sdk[sdk.index('[')+1:sdk.index(']')]) / sdk.split()[0] / 'Roslyn/bincore/csc.dll'
framework = Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319'
sources = [ROOT/'Utilities/CoopAiPredictionHealthProtection.cs', ROOT/'Patches/Combat/BossLethalHealthProtectionPatch.cs', HERE/'Stubs.cs', HERE/'Program.cs']
sources += [ROOT/path for path in ('Common/Coop/BossRushCoopJson.cs','Common/Data/BossRushJsonValue.cs','Utilities/SimpleJsonHelper.cs')]
target = fresh/'CoopAiPrediction.exe'
args = ['/nologo','/target:exe','/langversion:7.3','/noconfig','/nostdlib+', '/out:"'+str(target)+'"','/r:"'+str(harmony)+'"']
args += ['/r:"'+str(framework/name)+'"' for name in ('mscorlib.dll','System.dll','System.Core.dll')]
args += ['"'+str(p)+'"' for p in sources]
response = fresh/'compile.rsp'; response.write_text('\n'.join(args),encoding='utf-8-sig')
(fresh/'source-sha256.txt').write_text('\n'.join(str(p)+' '+hashlib.sha256(p.read_bytes()).hexdigest() for p in sources+[harmony]),encoding='utf-8')
result = subprocess.call(['dotnet',str(compiler),'@'+str(response)],cwd=ROOT)
if result: raise SystemExit(result)
print('Execute fresh TargetPath:',target,flush=True)
print('SHA-256:',hashlib.sha256(target.read_bytes()).hexdigest(),flush=True)
raise SystemExit(subprocess.call([str(target)],cwd=fresh))
