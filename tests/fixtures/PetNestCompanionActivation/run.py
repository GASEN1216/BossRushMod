"""Execute actual companion activation, AI ownership, damage normalization and cleanup."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape
ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/PetNestCompanionActivation'
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for end in range(opening, len(source)):
        depth += (source[end] == '{') - (source[end] == '}')
        if depth == 0:
            return source[start:end + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    spawner = ROOT / 'PetNest/PetNestCompanionSpawner.cs'
    source = clean_source(spawner.read_text(encoding='utf-8-sig'))
    text = 'using System; using UnityEngine; using ItemStatsSystem; namespace BossRush {\n'
    text += member(source, 'internal sealed class PetNestCompanionHandle')
    text += '\ninternal static partial class PetNestCompanionSpawner {\n'
    text += '\n'.join(member(source, signature) for signature in (
        'internal static bool TryActivate(', 'internal static void CleanupOnce(',
        'internal static void NormalizeCombatOutput(', 'private static void ClampDamageStat(',
        'private static void ApplyPersonalityToAI(', 'private static void TopUpHealthToMax(',
        'private static void DestroyClone('))
    generated = OUT / 'Production.cs'
    generated.write_text(text + '\n}}', encoding='utf-8')
    official = ROOT / '鸭科夫源码/TeamSoda.Duckov.Core'
    production = [ROOT / 'PetNest/PetNestCompanionAgent.cs', official / 'Team.cs', official / 'Teams.cs']
    paths = production + [generated, HERE / 'Program.cs']
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'
                      + ''.join('<Compile Include="' + escape(str(p)) + '" />' for p in paths)
                      + '</ItemGroup></Project>', encoding='utf-8')
    (OUT / 'sources.json').write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in production + [spawner]}, indent=2), encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
