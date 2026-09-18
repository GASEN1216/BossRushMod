"""P0 五把新武器（500048-500052）的获取途径与表现层接线守卫。

背景：这五把武器长期处于「代码完整但玩家拿不到」的状态——编译绿、全量 guard 绿，
因为守卫断言的是结构不变式，不验证「玩家操作能否走到内容」。2026-09-07 开放获取后，
以下每一条被删掉都会让功能静默失效而不触发任何既有守卫，故单独钉住。

不变式：
- 运气线：OnDead 前缀入口存在；五个 Boss nameKey 常量齐全；掉率与 defer 协议入口在位。
  （defer 协议的四处接线由 ExtraBossDropDeferGuard 逐条断言，这里不重复。）
- 稳定线：叮当商店五条 ShopItemEntry 齐全，解锁等级与库存走 NewWeaponShopConfig。
- 售价：NewWeaponItemAttributes 必须写 item.Value —— 不写商店会标价 0 元。
- 注册即配置：五个 TypeID 登记进 ItemFactory 配置器，不能只靠延迟 bootstrap 那一次调用。
- 共享配置流程：五个 XxxWeaponConfig 只声明 Spec 并委托 NewWeaponConfiguratorCore，
  品质/售价/耐久由 core 统一调 NewWeaponItemAttributes.Apply；不许退回「每把各抄一遍模板」。
- 表现层：四个触发瞬间各自的特效 / 音效调用在位；三把近战走共享挥砍拖尾补丁。
- 自建伤害：毒爆发、雷电释放、灵魂爆裂必须标 isFromBuffOrEffect（与 Mode G 矩阵登记口径一致）。
- 成长项：毒爆发与雷电释放的「固定底 + 比例」两个常量都在位——只留固定底会让两件装备中后期失效。
- 召唤法杖定位：投放距离与灵魂爆裂常量在位，且爆裂不在死亡事件栈里调官方爆炸。
"""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402


REPO_ROOT = Path(__file__).resolve().parents[1]

IDS = Path("Integration/NewWeapons/Common/NewWeaponIds.cs")
ATTRIBUTES = Path("Integration/NewWeapons/Common/NewWeaponItemAttributes.cs")
CONFIGURATORS = Path("Integration/NewWeapons/Common/NewWeaponItemConfigurators.cs")
DROP_HANDLER = Path("Integration/NewWeapons/Common/NewWeaponBossDropHandler.cs")
FX = Path("Integration/NewWeapons/Common/NewWeaponFx.cs")
MELEE_FX = Path("Integration/NewWeapons/Common/NewWeaponMeleeFx.cs")
BOOTSTRAP = Path("Integration/NewWeapons/Common/NewWeaponBootstrap.cs")
RUNTIME = Path("Integration/NewWeapons/Common/NewWeaponRuntime.cs")
CONFIG_CORE = Path("Integration/NewWeapons/Common/NewWeaponConfiguratorCore.cs")
EQUIP_STATE = Path("Integration/NewWeapons/Common/NewWeaponEquipState.cs")
ON_DEAD_PATCH = Path("Patches/Combat/CharacterOnDeadPatch.cs")
GOBLIN = Path("Integration/Affinity/NPCs/GoblinAffinityConfig.cs")
ITEM_REGISTRY = Path("Integration/Items/ItemContentRegistry.cs")
VIPER_RUNTIME = Path("Integration/NewWeapons/ViperDagger/ViperDaggerRuntime.cs")
VIPER_CONFIG = Path("Integration/NewWeapons/ViperDagger/ViperDaggerConfig.cs")
THUNDER_RUNTIME = Path("Integration/NewWeapons/ThunderRing/ThunderRingRuntime.cs")
THUNDER_CONFIG = Path("Integration/NewWeapons/ThunderRing/ThunderRingConfig.cs")
SHIELD_RUNTIME = Path("Integration/NewWeapons/EnergyShield/EnergyShieldRuntime.cs")
SPEAR_RUNTIME = Path("Integration/NewWeapons/FrostSpear/FrostSpearRuntime.cs")
STAFF_ACTION = Path("Integration/NewWeapons/SummonStaff/SummonStaffAction.cs")
STAFF_CONFIG = Path("Integration/NewWeapons/SummonStaff/SummonStaffConfig.cs")

WEAPON_TYPE_IDS = {
    "ViperDaggerTypeId": 500048,
    "SummonStaffTypeId": 500049,
    "EnergyShieldTypeId": 500050,
    "FrostSpearTypeId": 500051,
    "ThunderRingTypeId": 500052,
}

