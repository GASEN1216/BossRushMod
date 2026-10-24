"""Reintroduce direction/origin defects in isolated copies, never shared source."""
from pathlib import Path
import contextlib
import hashlib
import io
import json
import tempfile

from run import ROOT, RUNTIME, RULES, OFFICIAL, run


def main():
    out = ROOT / "Build/runtime-regressions/FieldAdventurePrototypeRuntime-negative"
    out.mkdir(parents=True, exist_ok=True)
    workspace = Path(tempfile.mkdtemp(prefix="copy-", dir=out))
    hashes = {}
    for relative in (RUNTIME, RULES, OFFICIAL):
        original = (ROOT / relative).read_bytes()
        hashes[str(relative)] = hashlib.sha256(original).hexdigest()
        target = workspace / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(original)
    path = workspace / RUNTIME
    original = path.read_bytes()
    baseline = original.decode("utf-8-sig")
    records = []

    def execute(name):
        with contextlib.redirect_stdout(io.StringIO()):
            code, output = run(workspace, workspace / "evidence" / name)
        (workspace / (name + ".log")).write_text(output, encoding="utf-8")
        return code, output

    code, output = execute("baseline")
    assert code == 0, output
    probes = (
        ("old_aim_minus_muzzle", "Vector3 direction = ResolveShotDirection(gun, borrower);",
         "Vector3 direction = borrower.GetCurrentAimPoint() - from;\n"
         "if (direction.sqrMagnitude < 0.01f) direction = gun.muzzle.forward;\ndirection.Normalize();",
         "prototype direction must match official near/moveability/high-ground gate"),
        ("remove_moveability_gate", "if (player.CharacterMoveability > 0.5f)", "if (true)",
         "prototype direction must match official near/moveability/high-ground gate"),
        ("use_3d_distance", "aim.y = 0f; position.y = 0f; muzzle.y = 0f;", "",
         "prototype direction must match official near/moveability/high-ground gate"),
        ("skip_direction_helper", "Vector3 direction = ResolveShotDirection(gun, borrower);",
         "Vector3 direction = gun.muzzle.forward;",
         "prototype direction must match official near/moveability/high-ground gate"),
        ("old_wall_retreat", "from = Vector3.MoveTowards(obstruction.point, chest, 0.12f);",
         "from = obstruction.point - direction * 0.12f;",
         "obstructed origin stays on the character side for backward/tangent/high-ground shots"),
        ("forward_recoil", "recoilDirection = -Vector3.ProjectOnPlane(direction, Vector3.up).normalized;",
         "recoilDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;",
         "jet recoil opposes actual official horizontal firing direction"),
    )
    for name, anchor, replacement, expected in probes:
        assert baseline.count(anchor) == 1, name
        try:
            path.write_text(baseline.replace(anchor, replacement), encoding="utf-8")
            code, output = execute(name)
            assert code != 0 and expected in output, name + ":\n" + output
            records.append({"name": name, "result": "EXPECTED RED", "assertion": expected})
            print("Expected RED: " + name)
        finally:
            path.write_bytes(original)
            assert hashlib.sha256(path.read_bytes()).hexdigest() == hashes[str(RUNTIME)]
    code, output = execute("restored")
    assert code == 0, output
    assert all(hashlib.sha256((ROOT / relative).read_bytes()).hexdigest() == expected
               for relative, expected in hashes.items()), "shared source changed during isolated mutations"
    (workspace / "report.json").write_text(json.dumps({"probes": records, "source_sha256": hashes,
        "restored": "GREEN", "shared_source_unchanged": True}, indent=2, ensure_ascii=False), encoding="utf-8")
    print("Restored GREEN; shared production SHA-256 unchanged. Evidence: " + str(workspace))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
