"""Execute real enemy drop delivery and run-only registration with recorded inventory/native endpoints."""
from pathlib import Path
import hashlib
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/ZombieCoopDrops'


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    path = ROOT / 'ZombieMode/ZombieModeDropsAndPerformance.cs'
    source = path.read_text(encoding='utf-8-sig')
    generated = 'using System; using UnityEngine; using ItemStatsSystem; namespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n'
    for signature in ('private GameObject TryDropZombieModeItemNearPosition(',
                      'private void DropZombieModeItemAtPlayerFeet(',
                      'private void RegisterZombieModeDropCandidate('):
        generated += member(source, signature) + '\n'
    production = OUT / 'Production.cs'
    production.write_text(generated + '}}', encoding='utf-8')
    (OUT / 'production-sha256.txt').write_text(hashlib.sha256(path.read_bytes()).hexdigest() + ' ' + str(path), encoding='utf-8')
    paths = [production, HERE / 'Stubs.cs', HERE / 'Program.cs']
    includes = ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    sys.path.insert(0, str(ROOT / 'tools'))
    from run_runtime_regressions import run_project_fixture
    code, output = run_project_fixture(project, OUT / 'runs', ROOT)
    print(output)
    return code


if __name__ == '__main__':
    raise SystemExit(main())
