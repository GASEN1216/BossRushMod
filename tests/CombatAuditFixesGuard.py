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
    ("Integration/DeathWraith/DeathWraithLifecycleAndPersistence.cs", "internal void ClearDeathWraithState_DeathWraith(", "deathWraithSpawnGeneration++;"),
    ("Integration/DeathWraith/DeathWraithSystem.cs", "internal void OnSetFile_DeathWraith(", "ClearDeathWraithState_DeathWraith();"),
    ("Integration/Items/WildHornUsage.cs", "private async UniTaskVoid SpawnMountAsync(", "if (!committed && horse != null) UnityEngine.Object.Destroy(horse.gameObject);"),
    ("Integration/Items/WildHornUsage.cs", "public static void ClearMountCache(", "mountRequestGeneration++;"),
    ("Integration/DragonDescendant/DragonDescendantBoss.cs", "private void EquipDragonBreathWeapon(", "ItemAssetsCollection.GetPrefab(DragonDescendantConfig.DRAGON_BREATH_TYPE_ID) == null"),
    ("Integration/DragonDescendant/DragonDescendantBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonDescendant(", "if (!completed && character != null) CleanupCancelledDragonDescendant(character);"),
    ("Integration/DragonKing/DragonKingBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonKing(", "if (!completed && character != null) CleanupCancelledDragonKing(character, assetReferenceAdded);"),
    # 2026-10-01：焚皇断界戟爆燃与跳斩落地是技能自建伤害，不得被当成武器命中触发词缀 / 套装 / 雷戒。
    ("Integration/DragonKing/Weapons/FenHuangHalberdAction.cs", "private void TriggerDetonation(", "damageInfo.isFromBuffOrEffect = true;"),
    ("Integration/DragonKing/Weapons/FenHuangHalberdAction.cs", "private void DealLandingImpactDamage(", "dmg.isFromBuffOrEffect = true;"),
    # 2026-10-01：龙王 / 龙裔补根 §4.5 敌对性安全网（形态照幽灵女巫），豁免遗种巢随从与 Mode E/F。
    ("Integration/DragonKing/DragonKingBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonKing(", "else if (!Team.IsEnemy(Teams.player, character.Team))"),
    ("Integration/DragonKing/DragonKingBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonKing(", "PetNestCompanionAgent.IsCompanionCharacter(character)"),
    ("Integration/DragonKing/DragonKingBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonKing(", "(owner.IsModeEActive || owner.IsModeFActive)"),
    ("Integration/DragonDescendant/DragonDescendantBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonDescendant(", "else if (!Team.IsEnemy(Teams.player, character.Team))"),
    ("Integration/DragonDescendant/DragonDescendantBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonDescendant(", "PetNestCompanionAgent.IsCompanionCharacter(character)"),
    ("Integration/DragonDescendant/DragonDescendantBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonDescendant(", "(owner.IsModeEActive || owner.IsModeFActive)"),
]

# 安全网必须早于激活：激活后 AI 立即按当前 team 选目标。
HOSTILITY_ORDER = [
    ("Integration/DragonKing/DragonKingBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonKing("),
    ("Integration/DragonDescendant/DragonDescendantBoss.cs", "public async UniTask<CharacterMainControl> SpawnDragonDescendant("),
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
    for path, marker in HOSTILITY_ORDER:
        try:
            body = member(path, marker)
        except ValueError:
            errors.append(path + ": missing method " + marker)
            continue
        net = body.find("character.SetTeam(Teams.wolf);")
        activate = body.find("character.gameObject.SetActive(true);")
        if net < 0 or activate < 0 or net > activate:
            errors.append(path + ": hostility safety net must run before activation")
    if errors:
        print("CombatAuditFixesGuard: FAIL\n" + "\n".join(errors))
        return 1
    print("CombatAuditFixesGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
