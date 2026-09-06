"""CR-2026-09-06-007/008: 重试资产屏障与战痕持久处置接线；执行回归见同名 fixture。"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILES = ('ModeHRealStakeService.cs', 'ModeHRuntimeModule_MatchFlow.cs',
         'ModeHRuntimeModule_CombatFlow.cs', 'ModeHRuntimeModule_SettlementFlow.cs',
         'ModeHRuntimeModule.cs')


def body(source, signature):
    start = source.index(signature)
    pos = source.index('{', start) + 1
    depth, opening = 1, pos
    while depth:
        depth += (source[pos] == '{') - (source[pos] == '}')
        pos += 1
    return source[opening:pos - 1]


def check(sources):
    errors = []
    service, match, combat, settlement, runtime = [sources[name] for name in FILES]

    def need(code, token, why):
        if token not in code:
            errors.append(why + ': missing ' + token)

    try:
        lock = body(service, 'internal static bool TryLockForMatch(')
        if lock.index('!ModeHWarehouseStakeJournal.IsSlotConsistent') > lock.index('_selectedPositions.Count == 0'):
            errors.append('empty selection must not bypass historical journal consistency')
        barrier = body(service, 'internal static bool TryPrepareTechnicalRetry(')
        for token in ('journal.runId, runId, StringComparison.Ordinal', 'journal.matchIndex == matchIndex',
                      'sameMatch && journal.settlementKind == (int)ModeHSettlementKind.MatchResult',
                      'if (!sameMatch)', 'if (!TryAbortReturn(runSeed, matchIndex, out failureReasonId)) return false;',
                      'phase != ModeHStakePhase.CancelledTerminal', 'phase != ModeHStakePhase.RefundedTerminal',
                      '!ModeHWarehouseStakeJournal.IsSlotConsistent'):
            need(barrier, token, 'asset retry barrier')
        for signature in ('private void RequestTechnicalRetry(', 'private void DriveRecovery()'):
            flow = body(match, signature)
            need(flow, 'TryPrepareTechnicalRetry(', 'all retry entry paths require asset barrier')
            need(flow, 'RequestSuspended(assetFailure ?? "retry_assets_unresolved", false);', 'failed barrier suspends without a second implicit return')
            if flow.index('TryPrepareTechnicalRetry(') > flow.index('RestoreMatchReservationAndSnapshot();'):
                errors.append('asset barrier must precede clearing reservation/snapshot: ' + signature)
        retry = body(match, 'private void RequestTechnicalRetry(')
        need(retry, 'if (FindLatestPendingReport() == null)', 'complete report must resume settlement without rollback')
        need(body(match, 'private void DriveRecovery()'), '_resumeNeedsMatchReset || (_season != null && _season.preMatchSnapshot != null)',
             'same-session resume must clear preserved reservations without requiring scene reload')
        abort = body(match, 'private void AbortMatchSpawning(')
        need(abort, 'RequestTechnicalRetry(', 'initial spawn abort shares retry barrier')
        if 'RestoreMatchReservationAndSnapshot' in abort or 'TryReturnRealStakeOnAbort' in abort:
            errors.append('initial spawn failure must not bypass central strict barrier')
        need(body(runtime, 'internal void RequestSuspended('), 'if (attemptStakeReturn)', 'barrier failure controls second return')
        need(body(combat, 'private bool PrepareLockedMatch('), 'ModeHRealStakeService.HasLockedStakeForMatch(',
             'old terminal journal is not a current real stake')
        need(body(match, 'private void StartMatchSpawning()'), '_season.currentLoadoutLock.realStakeSelected',
             'stake branch must use current lock')

        begin = body(combat, 'private void BeginMatchSettlement()')
        if not (begin.index('report.seasonRewardOperationId = operation.operationId;')
                < begin.index('RecordScarOffer(report);') < begin.index('TryPersistSeason("match_settling", true)')):
            errors.append('offered receipt must share the report/reward atomic payload')
        if '_pendingScarProfileId' in settlement:
            errors.append('scar ownership/resolution must not rely on a runtime-only cache')
        for token in ('"scar_resolved|"', '"scar_offered|"'):
            need(body(settlement, 'private static string ScarOfferToken('), token, 'distinct durable offer/resolution identities')
        owner = body(settlement, 'private bool TryGetScarOfferProfile(')
        for token in ('FindRewardOperation(report.seasonRewardOperationId)', 'operation.matchIndex != report.matchIndex',
                      'operation.resultToken, report.resultToken, StringComparison.Ordinal',
                      'FindSeasonProfile(operation.rewardProfileId)'):
            need(owner, token, 'durable scar owner')
        pending = body(settlement, 'private bool IsScarOfferPending(')
        for token in ('HasEventToken(ScarOfferToken(report, false))', '!_runState.HasEventToken(ScarOfferToken(report, true))'):
            need(pending, token, 'only unresolved durable offers can grant')
        resolve = body(settlement, 'private void ResolveScarOffer(')
        for token in ('_commandsClosed', '!_runState.IsOwnerTokenValid(ownerToken)', '_runState.MatchIndex != pageMatchIndex',
                      '_runState.Lifecycle != ModeHLifecycle.Intermission', 'candidate.seasonRewardOperationId, operationId, StringComparison.Ordinal',
                      'candidate.scarOfferId, scarId, StringComparison.Ordinal', '!IsScarOfferPending(report)',
                      'CloneProfile(profile)', 'TryApplyEventToken(ScarOfferToken(report, true))',
                      'ReplaceSeasonProfile(updated)', 'TryPersistSeason("scar_resolved", true)',
                      'RequestSuspended("scar_resolution_persist_failed")'):
            need(resolve, token, 'scar action fence/atomic effect receipt')
        page = body(settlement, 'private void BuildSettlementScarActions(')
        for token in ('_season.matchReports.Count', 'AppendScarOfferActions(page, candidate, profile);'):
            need(page, token, 'prior deferred reports remain reachable')
        need(body(settlement, 'private void RouteAfterIntermission('), '&& HasPendingScarOffers()',
             'final season cannot erase unresolved durable offers')
    except (ValueError, IndexError) as error:
        errors.append('production method/ordering anchor missing: ' + str(error))
    return errors


def main():
    sources = {name: (ROOT / 'ModeH' / name).read_text(encoding='utf-8-sig') for name in FILES}
    errors = check(sources)
    mutations = [
        (FILES[0], 'if (!ModeHWarehouseStakeJournal.IsSlotConsistent)', 'if (false)'),
        (FILES[0], 'phase != ModeHStakePhase.RefundedTerminal', 'false'),
        (FILES[0], 'if (!sameMatch)', 'if (false)'),
        (FILES[1], 'RequestSuspended(assetFailure ?? "retry_assets_unresolved", false);', 'RequestRecovering(assetFailure);'),
        (FILES[1], 'if (FindLatestPendingReport() == null)', 'if (true)'),
        (FILES[1], '_resumeNeedsMatchReset || (_season != null && _season.preMatchSnapshot != null)', '_resumeNeedsMatchReset'),
        (FILES[2], 'RecordScarOffer(report);', ''),
        (FILES[3], 'operation.matchIndex != report.matchIndex', 'false'),
        (FILES[3], '!_runState.HasEventToken(ScarOfferToken(report, true))', 'true'),
        (FILES[3], '!_runState.IsOwnerTokenValid(ownerToken)', 'false'),
        (FILES[3], '_runState.MatchIndex != pageMatchIndex', 'false'),
        (FILES[3], 'TryPersistSeason("scar_resolved", true)', 'TryPersistSeason("scar_resolved")'),
        (FILES[3], '&& HasPendingScarOffers()', '&& false'),
    ]
    for name, before, after in mutations:
        if before not in sources[name]:
            errors.append('mutation target missing: ' + name + ' / ' + before)
            continue
        changed = dict(sources)
        # Replace all matching guards: the same invariant occurs in both retry entry paths.
        changed[name] = changed[name].replace(before, after)
        if not check(changed): errors.append('regression escaped: ' + name + ' / ' + before)
    if errors:
        print('\n'.join(errors))
        return 1
    print('ModeHThirdReviewFixesGuard: PASS (asset retry barriers and durable scar ownership; 13 rejected mutations)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
