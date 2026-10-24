"""ZombieModeTargetSceneActivationGuard: initialize after target scene is active.

Unity can raise sceneLoaded for a sub scene before SceneManager.GetActiveScene()
has switched to that sub scene. ZombieMode initialization scans the active scene
for spawners, enemies and containers, so the target-map branch must wait for the
target scene to become active before BeginZombieModeRunShell/Initialize.
"""

from pathlib import Path
import sys
from cs_source_util import clean_source


ENTRY = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
RUNTIME_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")
TARGET_ENTRY = Path("ZombieMode/ZombieModeRuntimeModule_TargetSceneEntry.cs")


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
    entry = clean_source(ENTRY.read_text(encoding="utf-8-sig"))
    # 入场等待协程与身份核对在 2026-10-24 原样拆到同一 partial 的 _TargetSceneEntry.cs（大文件预算）。
    module = clean_source(RUNTIME_MODULE.read_text(encoding="utf-8-sig") + "\n" + TARGET_ENTRY.read_text(encoding="utf-8-sig"))

    host_handle = extract_method(entry, "private bool TryHandleZombieModePendingMapSceneLoaded")
    if not host_handle or "module.TryHandleZombieModePendingMapSceneLoaded(scene, loadedMapConfig)" not in host_handle:
        return fail("ZombieModeTargetSceneActivationGuard: host scene callback must forward to RuntimeModule")

    handle = extract_method(module, "internal bool TryHandleZombieModePendingMapSceneLoaded")
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

    wait_method = extract_method(module, "internal System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize")
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

    # 官方 InitLevel 置 levelInited 后还要 0.25 秒才 SetPosition(startPos) 并置 afterInit；
    # 只等 levelInited 会拿未定位的玩家做可达性判定，失败回基地还会让官方 SetPosition NRE。
    flat_wait = " ".join(wait_method.split())
    if "bool levelAfterInit = IsOfficialLevelAfterInitForZombieMode();" not in flat_wait or             "sceneLoaded && activeMatches && sceneLoaderDone && levelInited && levelAfterInit" not in flat_wait:
        return fail("ZombieModeTargetSceneActivationGuard: initialization must wait for LevelManager.AfterInit")

    final_guard = "if (!scene.isLoaded || finalActiveScene.name != scene.name || finalActiveScene.handle != scene.handle || !finalSceneLoaderDone || !finalLevelInited || !finalLevelAfterInit)"
    if final_guard not in flat_wait or flat_wait.index(final_guard) > flat_wait.index("int runId = BeginZombieModeRunShell("):
        return fail("ZombieModeTargetSceneActivationGuard: final target/readiness recheck must precede Begin, including timeout")
    for statement in [
        'bool finalSceneLoaderDone = owner.ReadSceneLoaderDoneWithWarningForRuntimeModule("ZombieModeTargetSceneInitialize");',
        'bool finalLevelInited = owner.ReadLevelInitedWithWarningForRuntimeModule("ZombieModeTargetSceneInitialize");',
        "bool finalLevelAfterInit = IsOfficialLevelAfterInitForZombieMode();",
        "bool activeMatches = activeScene.name == scene.name && activeScene.handle == scene.handle;",
    ]:
        if statement not in flat_wait:
            return fail("ZombieModeTargetSceneActivationGuard: missing live final readiness/scene identity probe -> " + statement)

    identity_call = "IsZombieModeTargetSceneEntryCurrent(expectedEntryAttemptId, expectedRunState, expectedTransaction, expectedOwner, expectedRunId, scene.name)"
    if flat_wait.count(identity_call) != 3:
        return fail("ZombieModeTargetSceneActivationGuard: entry identity must be checked in wait, before final probes and after probes")
    identity = " ".join(extract_method(module, "private bool IsZombieModeTargetSceneEntryCurrent(").split())
    for predicate in [
        "owner != null && ReferenceEquals(owner, expectedOwner)",
        "entryAttemptId == expectedEntryAttemptId && ReferenceEquals(runState, expectedRunState)",
        "ReferenceEquals(entryTransaction, expectedTransaction)",
        "runState.RunId == expectedRunId && !runState.IsCleaningUp && pendingEntry",
        "runState.LifecyclePhase == ZombieModeLifecyclePhase.LoadingMap",
        "ZombieModeMapSelectionHelper.HasPendingZombieEntry",
        "ZombieModeMapSelectionHelper.GetPendingTargetSubSceneName() == targetSceneName",
    ]:
        if predicate not in identity:
            return fail("ZombieModeTargetSceneActivationGuard: stale waiter can escape its entry identity -> " + predicate)
    entry_begin = " ".join(extract_method(module, "internal bool TryBeginMapSelectionShell()").split())
    if "entryAttemptId++; pendingEntry = true;" not in entry_begin:
        return fail("ZombieModeTargetSceneActivationGuard: successful entry selection must advance its attempt identity")
    after_init = extract_method(module, "internal static bool IsOfficialLevelAfterInitForZombieMode")
    if "LevelManager.AfterInit" not in after_init:
        return fail("ZombieModeTargetSceneActivationGuard: AfterInit probe must read LevelManager.AfterInit")

    print("ZombieModeTargetSceneActivationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
