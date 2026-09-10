"""天空岛独立出击生命周期、资源所有权与表现层接线；不代替 Unity 实机测试。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
FILES = {name: f'DebugAndTools/SkyIsland/SkyIsland{name}.cs'
         for name in ('Session', 'RaidLease', 'Rendering', 'Lighting', 'SearchPoint', 'Controls')}


def main():
    sources = {name: clean_source((ROOT / path).read_text(encoding='utf-8-sig')) for name, path in FILES.items()}
    required = {
        'Session': ['private readonly Vector3 origin = Vector3.zero', 'SceneRuntimeGate.IsBaseHubSceneName',
                    'lease = new SkyIslandRaidLease()', 'lease.Prepare(ModBehaviour.GetModPath(), timeOfDayTemplate)',
                    'lease.BeginLoad()', 'IInitializedQueryHandler',
                    'while ((SceneManager.GetActiveScene().handle != entryScene.handle || GameCamera.Instance == null ||',
                    'LevelManager.RegisterWaitForInitialization(this)', 'LevelManager.UnregisterWaitForInitialization(this)',
                    'while ((!lease.LoadFinished || !LevelManager.AfterInit)',
                    'SkyIslandOfficialContract.Verify(entryScene, SkyIslandSceneReferenceBridge.SceneId, out contractError)',
                    'player = CharacterMainControl.Main', 'moved = ready = true',
                    'rendering.Apply(root, groundLayer, wallLayer)', 'lighting.Apply(root)',
                    'navigation.BeginScan(filter.sharedMesh, origin)', 'probe.StartPath(',
                    'seeker.graphMask = navigation.Mask', 'clone.dropBoxOnDead = false', 'clone.exp = 0',
                    'clone.hasSoul = false', 'clone.setActiveByPlayerDistance = false', 'created.SetTeam(Teams.wolf)',
                    # 腾空救援与撤离读条都走游戏时间（2026-09-10 全方位审核）：暂停菜单 / 拍照模式把 timeScale 压到 0，
                    # unscaled 计时会在暂停时照走——站在圈里开暂停菜单 3 秒被送回基地、腾空时开暂停菜单被「救」回落脚点。
                    'this == null || closed', 'player.SetPosition(safePosition)', 'Time.time - airborneSince > 2.5f',
                    'searchCount++', 'searched.Add(key)', 'extractionHeld = -1', 'lease.Release(',
                    'lease.ReturnToBase(moved)', 'if (returnRequested) { DispatchReturnIfReady(); return; }',
                    'SkyIslandStorySaveRecovery.CloseOrRetain(story)', 'navigation.Dispose()', 'lighting.Dispose()'],
        'RaidLease': ['Assets/arenas/sky_island_raid', 'SkyIslandSceneReferenceBridge.EnsureRegistered()',
                      'scenes.Length != 1', 'SkyIslandSceneReferenceBridge.ScenePath, StringComparison.OrdinalIgnoreCase)',
                      'SceneLoader.Instance.LoadScene(SkyIslandSceneReferenceBridge.SceneId,',
                      'new MultiSceneLocation { SceneID = SkyIslandSceneReferenceBridge.SceneId, LocationName = "StartPoints/PlayerSpawn" }',
                      # 与官方出击一致：MapSelectionView.LoadTask 用 clickToConinue: true，
                      # 读条完停在「点击继续」，玩家点了才进图。
                      'clickToConinue: true, notifyEvacuation: false, saveToFile: true',
                      'SceneLoader.Instance.LoadScene(GameplayDataSettings.SceneManagement.BaseScene,',
                      'notifyEvacuation: evacuated, saveToFile: true', 'LoadFinished = true',
                      'if (releaseRequested) world.SetActive(true)', 'services.SetActive(true)',
                      'config.timeOfDayConfig = timeOfDay', 'config.startBuffPrefabs = new List<Duckov.Buffs.Buff>()',
                      # CR-2026-09-10-003：官方 TimeOfDayConfig 与 TimeOfDayEntry 都是场景 MonoBehaviour，
                      # 基地那份出图即销毁。必须克隆成常驻副本，并在唯一收口 TryRelease 里销毁。
                      'timeOfDay = UnityEngine.Object.Instantiate(template)',
                      'UnityEngine.Object.DontDestroyOnLoad(timeOfDay.gameObject)',
                      'if (timeOfDay != null) UnityEngine.Object.Destroy(timeOfDay.gameObject)',
                      'Time.unscaledTime < retryAt', 'retryAt = Time.unscaledTime + 2f',
                      'if (!releaseRequested || loading || returning) return',
                      'if (scene.IsValid() && scene.isLoaded) return', 'bundle.Unload(true)',
                      'Action callback = completed; completed = null', 'recovery.Bind(this)',
                      'main.Health.IsDead) return', 'lease.PumpRelease()'],
        'Rendering': ['BossRush/SkyIsland/Environment', 'BossRush/SkyIsland/Water', 'BossRush/SkyIsland/Cloud',
                      'source.GetTexture(sourceMap)', 'source.GetTextureScale(sourceMap)', 'source.GetTextureOffset(sourceMap)',
                      'material.HasProperty(targetMap)', 'COL_Ground', 'COL_Wall', 'COL_Rail'],
        'Lighting': ['previousEnabled = Shader.GetGlobalFloat(Enabled)', 'Shader.SetGlobalFloat(Enabled, previousEnabled)',
                     'Shader.SetGlobalVector(Direction, previousDirection)', 'Shader.SetGlobalVector(Sun, previousSun)',
                     'Shader.SetGlobalVector(Ambient, previousAmbient)', 'bool ownsShader = Shader.GetGlobalFloat(Owner) == ownerId',
                     'if (sameScene && ownsShader)', 'if (ownsShader)'],
        'SearchPoint': ['BossRushBuildingInteractableBase', 'return searched != null',
                        'if (searched != null) searched()', 'LocalizationHelper.InjectLocalization'],
        'Controls': ['SkyIslandSession.Enter(', 'session.Close(true,', 'session.SpawnEnemy()',
                     'session.VisitNextLandmark()', 'session.OpenMap()', 'BossRushUI.ApplyPanelSkin', 'BossRushUI.ApplyGameFont'],
    }
    errors = []
    for name, tokens in required.items():
        for token in tokens:
            if token not in sources[name]:
                errors.append(f'{FILES[name]}: 缺少 {token}')
    session = sources['Session']
    if session.index('while ((SceneManager.GetActiveScene().handle != entryScene.handle') > session.index('lighting.Apply(root)'):
        errors.append('必须等待官方活动场景与相机就绪后才创建天空岛光照')
    # 腾空救援同样走游戏时间（2026-09-10 全方位审核）：起点与比较必须是同一个时基。
    # 只钉比较那一句的话，把起点改回 unscaledTime 照样绿，而两种时基相减会让救援在暂停之后立刻触发或迟迟不触发。
    update = session.split('private void Update()', 1)[1].split('private bool HudSuppressed()', 1)[0]
    for line in update.splitlines():
        if 'airborneSince' in line and 'unscaled' in line.lower():
            errors.append('腾空计时不得混用 unscaled 时间：' + line.strip())
    if 'else if (airborneSince < 0) airborneSince = Time.time;' not in update:
        errors.append('腾空计时的起点必须是 Time.time（与救援判定同一时基）')
    rescue = session.split('private void Rescue()', 1)[1].split('\n        }', 1)[0]
    if 'extractionHeld = -1;' not in rescue:
        errors.append('坠落救援必须清零撤离读条：被拉回落脚点的人已经不在撤离圈里')
    for event, callback in [('SceneLoader.onStartedLoadingScene', 'OnStartedLoading'),
                            ('SceneManager.sceneLoaded', 'OnSceneLoaded'), ('SceneManager.sceneUnloaded', 'OnSceneUnloaded')]:
        for operator in ('+=', '-='):
            if f'{event} {operator} {callback}' not in session:
                errors.append(f'Session 事件未配对：{event} {operator} {callback}')
    for operation in ('AddListener(OnPlayerDied)', 'RemoveListener(OnPlayerDied)'):
        if operation not in session:
            errors.append('玩家死亡 listener 未配对：' + operation)
    for event in ('sceneLoaded', 'sceneUnloaded'):
        for operator in ('+=', '-='):
            callback = 'OnSceneLoaded' if event == 'sceneLoaded' else 'OnSceneUnloaded'
            if f'SceneManager.{event} {operator} {callback}' not in sources['RaidLease']:
                errors.append(f'RaidLease 事件未配对：{event} {operator} {callback}')
    if session.index('SkyIslandOfficialContract.Verify(') > session.index('moved = ready = true'):
        errors.append('独立关卡服务验证必须先于 ready')
    death = session.split('private void OnPlayerDied(', 1)[1].split('private void DestroyEnemy(', 1)[0]
    if 'deathPending = true; ready = false' not in death:
        errors.append('玩家死亡必须关闭玩法并交还官方死亡任务')
    for token in ('SetPosition(', 'Close(', 'Cleanup(', 'ReturnToBase(', 'lease.Release('):
        if token in death:
            errors.append('死亡回调不得抢先返航、移动尸体或卸载：' + token)
    close = session.split('internal void Close(', 1)[1].split('private void DispatchReturnIfReady(', 1)[0]
    if 'if (deathPending && entryScene.IsValid() && entryScene.isLoaded) return' not in close:
        errors.append('关闭旅程必须保留死亡中的场景供官方结算')
    for token in ('returnPosition', 'MoveGameObjectToScene(', 'DontDestroyOnLoad(player'):
        if token in session:
            errors.append('正式入口不得借用旧基地玩家/服务或资源预览租约：' + token)
    for token in ('SavesSystem.Save', 'PlayerPrefs.Set', 'SceneInfoCollection.Entries.Add', 'LoadSceneMode.Single', 'ClearGraphs', 'SetActiveScene('):
        if any(token in source for source in sources.values()):
            errors.append('生命周期/表现类不得越过官方加载或存档门面：' + token)
    for token in ('SceneManager.LoadScene', 'SceneManager.UnloadScene'):
        if token in sources['RaidLease'] or token in session:
            errors.append('独立出击加载/返航必须经过官方 SceneLoader：' + token)
    # 地图必须用官方那一套：场景包里带 MiniMapSettings，玩家按自己绑定的地图键开合
    # （CharacterInputControl.OnUIMapInput -> MiniMapView.Show）。Mod 侧只保留一个同样的入口，
    # 不再自绘任何地图界面，也不接管模态输入或时间缩放。
    if 'MiniMapView.Show()' not in session:
        errors.append('OpenMap 必须打开官方地图，而不是自绘界面')
    # 用 `new SkyIslandMap(` 而不是裸的 `SkyIslandMap`：后者会被 `SkyIslandMapFog` 误伤，
    # 而分区迷雾走的是官方图层数据，不是自绘界面。
    for token in ('new SkyIslandMap(', 'ClaimModalInput', 'Cursor.lockState', 'Time.timeScale ='):
        if token in session:
            errors.append('天空岛不得重建自绘地图或接管模态状态：' + token)
    if (ROOT / 'DebugAndTools/SkyIsland/SkyIslandMap.cs').exists():
        errors.append('自绘旅程图已废弃，不得重新引入')
    # F6 曾经是自绘地图的开合键；官方地图不需要 Mod 处理输入，残留按键说明没删干净。
    runtime_module = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs').read_text(encoding='utf-8-sig'))
    for name, text in (('Session', session), ('RuntimeModule', runtime_module)):
        if 'KeyCode.F6' in text:
            errors.append(name + ' 仍在处理 F6，官方地图由玩家自己的地图键开合')
    host = clean_source((ROOT / 'ModBehaviour.cs').read_text(encoding='utf-8-sig')).split('private void OnSceneLoaded(', 1)[1]
    if host.index('StoneOutpostSceneLease.IsResourceScene(scene)') > host.index('PrepareSceneRuntimeForLoad()'):
        errors.append('资源 Scene 必须在正式关卡清理前过滤')
    # 天空岛正式包是完整独立 Raid 关卡，必须走正常关卡状态重置；旧的资源预览租约已删除，不得复活。
    if 'SkyIslandSceneLease' in host or (ROOT / 'DebugAndTools/SkyIsland/SkyIslandSceneLease.cs').exists():
        errors.append('天空岛资源预览租约已废弃，不得重新引入')
    for path in ('DebugAndTools/ArenaPrototype/ArenaPrototypeSession.cs', 'DebugAndTools/F3GameplayValidationRunner.cs'):
        if 'GetComponent<SkyIslandSession>() != null' not in clean_source((ROOT / path).read_text(encoding='utf-8-sig')):
            errors.append('天空岛缺少双向互斥：' + path)
    f3 = clean_source((ROOT / 'DebugAndTools/F3DebugCheatMenu.cs').read_text(encoding='utf-8-sig'))
    show = f3.split('private void ShowF3DebugCheatMenu()', 1)[1]
    if show.index('SkyIslandSession.HideMapBeforeF3(this)') > show.index('CaptureF3DebugCheatPresentationState()'):
        errors.append('重新打开 F3 前必须释放天空岛简图租约，避免把暂停状态记作恢复目标')
    compile_list = (ROOT / 'compile_official.bat').read_text(encoding='utf-8-sig')
    for path in FILES.values():
        if path.replace('/', '\\') not in compile_list:
            errors.append('编译清单缺少：' + path)
    if 'copy /Y "Assets\\arenas\\sky_island_raid"' not in compile_list:
        errors.append('缺少天空岛独立出击场景部署段')
    print('SkyIslandLifecycleGuard: ' + ('FAIL\n' + '\n'.join(errors) if errors else 'PASS'))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