BOSS_NAME_KEYS = (
    "Cname_Prison_Boss",
    "Cname_XING",
    "Cname_PMCLeader",
    "Cname_Snow_BigIce",
    "Cname_Boss_3Shot",
)

errors = []


def read(rel):
    """读 C# 并剥掉注释与 #if false 区块——注释掉的接线不算接线。"""
    path = REPO_ROOT / rel
    if not path.is_file():
        errors.append("[MissingFile] " + rel.as_posix())
        return ""
    return clean_source(path.read_text(encoding="utf-8", errors="replace"))


def require(text, snippets, label):
    for snippet in snippets:
        if snippet not in text:
            errors.append("[{}] 缺少接线 -> {}".format(label, snippet))


def main():
    ids = read(IDS)
    for const_name, type_id in sorted(WEAPON_TYPE_IDS.items()):
        expected = "public const int {} = {};".format(const_name, type_id)
        if expected not in ids:
            errors.append("[TypeId] NewWeaponIds 缺少或改动了 -> " + expected)

    # ---- 运气线 ----
    require(read(ON_DEAD_PATCH), (
        "NewWeaponBossDropHandler.TryHandleNewWeaponBossDeath(__instance);",
    ), "LuckLine")

    drop = read(DROP_HANDLER)
    for name_key in BOSS_NAME_KEYS:
        if '"{}"'.format(name_key) not in drop:
            errors.append("[LuckLine] 掉落 handler 缺少 Boss nameKey -> " + name_key)
    require(drop, (
        "private const float WeaponDropChance = 0.20f;",
        # 比较方向也要钉：改成 < 就变成八成掉率，改成 * 0f 就永远不掉
        "if (UnityEngine.Random.value >= WeaponDropChance)",
        "ShouldDeferExtraBossDropToModPath",
        "BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(typeId)",
        "ItemAssetsCollection.InstantiateSync(typeId)",
        "internal static void ResetStaticCaches()",
        # roll 必须在死亡帧定下并以 TypeID 随 pending 携带，否则三条消费通道各摇一次
        "Dictionary<CharacterMainControl, int> pendingBossRushLootboxDrops",
    ), "LuckLine")

    # ---- 稳定线 ----
    goblin = read(GOBLIN)
    for const_name in sorted(WEAPON_TYPE_IDS):
        entry = "new ShopItemEntry(NewWeaponIds.{}, NewWeaponShopConfig.UnlockLevel, NewWeaponShopConfig.MaxStock)".format(const_name)
        if entry not in goblin:
            errors.append("[StableLine] 叮当商店缺少上架条目 -> " + entry)

    attributes = read(ATTRIBUTES)
    require(attributes, (
        # 不写 Value，StockShop 价格 = Value x 耐久比 x priceFactor 会算成 0 元
        "item.Value = value;",
        "EquipmentHelper.AddRepairableTag(item);",
        "public const int UnlockLevel = 5;",
        "public const int MaxStock = 1;",
        # 钉住数值本身，不只是赋值语句：把价目表改成 0 同样让商店标价 0 元
        "public const int MeleeWeaponValue = 20000;",
        "public const int TotemValue = 16000;",
    ), "StableLine")

    # Apply 的调用点：五把武器的 Spec 各自声明 TypeId，由共享流程统一写属性。
    # 只断言 core 里有 Apply 的话，把某个 Spec 的 TypeId 写错照样全绿，所以两头都钉。
    SPEC_TYPE_IDS = {
        "ViperDagger/ViperDaggerWeaponConfig.cs": "ViperDaggerTypeId",
        "SummonStaff/SummonStaffWeaponConfig.cs": "SummonStaffTypeId",
        "EnergyShield/EnergyShieldWeaponConfig.cs": "EnergyShieldTypeId",
        "FrostSpear/FrostSpearWeaponConfig.cs": "FrostSpearTypeId",
        "ThunderRing/ThunderRingWeaponConfig.cs": "ThunderRingTypeId",
    }
    for rel_name, const_name in sorted(SPEC_TYPE_IDS.items()):
        weapon_config = read(Path("Integration/NewWeapons") / rel_name)
        snippet = "TypeId = NewWeaponIds.{},".format(const_name)
        if snippet not in weapon_config:
            errors.append("[StableLine] Spec 缺少 TypeId 声明 -> " + rel_name + " : " + snippet)
        # 退回「每把各抄一遍配置模板」会让改一处口径漏掉其它几把，这里堵死
        for template in ("private static void ConfigureStats(", "private static void ConfigureTags("):
            if template in weapon_config:
                errors.append("[SharedConfigurator] " + rel_name
                              + " 又抄回了本地配置模板 -> " + template)

    config_core = read(CONFIG_CORE)
    require(config_core, (
        "NewWeaponItemAttributes.Apply(item, spec.TypeId);",
        "internal static bool ConfigureMelee(Item item, string baseName, NewWeaponMeleeSpec spec)",
        "internal static bool ConfigureTotem(Item item, string baseName, NewWeaponTotemSpec spec)",
    ), "SharedConfigurator")
    if config_core.count("NewWeaponItemAttributes.Apply(item, spec.TypeId);") < 2:
        errors.append("[SharedConfigurator] 近战与图腾两条流程都必须调 NewWeaponItemAttributes.Apply")

    # ---- 注册即配置 ----
    require(read(ITEM_REGISTRY), (
        "NewWeaponItemConfigurators.RegisterAll();",
    ), "ConfigureOnRegister")
    configurators = read(CONFIGURATORS)
    for const_name in sorted(WEAPON_TYPE_IDS):
        snippet = "ItemFactory.RegisterConfigurator(NewWeaponIds.{},".format(const_name)
        if snippet not in configurators:
            errors.append("[ConfigureOnRegister] 缺少配置器登记 -> " + snippet)

    # ---- 表现层 ----
    require(read(VIPER_RUNTIME), (
        "burstDamageInfo.isFromBuffOrEffect = true;",
        "NewWeaponFx.PlayBurst(",
        "NewWeaponFx.PlaySound(NewWeaponSfx.VenomBurst);",
    ), "Fx:ViperDagger")
    require(read(THUNDER_RUNTIME), (
        "thunderDamage.isFromBuffOrEffect = true;",
        "NewWeaponFx.PlayArc(",
        "NewWeaponFx.PlaySound(NewWeaponSfx.ThunderRelease);",
    ), "Fx:ThunderRing")
    require(read(SHIELD_RUNTIME), (
        "NewWeaponFx.PlayBurst(",
        "NewWeaponFx.PlaySound(NewWeaponSfx.ShieldAbsorb);",
        "if (targetHealth == null || targetHealth.IsDead || !targetHealth.IsMainCharacterHealth) return;",
    ), "Fx:EnergyShield")
    require(read(SPEAR_RUNTIME), (
        # 每次命中都会跑，必须保留去重与「不点实时光」两个约束
        "PerTargetFxCooldown",
        "withLight: false",
    ), "Fx:FrostSpear")
    require(read(STAFF_ACTION), (
        "NewWeaponFx.PlaySound(NewWeaponSfx.SoulSummon);",
    ), "Fx:SummonStaff")
    require(read(THUNDER_RUNTIME), (
        "if (isPlayerHurt && targetHealth.IsDead) return;",
    ), "Runtime:DeadHurtGate")
    require(read(MELEE_FX), (
        '[HarmonyPatch(typeof(CA_Attack), "OnStart")]',
        "NewWeaponSwingFx.PlayAt(",
    ), "Fx:Swing")

    # ---- 命中归因（2026-09-18）----
    # 「打在敌人身上才触发」的机制必须过同一套谓词：不过滤的话，打木箱、误伤自己的召唤物、
    # 以及本 Mod 自己的 buff/效果伤害（荆棘反弹、殉爆、毒爆发）都会把攒满的电能/毒层白白吃掉。
    require(read(EQUIP_STATE), (
        "internal static bool IsHostileVictim(Health victim)",
        "internal static bool IsPlayerDirectHit(Health victim, ref DamageInfo info, CharacterMainControl player)",
        "return victim.team != Teams.player;",
        "if (info.isFromBuffOrEffect) return false;",
        # 事件驱动缓存的三个订阅点与退订路径
        "CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldItemChanged;",
        "CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldItemChanged;",
        "internal static void ResetStaticCaches()",
    ), "Attribution")
    require(read(VIPER_RUNTIME), (
        "if (!NewWeaponAttribution.IsHostileVictim(targetHealth)) return;",
    ), "Attribution:ViperDagger")
    require(read(THUNDER_RUNTIME), (
        "if (!NewWeaponAttribution.IsPlayerDirectHit(targetHealth, ref damageInfo, player)) return;",
        # DoT 不该能把电能攒满（站火里 1.5 秒满层）
        "if (damageInfo.isFromBuffOrEffect) return;",
    ), "Attribution:ThunderRing")

    # ---- 数值成长项（中后期不失效）----
    require(read(VIPER_CONFIG), (
        "public const float BurstDamageOnMaxStack = 35f;",
        "public const float BurstAccumulatedDamageRatio =",
    ), "Scaling:ViperDagger")
    require(read(VIPER_RUNTIME), (
        "accumulatedDamage * ViperDaggerConfig.BurstAccumulatedDamageRatio",
    ), "Scaling:ViperDagger")
    require(read(THUNDER_CONFIG), (
        "public const float ReleaseDamage = 40f;",
        "public const float ReleaseHitDamageRatio =",
    ), "Scaling:ThunderRing")
    require(read(THUNDER_RUNTIME), (
        "damageInfo.finalDamage * ThunderRingConfig.ReleaseHitDamageRatio",
    ), "Scaling:ThunderRing")

    # ---- 召唤法杖定位（与霜之哀伤错开）----
    require(read(STAFF_CONFIG), (
        "public const float PlacementDistance =",
        "public const float SoulBurstDamage =",
        "public const float SoulBurstRadius =",
    ), "Identity:SummonStaff")
    staff_action = read(STAFF_ACTION)
    require(staff_action, (
        "ResolvePlacementCenter(player, playerPos)",
        "player.CurrentAimDirection",
        "blast.isFromBuffOrEffect = true;",
        "ExplosionFxTypes.normal",
        "private void TriggerSoulBurst()",
    ), "Identity:SummonStaff")
    # 爆裂只能从 Update 触发：在 Health.OnDead/OnHurt 派发栈里调官方爆炸会覆写它的命中缓冲
    if "Health.OnDead" in staff_action or "Health.OnHurt" in staff_action:
        errors.append("[Identity:SummonStaff] 灵魂爆裂不得订阅 Health 死亡/受伤事件，"
                      "只能在 Update 里按 IsDead 自查（CR-2026-09-17-015）")

    # 音效路径必须惰性解析：静态字段里拼 Assembly.Location 会毒化类型并吃掉后续气泡
    fx = read(FX)
    require(fx, ("ModBehaviour.GetModPath();",), "Fx:SfxPath")
    if "Assembly" in fx and ".Location" in fx:
        errors.append("[Fx:SfxPath] 音效路径不得经 Assembly.Location 推导，走 ModBehaviour.GetModPath()")

    # 音效文件名必须与磁盘上的产物对得上（改个名字 PlaySoundEffect 会静默返回）
    sfx_dir = REPO_ROOT / "Assets" / "Sounds" / "NewWeapons"
    for wav in ("venom_burst.wav", "soul_summon.wav", "shield_absorb.wav", "thunder_release.wav"):
        if '"{}"'.format(wav) not in fx:
            errors.append("[Fx:SfxPath] NewWeaponSfx 缺少音效常量 -> " + wav)
        elif not (sfx_dir / wav).is_file():
            # 产物是 local-only（Assets 进 .gitignore），缺文件只提示不判红
            print("  [WARN] 音效产物缺失，跑 tools/gen_newweapon_sfx.py 生成 -> " + wav)

    # ---- 清理 ----
    # 实现在 NewWeaponRuntime（AGENTS §4.15：子系统状态不放宿主 partial），
    # 宿主只留四个一行转发；两头都钉，任一处断掉都等于清理链断掉。
    require(read(RUNTIME), (
        "NewWeaponEquipState.Unsubscribe();",
        "FrostSpearRuntime.Unsubscribe();",
        "NewWeaponEquipState.ResetStaticCaches();",
        "NewWeaponFx.ResetStaticCaches();",
        "NewWeaponBossDropHandler.ResetStaticCaches();",
        "SummonStaffAction.CleanupAllSummonedAllies();",
    ), "Cleanup")
    require(read(BOOTSTRAP), (
        "NewWeaponRuntime.Initialize();",
        "NewWeaponRuntime.SetupForScene(this, scene);",
        "NewWeaponRuntime.ConfigureAfterLoad();",
        "NewWeaponRuntime.CleanupOnDestroy();",
    ), "HostForwarding")

    if errors:
        print("NewWeaponLifecycleGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("NewWeaponLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
