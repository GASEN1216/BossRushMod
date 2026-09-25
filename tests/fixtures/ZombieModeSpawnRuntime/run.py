"""Execute the current Zombie spawn/pause/slot methods against controlled async host callbacks."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/ZombieModeSpawnRuntime'


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
    path = ROOT / 'ZombieMode/ZombieModeSpawner.cs'
    raw = path.read_bytes()
    text = raw.decode('utf-8-sig')
    signatures = ['internal async UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync(',
                  'private bool IsZombieModeNormalSpawnStillAllowed(', 'private bool TryReserveZombieModeNormalSpawnSlot(',
                  'private void ReleaseZombieModeNormalSpawnSlot(', 'internal async UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync(',
                  'private void DestroyZombieModePausedSpawnCandidate(', 'private void PrepareZombieModeSpawnedEnemy(',
                  'private async UniTask<bool> WaitForZombieModeRuntimeResumeAsync(']
    production = 'using System; using System.Collections.Generic; using UnityEngine; using Cysharp.Threading.Tasks;\nnamespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n'
    production += '\n'.join(member(text, signature) for signature in signatures) + '\n}}'
    generated = OUT / 'Production.cs'
    generated.write_text(production, encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps({str(path.relative_to(ROOT)): hashlib.sha256(raw).hexdigest()}, indent=2), encoding='utf-8')
    sources = [generated, HERE / 'Program.cs', HERE / 'Stubs.cs']
    includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
