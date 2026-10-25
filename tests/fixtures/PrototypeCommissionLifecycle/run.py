"""Execute the complete production commission owner with a delayed dialogue backend."""
from pathlib import Path
import hashlib
import json
import sys
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
SOURCE = Path("Integration/FieldAdventures/Prototypes/DangerousPrototypeCommission.cs")
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import strip_comments
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def run(root=ROOT, output=None):
    root = Path(root)
    output = output or ROOT / "Build/runtime-regressions/PrototypeCommissionLifecycle"
    output.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="sources-", dir=output)).resolve()
    source = root / SOURCE
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    text = strip_comments(source.read_text(encoding="utf-8-sig"))
    production = invocation / "Production.cs"
    production.write_text("using System; using System.Collections.Generic; using System.Threading; "
                          "using Saves; using UnityEngine; namespace BossRush {\n"
                          + member(text, "internal sealed class DangerousPrototypeCommissionData") + "\n"
                          + member(text, "internal sealed class DangerousPrototypeCommission :") + "\n}", encoding="utf-8")
    sources = [production, HERE / "Program.cs", HERE / "Stubs.cs"]
    includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project = invocation / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    (invocation / "source-sha256.json").write_text(json.dumps({str(SOURCE): digest}), encoding="utf-8")
    code, result = run_project_fixture(project, invocation / "execution", root)
    (invocation / "result.log").write_text(result, encoding="utf-8")
    assert hashlib.sha256(source.read_bytes()).hexdigest() == digest
    print(result)
    print("Evidence: " + str(invocation))
    return code, result


if __name__ == "__main__":
    raise SystemExit(run()[0])
