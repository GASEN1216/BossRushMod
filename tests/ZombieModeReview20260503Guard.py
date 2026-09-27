"""ZombieModeReview20260503Guard: 2026-05-03 丧尸模式审查修复 invariant 守护。

涵盖审查中已修复的关键 invariant；任何回退会让此守护脚本退出 1。

涉及审查发现：
  §1.1  已删除 FindZombieModeNormalZombiePreset / 缓存字段；改 EnsureCharacterPresetsCacheReady
  §1.2  ZombieModeTuning.GetBossKind / BossKindTuning 数据表
  §1.3  RunOnly、转存回滚和两类临时 NPC 清理分别使用 RunScopedRegistry.ForEachReverse
  §1.4  Common/Stats/RuntimeStatModifierTracker 存在
  §2.1  BossSkillState.Tick 抽象化（virtual + 5 子类 override）
  §2.3  ZombieModeStatNames 常量集中
  §2.4  GetZombieModeBossDisplayName 单行拼接
  §2.5  Quest tag 反射缓存（2026-09-09 起唯一实现在 Config/LootExcludeTagPolicy.cs）
  §3.1  OnHurt/OnDead HashSet<int> 早返
  §3.2  Hunter Frenzy / Player Slow / Reward Attribute 改用 PercentageAdd（去除 stat.BaseValue * percent 模式）
  §3.3  共享 disk mesh visual（s_zoneDiskMesh + CreateZombieModeFlatZoneVisual）
  §3.7  静态数组替代每帧 new （s_zombieModeBossKindOrder / s_zombieModeSpecialKindOrder）
  §4.1  Boss 护盾/减伤在官方死亡判定前消费（2026-09-26 修复原补血债务）
  §4.2  ExplosionManager.CreateExplosion 接入（DealZombieModeExplosionAreaDamage）
"""

from pathlib import Path
import sys
from cs_source_util import clean_source
from ZombieModeBossPresentationGuard import body

