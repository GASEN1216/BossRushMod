"""Guard: Poop ammo poison pool must keep its lightweight runtime budget."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunPoopPerformanceGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig").replace("\r\n", "\n")

    required_snippets = [
        "private const int MaxActivePoisonZones = 6;",
        "private const int PoisonZoneMaxParticles = 36;",
        "private const float PoisonZoneEmissionMin = 14f;",
        "private const float PoisonZoneEmissionMax = 24f;",
        # 2026-09-29 水弹的灵泉水洼（ghost）并入同一份轻量预算：同屏上限、低粒子、不挂灯、同目标结算锁。
        "return element == ElementTypes.poison || element == ElementTypes.ghost;",
        "if (profile == null || !UsesSharedZoneBudget(profile.GroundZoneElement))",
        "while (activePoisonZones.Count >= MaxActivePoisonZones)",
        "if (!UsesSharedZoneBudget(profile.GroundZoneElement))\n            {\n                CreateZoneLight(zoneColor);",
        "bool sharedBudgetZone = UsesSharedZoneBudget(profile.GroundZoneElement);",
        "main.maxParticles = sharedBudgetZone ? PoisonZoneMaxParticles : 72;",
        "float emissionMin = sharedBudgetZone ? PoisonZoneEmissionMin : 22f;",
        "float emissionMax = sharedBudgetZone ? PoisonZoneEmissionMax : 42f;",
        "if (UsesSharedZoneBudget(profile.GroundZoneElement) && !TryClaimPoisonTick(receiverId))",
    ]

    for snippet in required_snippets:
        if snippet not in text:
            return fail("missing snippet -> " + snippet)

    print("DragonKingBossGunPoopPerformanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
