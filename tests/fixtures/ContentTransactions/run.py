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
    extracted = "using System; using System.Collections.Generic; namespace BossRush { internal static partial class DailyReportService {\n"
    for signature in ("internal static void TryRedeliverPendingBountyReward()", "private static bool Persist(DailyReportData data)", "internal static long GetPendingBountyCash(DailyReportData data)"):
        extracted += method("Integration/DailyReport/DailyReportService.cs", signature) + "\n"
    extracted += "}\ninternal static partial class DailyReportPersistence {\n"
    # 2026-09-06 (D-2): the collect callback lives in the shared BossRushSlotJsonStore; the daily
    # facade only contributes BeforeCollectSaveData(). Extract that hook and rebuild the callback
    # shape the store executes (hook -> FlushPending) so the ordering assertion still runs.
    extracted += method("Integration/DailyReport/DailyReportPersistence.cs", "private static bool BeforeCollectSaveData()") + "\n"
    extracted += "private static void HandleCollectSaveData() { try { if (!BeforeCollectSaveData()) return; FlushPending(); } catch (Exception) { } }\n"
    extracted += "}\ninternal static partial class DailyReportRewards {\n"
    extracted += method("Integration/DailyReport/DailyReportRewards.cs", "internal static bool TryGrantBountyCash(long amount, out string failureReason)")
    extracted += "}\n"
    # 远征入口执行生产页面方法；卡片渲染适配器只记录有没有挂上派遣入口。
    # 2026-09-24 交互重排：页面快照多了分区与巢页两栏的数据类，一起抽出来原样编译
    for signature in ("internal sealed class PetNestPageContent", "internal enum PetNestSectionLayout",
                      "internal sealed class PetNestSection", "internal sealed class PetNestCardData",
                      "internal sealed class PetNestNestView", "internal sealed class PetNestDetailData",
                      "internal sealed class PetNestActionData"):
        extracted += method("PetNest/PetNestUIPages.cs", signature) + "\n"
    extracted += "internal static partial class PetNestUIPages {\n"
    extracted += method("PetNest/PetNestUIPages.cs", "internal static PetNestPageContent BuildExpeditionPage(")
    extracted += "}}"
    (OUT / "Extracted.cs").write_text(extracted, encoding="utf-8")
    linked = [
        "Campaign/CampaignProgressService.cs", "Campaign/CampaignModels.cs",
        "Campaign/CampaignPersistence.cs", "Campaign/CampaignSaveCoordinator.cs",
        "Integration/DailyReport/DailyReportSaveCoordinator.cs",
        "Integration/BackMountain/RaidMealUsageBehavior.cs",
        "Integration/BackMountain/ShowcaseService.cs",
        "Integration/BackMountain/ShowcaseDisplayJudges.cs",
        "Common/Lifecycle/BossRushSaveFileThrottle.cs",
        "Common/Lifecycle/BossRushSaveCoordinatorEngine.cs",
        "Common/Lifecycle/BossRushSlotJsonStore.cs",
        "Common/Data/BossRushJsonValue.cs",
        "Utilities/SimpleJsonHelper.cs",
    ] + ["PetNest/" + name + ".cs" for name in (
        "PetNestService", "PetNestModels", "PetNestTuning", "PetNestPersistenceCodec",
        "PetNestPersistence", "PetNestSaveCoordinator", "PetNestHatchService", "PetNestMuseumStats",
        "PetNestExpeditionService",
        # 2026-09-20：孵化 roll 与显示名都要用炫彩调色板（纯数据、无 Unity 依赖）
        "PetNestChroma")]
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
