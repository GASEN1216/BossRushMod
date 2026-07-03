"""Guard: ZombieMode should keep custom Exploder and official self-destruction Exploder as separate special kinds."""

from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
POLLUTION = Path("ZombieMode/ZombieModePollution.cs")
RUNTIME = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print("ZombieModeExploderOfficialSelfDestructGuard: FAIL - " + message)
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
    models = MODELS.read_text(encoding="utf-8-sig")
    if "OfficialExploder" not in models:
        return fail("missing OfficialExploder special kind enum")

    pollution = POLLUTION.read_text(encoding="utf-8-sig")
    if "ZombieModeSpecialKind.OfficialExploder" not in pollution:
        return fail("pollution config no longer references OfficialExploder")
    if "BossRush_ZombieMode_Special_OfficialExploder" not in pollution:
        return fail("missing OfficialExploder display-name localization key")

    runtime = RUNTIME.read_text(encoding="utf-8-sig")

    death_body = extract_method_body(
        runtime,
        "private void HandleZombieModeSpecialDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)",
    )
    if death_body is None:
        return fail("missing HandleZombieModeSpecialDeathEffects body")
    if "marker.SpecialKind != ZombieModeSpecialKind.Exploder" not in death_body:
        return fail("custom Exploder death-effect guard lost special-kind filter")
    if "DealZombieModeAreaDamageToPlayer(" not in death_body:
        return fail("custom Exploder death effect no longer deals area damage")
    if "OfficialExploder" in death_body:
        return fail("official exploder leaked into custom death-effect handler")

    skill_body = extract_method_body(
        runtime,
        "private void TryExecuteZombieModeSpecialSkill(",
    )
    if skill_body is None:
        return fail("missing TryExecuteZombieModeSpecialSkill body")

    exploder_case = skill_body.find("case ZombieModeSpecialKind.Exploder:")
    if exploder_case < 0:
        return fail("missing custom Exploder special-skill case")
    official_case = skill_body.find("case ZombieModeSpecialKind.OfficialExploder:")
    if official_case < 0:
        return fail("missing OfficialExploder special-skill case")
    plague_case = skill_body.find("case ZombieModeSpecialKind.Plague:")
    if plague_case < 0:
        return fail("missing Plague special-skill case")

    exploder_slice = skill_body[exploder_case:official_case]
    if "StartZombieModeTelegraphedAreaDamage(" not in exploder_slice:
        return fail("custom Exploder no longer uses telegraphed custom explosion skill")

    official_slice = skill_body[official_case:plague_case]
    if "StartZombieModeTelegraphedAreaDamage(" in official_slice:
        return fail("OfficialExploder should not stack custom telegraphed explosion")

    threat_body = extract_method_body(
        runtime,
        "private void EnsureZombieModeThreatRuntime(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)",
    )
    if threat_body is None:
        return fail("missing EnsureZombieModeThreatRuntime body")
    official_guard = threat_body.find("marker.SpecialKind == ZombieModeSpecialKind.OfficialExploder")
    runtime_init = threat_body.find("runtime.Initialize(")
    if official_guard < 0:
        return fail("OfficialExploder threat runtime bypass is missing")
    if runtime_init < 0:
        return fail("ZombieMode threat runtime initialization path is missing")
    if official_guard > runtime_init:
        return fail("OfficialExploder bypass must happen before threat runtime initialization")
    if "return;" not in threat_body[official_guard:runtime_init]:
        return fail("OfficialExploder bypass no longer exits before threat runtime initialization")

    localization = LOCALIZATION.read_text(encoding="utf-8-sig")
    if 'InjectZombieModeString("BossRush_ZombieMode_Special_OfficialExploder"' not in localization:
        return fail("missing OfficialExploder localization injection")

    print("ZombieModeExploderOfficialSelfDestructGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
