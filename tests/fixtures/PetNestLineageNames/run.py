"""Execute the production lineage catalog and pet-name entry with late language data."""
from pathlib import Path
import hashlib
import json
import os
import sys
from xml.sax.saxutils import escape

ROOT = Path(os.environ.get("PETNEST_NAMES_SOURCE_ROOT", Path(__file__).resolve().parents[3]))
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get("PETNEST_NAMES_OUTPUT", ROOT / "Build/runtime-regressions/PetNestLineageNames"))
sys.path.insert(0, str(ROOT / "tests"))
sys.path.insert(0, str(ROOT / "tools"))
from cs_source_util import clean_source
from run_runtime_regressions import run_project_fixture


def member(path, signature):
    code = clean_source(path.read_text(encoding="utf-8-sig"))
    assert code.count(signature) == 1, signature
    start = code.index(signature)
    opening = code.index("{", start)
    depth = 0
    for end in range(opening, len(code)):
        depth += (code[end] == "{") - (code[end] == "}")
        if depth == 0:
            return code[start:end + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    catalog = ROOT / "PetNest/PetNestLineageCatalog.cs"
    service = ROOT / "PetNest/PetNestService.cs"
    official = ROOT / "鸭科夫源码/TeamSoda.Duckov.Core/CharacterRandomPreset.cs"
    source = "namespace BossRush { static class PetNestService {\n"
    source += member(service, "internal static string GetPetDisplayName(") + "\n}}\n"
    source += "public partial class CharacterRandomPreset {\n" + member(official, "public string DisplayName") + "\n}\n"
    generated = OUT / "Production.cs"
    generated.write_text("using SodaCraft.Localizations;\n" + source, encoding="utf-8")
    paths = [catalog, generated, HERE / "Program.cs"]
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'
                       + ''.join('<Compile Include="' + escape(str(path)) + '" />' for path in paths)
                       + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "sources.json").write_text(json.dumps({str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
                                                for path in (catalog, service, official)}, indent=2), encoding="utf-8")
    code, output = run_project_fixture(project, OUT, ROOT)
    print(output, flush=True)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
