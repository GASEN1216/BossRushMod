#!/usr/bin/env python3
"""Mode H recovery actions bind persisted identity and release owners before clearing gates."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILES = ('ModeHRuntimeModule_SettlementFlow.cs', 'ModeHRuntimeModule_UiFlow.cs',
         'ModeHRuntimeModule_SceneFlow.cs', 'ModeHRuntimeModule_Recovery.cs', 'ModeHRunState.cs')


def body(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth, pos = 1, opening + 1
    while depth:
        depth += (source[pos] == '{') - (source[pos] == '}')
        pos += 1
    return source[opening:pos]


def check(sources):
    errors = []
    settlement, ui, scene, recovery, run = [sources[name] for name in FILES]
    def need(text, token, message):
        if token not in text: errors.append(message + ': missing ' + token)
    try:
        for signature in ('private ModeHPageContent BuildCompletedSettlementPageContent()',
                          'private void CompleteSettlementAndRoute()'):
            code = body(settlement, signature)
            for token in ('FindLatestPendingReport()', 'FindRewardOperation('):
                need(code, token, 'SettlementFlow ' + signature)
            if '_lastRewardOperation' in code or '_lastSettlementReport' in code:
                errors.append('SettlementFlow: current persistent report/operation must not depend on last-match caches')
        action = body(settlement, 'private bool IsSettlementActionCurrent(')
        for token in ('_commandsClosed', '_runState.IsOwnerTokenValid(ownerToken)',
                      '_runState.MatchIndex != matchIndex', 'ModeHLifecycle.Intermission',
                      'report.seasonRewardOperationId, operationId, StringComparison.Ordinal'):
            need(action, token, 'SettlementFlow: stale action fence')
        selection = body(settlement, 'private void SelectSettlementReward(')
        need(selection, 'IsSettlementActionCurrent(ownerToken, matchIndex, operationId)', 'reward action must validate captured owner')
        need(selection, 'FindRewardOperation(operationId)', 'reward action must resolve persisted operation')
        page = body(settlement, 'private ModeHPageContent BuildCompletedSettlementPageContent()')
        for token in ('SelectSettlementReward(selectedKitId, false, ownerToken, matchIndex, operationId)',
                      'SelectSettlementReward(null, true, ownerToken, matchIndex, operationId)',
                      'IsSettlementActionCurrent(ownerToken, matchIndex, operationId)'):
            need(page, token, 'SettlementFlow: every reward/confirm callback captures identity')
        abandon = body(ui, 'private void AbandonSeasonFromRecovery()')
        order = ('RequestSeasonWrite(persisted, out writeError, true)', '_commandsClosed = true;',
                 'CancelSeasonResume();', 'ReleaseRuntimeObjects();', '_runState = null;',
                 'SetRunOwnerActive(false)', 'SetRecoveryOnlyBlocked(false, null)')
        locations = [abandon.find(token) for token in order]
        if any(pos < 0 for pos in locations) or locations != sorted(locations):
            errors.append('UiFlow: durable abandon must stop commands, release while owner exists, then clear gates')
        if 'return;' not in abandon[locations[0]:locations[1]]:
            errors.append('UiFlow: failed durable write must return before releasing runtime')
        release = body(scene, 'private void ReleaseRuntimeObjects()')
        for token in ('try { ReleaseMatchRuntime(); }', 'LogFailure("release_match", e)',
                      '_spectatorLease.Release(_sceneGeneration)', '_arenaLease.Release(_sceneGeneration)',
                      'try { DestroyUi(); }'):
            need(release, token, 'SceneFlow: cleanup must continue across stage failures')
        identity = body(scene, 'private static string ComposeRunId(')
        need(identity, 'Guid.NewGuid().ToString("N")', 'SceneFlow: new run identity must survive process counter reset')
        if scene.count('ComposeRunId(sceneName, _sceneGeneration)') != 1 or 'ComposeRunId(' in recovery:
            errors.append('run identity may only be allocated once in new-season setup, never in recovery')
        restore = body(run, 'public static ModeHRunState FromDto(')
        need(restore, 'new ModeHRunState(dto.runId, dto.runSeed, dto.sceneName, dto.sceneGeneration)',
             'RunState: restoring old/new season must preserve saved identity and seed')
    except (ValueError, IndexError) as error:
        errors.append('required production method missing: ' + str(error))
    return errors


def main():
    sources = {name: (ROOT / 'ModeH' / name).read_text(encoding='utf-8-sig') for name in FILES}
    errors = check(sources)
    mutations = [
        (FILES[0], 'ModeHMatchReportDto report = _runState != null ? FindLatestPendingReport() : null;',
         'ModeHMatchReportDto report = _lastSettlementReport;'),
        (FILES[0], '_runState.IsOwnerTokenValid(ownerToken)', 'true'),
        (FILES[0], '_runState.MatchIndex != matchIndex', 'false'),
        (FILES[0], 'SelectSettlementReward(null, true, ownerToken, matchIndex, operationId)', 'SelectSettlementReward(null, true)'),
        (FILES[1], 'ReleaseRuntimeObjects();', ''),
        (FILES[1], 'RequestSeasonWrite(persisted, out writeError, true)', 'RequestSeasonWrite(persisted, out writeError, false)'),
        (FILES[2], 'try { ReleaseMatchRuntime(); }', 'ReleaseMatchRuntime();'),
        (FILES[2], 'Guid.NewGuid().ToString("N")', 'sceneGeneration.ToString("x")'),
        (FILES[4], 'new ModeHRunState(dto.runId, dto.runSeed, dto.sceneName, dto.sceneGeneration)',
         'new ModeHRunState("new", 0, dto.sceneName, dto.sceneGeneration)'),
    ]
    for name, before, after in mutations:
        if before not in sources[name]:
            errors.append('mutation target missing: ' + name + ' / ' + before); continue
        changed = dict(sources); changed[name] = changed[name].replace(before, after, 1)
        if not check(changed): errors.append('regression escaped guard: ' + name + ' / ' + before)
    if errors:
        print('\n'.join(errors)); return 1
    print('ModeHRecoverySecondReviewGuard: PASS (persisted reward identity, teardown ordering, cross-process ID; 9 rejected mutations)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
