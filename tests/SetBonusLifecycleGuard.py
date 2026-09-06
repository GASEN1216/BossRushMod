"""Frost/Thunder set bonuses must have scoped triggers and paired cleanup.

2026-09-06 扩展（龙王级重做 + 开放获取）：
- 雷霆反震的 CreateExplosion 必须显式传 canHurtSelf=false，且伤害走 buff/effect 通道；
- 引雷术 / 冰葬只在 Health.OnDead 回调里过滤与调度，结算延后到协程，并有重入门控；
- 两套装各只保留一个 Health.OnDead 订阅点（同文件 += / -= 配对）；
- 停用时销毁眼光 / 霜雾 / 电弧池；场景重载先停用再重查（官方每图重建 CharacterItem）；
- 获取路径：专属掉落格已接进特殊掉落协程，叮当商店四件齐全，Config 设了售价与天气防护。
"""

from pathlib import Path
import re
import sys


FROST = Path("Integration/Bonus/FrostSetBonus.cs")
FROST_NOVA = Path("Integration/Bonus/FrostSetBonus_Nova.cs")
THUNDER = Path("Integration/Bonus/ThunderSetBonus.cs")
THUNDER_STORM = Path("Integration/Bonus/ThunderSetBonus_Storm.cs")
VISUALS = Path("Integration/Bonus/SetBonusVisuals.cs")
MANAGER = Path("Integration/Bonus/SetBonusManager.cs")
PLACEHOLDER = Path("Integration/Bonus/SetBonusPlaceholderRegistry.cs")
FACTORY = Path("Integration/EquipmentFactory.cs")
CONFIG = Path("Integration/Config/FrostThunderSetConfig.cs")
SET_LOOT = Path("Integration/Bonus/SetBonusBossDropHandler.cs")
ON_DEAD_PATCH = Path("Patches/Combat/CharacterOnDeadPatch.cs")
SPECIAL_LOOT = Path("LootAndRewards/LootAndRewardsSpecialLoot.cs")
GOBLIN = Path("Integration/Affinity/NPCs/GoblinAffinityConfig.cs")


def fail(message: str) -> int:
    print("SetBonusLifecycleGuard: FAIL - " + message)
    return 1


def require(text: str, snippets, label: str) -> int:
    for snippet in snippets:
        if snippet not in text:
            return fail(label + " missing snippet -> " + snippet)
    return 0


