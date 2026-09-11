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
- 表现层：四个触发瞬间各自的特效 / 音效调用在位；三把近战走共享挥砍拖尾补丁。
- 自建伤害：毒爆发与雷电释放必须标 isFromBuffOrEffect（与 Mode G 矩阵登记口径一致）。
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
ON_DEAD_PATCH = Path("Patches/Combat/CharacterOnDeadPatch.cs")
GOBLIN = Path("Integration/Affinity/NPCs/GoblinAffinityConfig.cs")
ITEM_REGISTRY = Path("Integration/Items/ItemContentRegistry.cs")
VIPER_RUNTIME = Path("Integration/NewWeapons/ViperDagger/ViperDaggerRuntime.cs")
THUNDER_RUNTIME = Path("Integration/NewWeapons/ThunderRing/ThunderRingRuntime.cs")
SHIELD_RUNTIME = Path("Integration/NewWeapons/EnergyShield/EnergyShieldRuntime.cs")
SPEAR_RUNTIME = Path("Integration/NewWeapons/FrostSpear/FrostSpearRuntime.cs")
STAFF_ACTION = Path("Integration/NewWeapons/SummonStaff/SummonStaffAction.cs")

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

    # Apply 的调用点：只断言定义存在的话，把某一把的调用注释掉照样全绿
    APPLY_CALL_SITES = {
        "ViperDagger/ViperDaggerWeaponConfig.cs": "ViperDaggerTypeId",
        "SummonStaff/SummonStaffWeaponConfig.cs": "SummonStaffTypeId",
        "EnergyShield/EnergyShieldWeaponConfig.cs": "EnergyShieldTypeId",
        "FrostSpear/FrostSpearWeaponConfig.cs": "FrostSpearTypeId",
        "ThunderRing/ThunderRingWeaponConfig.cs": "ThunderRingTypeId",
    }
    for rel_name, const_name in sorted(APPLY_CALL_SITES.items()):
        snippet = "NewWeaponItemAttributes.Apply(item, NewWeaponIds.{});".format(const_name)
        if snippet not in read(Path("Integration/NewWeapons") / rel_name):
            errors.append("[StableLine] 缺少属性写入调用 -> " + rel_name + " : " + snippet)

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
        "burstDamage.isFromBuffOrEffect = true;",
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
    require(read(BOOTSTRAP), (
        "FrostSpearRuntime.Unsubscribe();",
        "NewWeaponFx.ResetStaticCaches();",
        "NewWeaponBossDropHandler.ResetStaticCaches();",
    ), "Cleanup")

    if errors:
        print("NewWeaponLifecycleGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("NewWeaponLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
