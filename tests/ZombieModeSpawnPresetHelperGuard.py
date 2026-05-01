from pathlib import Path
import sys


SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    spawner = SPAWNER.read_text(encoding="utf-8")

    if "SpawnEnemyCore" in spawner:
        return fail("ZombieModeSpawnPresetHelperGuard: Zombie spawner must not call SpawnEnemyCore")

    helper = extract_method(spawner, "private async UniTask<CharacterMainControl> TryCreateZombieModePresetCharacterAsync")
    if not helper:
        return fail("ZombieModeSpawnPresetHelperGuard: missing TryCreateZombieModePresetCharacterAsync")

    for token in [
        "preset.CreateCharacterAsync(position, Vector3.forward, SceneManager.GetActiveScene().buildIndex, null, false)",
        "if (!IsZombieModeRunValid(runId))",
        "Destroy(character.gameObject)",
        "DevLog(failureLogPrefix + e.Message)",
    ]:
        if token not in helper:
            return fail("ZombieModeSpawnPresetHelperGuard: helper missing token -> " + token)

    normal = extract_method(spawner, "private async UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync")
    boss = extract_method(spawner, "private async UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync")
    if "TryCreateZombieModePresetCharacterAsync(runId, preset, position, \"[ZombieMode] 丧尸生成失败: \")" not in normal:
        return fail("ZombieModeSpawnPresetHelperGuard: normal zombie spawn does not use preset helper")

    if "TryCreateZombieModePresetCharacterAsync(runId, preset, position, \"[ZombieMode] Boss 生成失败: \")" not in boss:
        return fail("ZombieModeSpawnPresetHelperGuard: boss spawn does not use preset helper")

    if "CreateCharacterAsync(" in normal or "CreateCharacterAsync(" in boss:
        return fail("ZombieModeSpawnPresetHelperGuard: CreateCharacterAsync should only live in the preset helper")

    print("ZombieModeSpawnPresetHelperGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
