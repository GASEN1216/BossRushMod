"""Guard: zombie reward trigger hurt handling should reuse one player lookup."""

from pathlib import Path
import sys


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
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "internal void HandleZombieModeOptionHealthHurt(")
    if body is None:
        return fail("missing HandleZombieModeOptionHealthHurt body")

    if body.count("CharacterMainControl.Main") != 1:
        return fail("HandleZombieModeOptionHealthHurt should read CharacterMainControl.Main exactly once")

    required = [
        "CharacterMainControl player = CharacterMainControl.Main;",
        "damageInfo.fromCharacter != player",
        # 吸血抽成辅助方法（击杀那一下在死亡处理里补判，2026-10-16）：受伤处理把同一个 player 传进去。
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

    lifesteal = extract_method_body(text, "private static void TryApplyZombieModeTriggerLifesteal(CharacterMainControl player, ZombieModeOptionRuntimeState options)")
    if lifesteal is None:
        return fail("missing TryApplyZombieModeTriggerLifesteal body")
    if "player.Health.AddHealth" not in lifesteal:
        return fail("lifesteal helper must heal through the passed-in player")
    if "CharacterMainControl.Main" in lifesteal:
        return fail("lifesteal helper must not re-read CharacterMainControl.Main")

    print("ZombieModeTriggerPlayerLookupSingleReadGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
