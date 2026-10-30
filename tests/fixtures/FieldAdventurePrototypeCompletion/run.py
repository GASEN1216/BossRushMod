"""Execute real lease, synchronous shot and asynchronous firework completion methods."""
from pathlib import Path
import hashlib
import importlib.util
import json
import shutil
import sys
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
RUNTIME = Path("Integration/FieldAdventures/Prototypes/DangerousPrototypeRuntimeModule.cs")
RULES = Path("Integration/FieldAdventures/Prototypes/DangerousPrototypeRules.cs")


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


extract = module("prototype_completion_extract", ROOT / "tests/fixtures/FieldAdventurePrototypeRuntime/run.py")
runner = module("prototype_completion_runner", ROOT / "tools/run_runtime_regressions.py")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def execute(root, output):
    output.mkdir(parents=True, exist_ok=True)
    source = (root / RUNTIME).read_text(encoding="utf-8-sig")
    signatures = ("public override void OnUpdate(", "private void BeginLease(", "private bool IsHeld()",
                  "private void OnHoldChanged(", "private void RefreshVisuals()", "private void OnShot(",
                  "private static Vector3 ResolveShotDirection(", "private void TraceBlast(",
                  "private void TickRecoil(", "private void FireSpecial(", "private void LaunchFinale(",
                  "private void TickFinales(", "private void Explode(", "private bool TryHurt(",
                  "private void EndLease(", "private void CleanupRaid()", "private void ClearFinales()",
                  "private sealed class FinaleShell", "private sealed class FinaleBomblet")
    methods = [extract.member(source, signature) for signature in signatures]
    # 旧版没有完成收口方法，仍允许执行同一回归来证明旧生产逻辑转红。
    if "private bool CompleteLeaseIfReady()" in source:
        methods.append(extract.member(source, "private bool CompleteLeaseIfReady()"))
    palette = source[source.index("private static readonly Color[] FinalePalette"):]
    palette = palette[:palette.index("};") + 2]
    generated = output / "Production.cs"
    generated.write_text("using System; using System.Collections.Generic; using UnityEngine; "
                         "using UnityEngine.SceneManagement; namespace BossRush { "
                         "internal sealed partial class DangerousPrototypeRuntimeModule : ReviewModuleBase {\n"
                         + "\n".join(methods) + "\n" + palette + "\n}}", encoding="utf-8")
    sources = (generated, root / RULES, HERE / "Program.cs", HERE / "Stubs.cs")
    includes = "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in sources)
    project = output / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0067</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    code, text = runner.run_project_fixture(project, output / "execution", root)
    (output / "result.log").write_text(text, encoding="utf-8")
    return code, text


def run():
    evidence = ROOT / "Build/runtime-regressions/FieldAdventurePrototypeCompletion"
    evidence.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="evidence-", dir=evidence))
    hashes = {str(path): sha(ROOT / path) for path in (RUNTIME, RULES)}
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    code, text = execute(ROOT, invocation / "baseline")
    print(text, flush=True)
    if code:
        print("Evidence: " + str(invocation), flush=True)
        return code
    probes = (
        ("skip-async-completion", b"TickFinales(deltaTime);\r\n            CompleteLeaseIfReady();",
         b"TickFinales(deltaTime);", "asynchronous main explosion: completion releases both event subscriptions"),
        ("repeat-success", b"!rules.Completed || !leaseSubscribed", b"!rules.Completed",
         "later explosions and updates cannot repeat completion"),
        ("drop-finales-on-success", b"visuals.HideRig(); visualTailTime = 0.65f;",
         b"ClearFinales(); visuals.HideRig(); visualTailTime = 0.65f;",
         "completion preserves all launched bomblets and the short visual tail"),
        ("keep-shoot-subscription", b"ItemAgent_Gun.OnMainCharacterShootEvent -= OnShot;",
         b";", "asynchronous main explosion: completion releases both event subscriptions"),
    )
    try:
        for label, anchor, replacement, expected in probes:
            isolated = invocation / ("isolated-" + label)
            for path in (RUNTIME, RULES):
                destination = isolated / path
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(ROOT / path, destination)
            path = isolated / RUNTIME
            original = path.read_bytes()
            # 支持检出策略的 CRLF / LF 差异，生产与隔离源仍按原字节恢复。
            if anchor not in original:
                anchor = anchor.replace(b"\r\n", b"\n")
            if original.count(anchor) != 1:
                raise RuntimeError("Nonunique mutation anchor: " + label)
            try:
                path.write_bytes(original.replace(anchor, replacement, 1))
                failed, negative = execute(isolated, invocation / label)
                if failed == 0 or expected not in negative:
                    raise RuntimeError("Mutation did not fail at expected assertion: " + label + "\n" + negative[-5000:])
                print("NEGATIVE PASS " + label + ": " + expected, flush=True)
            finally:
                path.write_bytes(original)
                restored = sha(path)
                if restored != hashlib.sha256(original).hexdigest():
                    raise RuntimeError("Isolated restoration SHA mismatch: " + label)
                (isolated / "restoration.json").write_text(json.dumps({"originalSha256": hashlib.sha256(original).hexdigest(),
                    "restoredSha256": restored, "restored": True}, indent=2), encoding="utf-8")
        if any(sha(ROOT / path) != expected for path, expected in hashes.items()):
            raise RuntimeError("Production files changed during regression")
    except (OSError, RuntimeError) as error:
        print("FAIL: " + str(error), flush=True)
        return 1
    (invocation / "result.json").write_text(json.dumps({"passed": True, "mutations": [p[0] for p in probes],
        "inputs": hashes}, indent=2), encoding="utf-8")
    print("PASS: baseline and isolated negative probes; evidence " + str(invocation), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(run())
