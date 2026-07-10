"""Guard: Firework split shots must stagger activation without reducing the visual volley."""

from pathlib import Path
import sys


PROFILE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
AGENT_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")
RUNTIME_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs")
DEBUG_SOURCE = Path("DebugAndTools/DebugAndTools.cs")
CACHE_SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs")


def fail(message: str) -> int:
    print("DragonKingBossGunFireworkActivationStaggerGuard: FAIL - " + message)
    return 1


def profile_block(text: str) -> str | None:
    start = text.find("Id = DragonKingBossGunProfileId.Firework")
    if start < 0:
        return None

    end = text.find("new DragonKingBossGunShotProfile", start + 1)
    return text[start:] if end < 0 else text[start:end]


def main() -> int:
    profiles = PROFILE_SOURCE.read_text(encoding="utf-8-sig")
    agent = AGENT_SOURCE.read_text(encoding="utf-8-sig")
    runtime = RUNTIME_SOURCE.read_text(encoding="utf-8-sig")
    debug = DEBUG_SOURCE.read_text(encoding="utf-8-sig")
    cache_runtime = CACHE_SOURCE.read_text(encoding="utf-8-sig")
    firework = profile_block(profiles)
    if firework is None:
        return fail("missing Firework profile")

    for snippet in [
        "public float SplitActivationStagger;",
        "SplitCount = 12",
        "SplitActivationDelay = 0.08f",
        "SplitActivationStagger = 0.02f",
    ]:
        if snippet not in profiles if snippet.startswith("public") else snippet not in firework:
            return fail("missing firework stagger invariant -> " + snippet)

    for snippet in [
        "float activationDelay = ResolveSplitActivationDelay();",
        "if (splitActivationTimer >= activationDelay)",
        "profile.SplitActivationDelay + projectileIndex * profile.SplitActivationStagger",
        "private const int MaxPooledFireworkSparkEffects = 24;",
        "private static FireworkSparkEffectHandle RentFireworkSparkEffect()",
        "private const int MaxPooledFireworkBloomEffects = 24;",
        "private static FireworkBloomEffectHandle RentFireworkBloomEffect()",
        "private static void ClearFireworkBloomEffectPool()",
        "emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 72) });",
        "flashEmission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 10) });",
        "bool staggerFireworkCollisionCheck = secondaryProjectile",
        "((Time.frameCount + projectileIndex) & 1) == 0",
        "Vector3 castDelta = predictedEnd - fireworkCollisionCheckStart;",
        "fireworkCollisionCheckStart = predictedEnd;",
        "private static void ClearFireworkSparkEffectPool()",
        "effect.Play(position, ResolveFireworkColor(projectileIndex + shotId, true), fireworkSparkEffectPoolGeneration);",

    ]:
        if snippet not in agent:
            return fail("missing staggered activation logic -> " + snippet)

    for snippet in [
        "private const int MaxPooledFireExplosionEffects = 96;",
        "private static FireExplosionEffectHandle RentFireExplosionEffect()",
        "FireExplosionEffectHandle fireFx = RentFireExplosionEffect();",
        "int ignoredReceiverId = -1",
        "receiverId == ignoredReceiverId || SharedReceiverIdSet.Contains(receiverId)",
    ]:
        if snippet not in runtime:
            return fail("missing pooled fire explosion logic -> " + snippet)
    for snippet in [
        "private const int MaxPooledFireExplosionEffects = 96;",
        "internal static void RequestFireExplosionEffectWarmup(int desiredPoolSize)",
        "WarmFireExplosionEffectPoolAsync().Forget();",
        "DragonKingBossGunRuntime.RequestFireExplosionEffectWarmup(Mathf.Max(12, profile.SplitCount * 2));",
    ]:
        if snippet not in runtime and snippet not in agent:
            return fail("missing firework pool warmup invariant -> " + snippet)

    if "if (!HardcodedDevModeEnabled || !DevModeEnabled) return;" not in debug:
        return fail("shoot debug listener is not hard-disabled outside development mode")
    if "profile.SplitIgnoreSourceOnSplit ? splitSourceReceiverId : -1" not in agent:
        return fail("split source target is not excluded from secondary explosion damage")

    if "ClearFireExplosionEffectPool();" not in cache_runtime:
        return fail("missing pooled fire explosion cache cleanup")
    print("DragonKingBossGunFireworkActivationStaggerGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())