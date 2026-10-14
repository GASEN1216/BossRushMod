"""Execute production co-op cohort and personal payout methods against in-memory adapters."""
from pathlib import Path
import hashlib
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/ZombieCoopFlow"
OUT.mkdir(parents=True, exist_ok=True)


def block(path, signature):
    source = (ROOT / path).read_text(encoding="utf-8-sig")
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


coop = "ZombieMode/ZombieModeRuntimeModule_Coop.cs"
selection = "ZombieMode/ZombieModeRewardOptionCore.cs"
extraction = "ZombieMode/ZombieModeRuntimeModule_Extraction.cs"
wave = "ZombieMode/ZombieModeRuntimeModule_WaveController.cs"
models = "ZombieMode/ZombieModeModels.cs"
generated = "using System; using System.Collections.Generic; using UnityEngine; using Duckov.Economy; using Duckov.UI; namespace BossRush {\n"
generated += block(models, "public enum ZombieModeRewardType") + "\n"
generated += block(coop, "internal sealed class CoopZombiePersonal") + "\n"
generated += "internal sealed partial class ZombieModeRuntimeModule {\n"
generated += block(coop, "private sealed class CoopZombieRefund") + "\n"
generated += block(coop, "private sealed class CoopZombieWallet") + "\n"
for path, signature in (
    (coop, "private void RetireLostCoopZombieProjection("),
    (coop, "private bool IsCoopZombieLocalAssetCurrent("),
    (coop, "private static string CoopZombieParticipantKey("),
    (coop, "private bool IsCoopZombieParticipant("),
    (coop, "private bool CoopZombieAnyParticipantAlive("),
    (coop, "private bool QueueCoopZombieWrongAssetRefund("),
    (coop, "private void TickCoopZombieRefunds("),
    (coop, "internal void RememberCoopZombieWallet("),
    (coop, "private void ResetCoopZombieRun("),
    (coop, "private void DeliverCoopZombiePoints("),
    (coop, "private bool CoopZombieEveryoneReady("),
    (coop, "private bool CoopZombieAllInsideExtraction("),
    (coop, "private bool CoopZombieAllPlayersInsideSafeZone("),
    (coop, "private int GetCoopZombieRewardPollution("),
    (coop, "private bool ApplyCoopZombiePersonal("),
    (coop, "public override void OnUpdate("),
    (selection, "private int GetZombieModeOptionTradeoffPollutionGain("),
    (extraction, "private bool SettleZombieModeExtractionCashShell("),
    (extraction, "private void CompleteZombieModeExtractionSuccess("),
    (wave, "internal static bool IsZombieModeBossWave("),
):
    generated += block(path, signature) + "\n"
generated += "}}"
(OUT / "Production.cs").write_text(generated, encoding="utf-8")
(OUT / "source-hashes.txt").write_text("\n".join(
    hashlib.sha256((ROOT / path).read_bytes()).hexdigest() + " " + path
    for path in (coop, selection, extraction, wave, models)), encoding="utf-8")
sources = [HERE / "Program.cs", OUT / "Production.cs", ROOT / "Common/Coop/BossRushCoopJson.cs", ROOT / "Common/Data/BossRushJsonValue.cs", ROOT / "Utilities/SimpleJsonHelper.cs"]
includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture
code, output = run_project_fixture(project, OUT / "runs", ROOT)
print(output)
raise SystemExit(code)
