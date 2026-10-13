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
    setup = body(scene, 'private void BeginSeasonSetup(')
    assert 'if (!BossRushMapSelectionHelper.HasPendingPrepaidTicket())' in setup, '新赛季必须认实际付票回执'
    assert setup.index('HasPendingPrepaidTicket()') < setup.index('ModeHRuntimeGates.SetRunOwnerActive(true);'), '未付票不得取得 run owner'
    ready = body(scene, 'private static bool IsModeHSceneReady(')
    for token in ('SceneLoader.IsSceneLoading', 'LevelManager.LevelInited', 'LevelManager.AfterInit',
                  'SceneManager.GetActiveScene().handle != scene.handle', 'core.IsLoading', 'player.Health.IsDead',
                  'global::AstarPath.active.isScanning'):
        assert token in ready, '缺官方就绪判据: ' + token
    # 2026-10-12：第三方 OnAfterLevelInitialized 订阅者抛异常会让 afterInit 永远为 false；
    # 只允许用官方在最终落点之后写入的完成标记替代，标记顺序由 SkyIslandOfficialContract 夹具对真实 DLL 钉住。
    assert 'LevelManager.LevelInitializingComment' in ready and 'OfficialInitDoneComment' in ready, 'AfterInit 替代判据必须是官方完成标记'
    assert 'internal const string OfficialInitDoneComment = "Done!";' in clean_source((ROOT / scene).read_text(encoding='utf-8-sig'))
    wait_src = body(scene, 'private IEnumerator WaitForModeHSceneReady(')
    assert 'FailSceneReadyWait(resume, "scene_not_ready:" + notReady);' in wait_src, '超时必须带出卡住的具体条件'
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
    assert len(gates) == 2 and gates[0].start() < legacy.index('yield return bossRushIntegrationRuntime.WaitForCustomTeleportSceneReady();')
    assert legacy.index('yield return bossRushIntegrationRuntime.WaitForCustomTeleportSceneReady();') < gates[1].start()
    assert gates[1].start() < legacy.index('bossRushIntegrationRuntime.ApplyCustomTeleportPosition(')
    travel = body('Integration/BossRushIntegrationRuntimeModule_Travel.cs', 'internal IEnumerator WaitForCustomTeleportSceneReady(')
    for token in ('const float maxWait = 30f;', 'const float interval = 0.1f;',
                  'ReadMainExistsWithWarning("TeleportPlayerToCustomPosition")',
                  'ReadLevelInitedWithWarning("TeleportPlayerToCustomPosition")',
                  'yield return new WaitForSeconds(interval);',
                  'yield return _owner.IntegrationSharedWait05s;'):
        assert token in travel, 'custom teleport readiness wait changed: ' + token
    landing = body('Integration/BossRushIntegrationRuntimeModule_Travel.cs', 'internal Vector3 ApplyCustomTeleportPosition(')
    skip_landing = re.search(r'if\s*\(isModeEEntry\s*\|\|\s*BossRushInitialSpawn.HasArrived\(targetPosition\)\)\s*\{\s*return finalPosition;\s*\}', landing)
    assert skip_landing and skip_landing.start() < landing.index('Physics.RaycastAll('), 'Mode E and completed official arrival must preserve the spawn point'
    assert landing.index('Physics.RaycastAll(') < landing.index('Physics.Raycast(') < landing.index('main.SetPosition(finalPosition);')
    assert landing.index('cameraOffset = camera.transform.position - main.transform.position;') < landing.index('main.SetPosition(finalPosition);')
    assert 'camera.transform.position = main.transform.position + cameraOffset;' in landing
    gate = body('ModeH/ModeHEntry.cs', 'private bool ShouldSkipLegacySceneSetupForModeH()')
    assert 'HasPendingModeHEntryIntent()' in gate and 'IsModeHRunInProgressSafe()' in gate
    integration = body('Integration/BossRushIntegration_StartAndScene.cs', 'private void OnSceneLoaded_Integration(')
    assert re.search(r'if\s*\(!ShouldSkipLegacySceneSetupForModeH\(\)\)\s*StartCoroutine\(TeleportPlayerToCustomPosition\(customPos.Value\)\);', integration)
    print('ModeHSceneEntryGuard: PASS (entry, resume, official readiness, cancellation, Legacy position ownership)')


if __name__ == '__main__':
    main()
