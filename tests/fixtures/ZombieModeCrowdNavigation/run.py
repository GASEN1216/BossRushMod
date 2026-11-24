"""Execute the production zombie path-request throttle decision against controlled path states."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/ZombieModeCrowdNavigation'


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    # 带 XML 文档注释的成员连注释一起带上，便于排查生成文件。
    opening = source.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    path = ROOT / 'ZombieMode/ZombieModeCrowdNavigation.cs'
    text = path.read_bytes().decode('utf-8-sig')
    tuning_path = ROOT / 'ZombieMode/ZombieModeTuning.cs'
    tuning_text = tuning_path.read_bytes().decode('utf-8-sig')
    constants = re.findall(r'public const float Path(?:Repath\w+|RequestPendingTimeoutSeconds) = [0-9.]+f?;', tuning_text)
    assert len(constants) == 10, constants
    production = 'using UnityEngine;\nnamespace BossRush {\n'
    production += 'public static partial class ZombieModeTuning {\n' + '\n'.join(constants) + '\n}\n'
    production += 'internal static class ZombieModeCrowdNavigation {\n'
    production += '\n'.join(member(text, signature) for signature in (
        'internal static bool ShouldThrottleZombieModePathRequest(',
        'private static float HorizontalDistance(',
        'private static bool IsFinite('))
    production += '\n}\n}\n'
    generated = OUT / 'Production.cs'
    generated.write_text(production, encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                                         for p in (path, tuning_path)}, indent=2), encoding='utf-8')
    sources = [generated, HERE / 'Program.cs', HERE / 'Stubs.cs']
    includes = ''.join('<Compile Include="' + escape(str(source), {'"': '&quot;'}) + '" />' for source in sources)
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
