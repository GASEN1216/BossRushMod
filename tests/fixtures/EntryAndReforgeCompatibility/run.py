"""Execute verbatim entry callbacks and reforge calculation against deterministic hosts."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/EntryAndReforgeCompatibility"
HASHES = []


def extract(path, signature):
    source = (ROOT / path).read_text(encoding="utf-8-sig")
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    code = source[start:end]
    HASHES.append(dict(path=path, signature=signature, sha256=hashlib.sha256(code.encode()).hexdigest()))
    return code


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    core = "Integration/Reforge/ReforgeSystem.cs"
    apply = "Integration/Reforge/ReforgeSystem_ApplyAndResults.cs"
    maps = "MapSelection/BossRushMapSelectionHelper.cs"
    code = ["using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.SceneManagement; using Duckov.Scenes; using Duckov.UI; using UnityEngine.EventSystems; using ItemStatsSystem; using ItemStatsSystem.Stats; namespace BossRush { public static partial class ReforgeSystem {"]
    source = (ROOT / core).read_text(encoding="utf-8-sig")
    code += re.findall(r"(?:public|private) const float \w+ = [^;]+;", source)
    code += [extract(core, sig) for sig in (
        "public static ReforgeResult Reforge(", "private static bool IsIntegerProperty(",
        "public static void GetPropertyValueBounds(", "public static int RollSign(",
        "public static float RollMagnitude(", "public static bool IsValueAtUpperBound(",
        "public static bool IsValueAtLowerBound(")]
    code += [extract(apply, sig) for sig in (
        "public static int GetBeneficialValueDirection(", "public static bool IsBeneficialChange(",
        "private class ReforgeableProperty")]
    code += ["}", extract(apply, "public class ReforgeResult"), extract(apply, "public class ModifiedStatInfo")]
    code += ["public static partial class ReforgeUIManager {", extract(
        "Integration/Reforge/ReforgeUIManager_Feel.cs", "private struct ReforgeRevealRow")]
    code += [extract("Integration/Reforge/ReforgeUIManager_Feel.cs", "private static void QueueReforgeReveal(")]
    code += [extract("Integration/Reforge/ReforgeUIManager_ComparisonAndState.cs", "private static string GetReforgeBoundLabelMarkup(")]
    code += ["} public static partial class BossRushMapSelectionHelper {"]
    code += [extract(maps, sig) for sig in ("public static string GetPendingTargetSubSceneName(",
        "public static string GetPendingMainSceneName(", "internal static bool IsPendingTargetScene(",
        "internal static bool ShouldIgnoreAuxiliarySceneLoad(",
        "private enum BossRushEntryFlowSource", "internal static void CaptureInitialSpawnSelection(",
        "internal static void ConfirmInitialSpawnSelection(", "internal static BossRushMapConfig TakeInitialSpawnSelection(",
        "internal static void CancelUnstartedMapSelection(", "public static void ClearPendingMapEntry(",
        "public static void ClearPendingEntryFlowState(",
        "public static void MarkTargetSceneLoadStarted(",
        "public static void MarkEntryFlowFromMapSelectionUi(BossRushPendingEntryKind",
        "public static void MarkEntryFlowFromDirectTeleport(BossRushPendingEntryKind")]
    code += ["} internal static partial class BossRushMapEntrySelectionPatch {", extract(maps, "internal static void RecordSelection(MapSelectionEntry"), "}"]
    code += ["public partial class BossRushMapEntryClickHandler {", extract(maps, "public void OnPointerClick(PointerEventData"), "}"]
    code += ["public partial class ModBehaviour {", extract("ModBehaviour.cs", "private void OnSceneLoaded(Scene scene, LoadSceneMode mode)")]
    code += [extract("WavesArena/BossRushEntryFlow.cs", sig) for sig in ("private enum BossRushEntryMode", "internal bool UsesBossRushInitialSpawn(")]
    code += ["}}"]
    generated = OUT / "Extracted.cs"
    generated.write_text("\n".join(code), encoding="utf-8")
    (OUT / "source-hashes.json").write_text(json.dumps(HASHES, indent=2), encoding="utf-8")
    spawn_source = (ROOT / "MapSelection/BossRushInitialSpawn.cs").read_text(encoding="utf-8-sig")
    HASHES.append(dict(path="MapSelection/BossRushInitialSpawn.cs", sha256=hashlib.sha256(spawn_source.encode()).hexdigest()))
    # Task replaces only the async return/parameter type; all production method bodies are unchanged.
    spawn = OUT / "InitialSpawn.cs"
    spawn.write_text(spawn_source.replace("using Cysharp.Threading.Tasks;", "using System.Threading.Tasks;").replace("UniTask<bool>", "Task<bool>"), encoding="utf-8")
    (OUT / "source-hashes.json").write_text(json.dumps(HASHES, indent=2), encoding="utf-8")
    files = [generated, spawn, HERE / "Stubs.cs", HERE / "Program.cs", HERE / "InitialSpawnRegression.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>'
    project += "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project += "</ItemGroup></Project>"
    (OUT / "Regression.csproj").write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(OUT / "Regression.csproj"), "-c", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
