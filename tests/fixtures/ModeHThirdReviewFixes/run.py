"""执行真实 Mode H 押品编排/结算页及提取的恢复方法；仅隔离 Unity、库存与磁盘边界。"""
from pathlib import Path
import hashlib
import os
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "modeh-third-review-fixes"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = []

    def method(file, signature):
        source = (ROOT / "ModeH" / file).read_text(encoding="utf-8-sig")
        start = source.index(signature)
        end = source.index("{", start) + 1
        depth = 1
        while depth:
            depth += (source[end] == "{") - (source[end] == "}")
            end += 1
        code = source[start:end]
        hashes.append(file + " | " + signature + " | " + hashlib.sha256(code.encode()).hexdigest())
        return code

    groups = {
        "ModeHRuntimeModule": [
            ("ModeHRuntimeModule.cs", "internal bool TryTransition("),
            ("ModeHRuntimeModule.cs", "internal void RequestRecovering("),
            ("ModeHRuntimeModule.cs", "internal void RequestErrorRecoveryPending("),
            ("ModeHRuntimeModule.cs", "internal void RequestSuspended("),
            ("ModeHRuntimeModule_MatchFlow.cs", "private void RequestTechnicalRetry("),
            ("ModeHRuntimeModule_MatchFlow.cs", "private void AbortMatchSpawning("),
            ("ModeHRuntimeModule_MatchFlow.cs", "private void DriveRecovery()"),
            ("ModeHRuntimeModule_MatchFlow.cs", "private ModeHLifecycle ResolveRecoveryResumeLifecycle()"),
            ("ModeHRuntimeModule_MatchFlow.cs", "private ModeHLifecycle DeriveResumeFromSeasonProgress()"),
            ("ModeHRuntimeModule_CombatFlow.cs", "private void RestoreMatchReservationAndSnapshot()"),
            ("ModeHRuntimeModule_CombatFlow.cs", "private void RemoveUnarchivedSettlementForCurrentMatch()"),
            ("ModeHRuntimeModule_UiFlow.cs", "private void ProjectRunStateIntoSeason()"),
            ("ModeHRuntimeModule_UiFlow.cs", "private bool TryPersistSeason(string reasonId)"),
            ("ModeHRuntimeModule_UiFlow.cs", "private bool TryPersistSeason(string reasonId, bool requireDurable)"),
            ("ModeHRuntimeModule_CombatProfiles.cs", "private ModeHMatchReportDto FindLatestPendingReport()"),
            ("ModeHRuntimeModule_CombatProfiles.cs", "private ModeHSeasonRewardOperationDto FindRewardOperation("),
            ("ModeHRuntimeModule_CombatProfiles.cs", "private ModeHProfileDto FindSeasonProfile("),
            ("ModeHRuntimeModule_CombatProfiles.cs", "private void ReplaceSeasonProfile("),
            ("ModeHRuntimeModule_CombatProfiles.cs", "private static ModeHProfileDto CloneProfile("),
        ],
        "ModeHSeasonRewardService": [("ModeHSeasonRewardService.cs", signature) for signature in (
            "public static bool TrySelectKit(", "public static bool TryDeclineToFame(",
            "public static bool TryArchive(", "private static void ApplyFameDisplay(",
            "private static ModeHSeasonRewardOperationDto FindByOperationId(", "private static ModeHProfileDto FindProfile(")],
        "ModeHInjuryAndScarSystem": [("ModeHInjuryAndScarSystem.cs", signature) for signature in (
            "public static bool TryAcceptScar(", "public static void DeclineScar(")],
        "ModeHWarehouseStakeJournal": [("ModeHWarehouseStakeJournal.cs", "public static bool IsTerminalPhase(")],
    }
    parts = ["using System; using System.Collections.Generic; namespace BossRush {"]
    for name, methods in groups.items():
        modifier = "" if name == "ModeHRuntimeModule" else "static "
        parts.append("internal " + modifier + "partial class " + name + " {")
        parts.extend(method(file, signature) for file, signature in methods)
        parts.append("}")
    parts.append("}")
    (OUT / "Extracted.cs").write_text("\n".join(parts), encoding="utf-8")
    linked = ["ModeHConfig", "ModeHStateModel", "ModeHStateDtos", "ModeHRunState", "ModeHStateMachine",
              "ModeHSeedStream", "ModeHVirtualStakeController", "ModeHRealStakeService",
              "ModeHRuntimeModule_SettlementFlow"]
    for name in linked:
        path = ROOT / "ModeH" / (name + ".cs")
        hashes.append("ModeH/" + name + ".cs | " + hashlib.sha256(path.read_bytes()).hexdigest())
    (OUT / "source-hashes.txt").write_text("\n".join(hashes) + "\n", encoding="utf-8")
    xml = ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
           '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
           '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn>'
           '</PropertyGroup><ItemGroup><Compile Include="Extracted.cs"/>')
    xml += '<Compile Include="' + (HERE / "Harness.cs").as_posix() + '"/>'
    xml += ''.join('<Compile Include="' + (ROOT / "ModeH" / (name + ".cs")).as_posix() + '"/>' for name in linked)
    xml += '</ItemGroup></Project>'
    (OUT / "Fixture.csproj").write_text(xml, encoding="utf-8")
    result = subprocess.run(["dotnet", "run", "--project", str(OUT / "Fixture.csproj"),
                             "--configuration", "Release", "--verbosity", "quiet"], cwd=ROOT,
                            capture_output=True, text=True, encoding="utf-8", errors="replace",
                            env=dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en-US"))
    output = result.stdout + result.stderr
    (OUT / "execution.log").write_text(output, encoding="utf-8")
    print(output, end="")
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
