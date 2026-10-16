"""Execute production random-event projection and director gate with isolated event effects."""
from pathlib import Path
import hashlib, json, subprocess, argparse, sys
from xml.sax.saxutils import escape
ROOT=Path(__file__).resolve().parents[3]; HERE=Path(__file__).resolve().parent; OUT=ROOT/'Build/runtime-regressions/RandomEventsCoop'
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=OUT);OUT=parser.parse_args().output.resolve()
def block(text, signature):
    assert text.count(signature)==1, signature
    start=text.index(signature); opening=text.index('{',start); end=opening+1; depth=1
    while depth:
        depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[start:end]
def main():
    OUT.mkdir(parents=True,exist_ok=True)
    coop=ROOT/'RandomEvents/RandomEventsRuntimeModule_Coop.cs'; director=ROOT/'RandomEvents/RandomEventDirector.cs'
    c=coop.read_text(encoding='utf-8-sig');d=director.read_text(encoding='utf-8-sig')
    code='using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.SceneManagement; namespace BossRush {\n'
    for signature in ('internal sealed class CoopRandomEventState','internal sealed class CoopRandomEventFx','internal sealed partial class RandomEventDirector','internal sealed class CoopRandomEventProjection'):
        code+=block(c,signature)+'\n'
    code+='internal sealed partial class RandomEventDirector {\n'
    for signature in ('internal void Tick(float deltaTime)','internal void OnSceneChanged()', 'private RandomEventContext CreateContext(','private void EndActiveEvent('):
        code+=block(d,signature)+'\n'
    code+='} internal sealed partial class RandomEventsRuntimeModule {\n'
    for signature in ('private void ReceiveCoopFx(', 'private void RefreshCoopScope(', 'private void ClearCoopAirdropVisuals('):
        code+=block(c,signature)+'\n'
    code+='}}'
    generated=OUT/'Production.cs';generated.write_text(code,encoding='utf-8')
    codec_sources=[ROOT/name for name in ('Common/Coop/BossRushCoopJson.cs','Common/Data/BossRushJsonValue.cs','Utilities/SimpleJsonHelper.cs')]
    sources=[generated,HERE/'Program.cs',ROOT/'RandomEvents/RandomEventModels.cs']+codec_sources
    project=OUT/'Regression.csproj';includes=''.join('<Compile Include="'+escape(str(p),{'"':'&quot;'})+'" />' for p in sources)
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>'+includes+'</ItemGroup></Project>',encoding='utf-8')
    (OUT/'production-sha256.json').write_text(json.dumps({str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [coop,director,sources[2]]+codec_sources},indent=2),encoding='utf-8')
    sys.path.insert(0,str(ROOT/'tools'))
    from run_runtime_regressions import run_project_fixture
    code,output=run_project_fixture(project,OUT/'execution',ROOT);print(output)
    if code:return code
    sys.path.insert(0,str(HERE/'Party'))
    from party_fixture import run as run_party
    code,output=run_party(ROOT,OUT/'party',block,run_project_fixture);print(output);return code
if __name__=='__main__':raise SystemExit(main())
