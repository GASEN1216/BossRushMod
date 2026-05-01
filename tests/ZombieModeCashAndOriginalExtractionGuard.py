from pathlib import Path
import sys


COMPILE = Path("compile_official.bat")
COMPILE_GUARD = Path("tests/ZombieModeCompileListGuard.py")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
MAP_SELECTION = Path("ZombieMode/ZombieModeMapSelectionHelper.cs")
MAP_ISOLATION = Path("ZombieMode/ZombieModeMapIsolation.cs")
EXTRACTION_HELPER = Path("Utilities/OriginalExtractionPointIsolationHelper.cs")
MODELS = Path("ZombieMode/ZombieModeModels.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeCashAndOriginalExtractionGuard: missing " + label + " -> " + snippet)
    return 0


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
    compile_text = COMPILE.read_text(encoding="utf-8")
    compile_guard = COMPILE_GUARD.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    map_selection = MAP_SELECTION.read_text(encoding="utf-8")
    map_isolation = MAP_ISOLATION.read_text(encoding="utf-8")
    extraction_helper = EXTRACTION_HELPER.read_text(encoding="utf-8")
    models = MODELS.read_text(encoding="utf-8")

    result = require(compile_text, "ZombieMode\\ZombieModeCashInvestmentView.cs", "official compile list")
    if result:
        return result

    result = require(compile_guard, "ZombieMode\\\\ZombieModeCashInvestmentView.cs", "compile guard list")
    if result:
        return result

    for stale in [
        "第一版没有现金投入 UI",
        "PendingCashInvestment 默认 0，CashTemporarilyHeld 保持 false",
    ]:
        if stale in entry:
            return fail("ZombieModeCashAndOriginalExtractionGuard: stale cash investment comment remains -> " + stale)

    for snippet in [
        "CreateZombieModeFreeMapEntryCost",
        "entry.enabled = false;",
        "ShowZombieModeCashInvestmentPrompt(delegate",
        "inst.MarkZombieModeMapConfirmedPhase1();",
        "pendingZombieMapConfirmed = true;",
        "StartZombieModeConfirmedMapLoad",
        "SceneLoader.Instance.LoadScene",
    ]:
        result = require(map_selection, snippet, "cash prompt confirmation flow")
        if result:
            return result

    direct_index = map_selection.find("inst.MarkZombieModeMapConfirmedPhase1();")
    prompt_index = map_selection.find("ShowZombieModeCashInvestmentPrompt(delegate")
    if direct_index < prompt_index:
        return fail("ZombieModeCashAndOriginalExtractionGuard: map confirmation must go through cash prompt before commit")

    if 'ReadMapSelectionViewBool(mapView, "confirmButtonClicked")' in map_selection:
        return fail("ZombieModeCashAndOriginalExtractionGuard: map confirmation must not poll original MapSelectionView confirm state")

    for snippet in [
        "private readonly List<GameObject> zombieModeDisabledOriginalExtractionObjects",
        "private readonly Dictionary<int, bool> zombieModeOriginalExtractionActiveStateByObjectId",
        "DisableZombieModeOriginalExtractionPoints(runId);",
        "OriginalExtractionPointIsolationHelper.Disable(",
        "ShouldSkipZombieModeOriginalExtractionArea",
        "zombieModeRunState.ActiveExtractionArea",
        "zombieModeRunState.MapProfile.DisabledExtractionAreaIds",
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.MapIsolation",
        "OriginalExtractionPointIsolationHelper.Restore(",
        "RestoreZombieModeOriginalExtractionPoints();",
    ]:
        result = require(map_isolation, snippet, "original extraction isolation")
        if result:
            return result

    for snippet in [
        "internal static class OriginalExtractionPointIsolationHelper",
        "private static readonly BindingFlags InstanceBindingFlags",
        "private static readonly string[] ExitCreatorAreaMemberNames",
        "internal static int Disable(",
        "internal static int Restore(",
        "TryCollectFromExitCreator",
        "CollectFromFallbackSceneScan",
        "LevelManager.Instance",
        "levelManager.ExitCreator",
        "CountDownArea[] areas = UnityEngine.Object.FindObjectsOfType<CountDownArea>(true)",
        "IsLikelyOriginalExtractionArea",
    ]:
        result = require(extraction_helper, snippet, "shared original extraction isolation helper")
        if result:
            return result

    if "public int[] DisabledExtractionAreaIds = new int[0];" not in models:
        return fail("ZombieModeCashAndOriginalExtractionGuard: map profile must expose disabled extraction ids")

    print("ZombieModeCashAndOriginalExtractionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
