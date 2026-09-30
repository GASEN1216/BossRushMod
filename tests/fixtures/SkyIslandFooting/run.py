"""Run unchanged SkyIsland footing production code against controlled Unity query boundaries."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BASELINE_ENV = "BOSSRUSH_SKY_FOOTING_BASELINE_REF"
PRODUCTION = "SkyIsland/SkyIslandSessionFooting.cs"
SESSION = "SkyIsland/SkyIslandSession.cs"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def run_logged(command, log):
    result = subprocess.run(command, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
    log.write_bytes(result.stdout)
    print(result.stdout.decode("utf-8", errors="replace"), end="", flush=True)
    return result.returncode


def main():
    if os.name != "nt":
        raise SystemExit("SkyIslandFooting requires Windows .NET Framework and the .NET SDK")
    baseline = os.environ.get(BASELINE_ENV)
    if baseline:
        baseline = subprocess.check_output(
            ["git", "rev-parse", "--verify", "--end-of-options", baseline + "^{commit}"], cwd=ROOT, text=True).strip()
    mode = "baseline" if baseline else "working-tree"
    output = ROOT / "Build/runtime-regressions/SkyIslandFooting"
    output.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix=mode + "-", dir=output))
    print("Evidence: " + str(invocation), flush=True)

    def read_source(path):
        if baseline:
            return subprocess.check_output(["git", "show", baseline + ":" + path], cwd=ROOT)
        return (ROOT / path).read_bytes()

    production = read_source(PRODUCTION)
    session = read_source(SESSION)
    production_path = invocation / "SkyIslandSessionFooting.cs"
    production_path.write_bytes(production)
    (invocation / "SkyIslandSession.source.txt").write_bytes(session)
    # Keep the exact production helper for the old implementation's VerifyGround call.
    # Two unique sibling signatures delimit the method without rewriting its decisions.
    text = session.decode("utf-8-sig")
    start = "        private void VerifyGround(Transform marker)"
    following = "        private void OnProbePath("
    if text.count(start) != 1 or text.count(following) != 1:
        raise ValueError("VerifyGround extraction anchors must each occur once")
    first = text.index(start)
    method = text[first:text.index(following, first)]
    extracted = invocation / "Production.VerifyGround.cs"
    extracted.write_text("using System; using UnityEngine; namespace BossRush { internal sealed partial class SkyIslandSession {"
                         + method + "}}", encoding="utf-8-sig")
    sources = [production_path, extracted]
    hashes = {PRODUCTION: sha256(production), SESSION: sha256(session), "VerifyGround method": sha256(method.encode("utf-8"))}
    for name in ("Host.cs", "Program.cs"):
        data = (HERE / name).read_bytes()
        snapshot = invocation / name
        snapshot.write_bytes(data)
        sources.append(snapshot)
        hashes["fixture/" + name] = sha256(data)
    evidence = {"mode": mode, "baseline_commit": baseline, "source_sha256": hashes}
    (invocation / "sources.json").write_text(json.dumps(evidence, indent=2) + chr(10), encoding="utf-8")

    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index("[") + 1:sdk.index("]")]) / sdk.split()[0] / "Roslyn/bincore/csc.dll"
    framework = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319"
    executable = invocation / "Regression.exe"
    arguments = ["/nologo", "/target:exe", "/langversion:7.3", "/nostdlib+", '/out:"' + str(executable) + '"']
    arguments += ['/r:"' + str(framework / name) + '"' for name in ("mscorlib.dll", "System.dll", "System.Core.dll")]
    arguments += ['"' + str(path) + '"' for path in sources]
    response = invocation / "compile.rsp"
    response.write_text(chr(10).join(arguments), encoding="utf-8-sig")
    compile_code = run_logged(["dotnet", str(compiler), "/noconfig", "@" + str(response)], invocation / "compile.log")
    result = {"mode": mode, "baseline_commit": baseline, "compile_exit_code": compile_code}
    if compile_code == 0:
        result["executable_sha256"] = sha256(executable.read_bytes())
        print("Execute fresh binary: " + str(executable), flush=True)
        print("SHA-256: " + result["executable_sha256"], flush=True)
        result["execution_exit_code"] = run_logged([str(executable)], invocation / "execution.log")
    (invocation / "result.json").write_text(json.dumps(result, indent=2) + chr(10), encoding="utf-8")
    return compile_code or result["execution_exit_code"]


if __name__ == "__main__":
    raise SystemExit(main())
