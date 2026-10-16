"""Execute production mode destruction, cleanup and validity gates with Unity boundary doubles."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get('BOSSRUSH_FIXTURE_OUTPUT', str(ROOT / 'Build/runtime-regressions/ModeDestroyLifecycle')))
OUT.mkdir(parents=True, exist_ok=True)
hashes = {}

def member(path, marker):
    source = ROOT / path
    text = source.read_text(encoding='utf-8-sig')
    start = text.index(marker)
    opening = text.index('{', start)
    depth = 0
    for index in range(opening, len(text)):
        depth += (text[index] == '{') - (text[index] == '}')
        if depth == 0:
            hashes[path] = hashlib.sha256(source.read_bytes()).hexdigest()
            return text[start:index + 1]
    raise ValueError(marker)

groups = {
    'ModeDRuntimeModule': {
        'ModeD/ModeDRuntimeModule_Lifecycle.cs': ['private void CleanupModeDRuntimeOnDestroy()', 'private void CleanupModeDWaveEnemiesOnExit()'],
    },
    'ModeERuntimeModule': {
        'ModeE/ModeERuntimeModule.cs': ['public override void OnAwake(', 'public override void OnDestroy()'],
        'ModeE/ModeEStartup.cs': ['private System.Collections.IEnumerator PrepareModeEStartupCoroutine(', 'private bool TryRunModeEStartupWarmupStep(', 'private void ClearModeEStartupWarmupCoroutine(', 'internal void ScheduleModeEStartupWarmup(', 'internal void StopModeEStartupWarmupIfPending()', 'private int BeginModeESession()', 'private void InvalidateModeESession()', 'internal void ResetModeESharedRuntimeState(', 'internal bool IsModeESessionStillValid(', 'private bool IsModeESessionOwnerCurrent()', 'internal bool IsModeEOrModeFSpawnSessionStillValid('],
        'ModeE/ModeELifecycle.cs': ['public void EndModeE('],
        'ModeE/ModeERuntimeModule_Coop.cs': ['private sealed class CoopModeEPlayerTeam', 'private void RememberCoopModeEPlayerTeam(', 'private void SetCoopModeEPlayerTeam(', 'private bool RestoreCoopModeEPlayerTeams('],
        'ModeE/ModeEBattle_ScalingAndRuntime.cs': ['private void RemoveModeEPlayerScalingModifiers()', 'internal void RemoveModeEScalingModifiers(', 'private void CleanupModeEEnemyRuntimeState('],
    },
    'ModeFRuntimeModule': {
        'ModeF/ModeFRuntimeModule.cs': ['public override void OnAwake(', 'public override void OnDestroy()'],
        'ModeF/ModeFEntry.cs': ['private int BeginModeFSession()', 'private void InvalidateModeFSession()', 'internal bool IsModeFSessionStillValid(', 'private bool IsModeFSessionOwnerCurrent()'],
        'ModeF/ModeFPhases.cs': ['internal void ExitModeF(', 'private void CleanupModeFPlayerMaxHealthGrowth()', 'private void RetireModeFActiveBossAtExit(', 'private System.Collections.IEnumerator DestroyModeFExitBossDeferred(', 'private void CleanupModeFDeferredExitBossObjects()'],
        'ModeF/ModeFRespawn.cs': ['private void UnregisterModeFBossDeath(', 'private void UnregisterModeFBossLoot(', 'private void CleanupModeESharedRuntimeForModeFBoss(', 'private void CleanupModeFBossRuntimeState(', 'private void ResetModeESharedRuntimeAfterModeF()'],
        'ModeF/ModeFBounty.cs': ['private void RemoveModeFBossGrowthModifiers('],
        'ModeF/ModeFFortifications.cs': ['internal void CancelFortPlacement()', 'private void ClearFortPlacementPreviewMaterialCache()'],
        'ModeF/ModeFFortifications_RepairRewardsCleanup.cs': ['private void CleanupAllModeFortifications()', 'internal void RefundModeFUtilityItem('],
    },
}
generated = ['using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.Events; using ItemStatsSystem; namespace BossRush {']
for owner, sources in groups.items():
    generated.append('internal sealed partial class ' + owner + ' {')
    if owner in ('ModeERuntimeModule','ModeFRuntimeModule'):
        mode=owner[4];entry='ModeE/ModeEStartup.cs' if mode=='E' else 'ModeF/ModeFEntry.cs'
        raw=(ROOT/entry).read_text(encoding='utf-8-sig')
        for kind,suffix in [('bool','Connected'),('string','Room'),('int','SceneHandle')]:
            start=raw.index('private '+kind+' mode'+mode+'Session'+suffix)
            generated.append(raw[start:raw.index(';',start)+1])
    for source, signatures in sources.items():
        generated.extend(member(source, signature) for signature in signatures)
    if owner == 'ModeERuntimeModule':
        raw = (ROOT / 'ModeE/ModeERuntimeModule_Coop.cs').read_text(encoding='utf-8-sig')
        start = raw.index('private readonly Dictionary<CharacterMainControl, CoopModeEPlayerTeam>')
        generated.append(raw[start:raw.index(';', start) + 1])
    generated.append('}')
generated.append('internal static partial class BossRushCoop {')
generated.extend(member('Common/Coop/BossRushCoop.cs', signature) for signature in ['public static string GetPlayerId(', 'public static bool IsPlayer('])
generated.append('}')
generated.append('}')
(OUT / 'Generated.cs').write_text('\n'.join(generated), encoding='utf-8')
production = [ROOT / p for p in ['ModeD/ModeDRuntimeModule.cs', 'ModeF/ModeFModels.cs', 'Utilities/ModeEFEnemyRegistry.cs', 'Utilities/RunScopedRegistry.cs']]
sources = [ROOT / "tests/fixtures/CoopOfflineStubs.cs"] + production + [HERE / 'Stubs.cs', HERE / 'Program.cs', OUT / 'Generated.cs']
for path in production:
    hashes[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
includes = ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '"/>' for p in sources)
project = OUT / 'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
(OUT / 'production-sha256.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
sys.path.insert(0,str(ROOT/'tools'))
from run_runtime_regressions import run_project_fixture
code,output=run_project_fixture(project,OUT/'runs',ROOT)
print(output)
raise SystemExit(code)
