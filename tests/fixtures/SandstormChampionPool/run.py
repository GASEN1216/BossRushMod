"""Run the production champion factory and pool adapter with controlled Unity boundaries."""
from pathlib import Path
import hashlib
import json
import os
import sys
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(os.environ.get("BOSSRUSH_CHAMPION_SOURCE_ROOT", Path(__file__).resolve().parents[3]))
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture

PRODUCTION = (
    "Integration/SandstormChampion/SandstormChampionBoss.cs",
    "Integration/SandstormChampion/SandstormChampionPoolAdapter.cs",
    "Integration/SandstormChampion/SandstormChampionConfig.cs",
)


def member(text, signature):
    assert text.count(signature) == 1, signature
    start = text.index(signature)
    opening = text.index("{", start)
    depth = 0
    for end in range(opening, len(text)):
        depth += (text[end] == "{") - (text[end] == "}")
        if not depth:
            return text[start:end + 1]
    raise ValueError(signature)


def run_fixture(source_root=ROOT, output_root=None):
    output_root = output_root or ROOT / "Build/runtime-regressions/SandstormChampionPool"
    output_root.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="source-", dir=output_root))
    controller_path = source_root / "Integration/SandstormChampion/SandstormChampionAbilityController.cs"
    controller = controller_path.read_text(encoding="utf-8-sig")
    reward = invocation / "ProductionReward.cs"
    reward.write_text(
        "using System; using UnityEngine; using ItemStatsSystem; namespace BossRush { "
        "internal sealed partial class SandstormChampionController {"
        + member(controller, "private void OnBeforeSpawnLoot(") + "}}", encoding="utf-8")
    sources = [source_root / path for path in PRODUCTION]
    sources += [HERE / "Program.cs", HERE / "Stubs.cs", HERE / "Async.cs", reward]
    project = invocation / "ChampionPool.csproj"
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
        '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
        + ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
        + '</ItemGroup></Project>', encoding="utf-8")
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    hashes[str(controller_path)] = hashlib.sha256(controller_path.read_bytes()).hexdigest()
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return run_project_fixture(project, invocation / "execution", ROOT)


if __name__ == "__main__":
    code, output = run_fixture()
    print(output)
    raise SystemExit(code)
