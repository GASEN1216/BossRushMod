"""自建试验场只拥有本次手动会话的资源，不能改写正式地图/存档。"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    paths = {
        "session": "DebugAndTools/ArenaPrototype/ArenaPrototypeSession.cs",
        "nav": "DebugAndTools/ArenaPrototype/ArenaPrototypeNavigation.cs",
        "ui": "DebugAndTools/ArenaPrototype/ArenaPrototypeControls.cs",
        "runner": "DebugAndTools/F3GameplayValidationRunner.cs",
    }
    sources = {key: (ROOT / path).read_text(encoding="utf-8-sig") for key, path in paths.items()}
    required = {
        "session": ["!ModBehaviour.DevModeEnabled", "F3GameplayValidationRunner.IsRunning",
                    "owner.ValidationHasActiveMode(out mode)", "steps.Current", "yield return current",
                    "this == null || closed || arena == null", "Destroy(created.gameObject)",
                    "clone = Instantiate(source)", "clone.dropBoxOnDead = false", "clone.hasSoul = false",
                    "enemyPresets.Add(clone)", "clone = null", "enemyPresets.Clear()",
                    "foreach (CharacterRandomPreset owned in enemyPresets)",
                    "seeker.graphMask = navigation.Mask", "enemy.SetTeam(Teams.wolf)",
                    "ReleaseFromPlayerDistanceSleep(enemy)", "scan.Dispose()", "navigation.Dispose()",
                    "bundle.Unload(true)", "player.SetPosition(returnPosition)", "if (closed) return",
                    "wallMask = layers.wallLayerMask.value;", "runtimeMaterials.Add(converted)",
                    "Destroy(material)", "runtimeMaterials.Clear()"],
        "nav": ["owner.data.AddGraph<NavMeshGraph>()", "owner.ScanAsync(graph)",
                "mesh.vertexCount > 4095",
                "owner.data.RemoveGraph(graph)", "graph.graphIndex >= 32", "GraphMask.FromGraph(graph)"],
        "ui": ["BossRushUILayers.Hud", "ConfigureCanvasScaler", "ApplyGameFont", "ApplyPanelSkin"],
        "runner": ["_host.GetComponent<ArenaPrototypeSession>() != null"],
    }
    errors = []
    for key, tokens in required.items():
        for token in tokens:
            if token not in sources[key]:
                errors.append(f"{paths[key]}: 缺少 {token}")
    session = sources["session"]
    for event, callback in [("SceneLoader.onStartedLoadingScene", "OnStartedLoading"),
                            ("SceneManager.sceneUnloaded", "OnSceneUnloaded")]:
        for operator in ("+=", "-="):
            if f"{event} {operator} {callback}" not in session:
                errors.append(f"{paths['session']}: 事件未配对 {event} {operator}")
    for operation in ("AddListener(OnPlayerDied)", "RemoveListener(OnPlayerDied)"):
        if operation not in session:
            errors.append(f"{paths['session']}: 缺少 {operation}")
    close = session[session.index("internal void Close("):]
    if close.index("player.SetPosition(returnPosition)") > close.index("Destroy(arena)"):
        errors.append("回收场地前必须先返还玩家")
    for token in ("SavesSystem.Save", "PlayerPrefs.Set", "SceneLoader.Load", "DestroyAllNodes", "ClearGraphs"):
        if token in session + sources["nav"]:
            errors.append(f"试验场不得调用 {token}")
    compile_list = (ROOT / "compile_official.bat").read_text(encoding="utf-8-sig")
    for key in ("session", "nav", "ui"):
        if paths[key].replace("/", "\\") not in compile_list:
            errors.append(f"未登记编译：{paths[key]}")
    if errors:
        print("ArenaPrototypeLifecycleGuard: FAIL\n" + "\n".join(errors))
        return 1
    print("ArenaPrototypeLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
