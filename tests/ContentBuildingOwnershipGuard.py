"""D1: content building modules own their state; ModBehaviour keeps only stable entry bridges."""
from pathlib import Path
import json
import re
import sys
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
MODULES = {
    "DailyReportMailboxBuilder": ["Integration/DailyReport/DailyReportMailboxBuilder.cs", "Integration/DailyReport/DailyReportMailboxRuntime.cs"],
    "CampaignBoardBuilder": ["Campaign/CampaignBoardBuilder.cs"],
    "ShowcaseBuildingBuilder": ["Integration/BackMountain/ShowcaseBuildingBuilder.cs"],
    "PetNestBuilder": ["PetNest/PetNestBuilder.cs", "PetNest/PetNestBuilder_DataEventsAndRuntime.cs"],
}


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def wedding_compatibility_errors(wedding_bridge, repaint_module):
    errors = []
    if "internal void RequestBaseBuildingAreaRepaint(string source)" not in repaint_module:
        errors.append("Wedding runtime must retain the repaint implementation")
    if "WeddingRuntime.RequestBaseBuildingAreaRepaint(source)" not in wedding_bridge:
        errors.append("legacy repaint callers must forward to the Wedding module")
    for name in ("FindGameType", "GetBuildingManagerType", "GetBuildingManagerAnyMethod", "GetBuildingDataMethod",
                 "GetBuildingType", "GetBuildingIdProperty", "AssignBuildingContainerField"):
        if not re.search(r"private static [\w<>]+ " + name + r"\(", wedding_bridge) \
                or "BuildingInjectionHelper." + name + "(" not in wedding_bridge:
            errors.append("Wedding host compatibility must retain the shared building helper: " + name)
    return errors


