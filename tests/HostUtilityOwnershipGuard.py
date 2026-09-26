"""Utility state and algorithms retain independent owners and original host wiring."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def compact(text):
    return re.sub(r"\s+", "", text)


def body(text, signature):
    assert text.count(signature) == 1, "missing or duplicate method: " + signature
    start = text.index("{", text.index(signature))
    depth = 0
    for end in range(start, len(text)):
        depth += (text[end] == "{") - (text[end] == "}")
        if depth == 0:
            return compact(text[start + 1:end])
    raise AssertionError("unclosed method: " + signature)


def main():
    host = read("ModBehaviour.cs")
    utility = read("Utilities/Utilities.cs")
    scaling = read("Utilities/BossStatScaling.cs")
    waits = read("Utilities/BossRushWaitCache.cs")
    sanitizer = read("Utilities/ZombieSpawnSanitizer.cs")
    policy = read("ZombieMode/ZombieModeSpawnSanitizationPolicy.cs")
    shop = read("Integration/BossRushIntegrationRuntimeModule_AmmoShop.cs")
    integration = read("Integration/BossRushIntegration.cs")
    scene = read("Integration/BossRushIntegration_StartAndScene.cs")
    loot = read("LootAndRewards/LootAndRewards.cs")
    for source in (scaling, waits, sanitizer, policy, shop):
        assert "partial class ModBehaviour" not in source, "utility business code must not be a host partial"
    assert "private StockShop ammoShop;" in shop and "StockShop ammoShop" not in host + utility, "ammo shop field must belong to Integration runtime"
    assert "internal sealed partial class IntegrationRuntimeModule" in shop, "ammo shop must extend registered Integration owner"
    assert body(host, "public void ShowAmmoShop()") == "bossRushIntegrationRuntime.ShowAmmoShop();", "public shop entry must reach Integration owner"
    assert body(utility, "private void EnsureAmmoShop_Utilities()") == "bossRushIntegrationRuntime.EnsureAmmoShop_Utilities();", "lazy shop entry must reach Integration owner"
    assert body(integration, "internal bool IsIntegrationAmmoShop(StockShop shop)") == "returnbossRushIntegrationRuntime.IsIntegrationAmmoShop(shop);", "purchase routing must reach same shop owner"
    assert "bossRushIntegrationRuntime.CleanupAmmoShop();" in scene, "scene exit must clean the Integration-owned shop"
    assert "bossRushIntegrationRuntime.CleanupAmmoShopOnPlayerDeath(LogLootWarningLimited);" in loot, "player death must retain the logged cleanup entry"
    assert body(utility, "private void ApplyBossStatMultiplier(") == "BossStatScaling.ApplyBossStatMultiplier(character,multiplier,config!=null?config.bossStatMultiplier:1f);", "host must pass nullable multiplier and current config fallback"
    assert "internal static class BossStatScaling" in scaling and "ModBehaviour.Instance" not in scaling, "stat scaling must remain stateless and consume explicit parameters"
    for field in ("sharedWait01s", "sharedWait05s", "sharedWait1s"):
        assert compact("private static WaitForSeconds " + field + " { get { return BossRushWaitCache." + field + "; } }") in compact(utility), "wait bridge must preserve shared identity: " + field
        assert "internal static readonly WaitForSeconds " + field in waits, "wait owner must retain readonly cached object: " + field
    assert "new WaitForSeconds(" not in utility, "host wait properties must not allocate"
    assert compact("private readonly ZombieSpawnSanitizer zombieSpawnSanitizer = new ZombieSpawnSanitizer(ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill);") in compact(utility), "host must construct one sanitizer with production zombie policy"
    assert body(utility, "internal void SanitizeBossRushZombieSpawn(") == "zombieSpawnSanitizer.SanitizeBossRushZombieSpawn(character,spawnOwner);", "spawn entry must forward original character and owner label"
    assert "Func<CharacterMainControl, bool> ShouldKeepBossRushZombieSelfDestructionSkill" in sanitizer, "shared sanitizer must receive explicit policy"
    assert "ZombieModeEnemyRuntimeMarker" not in sanitizer and "ZombieModeSpecialKind" not in sanitizer, "shared sanitizer must not own zombie classification"
    assert "internal sealed partial class ZombieModeRuntimeModule" in policy, "exploder policy belongs to zombie runtime"
    calls = body(sanitizer, "internal void SanitizeBossRushZombieSpawn(")
    assert calls.index("RemoveBossRushZombieSelfDestructionSkills(character,spawnOwner);") < calls.index("RemoveBossRushZombieBoomAttachments(character,spawnOwner);"), "skill cleanup must precede attachment cleanup"
    for signature in ("private void RemoveBossRushZombieSelfDestructionSkills(", "private void RemoveBossRushZombieBoomAttachments("):
        assert body(sanitizer, signature).startswith("if(ShouldKeepBossRushZombieSelfDestructionSkill(character)){return;}"), "each cleanup stage must query current exemption before side effects"
    print("HostUtilityOwnershipGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("HostUtilityOwnershipGuard: FAIL - " + str(error))
        raise SystemExit(1)
