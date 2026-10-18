"""Execute the current Zombie Mode caliber-matched ammo selection against a controlled item table."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/ZombieModeAmmoSelection'


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
    hashes = {}

    def read(name):
        path = ROOT / 'ZombieMode' / name
        raw = path.read_bytes()
        hashes[str(path.relative_to(ROOT))] = hashlib.sha256(raw).hexdigest()
        return raw.decode('utf-8-sig')

    grants = read('ZombieModeRewardItemGrants.cs')
    rewards = read('ZombieModeRewards.cs')
    source = ('using System; using System.Collections.Generic; using UnityEngine; using Duckov.Utilities; using ItemStatsSystem;\n'
              'namespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n')
    for signature in ('private bool TryGiveZombieModeAmmo(', 'private int PickZombieModeAmmoTypeForCaliber(',
                      'private int[] SearchZombieModeAmmo(', 'private static int GetZombieModeItemQuality(',
                      'internal static bool IsZombieModeAttributeBonusAtCap('):
        source += member(grants, signature) + '\n'
    constants = re.findall(r'^        private const int ZombieModeAmmoAnyQualityMax = \d+;', grants, re.M)
    assert len(constants) == 1, constants
    source += constants[0] + '\n'
    for signature in ('private int PickZombieModeStrictQualityCandidate(', 'private bool IsZombieModeItemQualityInRange('):
        source += member(rewards, signature) + '\n'
    tags = re.findall(r'^        internal static readonly string\[\] ZombieModeRewardTag(?:Ammo|Bullet) = [^\n]+;', rewards, re.M)
    assert len(tags) == 2, tags
    source += '\n'.join(tags) + '\n}}'
    generated = OUT / 'Production.cs'
    generated.write_text(source, encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
    includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />'
                       for path in (generated, HERE / 'Stubs.cs', HERE / 'Program.cs'))
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
                       '<LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
