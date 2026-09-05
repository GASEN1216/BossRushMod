#!/usr/bin/env python3
"""Compile current production transaction code against in-memory game adapters."""
from pathlib import Path
import hashlib
import subprocess
import xml.sax.saxutils as xml

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "content-transactions"


def method(path, signature):
    source = (ROOT / path).read_text(encoding="utf-8-sig")
    start = source.index(signature)
    # Methods selected here contain no unmatched braces in comments/strings.
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    extracted = "using System; namespace BossRush { internal static partial class DailyReportService {\n"
    for signature in ("internal static void TryRedeliverPendingBountyReward()", "private static bool Persist(DailyReportData data)"):
        extracted += method("Integration/DailyReport/DailyReportService.cs", signature) + "\n"
    extracted += "}\ninternal static partial class DailyReportPersistence {\n"
    extracted += method("Integration/DailyReport/DailyReportPersistence.cs", "private static void HandleCollectSaveData()")
    extracted += "}\ninternal static partial class DailyReportRewards {\n"
    extracted += method("Integration/DailyReport/DailyReportRewards.cs", "internal static bool TryGrantBountyCash(long amount, out string failureReason)")
    extracted += "}}"
    (OUT / "Extracted.cs").write_text(extracted, encoding="utf-8")
    linked = [
        "Campaign/CampaignProgressService.cs", "Campaign/CampaignModels.cs",
        "Campaign/CampaignPersistence.cs", "Campaign/CampaignSaveCoordinator.cs",
        "Integration/DailyReport/DailyReportSaveCoordinator.cs",
        "Integration/BackMountain/RaidMealUsageBehavior.cs",
        "Common/Lifecycle/BossRushSaveFileThrottle.cs",
        "Utilities/SimpleJsonHelper.cs",
    ] + ["PetNest/" + name + ".cs" for name in (
        "PetNestService", "PetNestModels", "PetNestTuning", "PetNestPersistenceCodec",
        "PetNestJson", "PetNestPersistence", "PetNestSaveCoordinator", "PetNestHatchService", "PetNestMuseumStats")]
    paths = [ROOT / p for p in linked] + [HERE / "Program.cs", HERE / "Stubs.cs", OUT / "Extracted.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0067</NoWarn></PropertyGroup><ItemGroup>'
    project += "".join('<Compile Include="' + xml.escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    project += "</ItemGroup></Project>"
    (OUT / "ContentTransactions.csproj").write_text(project, encoding="utf-8")
    (OUT / "source-hashes.txt").write_text("\n".join(
        hashlib.sha256(p.read_bytes()).hexdigest() + " " + str(p.relative_to(ROOT))
        for p in paths if p != OUT / "Extracted.cs") + "\nextracted " + hashlib.sha256(extracted.encode()).hexdigest(), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(OUT / "ContentTransactions.csproj"), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
