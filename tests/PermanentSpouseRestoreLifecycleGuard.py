"""Guard: permanent spouse async completion and its real runtime entry points."""
from pathlib import Path
import sys
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
BRIDGE = "Integration/Wedding/WeddingModBehaviourBridge.cs"
HOST_BRIDGE = "Integration/Wedding/WeddingHostCompatibilityBridge.cs"
RUNTIME = "Integration/Wedding/WeddingRuntimeModule.cs"
MODULE = "Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs"


def body(source, marker):
    start = source.index(marker)
    start = source.index("{", start)
    depth = 0
    for end in range(start, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return source[start:end + 1]
    raise ValueError("unclosed member: " + marker)


def validate(bridge, module, cleanup, events, host_bridge, runtime):
    errors = []

    def need(source, marker, tokens):
        block = body(source, marker)
        for token in tokens:
            if token not in block:
                errors.append(marker + " missing: " + token)
        return block

    need(bridge, "public Transform TrySpawnMarriedNpcAtWeddingPoint()", [
        "RequestPermanentSpouseRestore(spouseNpcId, weddingPosition, false)"])
    need(bridge, "private void RestoreFollowingSpouseForCurrentScene(", [
        "RequestPermanentSpouseRestore(spouseNpcId, restorePosition, true)"])
    need(bridge, "private void RequestPermanentSpouseRestore(", [
        "pending.Following == following", "IsPermanentSpouseRestoreCurrent(pending)",
        "Generation = ++permanentSpouseRestoreGeneration", "permanentSpouseRestoreRequest = request"])
    need(bridge, "private bool IsPermanentSpouseRestoreCurrent(", [
        "_owner == null || ModBehaviour.Instance != _owner", "request != permanentSpouseRestoreRequest", "request.Generation != permanentSpouseRestoreGeneration",
        "request.SceneHandle != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle",
        "AffinityManager.GetCurrentSpouseNpcId()", "!AffinityManager.IsMarriedToPlayer(request.NpcId)",
        "AffinityManager.IsSpouseFollowingPlayer(request.NpcId) != request.Following", "HasWeddingBuildingPlaced()"])
    completion = need(bridge, "private async UniTaskVoid RestorePermanentSpouseAsync(", [
        "await PermanentDuckNpcModule.ForceSpawnAtAsync(", "() => IsPermanentSpouseRestoreCurrent(request)",
        "npc == null || !IsPermanentSpouseRestoreCurrent(request)",
        "PrepareSpouseInstanceForFollow(npc.gameObject, request.NpcId)",
        "MarkWeddingNpcInstance(npc.gameObject, request.NpcId)", "DestroyWeddingPlaceholder()",
        "RefreshSpouseInteractionOptions(npc.gameObject)", "if (permanentSpouseRestoreRequest == request)"])
    if completion.find("await PermanentDuckNpcModule") > completion.find("MarkWeddingNpcInstance"):
        errors.append("resident finalization must happen after awaited creation")
    force = need(module, "internal static async UniTask<CharacterMainControl> ForceSpawnAtAsync(", [
        "await DuckNpcSpawner.SpawnAsync", "generation != _spawnGeneration",
        "sceneHandle != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle",
        "isRequestValid != null && !isRequestValid()", "finally", "if (!registered)", "DuckNpcSpawner.Despawn(npc)"])
    post_await = force[force.index("await DuckNpcSpawner.SpawnAsync"):force.index("PermanentDuckNpcRegistry.RegisterInstance")]
    if "isRequestValid != null && !isRequestValid()" not in post_await:
        errors.append("owner validity must be checked after the actual await")
    if "UnregisterInstance" in force:
        errors.append("late owned object cleanup must not unregister a newer object with the same id")
    need(module, "public void Destroy(ModBehaviour mod)", ["_spawnGeneration++"])
    need(module, "internal static void ResetStaticCaches()", ["_spawnGeneration++"])
    need(cleanup, "public void CleanupWeddingBuilding()", ["InvalidatePermanentSpouseRestore()", "spouseFollowRestoreRequestId++"])
    need(events, "private void OnWeddingBuildingDestroyed(", ["InvalidatePermanentSpouseRestore()"])
    need(bridge, "public bool SendSpouseHome(", ["InvalidatePermanentSpouseRestore()"])
    need(bridge, "public void HandleDivorceNpcRelocation(", ["InvalidatePermanentSpouseRestore()"])
    need(bridge, "private void RefreshSpouseInteractionOptions(", ["GetComponentInChildren<PermanentDuckNpcInteractable>(true)"])
    need(bridge, "private IEnumerator DelayedRestoreFollowingSpouse(", ["yield return new WaitForSeconds(SpouseFollowRestorePollInterval)", "SpouseFollowRestoreTimeout"])
    need(bridge, "public void ScheduleRestoreFollowingSpouse(", ["spouseFollowRestoreRequestId++", "StartCoroutine(DelayedRestoreFollowingSpouse(expectedSceneName, spouseFollowRestoreRequestId, context))"])
    need(host_bridge, "public void ScheduleRestoreFollowingSpouse(", ["WeddingRuntime.ScheduleRestoreFollowingSpouse(expectedSceneName, context)"])
    need(host_bridge, "public void CleanupWeddingBuilding()", ["WeddingRuntime.CleanupWeddingBuilding()"])
    need(host_bridge, "internal void TryInitializeWeddingBuildingEarly()", ["WeddingRuntime.TryInitializeWeddingBuildingEarly()"])
    need(runtime, "public override void OnAwake(ModBehaviour owner)", ["_owner = owner"])
    need(runtime, "public override void OnDestroy()", ["CleanupWeddingBuilding()"])
    if "private bool _builtEventSubscribed;" not in runtime or "private bool _destroyedEventSubscribed;" not in runtime:
        errors.append("runtime must own both event subscription flags")
    if "0.25f" not in bridge or "20f" not in bridge or "0.75f" not in bridge:
        errors.append("spouse restore poll, timeout, or settle timing changed")
    return errors


def main():
    paths = [BRIDGE, MODULE, "Integration/Wedding/WeddingBuildingInjector.cs",
             "Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs", HOST_BRIDGE, RUNTIME]
    sources = [clean_source((ROOT / path).read_text(encoding="utf-8-sig")) for path in paths]
    try:
        errors = validate(*sources)
        # Prove the guard rejects the original two omissions and the late-registration regression.
        for index, token in [(0, "RequestPermanentSpouseRestore(spouseNpcId, restorePosition, true)"),
                             (0, "MarkWeddingNpcInstance(npc.gameObject, request.NpcId)"),
                             (1, "generation != _spawnGeneration")]:
            mutation = sources.copy()
            mutation[index] = mutation[index].replace(token, "false" if index == 1 else "MissingCall()")
            if not validate(*mutation):
                errors.append("negative probe was not rejected: " + token)
    except ValueError as exc:
        errors = [str(exc)]
    for error in errors:
        print("PermanentSpouseRestoreLifecycleGuard: FAIL - " + error)
    if not errors:
        print("PermanentSpouseRestoreLifecycleGuard: PASS (3 negative probes)")
    return bool(errors)


if __name__ == "__main__":
    sys.exit(main())
