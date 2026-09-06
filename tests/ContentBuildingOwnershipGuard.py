"""D1: content building modules own their state; ModBehaviour keeps only stable entry bridges."""
from pathlib import Path
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
MODULES = {
    "DailyReportMailboxBuilder": ["Integration/DailyReport/DailyReportMailboxBuilder.cs", "Integration/DailyReport/DailyReportMailboxRuntime.cs"],
    "CampaignBoardBuilder": ["Campaign/CampaignBoardBuilder.cs"],
    "ShowcaseBuildingBuilder": ["Integration/BackMountain/ShowcaseBuildingBuilder.cs"],
    "PetNestBuilder": ["PetNest/PetNestBuilder.cs", "PetNest/PetNestBuilder_DataEventsAndRuntime.cs"],
}


def read(path):
    return re.sub(r"//[^\n]*|/\*.*?\*/", "", (ROOT / path).read_text(encoding="utf-8-sig"), flags=re.S)


def main():
    bridge = read("Integration/ContentBuildingBridges.cs")
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
    for path in (MODULES["DailyReportMailboxBuilder"][1], MODULES["PetNestBuilder"][1]):
        code = read(path)
        assert "_owner.StartCoroutine(" in code, path + " must schedule restoration on the same owner"
        assert "AddEventHandler(null," in code and "RemoveEventHandler(null," in code, path + " must keep event cleanup"
    for path in (MODULES["DailyReportMailboxBuilder"][0], MODULES["PetNestBuilder"][0]):
        assert "_owner.StopCoroutine(" in read(path), path + " cleanup must stop the owner's restoration coroutine"
    assert "internal void RequestBaseBuildingAreaRepaint(string source)" in read("Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs"), \
        "Repaint keeps its existing host implementation and only opens internal access"
    assert "RepaintBaseBuildingAreasDelayed" not in bridge, "Bridge must not clone the repaint engine"
    for name in ("FindGameType", "GetBuildingType", "GetBuildingManagerType", "GetBuildingManagerAnyMethod", "GetBuildingDataMethod", "GetBuildingIdProperty", "AssignBuildingContainerField"):
        assert re.search(r"internal static [\w<>\[\]]+ " + name + r"\(", helper), "Missing shared binding: " + name
        assert "BuildingInjectionHelper." + name + "(" in read("Integration/Wedding/WeddingBuildingInjector.cs"), "Legacy building caller must forward: " + name
    for flag in ("buildingManagerTypeResolved", "buildingManagerAnyMethodResolved", "getBuildingDataMethodResolved", "buildingTypeResolved", "buildingIdPropertyResolved"):
        assert "if (!" + flag + ")" in helper and flag + " = true" in helper, "One-time resolution, including misses, must remain cached"
    assert 'new Type[] { typeof(string), typeof(bool) }' in helper, "Any reflection overload contract must remain exact"
    for name in ("CollectStarwishRenderableComponents", "TryGetCombinedBounds", "AddStarwishGraphicsCollider", "FixStarwishModelShaders"):
        assert re.search(r"internal static [\w<>\[\]]+ " + name + r"\(", models), "Missing model utility: " + name
        assert "BuildingModelHelper." + name + "(" in read("Integration/WishFountain/WishFountainBuilder.cs"), "Wish fountain must forward model utility: " + name
    assert "_owner.StarwishBuildingModelPrefab" in read(MODULES["DailyReportMailboxBuilder"][0]), "Mailbox must borrow the existing explicitly owned resource"
    assert "return starwishModelPrefab" in bridge and "AssetBundle.Load" not in bridge, "Bridge must not introduce another resource loader"
    for path in ("tests/fixtures/ContentBuildingOwnership/ContentBuildingOwnership.csproj", "tests/fixtures/ContentBuildingOwnership/Program.cs", "tests/fixtures/ContentBuildingOwnership/run.py"):
        assert (ROOT / path).is_file(), "Missing executable regression: " + path
    coverage = json.loads((ROOT / "Assets/Data/GameplayCoverage.json").read_text(encoding="utf-8-sig"))
    feature = next((row for row in coverage["features"] if row["id"] == "CONTENT_BUILDINGS"), None)
    assert feature and "Common/Buildings" in feature["sources"], "Shared building directory needs gameplay coverage mapping"
    assert feature["automatic"] == [], "Host-independent fixture must not masquerade as a registered F3 automatic case"
    assert any("tests/fixtures/ContentBuildingOwnership/run.py" in case["steps"] for case in feature["manual"]), "Coverage must identify the actual offline fixture"
    assert len(feature["manual"]) >= 2, "Keep real old-save restoration and build/slot/lifetime smoke paths pending"
    print("ContentBuildingOwnershipGuard: PASS")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print("ContentBuildingOwnershipGuard: FAIL - " + str(error))
        sys.exit(1)
