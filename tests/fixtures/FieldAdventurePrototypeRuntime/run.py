"""Execute extracted production firing/recoil methods against official direction code."""
from pathlib import Path
import hashlib
import importlib.util
import json
import sys
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
RUNTIME = Path("Integration/FieldAdventures/Prototypes/DangerousPrototypeRuntimeModule.cs")
RULES = Path("Integration/FieldAdventures/Prototypes/DangerousPrototypeRules.cs")
OFFICIAL = Path("鸭科夫源码/TeamSoda.Duckov.Core/ItemAgent_Gun.cs")
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import strip_comments


def member(source, signature):
    source = strip_comments(source)
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        assert end < len(source), signature
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def run(root=ROOT, output_root=None):
    root = Path(root)
    output_root = output_root or root / "Build/runtime-regressions/FieldAdventurePrototypeRuntime"
    output_root.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="sources-", dir=output_root)).resolve()
    hashes = {str(p): hashlib.sha256((root / p).read_bytes()).hexdigest()
              for p in (RUNTIME, RULES, OFFICIAL)}
    runtime = (root / RUNTIME).read_text(encoding="utf-8-sig")
    methods = [member(runtime, signature) for signature in (
        "private bool IsHeld()", "private void OnShot(",
        "private static Vector3 ResolveShotDirection(", "private void TraceBlast(",
        "private void TickRecoil(",
    )]
    production = ("using System; using System.Collections.Generic; using UnityEngine; "
                  "using UnityEngine.SceneManagement; namespace BossRush { "
                  "internal sealed partial class DangerousPrototypeRuntimeModule {\n"
                  + "\n".join(methods) + "\n}}\n")
    official = (root / OFFICIAL).read_text(encoding="utf-8-sig")
    trans = member(official, "private void TransToFire(")
    start, end = "Vector3 vector = this.muzzle.forward;", "if (this.overrideShootPoint)"
    assert trans.count(start) == trans.count(end) == 1
    direction = trans[trans.index(start):trans.index(end)]
    production += ("internal sealed class OfficialGunFixture : GunBase { "
                   "internal UnityEngine.Vector3 Direction() {\n" + direction
                   + "\nreturn vector; }}\n")
    generated = invocation / "Production.cs"
    generated.write_text(production, encoding="utf-8")
    (invocation / "source-sha256.json").write_text(json.dumps(hashes, indent=2, ensure_ascii=False), encoding="utf-8")
    sources = [generated, root / RULES, HERE / "Program.cs", HERE / "Stubs.cs"]
    includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project = invocation / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    spec = importlib.util.spec_from_file_location("prototype_regression_runner", ROOT / "tools/run_runtime_regressions.py")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    code, output = runner.run_project_fixture(project, invocation / "execution", root)
    (invocation / "result.log").write_text(output, encoding="utf-8")
    assert all(hashlib.sha256((root / p).read_bytes()).hexdigest() == expected for p, expected in hashes.items())
    print(output)
    print("Evidence: " + str(invocation))
    return code, output


if __name__ == "__main__":
    raise SystemExit(run()[0])
