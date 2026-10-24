"""Execute production Zombie wave/death/beacon flow against controlled host failures."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ZombieModeWaveRuntime"


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
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
    tuning = read("ZombieMode/ZombieModeTuning.cs")
    waves = read("ZombieMode/ZombieModeRuntimeModule_WaveController.cs")
    extraction = read("ZombieMode/ZombieModeRuntimeModule_Extraction.cs")
    runtime = read("ZombieMode/ZombieModeRuntimeModule.cs")
    source = "using System; using System.Collections; using UnityEngine; using UnityEngine.SceneManagement; using Duckov.UI; using Cysharp.Threading.Tasks;\nnamespace BossRush {\n"
    for name in ("ZombieModeLifecyclePhase", "ZombieModeCombatPhase", "ZombieModeEnemyKind", "ZombieModeRunOnlyObjectKind"):
        source += member(models, "public enum " + name) + "\n"
    source += member(tuning, "public static class ZombieModePhaseGuards") + "\n"
    settlement_constants = [line.strip() for line in tuning.splitlines()
                            if line.strip().startswith("public const float SettlementMaxWaitSeconds = Pacing.")]
    assert len(settlement_constants) == 1, settlement_constants
    source += "public static class ZombieModeTuning {\n" + member(tuning, "private static class Pacing")
    source += "\n" + settlement_constants[0] + "\n}\n"
    source += "internal sealed partial class ZombieModeRuntimeModule {\n"
    fields = [line.strip() for line in waves.splitlines() if line.strip().startswith((
        "private float zombieModeLastWaveFailureWarningTime", "private bool zombieModeSettlementRunning",
        "private int zombieModeSettlementRunId", "private int zombieModeSettlementWave",
        "private float zombieModeSettlementRetryTime"))]
    assert len(fields) == 5, fields
    beacon_fields = [line.strip() for line in extraction.splitlines()
                     if line.strip().startswith("private int zombieModeBeaconOperationId;")]
    assert len(beacon_fields) == 1, beacon_fields
    source += "\n".join(fields + beacon_fields) + "\n"
    for signature in (
        "private void LogZombieModeWaveFailure(",
        "private void RunZombieModeWaveSideEffect(",
        "internal void RegisterZombieModeEventListeners(",
        "private void UnregisterZombieModeEventListeners(",
        "private void HandleZombieModeHealthDead(",
        "internal void TickZombieModeWaveController(",
        "internal void StartZombieModeWave(",
        "private void CompleteZombieModeWave(",
        "private void TryStartZombieModeSettlement(",
        "private IEnumerator ZombieModeSettlementCoroutine(",
        "internal static bool IsZombieModeBossWave(",
    ):
        source += member(waves, signature) + "\n"
    for signature in (
        "private bool IsZombieModeActive",
        "internal bool AnyZombieModeSafeZoneActive",
        "internal bool CanUseZombieModeBeacon(",
        "internal string GetZombieModeBeaconUnavailableReasonKey(",
        "internal bool TryUseZombieModeBeacon(",
        "private bool IsZombieModeBeaconOperationCurrent(",
        "private IEnumerator ZombieModeBeaconChannelCoroutine(",
    ):
        source += member(extraction, signature) + "\n"
    source += member(runtime, "internal bool IsZombieModeRunValid(") + "\n"
    source += "}}\n"
    generated = OUT / "Production.cs"
    generated.write_text(source, encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    sources = (generated, HERE / "Program.cs", HERE / "Stubs.cs")
    includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
