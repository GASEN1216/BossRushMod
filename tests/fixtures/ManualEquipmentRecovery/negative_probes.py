"""Reintroduce Frostmourne's premature cancellation in an isolated source copy."""
from pathlib import Path
import contextlib
import hashlib
import io
import json
import sys
import tempfile
import run


def main():
    root = run.ROOT
    sys.path.insert(0, str(root / "tools"))
    source = Path("Integration/Frostmourne/FrostmourneAction.cs")
    data = (root / source).read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    inputs = json.loads((run.OUT / "production-sha256.json").read_text(encoding="utf-8"))
    paths = set(inputs) | {
        "Config/ConfigItemIds.cs", "Integration/NewWeapons/AstralStaff/AstralStaffConfig.cs",
        "Integration/NewWeapons/Common/NewWeaponIds.cs", "Integration/NewWeapons/Common/NewWeaponItemAttributes.cs",
    }
    output = root / "Build/runtime-regressions/ManualEquipmentRecovery-negative"
    output.mkdir(parents=True, exist_ok=True)
    isolated = Path(tempfile.mkdtemp(prefix="normal-stop-", dir=output))
    for path in paths:
        target = isolated / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((root / path).read_bytes())
    old = b"keepPendingSpawnOnStop = !spawningComplete;"
    assert data.count(old) == 1
    (isolated / source).write_bytes(data.replace(old, b"keepPendingSpawnOnStop = false;"))
    run.ROOT = isolated
    run.OUT = isolated / "execution"
    result = io.StringIO()
    with contextlib.redirect_stdout(result):
        code = run.main()
    text = result.getvalue()
    (isolated / "result.log").write_text(text, encoding="utf-8")
    assert code != 0 and "Frostmourne cold creation survives normal cast completion" in text, text
    assert hashlib.sha256((root / source).read_bytes()).hexdigest() == digest
    print("EXPECTED RED: Frostmourne cold creation survives normal cast completion")
    print("ManualEquipmentRecovery negative probe: PASS; original bytes unchanged")
    print("Evidence: " + str(isolated))


if __name__ == "__main__":
    main()
