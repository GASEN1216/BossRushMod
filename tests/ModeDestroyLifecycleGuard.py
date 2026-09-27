"""Mode cleanup is owned, non-settling on destruction, and closes real async gates."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def body(path, marker):
    source = clean_source((ROOT / path).read_text(encoding='utf-8-sig'))
    start = source.index(marker); opening = source.index('{', start); depth = 0
    for index in range(opening, len(source)):
        depth += (source[index] == '{') - (source[index] == '}')
        if not depth:
            return re.sub(r'\s+', ' ', source[opening:index + 1])
    raise ValueError(marker)

def require(text, token, message):
    assert token in text, message

def ordered(text, tokens, message):
    last = -1
    for token in tokens:
        index = text.find(token, last + 1)
        assert index > last, message + ': ' + token
        last = index

def main():
    d = body('ModeD/ModeDRuntimeModule.cs', 'public override void OnDestroy()')
    ordered(d, ['generation++;', 'CleanupModeDRuntimeOnDestroy();', 'owner = null;'], 'D must clean owned run before dropping its host')
    cleanup_d = body('ModeD/ModeDRuntimeModule_Lifecycle.cs', 'private void CleanupModeDRuntimeOnDestroy()')
    require(cleanup_d, 'cleanupHostState && !object.ReferenceEquals(owner, null)', 'D dormant owner must not clear another mode host state')
    for mode, lifecycle, entry, exit_path, exit_marker, exit_call, host in [
        ('E', 'ModeE/ModeERuntimeModule.cs', 'ModeE/ModeEStartup.cs', 'ModeE/ModeELifecycle.cs', 'public void EndModeE(', 'EndModeE(false);', 'modeEHost'),
        ('F', 'ModeF/ModeFRuntimeModule.cs', 'ModeF/ModeFEntry.cs', 'ModeF/ModeFPhases.cs', 'internal void ExitModeF(', 'ExitModeF(false);', 'owner'),
    ]:
        destroy = body(lifecycle, 'public override void OnDestroy()')
        ordered(destroy, ['if (mode'+mode+'RuntimeDestroyed) return;', 'mode'+mode+'RuntimeDestroyed = true;', 'InvalidateMode'+mode+'Session();', exit_call, host+' = null;'], mode + ' destroy must invalidate, clean and release exactly once')
        require(body(entry, 'private int BeginMode'+mode+'Session()'), 'mode'+mode+'CleanupPending = true;', mode+' session must own cleanup debt')
        ended = body(exit_path, exit_marker)
        require(ended, 'if (!mode'+mode+'Active && !mode'+mode+'CleanupPending) return;', mode+' dormant/already-ended runtime must not repeat shared cleanup')
        require(ended, 'mode'+mode+'CleanupPending = false;', mode+' completed cleanup must release its debt')
        require(body(entry, 'internal bool IsMode'+mode+'SessionStillValid('), 'mode'+mode+'RuntimeDestroyed || '+host+' == null || sessionToken <= 0', mode+' real gate must fail closed on released owner')
    shared = body('ModeE/ModeEStartup.cs', 'internal bool IsModeEOrModeFSpawnSessionStillValid(')
    require(shared, 'if (modeERuntimeDestroyed || modeEHost == null) return false;', 'shared E/F gate must reject owner disposal before F host dereference')
    require(body('ModeE/ModeERuntimeModule.cs', 'public override void OnDestroy()'), 'StopModeEStartupWarmupIfPending();', 'E must cancel pre-entry warmup even if no session began')
    warmup = body('ModeE/ModeEStartup.cs', 'internal void StopModeEStartupWarmupIfPending()')
    require(warmup, 'if (modeEHost != null) modeEHost.StopCoroutine(modeEStartupWarmupCoroutine);', 'E warmup stop must tolerate native-destroyed host')
    require(warmup, 'if (modeEHost != null) modeEHost.StopCoroutine(modeEMerchantWarmupCoroutine);', 'E warmup owner must also cancel separately scheduled merchant child')
    prepare = body('ModeE/ModeEStartup.cs', 'private System.Collections.IEnumerator PrepareModeEStartupCoroutine(')
    ordered(prepare, ['modeEMerchantWarmupCoroutine = modeEHost.StartCoroutine(WarmModeEMerchantCachesAsync());', 'yield return modeEMerchantWarmupCoroutine;', 'modeEMerchantWarmupCoroutine = null;'], 'E merchant child must retain original wait and release its handle')
    exit_f = body('ModeF/ModeFPhases.cs', 'internal void ExitModeF(')
    require(exit_f, 'if (!modeFRuntimeDestroyed) { FlushModeFPendingUtilityRewards(true); } else { modeFPendingUtilityRewardCounts.Clear(); modeFPendingUtilityRewardTypeScratch.Clear(); }', 'destroy must discard pending award delivery, normal exit must preserve it')
    require(exit_f, 'CancelFortPlacement();', 'destroy must compensate consumed placement before clearing state')
    retire = body('ModeF/ModeFPhases.cs', 'private void RetireModeFActiveBossAtExit(')
    require(retire, 'if (modeFRuntimeDestroyed) { UnityEngine.Object.Destroy(bossObject); } else { modeFDeferredExitBossObjects[bossObject] = null; modeFDeferredExitBossObjects[bossObject] = owner.StartCoroutine(DestroyModeFExitBossDeferred(bossObject)); }', 'dying host cannot own deferred Boss destruction')
    require(body('ModeF/ModeFRuntimeModule.cs', 'public override void OnDestroy()'), 'CleanupModeFDeferredExitBossObjects();', 'F destroy must reclaim pending normal-exit objects independently of active run')
    deferred = body('ModeF/ModeFPhases.cs', 'private void CleanupModeFDeferredExitBossObjects()')
    ordered(deferred, ['owner.StopCoroutine(pending.Value);', 'UnityEngine.Object.Destroy(bossObject);', 'modeFDeferredExitBossObjects.Clear();'], 'F deferred cleanup must stop each handle before destroying its object and releasing records')
    refund = body('ModeF/ModeFFortifications_RepairRewardsCleanup.cs', 'internal void RefundModeFUtilityItem(')
    ordered(refund, ['TryGiveItemToPlayerOrDrop(typeId, displayName, false);', 'if (modeFRuntimeDestroyed) return;', 'owner.ShowMessage('], 'destroy refunds existing consumption without reopening UI')
    merchant = body('Utilities/ModeEFMerchantRuntime.cs', 'internal async UniTaskVoid SpawnModeEMerchant(')
    ordered(merchant, ['catch (Exception validityException)', 'return false;', 'if (!isRequestCurrent()) return;', 'await merchantPreset.CreateCharacterAsync('], 'merchant request gate must be exception safe and checked before factory start')
    assert 'if (!policy.IsSpawnSessionValid(' not in merchant, 'merchant continuation must use the same safe gate in normal and catch paths'
    print('ModeDestroyLifecycleGuard: PASS')

if __name__ == '__main__':
    try:
        main()
    except (AssertionError, ValueError) as error:
        raise SystemExit('ModeDestroyLifecycleGuard: FAIL - ' + str(error))
