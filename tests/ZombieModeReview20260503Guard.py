"""ZombieModeReview20260503Guard: 2026-05-03 丧尸模式审查修复 invariant 守护。

涵盖审查中已修复的关键 invariant；任何回退会让此守护脚本退出 1。

涉及审查发现：
  §1.1  已删除 FindZombieModeNormalZombiePreset / 缓存字段；改 EnsureCharacterPresetsCacheReady
  §1.2  ZombieModeTuning.GetBossKind / BossKindTuning 数据表
  §1.3  RunScopedRegistry.ForEachReverse 至少 5 处使用
  §1.4  Common/Stats/RuntimeStatModifierTracker 存在
  §2.1  BossSkillState.Tick 抽象化（virtual + 5 子类 override）
  §2.3  ZombieModeStatNames 常量集中
  §2.4  GetZombieModeBossDisplayName 单行拼接
  §2.5  LootAndRewards.TryFindQuestTag 缓存（s_cachedQuestTag / s_questTagPermanentlyMissing）
  §3.1  OnHurt/OnDead HashSet<int> 早返
  §3.2  Hunter Frenzy / Player Slow / Reward Attribute 改用 PercentageAdd（去除 stat.BaseValue * percent 模式）
  §3.3  共享 disk mesh visual（s_zoneDiskMesh + CreateZombieModeFlatZoneVisual）
  §3.7  静态数组替代每帧 new （s_zombieModeBossKindOrder / s_zombieModeSpecialKindOrder）
  §4.1  RestoreZombieModeFinalDamageReduction 加架构债注释
  §4.2  ExplosionManager.CreateExplosion 接入（DealZombieModeExplosionAreaDamage）
"""

from pathlib import Path
import sys

REWARD_PARTS = [
    Path("ZombieMode/ZombieModeRewards.cs"),
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]
POLLUTION_PARTS = [
    Path("ZombieMode/ZombieModePollution.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs"),
]


def fail(msg: str) -> int:
    print("ZombieModeReview20260503Guard: FAIL — " + msg)
    return 1


def must_contain(path: Path, *needles: str) -> str:
    if not path.is_file():
        return "missing file: " + str(path)
    text = path.read_text(encoding="utf-8")
    for n in needles:
        if n not in text:
            return "missing in " + str(path) + ": " + n
    return ""


def must_not_contain(path: Path, *needles: str) -> str:
    if not path.is_file():
        return "missing file: " + str(path)
    text = path.read_text(encoding="utf-8")
    for n in needles:
        if n in text:
            return "regression in " + str(path) + ": " + n
    return ""


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in REWARD_PARTS if path.is_file())


def read_pollution() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in POLLUTION_PARTS if path.is_file())


