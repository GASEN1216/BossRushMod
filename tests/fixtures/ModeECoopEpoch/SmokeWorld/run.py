from pathlib import Path
import argparse,hashlib,json,sys,tempfile
from xml.sax.saxutils import escape
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[3]
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=ROOT/'Build/runtime-regressions/ModeECoopEpoch/smoke-world')
args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
OUT=Path(tempfile.mkdtemp(prefix='source-',dir=args.output)).resolve()
path=ROOT/'ModeE/ModeERuntimeModule_CoopConsumables.cs'
source=path.read_text(encoding='utf-8-sig')
def block(signature):
    assert source.count(signature)==1,signature
    start=source.index(signature);end=source.index('{',start)+1;depth=1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]
code='using System;using UnityEngine;namespace BossRush {\n'
code+='\n'.join(block(x) for x in ('internal sealed class CoopModeEUtilitySmoke','internal sealed class CoopModeEUtilityRequest'))
code+='internal partial class ModeERuntimeModule {\n'+'\n'.join(block(x) for x in ('private void PublishCoopUtilitySmoke(','private void OnCoopUtilitySmoke(','private void PlayCoopUtilitySmoke(','private bool ApplyCoopUtilityReceipt(','private static string GetRespawnItemName('))+'}}\n'
source=(ROOT/'ModeE/ModeERuntimeModule_Coop.cs').read_text(encoding='utf-8-sig')
code+='namespace BossRush {'+block('internal sealed class CoopModeEPersonal')+'}\n'
generated=OUT/'Production.cs';generated.write_text(code,encoding='utf-8')
sources=[generated,HERE/'Program.cs']+[ROOT/p for p in ('Common/Coop/BossRushCoopJson.cs','Common/Data/BossRushJsonValue.cs','Utilities/SimpleJsonHelper.cs')]
project=OUT/'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p),{'"':'&quot;'})+'" />' for p in sources)+'</ItemGroup></Project>',encoding='utf-8')
production=[path,ROOT/'ModeE/ModeERuntimeModule_Coop.cs']+sources[2:]
(OUT/'production-sha256.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in production},indent=2)+'\n',encoding='utf-8')
sys.path.insert(0,str(ROOT/'tools'));from run_runtime_regressions import run_project_fixture
exit_code,output=run_project_fixture(project,OUT/'execution',ROOT);print(output);raise SystemExit(exit_code)
