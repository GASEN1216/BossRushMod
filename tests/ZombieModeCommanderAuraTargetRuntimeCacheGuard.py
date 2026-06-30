"""Guard: commander aura refresh should cache target runtime components on markers."""

from pathlib import Path
import sys


RUNTIME = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
SKILLS = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")


def fail(message: str) -> int:
    print("ZombieModeCommanderAuraTargetRuntimeCacheGuard: FAIL - " + message)
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
    runtime = RUNTIME.read_text(encoding="utf-8-sig")
    skills = SKILLS.read_text(encoding="utf-8-sig")
    refresh = extract_method_body(skills, "internal void RefreshZombieModeCommanderAuraTargets(")
    register = extract_method_body(runtime, "private ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(")
    if refresh is None:
        return fail("missing RefreshZombieModeCommanderAuraTargets body")
    if register is None:
        return fail("missing RegisterZombieModeEnemyRuntimeShell body")

    for snippet in [
        "public ZombieModeCommanderAuraTargetRuntime CommanderAuraTargetRuntime;",
        "marker.CommanderAuraTargetRuntime = null;",
    ]:
        if snippet not in runtime:
            return fail("missing marker runtime cache snippet -> " + snippet)

    required_refresh = [
        "ZombieModeCommanderAuraTargetRuntime targetRuntime = target.CommanderAuraTargetRuntime;",
        "if (targetRuntime == null)",
        "targetRuntime = targetObject.GetComponent<ZombieModeCommanderAuraTargetRuntime>();",
        "if (targetRuntime == null)",
        "targetRuntime = targetObject.AddComponent<ZombieModeCommanderAuraTargetRuntime>();",
        "target.CommanderAuraTargetRuntime = targetRuntime;",
    ]
    for snippet in required_refresh:
        if snippet not in refresh:
            return fail("missing commander aura target-runtime cache snippet -> " + snippet)

    forbidden = "ZombieModeCommanderAuraTargetRuntime targetRuntime = targetObject.GetComponent<ZombieModeCommanderAuraTargetRuntime>();"
    if forbidden in refresh:
        return fail("commander aura still initializes target runtime with a direct GetComponent")

    print("ZombieModeCommanderAuraTargetRuntimeCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
