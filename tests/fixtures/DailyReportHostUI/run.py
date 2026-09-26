"""Run the complete DailyReport module/UI owners, current host entry and actual view teardown."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT/'Build/runtime-regressions/DailyReportHostUI'
sys.path.insert(0,str(ROOT/'tests'))
from integration_host_source import materialize_host


def member(source, signature):
    assert source.count(signature)==1,signature
    start=source.index(signature); opening=source.index('{',start); end=opening+1; depth=1
    while depth:
        depth+=(source[end]=='{')-(source[end]=='}'); end+=1
    return source[start:end]


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    production=['Integration/DailyReport/DailyReportRuntimeModule.cs',
                'Integration/DailyReport/DailyReportRuntimeModule_UI.cs','Utilities/SafeRuntime.cs']
    host=materialize_host(ROOT,OUT/'HostUI.cs','DailyReportUIBridge')
    registration=(ROOT/'ModBehaviourRuntimeModules.cs').read_text(encoding='utf-8-sig')
    start=registration.index('dailyReportRuntime = new DailyReportRuntimeModule();')
    end=registration.index('runtimeModuleHost.Register(dailyReportRuntime);',start)+len('runtimeModuleHost.Register(dailyReportRuntime);')
    binding=registration[start:end]
    view=(ROOT/'Integration/DailyReport/DailyReportUI.cs').read_text(encoding='utf-8-sig')
    cleanup=member(view,'internal static void CleanupRuntime()')
    generated=OUT/'ProductionBindings.cs'
    generated.write_text('using UnityEngine; namespace BossRush { public partial class ModBehaviour {\n'
                         +'internal void BindProductionForFixture() {\n'+binding+'\n}}\n'
                         +'internal sealed partial class DailyReportView {\n'+cleanup+'\n}}',encoding='utf-8')
    paths=[ROOT/p for p in production]+[host,generated,HERE/'Stubs.cs',HERE/'Program.cs']
    includes=''.join('<Compile Include="'+escape(str(p),{'"':'&quot;'})+'" />' for p in paths)
    project=OUT/'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>'+includes+'</ItemGroup></Project>',encoding='utf-8')
    inputs=production+['Integration/IntegrationHostCompatibility.cs','ModBehaviourRuntimeModules.cs','Integration/DailyReport/DailyReportUI.cs']
    (OUT/'production-sha256.json').write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in inputs},indent=2),encoding='utf-8')
    return subprocess.call(['dotnet','run','--project',str(project),'--configuration','Release'],cwd=ROOT)


if __name__=='__main__':raise SystemExit(main())
