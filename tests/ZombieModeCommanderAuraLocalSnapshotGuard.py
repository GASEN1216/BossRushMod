"""Guard: commander aura refresh should reuse local snapshots inside its tick loop."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")


def fail(message: str) -> int:
    print("ZombieModeCommanderAuraLocalSnapshotGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start:idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "internal void RefreshZombieModeCommanderAuraTargets(")
    if body is None:
        return fail("missing RefreshZombieModeCommanderAuraTargets body")

    required = [
        "GameObject commanderObject = commander.gameObject;",
        "Vector3 commanderPosition = commander.transform.position;",
        "int sourceId = commanderObject.GetInstanceID();",
        "GameObject recordObject = record.GameObject;",
        "recordObject == null",
        "recordObject == commanderObject",
        "target = recordObject.GetComponent<ZombieModeEnemyRuntimeMarker>();",
        "Vector3 delta = target.transform.position - commanderPosition;",
        "GameObject targetObject = targetCharacter.gameObject;",
        "int targetId = targetObject.GetInstanceID();",
        "targetRuntime = targetObject.GetComponent<ZombieModeCommanderAuraTargetRuntime>();",
        "targetRuntime = targetObject.AddComponent<ZombieModeCommanderAuraTargetRuntime>();",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing local snapshot snippet -> " + snippet)

    forbidden = [
        "commander.gameObject.GetInstanceID()",
        "record.GameObject == commander.gameObject",
        "record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>()",
        "target.transform.position - commander.transform.position",
        "targetCharacter.gameObject.GetInstanceID()",
        "targetCharacter.gameObject.GetComponent<ZombieModeCommanderAuraTargetRuntime>()",
        "targetCharacter.gameObject.AddComponent<ZombieModeCommanderAuraTargetRuntime>()",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("commander aura still repeats Unity property access -> " + snippet)

    print("ZombieModeCommanderAuraLocalSnapshotGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
