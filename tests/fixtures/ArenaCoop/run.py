"""Link the production Arena/Mode D room participation, personal facts and milestone delivery."""
from pathlib import Path
import hashlib
import subprocess
import sys
import argparse
from xml.sax.saxutils import escape
ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=ROOT / "Build/runtime-regressions/ArenaCoop")
OUT = parser.parse_args().output.resolve()
OUT.mkdir(parents=True, exist_ok=True)
# Reuse the executed protocol fixture's in-memory peer transport. Extend only engine/peer models.
network = (ROOT / "tests/fixtures/CoopProtocol/Stubs.cs").read_text(encoding="utf-8")
network = network.replace("internal static class Mathf", "internal static partial class Mathf")
network = network.replace("internal sealed class BossRushCoopPlayer", "internal sealed partial class BossRushCoopPlayer")
network = network.replace("internal sealed class TestPeer", "internal sealed partial class TestPeer")
network = network.replace("internal static class BossRushCoop", "internal static partial class BossRushCoop")
assert network.count("AssetId = peer.AssetId, ReadyEpoch") == 1
network = network.replace("AssetId = peer.AssetId, ReadyEpoch", "AssetId = peer.AssetId, Character = peer.Character, ReadyEpoch")
network = network.replace("UnityEngine.JsonUtility.", "BossRushCoopJson.")
network = network.replace("return !Current.Host;", "return IsConnected && !Current.Host;")
generated = OUT / "NetworkAdapters.cs"
generated.write_text(network, encoding="utf-8")
def member(source, signature):
    assert source.count(signature) == 1, signature
    start=source.index(signature)
    opening=source.index("{",start)
    depth=0
    for i in range(opening,len(source)):
        depth+=(source[i]=="{")-(source[i]=="}")
        if depth==0:return source[start:i+1]
    raise AssertionError(signature)

deaths_source = ROOT / "WavesArena/WavesArenaRuntimeModule_WaveDeaths.cs"
deaths = OUT / "ProductionDeaths.cs"
deaths.write_text("using System;using UnityEngine;namespace BossRush {internal sealed partial class WavesArenaRuntimeModule {\n"
                  + member(deaths_source.read_text(encoding="utf-8-sig"), "internal void HandleBossDeath(") + "\n}internal sealed partial class ModeDRuntimeModule {\n"
                  + member((ROOT/"ModeD/ModeDRuntimeModule_Lifecycle.cs").read_text(encoding="utf-8-sig"),"internal static bool TryStartModeD(") + "\n"
                  + member((ROOT/"ModeD/ModeDRuntimeModule_Lifecycle.cs").read_text(encoding="utf-8-sig"),"internal void StartModeD()")
                  + "\n}internal partial class ModBehaviour {\n"
                  + member((ROOT/"LootAndRewards/LootAndRewards.cs").read_text(encoding="utf-8-sig"),"private void OnPlayerDeathInBossRush_LootAndRewards(") + "\n}}", encoding="utf-8")
production = [ROOT / "WavesArena/WavesArenaRuntimeModule_Coop.cs", ROOT / "WavesArena/WavesArenaRuntimeModule_CoopRewards.cs", ROOT / "Utilities/InteractableLootboxInventoryHelper.cs", ROOT / "ModeD/ModeDRuntimeModule_Coop.cs", ROOT / "LootAndRewards/InfiniteHellMilestoneDelivery.cs", ROOT / "Utilities/CoopGameplayChannel.cs", ROOT / "Utilities/CoopPersonalDelivery.cs", ROOT / "Common/Coop/BossRushCoopJson.cs", ROOT / "Common/Data/BossRushJsonValue.cs", ROOT / "Utilities/SimpleJsonHelper.cs"]
boundary=OUT / "SceneBoundary.cs"
boundary.write_text((ROOT / "tests/fixtures/CoopSceneCoreBoundary.cs").read_text(encoding="utf-8").replace("internal sealed class MultiSceneCore", "internal sealed partial class MultiSceneCore"),encoding="utf-8")
paths = production + [generated, deaths, boundary, HERE / "Stubs.cs", HERE / "Rewards.cs", HERE / "RewardRegression.cs", HERE / "Program.cs"]
project = OUT / "Regression.csproj"
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
(OUT / "production-sha256.txt").write_text("\n".join(str(p) + " " + hashlib.sha256(p.read_bytes()).hexdigest() for p in production + [deaths_source,ROOT/"ModeD/ModeDRuntimeModule_Lifecycle.cs"]), encoding="utf-8")
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture
code, output = run_project_fixture(project, OUT / "runs", ROOT)
print(output)
if code == 0:
    code = subprocess.call([sys.executable, str(HERE / "NativeLoot/run.py"), "--output", str(OUT)], cwd=ROOT)
raise SystemExit(code)
