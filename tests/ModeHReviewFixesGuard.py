#!/usr/bin/env python3
"""Mode H 2026-09-05: durable progression, legacy matching, explicit session recovery."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
FILES = [
    'ModeHRuntimeModule.cs', 'ModeHRuntimeModule_Recovery.cs', 'ModeHRuntimeModule_UiFlow.cs',
    'ModeHRuntimeModule_MatchFlow.cs', 'ModeHRuntimeModule_SceneFlow.cs',
    'ModeHRuntimeModule_CombatFlow.cs', 'ModeHRuntimeModule_SettlementFlow.cs',
    'ModeHSaveFlushCoordinator.cs', 'ModeHInventoryPersistenceBridge.cs', 'ModeHWarehouseStakeJournal.cs',
]

def check(source):
    errors = []
    def need(file, pattern, message):
        if not re.search(pattern, source[file], re.S):
            errors.append(file + ': ' + message)
    for file, reasons in (
        ('ModeHRuntimeModule_MatchFlow.cs', ('loadout_locked',)),
        ('ModeHRuntimeModule_CombatFlow.cs', ('match_settling','hall_command_pending','hall_command_completed')),
        ('ModeHRuntimeModule_SettlementFlow.cs', ('intermission_archive',)),
    ):
        for reason in reasons:
            need(file, r'TryPersistSeason\("' + reason + r'",\s*true\)', reason + ' must be durable before progression')
    need('ModeHRuntimeModule_CombatFlow.cs', r'RequestHallOfFameInsert\(record,\s*out error,\s*true\)', 'hall insert must be durable')
    need('ModeHRuntimeModule_SceneFlow.cs', r'RequestSeasonWrite\(_season,\s*out error,\s*true\)', 'initial season must survive prior certification write')
    need('ModeHSaveFlushCoordinator.cs', r'RequestSeasonWrite\(ModeHSeasonDto season, out string error, bool requireDurable\).*?FlushBatch\(out error, requireDurable\)', 'explicit durability must reach physical writer')
    need('ModeHRuntimeModule.cs', r'_season = season;\s*_restoredSeasonPending = true;', 'restore full season but pause gameplay')
    need('ModeHRuntimeModule_MatchFlow.cs', r'if \(_restoredSeasonPending \|\| _resumeScenePending\) return;', 'loaded combat must not tick before resume')
    need('ModeHRuntimeModule.cs', r'private void RestoreForSlotChange\(\).*?CancelSeasonResume\(\).*?ReleaseRuntimeObjects\(\).*?_season = null;', 'slot changes must discard old owner and season')
    need('ModeHRuntimeModule_UiFlow.cs', r'Suspended \|\| _restoredSeasonPending', 'saved active lifecycle needs a resume action')
    need('ModeHRuntimeModule_SceneFlow.cs', r'if \(TryHandleSeasonResumeScene\(context\)\) return;', 'resume scene must bypass new-season creation')
    for field in ('gameBuildSignature','modBuildSignature','contentCatalogSignature'):
        need('ModeHRuntimeModule_Recovery.cs', r'string.Equals\(_season\.' + field + r'\b', 'resume must validate ' + field)
    for token in ('_runState.OwnerToken == ownerToken', 'ModeHRuntimeGates.SlotGeneration == slotGeneration',
                  '_resumeSceneIntentGeneration == intentGeneration', '_arenaLease.TryAcquire(', '_spectatorLease.TryAcquire('):
        need('ModeHRuntimeModule_Recovery.cs', re.escape(token), 'resume ownership/lease missing: ' + token)
    need('ModeHInventoryPersistenceBridge.cs', r'CountOccurrences\(ModeHItemTreeSnapshotDto expected\).*?HasRestoreData\(expected\)', 'legacy occurrence needs expected payload format')
    need('ModeHInventoryPersistenceBridge.cs', r'TryCapture\(item, i, 1, out reason, includeRestoreData\)', 'capture must honor selected format')
    if re.search(r'CountOccurrences\(\s*(?:snapshot|expected|_active\.escrowItems\[i\])\.semanticTreeDigest\)', source['ModeHWarehouseStakeJournal.cs']):
        errors.append('journal must pass complete expected snapshot instead of digest alone')
    return errors

def main():
    source = {name: (ROOT / 'ModeH' / name).read_text(encoding='utf-8-sig') for name in FILES}
    errors = check(source)
    mutations = [
        ('ModeHRuntimeModule_MatchFlow.cs', 'TryPersistSeason("loadout_locked", true)', 'TryPersistSeason("loadout_locked")'),
        ('ModeHRuntimeModule_CombatFlow.cs', 'RequestHallOfFameInsert(record, out error, true)', 'RequestHallOfFameInsert(record, out error)'),
        ('ModeHRuntimeModule.cs', '_season = season;', '_season = null;'),
        ('ModeHRuntimeModule_MatchFlow.cs', 'if (_restoredSeasonPending || _resumeScenePending) return;', ''),
        ('ModeHRuntimeModule_Recovery.cs', '_runState.OwnerToken == ownerToken', 'true'),
        ('ModeHInventoryPersistenceBridge.cs', 'HasRestoreData(expected)', 'HasRestoreData(null)'),
    ]
    for file, before, after in mutations:
        if before not in source[file]:
            errors.append('mutation target missing: ' + file + ' / ' + before)
            continue
        mutated = dict(source); mutated[file] = mutated[file].replace(before, after, 1)
        if not check(mutated): errors.append('regression mutation escaped: ' + file + ' / ' + before)
    if errors:
        print('\n'.join(errors)); return 1
    print('ModeHReviewFixesGuard: PASS (durable/legacy/recovery invariants; 6 rejected regression mutations)')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
