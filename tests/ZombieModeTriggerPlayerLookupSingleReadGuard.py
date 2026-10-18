"""Guard: zombie reward trigger hurt handling should reuse one player lookup."""

from pathlib import Path
import sys
from cs_source_util import clean_source


SOURCE = Path("ZombieMode/ZombieModeRewardTriggerEffects.cs")


def fail(message: str) -> int:
    print("ZombieModeTriggerPlayerLookupSingleReadGuard: FAIL - " + message)
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
    text = clean_source(SOURCE.read_text(encoding="utf-8-sig"))
    body = extract_method_body(text, "internal void HandleZombieModeOptionHealthHurt(")
    if body is None:
        return fail("missing HandleZombieModeOptionHealthHurt body")

    if body.count("CharacterMainControl.Main") != 1:
        return fail("HandleZombieModeOptionHealthHurt should read CharacterMainControl.Main exactly once")

    required = [
        "CharacterMainControl player = CharacterMainControl.Main;",
        "damageInfo.fromCharacter != player",
        "TryApplyZombieModeTriggerLifesteal(player, options);",
        "Vector3 baseDirection = (victim.transform.position - player.transform.position);",
        "baseDirection = player.transform.forward;",
        "Vector3 returnDirection = (player.transform.position - victim.transform.position);",
        "if (options.ProjectileStasisStacks > 0 && IsZombieModePlayerProjectileDamage(damageInfo))",
        "if (IsZombieModePlayerProjectileDamage(damageInfo))",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing player reuse snippet -> " + snippet)

    if "CharacterMainControl.Main.transform" in body:
        return fail("HandleZombieModeOptionHealthHurt still dereferences CharacterMainControl.Main.transform")

    helper = extract_method_body(text, "private static void TryApplyZombieModeTriggerLifesteal(")
    if helper is None or "CharacterMainControl.Main" in helper or "player.Health.AddHealth(Mathf.Max(1, options.TriggerLifestealHealAmount));" not in helper:
        return fail("lifesteal helper must heal the captured player without another Main lookup")
    dead = extract_method_body(text, "internal void HandleZombieModeOptionHealthDead(")
    if dead is None or "TryApplyZombieModeTriggerLifesteal(player, options);" not in dead:
        return fail("fatal direct hit must reuse the captured player for lifesteal")

    print("ZombieModeTriggerPlayerLookupSingleReadGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
