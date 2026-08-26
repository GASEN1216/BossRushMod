from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")
SAFE_ZONE = Path("ZombieMode/ZombieModeSafeZoneController.cs")
EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
DEBUG_TOOLS = Path("DebugAndTools/DebugAndTools.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeSafeZoneGuard: missing " + label + " -> " + snippet)
    return 0


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start : index + 1]
    return ""


def main() -> int:
    models = MODELS.read_text(encoding="utf-8") + "\n" + TUNING.read_text(encoding="utf-8")
    safe_zone = SAFE_ZONE.read_text(encoding="utf-8")
    extraction = EXTRACTION.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    debug_tools = DEBUG_TOOLS.read_text(encoding="utf-8")

    for snippet in [
        "public float LastSafeZoneTickTime;",
        "public bool SafeZoneThreatSuppressed;",
        "TickIntervalSeconds = 0.2f",
        "EnemyExclusionPadding = 1.5f",
        "EnemyEjectionClearance = 0.75f",
        "EnemyEjectionNavMeshRadius = 0.5f",
        "public static bool AllowsPortableSafeZoneDeployment",
        "phase == ZombieModeCombatPhase.Settling",
        "phase == ZombieModeCombatPhase.RewardSelection",
    ]:
        result = require(models, snippet, "safe zone state model")
        if result:
            return result

    for snippet in [
        "TickZombieModeSafeZone",
        "Time.unscaledTime - zombieModeRunState.LastSafeZoneTickTime",
        "ZombieModeTuning.SafeZoneTickIntervalSeconds",
        "UpdateZombieModeSafeZonePlayerPresence",
        "SuppressZombieModeSafeZoneThreats",
        "ReleaseZombieModeSafeZoneThreatSuppression",
        "SetZombieModeEnemyThreatSuppressed",
        "zombieModeRunState.PlayerInsideSafeZone = IsZombieModePlayerInsideActiveSafeZone();",
        "zombieModeRunState.SafeZoneThreatSuppressed = shouldSuppress",
        "AICharacterController",
        "marker.SuppressedForceTraceDistance = ai.forceTracePlayerDistance;",
        "marker.HasSuppressedForceTraceDistance = true;",
        "ai.forceTracePlayerDistance = 0f;",
        "ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, marker.SuppressedForceTraceDistance);",
        "marker.HasSuppressedForceTraceDistance = false;",
        "ai.searchedEnemy = null",
        "ai.noticed = false",
        "TryRegisterZombieModeShootStealthBreaker",
        "ItemAgent_Gun.OnMainCharacterShootEvent += OnZombieModeMainCharacterShoot",
        "ItemAgent_Gun.OnMainCharacterShootEvent -= OnZombieModeMainCharacterShoot",
        "OnZombieModeMainCharacterShoot",
        "UpdateZombieModeSafeZonePlayerPresence();",
        "TryMoveZombieModeEnemyOutsideSafeZone",
        "if (!zombieModeRunState.ActiveSafeZoneActive)",
        "IsZombieModePositionInsideSafeZoneEnemyExclusion(enemyTransform.position)",
        "ZombieModeTuning.SafeZoneEnemyExclusionPadding",
        "ZombieModeTuning.SafeZoneEnemyEjectionClearance",
        "ZombieModeTuning.SafeZoneEnemyEjectionNavMeshRadius",
        "!IsZombieModePositionInsideSafeZoneEnemyExclusion(resolved)",
        "suppressThreat",
    ]:
        result = require(safe_zone, snippet, "safe zone tick and stealth breaker")
        if result:
            return result

    for snippet in [
        "TickZombieModeSafeZone();",
        "zombieModeRunState.LastSafeZoneTickTime = 0f;",
        "zombieModeRunState.SafeZoneThreatSuppressed = false;",
        "CanUseZombieModePortableSafeZoneDevice",
        "TryUseZombieModePortableSafeZoneDevice",
        "ZombieModePhaseGuards.AllowsPortableSafeZoneDeployment",
        "ResetZombieModeSafeZoneForReplacement",
        "RemoveZombieModeSafeZoneRunOnlyRecord",
        "CreateZombieModeSafeZone(zombieModeRunState.RunId, false, true, true);",
        "ClearZombieModeEnemiesInsideActiveSafeZone(runId, \"CreateSafeZone\");",
        "private void CleanupZombieModePreparationObjects(int runId, bool preservePortableSafeZone = false)",
        "bool keepPortableSafeZone = preservePortableSafeZone &&",
    ]:
        result = require(extraction, snippet, "safe zone lifecycle")
        if result:
            return result

    drops = Path("ZombieMode/ZombieModeDropsAndPerformance.cs").read_text(encoding="utf-8")
    for signature in [
        "private void RecycleZombieModeSafeZoneBoundTemporaryNpcs(int runId)",
        "private void RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(int runId)",
    ]:
        body = extract_method(drops, signature)
        if not body or "RemoveZombieModeRunOnlyObjectRecord(npc.GameObject);" not in body:
            return fail("safe-zone NPC recycle must remove its run-only record before destroying the object")

    for snippet in [
        "TickZombieModeSafeZone();",
        "if (zombieModeRunState.ActiveSafeZoneActive)",
        "CleanupZombieModePreparationObjects(runId, preservePortableSafeZone);",
        "CleanupZombieModePreparationObjects(runId);",
    ]:
        result = require(waves, snippet, "wave controller safe zone tick")
        if result:
            return result

    for snippet in [
        "TryHandleZombieModeSafeZonePlayerAttack",
        "!IsZombieModePlayerInsideActiveSafeZone()",
        "ZombieModePhaseGuards.AllowsSafeZone(zombieModeRunState.CombatPhase)",
        "CancelZombieModeSafeZone(runId, \"PlayerAttack\");",
    ]:
        result = require(waves, snippet, "damage-driven safe zone stealth break")
        if result:
            return result

    hurt_handler = extract_method(
        waves,
        "private void HandleZombieModeHealthHurt(int runId, Health health, DamageInfo damageInfo)",
    )
    stealth_index = hurt_handler.find("TryHandleZombieModeSafeZonePlayerAttack")
    marker_index = hurt_handler.find("TryGetZombieModeKnownEnemyMarker")
    if stealth_index < 0 or marker_index < 0 or stealth_index < marker_index:
        return fail(
            "ZombieModeSafeZoneGuard: non-lethal stealth break must require a known zombie marker"
        )

    dead_handler = extract_method(
        waves,
        "private void HandleZombieModeHealthDead(int runId, Health health, DamageInfo damageInfo)",
    )
    lethal_stealth_index = dead_handler.find("TryHandleZombieModeSafeZonePlayerAttack")
    death_settled_index = dead_handler.find("marker.DeathSettled = true;")
    unregister_index = dead_handler.find("UnregisterZombieModeEnemyInstanceId")
    if (
        lethal_stealth_index < 0
        or death_settled_index < 0
        or unregister_index < 0
        or lethal_stealth_index > death_settled_index
        or lethal_stealth_index > unregister_index
    ):
        return fail(
            "ZombieModeSafeZoneGuard: lethal stealth break must run before death settlement and marker unregister"
        )

    for snippet in [
        "if (ShouldSuppressZombieModeEnemyAggroForSafeZone())",
        "SetZombieModeEnemyThreatSuppressed(enemy.gameObject, marker, true);",
        "TryMoveZombieModeEnemyOutsideSafeZone(",
        "RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position);",
        "RegisterEnemyRecoveryAnchor(boss, boss.transform.position);",
    ]:
        result = require(spawner, snippet, "spawn aggro suppression")
        if result:
            return result

    if "if (!DevModeEnabled) return;" in safe_zone:
        return fail("ZombieModeSafeZoneGuard: zombie stealth shoot breaker must not depend on DevMode")

    if "ItemAgent_Gun.OnMainCharacterShootEvent += OnZombieModeMainCharacterShoot" in debug_tools:
        return fail("ZombieModeSafeZoneGuard: zombie stealth breaker must live in ZombieMode, not DevMode debug")

    hud = Path("ZombieMode/ZombieModeHudController.cs").read_text(encoding="utf-8")
    if "zombieModeRunState.PreparationTimer > 0f &&" not in hud:
        return fail("ZombieModeSafeZoneGuard: combat portable safe zone must not flash as a zero-second preparation timer")

    print("ZombieModeSafeZoneGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
