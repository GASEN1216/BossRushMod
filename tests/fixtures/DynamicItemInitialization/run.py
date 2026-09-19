"""Execute the unchanged instance initialization helper with an inactive-item adapter."""
from pathlib import Path
import hashlib
import subprocess
import xml.sax.saxutils as xml

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/dynamic-item-initialization"


def main():
    path = ROOT / "Patches/ItemStatsSystem/ItemAssetsCollectionDynamicRegistrationPatch.cs"
    source = path.read_text(encoding="utf-8-sig")
    start = source.index("internal static void InitializeInstance(Item item)")
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "Production.cs").write_text("using System; static class Production {\n" + source[start:end] + "\n}", encoding="utf-8")
    files = [OUT / "Production.cs", HERE / "Program.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += "".join('<Compile Include="' + xml.escape(str(p), {'"': '&quot;'}) + '" />' for p in files)
    project += '</ItemGroup></Project>'
    (OUT / "Probe.csproj").write_text(project, encoding="utf-8")
    (OUT / "source-sha256.txt").write_text(hashlib.sha256(path.read_bytes()).hexdigest(), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(OUT / "Probe.csproj"), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
