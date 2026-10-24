"""Verify the EclipseBloom/final-showdown exclusion in a sparse independent copy."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/CampaignPlayability-eclipse-negative"
TARGET = Path("Campaign/CampaignFinalBoss.cs")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    before = hashlib.sha256((ROOT / TARGET).read_bytes()).hexdigest()
    baseline = subprocess.run([sys.executable, str(ROOT / "tools/run_runtime_regressions.py"),
                               "--filter", "CampaignPlayability"], cwd=ROOT, capture_output=True)
    (OUT / "baseline.log").write_bytes(baseline.stdout + baseline.stderr)
    if baseline.returncode:
        print("FAIL: baseline is red before the exclusion probe")
        return 1
    manifest = json.loads((ROOT / "Build/campaign-playability/production-source-sha256.json").read_text(encoding="utf-8"))
    copied_root = Path(tempfile.mkdtemp(prefix="missing-exclusion-", dir=OUT))
    files = list(manifest) + ["tests/cs_source_util.py", "Assets/Data/Campaign/Chapters.json"]
    files += [str(path.relative_to(ROOT)) for path in HERE.iterdir() if path.suffix in (".py", ".cs")]
    for filename in files:
        destination = copied_root / filename
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / filename, destination)
    source = copied_root / TARGET
    text = source.read_text(encoding="utf-8-sig")
    anchor = "if (EclipseBloomRuntimeModule.IsEncounterActive) return true;"
    if text.count(anchor) != 1:
        raise AssertionError("EclipseBloom exclusion mutation anchor must occur exactly once")
    source.write_text(text.replace(anchor, "if (false) return true;"), encoding="utf-8")
    mutation = subprocess.run([sys.executable, str(copied_root / HERE.relative_to(ROOT) / "run.py")],
                              cwd=copied_root, capture_output=True)
    output = (mutation.stdout + mutation.stderr).decode("utf-8", errors="replace")
    (OUT / "mutation.log").write_text(output, encoding="utf-8")
    expected = "active eclipse bloom excludes the final showdown through the production interaction gate"
    red = mutation.returncode != 0 and expected in output and "Unhandled exception." in output
    unchanged = hashlib.sha256((ROOT / TARGET).read_bytes()).hexdigest() == before
    print(("PASS" if red else "FAIL") + ": missing exclusion fails at the real occupied-entry assertion")
    print(("PASS" if unchanged else "FAIL") + ": workspace campaign production SHA-256 unchanged")
    (OUT / "results.json").write_text(json.dumps({"expected_failure": red, "workspace_unchanged": unchanged,
        "production_sha256": before, "copy_root": str(copied_root)}, indent=2), encoding="utf-8")
    if not red:
        print(output[-6000:])
    return 0 if red and unchanged else 1


if __name__ == "__main__":
    raise SystemExit(main())
