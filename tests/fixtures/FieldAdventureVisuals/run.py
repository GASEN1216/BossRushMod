"""Render six production field-adventure visuals in isolated Unity play mode and inspect textual evidence."""
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

SOURCES = (
    "Integration/FieldAdventures/FieldAdventureVisualAssets.cs",
    "Integration/FieldAdventures/Mimic/GreedyMimicRules.cs",
    "Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs",
    "Integration/FieldAdventures/Mimic/GreedyMimicVisual.cs",
    "Integration/FieldAdventures/Mimic/GreedyMimicRuntimeModule.cs",
    "鸭科夫源码/TeamSoda.Duckov.Core/HealthSimpleBase.cs",
    "鸭科夫源码/TeamSoda.Duckov.Core/DamageReceiver.cs",
    "鸭科夫源码/TeamSoda.Duckov.Core/ExplosionManager.cs",
    "Integration/FieldAdventures/Delivery/OddParcelRules.cs",
    "Integration/FieldAdventures/Delivery/OddParcelVisual.cs",
    "Integration/FieldAdventures/Prototypes/DangerousPrototypeRules.cs",
    "Integration/FieldAdventures/Prototypes/DangerousPrototypeVisuals.cs",
    "Common/Effects/BossRushFxKit.cs",
    "Common/Effects/BossRushFxMaterials.cs",
    "Common/Effects/BossRushParticleTextures.cs",
    "Common/Effects/BossRushProceduralSprites.cs",
)
BUNDLE = "Assets/ui/field_adventures_visuals"
OUTPUT = ROOT / "Build/FieldAdventureVisuals"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def method_anchor(source, signature):
    if source.count(signature) != 1:
        raise RuntimeError("Method mutation signature is not unique: " + signature.decode("ascii"))
    start = source.index(signature)
    opening = source.index(b"{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end:end + 1] == b"{") - (source[end:end + 1] == b"}")
        end += 1
    return source[start:end]


def move_owned_library(source, destination, boundary):
    source, destination, boundary = source.resolve(), destination.resolve(), boundary.resolve()
    if source.name != "Library" or destination.name != "Library" or not source.is_relative_to(boundary) or not destination.is_relative_to(boundary):
        raise RuntimeError("Refusing a Library move outside this owned evidence directory")
    if not source.is_dir() or destination.exists():
        raise RuntimeError("Library cache move requires an existing source and absent destination")
    shutil.move(str(source), str(destination))


def mimic_negative_probes(editor, invocation, baseline_project):
    visual = (ROOT / "Integration/FieldAdventures/Mimic/GreedyMimicVisual.cs").read_bytes()
    encounter = (ROOT / "Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs").read_bytes()
    bounds = method_anchor(visual, b"internal void GetDamageBounds(out Bounds bodyBounds, out Bounds lidBounds)")
    visual_newline = b"\r\n" if b"\r\n" in visual else b"\n"
    encounter_newline = b"\r\n" if b"\r\n" in encounter else b"\n"
    growth = b"scale = rules.Scale; swallow = 1.4f;" + visual_newline + b"            ApplyBodyPose(false, 0f);"
    frame = b"visual.Tick(dt, player.transform.position, fighting, health != null ? health.HealthValue / Rules.MaximumHealth : 1, warningLeft, attack);" + encounter_newline + b"            SyncReceiverGeometry();"
    probes = (
        ("fixed-envelope", "GreedyMimicVisual.cs", bounds,
         b"internal void GetDamageBounds(out Bounds bodyBounds, out Bounds lidBounds) { bodyBounds = new Bounds(new Vector3(0, 1.35f * scale, 0), new Vector3(2.8f, 2.5f, 2f) * scale); lidBounds = bodyBounds; }",
         ("swallow upper face ray must reach damage receiver", "swallow lower face ray must reach damage receiver")),
        ("missing-grow-pose", "GreedyMimicVisual.cs", growth, b"scale = rules.Scale; swallow = 1.4f;",
         ("growth callback must fully apply visible pose before any visual Tick",)),
        ("stale-grow-geometry", "GreedyMimicEncounter.cs", b"visual.Grow(Rules); CreateReceiver();", b"CreateReceiver(); visual.Grow(Rules);",
         ("visible mesh coverage phase=growth callback before Tick",)),
        ("missing-frame-geometry", "GreedyMimicEncounter.cs", frame, frame.replace(encounter_newline + b"            SyncReceiverGeometry();", b""),
         ("visible mesh coverage phase=swallow",)),
        ("fixed-world-yaw", "GreedyMimicEncounter.cs", b"receiverBody.rotation = visual.Facing.rotation;", b"receiverBody.rotation = Quaternion.identity;",
         ("visible mesh coverage phase=breathing",)),
        ("duplicate-explosion-collider", "GreedyMimicEncounter.cs", b"bodyCollider = receiverRoot.AddComponent<BoxCollider>();",
         b"bodyCollider = receiverRoot.AddComponent<BoxCollider>(); receiverRoot.AddComponent<BoxCollider>();",
         ("actual official explosion must deduct exactly one damage instance from simpleHealth",)),
    )
    records = []
    for label, name, anchor, replacement, expected in probes:
        # 每个 mutant 独有工程及源副本；不向共享基线或生产文件写入变异。
        mutant = invocation / ("mutant-" + label)
        project = mutant / "UnityProject"
        mod_root, _ = prepare(project, mutant)
        # 源副本和项目各自独立，串行借用本轮已初始化 Library，避免六次 URP 首次导入。
        # 每次移动前核实绝对路径属于本轮生成目录；它不涉及作者工程、游戏或存档。
        for artifact in (baseline_project / "Assets").iterdir():
            if artifact.suffix in (".meta", ".asset"):
                shutil.copy2(artifact, project / "Assets" / artifact.name)
        for setting in (baseline_project / "ProjectSettings").glob("*"):
            if setting.is_file():
                shutil.copy2(setting, project / "ProjectSettings" / setting.name)
        source = project / "Assets" / name
        original = source.read_bytes()
        if original.count(anchor) != 1:
            raise RuntimeError("Nonunique native mutation anchor: " + label)
        record = {"label": label, "source": str(source), "originalSha256": hashlib.sha256(original).hexdigest(), "expectedFailures": expected}
        move_owned_library(baseline_project / "Library", project / "Library", invocation.parent)
        record["reusedOwnedLibrary"] = True
        try:
            source.write_bytes(original.replace(anchor, replacement, 1))
            record["mutantSha256"] = sha(source)
            code, negative = run_unity(editor, project, mutant, mod_root, label)
            failures = "\n".join(negative.get("failures", []))
            if code == 0 or negative.get("passed") or not all(item in failures for item in expected):
                raise RuntimeError("Native mutant did not fail at every expected assertion: " + label + "\n" + failures[-3000:])
            record["expectedRed"] = True
            record["failureCount"] = len(negative.get("failures", []))
            print("NEGATIVE PASS " + label + ": " + "; ".join(expected), flush=True)
        finally:
            try:
                source.write_bytes(original)
                record["restoredSha256"] = sha(source)
                record["restored"] = record["restoredSha256"] == record["originalSha256"]
                (mutant / "restoration.json").write_text(json.dumps(record, indent=2), encoding="utf-8")
                if not record["restored"]:
                    raise RuntimeError("Native mutant restoration SHA mismatch: " + label)
            finally:
                move_owned_library(project / "Library", baseline_project / "Library", invocation.parent)
        records.append(record)
    (invocation / "mimic-negative-restorations.json").write_text(json.dumps(records, indent=2), encoding="utf-8")
    return records


def prepare(project, invocation):
    assets = project / "Assets"
    assets.mkdir(parents=True, exist_ok=True)
    hashes = {}
    adaptations = []
    for relative in SOURCES:
        path = ROOT / relative
        target = assets / path.name
        shutil.copy2(path, target)
        hashes[relative] = sha(path)
        if path.name == "ExplosionManager.cs":
            # 反编译源中的 Object 同时引入 System/UnityEngine；只修隔离副本的名称歧义。
            target.write_bytes(b"using Object = UnityEngine.Object;\n" + target.read_bytes())
            adaptations.append({"source": relative, "kind": "compile-only UnityEngine.Object alias", "sourceSha256": hashes[relative], "isolatedSha256": sha(target)})
    for name in ("HostStubs.cs", "MimicHostStubs.cs", "FieldAdventureVisualRegression.cs", "FieldAdventureMimicCollisionRegression.cs"):
        shutil.copy2(HERE / name, assets / name)
        hashes[(HERE / name).relative_to(ROOT).as_posix()] = sha(HERE / name)
    mod_root = invocation / "Mod"
    destination = mod_root / BUNDLE
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / BUNDLE, destination)
    hashes[BUNDLE] = sha(ROOT / BUNDLE)
    packages = project / "Packages"
    packages.mkdir(exist_ok=True)
    dependencies = {"com.unity.render-pipelines.universal": "14.0.11"}
    dependencies.update({"com.unity.modules." + name: "1.0.0" for name in (
        "physics", "particlesystem", "imageconversion", "jsonserialize", "assetbundle", "audio")})
    (packages / "manifest.json").write_text(json.dumps({"dependencies": dependencies}, indent=2), encoding="utf-8")
    settings = project / "ProjectSettings"
    settings.mkdir(exist_ok=True)
    (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f3\n", encoding="utf-8")
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    (invocation / "compile-adaptations.json").write_text(json.dumps(adaptations, indent=2), encoding="utf-8")
    return mod_root, hashes


def run_unity(editor, project, invocation, mod_root, label):
    report = invocation / (label + ".json")
    log = invocation / (label + ".log")
    screenshots = invocation / label
    (project / "visual-regression.json").write_text(json.dumps({
        "modRoot": str(mod_root), "reportPath": str(report), "outputDirectory": str(screenshots)
    }), encoding="utf-8")
    command = [editor, "-batchmode", "-force-d3d11", "-projectPath", str(project), "-executeMethod",
               "BossRush.FieldAdventureVisualRegression.Run", "-logFile", str(log)]
    print("Unity " + label + ": " + str(log), flush=True)
    with subprocess.Popen(command, cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as process:
        started = progress = time.monotonic()
        while process.poll() is None:
            if time.monotonic() - started > 600:
                process.kill()
                process.wait()
                raise RuntimeError("Unity preview timed out: " + str(log))
            if time.monotonic() - progress > 30:
                print("Unity " + label + " running " + str(round(time.monotonic() - started)) + " s", flush=True)
                progress = time.monotonic()
            time.sleep(.25)
        code = process.returncode
    if not report.is_file():
        tail = log.read_text(encoding="utf-8", errors="replace")[-9000:] if log.is_file() else "No log"
        raise RuntimeError("Unity produced no report, exit " + str(code) + "\n" + tail)
    data = json.loads(report.read_text(encoding="utf-8"))
    print(label + ": " + json.dumps({key: data.get(key) for key in (
        "passed", "unityVersion", "graphicsDevice", "cleanup", "constructorFailureCleanup", "inactiveOwnerCleanup", "hudGate", "leaseReacquire", "tongueReach", "parcelAudioSettings")}), flush=True)
    for sample in data.get("samples", []):
        print(json.dumps({key: sample[key] for key in ("name", "solidRenderers", "vertices", "dimensions", "activePixels", "magentaPixels", "animationDelta", "deferred")}), flush=True)
    for name in ("failures", "errors"):
        if data.get(name):
            print(name + ": " + json.dumps(data[name][:8], ensure_ascii=False), flush=True)
    if data.get("mimicCollision"):
        print("mimicCollision: " + json.dumps({key: data["mimicCollision"].get(key) for key in (
            "checks", "edgeCases", "rayHits", "sphereHits", "swallowOverhangCases", "meshCases", "officialDamageHits", "directQueriesWithoutSync", "upperFaceCases", "lowerFaceCases", "lidCases", "explosionCases")}), flush=True)
    return code, data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--negative-probe", action="store_true", help="Remove the parcel material only in the isolated source and require a visual/material failure")
    parser.add_argument("--tongue-negative-probe", action="store_true", help="Restore the old 32 m tongue cap in the isolated source and require an endpoint failure")
    parser.add_argument("--skip-mimic-negative-probe", action="store_true", help="Skip the default native geometry, growth, publication and explosion negative probes for preview-only runs")
    parser.add_argument("--evidence-dir", type=Path, default=os.environ.get("BOSSRUSH_FIELD_VISUAL_EVIDENCE_DIR"), help="Optional artifact directory; all source mutations remain inside its isolated Unity project")
    args = parser.parse_args()
    editor = find_unity_editor()
    if not editor or not (ROOT / BUNDLE).is_file():
        print("PARTIAL: Unity Editor and self-contained field_adventures_visuals bundle are required.")
        return 2
    output = args.evidence_dir.resolve() if args.evidence_dir else OUTPUT
    output.mkdir(parents=True, exist_ok=True)
    project = output / "UnityProject"
    invocation = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    mod_root, hashes = prepare(project, invocation)
    print("Editor: " + editor + "\nEvidence: " + str(invocation), flush=True)
    try:
        code, baseline = run_unity(editor, project, invocation, mod_root, "baseline")
        if code or not baseline.get("passed"):
            return 1
        if not args.skip_mimic_negative_probe:
            mimic_negative_probes(editor, invocation, project)
        if args.negative_probe:
            source = project / "Assets/OddParcelVisual.cs"
            original = source.read_bytes()
            anchor = b"Material mat = FieldAdventureVisualAssets.Solid;"
            if original.count(anchor) != 1:
                raise RuntimeError("negative material anchor is not unique")
            try:
                source.write_bytes(original.replace(anchor, b"Material mat = null;", 1))
                negative_code, negative = run_unity(editor, project, invocation, mod_root, "negative-missing-solid")
                evidence = "\n".join(negative.get("failures", []))
                if negative_code == 0 or negative.get("passed") or "missing or unsupported material" not in evidence:
                    raise RuntimeError("negative probe did not fail on the removed solid material")
                print("NEGATIVE PASS: missing production parcel material was rejected; isolated bytes restored.", flush=True)
            finally:
                source.write_bytes(original)
                if sha(source) != hashlib.sha256(original).hexdigest():
                    raise RuntimeError("negative probe restoration SHA mismatch")
        if args.tongue_negative_probe:
            source = project / "Assets/GreedyMimicVisual.cs"
            original = source.read_bytes()
            anchor = b"Vector3 start = Root.transform.position + Vector3.up * 1.6f, end = target + Vector3.up * 0.4f;"
            if original.count(anchor) != 1:
                raise RuntimeError("negative tongue anchor is not unique")
            try:
                source.write_bytes(original.replace(anchor, anchor + b"\n            end = start + Vector3.ClampMagnitude(end - start, 32f);", 1))
                negative_code, negative = run_unity(editor, project, invocation, mod_root, "negative-short-tongue")
                evidence = "\n".join(negative.get("failures", []))
                if negative_code == 0 or negative.get("passed") or "tongue tip must reach the actual locked attack target" not in evidence:
                    raise RuntimeError("negative probe did not fail on the restored tongue cap")
                print("NEGATIVE PASS: old tongue cap rejected by real Unity endpoint checks; isolated bytes restored.", flush=True)
            finally:
                source.write_bytes(original)
                if sha(source) != hashlib.sha256(original).hexdigest():
                    raise RuntimeError("negative tongue restoration SHA mismatch")
        changed = [name for name, expected in hashes.items() if sha(ROOT / name) != expected]
        if changed:
            raise RuntimeError("Production changed during the visual regression: " + ", ".join(changed))
        (output / "latest-result.json").write_text(json.dumps({"invocation": str(invocation), "report": str(invocation / "baseline.json"), "passed": True}, indent=2), encoding="utf-8")
        print("PASS: six real production previews, deferred solid materials, native particle checks, cleanup, source SHA unchanged. Screenshots are for owner review.")
        return 0
    except (OSError, RuntimeError, ValueError) as error:
        print("FAIL: " + str(error), flush=True)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
