"""Mod 角色换到主场景后仍必须解除子场景的距离休眠登记。"""
from pathlib import Path


def main():
    helper = Path("Utilities/SpawnedEnemyActivationHelper.cs").read_text(encoding="utf-8")
    for token in ("SceneManager.sceneCount", "SceneManager.GetSceneAt(i).buildIndex",
                  "SetActiveByPlayerDistance.Unregister(characterObject, sceneIndex)",
                  "character.SetSleeping(false)", "characterObject.SetActive(true)"):
        if token not in helper:
            print("SpawnedEnemyActivationGuard: FAIL 缺少 " + token)
            return 1
    if "characterObject.scene.buildIndex" in helper or "FindObjectsOfType" in helper:
        print("SpawnedEnemyActivationGuard: FAIL 不得用主场景索引代替登记索引，或扫描整图角色")
        return 1
    for path in ("Utilities/EnemySpawnCore.cs", "ModBehaviour.cs", "ModeH/ModeHSpawnBridge.cs"):
        if "SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep" not in Path(path).read_text(encoding="utf-8"):
            print("SpawnedEnemyActivationGuard: FAIL 激活路径未接保护: " + path)
            return 1
    print("SpawnedEnemyActivationGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
