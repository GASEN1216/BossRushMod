"""Guard: custom ZombieMode Exploder must honor its trigger-distance tuning."""

from pathlib import Path
import re
import sys


RUNTIME = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")
MARKER = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")


def fail(message: str) -> int:
    print("ZombieModeExploderTriggerDistanceGuard: FAIL - " + message)
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
    tuning = TUNING.read_text(encoding="utf-8-sig")
    if "public const float ExploderTriggerDistance = 2.5f;" not in tuning:
        return fail("ExploderTriggerDistance tuning constant changed or removed")

    runtime = RUNTIME.read_text(encoding="utf-8-sig")
    skill_body = extract_method_body(runtime, "private void TryExecuteZombieModeSpecialSkill(")
    if skill_body is None:
        return fail("missing TryExecuteZombieModeSpecialSkill body")

    exploder_match = re.search(
        r"case\s+ZombieModeSpecialKind\.Exploder\s*:(.*?)case\s+ZombieModeSpecialKind\.OfficialExploder\s*:",
        skill_body,
        re.S,
    )
    if exploder_match is None:
        return fail("missing custom Exploder case before OfficialExploder")

    exploder_case = exploder_match.group(1)
    for token in [
        "ZombieModeTuning.ExploderTriggerDistance",
        "exploderDelta.y = 0f;",
        "exploderDelta.sqrMagnitude > triggerDistance * triggerDistance",
        "break;",
        "StartZombieModeTelegraphedAreaDamage(",
        "true",
    ]:
        if token not in exploder_case:
            return fail("custom Exploder trigger gate missing token -> " + token)

    if exploder_case.find("exploderDelta.sqrMagnitude") > exploder_case.find("StartZombieModeTelegraphedAreaDamage("):
        return fail("custom Exploder distance gate must run before telegraphed explosion")

    marker_text = MARKER.read_text(encoding="utf-8-sig")
    for token in [
        "public bool CustomExploderSkillDetonated;",
        "marker.CustomExploderSkillDetonated = false;",
    ]:
        if token not in marker_text:
            return fail("custom Exploder self-detonation marker missing token -> " + token)

    death_body = extract_method_body(runtime, "private void HandleZombieModeSpecialDeathEffects(")
    if death_body is None:
        return fail("missing HandleZombieModeSpecialDeathEffects body")
    if "marker.CustomExploderSkillDetonated" not in death_body:
        return fail("custom Exploder skill detonation must skip duplicate death explosion")

    telegraph_body = extract_method_body(runtime, "public void TryExecuteZombieModeTelegraphedAreaDamage(")
    if telegraph_body is None:
        return fail("missing TryExecuteZombieModeTelegraphedAreaDamage body")
    if "TryKillZombieModeCustomExploderAfterDetonation(runId, source, origin);" not in telegraph_body:
        return fail("telegraphed explosion must self-kill custom Exploder after detonation")

    self_kill_body = extract_method_body(runtime, "private void TryKillZombieModeCustomExploderAfterDetonation(")
    if self_kill_body is None:
        return fail("missing custom Exploder self-kill helper")
    for token in [
        "marker.SpecialKind != ZombieModeSpecialKind.Exploder",
        "marker.CustomExploderSkillDetonated = true;",
        "new DamageInfo(source)",
        "selfDamage.isExplosion = true;",
        "source.Health.Hurt(selfDamage);",
    ]:
        if token not in self_kill_body:
            return fail("custom Exploder self-kill helper missing token -> " + token)

    components = Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs").read_text(encoding="utf-8-sig")
    for token in [
        "private bool followSourcePosition;",
        "bool newFollowSourcePosition = false",
        "RefreshFollowSourceOrigin();",
        "ShouldCancelFollowSourceRuntime()",
        "marker.DeathSettled || marker.RemovedFromRuntime",
        "source.Health.CurrentHealth <= 0f",
    ]:
        if token not in components:
            return fail("follow-source telegraph runtime missing token -> " + token)

    print("ZombieModeExploderTriggerDistanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
