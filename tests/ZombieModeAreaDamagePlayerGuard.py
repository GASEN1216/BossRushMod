"""ZombieModeAreaDamagePlayerGuard: player-only area damage must not enter Health.Hurt with null DamageInfo source."""

from pathlib import Path
import re
import sys


POLLUTION = Path("ZombieMode/ZombieModePollution.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")
WAVE = Path("ZombieMode/ZombieModeWaveController.cs")


def fail(message: str) -> int:
    print("ZombieModeAreaDamagePlayerGuard: FAIL - " + message)
    return 1


def extract_method(text: str, method_name: str, signature_hint: str = "") -> str:
    pattern = r"\b" + re.escape(method_name) + r"\s*\([^)]*\)\s*\{"
    for match in re.finditer(pattern, text):
        start = match.start()
        prefix = text[max(0, start - 240):start]
        body_start = match.end() - 1
        if signature_hint and signature_hint not in text[start:body_start]:
            continue
        depth = 0
        for index in range(body_start, len(text)):
            char = text[index]
            if char == "{":
                depth += 1
            elif char == "}":
                depth -= 1
                if depth == 0:
                    return prefix + text[start:index + 1]
    return ""


def main() -> int:
    pollution = POLLUTION.read_text(encoding="utf-8")
    boss = BOSS.read_text(encoding="utf-8")
    wave = WAVE.read_text(encoding="utf-8")

    area_damage = extract_method(
        pollution,
        "DealZombieModeAreaDamageToPlayer",
        "CharacterMainControl source",
    )
    if not area_damage:
        return fail("source-aware DealZombieModeAreaDamageToPlayer overload not found")

    forbidden = [
        "new DamageInfo()",
        "player.Health.Hurt(damageInfo)",
    ]
    for token in forbidden:
        if token in area_damage:
            return fail("player-only area damage still uses unsafe Health.Hurt path: " + token)

    required = [
        "CharacterMainControl damageSource = source != null ? source : player;",
        "DamageInfo damageInfo = new DamageInfo(damageSource);",
        "damageInfo.isFromBuffOrEffect = source == null;",
        "DamageReceiver receiver = player.mainDamageReceiver;",
        "receiver.Hurt(damageInfo);",
    ]
    for token in required:
        if token not in area_damage:
            return fail("player-only area damage missing safe receiver/source token: " + token)

    stealth_break = extract_method(wave, "TryProcessZombieModeSafeZoneStealthBreak")
    if not stealth_break:
        return fail("safe zone stealth break method not found")
    if "damageInfo.isFromBuffOrEffect" not in stealth_break:
        return fail("safe zone stealth break must ignore internal effect/self-source damage")

    runtime = boss[boss.find("public sealed class ZombieModeAreaTickRuntime"):]
    for token in [
        "private CharacterMainControl source;",
        "CharacterMainControl newSource",
        "source = newSource;",
        "inst.DealZombieModeRuntimeAreaDamageToPlayer(RuntimeRunId, source, transform.position, radius, tickDamage);",
    ]:
        if token not in runtime:
            return fail("area tick runtime must preserve the real source -> " + token)

    for token in [
        "SpawnZombieModeCorruptionZone(runId, boss, player.transform.position);",
        "SpawnZombieModePoisonPathSegment(runId, boss, boss.transform.position);",
        "SpawnZombieModeDeathCloud(runId, character, character.transform.position);",
        "runtime.Initialize(\n                runId,\n                source,",
        "DealZombieModeAreaDamageToPlayer(\n                runId,\n                character,",
    ]:
        if token not in boss and token not in pollution:
            return fail("death/corruptor area damage must pass its zombie source -> " + token)

    print("ZombieModeAreaDamagePlayerGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