def main() -> int:
    for path in (FROST, FROST_NOVA, THUNDER, THUNDER_STORM, VISUALS, MANAGER, PLACEHOLDER, FACTORY, CONFIG, SET_LOOT, ON_DEAD_PATCH, SPECIAL_LOOT, GOBLIN):
        if not path.exists():
            return fail("missing source file -> " + path.as_posix())

    frost = FROST.read_text(encoding="utf-8")
    frost_nova = FROST_NOVA.read_text(encoding="utf-8")
    thunder = THUNDER.read_text(encoding="utf-8")
    thunder_storm = THUNDER_STORM.read_text(encoding="utf-8")
    visuals = VISUALS.read_text(encoding="utf-8")
    manager = MANAGER.read_text(encoding="utf-8")
    placeholder = PLACEHOLDER.read_text(encoding="utf-8")
    factory = FACTORY.read_text(encoding="utf-8")
    config = CONFIG.read_text(encoding="utf-8")
    set_loot = SET_LOOT.read_text(encoding="utf-8")
    on_dead_patch = ON_DEAD_PATCH.read_text(encoding="utf-8")
    special_loot = SPECIAL_LOOT.read_text(encoding="utf-8")
    goblin = GOBLIN.read_text(encoding="utf-8")

    rc = require(frost, (
        "private const float FROST_SET_CLOSE_RANGE",
        "private Stat frostSetIceResistStat = null;",
        "frostSetIceResistStat = iceFactorStat;",
        "frostSetIceResistStat.RemoveModifier(frostSetIceResistModifier);",
        "if (damageInfo.fromCharacter == null) return;",
        "delta.sqrMagnitude > FROST_SET_CLOSE_RANGE * FROST_SET_CLOSE_RANGE",
        "private sealed class FrostFallbackSlowState",
        "RemoveFrostFallbackSlowModifiers(state);",
        "state.WalkSpeedStat.RemoveModifier(state.WalkSpeedModifier);",
        "state.RunSpeedStat.RemoveModifier(state.RunSpeedModifier);",
        # 2026-09-06：单一 OnDead 分派点 + 停用清理 + 冻结三级回退提炼
        "Health.OnDead += OnFrostSetAnyDead;",
        "Health.OnDead -= OnFrostSetAnyDead;",
        "TryScheduleFrostNova(target, damageInfo);",
        "DestroySetEyeLights(ref frostSetEyeLights);",
        "StopFrostMist();",
        "ResetFrostNovaState();",
        "private bool TryApplyFrostFreeze(CharacterMainControl target)",
        "GetSetBonusElementDamagePortion(health, damageInfo, ElementTypes.ice)",
    ), "frost bonus")
    if rc:
        return rc

    rc = require(frost_nova, (
        "if (!frostSetActive || frostNovaResolving) return;",
        "if (damageInfo.isFromBuffOrEffect) return;",
        "yield return frostNovaWait;",
        "frostNovaResolving = true;",
        "frostNovaResolving = false;",
        "dmg.isFromBuffOrEffect = true;",
        "dmg.fromWeaponItemID = 0;",
        "TryApplyFrostFreeze(enemy);",
    ), "frost nova")
    if rc:
        return rc

    rc = require(thunder, (
        "private Stat thunderSetElecResistStat = null;",
        "thunderSetElecResistStat = elecFactorStat;",
        "thunderSetElecResistStat.RemoveModifier(thunderSetElecResistModifier);",
        "if (damageInfo.fromCharacter == null) return;",
        "object.ReferenceEquals(damageInfo.fromCharacter, player)",
        # 2026-09-06：单一 OnDead 分派点 + 反震延后一帧 + buff/effect 通道 + 停用清理
        "Health.OnDead += OnThunderSetAnyDead;",
        "Health.OnDead -= OnThunderSetAnyDead;",
        "TryScheduleThunderChain(target, damageInfo);",
        "StartCoroutine(ThunderCounterStep(player, damageInfo.fromCharacter));",
        "dmg.isFromBuffOrEffect = true;",
        "dmg.fromWeaponItemID = 0;",
        "DestroySetEyeLights(ref thunderSetEyeLights);",
        "DestroySetArcPool();",
        "ResetThunderChainState();",
        "GetSetBonusElementDamagePortion(health, damageInfo, ElementTypes.electricity)",
    ), "thunder bonus")
    if rc:
        return rc

    # 反震爆炸必须显式 canHurtSelf=false：官方默认 true 时 selfTeam=Teams.all，玩家自己必吃这一下
    explosion_calls = re.findall(r"CreateExplosion\((.*?)\);", thunder, re.S)
    if not explosion_calls:
        return fail("thunder bonus no longer calls CreateExplosion")
    for call in explosion_calls:
        args = [part.strip() for part in call.split(",")]
        if len(args) < 6 or args[-1] != "false":
            return fail("thunder counter CreateExplosion must pass canHurtSelf=false explicitly")

    rc = require(thunder_storm, (
        "if (depth >= THUNDER_CHAIN_MAX_DEPTH) return;",
        "if (damageInfo.isFromBuffOrEffect) return;",
        "yield return thunderChainHopWait;",
        "thunderChainDepth = hop;",
        "thunderChainDepth = 0;",
        "dmg.isFromBuffOrEffect = true;",
        "dmg.fromWeaponItemID = 0;",
        "private void StopThunderAmbientArcLoop()",
    ), "thunder storm")
    if rc:
        return rc

    if "Health.OnDead +=" in thunder_storm or "Health.OnDead +=" in frost_nova or "Health.OnDead +=" in visuals:
        return fail("only ThunderSetBonus.cs / FrostSetBonus.cs may subscribe Health.OnDead (paired += / -= in one file)")

    rc = require(visuals, (
        "private bool TryResolveSetBonusKillVictim(Health target, DamageInfo info, out CharacterMainControl victim, out Vector3 position)",
        "if (target.IsMainCharacterHealth) return false;",
        "if (IsModeHRunInProgressSafe()) return false;",
        "if (info.fromCharacter == null || !info.fromCharacter.IsMainCharacter) return false;",
        "if (PetNestCompanionAgent.IsCompanionHealth(target)) return false;",
        "if (resolved.Team == Teams.player) return false;",
        "Team.IsEnemy(Teams.player, character.Team)",
        "private void DestroySetArcPool()",
        "private void DestroySetEyeLights(ref SetEyeLightState state)",
    ), "set bonus visuals")
    if rc:
        return rc

    rc = require(manager, (
        "private bool setBonusLevelEventRegistered = false;",
        "if (setBonusEventRegistered && setBonusLevelEventRegistered) return;",
        "LevelManager.OnAfterLevelInitialized += OnLevelInitializedCheckSetBonus;",
        "LevelManager.OnAfterLevelInitialized -= OnLevelInitializedCheckSetBonus;",
        "DeactivateFrostSetBonus();",
        "DeactivateThunderSetBonus();",
        "private void CheckSetBonusStatus(CharacterMainControl character, bool announce = true)",
        "CheckSetBonusStatus(main, false);",
    ), "set bonus manager")
    if rc:
        return rc

    if "if (!setBonusEventRegistered) return;" in manager:
        return fail("unregister still returns before level-event cleanup")

    # 场景重载：先停用两套再重查（官方每图重建 CharacterItem，旧 Modifier / 视觉已作废）
    level_start = manager.find("private void OnLevelInitializedCheckSetBonus()")
    level_body = manager[level_start:level_start + 1200] if level_start >= 0 else ""
    frost_idx = level_body.find("DeactivateFrostSetBonus();")
    thunder_idx = level_body.find("DeactivateThunderSetBonus();")
    check_idx = level_body.find("CheckSetBonusStatus(main, false);")
    if min(frost_idx, thunder_idx, check_idx) < 0 or not (frost_idx < check_idx and thunder_idx < check_idx):
        return fail("OnLevelInitializedCheckSetBonus must deactivate both sets before re-checking")

    rc = require(factory, (
        "public static bool TryBindLoadedEquipmentModel(Item itemPrefab, string modelBaseName)",
        'SetAgentUtilityPrefab(itemPrefab, "EquipmentModel", modelAgent);',
        "InjectItemGraphicForEquipment(itemPrefab, modelAgent, true);",
    ), "equipment factory resource-model binding")
    if rc:
        return rc

    rc = require(config, (
        'private const string FROST_HELMET_BASE = "FrostCrown_Helmet";',
        'private const string FROST_ARMOR_BASE = "IceArmor_Armor";',
        'private const string THUNDER_HELMET_BASE = "ThunderHorn_Helmet";',
        'private const string THUNDER_ARMOR_BASE = "ThunderArmor_Armor";',
        "public static bool TryConfigure(Item item, string baseName)",
        "public static bool TryConfigureByTypeId(Item item)",
        "EquipmentFactory.TryBindLoadedEquipmentModel(item, modelBaseName);",
        "EquipmentHelperIcon.TryInjectIcon(item, bundleName, iconAssetName);",
        # 2026-09-06：售价（商店价格 = Value × 耐久比 × priceFactor）与天气防护走幂等路径
        "item.Value = SET_PIECE_VALUE;",
        '"StormProtection" : "ColdProtection"',
        "public const int SET_PIECE_UNLOCK_LEVEL",
    ), "frost/thunder config")
    if rc:
        return rc

    rc = require(placeholder, (
        "FrostThunderSetConfig.TryConfigureByTypeId(existing);",
        "FrostThunderSetConfig.TryConfigureByTypeId(clone);",
        "Item clone = UnityEngine.Object.Instantiate(source);",
    ), "set bonus placeholder fallback-delegation")
    if rc:
        return rc

    # 获取路径：走 Harmony OnDead 前缀（原版地图也能掉），不是奖励箱专用协程。
    # defer 协议的四处接线由 ExtraBossDropDeferGuard 逐条断言，这里只钉住入口与身份判定。
    rc = require(on_dead_patch, (
        "SetBonusBossDropHandler.TryHandleSetBonusBossDeath(__instance);",
    ), "on-dead patch wiring")
    if rc:
        return rc

    rc = require(set_loot, (
        'private const string StormZoneBossNameKeyPrefix = "Cname_StormBoss";',
        'private const string BlueBossNameKey = "Cname_Boss_Blue";',
        "FrostThunderSetConfig.THUNDER_HELMET_ID",
        "FrostThunderSetConfig.FROST_HELMET_ID",
        "BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(typeId)",
        "ItemAssetsCollection.InstantiateSync(typeId);",
        "internal static void ResetStaticCaches()",
    ), "set bonus loot")
    if rc:
        return rc

    # roll 结果必须在死亡帧定下并随 pending 携带：三条消费通道（进箱 / 落地 / characterItem 回退）
    # 必须发同一件，不能到消费时再摇一次。
    if "Dictionary<CharacterMainControl, int> pendingBossRushLootboxDrops" not in set_loot:
        return fail("set bonus loot must remember the rolled TypeID per boss in the pending table")

    shop_start = goblin.find("public List<ShopItemEntry> GetShopItems()")
    shop_body = goblin[shop_start:shop_start + 3000] if shop_start >= 0 else ""
    for type_id in ("FROST_HELMET_ID", "FROST_ARMOR_ID", "THUNDER_HELMET_ID", "THUNDER_ARMOR_ID"):
        if "FrostThunderSetConfig." + type_id not in shop_body:
            return fail("goblin shop missing set piece -> " + type_id)

    print("SetBonusLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
