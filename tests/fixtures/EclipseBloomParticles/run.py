"""Run the production EclipseBloom body in an isolated, real Unity Editor player loop."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
from unity_project_path import find_unity_editor

PRODUCTION = (
    "Integration/EclipseBloom/EclipseBloomBody.cs",
    "Integration/EclipseBloom/EclipseBloomAssetManager.cs",
    "Integration/EclipseBloom/EclipseBloomAuthoredAssets.cs",
    "Integration/EclipseBloom/EclipseBloomVisuals.cs",
    "Integration/EclipseBloom/EclipseBloomVisuals_Geometry.cs",
    "Integration/EclipseBloom/EclipseBloomGroundProjection.cs",
    "Integration/EclipseBloom/EclipseBloomRules.cs",
    "Common/Effects/BossRushFxKit.cs",
    "Common/Effects/BossRushFxMaterials.cs",
    "Common/Effects/BossRushParticleTextures.cs",
    "Common/Effects/BossRushProceduralSprites.cs",
)
BUNDLE = "Assets/ui/eclipse_bloom_visuals"
OUTPUT = ROOT / "Build/EclipseBloomParticles"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(project, invocation):
    assets = project / "Assets"
    assets.mkdir(parents=True, exist_ok=True)
    hashes = {}
    for name in PRODUCTION:
        source = ROOT / name
        shutil.copy2(source, assets / source.name)
        hashes[name] = sha(source)
    for name in ("HostStubs.cs", "EclipseBloomParticleRegression.cs"):
        shutil.copy2(HERE / name, assets / name)
    mod_root = invocation / "Mod"
    bundle_target = mod_root / BUNDLE
    bundle_target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / BUNDLE, bundle_target)
    hashes[BUNDLE] = sha(ROOT / BUNDLE)
    packages = project / "Packages"
    packages.mkdir(exist_ok=True)
    dependencies = {"com.unity.render-pipelines.universal": "14.0.11"}
    dependencies.update({"com.unity.modules." + name: "1.0.0" for name in (
        "physics", "particlesystem", "imageconversion", "jsonserialize", "assetbundle")})
    (packages / "manifest.json").write_text(json.dumps({"dependencies": dependencies}, indent=2), encoding="utf-8")
    settings = project / "ProjectSettings"
    settings.mkdir(exist_ok=True)
    (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f3\n", encoding="utf-8")
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    return mod_root, hashes


def run_unity(editor, project, invocation, mod_root, label):
    report = invocation / (label + ".json")
    log = invocation / (label + ".log")
    (project / "particle-regression.json").write_text(json.dumps({"modRoot": str(mod_root), "reportPath": str(report)}), encoding="utf-8")
    command = [editor, "-batchmode", "-force-d3d11", "-projectPath", str(project), "-executeMethod",
               "BossRush.EclipseBloomParticleRegression.Run", "-logFile", str(log)]
    print("Unity " + label + ": " + str(log), flush=True)
    with subprocess.Popen(command, cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as process:
        started = time.monotonic()
        progress = started
        while process.poll() is None:
            if time.monotonic() - started > 480:
                process.kill()
                process.wait()
                raise RuntimeError("Unity regression timed out; inspect " + str(log))
            if time.monotonic() - progress > 30:
                print("Unity " + label + " running (" + str(round(time.monotonic() - started)) + " s)", flush=True)
                progress = time.monotonic()
            time.sleep(0.25)
        code = process.returncode
    if not report.is_file():
        tail = log.read_text(encoding="utf-8", errors="replace")[-8000:] if log.is_file() else "No Unity log"
        raise RuntimeError("Unity did not produce its execution report (exit " + str(code) + "):\n" + tail)
    data = json.loads(report.read_text(encoding="utf-8"))
    print(label + ": " + json.dumps({key: data[key] for key in ("passed", "unityVersion", "simulatedSystems", "velocityErrors")}), flush=True)
    if data.get("failures"):
        print("Failures: " + json.dumps(data["failures"], ensure_ascii=False), flush=True)
    if data.get("errors"):
        print("Engine errors: " + json.dumps(data["errors"][:3], ensure_ascii=False), flush=True)
    return code, data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--negative-probe", action="store_true", help="After baseline, remove x/z assignments only in the isolated project and require the native Unity error")
    args = parser.parse_args()
    editor = find_unity_editor()
    if not editor or not (ROOT / BUNDLE).is_file():
        print("PARTIAL: real Unity Editor 2022.3.62f3 and authored EclipseBloom bundle are required; no engine test ran.")
        return 2
    OUTPUT.mkdir(parents=True, exist_ok=True)
    # 持久项目只缓存 Unity 导入结果；每次执行仍重新复制生产源码、真实包并生成独立报告。
    project = OUTPUT / "UnityProject"
    invocation = Path(tempfile.mkdtemp(prefix="run-", dir=OUTPUT))
    mod_root, hashes = prepare(project, invocation)
    print("Editor: " + editor + "\nProject: " + str(project) + "\nEvidence: " + str(invocation), flush=True)
    try:
        code, baseline = run_unity(editor, project, invocation, mod_root, "baseline")
        if code or not baseline.get("passed"):
            return 1
        if args.negative_probe:
            body = project / "Assets/EclipseBloomBody.cs"
            original = body.read_bytes()
            mutated = original
            for axis in ("x", "z"):
                anchor = ("velocity." + axis + " = new ParticleSystem.MinMaxCurve(0f, 0f);").encode()
                if mutated.count(anchor) != 1:
                    raise RuntimeError("Negative probe anchor must occur exactly once: " + anchor.decode())
                mutated = mutated.replace(anchor, b"", 1)
            try:
                body.write_bytes(mutated)
                negative_code, negative = run_unity(editor, project, invocation, mod_root, "negative-mixed-velocity")
                if negative_code == 0 or negative.get("passed") or negative.get("velocityErrors", 0) < 1:
                    raise RuntimeError("Negative probe did not fail with the native particle velocity curve error")
                print("NEGATIVE PASS: isolated old x/z defaults reproduced the native Unity error.", flush=True)
            finally:
                body.write_bytes(original)
                if sha(body) != hashlib.sha256(original).hexdigest():
                    raise RuntimeError("Isolated Body restoration SHA mismatch")
        changed = [name for name, expected in hashes.items() if sha(ROOT / name) != expected]
        if changed:
            raise RuntimeError("Production SHA changed during regression: " + ", ".join(changed))
        print("PASS: real Unity play mode, production body/FxKit, native particle simulation, source SHA unchanged.")
        return 0
    except (OSError, RuntimeError, ValueError) as error:
        print("FAIL: " + str(error), flush=True)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
