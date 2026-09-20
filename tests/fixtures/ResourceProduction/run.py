from pathlib import Path
import subprocess
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
files = [OUT/'BuildingHelper.cs', HERE/'BuildingModels.cs', ROOT/'Utilities/ResourceBundleLoader.cs' ,ROOT/'Integration/FactoryResourceLoading.cs',ROOT/'Integration/ProductionIconCache.cs',HERE/'Stubs.cs',HERE/'Program.cs',OUT/'Metrics.cs']
project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
project+=''.join('<Compile Include="'+escape(str(p))+'" />' for p in files)+'</ItemGroup></Project>'
(OUT/'Regression.csproj').write_text(project,encoding='utf8')
raise SystemExit(subprocess.call(['dotnet','run','--project',str(OUT/'Regression.csproj'),'--configuration','Release','--',str(OUT/'data')]))
