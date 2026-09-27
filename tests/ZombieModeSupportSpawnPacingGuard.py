"""ZombieModeSupportSpawnPacingGuard: split/summon support spawns must be paced without reducing spawn count."""

from pathlib import Path


BOSS = Path("ZombieMode/ZombieModeRuntimeModule_BossController.cs")
BOSS_HOST = Path("ZombieMode/ZombieModeCombatHostBridge.cs")
POLLUTION_PARTS = [
    Path("ZombieMode/ZombieModeRuntimeModule_Pollution.cs"),
    Path("ZombieMode/ZombieModeRuntimeModule_PollutionSkills.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs"),
]
CLEANUP = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
RUNTIME_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")
BRIDGES = Path("ZombieMode/ZombieModeEntryHostBridge.cs")


def fail(message: str) -> int:
    print("ZombieModeSupportSpawnPacingGuard: FAIL - " + message)
    return 1


def read_pollution() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in POLLUTION_PARTS)


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    boss_text = BOSS.read_text(encoding="utf-8")
    pollution_text = read_pollution()
    cleanup_text = CLEANUP.read_text(encoding="utf-8")
    runtime_module_text = RUNTIME_MODULE.read_text(encoding="utf-8")
    bridge_text = BRIDGES.read_text(encoding="utf-8")
    boss_host_text = BOSS_HOST.read_text(encoding="utf-8")

    required = [
        "QueueZombieModeSmallSplitSpawn(",
        "QueueZombieModeSplitterChildSpawn(",
        "ProcessZombieModeSupportSpawnQueue(",
        "ZombieModeSupportSpawnRequestsPerFrame",
        "yield return null;",
    ]
    for token in required:
        if token not in pollution_text and token not in boss_text:
            return fail("missing paced support spawn token: " + token)

    forbidden_direct_calls = [
        "SpawnZombieModeSplitterChildAsync(runId, boss.transform.position + offset, ZombieModeTuning.SplitterBossSummonScale).Forget();",
        "SpawnZombieModeSplitterChildAsync(runId, victim.transform.position + offset, ZombieModeTuning.SplitterBossSplitChildScale).Forget();",
        "SpawnZombieModeSmallSplitAsync(runId, character.transform.position + offset).Forget();",
    ]
    combined = boss_text + "\n" + pollution_text
    for token in forbidden_direct_calls:
        if token in combined:
            return fail("support spawn still bypasses pacing queue: " + token)

    cleanup_start = runtime_module_text.find("internal void CleanupZombieModeRunOnlyState")
    cleanup_end = runtime_module_text.find("internal bool ShouldRollbackZombieModeEntryResources", cleanup_start)
    cleanup_body = runtime_module_text[cleanup_start:cleanup_end] if cleanup_start >= 0 and cleanup_end > cleanup_start else ""
    clear_queue = cleanup_body.find("owner.ClearZombieModeSupportSpawnQueueForRuntimeModule();")
    run_only_cleanup = cleanup_body.find("RunScopedRegistry.ForEachReverse(")
    if clear_queue < 0 or run_only_cleanup < 0 or clear_queue > run_only_cleanup:
        return fail("RuntimeModule cleanup must clear pending support spawns before run-only object callbacks")
    if "ClearZombieModeSupportSpawnQueue();" not in bridge_text:
        return fail("RuntimeModule compatibility bridge must clear pending support spawn queue")
    if "module.ClearZombieModeSupportSpawnQueue();" not in boss_host_text or "zombieModeSupportSpawnRequests.Clear();" not in boss_text:
        return fail("boss support queue must be module-owned and cleared through the host bridge")
    processor = extract_method(boss_text, "private IEnumerator ProcessZombieModeSupportSpawnQueue(")
    enqueue = extract_method(boss_text, "private void QueueZombieModeSupportSpawn(")
    if not processor or not enqueue:
        return fail("module support-spawn enqueue/processor methods not found")
    for token in [
        "processed < ZombieModeSupportSpawnRequestsPerFrame",
        "zombieModeSupportSpawnRequests.Dequeue()",
        "if (!IsZombieModeRunValid(request.RunId))",
        "SpawnZombieModeSmallSplitAsync(request.RunId, request.Position).Forget();",
        "SpawnZombieModeSplitterChildAsync(request.RunId, request.Position, request.Scale).Forget();",
        "if (zombieModeSupportSpawnRequests.Count > 0)",
        "yield return null;",
        "zombieModeSupportSpawnProcessorStarted = false;",
    ]:
        if token not in processor:
            return fail("support-spawn processor behavior missing -> " + token)
    if "zombieModeSupportSpawnRequests.Enqueue(request);" not in enqueue or "owner.StartZombieModeBossCoroutineForRuntimeModule(ProcessZombieModeSupportSpawnQueue(request.RunId), request.RunId);" not in enqueue:
        return fail("support-spawn enqueue must start the run-owned coroutine only when needed")
    if "return StartZombieModeCoroutine(routine, runId);" not in boss_host_text:
        return fail("module support-spawn coroutine must retain the host RunOnly registration bridge")
    if "zombieModeRuntimeModule.CleanupZombieModeRunOnlyState(reason, destroyGameObjects)" not in cleanup_text:
        return fail("host cleanup entry must forward into RunOnly owner")

    print("ZombieModeSupportSpawnPacingGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
