"""
Guard: BossRush enemy cleanup must not remove active Death Wraiths.
"""

from pathlib import Path
import sys


ENEMY_MAINTENANCE_SOURCE = Path("WavesArena/WavesArenaEnemyMaintenance.cs")
DEATH_WRAITH_SOURCE = Path("Integration/DeathWraith/DeathWraithSystem.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def require_before(block: str, required: str, later: str, message: str) -> int:
    required_index = block.find(required)
    later_index = block.find(later)

    if required_index == -1:
        return fail(message + " missing required snippet -> " + required)

    if later_index == -1:
        return fail(message + " missing reference snippet -> " + later)

    if required_index > later_index:
        return fail(message + " must check Death Wraiths before normal cleanup filtering")

    return 0


def main() -> int:
    enemy_maintenance_text = ENEMY_MAINTENANCE_SOURCE.read_text(encoding="utf-8")
    death_text = DEATH_WRAITH_SOURCE.read_text(encoding="utf-8")

    helper_block = extract_block(
        death_text,
        "private bool IsDeathWraithCharacter_DeathWraith(CharacterMainControl character)",
    )
    if not helper_block:
        return fail("DeathWraithBossRushClearGuard: missing IsDeathWraithCharacter_DeathWraith helper")

    helper_required = [
        "activeWraithRaidIdByCharacter.ContainsKey(character)",
        '"BossRush_DeathWraith_"',
        "DEATH_WRAITH_NAME_KEY_PREFIX",
    ]
    for snippet in helper_required:
        if snippet not in helper_block:
            return fail("DeathWraithBossRushClearGuard: helper missing snippet -> " + snippet)

    force_block = extract_block(enemy_maintenance_text, "private void ForceKillAllEnemies()")
    if not force_block:
        return fail("DeathWraithBossRushClearGuard: missing ForceKillAllEnemies block")

    result = require_before(
        force_block,
        "if (IsDeathWraithCharacter_DeathWraith(c))",
        "bool isPet = false;",
        "DeathWraithBossRushClearGuard: ForceKillAllEnemies",
    )
    if result != 0:
        return result

    clear_block = extract_block(enemy_maintenance_text, "private void ClearEnemiesForBossRush()")
    if not clear_block:
        return fail("DeathWraithBossRushClearGuard: missing ClearEnemiesForBossRush block")

    result = require_before(
        clear_block,
        "if (IsDeathWraithCharacter_DeathWraith(c))",
        "bool isEggDuck = false;",
        "DeathWraithBossRushClearGuard: ClearEnemiesForBossRush",
    )
    if result != 0:
        return result

    if clear_block.find("if (IsDeathWraithCharacter_DeathWraith(c))") > clear_block.find("_reusableDestroyList.Add(c.gameObject);"):
        return fail("DeathWraithBossRushClearGuard: Death Wraith guard must run before destroy-list collection")

    print("DeathWraithBossRushClearGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
