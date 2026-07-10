"""Guard: ZombieMode death explosions must use the native explosion path."""

from pathlib import Path
import re
import sys


RUNTIME = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")


def fail(message: str) -> int:
    print("ZombieModeDeathExplosionVisualGuard: FAIL - " + message)
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
    runtime = RUNTIME.read_text(encoding="utf-8-sig")
    boss = BOSS.read_text(encoding="utf-8-sig")

    elite_death = extract_method_body(runtime, "private void HandleZombieModeEliteDeathEffects(")
    if elite_death is None:
        return fail("missing HandleZombieModeEliteDeathEffects body")

    burst_match = re.search(
        r"if\s*\(marker\.EliteAffixes\.Contains\(ZombieModeEliteAffix\.Burst\)\)\s*\{(?P<body>.*?)\n\s*\}",
        elite_death,
        re.S,
    )
    if burst_match is None:
        return fail("missing Burst death affix block")

    burst_body = burst_match.group("body")
    if "DealZombieModeAreaDamageToPlayer(" in burst_body:
        return fail("Burst death affix reverted to invisible player-only area damage")
    if "DealZombieModeExplosionAreaDamage(" not in burst_body:
        return fail("Burst death affix must use native explosion path")

    boss_death = extract_method_body(boss, "private void HandleZombieModeBossDeathEffects(")
    if boss_death is None:
        return fail("missing HandleZombieModeBossDeathEffects body")

    for kind in ["Splitter", "Titan"]:
        kind_index = boss_death.find("ZombieModeBossKind." + kind)
        if kind_index < 0:
            return fail("missing boss death block for " + kind)

    if re.search(r"DealZombieModeAreaDamageToPlayer\(\s*runId,\s*character,", boss_death):
        return fail("boss death explosions must not use player-only area damage")

    for token in [
        "DealZombieModeExplosionAreaDamage(\n                    runId,\n                    character,\n                    character.transform.position,\n                    ZombieModeTuning.SplitterBossDeathRadius",
        "DealZombieModeExplosionAreaDamage(runId, character, character.transform.position, 6f, 60f);",
    ]:
        if token not in boss_death:
            return fail("boss death explosion missing token -> " + token)

    print("ZombieModeDeathExplosionVisualGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
