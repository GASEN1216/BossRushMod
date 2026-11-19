"""Execute current map, blacklist and numeric parsers without Unity or player saves."""
from pathlib import Path
import json
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
# 实际刷新点文件的标量字段由 Python json 独立读出，交给 C# 与生产 MapSpawnPointRegistry 的读取逐字比对。
# 口径同生产：字符串字段缺失 / null / 非字符串为 ""（previewImageName 为 null）；整数字段缺失或非数字取默认。
INT_MAX = 2147483647


def expected_int(value, default):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return default
    rounded = round(value)
    return rounded if -2147483648 <= rounded <= INT_MAX else default


rows = []
for path in sorted((ROOT / 'Assets/SpawnPoints').glob('*.json')):
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    text = lambda key: data.get(key) if isinstance(data.get(key), str) else ''
    preview = data.get('previewImageName') if isinstance(data.get('previewImageName'), str) else None
    rows.append({'file': path.name, 'sceneName': text('sceneName'), 'sceneID': text('sceneID'),
                 'displayNameCN': text('displayNameCN'), 'displayNameEN': text('displayNameEN'),
                 'previewImageName': preview, 'sortOrder': expected_int(data.get('sortOrder'), INT_MAX),
                 'beaconIndex': expected_int(data.get('beaconIndex'), 0)})
if not rows:
    raise SystemExit('Assets/SpawnPoints has no JSON files')
expected = OUT / 'spawn-scalars.tsv'
# 制表符分隔、\u0001 表示 null；文件名与字段都不含这两个字符。
expected.write_text(''.join('\t'.join('\u0001' if row[key] is None else str(row[key]) for key in (
    'file', 'sceneName', 'sceneID', 'displayNameCN', 'displayNameEN', 'previewImageName', 'sortOrder', 'beaconIndex')) + '\n'
    for row in rows), encoding='utf-8')
includes = sources + [str(HERE / 'Program.cs'), str(generated)]
project = OUT / 'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
                   '<OutputType>Exe</OutputType><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                   '</PropertyGroup><ItemGroup>' + ''.join('<Compile Include="' + xml.escape(str(ROOT / p), {'"': '&quot;'}) + '" />'
                   for p in includes) + '</ItemGroup></Project>', encoding='utf-8')
result = subprocess.run(['dotnet', 'build', str(project), '-o', str(OUT / 'bin')], cwd=ROOT)
if result.returncode:
    raise SystemExit(result.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin/Regression.dll'), str(expected)], cwd=ROOT).returncode)
