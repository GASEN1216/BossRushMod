"""Execute current map, blacklist and numeric parsers without Unity or player saves."""
from pathlib import Path
import subprocess
import xml.sax.saxutils as xml

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/AuditCoreParsing'
OUT.mkdir(parents=True, exist_ok=True)
sources = ['Common/Data/BossRushJsonValue.cs', 'Common/Data/JsonDataRegistry.cs',
           'Utilities/SimpleJsonHelper.cs', 'Common/MapConfig/BossRushMapConfig.cs',
           'Common/MapConfig/MapSpawnPointRegistry.cs', 'Config/BossPoolFactorJson.cs']
text = (ROOT / 'Config/LootBlacklistRegistry.cs').read_text(encoding='utf-8-sig')
start = text.index('        internal static int[] ParseItemIds(string json)')
end = text.index('        private static int[] CreateFallbackBlacklistIds()', start)
generated = OUT / 'Blacklist.cs'
generated.write_text('using System.Collections.Generic; namespace BossRush { internal static class LootBlacklistRegistry {\n'
                     + text[start:end] + '\n}}', encoding='utf-8')
includes = sources + [str(HERE / 'Program.cs'), str(generated)]
project = OUT / 'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
                   '<OutputType>Exe</OutputType><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                   '</PropertyGroup><ItemGroup>' + ''.join('<Compile Include="' + xml.escape(str(ROOT / p), {'"': '&quot;'}) + '" />'
                   for p in includes) + '</ItemGroup></Project>', encoding='utf-8')
result = subprocess.run(['dotnet', 'build', str(project), '-o', str(OUT / 'bin')], cwd=ROOT)
if result.returncode:
    raise SystemExit(result.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin/Regression.dll')], cwd=ROOT).returncode)
