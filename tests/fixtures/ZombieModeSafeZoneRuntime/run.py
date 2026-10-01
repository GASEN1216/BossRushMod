"""Run production safe-zone and spatial queries with controllable Unity adapters."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ZombieModeSafeZoneRuntime"


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def read(relative):
        raw = (ROOT / relative).read_bytes()
        hashes[relative] = hashlib.sha256(raw).hexdigest()
        return raw.decode("utf-8-sig")

    models = read("ZombieMode/ZombieModeModels.cs")
    runtime = read("ZombieMode/ZombieModeRuntimeModule.cs")
    spatial = read("ZombieMode/ZombieModeRuntimeModule_EnemyRuntime.cs")
    extraction = read("ZombieMode/ZombieModeRuntimeModule_Extraction.cs")
    drops = read("ZombieMode/ZombieModeDropsAndPerformance.cs")
    source = "using System; using System.Collections.Generic; using ItemStatsSystem; using UnityEngine; using UnityEngine.SceneManagement;\nnamespace BossRush {\n"
    for name in ("ZombieModeLifecyclePhase", "ZombieModeCombatPhase", "ZombieModeRunOnlyObjectKind", "ZombieModeBossKind"):
        source += member(models, "public enum " + name) + "\n"
    source += member(models, "public sealed class ZombieModeRunOnlyRecord") + "\n"
    source += member(models, "public sealed class ZombieModeDropCandidate") + "\n"
    source += "internal sealed partial class ZombieModeRuntimeModule {\n"
    for signature in ("internal bool IsZombieModeRunValid(", "internal void RegisterZombieModeRunOnlyObject(", "internal void PruneZombieModeRunOnlyEnemyRecords(",
                      "internal void RemoveZombieModeRunOnlyObjectRecord("):
        source += member(runtime, signature) + "\n"
    source += member(drops, "internal void ReleaseZombieModeOwnedDropCandidates()") + "\n"
    source += member(extraction, "internal bool AnyZombieModeSafeZoneActive") + "\n"
    for field in ("private readonly HashSet<int> zombieModeEnemyInstanceIds", "private readonly Dictionary<int, ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkersByInstanceId", "private readonly List<ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkerScratch"):
        start = spatial.index(field)
        source += spatial[start:spatial.index(";", start) + 1] + "\n"
    for signature in ("internal bool IsZombieModeKnownEnemy(",
                      "internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)",
                      "internal void UnregisterZombieModeEnemyInstanceId(",
                      "internal int CollectZombieModeRuntimeEnemyMarkers(",
                      "internal static AICharacterController GetZombieModeEnemyAI(",
                      "internal static void SetZombieModeEnemyTargetToMainPlayer(",
                      "internal void RefreshZombieModeGravityWellTargets(",
                      "internal CharacterMainControl TryFindZombieModeNearestEnemyTarget("):
        source += member(spatial, signature) + "\n"
    source += "}}\n"
    generated = OUT / "Production.cs"
    generated.write_text(source, encoding="utf-8")
    full_sources = [ROOT / "ZombieMode/ZombieModeSafeZoneController.cs", ROOT / "ZombieMode/ZombieModeTuning.cs"]
    for path in full_sources:
        read(path.relative_to(ROOT).as_posix())
    sources = [generated, *full_sources, HERE / "Program.cs", HERE / "Stubs.cs"]
    includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
