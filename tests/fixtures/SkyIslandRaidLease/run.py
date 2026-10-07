"""Execute the real independent-raid lease with controlled official-loader substitutes."""
from pathlib import Path
import hashlib
import json
import subprocess
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
OUT=ROOT/'Build'/'runtime-regressions'/'SkyIslandRaidLease'
OUT.mkdir(parents=True, exist_ok=True)
session=(ROOT/'SkyIsland/SkyIslandSession.cs').read_text(encoding='utf-8-sig')
start='        private void DispatchReturnIfReady()'
end='        private void CancelPendingInitialization()'
assert session.count(start)==session.count(end)==1
method=session[session.index(start):session.index(end)]
extracted=OUT/'SessionReturn.cs'
extracted.write_text('using UnityEngine; namespace BossRush { internal sealed partial class SkyIslandSession {\n'+method+'\n}}', encoding='utf-8')
(OUT/'session-return-source.json').write_text(json.dumps({'source':'SkyIsland/SkyIslandSession.cs','method_sha256':hashlib.sha256(method.encode('utf-8')).hexdigest()},indent=2),encoding='utf-8')
result=subprocess.run(['dotnet','build',str(HERE/'Regression.csproj'),'--configuration','Release','--output',str(OUT/'bin'),'-p:BaseIntermediateOutputPath='+str(OUT/'obj')+'/', '-p:SessionReturnSource='+str(extracted)],cwd=ROOT)
if result.returncode: raise SystemExit(result.returncode)
raise SystemExit(subprocess.run(['dotnet',str(OUT/'bin'/'Regression.dll')],cwd=ROOT).returncode)
