"""Execute unchanged periodic E utility/coal and F fortification methods through the real channel."""
from pathlib import Path
import argparse,hashlib,json,sys,tempfile
from xml.sax.saxutils import escape
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[3]
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=ROOT/'Build/runtime-regressions/ModeECoopEpoch/repeat-callers')
args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
OUT=Path(tempfile.mkdtemp(prefix='source-',dir=args.output)).resolve()
def block(source,signature):
    assert source.count(signature)==1,signature
    start=source.index(signature);end=source.index('{',start)+1;depth=1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]
code='using System;using System.Collections.Generic;using UnityEngine;namespace BossRush {\n'
production=[]
for path,dto,owner,method in (
    ('ModeE/ModeERuntimeModule_CoopConsumables.cs','CoopModeEUtilityRequest','ModeERuntimeModule','TickCoopUtilities'),
    ('ModeE/ModeERuntimeModule_CoopCoal.cs','CoopModeECoalRequest','ModeERuntimeModule','TickCoopCoal'),
    ('ModeF/ModeFRuntimeModule_CoopFortifications.cs','CoopModeFFortificationRequest','ModeFRuntimeModule','TickCoopFortificationTransactions')):
    path=ROOT/path;production.append(path);source=path.read_text(encoding='utf-8-sig')
    code+=block(source,'internal sealed class '+dto)+'\ninternal sealed partial class '+owner+'{'+block(source,'private void '+method+'(')+'}\n'
epath=ROOT/'ModeE/ModeERuntimeModule_Coop.cs';production.append(epath);esource=epath.read_text(encoding='utf-8-sig')
code+=''.join(block(esource,'internal sealed class '+dto) for dto in ('CoopModeEParticipant','CoopModeEPersonal','CoopModeEHireRequest'))
code+='internal sealed partial class ModeERuntimeModule {'+block(esource,'private void TickCoopModeE(')+'}'
code+='}'
network=OUT/'Network.cs';network.write_text((ROOT/'tests/fixtures/CoopProtocol/Stubs.cs').read_text(encoding='utf-8').replace('internal sealed class BossRushCoopPlayer','internal sealed partial class BossRushCoopPlayer').replace('internal static class BossRushCoop','internal static partial class BossRushCoop'),encoding='utf-8')
generated=OUT/'Production.cs';generated.write_text(code,encoding='utf-8')
paths=[generated,HERE/'Program.cs',HERE/'EntryStartup.cs',network]+[ROOT/p for p in ('tests/fixtures/CoopSceneCoreBoundary.cs','Utilities/CoopGameplayChannel.cs','Common/Coop/BossRushCoopJson.cs','Common/Data/BossRushJsonValue.cs','Utilities/SimpleJsonHelper.cs')]
project=OUT/'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p),{'"':'&quot;'})+'" />' for p in paths)+'</ItemGroup></Project>',encoding='utf-8')
(OUT/'production-sha256.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in production+paths[4:]},indent=2)+'\n',encoding='utf-8')
sys.path.insert(0,str(ROOT/'tools'));from run_runtime_regressions import run_project_fixture
exit_code,output=run_project_fixture(project,OUT/'execution',ROOT);print(output);raise SystemExit(exit_code)
