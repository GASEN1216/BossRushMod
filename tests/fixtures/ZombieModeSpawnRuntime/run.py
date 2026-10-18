"""Execute the current Zombie spawn/pause/slot methods against controlled async host callbacks."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
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
                  'private bool HasZombieModeReachableSpawnAnchor(',
                  'internal bool TryGetNearestZombieModeMapSpawnPositionToPlayer(',
                  'internal Vector3 GetZombieModeBossSpawnPosition(']
    signatures += ['internal bool CollectZombieModeSpawnPoints(', 'private void ResetZombieModeSpawnPointDedupGrid(',
                   'private void TryPopulateZombieModeSpawnPointsFromCachedOriginalSpawnerPositions(',
                   'private void AddZombieModeSpawnPointArray(', 'private void AddZombieModeSpawnPoint(',
                   'private bool HasZombieModeDuplicateSpawnPoint(', 'private void RegisterZombieModeSpawnPointDedupCell(',
                   'private static long GetZombieModeSpawnPointDedupCellKey(']
    damage_path = ROOT / 'ZombieMode/ZombieModeRuntimeModule_PollutionSkills.cs'
    damage_raw = damage_path.read_bytes()
    production = 'using System; using System.Collections.Generic; using Pathfinding; using UnityEngine; using UnityEngine.AI; using Duckov.Buffs; using Cysharp.Threading.Tasks;\nnamespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n'
    production += '\n'.join(member(text, signature) for signature in signatures)
    production += '\n' + member(text, 'private struct ZombieModeSpawnScanEntry')
    production += '\n' + member(text, 'private static int CompareZombieModeSpawnScanEntries(')
    fields = [line.strip() for line in text.splitlines()
              if line.strip().startswith(('private int zombieModeReliableSpawnFailedFrame',
                                          'private readonly List<ZombieModeSpawnScanEntry> zombieModeSpawnScanEntries',
                                          'private static readonly System.Comparison<ZombieModeSpawnScanEntry> ZombieModeSpawnScanOrder'))]
    assert len(fields) == 3, fields
    production += '\n' + '\n'.join(fields) + '\n'
    coop_path = ROOT / 'ZombieMode/ZombieModeRuntimeModule_Coop.cs'
    coop_text = coop_path.read_text(encoding='utf-8-sig')
    for signature in ('private static string CoopZombieParticipantKey(', 'private bool IsCoopZombieParticipant(',
                      'private CharacterMainControl GetCoopZombieCombatTarget(', 'private CharacterMainControl GetCoopZombieSpawnAnchor('):
        production += '\n' + member(coop_text, signature)
    wave_path = ROOT / 'ZombieMode/ZombieModeRuntimeModule_WaveController.cs'
    production += '\n' + member(wave_path.read_text(encoding='utf-8-sig'), 'private void HandleZombieModeHealthDead(')
    boss_path = ROOT / 'ZombieMode/ZombieModeRuntimeModule_BossController.cs'
    boss_text = boss_path.read_text(encoding='utf-8-sig')
    for signature in ('internal void TickZombieModeBossController(', 'private void TryExecuteZombieModeBossSkill(',
                      'private bool TryClaimZombieModeBossSkillWindow(', 'internal void TickZombieModeHunterState(',
                      'internal void TickZombieModeCorruptorState(', 'private bool IsZombieModeBossEngagedWithPlayer(',
                      'private void TeleportZombieModeBossNearPlayer(', 'private bool TryResolveZombieModeBossFallbackPosition('):
        production += '\n' + member(boss_text, signature)
    isolation_path = ROOT / 'ZombieMode/ZombieModeMapIsolation.cs'
    production += '\n' + member(isolation_path.read_text(encoding='utf-8-sig'), 'private bool ShouldSkipZombieModeOriginalCharacter(')
    production += '\n' + member(damage_raw.decode('utf-8-sig'), 'internal void DealZombieModeAreaDamageToPlayer(int runId, CharacterMainControl source,')
    zone_path = ROOT / 'ZombieMode/ZombieModeCoopZoneEffects.cs'
    production += '\n' + member(zone_path.read_text(encoding='utf-8-sig'), 'private IEnumerable<CharacterMainControl> EnumerateZombieModeZoneTargets(') + '\n}'
    # 可达性判据在共享 SpawnPositionHelper（A* 优先、无 A* 退 NavMesh），连同常量一起按原文执行。
    helper_path = ROOT / 'Utilities/SpawnPositionHelper.cs'
    helper_raw = helper_path.read_bytes()
    helper_text = helper_raw.decode('utf-8-sig')
    constants = [line.strip() for line in helper_text.splitlines()
                 if line.strip().startswith(('internal const float ReachableAnchorSnapDistance', 'internal const float ReachableVerticalTolerance'))]
    assert len(constants) == 2, constants
    production += '\ninternal static partial class SpawnPositionHelper {\n' + '\n'.join(constants) + '\n'
    production += member(helper_text, 'internal static bool TryResolveReachableFrom(') + '\n'
    production += member(helper_text, 'internal static bool IsReachableAnchor(') + '\n'
    production += member(helper_text, 'private static bool IsNearWalkableNode(') + '\n}}'
    generated = OUT / 'Production.cs'
    generated.write_text(production, encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                                         for p in (path, damage_path, helper_path, wave_path, isolation_path, coop_path, boss_path, zone_path)}, indent=2), encoding='utf-8')
    sources = [generated, HERE / 'Program.cs', HERE / 'Stubs.cs', HERE.parent / 'CoopOfflineStubs.cs']
    includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    sys.path.insert(0, str(ROOT / 'tools'))
    from run_runtime_regressions import run_project_fixture
    code, output = run_project_fixture(project, OUT / 'runs', ROOT)
    print(output)
    return code


if __name__ == '__main__':
    raise SystemExit(main())
