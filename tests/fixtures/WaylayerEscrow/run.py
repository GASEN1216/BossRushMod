"""Extract production Waylayer escrow and inventory entry points; execute fresh target."""
from pathlib import Path
import hashlib
import json
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/WaylayerEscrow"
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    path = "SkyIsland/SkyIslandWaylayerChief.cs"
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    hashes = []
    def extract(signature):
        assert source.count(signature) == 1, signature
        start = source.index(signature)
        end = source.index("{", start) + 1
        depth = 1
        while depth:
            depth += (source[end] == "{") - (source[end] == "}")
            end += 1
        value = source[start:end]
        hashes.append(dict(path=path, signature=signature, sha256=hashlib.sha256(value.encode()).hexdigest()))
        return value
    parts = ["using System; using System.Collections.Generic; using UnityEngine; using ItemStatsSystem; using ItemStatsSystem.Data; using Duckov.Utilities; namespace BossRush {"]
    parts += [extract("internal sealed class CoopWaylayerEscrow")]
    parts += ["internal sealed partial class SkyIslandWaylayerChief {"]
    parts += [extract(signature) for signature in ("internal static Item BestTarget(", "internal static void GiveBack(", "internal bool AcceptCoopSnatch(", "internal void PublishCoopSnatch(", "internal void CompleteCoopSnatch(")]
    parts += ["}}"]
    generated = OUT / "Production.cs"
    generated.write_text("\n".join(parts), encoding="utf-8")
    (OUT / "source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    files = [generated, HERE / "Stubs.cs", HERE / "Program.cs", HERE / "Persistence.cs", HERE / "JournalCases.cs"]
    files += [ROOT / p for p in ("Common/Coop/BossRushCoopJson.cs", "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs", "SkyIsland/CoopWaylayerJournal.cs", "Common/Lifecycle/BossRushSlotJsonStore.cs", "Common/Lifecycle/BossRushSaveCoordinatorEngine.cs", "Common/Lifecycle/BossRushSaveFileThrottle.cs")]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + ''.join('<Compile Include="' + escape(str(path)) + '" />' for path in files) + '</ItemGroup></Project>'
    project_path = OUT / "Regression.csproj"
    project_path.write_text(project, encoding="utf-8")
    sys.path.insert(0, str(ROOT / "tools"))
    from run_runtime_regressions import run_project_fixture
    result, output = run_project_fixture(project_path, OUT, ROOT)
    print(output)
    if result: return result
    from run_harmony import main as run_harmony
    return run_harmony(ROOT,OUT,generated)

if __name__ == "__main__":
    raise SystemExit(main())
