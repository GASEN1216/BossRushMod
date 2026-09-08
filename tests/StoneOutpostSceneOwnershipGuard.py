"""独立前哨场景的延迟加载、卸载和光照必须由同一个会话回收。"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    'lease': 'DebugAndTools/ArenaPrototype/StoneOutpostSceneLease.cs',
    'session': 'DebugAndTools/ArenaPrototype/ArenaPrototypeSession.cs',
    'lighting': 'DebugAndTools/ArenaPrototype/ArenaPrototypeLighting.cs',
    'search': 'DebugAndTools/ArenaPrototype/StoneOutpostSearchPoint.cs',
    'host': 'ModBehaviour.cs',
}


def main():
    text = {key: (ROOT / value).read_text(encoding='utf-8-sig') for key, value in FILES.items()}
    required = {
        'lease': ['bundle.GetAllScenePaths()', 'LoadSceneMode.Additive', 'operation.completed -= OnLoaded',
                  'if (released) { Unload(); return; }', 'SceneManager.UnloadSceneAsync(scene)',
                  'operation.completed -= OnUnloaded', 'bundle.Unload(true)', 'if (loading == null) Unload()'],
        'session': ['SceneRuntimeGate.IsBaseHubSceneName', 'yield return sceneLease.BeginLoad(modDir)',
                    'while (!sceneLease.LoadResolved', 'sceneLease.LoadError != null',
                    'lighting.Apply(arena)', 'lighting.Dispose()', 'sceneLease.Release(',
                    'searchedPoints++', 'extractionStarted = -1', '"outpost_extract"'],
        'lighting': ['sunEnabled = sun.enabled', 'sun.enabled = sunEnabled', 'profile.Add<LightControl>(false)',
                     'volumeObject.SetActive(false)', 'Destroy(profile)', 'Destroy(component)',
                     'renderer.SetPropertyBlock(block)', 'RenderSettings.ambientSkyColor = sky'],
        'search': ['BossRushBuildingInteractableBase', 'if (collected) return', 'collected = true'],
        'host': ['if (StoneOutpostSceneLease.IsResourceScene(scene)) return;'],
    }
    errors = []
    for key, tokens in required.items():
        for token in tokens:
            if token not in text[key]:
                errors.append(f'{FILES[key]}: 缺少 {token}')
    close = text['session'].split('internal void Close(', 1)[1]
    if close.index('player.SetPosition(returnPosition)') > close.index('sceneLease.Release('):
        errors.append('必须在卸载前哨前返还玩家')
    host_load = text['host'].split('private void OnSceneLoaded(', 1)[1]
    if host_load.index('StoneOutpostSceneLease.IsResourceScene(scene)') > host_load.index('PrepareSceneRuntimeForLoad()'):
        errors.append('资源场景不得触发正式关卡清理')
    for forbidden in ('SceneInfoCollection.Entries.Add', 'SaveFile(', 'SavesSystem.Save', 'PlayerPrefs.Set', 'LoadSceneMode.Single'):
        if any(forbidden in value for key, value in text.items() if key != 'host'):
            errors.append('实验场景不得使用 ' + forbidden)
    compile_list = (ROOT / 'compile_official.bat').read_text(encoding='utf-8-sig')
    for key in ('lease', 'lighting', 'search'):
        if FILES[key].replace('/', '\\') not in compile_list:
            errors.append('编译清单缺少 ' + FILES[key])
    print('StoneOutpostSceneOwnershipGuard: ' + ('FAIL\n' + '\n'.join(errors) if errors else 'PASS'))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
