"""Link the real utility owners; isolate Unity, items, AI and shop UI at their boundaries."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/HostUtilityOwners"
SOURCES = [
    "Utilities/BossStatScaling.cs", "Utilities/BossRushWaitCache.cs",
    "Utilities/ZombieSpawnSanitizer.cs", "ZombieMode/ZombieModeSpawnSanitizationPolicy.cs",
    "Integration/BossRushIntegrationRuntimeModule_AmmoShop.cs",
]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    counter = ROOT / "Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs"
    source = counter.read_text(encoding="utf-8-sig")
    field = "private int _item105PurchaseCount;"
    signature = "internal int Item105PurchaseCount"
    assert source.count(field) == source.count(signature) == 1
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for end in range(opening, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            break
    generated = OUT / "PurchaseCounter.cs"
    generated.write_text("namespace BossRush { internal sealed partial class IntegrationRuntimeModule {\n"
                         + field + "\n" + source[start:end + 1] + "\n}}", encoding="utf-8")
    hashes = {rel: hashlib.sha256((ROOT / rel).read_bytes()).hexdigest() for rel in SOURCES}
    hashes[counter.relative_to(ROOT).as_posix()] = hashlib.sha256(counter.read_bytes()).hexdigest()
    (OUT / "production-source-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    paths = [ROOT / rel for rel in SOURCES] + [generated, HERE / "Stubs.cs", HERE / "Program.cs"]
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
        '</PropertyGroup><ItemGroup>' + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'})
        + '" />' for p in paths) + '</ItemGroup></Project>', encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
