"""Guard: ZombieMode plague effects must be sustained damage clouds, not one-shot blasts."""

from pathlib import Path
import re
import sys


RUNTIME = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")
COMPONENTS = Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("ZombieModePlagueCloudBehaviorGuard: FAIL - " + message)
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


def extract_case(body: str, case_token: str, next_case_token: str) -> str | None:
    start = body.find(case_token)
    if start < 0:
        return None

    end = body.find(next_case_token, start + len(case_token))
    if end < 0:
        return None

    return body[start:end]


def main() -> int:
    runtime = RUNTIME.read_text(encoding="utf-8-sig")
    components = COMPONENTS.read_text(encoding="utf-8-sig")

    special_body = extract_method_body(runtime, "private void TryExecuteZombieModeSpecialSkill(")
    if special_body is None:
        return fail("missing TryExecuteZombieModeSpecialSkill body")

    plague_case = extract_case(
        special_body,
        "case ZombieModeSpecialKind.Plague:",
        "case ZombieModeSpecialKind.Summoner:",
    )
    if plague_case is None:
        return fail("missing Plague special case")

    if "PlagueCloudDamagePerSecond * ZombieModeTuning.PlagueCloudDurationSeconds" in plague_case:
        return fail("Plague special case reverted to one-shot total damage")

    for token in [
        "StartZombieModeTelegraphedDamageCloud(",
        "ZombieModeTuning.PlagueCloudRadius",
        "ZombieModeTuning.PlagueCloudDurationSeconds",
        "ZombieModeTuning.PlagueCloudDamagePerSecond",
        "\"ZombieMode_PlagueCloud\"",
    ]:
        if token not in plague_case:
            return fail("Plague special case missing sustained-cloud token -> " + token)

    elite_body = extract_method_body(runtime, "private void TryExecuteZombieModeEliteSkill(")
    if elite_body is None:
        return fail("missing TryExecuteZombieModeEliteSkill body")

    toxic_match = re.search(
        r"if\s*\(marker\.EliteAffixes\.Contains\(ZombieModeEliteAffix\.ToxicAura\).*?\)\s*\{(?P<body>.*?)\n\s*\}\n\s*if\s*\(marker\.EliteAffixes\.Contains\(ZombieModeEliteAffix\.Shielded\)",
        elite_body,
        re.S,
    )
    if toxic_match is None:
        return fail("missing elite ToxicAura/Plague skill block")

    toxic_body = toxic_match.group("body")
    if "StartZombieModeTelegraphedAreaDamage(" in toxic_body and "26f" in toxic_body:
        return fail("elite ToxicAura/Plague reverted to one-shot area damage")

    for token in [
        "StartZombieModeTelegraphedDamageCloud(",
        "26f / Mathf.Max(0.1f, ZombieModeTuning.PlagueCloudDurationSeconds)",
        "ZombieModeTuning.PlagueCloudDurationSeconds",
        "ZombieMode_ToxicAuraCloud",
        "ZombieMode_ElitePlagueCloud",
    ]:
        if token not in toxic_body:
            return fail("elite ToxicAura/Plague block missing cloud token -> " + token)

    spawn_body = extract_method_body(runtime, "public void SpawnZombieModeDamageCloud(")
    if spawn_body is None:
        return fail("missing SpawnZombieModeDamageCloud helper")
    for token in [
        "ZombieModeAreaTickRuntime runtime = cloud.AddComponent<ZombieModeAreaTickRuntime>();",
        "damagePerSecond",
        "tickInterval",
        "followSourcePosition",
    ]:
        if token not in spawn_body:
            return fail("damage-cloud spawn helper missing token -> " + token)

    for token in [
        "public sealed class ZombieModeTelegraphedDamageCloudRuntime : ZombieModeTimedRunScopedRuntime",
        "inst.SpawnZombieModeDamageCloud(",
        "triggerTime += pausedDuration",
    ]:
        if token not in components:
            return fail("telegraphed damage-cloud runtime missing token -> " + token)

    print("ZombieModePlagueCloudBehaviorGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
