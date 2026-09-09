"""独立天空岛仅扩展其场景引用和单场景 core；禁止全局 -1 身份别名。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
NEWLINE = chr(10)


def main():
    source = clean_source((ROOT / "DebugAndTools/SkyIsland/SkyIslandSceneReferenceBridge.cs").read_text(encoding="utf-8-sig"))
    errors = []
    for token in ('SceneId = "BossRush_SkyIsland"', 'ScenePath = "Assets/SkyIsland/SkyIslandRaid.unity"',
                  'SceneName = "SkyIslandRaid"', 'SpawnLocation = "StartPoints/PlayerSpawn"',
                  "SceneGuidToPathMapProvider.SceneGuidToPathMap", "SceneGuidToPathMapProvider.ScenePathToGuidMap",
                  "new SceneReference(SceneGuid)", "new SceneInfoEntry(SceneId, sceneReference)",
                  "OwnsReference(__instance)", "OwnsCore(__instance)", "IsActiveScene",
                  "SceneManager.GetActiveScene().handle != __instance.gameObject.scene.handle",
                  "provider.gameObject.scene.handle == scene.handle", "provider.GetLocation(name)",
                  'RequireField(typeof(MultiSceneCore), "OnSubSceneLoaded")', "loadedEventField.GetValue(null)",
                  "listener(core, scene)", "activeSubSceneField.SetValue(core, previous)",
                  "SceneLoader.onFinishedLoadingScene += OnSceneLoadFinished", "SceneLoader.onFinishedLoadingScene -= OnSceneLoadFinished",
                  "SceneManager.sceneUnloaded += OnSceneUnloaded", "SceneManager.sceneUnloaded -= OnSceneUnloaded",
                  "SceneLoader.IsSceneLoading || SceneManager.GetSceneByPath(ScenePath).isLoaded",
                  "harmony.UnpatchAll(HarmonyId)", "guidMap.Remove(SceneGuid)", "pathMap.Remove(ScenePath)"):
        if token not in source:
            errors.append("桥接缺少 " + token)
    for token in ("SceneInfoCollection.Entries.Add", 'PropertyGetter(typeof(SceneReference), "BuildIndex")',
                  'RequireMethod(typeof(SceneInfoCollection), "GetSceneInfo", typeof(int))',
                  "SceneManager.LoadSceneAsync(", "SceneManager.UnloadSceneAsync("):
        if token in source:
            errors.append("单场景桥接不得全局解释 -1 或再次装卸世界：" + token)
    # 官方小地图用 GetSceneID(GetActiveScene().buildIndex) 反查地图身份，bundle 场景恒为 -1，
    # 所以这一处 patch 是必要的；但它必须按「当前活动场景是天空岛」收窄，
    # 绝不能写成「buildIndex == -1 就返回天空岛」——那才是被禁的全局 -1 别名。
    if 'RequireMethod(typeof(SceneInfoCollection), "GetSceneID", typeof(int))' in source:
        if 'SceneIdByBuildIndexPrefix' not in source:
            errors.append('patch 了 GetSceneID(int) 却没有对应前缀')
        else:
            body = source.split('SceneIdByBuildIndexPrefix(int buildIndex', 1)[-1].split(NEWLINE + '        }', 1)[0]
            if 'IsActiveScene' not in body:
                errors.append('GetSceneID(int) 前缀必须按当前活动场景收窄，否则是全局 -1 别名')
            if 'buildIndex >= 0' not in body:
                errors.append('GetSceneID(int) 前缀必须拒绝正常 buildIndex')
    runtime = clean_source((ROOT / "DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs").read_text(encoding="utf-8-sig"))
    for token in ("SkyIslandSceneReferenceBridge.EnsureRegistered()", "SkyIslandSceneReferenceBridge.Shutdown()"):
        if token not in runtime:
            errors.append("缺少生命周期归属：" + token)
    # CR-2026-09-08-005：初始化失败信号既要有实现，也要有消费方；只有定义等于没有恢复流程。
    for token in ('RequireMethod(typeof(LevelManager), "NotifySaveBeforeLoadScene", typeof(bool)), "SaveBeforeLoadPrefix"',
                  '"LoaderInitializationTranspiler"', 'throw new OperationCanceledException(initializationFailure)',
                  'if (replaced != 1) throw'):
        if token not in source:
            errors.append("桥接缺少初始化失败信号：" + token)
    lease = clean_source((ROOT / "DebugAndTools/SkyIsland/SkyIslandRaidLease.cs").read_text(encoding="utf-8-sig"))
    session = clean_source((ROOT / "DebugAndTools/SkyIsland/SkyIslandSession.cs").read_text(encoding="utf-8-sig"))
    for token in ("SkyIslandSceneReferenceBridge.BeginInitialization(this)",
                  "SkyIslandSceneReferenceBridge.BindInitializationScene(this, scene)",
                  "SkyIslandSceneReferenceBridge.AbortInitialization(this, ",
                  "SkyIslandSceneReferenceBridge.EndInitialization(this)",
                  "internal void Abort(string reason)", "internal void MarkInitialized()"):
        if token not in lease:
            errors.append("租约未消费初始化令牌：" + token)
    if "MarkInitialized();" not in lease.split("private void TryRelease()", 1)[-1]:
        errors.append("释放路径必须归还初始化令牌，否则下次进岛会被旧令牌拦死")
    for token in ("lease.MarkInitialized()", "lease.Abort("):
        if token not in session:
            errors.append("会话未接初始化失败恢复：" + token)
    print("SkyIslandSceneReferenceBridgeGuard: " + ("FAIL\n" + "\n".join(errors) if errors else "PASS"))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
