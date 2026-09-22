"""Mode H 场景 owner 必须等官方传送完成，Legacy 不得在等待后抢回位置。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(path, signature):
    text = clean_source((ROOT / path).read_text(encoding='utf-8-sig'))
    start = text.index(signature)
    opening = text.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[opening:end]


def main():
    scene = 'ModeH/ModeHRuntimeModule_SceneFlow.cs'
    callback = body(scene, 'partial void OnSceneLoadedInternal(')
    assert 'ScheduleSceneReadyWait(context, sceneId, frozenGeneration, false);' in callback
    assert 'BeginSeasonSetup(context.SceneName, sceneId);' not in callback, 'sceneLoaded 不得同步取得租约'
    ready = body(scene, 'private static bool IsModeHSceneReady(')
    for token in ('SceneLoader.IsSceneLoading', 'LevelManager.LevelInited', 'LevelManager.AfterInit',
                  'SceneManager.GetActiveScene().handle != scene.handle', 'core.IsLoading', '!player.Health.IsDead'):
        assert token in ready, '缺官方就绪判据: ' + token
    wait = body(scene, 'private IEnumerator WaitForModeHSceneReady(')
    assert 'if (ready && readyLastFrame)' in wait, '必须跨帧确认官方传送结束'
    assert wait.index('if (request != _sceneReadyRequestSerial) yield break;') < wait.rindex('_sceneReadyRoutine = null;')
    assert 'slotGeneration != ModeHRuntimeGates.SlotGeneration' in wait, '取消不得跨槽退款'
    assert 'CancelSceneReadyWait();' in body(scene, 'private void ReleaseRuntimeObjects()')
    recovery = 'ModeH/ModeHRuntimeModule_Recovery.cs'
    assert 'ScheduleSceneReadyWait(context, _map.SceneId, intentGeneration, true);' in body(recovery, 'private bool TryHandleSeasonResumeScene(')
    load = body(recovery, 'private async UniTask LoadSeasonResumeScene(')
    assert 'if (HasSceneReadyWait(intentGeneration)) return;' in load
    assert 'CancelSceneReadyWait();' in body(recovery, 'private void CancelSeasonResume()')
    assert 'BossRushMapSelectionHelper.GetPendingModeHSceneGeneration() == intentGeneration' in body(recovery, 'private bool IsSeasonResumeRequestCurrent(')
    legacy = body('Integration/BossRushIntegration_TravelAndSetup.cs', 'private System.Collections.IEnumerator TeleportPlayerToCustomPosition(')
    gates = list(re.finditer(r'if\s*\(ShouldSkipLegacySceneSetupForModeH\(\)\)\s*yield break;', legacy))
    assert len(gates) == 2 and gates[0].start() < legacy.index('while (elapsed < maxWait)')
    assert legacy.index('yield return sharedWait05s;') < gates[1].start() < legacy.index('main.SetPosition(finalPosition);')
    gate = body('ModeH/ModeHEntry.cs', 'private bool ShouldSkipLegacySceneSetupForModeH()')
    assert 'HasPendingModeHEntryIntent()' in gate and 'IsModeHRunInProgressSafe()' in gate
    integration = body('Integration/BossRushIntegration_StartAndScene.cs', 'private void OnSceneLoaded_Integration(')
    assert re.search(r'if\s*\(!ShouldSkipLegacySceneSetupForModeH\(\)\)\s*StartCoroutine\(TeleportPlayerToCustomPosition\(customPos.Value\)\);', integration)
    print('ModeHSceneEntryGuard: PASS (entry, resume, official readiness, cancellation, Legacy position ownership)')


if __name__ == '__main__':
    main()
