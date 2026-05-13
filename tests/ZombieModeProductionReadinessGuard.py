"""ZombieModeProductionReadinessGuard: production-readiness fixes stay wired.

This guard covers behavior-level issues that static compile-list guards do not:
- Zombie bosses must opt out of BossRush random-loot tracking when reusing SpawnEnemyCore.
- Corruptor player slow must apply and remove real movement stat modifiers.
- Hunter frenzy must apply timed movement and attack-speed modifiers, not only scale.
- Zombie item drops must use the official Item.Drop path.
- Attribute reward cleanup must be registered once per run, not once per modifier.
"""

from pathlib import Path
import re
import sys


SPAWN_CORE = Path("Utilities/EnemySpawnCore.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")
BOSS_PARTS = [
    BOSS,
    Path("ZombieMode/ZombieModePlayerSlowRuntime.cs"),
]
DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)


def read_boss() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in BOSS_PARTS if path.exists())



def fail(message: str) -> int:
    print(message)
    return 1


def extract_method(text: str, method_name: str) -> str:
    match = re.search(r"\b" + re.escape(method_name) + r"\s*\([^)]*\)\s*\{", text)
    if match is None:
        return ""

    depth = 0
    for index in range(match.end() - 1, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[match.start():index + 1]
    return ""


def main() -> int:
    spawn_core = SPAWN_CORE.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    boss = read_boss()
    drops = DROPS.read_text(encoding="utf-8")
    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()

    if "bool skipBossRushLootTracking = false" not in spawn_core:
        return fail("ZombieModeProductionReadinessGuard: SpawnEnemyCore lacks skipBossRushLootTracking parameter")
    loot_registration = re.search(
        r"if\s*\(\s*isBoss\s*&&\s*!skipBossRushLootTracking\s*&&\s*character\s*!=\s*null\s*\)",
        spawn_core,
    )
    if loot_registration is None:
        return fail("ZombieModeProductionReadinessGuard: SpawnEnemyCore boss loot tracking is not guarded by skipBossRushLootTracking")

    boss_spawn = extract_method(spawner, "TrySpawnZombieModeBossAsync")
    if not boss_spawn:
        return fail("ZombieModeProductionReadinessGuard: TrySpawnZombieModeBossAsync not found")
    if "skipBossRushLootTracking: true" not in boss_spawn:
        return fail("ZombieModeProductionReadinessGuard: Zombie boss spawn does not opt out of BossRush loot tracking")

    slow_runtime = extract_method(boss, "ZombieModePlayerSlowRuntime")
    if "class ZombieModePlayerSlowRuntime" not in boss:
        return fail("ZombieModeProductionReadinessGuard: ZombieModePlayerSlowRuntime missing")
    # 2026-05-03 §3.2 修复：玩家减速改用 RuntimeStatModifierTracker.TryAdd（PercentageAdd）。
    # 守护接受新旧两种实现：
    #   - 旧：直接 new Modifier(ModifierType.Add, ...) 写入 stat
    #   - 新：调用 RuntimeStatModifierTracker.TryAdd
    if not ("new Modifier(ModifierType.Add" in boss
            or "RuntimeStatModifierTracker.TryAdd" in boss):
        return fail("ZombieModeProductionReadinessGuard: player slow runtime missing modifier registration")
    for token in [
        "AddSlowModifier",
        "RemoveSlowModifiers",
    ]:
        if token not in boss:
            return fail("ZombieModeProductionReadinessGuard: player slow runtime missing token -> " + token)
    # cleanup 路径：要么直接 stat.RemoveModifier(record.Modifier)，要么走 RuntimeStatModifierTracker.RemoveAll
    if not ("stat.RemoveModifier(record.Modifier)" in boss
            or "RuntimeStatModifierTracker.RemoveAll" in boss):
        tracker_path = Path("Common/Stats/RuntimeStatModifierTracker.cs")
        if not (tracker_path.is_file()
                and "stat.RemoveModifier(record.Modifier)" in tracker_path.read_text(encoding="utf-8")):
            return fail("ZombieModeProductionReadinessGuard: player slow runtime missing modifier cleanup path")
    # MoveSpeed / WalkSpeed / RunSpeed 改通过 ZombieModeStatNames 引用，仍可见但走常量
    for stat_token in ["MoveSpeed", "WalkSpeed", "RunSpeed"]:
        if stat_token not in boss:
            return fail("ZombieModeProductionReadinessGuard: player slow runtime missing stat -> " + stat_token)

    if "FrenzyModifierRecords" not in models:
        return fail("ZombieModeProductionReadinessGuard: Hunter state does not track frenzy modifiers")
    hunter_activate = extract_method(boss, "ActivateZombieModeHunterFrenzy")
    if "ApplyZombieModeHunterFrenzyModifiers" not in hunter_activate:
        return fail("ZombieModeProductionReadinessGuard: Hunter frenzy does not apply timed stat modifiers")
    if "ApplyZombieModeAiSpeedMultiplier" in hunter_activate:
        return fail("ZombieModeProductionReadinessGuard: Hunter frenzy still uses scale-only speed helper")
    # Stat 名常量收口到 ZombieModeStatNames（审查 §2.3）。
    for token in [
        "MoveSpeed",
        "WalkSpeed",
        "RunSpeed",
        "AttackSpeed",
        "ZombieModeTuning.HunterFrenzyAttackSpeedBonus",
        "RemoveZombieModeHunterFrenzyModifiers",
    ]:
        if token not in boss:
            return fail("ZombieModeProductionReadinessGuard: Hunter frenzy missing token -> " + token)

    drop_method = extract_method(drops, "TryDropZombieModeItemNearPosition")
    if not drop_method:
        return fail("ZombieModeProductionReadinessGuard: TryDropZombieModeItemNearPosition not found")
    if ".Drop(" not in drop_method:
        return fail("ZombieModeProductionReadinessGuard: Zombie item drop does not use Item.Drop")
    if "item.Detach()" in drop_method or "obj.SetActive(true)" in drop_method:
        return fail("ZombieModeProductionReadinessGuard: Zombie item drop still uses manual Detach/SetActive path")

    if "AttributeModifierCleanupRegistered" not in models:
        return fail("ZombieModeProductionReadinessGuard: run state lacks AttributeModifierCleanupRegistered")
    add_attr = extract_method(rewards, "AddZombieModeAttributeModifier")
    if "if (!zombieModeRunState.AttributeModifierCleanupRegistered)" not in add_attr:
        return fail("ZombieModeProductionReadinessGuard: attribute cleanup is not guarded by one-shot registration")
    if "zombieModeRunState.AttributeModifierCleanupRegistered = true;" not in add_attr:
        return fail("ZombieModeProductionReadinessGuard: attribute cleanup registration flag is not set")

    print("ZombieModeProductionReadinessGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