REWARD_PARTS = [
    Path("ZombieMode/ZombieModeRewards.cs"),
    Path("ZombieMode/ZombieModeRuntimeModule_RewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]
POLLUTION_PARTS = [
    Path("ZombieMode/ZombieModeRuntimeModule_Pollution.cs"),
    Path("ZombieMode/ZombieModeRuntimeModule_PollutionTuning.cs"),
    Path("ZombieMode/ZombieModeRuntimeModule_PollutionSkills.cs"),
    Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs"),
]


def fail(msg: str) -> int:
    print("ZombieModeReview20260503Guard: FAIL — " + msg)
    return 1


def must_contain(path: Path, *needles: str) -> str:
    if not path.is_file():
        return "missing file: " + str(path)
    text = clean_source(path.read_text(encoding="utf-8-sig"))
    for n in needles:
        if n not in text:
            return "missing in " + str(path) + ": " + n
    return ""


def must_not_contain(path: Path, *needles: str) -> str:
    if not path.is_file():
        return "missing file: " + str(path)
    text = clean_source(path.read_text(encoding="utf-8-sig"))
    for n in needles:
        if n in text:
            return "regression in " + str(path) + ": " + n
    return ""


def read_rewards() -> str:
    return "\n".join(clean_source(path.read_text(encoding="utf-8-sig")) for path in REWARD_PARTS if path.is_file())


def read_pollution() -> str:
    return "\n".join(clean_source(path.read_text(encoding="utf-8-sig")) for path in POLLUTION_PARTS if path.is_file())


def main() -> int:
    spawner = Path("ZombieMode/ZombieModeSpawner.cs")
    boss = Path("ZombieMode/ZombieModeRuntimeModule_BossController.cs")
    models = Path("ZombieMode/ZombieModeModels.cs")
    tuning = Path("ZombieMode/ZombieModeTuning.cs")
    pollution_text = read_pollution()
    wave = Path("ZombieMode/ZombieModeRuntimeModule_WaveController.cs")
    drops = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
    rewards_text = read_rewards()
    cleanup = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
    runtime_module = Path("ZombieMode/ZombieModeRuntimeModule.cs")
    runtime_module_enemy = Path("ZombieMode/ZombieModeRuntimeModule_EnemyRuntime.cs")
    runtime_module_inventory = Path("ZombieMode/ZombieModeRuntimeModule_InventoryTransfer.cs")
    runtime_bridges = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
    inventory = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
    map_iso = Path("ZombieMode/ZombieModeMapIsolation.cs")
    enemy_runtime = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
    extraction = Path("ZombieMode/ZombieModeRuntimeModule_Extraction.cs")
    tracker = Path("Common/Stats/RuntimeStatModifierTracker.cs")
    spawn_core = Path("Utilities/EnemySpawnCore.cs")
    loot = Path("LootAndRewards/LootAndRewards.cs")
    loot_tag_policy = Path("Config/LootExcludeTagPolicy.cs")

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
    err = must_contain(Path("ModeD/ModeDRuntimeModule_EnemyPools.cs"), "internal void EnsureCharacterPresetsCacheReady()")
    if err:
        return fail(err)
    err = must_contain(Path("Utilities/EnemySpawnHostBridge.cs"),
        "internal void EnsureCharacterPresetsCacheReady() { modeDRuntime.EnsureCharacterPresetsCacheReady(); }")
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

    # §1.3 — 按真实 owner 与集合钉住四条清理路径；历史总数把注释也计入了调用。
    reverse_owners = (
        (runtime_module, "internal void CleanupZombieModeRunOnlyState(", "runState.RunOnlyObjects"),
        (runtime_module_inventory, "internal void RollbackZombieModeInventoryTransfer(", "entryTransaction.InventoryTransferredItems"),
        (drops, "internal void RecycleZombieModeTemporaryNpcs(", "runState.TemporaryNpcs"),
        (drops, "internal void RecycleZombieModeTemporaryRealNpcs(", "runState.TemporaryRealNpcs"),
    )
    for path, signature, collection in reverse_owners:
        method = body(clean_source(path.read_text(encoding="utf-8-sig")), signature)
        if "RunScopedRegistry.ForEachReverse( " + collection + "," not in " ".join(method.split()):
            return fail(str(path) + " " + signature + " 必须逆序清理 " + collection)

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
    err = must_contain(boss, "instance.SkillState.Tick(owner, instance, now)")
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
    # 实现已收敛到共享排除口径里（Boss 奖池、空投、天空岛搜刮点共用），
    # 但「反射结果必须缓存」这条不变式照旧：没有 sentinel 就会每件物品重复三段反射。
    err = must_contain(loot_tag_policy, "cachedQuestTag", "questTagSearched")
    if err:
        return fail(err)
    err = must_contain(loot, "return LootExcludeTagPolicy.TryFindQuestTag(tagsData);")
    if err:
        return fail(err)

    # §3.1 — OnHurt HashSet 早返
    err = must_contain(runtime_module_enemy,
                       "private readonly HashSet<int> zombieModeEnemyInstanceIds",
                       "private readonly Dictionary<int, ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkersByInstanceId",
                       "internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)",
                       "internal void UnregisterZombieModeEnemyInstanceId(CharacterMainControl character)",
                       "internal void ClearZombieModeEnemyInstanceIds()")
    if err:
        return fail(err)
    err = must_contain(Path("ZombieMode/ZombieModeCombatHostBridge.cs"),
                       "module.IsZombieModeKnownEnemy(character)",
                       "module.TryGetZombieModeKnownEnemyMarker(character, out marker)",
                       "module.RegisterZombieModeEnemyInstanceId(character, marker)",
                       "module.UnregisterZombieModeEnemyInstanceId(character)",
                       "module.ClearZombieModeEnemyInstanceIds()",
                       "module.RegisterZombieModeEnemyRuntimeShell(")
    if err:
        return fail(err)
    err = must_contain(wave, "TryGetZombieModeKnownEnemyMarker", "TryHandleZombieModeSafeZonePlayerAttack", "CancelZombieModeSafeZone(runId, \"PlayerAttack\");")
    if err:
        return fail(err)
    err = must_contain(runtime_bridges, "ClearZombieModeEnemyInstanceIds();")
    if err:
        return fail(err)
    err = must_contain(Path("ZombieMode/ZombieModeSafeZoneController.cs"), "UnregisterZombieModeEnemyInstanceId(owner)")
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

    # §4.1 — 旧补血路径不能穿透致命一击；详细接线由 BossPresentationGuard 与真实 Hurt 回归覆盖。
    from ZombieModeBossPresentationGuard import main as check_boss_contract
    check_boss_contract()

    # §4.2 — ExplosionManager 接入
    for needle in ("DealZombieModeExplosionAreaDamage", "ExplosionManager.CreateExplosion"):
        if needle not in pollution_text:
            return fail("missing in ZombieMode pollution partials: " + needle)

    print("ZombieModeReview20260503Guard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
