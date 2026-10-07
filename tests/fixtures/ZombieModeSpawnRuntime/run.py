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
                  'private async UniTask<bool> WaitForZombieModeRuntimeResumeAsync(',
                  'private bool TryResolveZombieModeSpawnPoint(',
                  'internal Vector3 GetZombieModeSpawnPosition(',
                  'internal bool TryGetZombieModeReliableSpawnPosition(',
                  'internal bool TryGetNearestZombieModeMapSpawnPositionToPlayer(',
                  'internal Vector3 GetZombieModeBossSpawnPosition(']
    damage_path = ROOT / 'ZombieMode/ZombieModeRuntimeModule_PollutionSkills.cs'
    damage_raw = damage_path.read_bytes()
    production = 'using System; using System.Collections.Generic; using Pathfinding; using UnityEngine; using UnityEngine.AI; using Duckov.Buffs; using Cysharp.Threading.Tasks;\nnamespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n'
    production += '\n'.join(member(text, signature) for signature in signatures)
    wave_path = ROOT / 'ZombieMode/ZombieModeRuntimeModule_WaveController.cs'
    production += '\n' + member(wave_path.read_text(encoding='utf-8-sig'), 'private void HandleZombieModeHealthDead(')
    isolation_path = ROOT / 'ZombieMode/ZombieModeMapIsolation.cs'
    production += '\n' + member(isolation_path.read_text(encoding='utf-8-sig'), 'private bool ShouldSkipZombieModeOriginalCharacter(')
    production += '\n' + member(damage_raw.decode('utf-8-sig'), 'internal void DealZombieModeAreaDamageToPlayer(int runId, CharacterMainControl source,') + '\n}'
    # 可达性判据在共享 SpawnPositionHelper（A* 优先、无 A* 退 NavMesh），连同常量一起按原文执行。
    helper_path = ROOT / 'Utilities/SpawnPositionHelper.cs'
    helper_raw = helper_path.read_bytes()
    helper_text = helper_raw.decode('utf-8-sig')
    constants = [line.strip() for line in helper_text.splitlines()
                 if line.strip().startswith(('internal const float ReachableAnchorSnapDistance', 'internal const float ReachableVerticalTolerance'))]
    assert len(constants) == 2, constants
    production += '\ninternal static partial class SpawnPositionHelper {\n' + '\n'.join(constants) + '\n'
    production += member(helper_text, 'internal static bool TryResolveReachableFrom(') + '\n'
    production += member(helper_text, 'private static bool IsNearWalkableNode(') + '\n}}'
    generated = OUT / 'Production.cs'
    generated.write_text(production, encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                                         for p in (path, damage_path, helper_path, wave_path, isolation_path)}, indent=2), encoding='utf-8')
    sources = [generated, HERE / 'Program.cs', HERE / 'Stubs.cs']
    includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
