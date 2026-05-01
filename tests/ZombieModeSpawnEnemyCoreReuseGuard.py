"""ZombieModeSpawnEnemyCoreReuseGuard: 丧尸模式刷怪必须走 SpawnEnemyCore。"""
from pathlib import Path
import re
import sys


SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")


def fail(msg: str) -> int:
    print(msg)
    return 1


def extract_method_body(text: str, marker: str) -> str:
    m = re.search(marker, text)
    if not m:
        return ""
    body_start = text.find("{", m.end())
    if body_start < 0:
        return ""
    depth = 0
    for i in range(body_start, len(text)):
        ch = text[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[body_start:i + 1]
    return ""


def main() -> int:
    text = SPAWNER.read_text(encoding="utf-8")
    for method_pattern, label in [
        (r"TrySpawnZombieModeNormalZombieAsync\b", "TrySpawnZombieModeNormalZombieAsync"),
        (r"TrySpawnZombieModeBossAsync\b", "TrySpawnZombieModeBossAsync"),
    ]:
        body = extract_method_body(text, method_pattern)
        if not body:
            return fail("ZombieModeSpawnEnemyCoreReuseGuard: 找不到 " + label)
        if "preset.CreateCharacterAsync" in body:
            return fail("ZombieModeSpawnEnemyCoreReuseGuard: " + label + " 仍在直接调用 CreateCharacterAsync")
        if "SpawnEnemyCore(" not in body:
            return fail("ZombieModeSpawnEnemyCoreReuseGuard: " + label + " 没有调用 SpawnEnemyCore")
    return 0


if __name__ == "__main__":
    sys.exit(main())
