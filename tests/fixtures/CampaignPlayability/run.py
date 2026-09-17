"""Execute the production campaign catalog and objective collection without the game."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "campaign-playability"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sources = [ROOT / name for name in (
        "Campaign/CampaignModels.cs", "Campaign/CampaignTuning.cs",
        "Campaign/CampaignContentCatalog.cs", "Campaign/CampaignObjectiveTracker.cs",
        "Campaign/CampaignObjectiveCollector.cs", "Campaign/CampaignFacilityUnlocks.cs",
        "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs",
        "ModeH/ModeHCanonicalDigest.cs", "ModeH/ModeHSeedStream.cs",
    )] + [HERE / "Program.cs", HERE / "Stubs.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    project += '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    project += '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
    project += '</PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project += '</ItemGroup></Project>'
    path = OUT / "CampaignPlayability.csproj"
    path.write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(path), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
