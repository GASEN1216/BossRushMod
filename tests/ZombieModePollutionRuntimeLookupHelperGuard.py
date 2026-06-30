"""Guard: Zombie pollution runtime Update paths should route component fallback through helpers."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("ZombieModePollutionRuntimeLookupHelperGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
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
                return text[start : idx + 1]

    return None


def extract_class(text: str, class_name: str) -> str | None:
    return extract_block(text, "public sealed class " + class_name)


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    threat = extract_class(text, "ZombieModeThreatRuntime")
    commander = extract_class(text, "ZombieModeCommanderAuraRuntime")
    if threat is None:
        return fail("missing ZombieModeThreatRuntime")
    if commander is None:
        return fail("missing ZombieModeCommanderAuraRuntime")

    threat_update = extract_block(threat, "private void Update()")
    commander_update = extract_block(commander, "private void Update()")
    if threat_update is None or commander_update is None:
        return fail("missing Update block")

    if "GetComponent<" in threat_update:
        return fail("Threat Update still performs direct GetComponent fallback")
    if "GetComponent<" in commander_update:
        return fail("CommanderAura Update still performs direct GetComponent fallback")

    for token in [
        "private ZombieModeEnemyRuntimeMarker GetCachedMarker()",
        "marker = GetComponent<ZombieModeEnemyRuntimeMarker>();",
        "inst.TryExecuteZombieModeEnemyRuntimeSkill(GetCachedMarker());",
    ]:
        if token not in threat:
            return fail("Threat runtime missing lookup-helper token -> " + token)

    for token in [
        "private CharacterMainControl GetOwnerCharacter()",
        "private ZombieModeEnemyRuntimeMarker GetMarker()",
        "ownerCharacter = GetComponent<CharacterMainControl>();",
        "marker = GetComponent<ZombieModeEnemyRuntimeMarker>();",
        "CharacterMainControl character = GetOwnerCharacter();",
        "ZombieModeEnemyRuntimeMarker runtimeMarker = GetMarker();",
    ]:
        if token not in commander:
            return fail("Commander aura missing lookup-helper token -> " + token)

    print("ZombieModePollutionRuntimeLookupHelperGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
