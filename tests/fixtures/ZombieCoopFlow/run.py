"""Execute production co-op cohort and personal payout methods against in-memory adapters."""
from pathlib import Path
import hashlib
import subprocess
import sys
import re
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
import argparse
parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path, default=ROOT / "Build/runtime-regressions/ZombieCoopFlow")
OUT = parser.parse_args().output.resolve()
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
for dto in ("CoopZombieEnemy", "CoopZombieTerminal", "CoopZombieWorld"):
    generated += block(coop, "internal sealed class " + dto) + "\n"
generated += block("Utilities/CoopGameplayChannel.cs", "internal sealed class CoopGameplayCommand") + "\n"
generated += "internal sealed partial class ZombieModeRuntimeModule {\n"
generated += block(coop, "private sealed class CoopZombieRefund") + "\n"
generated += block(coop, "private sealed class CoopZombieExtractionEligibility") + "\n"
generated += block(coop, "private sealed class CoopZombieWallet") + "\n"
for path, signature in (
    (coop, "private void RetireLostCoopZombieProjection("),
    (coop, "private bool IsCoopZombieLocalAssetCurrent("),
    (coop, "private static string CoopZombieParticipantKey("),
    (coop, "private bool IsCoopZombieParticipant("),
    (coop, "private void RefreshCoopZombieExtractionEligibility("),
    (coop, "private bool ApplyCoopZombieAdmissionState("),
    (coop, "private static bool CoopZombieContainsPlayer("),
    (coop, "private static void NotifyCoopZombieSkippedEntry("),
    (coop, "private bool CanOfferCoopZombieStart("),
    (coop, "private void ShowCoopZombieStartConfirmation("),
    (coop, "private void RetireCoopZombieUnconfirmedEntry("),
    (coop, "private bool ApplyCoopZombieSkippedEntry("),
    (coop, "private void StartCoopZombieWithConfirmedPlayers("),
    (coop, "private void QueueCoopZombieSkippedRefunds("),
    (coop, "private bool RegisterCoopZombieStarter("),
    (coop, "private void CommitCoopZombieStarterIfConfirmed("),
    ("ZombieMode/ZombieModeEntry_StarterLoadout.cs", "public void SelectZombieModeStarterLoadout("),
    ("ZombieMode/ZombieModeRuntimeModule.cs", "internal void FinalizeZombieModeEntryResources("),
    (coop, "private bool IsCoopZombieExtractionRecipient("),
    (coop, "internal bool TryGetCoopCampaignParticipantAsset("),
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
    (coop, "private void SendCoopZombieCommand("),
    (coop, "private void ApplyCoopZombieStarterConfirmation("),
    (selection, "private int GetZombieModeOptionTradeoffPollutionGain("),
    (extraction, "private bool SettleZombieModeExtractionCashShell("),
    (extraction, "private void CompleteZombieModeExtractionSuccess("),
    (wave, "internal static bool IsZombieModeBossWave("),
):
    if path != extraction:
        generated += block(path, signature) + "\n"
generated += "} internal sealed partial class ZombieModeStarterChoiceView {" + block("ZombieMode/ZombieModeEntry_StarterLoadout.cs", "private bool KeepCoopHostStartChoice(") + "}}"
(OUT / "Production.cs").write_text(generated, encoding="utf-8")
# Compile this partial with its own production using declarations. A shared
# generated header used to hide a missing Collections.Generic in this file.
extraction_header = (ROOT / extraction).read_text(encoding="utf-8-sig").split("namespace BossRush", 1)[0]
extraction_source = extraction_header + "namespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n"
for signature in ("private bool SettleZombieModeExtractionCashShell(", "private void CompleteZombieModeExtractionSuccess("):
    extraction_source += block(extraction, signature) + "\n"
(OUT / "Extraction.cs").write_text(extraction_source + "}}", encoding="utf-8")
starter = "ZombieMode/ZombieModeEntry_StarterLoadout.cs"
starter_header = (ROOT / starter).read_text(encoding="utf-8-sig").split("namespace BossRush", 1)[0]
waiting = starter_header + "namespace BossRush { internal sealed partial class ZombieModeStarterChoiceView {\n"
waiting += block(starter, "private void BuildCoopHostWaiting(") + "} internal static partial class BossRushUIDepth {\n"
waiting += block("Common/UI/BossRushUIFeel.cs", "internal static void ApplyButtonDepth(") + "}}"
(OUT / "WaitingUi.cs").write_text(waiting, encoding="utf-8")
localization = "Localization/LocalizationInjector.cs"
localized_calls = re.findall(r'InjectZombieModeString\("BossRush_ZombieMode_Coop_[^"\n]+",[^\n]+;', (ROOT / localization).read_text(encoding="utf-8-sig"))
assert len(localized_calls) == 8, "all actual co-op waiting/refund texts must have bilingual injection"
(OUT / "Localization.cs").write_text('namespace BossRush { internal static class ZombieCoopLocalization { internal static void Register(){' + '\n'.join(localized_calls) + '} private static void InjectZombieModeString(string key,string cn,string en){L10n.Register(key,cn,en);} }}', encoding="utf-8")
(OUT / "source-hashes.txt").write_text("\n".join(
    hashlib.sha256((ROOT / path).read_bytes()).hexdigest() + " " + path
    for path in (coop, selection, extraction, wave, models, starter, localization, "ZombieMode/ZombieModeRuntimeModule.cs", "Utilities/CoopGameplayChannel.cs", "Common/UI/BossRushUIFeel.cs")), encoding="utf-8")
sources = [HERE / "Program.cs", HERE / "WaitingUiBoundaries.cs", OUT / "Production.cs", OUT / "Extraction.cs", OUT / "WaitingUi.cs", OUT / "Localization.cs", ROOT / "Common/Coop/BossRushCoopJson.cs", ROOT / "Common/Data/BossRushJsonValue.cs", ROOT / "Utilities/SimpleJsonHelper.cs"]
includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture
code, output = run_project_fixture(project, OUT / "runs", ROOT)
print(output)
if code == 0:
    sys.path.insert(0, str(ROOT / "tests/fixtures/ModeFCoop"))
    from delivery_chain import run as run_delivery_chain
    code, output = run_delivery_chain(ROOT, HERE, OUT, OUT / "Production.cs", zombie=True)
    print(output)
raise SystemExit(code)
