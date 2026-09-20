"""Frost/Thunder set bonuses must have scoped triggers and paired cleanup.

2026-09-06 扩展（龙王级重做 + 开放获取）：
- 雷霆反震的 CreateExplosion 必须显式传 canHurtSelf=false，且伤害走 buff/effect 通道；
- 雷噬 / 霜噬（2026-09-20 起改为普攻附带）只在 Health.OnHurt 回调里过滤与调度，
  结算延后到协程，并有内置冷却与重入门控；击杀不再触发任何套装技能；
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
        "TryScheduleFrostBite(health, damageInfo);",
        "DestroySetEyeLights(ref frostSetEyeLights);",
        "StopFrostMist();",
        "ResetFrostNovaState();",
        "private bool TryApplyFrostFreeze(CharacterMainControl target)",
        # 官方 AddBuff 返回 void 且有静默 no-op 路径：必须回读 buffManager，不能写死 return true
        "private static bool HasFrostFreezeBuff(CharacterMainControl target, Buff freezeBuff)",
        "return target.HasBuff(freezeBuff.ID);",
        "if (HasFrostFreezeBuff(target, freezeBuff))",
        "return ApplyFrostSetFallbackSlow(target);",
        "private bool ApplyFrostSetFallbackSlow(CharacterMainControl target)",
        "GetSetBonusElementDamagePortion(health, damageInfo, ElementTypes.ice)",
        "if (!frostSetActive || health == null) return;",
        "if (!health.IsMainCharacterHealth)",
        "if (health.IsDead) return;",
    ), "frost bonus")
    if rc:
        return rc

    rc = require(frost_nova, (
        # 普攻附带：内置冷却 + 常数伤害 + 只认玩家亲手的直接命中，三道闸缺一不可
        "if (!frostSetActive || frostBiteResolving || frostBitePending) return;",
        "if (Time.time - lastFrostBiteTime < FROST_BITE_COOLDOWN) return;",
        "frostBitePending = true;",
        "frostBitePending = false;",
        "if (damageInfo.isFromBuffOrEffect) return;",
        "if (!(damageInfo.finalDamage > 0f)) return;",
        "lastFrostBiteTime = Time.time;",
        "yield return frostBiteWait;",
        "generation != setBonusGeneration",
        "frostBiteResolving = true;",
        "frostBiteResolving = false;",
        "dmg.damageValue = FROST_BITE_DAMAGE;",
        "dmg.isFromBuffOrEffect = true;",
        "dmg.fromWeaponItemID = 0;",
        "TryApplyFrostFreeze(victim)",
    ), "frost bite")
    if rc:
        return rc

    # 伤害必须是常数，不得乘任何武器/命中伤害：否则高 DPS 武器会把套装增伤拉成主输出
    if "damageInfo.finalDamage *" in frost_nova or "damageInfo.damageValue *" in frost_nova:
        return fail("frost bite damage must not scale with the triggering hit")

    # 2026-09-20 第三轮：冷却只能在「效果真的落地」之后扣，不能在排队时先扣。
    # 判据用**位置关系**而不是子串存在性：两者在文件里都在，顺序才是不变式。
    for label, text, pending_call, cooldown_write in (
        ("frost bite", frost_nova, "StartCoroutine(FrostBiteStep(", "lastFrostBiteTime = Time.time;"),
        ("thunder bite", thunder_storm, "StartCoroutine(ThunderBiteStep(", "lastThunderBiteTime = Time.time;"),
    ):
        schedule_at = text.find(pending_call)
        cooldown_at = text.find(cooldown_write)
        if schedule_at < 0 or cooldown_at < 0:
            return fail(label + " missing schedule/cooldown anchors")
        if cooldown_at < schedule_at:
            return fail(label + " must commit its cooldown inside the resolution step, not before StartCoroutine")
        if text.count(cooldown_write) != 1:
            return fail(label + " cooldown must be written exactly once (resolution step only)")

    # 雷噬扫不到别的敌人 = 不造成任何伤害，此时不得扣冷却
    storm_step = thunder_storm.split("private IEnumerator ThunderBiteStep(", 1)[1]
    empty_scan_at = storm_step.find("if (count <= 0) yield break;")
    storm_cooldown_at = storm_step.find("lastThunderBiteTime = Time.time;")
    if empty_scan_at < 0 or storm_cooldown_at < 0 or storm_cooldown_at < empty_scan_at:
        return fail("thunder bite must skip the cooldown when the scan finds nobody")

    # 反击冻结：冷却只能在 TryApplyFrostFreeze 返回 true 的分支里扣
    counter_body = frost.split("// 2) 反击冻结", 1)[1].split("catch (Exception e)", 1)[0]
    freeze_at = counter_body.find("if (TryApplyFrostFreeze(damageInfo.fromCharacter))")
    counter_cooldown_at = counter_body.find("lastFrostTriggerTime = Time.time;")
    if freeze_at < 0 or counter_cooldown_at < 0 or counter_cooldown_at < freeze_at:
        return fail("frost counter must consume its cooldown only after the freeze actually lands")

    rc = require(thunder, (
        "private Stat thunderSetElecResistStat = null;",
        "thunderSetElecResistStat = elecFactorStat;",
        "thunderSetElecResistStat.RemoveModifier(thunderSetElecResistModifier);",
        "if (damageInfo.fromCharacter == null) return;",
        "object.ReferenceEquals(damageInfo.fromCharacter, player)",
        # 2026-09-06：单一 OnDead 分派点 + 反震延后一帧 + buff/effect 通道 + 停用清理
        "Health.OnDead += OnThunderSetAnyDead;",
        "Health.OnDead -= OnThunderSetAnyDead;",
        "TryScheduleThunderBite(health, damageInfo);",
        "StartCoroutine(ThunderCounterStep(player, damageInfo.fromCharacter, setBonusGeneration));",
        "dmg.isFromBuffOrEffect = true;",
        "dmg.fromWeaponItemID = 0;",
        "DestroySetEyeLights(ref thunderSetEyeLights);",
        "DestroySetArcPool();",
        "ResetThunderChainState();",
        "GetSetBonusElementDamagePortion(health, damageInfo, ElementTypes.electricity)",
        "if (!thunderSetActive || health == null) return;",
        "if (!health.IsMainCharacterHealth)",
        "if (health.IsDead) return;",
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
    # 雷电反震用 flash（闪光）而不是 normal（火焰系爆炸）：normal 在实机上是一团火
    if "ExplosionFxTypes.flash" not in thunder:
        return fail("thunder counter must use ExplosionFxTypes.flash, not the fire-flavoured normal explosion")

    rc = require(thunder_storm, (
        # 普攻附带：内置冷却 + 常数伤害 + 只认玩家亲手的直接命中，三道闸缺一不可
        "if (!thunderSetActive || thunderBiteResolving || thunderBitePending) return;",
        "if (Time.time - lastThunderBiteTime < THUNDER_BITE_COOLDOWN) return;",
        "thunderBitePending = true;",
        "thunderBitePending = false;",
        "if (damageInfo.isFromBuffOrEffect) return;",
        "if (!(damageInfo.finalDamage > 0f)) return;",
        "lastThunderBiteTime = Time.time;",
        "yield return thunderBiteWait;",
        "generation != setBonusGeneration",
        "thunderBiteResolving = true;",
        "thunderBiteResolving = false;",
        # 命中目标本身不再吃套装伤害（否则等于直接给武器加伤）
        "ScanSetBonusEnemies(origin, THUNDER_BITE_RADIUS, struckTarget, THUNDER_BITE_MAX_TARGETS)",
        "dmg.damageValue = THUNDER_BITE_DAMAGE;",
        "dmg.isFromBuffOrEffect = true;",
        "dmg.fromWeaponItemID = 0;",
        "private void StopThunderAmbientArcLoop()",
    ), "thunder bite")
    if rc:
        return rc

    # 伤害必须是常数，不得乘任何武器/命中伤害
    if "damageInfo.finalDamage *" in thunder_storm or "damageInfo.damageValue *" in thunder_storm:
        return fail("thunder bite damage must not scale with the triggering hit")

    # 只有一跳：被电死的目标不得再起第二段（击杀自续清场是旧设计的病灶）
    if thunder_storm.count("StartCoroutine(ThunderBiteStep(") != 1:
        return fail("ThunderBiteStep must be started exactly once (no chain continuation)")

    # 主角死亡也必须作废已排队的延时技能；只清冷却会让旧伤害在死后/复活后执行。
    for name, source in (("Frost", frost), ("Thunder", thunder)):
        death_body = source.split("private void On" + name + "SetAnyDead(", 1)[1]
        death_body = death_body.split("}", 1)[0]
        if "BumpSetBonusGeneration();" not in death_body:
            return fail(name + " player death must invalidate pending spells")

    # 击杀不再触发任何套装技能：OnDead 分派器只剩主角分支
    for name, source in (("Frost", frost), ("Thunder", thunder)):
        death_body = source.split("private void On" + name + "SetAnyDead(", 1)[1].split("\n        }", 1)[0]
        if "TrySchedule" in death_body:
            return fail(name + " kills must no longer schedule set bonus spells")

    for name, text in (("thunder_storm", thunder_storm), ("frost_nova", frost_nova), ("visuals", visuals)):
        if "Health.OnDead +=" in text or "Health.OnHurt +=" in text:
            return fail("only ThunderSetBonus.cs / FrostSetBonus.cs may subscribe Health events (paired += / -= in one file) -> " + name)

    rc = require(visuals, (
        "private bool TryResolveSetBonusEnemyTarget(Health target, DamageInfo info, out CharacterMainControl victim, out Vector3 position)",
        "if (target.IsMainCharacterHealth) return false;",
        "if (IsModeHRunInProgressSafe()) return false;",
        "if (info.fromCharacter == null || !info.fromCharacter.IsMainCharacter) return false;",
        "if (PetNestCompanionAgent.IsCompanionHealth(target)) return false;",
        "if (!Team.IsEnemy(info.fromCharacter.Team, resolved.Team)) return false;",
        "Team.IsEnemy(player.Team, character.Team)",
        "private void DestroySetArcPool()",
        "private void DestroySetEyeLights(ref SetEyeLightState state)",
        # 电弧池是独立 MonoBehaviour：宿主只留门面，不把 LineRenderer 细节堆回 ModBehaviour
        "BossRush.Common.Effects.SetBonusArcPool.Create()",
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
