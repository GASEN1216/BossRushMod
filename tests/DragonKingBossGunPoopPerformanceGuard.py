"""Guard: Poop ammo poison pool must keep its lightweight runtime budget."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunPoopPerformanceGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    required_snippets = [
        "private const int MaxActivePoisonZones = 6;",
        "private const int PoisonZoneMaxParticles = 36;",
        "private const float PoisonZoneEmissionMin = 14f;",
        "private const float PoisonZoneEmissionMax = 24f;",
        "if (profile == null || profile.GroundZoneElement != ElementTypes.poison)",
        "while (activePoisonZones.Count >= MaxActivePoisonZones)",
        "if (profile.GroundZoneElement != ElementTypes.poison)",
        "main.maxParticles = profile.GroundZoneElement == ElementTypes.poison ? PoisonZoneMaxParticles : 72;",
        "float emissionMin = profile.GroundZoneElement == ElementTypes.poison ? PoisonZoneEmissionMin : 22f;",
        "float emissionMax = profile.GroundZoneElement == ElementTypes.poison ? PoisonZoneEmissionMax : 42f;",
    ]

    for snippet in required_snippets:
        if snippet not in text:
            return fail("missing snippet -> " + snippet)

    print("DragonKingBossGunPoopPerformanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
