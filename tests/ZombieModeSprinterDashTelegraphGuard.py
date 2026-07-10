"""Guard: ZombieMode Sprinter must dash after startup instead of teleporting instantly."""

from pathlib import Path
import sys


RUNTIME = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")
COMPONENTS = Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("ZombieModeSprinterDashTelegraphGuard: FAIL - " + message)
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


def extract_case_until_break(body: str, case_token: str) -> str | None:
    start = body.find(case_token)
    if start < 0:
        return None

    end = body.find("break;", start + len(case_token))
    if end < 0:
        return None

    return body[start : end + len("break;")]


def main() -> int:
    runtime = RUNTIME.read_text(encoding="utf-8-sig")
    components = COMPONENTS.read_text(encoding="utf-8-sig")

    special_body = extract_method_body(runtime, "private void TryExecuteZombieModeSpecialSkill(")
    if special_body is None:
        return fail("missing TryExecuteZombieModeSpecialSkill body")

    sprinter_case = extract_case_until_break(special_body, "case ZombieModeSpecialKind.Sprinter:")
    if sprinter_case is None:
        return fail("missing Sprinter special case")

    for forbidden in [
        "Vector3.MoveTowards(",
        "character.transform.position =",
    ]:
        if forbidden in sprinter_case:
            return fail("Sprinter case reverted to instant teleport -> " + forbidden)

    if "StartZombieModeSprinterDash(runId, character, player.transform.position);" not in sprinter_case:
        return fail("Sprinter case must delegate to startup dash helper")

    helper = extract_method_body(runtime, "private void StartZombieModeSprinterDash(")
    if helper is None:
        return fail("missing StartZombieModeSprinterDash helper")

    for token in [
        "\"ZombieMode_SprinterDashTelegraph\"",
        "ZombieModeSprinterDashRuntime runtime = telegraph.AddComponent<ZombieModeSprinterDashRuntime>();",
        "ZombieModeTuning.SprinterDashDistance",
        "ZombieModeTuning.SprinterDashStartupSeconds",
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, telegraph, runtime, null);",
    ]:
        if token not in helper:
            return fail("Sprinter helper missing token -> " + token)

    for token in [
        "public sealed class ZombieModeSprinterDashRuntime : ZombieModeTimedRunScopedRuntime",
        "startupEndTime = Time.unscaledTime + Mathf.Max(0.05f, startupSeconds);",
        "source.SetForceMoveVelocity(dashDirection * (dashDistance / dashDuration));",
        "source.SetForceMoveVelocity(Vector3.zero);",
        "startupEndTime += pausedDuration;",
    ]:
        if token not in components:
            return fail("Sprinter dash runtime missing token -> " + token)

    print("ZombieModeSprinterDashTelegraphGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
