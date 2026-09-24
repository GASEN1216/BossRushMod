"""Guard the Wish Fountain module owner, legacy entry bridges, and frozen restore flow."""
from pathlib import Path
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "runtime": "Integration/WishFountain/WishFountainRuntimeModule.cs",
    "builder": "Integration/WishFountain/WishFountainBuilder.cs",
    "events": "Integration/WishFountain/WishFountainBuilder_DataEventsAndRuntime.cs",
    "ui": "Integration/WishFountain/WishFountainUIBridge.cs",
    "host": "Integration/WishFountain/WishFountainHostCompatibilityBridge.cs",
}


def body(source, marker):
    start = source.index(marker)
    opening = source.index("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening:index + 1]
    raise ValueError("unclosed method: " + marker)


def validate(runtime, builder, events, ui, host):
    errors = []

    def need(source, marker, tokens):
        try:
            block = body(source, marker)
        except ValueError as exc:
            errors.append(str(exc))
            return ""
        for token in tokens:
            if token not in block:
                errors.append(marker + " missing: " + token)
        return block

    module_decl = "internal sealed partial class WishFountainRuntimeModule"
    for name, source in (("builder", builder), ("events", events), ("UI bridge", ui), ("runtime", runtime)):
        if module_decl not in source:
            errors.append(name + " must belong to WishFountainRuntimeModule")
    if "partial class ModBehaviour" in builder or "partial class ModBehaviour" in events or "partial class ModBehaviour" in ui:
        errors.append("Wish implementation partials must not retain ModBehaviour state")

    for state in ("starwishBuildingInjected", "starwishBuildingPrefabGO", "starwishBuildingIcon",
                  "starwishAssetBundle", "starwishModelPrefab", "starwishRestoreCoroutine",
                  "preparedStarwishBuildingInstanceIds", "preparedStarwishSceneHandle", "wishFountainView"):
        if state not in builder + events + ui:
            errors.append("module state missing: " + state)
    for cache in ("starwishBuildingIcon", "starwishAssetBundle", "starwishModelPrefab"):
        if "private static " + ("Sprite " if cache == "starwishBuildingIcon" else "AssetBundle " if cache == "starwishAssetBundle" else "GameObject ") + cache in builder:
            errors.append(cache + " remains a host-independent static cache")

    if "internal GameObject ModelPrefab" not in runtime or "return starwishModelPrefab" not in runtime:
        errors.append("module must expose the existing model resource to the mailbox bridge")
    if "internal bool HasAssetBundle" not in runtime or "starwishAssetBundle != null" not in runtime:
        errors.append("module must expose the loaded bundle readiness predicate")
    need(runtime, "public override void OnAwake(ModBehaviour owner)", ["_owner = owner"])
    need(runtime, "public override void OnDestroy()", ["CleanupWishFountainBuilding()", "_owner = null"])
    need(runtime, "private Coroutine StartCoroutine(IEnumerator routine)", ["_owner.StartCoroutine(routine)"])
    need(runtime, "private void StopCoroutine(Coroutine coroutine)", ["_owner.StopCoroutine(coroutine)"])
    need(runtime, "private void RequestBaseBuildingAreaRepaint(string source)", ["_owner.RequestBaseBuildingAreaRepaint(source)"])
    for name in ("FindGameType", "GetBuildingType", "GetBuildingDataMethod", "GetBuildingManagerAnyMethod",
                 "GetBuildingIdProperty", "AssignBuildingContainerField"):
        if "BuildingInjectionHelper." + name + "(" not in runtime:
            errors.append("runtime reflection helper must forward to BuildingInjectionHelper: " + name)

    initialize = need(builder, "private void InitWishFountainBuilding(bool isEarlyInit)", [
        "LocalizationInjector.InjectWishFountainLocalization()", "LoadStarwishBuildingIcon()",
        "LoadStarwishBuildingModel()", "CreateStarwishBuildingPrefab()", "InjectStarwishBuildingData()",
        "RegisterStarwishBuildingEvents()", "EnsureWishFountainView()", "starwishBuildingInjected = true",
        "if (!isEarlyInit && HasPendingStarwishBuildingsInManager())",
        "RequestBaseBuildingAreaRepaint(\"InitWishFountainBuilding\")"])
    order = ["LocalizationInjector.InjectWishFountainLocalization()", "LoadStarwishBuildingIcon()",
             "LoadStarwishBuildingModel()", "CreateStarwishBuildingPrefab()", "InjectStarwishBuildingData()",
             "RegisterStarwishBuildingEvents()", "starwishBuildingInjected = true"]
    if initialize:
        positions = [initialize.find(token) for token in order]
        ensure_position = initialize.find("EnsureWishFountainView()", initialize.find("RegisterStarwishBuildingEvents()"))
        positions.insert(6, ensure_position)
        if positions != sorted(positions):
            errors.append("building initialization order changed")
    need(builder, "internal void TryInitializeWishFountainEarly()", [
        "Scene activeScene = SceneManager.GetActiveScene()", "!activeScene.IsValid() || !IsBaseHubSceneName(activeScene.name)",
        "FindGameType(\"Duckov.Buildings.BuildingDataCollection\")", "InitWishFountainBuilding(true)"])

    cleanup = need(builder, "public void CleanupWishFountainBuilding()", [
        "StopCoroutine(starwishRestoreCoroutine)", "starwishRestoreCoroutine = null",
        "ResetStarwishPreparedBuildingCache()", "UnregisterStarwishBuildingEvents()",
        "AssetBundleUnloadHelper.TryUnload(starwishAssetBundle", "starwishAssetBundle = null", "starwishModelPrefab = null"])
    cleanup_order = ["StopCoroutine(starwishRestoreCoroutine)", "ResetStarwishPreparedBuildingCache()",
                     "UnregisterStarwishBuildingEvents()", "AssetBundleUnloadHelper.TryUnload(starwishAssetBundle",
                     "starwishAssetBundle = null", "starwishModelPrefab = null"]
    if cleanup and [cleanup.find(token) for token in cleanup_order] != sorted(cleanup.find(token) for token in cleanup_order):
        errors.append("building cleanup order changed")

    need(events, "private void RegisterStarwishBuildingEvents()", [
        "builtEvent != null && !_builtEventSubscribed", "builtEvent.AddEventHandler(null, handler)",
        "_builtEventSubscribed = true", "destroyedEvent != null && !_destroyedEventSubscribed",
        "destroyedEvent.AddEventHandler(null, handler)", "_destroyedEventSubscribed = true"])
    need(events, "private void UnregisterStarwishBuildingEvents()", [
        "builtEvent != null && _builtEventSubscribed", "builtEvent.RemoveEventHandler(null, handler)",
        "_builtEventSubscribed = false", "destroyedEvent != null && _destroyedEventSubscribed",
        "destroyedEvent.RemoveEventHandler(null, handler)", "_destroyedEventSubscribed = false"])
    restore = need(events, "private IEnumerator RestoreWishFountainBuildingsDelayed(string source)", [
        "yield return null;", "RefreshStarwishPreparedBuildingCacheForActiveScene()",
        "ObjectCache.GetSceneObjectsByType(buildingType)", "EnsureStarwishFunctionPoints(buildingGO)",
        "preparedStarwishBuildingInstanceIds.Add(instanceId)", "finally", "starwishRestoreCoroutine = null"])
    if restore.count("yield return null;") != 2:
        errors.append("restoration must preserve its two-frame wait")
    if restore.find("yield return null;") > restore.find("RefreshStarwishPreparedBuildingCacheForActiveScene()"):
        errors.append("scene cache must refresh after both restoration frames")

    need(ui, "public void OpenWishFountainUI()", ["EnsureWishFountainView()", "wishFountainView.ResetAndOpen()"])
    bridge_routes = {
        "InitWishFountainBuilding": "WishFountainRuntime.InitWishFountainBuilding()",
        "TryInitializeWishFountainEarly": "WishFountainRuntime.TryInitializeWishFountainEarly()",
        "CleanupWishFountainBuilding": "WishFountainRuntime.CleanupWishFountainBuilding()",
        "RestoreWishFountainBuildings": "WishFountainRuntime.RestoreWishFountainBuildings()",
        "OpenWishFountainUI": "WishFountainRuntime.OpenWishFountainUI()",
    }
    for method, route in bridge_routes.items():
        if route not in body(host, method + "("):
            errors.append("host compatibility entry missing: " + method + " -> " + route)
    return errors


def main():
    sources = {key: clean_source((ROOT / path).read_text(encoding="utf-8-sig")) for key, path in FILES.items()}
    try:
        errors = validate(sources["runtime"], sources["builder"], sources["events"], sources["ui"], sources["host"])
        for key, token in (("host", "WishFountainRuntime.CleanupWishFountainBuilding()"),
                           ("runtime", "return BuildingInjectionHelper.FindGameType(fullTypeName);")):
            if sources[key].count(token) != 1:
                errors.append("negative probe anchor must be unique: " + token)
                continue
            mutated = sources.copy()
            mutated[key] = mutated[key].replace(token, "MissingWishFountainForward()", 1)
            if not validate(mutated["runtime"], mutated["builder"], mutated["events"], mutated["ui"], mutated["host"]):
                errors.append("negative probe was not rejected: " + token)
    except (ValueError, KeyError) as error:
        errors = [str(error)]
    for error in errors:
        print("WishFountainRuntimeModuleGuard: FAIL - " + error)
    if not errors:
        print("WishFountainRuntimeModuleGuard: PASS (2 negative probes)")
    return bool(errors)


if __name__ == "__main__":
    sys.exit(main())
