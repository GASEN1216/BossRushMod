"""ZombieModeBossLifecycleGuard: BossInstance 必须通过 Lifecycle 子对象暴露运行期追踪字段。"""

from pathlib import Path
import re
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
USERS = [
    Path("ZombieMode/ZombieModeBossController.cs"),
    Path("ZombieMode/ZombieModeSpawner.cs"),
    Path("ZombieMode/ZombieModeWaveController.cs"),
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    model_text = MODELS.read_text(encoding="utf-8")

    for required in [
        "public sealed class ZombieModeBossLifecycleTrack",
        "public bool Alive;",
        "public Vector3 LastKnownPosition;",
        "public float LastReachableTime;",
        "public float LastHurtTime;",
        "public readonly ZombieModeBossLifecycleTrack Lifecycle",
    ]:
        if required not in model_text:
            return fail("ZombieModeBossLifecycleGuard: model missing -> " + required)

    for forbidden in [
        "public bool LootSettled;",
        "public bool PointsSettled;",
        "public bool RuntimeRegistered;",
    ]:
        if forbidden in model_text:
            return fail("ZombieModeBossLifecycleGuard: dead field still present -> " + forbidden)

    pattern = re.compile(r"\binstance\.(Alive|LootSettled|PointsSettled|RuntimeRegistered|LastKnownPosition|LastReachableTime|LastHurtTime)\b")
    for path in USERS:
        text = path.read_text(encoding="utf-8")
        match = pattern.search(text)
        if match:
            return fail("ZombieModeBossLifecycleGuard: " + str(path) + " 仍直接访问 instance." + match.group(1))

    print("ZombieModeBossLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
