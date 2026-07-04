"""Guard: BossRush custom special Boss deferred activation must stay opt-in."""

from pathlib import Path
import sys


SPAWN_CORE = Path("Utilities/EnemySpawnCore.cs")
DRAGON_DESCENDANT = Path("Integration/DragonDescendant/DragonDescendantBoss.cs")
DRAGON_KING = Path("Integration/DragonKing/DragonKingBoss.cs")
PHANTOM_WITCH = Path("Integration/PhantomWitch/PhantomWitchBoss.cs")


def fail(message: str) -> int:
    print("ManagedSpecialBossDeferredActivationGuard: FAIL - " + message)
    return 1


def require(text: str, needle: str, message: str) -> int | None:
    if needle not in text:
        return fail(message)
    return None


def main() -> int:
    spawn_core = SPAWN_CORE.read_text(encoding="utf-8")
    dragon_descendant = DRAGON_DESCENDANT.read_text(encoding="utf-8")
    dragon_king = DRAGON_KING.read_text(encoding="utf-8")
    phantom_witch = PHANTOM_WITCH.read_text(encoding="utf-8")

    checks = (
        (spawn_core, "deferActivationUntilNextFrame: deferActivationUntilNextFrame", "spawn core must forward the deferred-activation flag into managed special Boss spawns"),
        (dragon_descendant, "bool deferActivationUntilNextFrame = false", "dragon descendant spawn signature must keep deferred activation opt-in"),
        (dragon_descendant, "if (deferActivationUntilNextFrame)", "dragon descendant activation must branch on the explicit opt-in flag"),
        (dragon_king, "bool deferActivationUntilNextFrame = false", "dragon king spawn signature must keep deferred activation opt-in"),
        (dragon_king, "if (deferActivationUntilNextFrame)", "dragon king activation must branch on the explicit opt-in flag"),
        (phantom_witch, "bool deferActivationUntilNextFrame = false", "phantom witch spawn signature must keep deferred activation opt-in"),
        (phantom_witch, "if (deferActivationUntilNextFrame)", "phantom witch activation must branch on the explicit opt-in flag"),
        (dragon_descendant, "character.gameObject.SetActive(true);", "dragon descendant activation point must remain explicit"),
        (dragon_king, "character.gameObject.SetActive(true);", "dragon king activation point must remain explicit"),
        (phantom_witch, "character.gameObject.SetActive(true);", "phantom witch activation point must remain explicit"),
    )

    for text, needle, message in checks:
        result = require(text, needle, message)
        if result is not None:
            return result

    print("ManagedSpecialBossDeferredActivationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
