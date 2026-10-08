"""Execute the real independent-raid lease with controlled official-loader substitutes."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import tempfile
import os
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
OUTPUT_ROOT=ROOT/'Build'/'runtime-regressions'/'SkyIslandRaidLease'
OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
OUT=Path(tempfile.mkdtemp(prefix='run-',dir=OUTPUT_ROOT)).resolve()
session=(ROOT/'SkyIsland/SkyIslandSession.cs').read_text(encoding='utf-8-sig')
start='        private void DispatchReturnIfReady()'
end='        private void CancelPendingInitialization()'
assert session.count(start)==session.count(end)==1
method=session[session.index(start):session.index(end)]
sys.path.insert(0, str(ROOT/'tests/fixtures/ModeDEntryOwnership'))
from run import member
tick=(ROOT/'SkyIsland/SkyIslandSessionTick.cs').read_text(encoding='utf-8-sig')
method += member(session, 'private static void Safe(') + member(tick, 'private void TickFault(')
a=session.index('            loadStarted = true;', session.index('private IEnumerator Build()'))
b=session.index('            // 官方地图的分区灰显', a)
method += 'private System.Collections.IEnumerator WaitForEntryActivation() {\n' + session[a:b] + '\n}'
a=session.index('            IEnumerator warm = PrewarmLootGuarded();')
b=session.index('            // 导航扫描跨了很多帧', a)
method += 'private System.Collections.IEnumerator DriveEntryPrewarm() {\n' + session[a:b] + '\n}'
extracted=OUT/'SessionReturn.cs'
extracted.write_text('using System; using System.Collections; using UnityEngine; using UnityEngine.SceneManagement; namespace BossRush { internal sealed partial class SkyIslandSession {\n'+method+'\n}}', encoding='utf-8')
(OUT/'session-return-source.json').write_text(json.dumps({'source':'SkyIsland/SkyIslandSession.cs','method_sha256':hashlib.sha256(method.encode('utf-8')).hexdigest()},indent=2),encoding='utf-8')
sources = [ROOT/'SkyIsland'/name for name in ('SkyIslandSession.cs','SkyIslandSessionEntry.cs','SkyIslandSessionTick.cs','SkyIslandRaidLease.cs')]
(OUT/'production-sha256.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},indent=2),encoding='utf-8')
result=subprocess.run(['dotnet','build',str(HERE/'Regression.csproj'),'--configuration','Release','--output',str(OUT/'bin'),'-p:BaseIntermediateOutputPath='+str(OUT/'obj')+os.sep, '-p:SessionReturnSource='+str(extracted),'-target:Build','-getProperty:TargetPath,TargetFramework'],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
output=result.stdout.decode('utf-8',errors='replace'); print(output)
if result.returncode: raise SystemExit(result.returncode)
properties=json.loads(output[output.rfind('\n{')+1:].strip())
target=Path(properties['TargetPath'] if 'TargetPath' in properties else properties['Properties']['TargetPath']).resolve()
assert target.is_relative_to(OUT/'bin') and target.is_file(), str(target)
print('Execute fresh TargetPath: '+str(target))
print('SHA-256: '+hashlib.sha256(target.read_bytes()).hexdigest())
raise SystemExit(subprocess.run(['dotnet',str(target)],cwd=ROOT).returncode)
