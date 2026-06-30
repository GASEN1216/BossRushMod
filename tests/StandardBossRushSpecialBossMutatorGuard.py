"""Guard: standard BossRush special-boss spawn branches must apply mutators.

The shared EnemySpawnCore path already applies MutatorManager.ApplyToEnemy to
managed special bosses. The legacy standard BossRush direct spawn path must do
the same before each early return, otherwise custom bosses miss enabled
mutator enemy buffs while normal bosses receive them.
"""

from pathlib import Path
import sys


SOURCE = Path("ModBehaviour.cs")


def fail(message: str) -> int:
    print("StandardBossRushSpecialBossMutatorGuard: FAIL - " + message)
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


def assert_branch_applies_mutator(method: str, marker: str, return_expr: str) -> str | None:
    marker_index = method.find(marker)
    if marker_index < 0:
        return "missing special boss marker -> " + marker

    return_index = method.find(return_expr, marker_index)
    if return_index < 0:
        return "missing early return after marker -> " + marker

    branch = method[marker_index:return_index]
    if "MutatorManager.ApplyToEnemy" not in branch:
        return "special boss branch returns before applying mutators -> " + marker

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    method = extract_block(text, "private async UniTask<CharacterMainControl> SpawnEnemyAtPositionAsync(")
    if method is None:
        return fail("missing SpawnEnemyAtPositionAsync")

    cases = [
        ("IsDragonDescendantPreset(preset)", "return dragonBoss;"),
        ("IsDragonKingPreset(preset)", "return dragonKing;"),
        ("IsPhantomWitchPreset(preset)", "return phantomWitch;"),
    ]
    for marker, return_expr in cases:
        issue = assert_branch_applies_mutator(method, marker, return_expr)
        if issue is not None:
            return fail(issue)

    print("StandardBossRushSpecialBossMutatorGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
