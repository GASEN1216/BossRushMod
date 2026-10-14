"""Execute production world-container registration, scene generation and control lease logic."""
from pathlib import Path
import hashlib
import subprocess
import sys
import re
from xml.sax.saxutils import escape
ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/CoopWorldRuntime"
OUT.mkdir(parents=True, exist_ok=True)
def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature); opening = source.index("{", start); end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}"); end += 1
    return source[start:end]
loot = ROOT / "Common/Coop/BossRushCoopWorldLoot.cs"
scene = ROOT / "Common/Coop/BossRushCoopRuntimeModule.cs"
loot_text = loot.read_text(encoding="utf-8-sig"); scene_text = scene.read_text(encoding="utf-8-sig")
code = "using System; using System.Text; using System.Collections.Generic; using UnityEngine; using UnityEngine.SceneManagement; namespace BossRush { partial class BossRushCoopWorldLoot {\n"
for signature in ("private sealed class BoxState", "private sealed class Snapshot\n", "private sealed class SnapshotPart",
                  "private static int Key(", "internal bool Register(", "private void PublishSnapshot(", "private void OnSnapshot(", "internal void Reset(", "internal void ChangeGeneration("):
    # Serializable is metadata only; codec maps the actual production field types.
    code += member(loot_text, signature)
for field in ("PartCharacters", "incoming", "incomingRevision", "incomingCount", "pending", "revision", "nextTick"):
    matches = re.findall(r"^\s*private [^\n]*\b" + field + r"\b[^\n]*;", loot_text, re.M)
    assert len(matches) == 1, field
    code += matches[0] + "\n"
code += "}\npartial class SceneOwner {\n"
code += member(scene_text, "private static string CurrentSceneId(") + member(scene_text, "private void RefreshSceneIdentity(") + member(scene_text, "private void ClearRemoteAlive(") + "}\n"
gravity = ROOT / "ZombieMode/ZombieModeRuntimeModule_EnemyRuntime.cs"
zombie_coop = ROOT / "ZombieMode/ZombieModeRuntimeModule_Coop.cs"
gravity_text = gravity.read_text(encoding="utf-8-sig")
zombie_text = zombie_coop.read_text(encoding="utf-8-sig")
code += member(zombie_text, "internal sealed class CoopZombieArea") + "\npartial class GravityOwner {\n"
routing = member(gravity_text, "internal void RefreshZombieModeGravityWellTargets(")
code += routing[:routing.index("            if (!IsZombieModeRunValid(runId))")] + 'throw new Exception("only the actual client producer is exercised");}\n}}'
generated = OUT / "Production.cs"; generated.write_text(code, encoding="utf-8")
sources = [generated, HERE / "Stubs.cs", HERE / "Program.cs", ROOT / "Common/Coop/BossRushCoopControl.cs", ROOT / "Common/Coop/BossRushCoopProtocol.cs"]
sources += [ROOT / path for path in ("Common/Coop/BossRushCoopJson.cs", "Common/Data/BossRushJsonValue.cs", "Utilities/SimpleJsonHelper.cs")]
project = OUT / "Regression.csproj"
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
(OUT / "production-sha256.txt").write_text("\n".join(str(p) + " " + hashlib.sha256(p.read_bytes()).hexdigest() for p in [loot, scene, gravity, zombie_coop, ROOT / "Common/Coop/BossRushCoopControl.cs", ROOT / "Common/Coop/BossRushCoopProtocol.cs", ROOT / "Common/Coop/BossRushCoopJson.cs"]), encoding="utf-8")
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture
result, output = run_project_fixture(project, OUT, ROOT)
print(output)
raise SystemExit(result)
