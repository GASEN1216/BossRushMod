"""Guard: Zombie mode safe zone must have an obvious in-world boundary."""

from pathlib import Path


EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")


def fail(message):
    print("ZombieModeSafeZoneVisualGuard: FAIL - " + message)
    raise SystemExit(1)


def require(text, needle, message):
    if needle not in text:
        fail(message)


def forbid(text, needle, message):
    if needle in text:
        fail(message)


def main():
    text = EXTRACTION.read_text(encoding="utf-8")

    require(text, "AttachZombieModeSafeZoneBoundaryVisual", "safe zone must attach a boundary visual")
    require(text, "ZombieMode_SafeZone_BoundaryRing", "safe zone boundary ring must be named")
    require(text, "LineRenderer", "safe zone boundary must use a LineRenderer ring")
    require(text, "line.positionCount = 97;", "safe zone ring must have enough segments")
    require(text, "line.widthMultiplier = 0.10f;", "safe zone boundary line must stay thin")
    require(text, "ZombieModeSafeZoneMaterialOwner", "safe zone boundary line material must be cleaned up")
    forbid(text, "ZombieModeSafeZoneBoundaryPulse", "safe zone boundary line must not pulse")
    forbid(text, "line.widthMultiplier = Mathf.Lerp", "safe zone boundary line width must not animate")
    forbid(text, "Mathf.Sin(Time.unscaledTime", "safe zone boundary line must not pulse with time")
    forbid(text, "AttachZombieModeSafeZoneParticleHalo", "safe zone boundary must not attach particle effects")
    forbid(text, "ZombieMode_SafeZone_BoundaryParticles", "safe zone particle halo must be removed")
    forbid(text, "ParticleSystem", "safe zone boundary must not use particle effects")

    print("ZombieModeSafeZoneVisualGuard: PASS")


if __name__ == "__main__":
    main()