def main():
    bridge = read("Integration/IntegrationHostCompatibility.cs")
    wedding_bridge = read("Integration/IntegrationHostCompatibility.cs")
    repaint_module = read("Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs")
    helper = read("Common/Buildings/BuildingInjectionHelper.cs")
    models = read("Common/Buildings/BuildingModelHelper.cs")
    for name, paths in MODULES.items():
        code = "\n".join(read(path) for path in paths)
        assert "partial class ModBehaviour" not in code, name + " must not return its implementation to ModBehaviour"
        assert "internal sealed partial class " + name in code, name + " must retain its own type"
        assert "private readonly ModBehaviour _owner" in code and "_owner = owner" in code, name + " must keep its creating owner"
        assert "new " + name + "(this)" in bridge, name + " must be created by its actual host"
        assert "BuildingInjectionHelper.FindGameType(" in code, name + " must use shared reflection"
        assert not re.search(r"(?<![.\w])(FindGameType|AssignBuildingContainerField)\(", code), name + " must not rely on hidden partial helpers"
        assert "_owner.RequestBaseBuildingAreaRepaint(" in code, name + " must reuse host-owned repaint lifecycle"
    restore_core = read("Common/Buildings/BuildingRestoreCore.cs")
    for path in (MODULES["DailyReportMailboxBuilder"][1], MODULES["PetNestBuilder"][1]):
        code = read(path)
        assert "Restore.Request(source)" in code, path + " must request its owner-local restore core"
        assert "AddEventHandler(null," in code and "RemoveEventHandler(null," in code, path + " must keep event cleanup"
    for path in (MODULES["DailyReportMailboxBuilder"][0], MODULES["PetNestBuilder"][0]):
        code = read(path)
        assert "new BuildingRestoreCore(owner," in code, path + " must bind a restore core to its actual owner"
        assert "Restore.Cancel()" in code, path + " cleanup must cancel its restoration coroutine"
    assert "owner.StartCoroutine(RestoreDelayed(generation))" in restore_core, "Shared core must schedule on creating owner"
    assert "owner.StopCoroutine(active)" in restore_core, "Shared core must cancel on creating owner"
    assert restore_core.count("yield return null;") == 2, "Shared core must wait two frames"
    assert "SceneManager.GetActiveScene().handle" in restore_core, "Shared core must read active scene after waiting"
    assert "if (comp == null || !isTargetBuilding(comp)) continue;" in restore_core, "Shared core must respect Unity fake null"
    assert "buildingGO.GetInstanceID()" in restore_core, "Shared core must deduplicate by Unity instance ID"
    assert "if (generation == requestGeneration) restoreCoroutine = null;" in restore_core, "Late finally must not clear a new request"
    compatibility_errors = wedding_compatibility_errors(wedding_bridge, repaint_module)
    assert not compatibility_errors, "; ".join(compatibility_errors)
    for token in ("WeddingRuntime.RequestBaseBuildingAreaRepaint(source)",
                  "return BuildingInjectionHelper.FindGameType(fullTypeName);"):
        assert wedding_bridge.count(token) == 1, "negative probe anchor must be unique: " + token
        mutation = wedding_bridge.replace(token, "MissingWeddingForward()", 1)
        assert wedding_compatibility_errors(mutation, repaint_module), "negative probe was not rejected: " + token
    assert "RepaintBaseBuildingAreasDelayed" not in bridge, "Bridge must not clone the repaint engine"
    for name in ("FindGameType", "GetBuildingType", "GetBuildingManagerType", "GetBuildingManagerAnyMethod", "GetBuildingDataMethod", "GetBuildingIdProperty", "AssignBuildingContainerField"):
        assert re.search(r"internal static [\w<>\[\]]+ " + name + r"\(", helper), "Missing shared binding: " + name
        assert "BuildingInjectionHelper." + name + "(" in read("Integration/Wedding/WeddingBuildingInjector.cs"), "Legacy building caller must forward: " + name
    for flag in ("buildingManagerTypeResolved", "buildingManagerAnyMethodResolved", "getBuildingAmountMethodResolved", "getBuildingDataMethodResolved", "buildingTypeResolved", "buildingIdPropertyResolved"):
        assert "if (!" + flag + ")" in helper and flag + " = true" in helper, "One-time resolution, including misses, must remain cached"
    assert 'new Type[] { typeof(string), typeof(bool) }' in helper, "Any reflection overload contract must remain exact"
    assert re.search(r"internal static MethodInfo GetBuildingAmountMethod\(", helper), "Missing shared binding: GetBuildingAmountMethod"
    assert '"GetBuildingAmount"' in helper and 'new Type[] { typeof(string) }' in helper, "GetBuildingAmount reflection overload contract must remain exact"
    wedding = read("Integration/Wedding/WeddingBuildingInjector.cs")
    presence = wedding.split("private bool RefreshWeddingBuildingPresence()", 1)[1].split("private bool TryUseCachedWeddingNpcPosition", 1)[0]
    assert "BuildingInjectionHelper.GetBuildingAmountMethod()" in presence and "GetBuildingManagerAnyMethod" not in presence, \
        "Chapel presence before injection must count raw saved IDs: official Any skips unregistered infos and is always false before injection"
    for name in ("CollectStarwishRenderableComponents", "TryGetCombinedBounds", "AddStarwishGraphicsCollider", "FixStarwishModelShaders"):
        assert re.search(r"internal static [\w<>\[\]]+ " + name + r"\(", models), "Missing model utility: " + name
        assert "BuildingModelHelper." + name + "(" in read("Integration/WishFountain/WishFountainBuilder.cs"), "Wish fountain must forward model utility: " + name
    assert "_owner.StarwishBuildingModelPrefab" in read(MODULES["DailyReportMailboxBuilder"][0]), "Mailbox must borrow the existing explicitly owned resource"
    assert "wishFountainRuntime != null ? wishFountainRuntime.ModelPrefab : null" in bridge, \
        "Mailbox bridge must read the existing WishFountain module resource"
    assert "return starwishModelPrefab" in read("Integration/WishFountain/WishFountainRuntimeModule.cs") \
        and "AssetBundle.Load" not in bridge, "Bridge must not introduce another resource loader"
    for path in ("tests/fixtures/ContentBuildingOwnership/ContentBuildingOwnership.csproj", "tests/fixtures/ContentBuildingOwnership/Program.cs", "tests/fixtures/ContentBuildingOwnership/run.py"):
        assert (ROOT / path).is_file(), "Missing executable regression: " + path
    coverage = json.loads((ROOT / "Assets/Data/GameplayCoverage.json").read_text(encoding="utf-8-sig"))
    feature = next((row for row in coverage["features"] if row["id"] == "CONTENT_BUILDINGS"), None)
    assert feature and "Common/Buildings" in feature["sources"], "Shared building directory needs gameplay coverage mapping"
    assert feature["automatic"] == [], "Host-independent fixture must not masquerade as a registered F3 automatic case"
    assert any("tests/fixtures/ContentBuildingOwnership/run.py" in case["steps"] for case in feature["manual"]), "Coverage must identify the actual offline fixture"
    assert len(feature["manual"]) >= 2, "Keep real old-save restoration and build/slot/lifetime smoke paths pending"
    print("ContentBuildingOwnershipGuard: PASS (2 negative probes)")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print("ContentBuildingOwnershipGuard: FAIL - " + str(error))
        sys.exit(1)
