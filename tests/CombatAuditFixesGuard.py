"""2026-09-21/22 战斗审计：效果归因与异步请求所有权。行为另由隔离夹具验证。"""
from pathlib import Path
import re
from cs_source_util import clean_source


def member(path, marker):
    source = clean_source(Path(path).read_text(encoding="utf-8-sig"))
    start = source.index(marker)
    end = source.index("{", start) + 1
    depth = 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[start:end])


CHECKS = [
    ("Integration/Bonus/DragonSetBonus.cs", "private void DamageEnemiesInRange(", "!Team.IsEnemy(owner.Team, targetTeam)"),
    ("Integration/Bonus/DragonSetBonus.cs", "private void ApplyLavaDamageToEnemy(", "damageInfo.isFromBuffOrEffect = true;"),
    ("Integration/ReverseScale/ReverseScaleAbilityManager.cs", "private IEnumerator TrackingBolt(", "if (!IsPrismaticBoltEnemy(enemyHealth, boltOwner)) continue;"),
    ("Integration/ReverseScale/ReverseScaleAbilityManager.cs", "private Transform FindNearestEnemyOptimized(", "if (!IsPrismaticBoltEnemy(health, CharacterMainControl.Main)) continue;"),
    ("Integration/NewWeapons/ViperDagger/ViperDaggerRuntime.cs", "private static void OnHurt(", "damageInfo.isFromBuffOrEffect) return;"),
    ("Common/Equipment/EquipmentAbilityManager.cs", "public static void CleanupStatic(", "Destroy(manager.gameObject);"),
    ("Integration/FlightTotem/FlightAbilityManager.cs", "public override bool TryExecuteAbility(", "!IsGameplayInputAllowed()) return false;"),
    ("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs", "public static void WarmupProjectileCache(", "!IsProjectileWarmupOwnerValid(CharacterMainControl.Main)"),
    ("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs", "private static async UniTask<Projectile> ExtractBossRedProjectileAsync(", "if (generation != projectileWarmupGeneration || !IsProjectileWarmupOwnerValid(owner)) return null;"),
    ("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs", "internal static void ClearFireExplosionEffectPool(", "fireExplosionEffectWarmupRunning = false; requestedFireExplosionEffectPoolSize = 0;"),
    ("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs", "private void ApplyDeathBuff(", "if (!IsTraceReceiverUsable(receiver, receiver.health != null ? receiver.health.TryGetCharacter() : null, false)) continue;"),
    ("Integration/PhantomWitch/PhantomWitchAssetManager.cs", "public static Buff GetCurseBuff(", "SetFieldSafe(modifier, modifierTypeField, ItemStatsSystem.Stats.ModifierType.PercentageAdd);"),
    ("Integration/PhantomWitch/PhantomWitchAssetManager.cs", "public static GameObject CreateWraithWindupOutlineEffect(", "ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical)"),
    ("Integration/PhantomWitch/PhantomWitchAssetManager.cs", "public static GameObject CreateBossCurseRealmVisual(", "ShouldSkipEffect(PhantomWitchFxEffectImportance.Critical)"),
    ("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs", "internal sealed class PhantomWitchVfxRecycler", "SetOwner(null); CleanupRootForPooling(gameObject);"),
    ("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs", "private static GameObject CreateBillboardQuad(", "filter.sharedMesh = GetBillboardQuadMesh();"),
    ("Integration/DeathWraith/DeathWraithSpawnFlow.cs", "private async void TrySpawnStoredDeathWraithForRaid_DeathWraith(", "if (ownsRequest && generation == deathWraithSpawnGeneration) spawningWraithRaidIds.Remove(raidID);"),
    ("Integration/DeathWraith/DeathWraithLifecycleAndPersistence.cs", "private void ClearDeathWraithState_DeathWraith(", "deathWraithSpawnGeneration++;"),
    ("Integration/DeathWraith/DeathWraithSystem.cs", "private void OnSetFile_DeathWraith(", "ClearDeathWraithState_DeathWraith();"),
    ("Integration/Items/WildHornUsage.cs", "private async UniTaskVoid SpawnMountAsync(", "if (!committed && horse != null) UnityEngine.Object.Destroy(horse.gameObject);"),
    ("Integration/Items/WildHornUsage.cs", "public static void ClearMountCache(", "mountRequestGeneration++;"),
    ("Integration/DragonDescendant/DragonDescendantBoss.cs", "private void EquipDragonBreathWeapon(", "ItemAssetsCollection.GetPrefab(DragonDescendantConfig.DRAGON_BREATH_TYPE_ID) == null"),
    ("Integration/DragonDescendant/DragonDescendantBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonDescendant(", "if (!completed && character != null) CleanupCancelledDragonDescendant(character);"),
    ("Integration/DragonKing/DragonKingBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonKing(", "if (!completed && character != null) CleanupCancelledDragonKing(character, assetReferenceAdded);"),
]


def main():
    errors = []
    for path, marker, required in CHECKS:
        try:
            expected = 2 if marker == "private static async UniTask<Projectile> ExtractBossRedProjectileAsync(" else 1
            if member(path, marker).count(required) != expected:
                errors.append(path + ": missing " + required)
        except ValueError:
            errors.append(path + ": missing method " + marker)
    if errors:
        print("CombatAuditFixesGuard: FAIL\n" + "\n".join(errors))
        return 1
    print("CombatAuditFixesGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
