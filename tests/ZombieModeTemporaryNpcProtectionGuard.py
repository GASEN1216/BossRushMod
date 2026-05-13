from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
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

SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
SAFE_ZONE = Path("ZombieMode/ZombieModeSafeZoneController.cs")
NPC_CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeTemporaryNpcProtectionGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    rewards = read_rewards()
    spawner = SPAWNER.read_text(encoding="utf-8")
    safe_zone = SAFE_ZONE.read_text(encoding="utf-8")
    npc_catalog = NPC_CATALOG.read_text(encoding="utf-8")

    for snippet in [
        "public sealed class ZombieModeTemporaryNpcProtectionMarker",
        "public int RunId;",
        "public string ServiceType",
    ]:
        result = require(models, snippet, "temporary NPC marker model")
        if result:
            return result

    for snippet in [
        "TickZombieModeTemporaryNpcProtection();",
    ]:
        result = require(entry, snippet, "temporary NPC protection tick")
        if result:
            return result

    for snippet in [
        "ApplyZombieModeTemporaryNpcProtection(npc, runId, serviceType);",
        "ZombieModeTemporaryNpcProtectionMarker marker",
        "TrySetZombieModeTemporaryNpcInvincible",
        "Health[] healths = npc.GetComponentsInChildren<Health>(true)",
        "health.SetInvincible(true)",
        "TickZombieModeTemporaryNpcProtection",
        "ClearZombieModeTemporaryNpcThreatTargets",
        "IsZombieModeTemporaryNpcDamageReceiver",
        "receiver.health.TryGetCharacter()",
        "GetComponentInParent<ZombieModeTemporaryNpcProtectionMarker>()",
    ]:
        result = require(rewards, snippet, "temporary NPC invulnerability and AI filtering")
        if result:
            return result

    for snippet in [
        "SetZombieModeEnemyTargetToMainPlayer(ai);",
    ]:
        result = require(spawner, snippet, "spawn target helper")
        if result:
            return result

    for snippet in [
        "SetZombieModeEnemyTargetToMainPlayer(ai);",
    ]:
        result = require(safe_zone, snippet, "safe-zone release target helper")
        if result:
            return result

    for stale in [
        "数据骨架",
        "实际生成 NPC、UI 交互、扣费由阶段 6/7 单独实现",
        "当前数据仅占位",
    ]:
        if stale in npc_catalog:
            return fail("ZombieModeTemporaryNpcProtectionGuard: stale NPC catalog comment remains -> " + stale)

    print("ZombieModeTemporaryNpcProtectionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
