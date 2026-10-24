"""Run behavioral mutations in independent copies without altering the workspace."""
from pathlib import Path
import hashlib
import importlib.util
import json
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
PRODUCTION = ROOT / "Integration/EclipseBloom/EclipseBloomRules.cs"
OUT = ROOT / "Build/runtime-regressions/EclipseBloomRules-negative"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location("eclipse_regression_runner", ROOT / "tools/run_runtime_regressions.py")
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    baseline_hash = digest(PRODUCTION)
    baseline_code, baseline_output = runner.run_project_fixture(
        HERE / "EclipseBloomRules.csproj", OUT / "baseline", ROOT)
    (OUT / "baseline.log").write_text(baseline_output, encoding="utf-8")
    if baseline_code:
        print("FAIL: baseline is red before negative probes")
        print(baseline_output[-6000:])
        return 1

    probes = (
        ("phase_regression", (
            ("int nextPhase = Phase;", "int nextPhase = 1;"),
            ("if (nextPhase > Phase)", "if (nextPhase != Phase)"),
        ), "healing cannot return to phase one"),
        ("warning_fast_forward", (
            ("stageElapsed += step;", "stageElapsed += step * 100f;"),
        ), "damage waits for a full simulated warning at every frame interval"),
        ("broken_sector_survives", (
            ("AttackMask &= ~bit;", "AttackMask |= 0;"),
        ), "breaking the firing petal immediately clears its damage sector"),
        ("inverted_sector", (
            ("Math.Abs(difference) <=", "Math.Abs(difference) >="),
        ), "phase two petal break makes its rotating sector safe immediately"),
        ("rotation_uses_unlimited_clock", (
            ("EclipseBloomTuning.RotationDegreesPerSecond * step",
             "EclipseBloomTuning.RotationDegreesPerSecond * deltaSeconds"),
        ), "petal rotation uses the same limited simulation step"),
        ("missing_core_fallback", (
            ("IsCoreAttack = Phase == 3 || IntactMask == 0;", "IsCoreAttack = Phase == 3;"),
        ), "no petals and phase three both start a fresh opposing core warning"),
    )
    results = {}
    source = PRODUCTION.read_text(encoding="utf-8")
    for name, mutations, expected_failure in probes:
        copy_root = Path(tempfile.mkdtemp(prefix=name + "-", dir=OUT))
        copied_production = copy_root / PRODUCTION.relative_to(ROOT)
        copied_fixture = copy_root / HERE.relative_to(ROOT)
        copied_production.parent.mkdir(parents=True)
        copied_fixture.mkdir(parents=True)
        modified = source
        for original, replacement in mutations:
            if modified.count(original) != 1:
                raise AssertionError(name + ": mutation anchor must occur exactly once: " + original)
            modified = modified.replace(original, replacement)
        copied_production.write_text(modified, encoding="utf-8")
        for filename in ("Program.cs", "EclipseBloomRules.csproj"):
            (copied_fixture / filename).write_bytes((HERE / filename).read_bytes())
        code, output = runner.run_project_fixture(
            copied_fixture / "EclipseBloomRules.csproj", copy_root / "execution", copy_root)
        (copy_root / "probe.log").write_text(output, encoding="utf-8")
        passed = code != 0 and "Unhandled exception." in output and expected_failure in output
        results[name] = {"passed": passed, "exit_code": code, "expected_assertion": expected_failure,
                         "copy_root": str(copy_root)}
        print(("PASS " if passed else "FAIL ") + name + ": " + expected_failure, flush=True)
        if not passed:
            print(output[-6000:])

    unchanged = digest(PRODUCTION) == baseline_hash
    results["workspace_production_unchanged"] = unchanged
    results["production_sha256"] = baseline_hash
    (OUT / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(("PASS" if unchanged else "FAIL") + ": workspace production SHA-256 unchanged")
    return 0 if unchanged and all(value["passed"] for name, value in results.items()
                                  if isinstance(value, dict)) else 1


if __name__ == "__main__":
    sys.exit(main())
