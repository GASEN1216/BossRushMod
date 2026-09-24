"""Mutate only a sparse disposable copy; rerun the real aggregate fixture and restore exact bytes."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import sys

from run import HERE, ROOT, find_harmony


def main():
    scratch = ROOT / "Build/ghn-negative"
    source_name = "Integration/BackMountain/GardenHarvestNoticePatch.cs"
    fixture_name = "tests/fixtures/GardenHarvestNotice/"
    paths = [source_name, "tools/run_runtime_regressions.py"] + [fixture_name + name for name in ("run.py", "Program.cs", "Stubs.cs")]
    for path in paths:
        target = scratch / path
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / path, target)
    env = dict(os.environ, BOSSRUSH_HARMONY_DLL=str(find_harmony()), PYTHONIOENCODING="utf-8", PYTHONUTF8="1")
    command = [sys.executable, str(scratch / "tools/run_runtime_regressions.py"), "--filter", "GardenHarvestNotice"]

    def execute():
        result = subprocess.run(command, cwd=scratch, env=env, capture_output=True, text=True, encoding="utf-8")
        log = (scratch / "Build/runtime-regressions/GardenHarvestNotice.log").read_text(encoding="utf-8")
        return result.returncode, log

    code, output = execute()
    assert code == 0, output
    probes = [
        ("premature-success", "await delivery;", "await default(UniTask);", "unfinished delivery never announces success"),
        ("swallowed-failure", "await delivery;", "try { await delivery; } catch (Exception) { }", "synchronous delivery failure remains observable"),
        ("lost-slot-gate", " || slot != SavesSystem.CurrentSlot", "", "slot change prevents stale notice"),
        ("lost-scene-gate", "|| sceneHandle != SceneManager.GetActiveScene().handle", "|| false", "scene handle change prevents stale notice"),
        ("missing-observer-call", "new CodeInstruction(OpCodes.Call, observe),", "new CodeInstruction(OpCodes.Nop),", "valid IL adds exactly one two-instruction observer"),
        ("ambiguous-delivery", "if (matches != 1 || forget", "if (matches < 1 || forget", "two delivery calls preserves original IL"),
    ]
    source = scratch / source_name
    original = source.read_bytes()
    original_hash = hashlib.sha256(original).hexdigest()
    results = []
    for name, before, after, assertion in probes:
        old, new = before.encode("utf-8"), after.encode("utf-8")
        assert original.count(old) == 1, name + ": probe anchor is not unique"
        try:
            source.write_bytes(original.replace(old, new))
            code, output = execute()
            assert code != 0 and ("System.Exception: " + assertion) in output, name + ": unexpected outcome\n" + output
            results.append({"probe": name, "expectedAssertion": assertion, "turnedRed": True})
            print(name + ": RED at expected assertion", flush=True)
        finally:
            source.write_bytes(original)
            assert hashlib.sha256(source.read_bytes()).hexdigest() == original_hash
    code, output = execute()
    assert code == 0, output
    report = {"sourceSha256": original_hash, "restoredSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
              "sharedWorkspaceSourceUnchanged": hashlib.sha256((ROOT / source_name).read_bytes()).hexdigest() == original_hash,
              "probes": results, "restoredFixturePassed": True}
    report_path = ROOT / "Build/garden-harvest-notice/negative-probes.json"
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    assert report["sharedWorkspaceSourceUnchanged"], "Shared production file changed concurrently; it was not overwritten by this probe"
    print("GardenHarvestNotice negative probes: PASS; restored source SHA-256 " + original_hash)


if __name__ == "__main__":
    main()
