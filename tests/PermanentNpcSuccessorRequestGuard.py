"""Permanent NPC requests retain the latest successor without releasing an old async owner."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs"


def body(source, marker):
    start = source.index("{", source.index(marker))
    depth = 0
    for end in range(start, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return source[start:end + 1]
    raise ValueError(marker)


def validate(source):
    errors = []
    spawn = body(source, "public void Spawn(ModBehaviour mod)")
    if "_spawnInFlight" in spawn or "_pendingSpawnRequest = new PermanentSpawnRequest" not in spawn:
        errors.append("Spawn must retain the latest request even while an earlier creation is pending")
    begin = body(source, "private void TryStartPendingSpawn()")
    for token in ["if (_spawnInFlight) return", "IsSpawnRequestValid(request)",
                  "ShouldSpawnInScene(request.Owner, request.SceneName)", "_activeSpawnRequest = request"]:
        if token not in begin:
            errors.append("successor start missing owner gate: " + token)
    valid = body(source, "private static bool IsSpawnRequestValid(")
    for token in ["ModBehaviour.Instance != request.Owner", "request.Owner == null",
                  "request.Generation != _spawnGeneration", "scene.name == request.SceneName",
                  "scene.handle == request.SceneHandle"]:
        if token not in valid:
            errors.append("request validity missing: " + token)
    finish = body(source, "private async UniTaskVoid SpawnForSceneAsync(")
    final = body(finish, "finally")
    if "if (_activeSpawnRequest == request)" not in final:
        errors.append("old finally must not clear a different active request")
    if not (0 <= final.find("_spawnInFlight = false") < final.find("TryStartPendingSpawn()")):
        errors.append("active owner must release busy before draining the successor")
    cleanup = body(source, "public void Destroy(ModBehaviour mod)")
    for token in ["_spawnGeneration++", "_pendingSpawnRequest = null"]:
        if token not in cleanup:
            errors.append("Destroy must invalidate active work and discard queued work: " + token)
    if "_spawnInFlight = false" in cleanup:
        errors.append("Destroy must not start another creation before the old async finally runs")
    return errors


def main():
    source = re.sub(r"/\*.*?\*/|//[^\n]*", "", SOURCE.read_text(encoding="utf-8-sig"), flags=re.S)
    try:
        errors = validate(source)
        for token in ["_pendingSpawnRequest = new PermanentSpawnRequest", "if (_activeSpawnRequest == request)",
                      "request.Generation != _spawnGeneration", "_pendingSpawnRequest = null;"]:
            if not validate(source.replace(token, "MissingInvariant")):
                errors.append("negative probe escaped: " + token)
    except ValueError as error:
        errors = [str(error)]
    for error in errors:
        print("PermanentNpcSuccessorRequestGuard: FAIL - " + error)
    if not errors:
        print("PermanentNpcSuccessorRequestGuard: PASS (4 negative probes)")
    return bool(errors)


if __name__ == "__main__":
    sys.exit(main())
