"""Run spread guard/runtime mutations in an isolated source copy, never shared files."""
from pathlib import Path
import hashlib
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]


def run(workspace, command):
    result = subprocess.run([sys.executable, *command], cwd=workspace, capture_output=True)
    return result.returncode, (result.stdout + result.stderr).decode("utf-8", errors="replace")


def main():
    out = ROOT / "Build/runtime-regressions/ZombieModeRewardRuntime"
    out.mkdir(parents=True, exist_ok=True)
    workspace = Path(tempfile.mkdtemp(prefix="spread-negative-", dir=out))
    sources = list((ROOT / "ZombieMode").glob("*.cs"))
    sources += list((ROOT / "tests/fixtures/ZombieModeRewardRuntime").glob("*.*"))
    sources += [ROOT / path for path in (
        "Utilities/RunScopedRegistry.cs", "Integration/Reforge/ReforgeSystem.cs",
        "tests/ZombieModeRewardSpreadSafetyGuard.py", "tests/cs_source_util.py",
        "tools/run_runtime_regressions.py", "鸭科夫源码/TeamSoda.Duckov.Core/ItemAgent_Gun.cs",
    )]
    for source in sources:
        target = workspace / source.relative_to(ROOT)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)

    guard = ["tests/ZombieModeRewardSpreadSafetyGuard.py"]
    runtime = ["tools/run_runtime_regressions.py", "--filter", "ZombieModeRewardRuntime"]
    for command in (guard, runtime):
        code, output = run(workspace, command)
        assert code == 0, output

    path = workspace / "ZombieMode/ZombieModeRewardProjectileSpread.cs"
    original = path.read_bytes()
    digest = hashlib.sha256(original).hexdigest()
    anchor = b'            TryAddZombieModeGunStatRuntimeModifier(snapshot, holdItem, "ShotCount", shotCount - currentShotCount);'
    assert original.count(anchor) == 1
    # Reintroduce the old original/new pellet damage ratio as an equivalent transient delta.
    mutation = b'            TryAddZombieModeGunStatRuntimeModifier(snapshot, holdItem, "Damage", -holdItem.Stats.GetStat("Damage").Value * (1f - currentShotCount / shotCount));\n'
    try:
        path.write_bytes(original.replace(anchor, mutation + anchor))
        code, output = run(workspace, guard)
        assert code != 0 and "only ShotCount/ShotAngle overlays are allowed" in output, output
        print("Expected RED: spread guard rejects second damage modification")
        code, output = run(workspace, runtime)
        log = workspace / "Build/runtime-regressions/ZombieModeRewardRuntime.log"
        details = log.read_text(encoding="utf-8")
        assert code != 0 and "official trident total damage must not be split twice" in details, output + details
        print("Expected RED: production rebuild plus official shot formula detects lost total damage")
    finally:
        path.write_bytes(original)
        assert hashlib.sha256(path.read_bytes()).hexdigest() == digest
    for command in (guard, runtime):
        code, output = run(workspace, command)
        assert code == 0, output
    print("Restored GREEN, SHA-256 " + digest)
    print("Isolated evidence: " + str(workspace))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
