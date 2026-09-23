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
    # 2026-09-23 审美审查 VA-28：边线改用 ZombieModeZoneVisuals 的共享材质（生命周期归 BossRushFxMaterials），
    # 不再按环 new Material + ZombieModeSafeZoneMaterialOwner 销毁。守住「不按环实例化材质」：
    # line.material 的 getter / setter 都会给每个环复制一份材质，而这份副本没人销毁就是泄漏。
    require(text, "line.sharedMaterial = lineMaterial;", "safe zone boundary line must use the shared ring material")
    require(text, "ZombieModeZoneVisuals.GetRingMaterial()", "safe zone boundary line material must come from the shared zone visuals")
    forbid(text, "line.material =", "safe zone boundary line must not instantiate a per-ring material (leaks without an owner)")
    forbid(text, "ZombieModeSafeZoneBoundaryPulse", "safe zone boundary line must not pulse")
    forbid(text, "line.widthMultiplier = Mathf.Lerp", "safe zone boundary line width must not animate")
    forbid(text, "Mathf.Sin(Time.unscaledTime", "safe zone boundary line must not pulse with time")
    forbid(text, "AttachZombieModeSafeZoneParticleHalo", "safe zone boundary must not attach particle effects")
    forbid(text, "ZombieMode_SafeZone_BoundaryParticles", "safe zone particle halo must be removed")
    forbid(text, "ParticleSystem", "safe zone boundary must not use particle effects")

    print("ZombieModeSafeZoneVisualGuard: PASS")


if __name__ == "__main__":
    main()