def main() -> int:
    spawner = Path("ZombieMode/ZombieModeSpawner.cs")
    boss = Path("ZombieMode/ZombieModeBossController.cs")
    models = Path("ZombieMode/ZombieModeModels.cs")
    tuning = Path("ZombieMode/ZombieModeTuning.cs")
    pollution_text = read_pollution()
    wave = Path("ZombieMode/ZombieModeWaveController.cs")
    drops = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
    rewards_text = read_rewards()
    cleanup = Path("ZombieMode/ZombieModeCleanup.cs")
    inventory = Path("ZombieMode/ZombieModeInventoryTransfer.cs")
    map_iso = Path("ZombieMode/ZombieModeMapIsolation.cs")
    enemy_runtime = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
    extraction = Path("ZombieMode/ZombieModeExtractionController.cs")
    tracker = Path("Common/Stats/RuntimeStatModifierTracker.cs")
    spawn_core = Path("Utilities/EnemySpawnCore.cs")
    loot = Path("LootAndRewards/LootAndRewards.cs")

    # §1.1 — preset 缓存与方法被删；EnsureCharacterPresetsCacheReady 接管
    err = must_not_contain(spawner,
        "FindZombieModeNormalZombiePreset",
        "zombieModeCachedNormalZombiePreset",
        "zombieModeNormalZombiePresetSearched")
    if err:
        return fail(err)
    err = must_contain(spawner, "EnsureCharacterPresetsCacheReady()")
    if err:
        return fail(err)
    err = must_contain(spawn_core, "EnsureCharacterPresetsCacheReady")
    if err:
        return fail(err)
    # SpawnEnemyCore 调用方不再传 directPreset
    err = must_not_contain(spawner, "directPreset: preset")
    if err:
        return fail(err)

    # §1.2 — BossKindTable / GetBossKind / BossKindTuning
    err = must_contain(tuning, "BossKindTuning", "BossKindTable", "GetBossKind")
    if err:
        return fail(err)
    err = must_contain(spawner, "ZombieModeTuning.GetBossKind(kind)")
    if err:
        return fail(err)

    # §1.3 — RunScopedRegistry.ForEachReverse 至少 5 处
    fer_count = 0
    for path in [cleanup, map_iso, drops, inventory]:
        if path.is_file():
            txt = path.read_text(encoding="utf-8")
            fer_count += txt.count("RunScopedRegistry.ForEachReverse")
    fer_count += rewards_text.count("RunScopedRegistry.ForEachReverse")
    if fer_count < 5:
        return fail("RunScopedRegistry.ForEachReverse 使用 < 5 处（实测 " + str(fer_count) + "）")

    # §1.4 — RuntimeStatModifierTracker 存在 + ZombieMode 已经接入
    if not tracker.is_file():
        return fail("Common/Stats/RuntimeStatModifierTracker.cs 缺失")
    err = must_contain(tracker, "TryAdd", "RemoveAll", "ModifierType.PercentageAdd")
    if err:
        return fail(err)
    err = must_contain(boss, "RuntimeStatModifierTracker.TryAdd")
    if err:
        return fail(err)
    if "RuntimeStatModifierTracker.TryAdd" not in rewards_text:
        return fail("missing in ZombieMode reward partials: RuntimeStatModifierTracker.TryAdd")

    # §2.1 — BossSkillState.Tick 抽象化（virtual + 5 子类 override）
    err = must_contain(models, "public virtual void Tick(ModBehaviour")
    if err:
        return fail(err)
    for kind in ("Titan", "Hunter", "Splitter", "Shielder", "Corruptor"):
        err = must_contain(models, "TickZombieMode" + kind + "State")
        if err:
            return fail(err + "（每个 SkillState 子类需要 override Tick）")
    err = must_contain(boss, "instance.SkillState.Tick(this, instance, now)")
    if err:
        return fail(err)
    # 旧 switch 主体已废
    err = must_not_contain(boss, "(ZombieModeTitanState)instance.SkillState")
    if err:
        return fail(err)

    # §2.3 — ZombieModeStatNames 常量集中
    err = must_contain(tuning, "internal static class ZombieModeStatNames",
                       "MaxHealth", "MoveSpeed", "AttackSpeed", "MeleeDamageMultiplier")
    if err:
        return fail(err)
    err = must_contain(boss, "ZombieModeStatNames.MoveSpeed")
    if err:
        return fail(err)

    # §2.4 — DisplayName 单行拼接
    err = must_contain(spawner, 'L10n.T("BossRush_ZombieMode_Boss_" + kind.ToString())')
    if err:
        return fail(err)

    # §2.5 — Quest tag 缓存
    err = must_contain(loot, "s_cachedQuestTag", "s_questTagPermanentlyMissing", "s_questTagSearched")
    if err:
        return fail(err)

    # §3.1 — OnHurt HashSet 早返
    err = must_contain(enemy_runtime, "zombieModeEnemyInstanceIds", "RegisterZombieModeEnemyInstanceId",
                       "UnregisterZombieModeEnemyInstanceId", "ClearZombieModeEnemyInstanceIds")
    if err:
        return fail(err)
    err = must_contain(wave, "TryGetZombieModeKnownEnemyMarker", "TryProcessZombieModeSafeZoneStealthBreak")
    if err:
        return fail(err)
    err = must_contain(cleanup, "ClearZombieModeEnemyInstanceIds()")
    if err:
        return fail(err)
    err = must_contain(cleanup, "UnregisterZombieModeEnemyInstanceId(owner)")
    if err:
        return fail(err)

    # §3.2 — 玩家减速不再用 stat.BaseValue * currentSlowPercent
    err = must_not_contain(boss, "stat.BaseValue * currentSlowPercent",
                                "-stat.BaseValue * currentSlowPercent")
    if err:
        return fail(err + "（应改为 PercentageAdd 形式）")
    # Hunter Frenzy 也不再用 stat.BaseValue * percent 模式
    err = must_not_contain(boss, "Modifier(ModifierType.Add, stat.BaseValue * percent")
    if err:
        return fail(err)

    # §3.3 — 共享 disk mesh visual
    for needle in ("s_zoneDiskMesh", "CreateZombieModeFlatZoneVisual", "EnsureZoneDiskAssets"):
        if needle not in pollution_text:
            return fail("missing in ZombieMode pollution partials: " + needle)
    # BossController / ExtractionController 走 helper（不再直接 CreatePrimitive(Cylinder)）
    err = must_not_contain(boss, "GameObject.CreatePrimitive(PrimitiveType.Cylinder)")
    if err:
        return fail(err)
    err = must_not_contain(extraction, "GameObject.CreatePrimitive(PrimitiveType.Cylinder)")
    if err:
        return fail(err)

    # §3.7 — 静态数组替代 new[]
    err = must_contain(spawner, "s_zombieModeBossKindOrder")
    if err:
        return fail(err)
    for needle in ("s_zombieModeSpecialKindOrder", "s_zombieModeEliteAffixAll"):
        if needle not in pollution_text:
            return fail("missing in ZombieMode pollution partials: " + needle)

    # §4.1 — 架构债注释
    err = must_contain(wave, "Health.cs:418", "heal-back 模式")
    if err:
        return fail(err)

    # §4.2 — ExplosionManager 接入
    for needle in ("DealZombieModeExplosionAreaDamage", "ExplosionManager.CreateExplosion"):
        if needle not in pollution_text:
            return fail("missing in ZombieMode pollution partials: " + needle)

    print("ZombieModeReview20260503Guard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
