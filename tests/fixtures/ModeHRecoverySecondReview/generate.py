"""Compile verbatim production methods; replace only Unity and persistence boundaries."""
from pathlib import Path
import hashlib

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Build' / 'modeh-recovery-second-review' / 'generated'
OUT.mkdir(parents=True, exist_ok=True)
hashes = []


def method(path, signature):
    source = (ROOT / path).read_text(encoding='utf-8-sig')
    start = source.index(signature)
    opening = source.index('{', start)
    depth, pos = 1, opening + 1
    while depth:
        depth += (source[pos] == '{') - (source[pos] == '}')
        pos += 1
    result = source[start:pos]
    hashes.append((path, signature, hashlib.sha256(result.encode()).hexdigest()))
    return result


groups = {
    'ModeHRuntimeModule': [
        ('ModeHRuntimeModule_SettlementFlow.cs', 'private ModeHPageContent BuildCompletedSettlementPageContent()'),
        ('ModeHRuntimeModule_SettlementFlow.cs', 'private bool IsSettlementActionCurrent('),
        ('ModeHRuntimeModule_SettlementFlow.cs', 'private void SelectSettlementReward('),
        ('ModeHRuntimeModule_SettlementFlow.cs', 'private void CompleteSettlementAndRoute()'),
        ('ModeHRuntimeModule_CombatProfiles.cs', 'private ModeHMatchReportDto FindLatestPendingReport()'),
        ('ModeHRuntimeModule_CombatProfiles.cs', 'private ModeHSeasonRewardOperationDto FindRewardOperation('),
        ('ModeHRuntimeModule_UiFlow.cs', 'private void AbandonSeasonFromRecovery()'),
        ('ModeHRuntimeModule_UiFlow.cs', 'private void DestroyUi()'),
        ('ModeHRuntimeModule_UiFlow.cs', 'private void HideRecoveryShell()'),
        ('ModeHRuntimeModule_SceneFlow.cs', 'private void ReleaseRuntimeObjects()'),
        ('ModeHRuntimeModule_SceneFlow.cs', 'private static string ComposeRunId('),
        ('ModeHRuntimeModule_SceneFlow.cs', 'private static long ComposeRunSeed('),
    ],
    'ModeHSeasonRewardService': [
        ('ModeHSeasonRewardService.cs', 'public static bool TrySelectKit('),
        ('ModeHSeasonRewardService.cs', 'public static bool TryDeclineToFame('),
        ('ModeHSeasonRewardService.cs', 'public static bool TryArchive('),
        ('ModeHSeasonRewardService.cs', 'private static void ApplyFameDisplay('),
        ('ModeHSeasonRewardService.cs', 'private static ModeHSeasonRewardOperationDto FindByOperationId('),
        ('ModeHSeasonRewardService.cs', 'private static ModeHProfileDto FindProfile('),
    ],
    'ModeHHallOfFamePersistence': [
        ('ModeHHallOfFamePersistence.cs', 'public static bool StageRecordInsert('),
        ('ModeHHallOfFamePersistence.cs', 'private static int CompareRecords('),
    ],
    'ModeHWarehouseStakeJournal': [
        ('ModeHWarehouseStakeJournal.cs', 'public static bool IsTerminalPhase('),
    ],
}
parts = ['using System; using System.Collections.Generic; namespace BossRush {']
for name, methods in groups.items():
    modifier = '' if name == 'ModeHRuntimeModule' else 'static '
    parts.append('internal ' + modifier + 'partial class ' + name + ' {')
    parts += [method('ModeH/' + path, signature) for path, signature in methods]
    parts.append('}')
parts.append('}')
(OUT / 'Extracted.cs').write_text('\n'.join(parts), encoding='utf-8')
(OUT / 'source-hashes.txt').write_text('\n'.join(' | '.join(row) for row in hashes), encoding='utf-8')
print('Mode H recovery regression: extracted', len(hashes), 'production methods')
