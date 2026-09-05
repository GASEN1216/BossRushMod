"""Re-extract production methods on every build; only host boundaries are replaced."""
from pathlib import Path
import hashlib
import re

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Build' / 'modeh-review-fixes' / 'generated'
OUT.mkdir(parents=True, exist_ok=True)
hashes = []

def method(path, signature):
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    start = text.index(signature)
    opening = text.index('{', start)
    depth, pos = 1, opening + 1
    while depth:
        depth += (text[pos] == '{') - (text[pos] == '}')
        pos += 1
    result = text[start:pos]
    hashes.append((path, signature, hashlib.sha256(result.encode()).hexdigest()))
    return result

groups = {
    'ModeHRuntimeModule': [
        ('ModeH/ModeHRuntimeModule.cs', 'private void RestoreFromSaveIfPresent()'),
        ('ModeH/ModeHRuntimeModule.cs', 'private void RestoreForSlotChange()'),
        ('ModeH/ModeHRuntimeModule.cs', 'internal bool TryTransition('),
        ('ModeH/ModeHRuntimeModule_MatchFlow.cs', 'private ModeHLifecycle DeriveResumeFromSeasonProgress()'),
        ('ModeH/ModeHRuntimeModule_MatchFlow.cs', 'private ModeHLifecycle ResolveRecoveryResumeLifecycle()'),
        ('ModeH/ModeHRuntimeModule_MatchFlow.cs', 'private void DriveRecovery()'),
        ('ModeH/ModeHRuntimeModule_MatchFlow.cs', 'private void LockLoadoutAndStartMatch()'),
        ('ModeH/ModeHRuntimeModule_UiFlow.cs', 'private void ProjectRunStateIntoSeason()'),
        ('ModeH/ModeHRuntimeModule_UiFlow.cs', 'private bool TryPersistSeason(string reasonId)'),
        ('ModeH/ModeHRuntimeModule_UiFlow.cs', 'private bool TryPersistSeason(string reasonId, bool requireDurable)'),
        ('ModeH/ModeHRuntimeModule_SettlementFlow.cs', 'private void CompleteSettlementAndRoute()'),
        ('ModeH/ModeHRuntimeModule_SettlementFlow.cs', 'private void RouteAfterIntermission('),
        ('ModeH/ModeHRuntimeModule_CombatFlow.cs', 'private void EnterHallOfFame()'),
        ('ModeH/ModeHRuntimeModule_CombatFlow.cs', 'private void ResolveRestRecovery(string profileId)'),
        ('ModeH/ModeHRuntimeModule_CombatProfiles.cs', 'private ModeHMatchReportDto FindLatestPendingReport()'),
        ('ModeH/ModeHRuntimeModule_CombatProfiles.cs', 'private ModeHSeasonRewardOperationDto FindRewardOperation('),
        ('ModeH/ModeHRuntimeModule_CombatProfiles.cs', 'private ModeHProfileDto FindSeasonProfile('),
        ('ModeH/ModeHRuntimeModule_Recovery.cs', 'private bool TryPrepareSeasonResume('),
        ('ModeH/ModeHRuntimeModule_Recovery.cs', 'private bool TryHandleSeasonResumeScene('),
        ('ModeH/ModeHRuntimeModule_Recovery.cs', 'private bool IsSeasonResumeRequestCurrent('),
        ('ModeH/ModeHRuntimeModule_Recovery.cs', 'private void FailSeasonResume('),
        ('ModeH/ModeHRuntimeModule_Recovery.cs', 'private void CancelSeasonResume()'),
    ],
    'ModeHInventoryPersistenceBridge': [
        ('ModeH/ModeHInventoryPersistenceBridge.cs', 'public static int CountOccurrences(string semanticTreeDigest)'),
        ('ModeH/ModeHInventoryPersistenceBridge.cs', 'public static int CountOccurrences(ModeHItemTreeSnapshotDto expected)'),
        ('ModeH/ModeHInventoryPersistenceBridge.cs', 'private static int CountOccurrences(string semanticTreeDigest, bool includeRestoreData)'),
    ],
    'ModeHTransferMarket': [
        ('ModeH/ModeHTransferMarket.cs', 'public static List<string> GetLiveContractProfileIds('),
        ('ModeH/ModeHTransferMarket.cs', 'private static void AppendIfLive('),
        ('ModeH/ModeHTransferMarket.cs', 'private static ModeHProfileDto FindProfile('),
    ],
    'ModeHInjuryAndScarSystem': [('ModeH/ModeHInjuryAndScarSystem.cs', 'public bool ResolveRestRecovery(')],
    'ModeHCombatTelemetry': [('ModeH/ModeHCombatTelemetry.cs', 'public bool HasRested(')],
}
parts = ['using System; using System.Collections.Generic; using ItemStatsSystem; namespace BossRush {']
for name, methods in groups.items():
    modifier = 'static ' if name in ('ModeHInventoryPersistenceBridge', 'ModeHTransferMarket') else ''
    parts.append('internal ' + modifier + 'partial class ' + name + ' {')
    parts += [method(path, signature) for path, signature in methods]
    if name == 'ModeHRuntimeModule':
        settlement = method('ModeH/ModeHRuntimeModule_CombatFlow.cs', 'private void BeginMatchSettlement()')
        start = settlement.index('_restedProfileIds.Clear();')
        end = settlement.index('// 退役结算', start)
        parts.append('public void SettleRest() {' + settlement[start:end] + '}')
    parts.append('}')
parts.append('}')
(OUT / 'Extracted.cs').write_text('\n'.join(parts), encoding='utf-8')

stubs = (ROOT / 'tests/fixtures/ReviewSeptember/Stubs.cs').read_text(encoding='utf-8-sig')
for name in ('ModeHStakeJournalDto','ModeHSeasonDto','ModeHHallOfFameRecordDto','ModeHProductionCertificationDto'):
    stubs = stubs.replace('public class ' + name + ' { }', '')
stubs = re.sub(r'    public class ModeHItemTreeSnapshotDto\s*\{[^}]*\}', '', stubs)
stubs = re.sub(r'    public static class ModeHConfig \{[^}]*\}', '', stubs)
for name in ('ModeHRuntimeGates','ModeHWarehouseStakeJournal','ModeHProfilePersistence','ModeHHallOfFamePersistence'):
    stubs = stubs.replace('public static class ' + name, 'public static partial class ' + name)
stubs = stubs.replace('error = null; HasPendingWrite = true; return true; }',
                      'error = null; HasPendingWrite = true; return true; }')
(OUT / 'HostStubs.cs').write_text(stubs, encoding='utf-8')
(OUT / 'source-hashes.txt').write_text('\n'.join(' | '.join(row) for row in hashes), encoding='utf-8')
print('Mode H review regression: extracted', len(hashes), 'production methods/blocks')
