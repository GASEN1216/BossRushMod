"""Mod 独立地图（天空岛、潮闸镇）只扩展各自的场景引用和单场景 core；补丁全仓一份；禁止全局 -1 身份别名。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
NEWLINE = chr(10)


def main():
    # 2026-11-05：补丁与 GUID 表抽到多图共用的 ModRaidSceneBridge（潮闸镇是第二张图），天空岛文件只剩身份门面。
    facade = clean_source((ROOT / "SkyIsland/SkyIslandSceneReferenceBridge.cs").read_text(encoding="utf-8-sig"))
    source = clean_source((ROOT / "Utilities/RaidScenes/ModRaidSceneBridge.cs").read_text(encoding="utf-8-sig"))
    errors = []
    for token in ('SceneId = "BossRush_SkyIsland"', 'ScenePath = "Assets/SkyIsland/SkyIslandRaid.unity"',
                  'SceneName = "SkyIslandRaid"', 'SpawnLocation = "StartPoints/PlayerSpawn"',
                  "ModRaidSceneBridge.Register(Identity)", "ModRaidSceneBridge.Unregister(Identity)",
                  "ModRaidSceneBridge.BeginInitialization(owner, Identity)"):
        if token not in facade:
            errors.append("天空岛身份门面缺少 " + token)
    for token in ("SceneGuidToPathMapProvider.SceneGuidToPathMap", "SceneGuidToPathMapProvider.ScenePathToGuidMap",
                  "new SceneReference(identity.SceneGuid)", "new SceneInfoEntry(identity.SceneId, identity.Reference)",
                  "FindReference(__instance)", "OwnsCore(__instance, out identity)", "IsActiveScene(identity)",
                  "SceneManager.GetActiveScene().handle != __instance.gameObject.scene.handle",
                  "provider.gameObject.scene.handle == scene.handle", "provider.GetLocation(name)",
                  'RequireField(typeof(MultiSceneCore), "OnSubSceneLoaded")', "loadedEventField.GetValue(null)",
                  "listener(core, scene)", "activeSubSceneField.SetValue(core, previous)",
                  "SceneLoader.onFinishedLoadingScene += OnSceneLoadFinished", "SceneLoader.onFinishedLoadingScene -= OnSceneLoadFinished",
                  "SceneManager.sceneUnloaded += OnSceneUnloaded", "SceneManager.sceneUnloaded -= OnSceneUnloaded",
                  "SceneLoader.IsSceneLoading || SceneManager.GetSceneByPath(identity.ScenePath).isLoaded",
                  "harmony.UnpatchAll(HarmonyId)", "guidMap.Remove(identity.SceneGuid)", "pathMap.Remove(identity.ScenePath)",
                  "if (identities.Count == 0) CleanupPatches();"):
        if token not in source:
            errors.append("桥接缺少 " + token)
    for token in ("SceneInfoCollection.Entries.Add", 'PropertyGetter(typeof(SceneReference), "BuildIndex")',
                  'RequireMethod(typeof(SceneInfoCollection), "GetSceneInfo", typeof(int))',
                  "SceneManager.LoadSceneAsync(", "SceneManager.UnloadSceneAsync("):
        if token in source or token in facade:
            errors.append("单场景桥接不得全局解释 -1 或再次装卸世界：" + token)
    # 官方加载状态机的转译补丁全仓只能装一份：第二份找不到已被替换的 LevelInited 调用，会让所有 Mod 地图一起进不去。
    if source.count("harmony.Patch(moveNext, transpiler:") != 1 or "new Harmony(" in facade:
        errors.append("场景加载转译补丁必须只由 ModRaidSceneBridge 装一份，客户端只登记身份")
    for path in ROOT.rglob("*.cs"):
        rel = path.relative_to(ROOT).as_posix()
        if rel.startswith(("tmp/", ".claude/", "tests/", "鸭科夫源码/", "Build/")) or rel == "Utilities/RaidScenes/ModRaidSceneBridge.cs":
            continue
        if "LoaderInitializationTranspiler" in path.read_text(encoding="utf-8-sig", errors="replace"):
            errors.append("别处不得再装场景加载转译补丁：" + rel)
    # 官方小地图用 GetSceneID(GetActiveScene().buildIndex) 反查地图身份，bundle 场景恒为 -1，
    # 所以这一处 patch 是必要的；但它必须按「当前活动场景是已登记的 Mod 地图」收窄，
    # 绝不能写成「buildIndex == -1 就返回某张图」——那才是被禁的全局 -1 别名。
    if 'RequireMethod(typeof(SceneInfoCollection), "GetSceneID", typeof(int))' in source:
        if 'SceneIdByBuildIndexPrefix' not in source:
            errors.append('patch 了 GetSceneID(int) 却没有对应前缀')
        else:
            body = source.split('SceneIdByBuildIndexPrefix(int buildIndex', 1)[-1].split(NEWLINE + '        }', 1)[0]
            if 'ActiveIdentity' not in body:
                errors.append('GetSceneID(int) 前缀必须按当前活动场景收窄，否则是全局 -1 别名')
            if 'buildIndex >= 0' not in body:
                errors.append('GetSceneID(int) 前缀必须拒绝正常 buildIndex')
    runtime = clean_source((ROOT / "SkyIsland/SkyIslandRuntimeModule.cs").read_text(encoding="utf-8-sig"))
    for token in ("SkyIslandSceneReferenceBridge.EnsureRegistered()", "SkyIslandSceneReferenceBridge.Shutdown()"):
        if token not in runtime:
            errors.append("缺少生命周期归属：" + token)
    # CR-2026-09-08-005：初始化失败信号既要有实现，也要有消费方；只有定义等于没有恢复流程。
    for token in ('RequireMethod(typeof(LevelManager), "NotifySaveBeforeLoadScene", typeof(bool)), "SaveBeforeLoadPrefix"',
                  '"LoaderInitializationTranspiler"', 'throw new OperationCanceledException(initializationFailure)',
                  'if (replaced != 1) throw'):
        if token not in source:
            errors.append("桥接缺少初始化失败信号：" + token)
    lease = clean_source((ROOT / "SkyIsland/SkyIslandRaidLease.cs").read_text(encoding="utf-8-sig"))
    session = clean_source((ROOT / "SkyIsland/SkyIslandSession.cs").read_text(encoding="utf-8-sig"))
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
