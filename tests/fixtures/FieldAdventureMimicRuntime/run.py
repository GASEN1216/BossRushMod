"""Execute real mimic production code and official lightweight-health event contracts."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture

SOURCES = (
    "Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs",
    "Integration/FieldAdventures/Mimic/GreedyMimicRules.cs",
    "鸭科夫源码/TeamSoda.Duckov.Core/HealthSimpleBase.cs",
    "鸭科夫源码/TeamSoda.Duckov.Core/DamageReceiver.cs",
)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    output = Path(os.environ.get("BOSSRUSH_MIMIC_RUNTIME_EVIDENCE_DIR", str(ROOT / "Build/runtime-regressions/FieldAdventureMimicRuntime")))
    output.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="evidence-", dir=output))
    hashes = {name: sha(ROOT / name) for name in SOURCES}
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    code, text = run_project_fixture(HERE / "FieldAdventureMimicRuntime.csproj", invocation / "baseline", ROOT)
    (invocation / "baseline.log").write_text(text, encoding="utf-8")
    print(text, flush=True)
    if code:
        return code
    probes = (
        ("raw-critical-credit", b"recoveryDamage += dealt;", b"recoveryDamage += damage.damageValue;",
         "critical exact: interrupt credits real positive health loss"),
        ("credit-rejected-source", b"damage.fromCharacter == null || !damage.fromCharacter.IsMainCharacter",
         b"false", "non-main and missing sources cannot interrupt recovery"),
    )
    try:
        for label, anchor, replacement, expected in probes:
            isolated = invocation / ("isolated-" + label)
            for name in SOURCES:
                destination = isolated / name
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(ROOT / name, destination)
            fixture = isolated / "tests/fixtures/FieldAdventureMimicRuntime"
            fixture.mkdir(parents=True)
            for name in ("FieldAdventureMimicRuntime.csproj", "Program.cs", "Stubs.cs"):
                shutil.copy2(HERE / name, fixture / name)
            source = isolated / SOURCES[0]
            original = source.read_bytes()
            if original.count(anchor) != 1:
                raise RuntimeError("Nonunique mutation anchor: " + label)
            try:
                source.write_bytes(original.replace(anchor, replacement, 1))
                failed, negative = run_project_fixture(fixture / "FieldAdventureMimicRuntime.csproj", invocation / label, isolated)
                (invocation / (label + ".log")).write_text(negative, encoding="utf-8")
                if failed == 0 or expected not in negative:
                    raise RuntimeError("Mutation did not fail at expected assertion: " + label + "\n" + negative[-6000:])
                print("NEGATIVE PASS " + label + ": " + expected, flush=True)
            finally:
                source.write_bytes(original)
                if sha(source) != hashlib.sha256(original).hexdigest():
                    raise RuntimeError("Isolated restoration SHA mismatch: " + label)
                (isolated / "restoration.json").write_text(json.dumps({"source": str(source),
                    "originalSha256": hashlib.sha256(original).hexdigest(), "restoredSha256": sha(source), "restored": True}, indent=2), encoding="utf-8")
        for name, expected in hashes.items():
            if sha(ROOT / name) != expected:
                raise RuntimeError("Production changed during regression: " + name)
    except (OSError, RuntimeError) as error:
        print("FAIL: " + str(error), flush=True)
        return 1
    (invocation / "result.json").write_text(json.dumps({"passed": True, "mutations": [item[0] for item in probes],
        "inputs": hashes}, indent=2), encoding="utf-8")
    print("PASS: baseline and isolated negative probes; official health contract executed; evidence " + str(invocation), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
