"""天空岛共享命令/个人收据：抽取生产方法，网络与物品宿主边界使用可控替身。"""
from pathlib import Path
import hashlib
import subprocess
import json
import tempfile
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUTPUT_ROOT = ROOT / "Build/runtime-regressions/SkyIslandCoopCommands"


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for end in range(opening, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return source[start:end + 1]
    raise AssertionError(signature)


def main():
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    OUT = Path(tempfile.mkdtemp(prefix="run-",dir=OUTPUT_ROOT)).resolve()
    path = ROOT / "SkyIsland/SkyIslandGnatsCoop.cs"
    source = path.read_text(encoding="utf-8-sig")
    extracted = "using System; using System.Collections.Generic; using ItemStatsSystem; using UnityEngine;\nnamespace BossRush { internal sealed partial class SkyIslandGnats {\n"
    for signature in ("private sealed class EnvironmentState", "private sealed class UseRequest", "private sealed class PersonalEvent",
                      "internal bool QueueCoopUse(", "private void ReceiveCoopUse(", "private bool ApplyCoopPersonal(",
                      "private void TickCoopUse(", "internal void OnCoopGnatKilled(", "private void ReportCoopBite("):
        extracted += member(source, signature) + "\n"
    extracted += "} }\n"
    production = OUT / "ProductionExtracted.cs"
    production.write_text(extracted, encoding="utf-8")
    (OUT / "source-sha256.txt").write_text(str(path) + " " + hashlib.sha256(path.read_bytes()).hexdigest() + "\n", encoding="utf-8")
    sky=ROOT/"SkyIsland"
    world=(sky/"SkyIslandCoopWorldEffects.cs").read_text(encoding="utf-8-sig")
    extra="using System; using System.Collections; using System.Collections.Generic; using UnityEngine; using Eflatun.SceneReference;\nnamespace BossRush { internal sealed partial class SkyIslandCoopWorldEffects {\n"
    for signature in ("private sealed class World {", "private sealed class Slow", "private sealed class PlayerEffects {", "private sealed class PlayerEffectsState", "private sealed class LocalSlow",
                      "private string TravelId", "internal static bool WorldEarmuffs", "internal static int WorldRootweavePieces", "private void EnsureLocalStream(", "private void OnLocalShot(",
                      "private void PublishPlayerEffects(", "private void ReceivePlayerEffects(", "private void ApplyPlayerEffects(",
                      "internal static void SendSlow(", "private void ReceiveSlow(", "private void TickSlows(", "private void ReceiveWorld("):
        extra+=member(world,signature)+"\n"
    extra+="}\n"+member((sky/"SkyIslandBossProps.cs").read_text(encoding="utf-8-sig"),"internal sealed class SkyIslandPlayerSlow")+"\n"
    extra+="internal static partial class SkyIslandRewardCrate {\n"+member((sky/"SkyIslandRewardCrate.cs").read_text(encoding="utf-8-sig"),"private static int RootweavePiecesFor(")+"}\n"
    session_coop=(sky/"SkyIslandSessionCoop.cs").read_text(encoding="utf-8-sig")
    extra+="internal sealed partial class SkyIslandSession {\nprivate bool coopScopeConnected,coopScopeHost,coopScopeLost; private string coopScopeRun;\n"
    for signature in ("private IEnumerator WaitForCoopScene(","private void CaptureCoopSessionScope()","private bool IsCoopSessionScopeValid()","private void OnCoopSessionChanged()"):
        extra+=member(session_coop,signature)+"\n"
    extra+="}\n"
    extra+="internal static partial class SkyIslandSceneReferenceBridge {\n"+member((sky/"SkyIslandSceneReferenceBridge.cs").read_text(encoding="utf-8-sig"),"private static void CoopArrivalPrefix(")+"}\n"
    moves=(sky/"SkyIslandChampionMoves.cs").read_text(encoding="utf-8-sig")
    bell=moves[moves.index("internal sealed class SkyIslandBellEngineMoves"):]
    extra+="internal sealed partial class SkyIslandBellEngineMoves {\n"
    for name in ("NearRadius","FarInner","FarOuter","FarDamage","FarHitRadius","TollSlow","TollSlowSeconds","SlowHeightTolerance"):
        import re
        constant=re.findall(r'(?:internal|private) const float '+name+r' = [^;]+;',bell)
        assert len(constant)==1,name
        extra+=constant[0]+"\n"
    for signature in ("private void StrikeFar(","private static bool Caught(","private static float FlatDistance("):
        extra+=member(bell,signature)+"\n"
    extra+="}\ninternal static partial class SkyIslandBossRules {\n"
    rules=(sky/"SkyIslandBossRules.cs").read_text(encoding="utf-8-sig")
    for name in ("CaveCenterX","CaveCenterZ","CaveRange"):
        constant=re.findall(r'internal const float '+name+r' = [^;]+;',rules)
        assert len(constant)==1,name
        extra+=constant[0]+"\n"
    extra+=member(rules,"internal static bool InCave(")+"}\n}\n"
    world_production=OUT/"WorldProduction.cs"
    world_production.write_text(extra,encoding="utf-8")
    codec_sources=[ROOT/name for name in ("Common/Coop/BossRushCoopJson.cs","Common/Data/BossRushJsonValue.cs","Utilities/SimpleJsonHelper.cs")]
    world_sources=[sky/name for name in ("SkyIslandCoopRuntime.cs","SkyIslandCoopWorldEffects.cs","SkyIslandBossProps.cs","SkyIslandSessionCoop.cs","SkyIslandSceneReferenceBridge.cs","SkyIslandChampionMoves.cs","SkyIslandBossRules.cs","SkyIslandRewardCrate.cs")]+codec_sources
    (OUT/"production-sha256.json").write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in world_sources},indent=2),encoding="utf-8")
    sources = [HERE / "Stubs.cs", HERE / "Program.cs", HERE/"WorldStubs.cs", HERE/"WorldRegression.cs",production,world_production,sky/"SkyIslandCoopRuntime.cs"]+codec_sources
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project += '</ItemGroup></Project>'
    target = OUT / "Regression.csproj"
    target.write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(target), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
