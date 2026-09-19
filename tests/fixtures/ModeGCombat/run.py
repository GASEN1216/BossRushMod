"""Mode G 生产源码执行回归；不启动游戏，不读玩家存档。"""
from pathlib import Path
import hashlib
import subprocess
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/ModeGCombat"


def member(source, signature):
    # 与现有夹具同口径逐字抽取；所选成员的字符串/注释无非配对花括号。
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for i in range(opening, len(source)):
        depth += (source[i] == "{") - (source[i] == "}")
        if depth == 0:
            return source[start:i + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sources = {p.name: p.read_text(encoding="utf-8-sig") for p in (ROOT / "ModeG").glob("*.cs")}
    generated = "using System; using System.Collections.Generic; using UnityEngine;\nnamespace BossRush {\n"
    models = sources["ModeGStateModel.cs"]
    # 公共数据类型和阶段判据保留生产实现，省去全局宿主门控的 Unity 依赖。
    for name in ("ModeGLifecyclePhase", "ModeGCombatPhase", "ModeGBattleResult", "ModeGExitReason",
                 "ModeGCounterAxis", "ModeGSlotOutcome", "ModeGPlanVariant", "ModeGNemesisTemperament",
                 "ModeGNemesisSelectionSource"):
        generated += member(models, "public enum " + name) + "\n"
    generated += member(models, "public static class ModeGPhaseGuards") + "\n"
    materializer_source = ROOT / "LootAndRewards/VictoryRewardShadowCrateController.cs"
    generated += member(materializer_source.read_text(encoding="utf-8-sig"),
                        "public sealed class ModeGRewardStrictMaterializer") + "\n"
    generated += member(sources["ModeGCleanupController.cs"], "public static class ModeGLateCleanupSink") + "\n"
    generated += "public static partial class ModeGEncounterVariation {\n"
    generated += member(sources["ModeGEncounterVariation.cs"], "public static UnityEngine.Vector2[] GetSpawnOffsets(") + "\n}\n"
    generated += "public partial class ModBehaviour {\n"
    generated += member(sources["ModeGRuntimeBridge.cs"], "private static bool TrySelectModeGFormation(") + "\n"
    generated += member(sources["ModeGRuntimeBridge.cs"], "private static bool TrySelectModeGSeparatedPoints(") + "\n}\n"
    hud = sources["ModeGHUD.cs"]
    generated += member(hud, "internal enum ModeGObjectiveState") + "\n"
    generated += member(hud, "internal struct ModeGHudModel") + "\n"
    generated += "internal static partial class HudHarness {\n"
    for signature in ("private static string ComposeObjectiveLine(", "private static string FormatGate("):
        generated += member(hud, signature) + "\n"
    generated += "}\ninternal sealed partial class ModeGRuntimeModule {\n"
    runtime = sources["ModeGRuntimeModule.cs"]
    begin = runtime.index("        private int _totalBossKills;")
    end = runtime.index("        #endregion", begin)
    generated += runtime[begin:end]
    for signature in ("private void SettleCurrentWave()", "private void PrepareNextAmmoBanIfNeeded()",
                      "private void PublishAmmoBan(", "private int GetAmmoThreatSharePercent(",
                      "private static float AdvanceSpawnWait("):
        generated += member(runtime, signature) + "\n"
    api = sources["ModeGRuntimeModule_PublicApiAndShutdown.cs"]
    generated += member(api, "public ModeGContractProgress BuildContractProgress()") + "\n"
    generated += member(api, "private void FillObjective(") + "\n}\n}\n"
    extracted = OUT / "ProductionExtracted.cs"
    extracted.write_text(generated, encoding="utf-8")
    files = [ROOT / "ModeG" / name for name in (
        "ModeGCombatTelemetry.cs", "ModeGAdaptiveCombat.cs", "ModeGRunState.cs", "ModeGWavePlan.cs",
        "ModeGDeterministicRandom.cs", "ModeGAvailability.cs", "ModeGFateContract.cs",
        "ModeGProfilePersistence.cs", "ModeGNemesisPersistence.cs", "ModeGWeaponScoringCompatibilityMatrix.cs",
        "ModeGRewardTransaction.cs",
    )] + [HERE / "Stubs.cs", HERE / "Program.cs", extracted]
    (OUT / "source-sha256.txt").write_text("\n".join(
        name + " " + hashlib.sha256((ROOT / "ModeG" / name).read_bytes()).hexdigest()
        for name in sorted(sources)) + "\nLootAndRewards/VictoryRewardShadowCrateController.cs "
        + hashlib.sha256(materializer_source.read_bytes()).hexdigest(), encoding="utf-8")
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    project += '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    project += '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project += '</ItemGroup></Project>'
    path = OUT / "ModeGCombat.csproj"
    path.write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(path), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
