"""Guard: ZombieMode runtime components should cache their owning character lookup."""

from pathlib import Path
import sys


POLLUTION = Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs")
REWARDS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print("ZombieModeRuntimeCharacterCacheGuard: FAIL - " + message)
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


def extract_method_body(text: str, signature: str) -> str | None:
    block = extract_block(text, signature)
    if block is None:
        return None

    brace_start = block.find("{")
    return block[brace_start:] if brace_start >= 0 else None


def require_cached_character(class_block: str, field: str, helper: str, class_name: str) -> int:
    if field not in class_block:
        return fail(f"{class_name} missing cached character field")

    if helper not in class_block:
        return fail(f"{class_name} missing cached character helper")

    helper_body = extract_method_body(class_block, helper)
    if helper_body is None:
        return fail(f"{class_name} missing helper body")

    if "GetComponent<CharacterMainControl>()" not in helper_body:
        return fail(f"{class_name} helper does not refresh from GetComponent")

    helper_call = helper.replace("private CharacterMainControl ", "") + ";"
    for method_name in ("private void Update()", "private void EnsureModifiers()"):
        body = extract_method_body(class_block, method_name)
        if body is not None:
            if "GetComponent<CharacterMainControl>()" in body:
                return fail(f"{class_name}.{method_name} still performs direct GetComponent")
            if helper_call not in body:
                return fail(f"{class_name}.{method_name} does not use cached helper")

    return 0


def main() -> int:
    pollution_text = POLLUTION.read_text(encoding="utf-8-sig")
    rewards_text = REWARDS.read_text(encoding="utf-8-sig")

    commander_target = extract_block(pollution_text, "public sealed class ZombieModeCommanderAuraTargetRuntime")
    if commander_target is None:
        return fail("missing ZombieModeCommanderAuraTargetRuntime")
    result = require_cached_character(
        commander_target,
        "private CharacterMainControl targetCharacter;",
        "private CharacterMainControl GetTargetCharacter()",
        "ZombieModeCommanderAuraTargetRuntime",
    )
    if result:
        return result

    regeneration = extract_block(pollution_text, "public sealed class ZombieModeRegenerationAffixRuntime")
    if regeneration is None:
        return fail("missing ZombieModeRegenerationAffixRuntime")
    result = require_cached_character(
        regeneration,
        "private CharacterMainControl cachedCharacter;",
        "private CharacterMainControl GetCachedCharacter()",
        "ZombieModeRegenerationAffixRuntime",
    )
    if result:
        return result

    stasis = extract_block(rewards_text, "public sealed class ZombieModeEnemyStasisRuntime")
    if stasis is None:
        return fail("missing ZombieModeEnemyStasisRuntime")
    result = require_cached_character(
        stasis,
        "private CharacterMainControl cachedEnemy;",
        "private CharacterMainControl GetCachedEnemy()",
        "ZombieModeEnemyStasisRuntime",
    )
    if result:
        return result

    print("ZombieModeRuntimeCharacterCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
