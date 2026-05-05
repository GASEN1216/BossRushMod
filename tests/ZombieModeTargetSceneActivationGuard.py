"""ZombieModeTargetSceneActivationGuard: initialize after target scene is active.

Unity can raise sceneLoaded for a sub scene before SceneManager.GetActiveScene()
has switched to that sub scene. ZombieMode initialization scans the active scene
for spawners, enemies and containers, so the target-map branch must wait for the
target scene to become active before BeginZombieModeRunShell/Initialize.
"""

from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    entry = ENTRY.read_text(encoding="utf-8")

    handle = extract_method(entry, "private bool TryHandleZombieModePendingMapSceneLoaded")
    if not handle:
        return fail("ZombieModeTargetSceneActivationGuard: pending map scene handler not found")

    if "WaitForZombieModeTargetSceneActiveThenInitialize" not in handle:
        return fail("ZombieModeTargetSceneActivationGuard: target scene branch must defer initialization")

    target_branch_start = handle.find("scene.name == targetSubScene")
    wait_call = handle.find("WaitForZombieModeTargetSceneActiveThenInitialize")
    begin_call = handle.find("BeginZombieModeRunShell", target_branch_start)
    init_call = handle.find("InitializeZombieModeRunAfterMapLoaded", target_branch_start)
    if begin_call >= 0 and begin_call < wait_call:
        return fail("ZombieModeTargetSceneActivationGuard: target branch begins run before active-scene wait")
    if init_call >= 0 and init_call < wait_call:
        return fail("ZombieModeTargetSceneActivationGuard: target branch initializes before active-scene wait")

    wait_method = extract_method(entry, "private System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize")
    if not wait_method:
        return fail("ZombieModeTargetSceneActivationGuard: wait coroutine not found")

    for snippet in [
        "SceneManager.GetActiveScene()",
        "activeScene.name == scene.name",
        "ReadSceneLoaderDoneWithWarning",
        "ReadLevelInitedWithWarning",
        "BeginZombieModeRunShell",
        "InitializeZombieModeRunAfterMapLoaded",
        "FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed)",
        "ZombieModeMapSelectionHelper.ClearPendingZombieEntry();",
    ]:
        if snippet not in wait_method:
            return fail("ZombieModeTargetSceneActivationGuard: wait coroutine missing -> " + snippet)

    print("ZombieModeTargetSceneActivationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
