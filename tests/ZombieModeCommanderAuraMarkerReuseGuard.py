"""Guard: zombie commander aura refresh should reuse run-only markers and owners."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")


def fail(message: str) -> int:
    print("ZombieModeCommanderAuraMarkerReuseGuard: FAIL - " + message)
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
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "internal void RefreshZombieModeCommanderAuraTargets(")
    if body is None:
        return fail("missing RefreshZombieModeCommanderAuraTargets body")

    required = [
        "ZombieModeEnemyRuntimeMarker target = record.Target as ZombieModeEnemyRuntimeMarker;",
        "if (target == null)",
        "target = recordObject.GetComponent<ZombieModeEnemyRuntimeMarker>();",
        "CharacterMainControl targetCharacter = target.Owner;",
        "if (targetCharacter == null)",
        "targetCharacter = target.GetComponent<CharacterMainControl>();",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing marker/owner reuse snippet -> " + snippet)

    direct_marker = "ZombieModeEnemyRuntimeMarker target = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();"
    if direct_marker in body:
        return fail("commander aura still directly fetches marker from record GameObject")

    direct_owner = "CharacterMainControl targetCharacter = target.GetComponent<CharacterMainControl>();"
    if direct_owner in body:
        return fail("commander aura still directly fetches owner component")

    print("ZombieModeCommanderAuraMarkerReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
