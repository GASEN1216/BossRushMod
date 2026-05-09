from pathlib import Path
import sys


ROOTS = [
    Path("ZombieMode"),
    Path("Utilities"),
]
COMPILE_SCRIPT = Path("compile_official.bat")

BANNED_TOKENS = [
    "ZombieModePerformanceTier",
    "PerformanceTierAdjuster",
    "SoftProtect",
    "ExtremeProtect",
    "PerfTier",
    "RefreshZombieModeProjectileRewardPerformanceState",
    "RecycleZombieModeFarEnemiesForPerformance",
    "CanRecycleZombieModeEnemyForPerformance",
    "RecycleZombieModeEnemyForPerformance",
    "RecycledForPerformance",
    "SplitterBossDeathSpawnCountSoftProtect",
    "SoftSpawnMultiplier",
    "PerformanceFarDistance",
    "PerformanceEvalIntervalSeconds",
    "LastPerformanceEvalTime",
    "EvalIntervalSeconds",
]


def fail(message: str) -> int:
    print("ZombieModeNoPerformanceGameplayScalingGuard: FAIL - " + message)
    return 1


def production_files():
    for root in ROOTS:
        if not root.exists():
            continue
        for path in root.rglob("*.cs"):
            yield path
    if COMPILE_SCRIPT.exists():
        yield COMPILE_SCRIPT


def main() -> int:
    offenders = []
    for path in production_files():
        text = path.read_text(encoding="utf-8", errors="ignore")
        for token in BANNED_TOKENS:
            if token in text:
                offenders.append(f"{path}: {token}")

    if offenders:
        return fail("performance-tier gameplay scaling must be removed -> " + "; ".join(offenders[:12]))

    print("ZombieModeNoPerformanceGameplayScalingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
