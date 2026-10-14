from pathlib import Path
import hashlib
import json
import re
import sys
from xml.sax.saxutils import escape
ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT/'Build/runtime-regressions/ResourceProduction'
OUT.mkdir(parents=True,exist_ok=True)
# The pure metric file is Dev-only in the game; expose its identical body in this release-policy fixture.
metric = (ROOT/'DebugAndTools/ResourcePerformanceMetrics.cs').read_text(encoding='utf8')
(OUT/'Metrics.cs').write_text(metric.removeprefix('#if BOSSRUSH_DEV\n').rsplit('#endif',1)[0],encoding='utf8')
helper = (ROOT/'Common/Buildings/BuildingModelHelper.cs').read_text(encoding='utf8')
method = helper[helper.index('        internal static bool TryInstantiateBundle('):helper.index('        internal static Renderer[] CollectStarwishRenderableComponents(')]
(OUT/'BuildingHelper.cs').write_text('using System; using UnityEngine; namespace BossRush { internal static class BuildingModelHelper {\n'+method+'\n}}',encoding='utf8')
sys.path.insert(0, str(ROOT/'tests'))
from cs_source_util import clean_source
report_path=ROOT/'DebugAndTools/F3GameplayValidationResourcePerformance.cs'
report=clean_source(report_path.read_text(encoding='utf8'))
masked=re.sub(r'''@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*' ''',lambda match:' '*len(match.group()),report,flags=re.X)
def member(signature):
    assert report.count(signature)==1,signature
    start=report.index(signature);opening=masked.index('{',start);depth=0
    for end in range(opening,len(masked)):
        depth+=(masked[end]=='{')-(masked[end]=='}')
        if depth==0:return report[start:end+1]
    raise AssertionError(signature)
extracted='using System; using System.IO; using System.Collections.Generic; using UnityEngine; namespace BossRush {\n'
extracted+='[Serializable] '+member('internal sealed class ResourcePerformanceWindow')+'\n'
extracted+='[Serializable] '+member('internal sealed class ResourcePerformanceReport')+'\n'
extracted+='internal sealed partial class F3GameplayValidationRunner {\n'
extracted+='private readonly ResourcePerformanceReport resourcePerformance = new ResourcePerformanceReport();\n'
for signature in ('private string AddResourcePerformanceManifest(', 'private string ResourcePerformanceSummary()', 'private void WriteResourcePerformanceReport()'):
    extracted+=member(signature)+'\n'
extracted+='}}'
(OUT/'Report.cs').write_text(extracted,encoding='utf8')
(OUT/'source-hashes.json').write_text(json.dumps({str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (report_path,ROOT/'DebugAndTools/ResourcePerformanceMetrics.cs',ROOT/'Utilities/ResourceBundleLoader.cs',ROOT/'Common/Coop/BossRushCoopJson.cs')},indent=2),encoding='utf8')
files = [OUT/'BuildingHelper.cs', HERE/'BuildingModels.cs', ROOT/'Utilities/ResourceBundleLoader.cs' ,ROOT/'Integration/FactoryResourceLoading.cs',ROOT/'Integration/ProductionIconCache.cs',HERE/'Stubs.cs',HERE.parent/'TextureCopyStubs.cs',HERE/'Program.cs',OUT/'Metrics.cs',OUT/'Report.cs']
files += [ROOT/p for p in ('Common/Coop/BossRushCoopJson.cs','Common/Data/BossRushJsonValue.cs','Utilities/SimpleJsonHelper.cs')]
project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
project+=''.join('<Compile Include="'+escape(str(p))+'" />' for p in files)+'</ItemGroup></Project>'
(OUT/'Regression.csproj').write_text(project,encoding='utf8')
sys.path.insert(0, str(ROOT/'tools'))
from run_runtime_regressions import run_project_fixture
code, output=run_project_fixture(OUT/'Regression.csproj',OUT,ROOT)
print(output)
raise SystemExit(code)
