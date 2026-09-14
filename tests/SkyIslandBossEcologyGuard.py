"""天空岛头目 / 岛主（R1：残星匠首、瞭台观星手）的结构不变式。

行为由执行回归 SkyIslandStory（SkyIslandBossRulesRegression：掉落精确计频、档次单调、逃圈速度、五栏防换皮）
与 F3 只读用例 SKY_BOSS_PROFILES 验证；这里只钉「接线在不在、顺序对不对、没有走回龙王那条老路」：

1. 档案挂在已有自动组的带队位上：内容表（内置表 + World.json）G 组带队是 Lord、S4 组带队是 Chief，id / marker / count 不变。
2. 身份层先分派给 SkyIslandBossForge，再走普通档次装饰；具名剧情对手（折翎、钟守）与噬风的分支原样在前。
3. 配装即掉落：
   - 配装先问 prefab 再实例化（缺资源时官方回空壳），刷新模型用 ForceInvokeSlotContentChangedEvent，不重复调 SetItem；
   - 掉落只挂该角色实例的 BeforeCharacterSpawnLootOnDead（官方建箱之前），订阅与退订成对，结算有一次性闩；
   - 抽样走纯规则 RollDrop + System.Random 种子，不用 UnityEngine.Random；没抽中的配装 Unplug 后 DestroyTree；
   - 头目 / 岛主的文件里不出现 BossRush 奖励箱追踪、OnDead 额外掉落处理器或手工建箱（龙王难管的三条老路）。
4. 招式控制器：只订自己身上的 Health.OnDeadEvent 并在 OnDestroy 退订；范围伤害走官方爆炸且 buff 通道、不伤自己。
5. 剧情：击败事件的订阅幂等、在 Dispose 退订；首杀手记 id 进 RecordNote 白名单。
6. 物品登记：四件专属装备的常量、中英名、正价值、AllTypeIds、掉落黑名单（代码 + JSON）、动态注册计划（bundle + 克隆兜底）、
   装备工厂配置链、本地化注入、编译清单与 bundle 部署行全部在位。

反向检查：每条关键断言都在内存里把源码改坏一次，确认本守卫真的会红（子串判断能被注释骗过，这里一律先剥注释再比）。
"""
import json
import re
import sys
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent
SKY = "DebugAndTools/SkyIsland/"

PATHS = {
    "rules": SKY + "SkyIslandBossRules.cs",
    "forge": SKY + "SkyIslandBossForge.cs",
    "loot": SKY + "SkyIslandBossLoot.cs",
    "foreman": SKY + "SkyIslandForemanBoss.cs",
    "stargazer": SKY + "SkyIslandStargazerChief.cs",
    "session": SKY + "SkyIslandSessionBosses.cs",
    "world_bosses": SKY + "SkyIslandWorldStoryBosses.cs",
    "fieldcraft_gear": SKY + "SkyIslandFieldcraftBossGear.cs",
    "encounters": SKY + "SkyIslandEncounters.cs",
    "content": SKY + "SkyIslandContent.cs",
    "tier": SKY + "SkyIslandEnemyTier.cs",
    "world": SKY + "SkyIslandWorldStory.cs",
    "service": SKY + "SkyIslandStoryService.cs",
    "item_rules": SKY + "SkyIslandItemRules.cs",
    "module": SKY + "SkyIslandRuntimeModule.cs",
    "gear_config": "Integration/SkyIsland/SkyIslandBossGearConfig.cs",
    "ids": "Config/ConfigItemIds.cs",
    "blacklist": "Config/LootBlacklistRegistry.cs",
    "registry": "Integration/BossRushDynamicItemRegistry.cs",
    "factory": "Integration/EquipmentFactory.cs",
    "equipment_loc": "Localization/EquipmentLocalization.cs",
    "bat": "compile_official.bat",
    "world_json": "Assets/Data/SkyIsland/World.json",
    "blacklist_json": "Assets/Data/LootBlacklist.json",
}

NEW_SOURCES = ("rules", "forge", "loot", "foreman", "stargazer", "session", "world_bosses", "fieldcraft_gear", "gear_config")

GEAR = {
    "SkyIslandStarbrassVisorHelm": 500086,
    "SkyIslandStarfurnaceHarness": 500087,
    "SkyIslandStarfurnacePack": 500088,
    "SkyIslandStargazerLensHelm": 500089,
}

BOSS_FILES = ("rules", "forge", "loot", "foreman", "stargazer", "world_bosses", "fieldcraft_gear")
OLD_LOOT_PATHS = ("RegisterBossRandomLootTracking", "MarkBossRushLootboxPathTracking", "AddBossSpecialLootToLootbox",
                  "CharacterOnDeadPatch", "ShouldDeferExtraBossDrop", "CreateFromItem", "TransferLoot")


def squash(text):
    return re.sub(r"\s+", "", text)


def body_of(source, signature):
    at = source.find(signature)
    if at < 0:
        return None
    brace = source.find("{", at)
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:index]
    return None


def ordered(errors, text, tokens, why):
    position = -1
    flat = squash(text or "")
    for token in tokens:
        found = flat.find(squash(token), position + 1)
        if found < 0:
            errors.append(why + "：缺少或顺序不对 —— " + token)
            return
        position = found


def require(errors, text, token, why):
    if squash(token) not in squash(text or ""):
        errors.append(why + "：缺少 " + token)


def check(raw):
    errors = []
    code = {key: clean_source(value) if key not in ("bat", "world_json", "blacklist_json") else value
            for key, value in raw.items()}

    # ---- 0. 新文件都在编译清单里 ----
    for key in NEW_SOURCES:
        rel = PATHS[key].replace("/", "\\")
        if "echo(" + rel not in raw["bat"]:
            errors.append("编译清单缺 " + PATHS[key] + "（无通配符，漏了不报错，这位 Boss 直接不存在）")
    require(errors, raw["bat"], 'copy /Y "Assets\\Equipment\\skyisland_boss_gear"', "专属装备 bundle 要在部署段复制到游戏目录")

    # ---- 1. 档案挂在已有自动组的带队位上 ----
    require(errors, code["tier"], "Chief = 4", "档次枚举要有 Chief")
    require(errors, code["tier"], "Lord = 5", "档次枚举要有 Lord")
    require(errors, code["content"], 'if (value == "Chief") { tier = SkyIslandEnemyTier.Chief; return true; }', "内容表按字符串认 Chief")
    require(errors, code["content"], 'if (value == "Lord") { tier = SkyIslandEnemyTier.Lord; return true; }', "内容表按字符串认 Lord")
    require(errors, code["content"],
            'Encounter("G", "EnemySpawn_G", 3, false, SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Lord)', "内置表 G 组带队是岛主")
    require(errors, code["content"],
            'Encounter("S4", "EnemySpawn_S4", 3, false, SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Chief)', "内置表 S4 组带队是头目")
    try:
        encounters = {e["id"]: e for e in json.loads(raw["world_json"])["encounters"]}
        if encounters.get("G", {}).get("lead") != "Lord" or encounters.get("S4", {}).get("lead") != "Chief":
            errors.append("World.json 的 G / S4 带队档次与内置表不一致（严格绑定下整张表会回退）")
    except (ValueError, KeyError, TypeError):
        errors.append("World.json 解析失败")
    for encounter_id, index, kind, tier in (("G", 0, "Foreman", "Lord"), ("S4", 0, "Stargazer", "Chief")):
        pattern = (r'EncounterId = "%s", Index = %d, Kind = SkyIslandBossKind\.%s, Tier = SkyIslandEnemyTier\.%s'
                   % (encounter_id, index, kind, tier))
        if not re.search(pattern, code["rules"]):
            errors.append("档案表缺 %s#%d → %s（%s）" % (encounter_id, index, kind, tier))
    # 五栏设计说明写在档案表每一项上方的「// 【五栏】」注释里（只给维护者与守卫看，不编进运行时），所以这里读原文。
    columns = ("小环境", "核心招式", "克制", "装备联动", "串联")
    blocks = re.findall(r'((?:[ \t]*// 【五栏】[^\r\n]*\r?\n)+)[ \t]*new SkyIslandBossProfile\s*\{\s*EncounterId = "(\w+)"', raw["rules"])
    if not blocks or len(blocks) != len(re.findall(r"new SkyIslandBossProfile\b", code["rules"])):
        errors.append("档案表每一项上方都要有「// 【五栏】」设计说明（小环境 / 核心招式 / 克制 / 装备联动 / 串联）")
    verbs = []
    for block, encounter_id in blocks:
        found = dict(re.findall(r"// 【五栏】(\S+?)：([^\r\n]*)", block))
        for column in columns:
            if not found.get(column, "").strip():
                errors.append("档案 %s 的五栏缺「%s」（防换皮纪律）" % (encounter_id, column))
        verbs.append(found.get("核心招式", "").strip())
    if len(set(verbs)) != len(verbs):
        errors.append("两位 Boss 的核心招式写成了同一句：同一条管线不许只换参数")

    # ---- 2. 身份层分派顺序 ----
    identity = body_of(code["encounters"], "private void ApplyIdentity(CharacterMainControl created, Encounter encounter, int index, SkyIslandEnemyTier tier)")
    ordered(errors, identity, ['SkyIslandEnemyTiers.ApplyStoryChampion(created, "bellkeeper"',
                               "if (SkyIslandBossForge.TryApply(created, encounter.Id, index, BossContext())) return;",
                               "SkyIslandEnemyTiers.Apply(created, tier);", "AddComponent<SkyIslandStormBoss>().Bind("],
            "身份层：具名对手在前、头目 / 岛主分派其次、普通档次装饰与噬风在后")

    # ---- 3. 配装即掉落 ----
    plug = body_of(code["forge"], "private static bool TryPlugPiece(")
    ordered(errors, plug, ["ItemAssetsCollection.GetPrefab(piece.TypeId)", "if (prefab == null)",
                           "ItemAssetsCollection.InstantiateSync(piece.TypeId)", "slot.Plug(created, out unplugged)"],
            "配装先问 prefab 再实例化再插槽")
    require(errors, body_of(code["forge"], "internal static bool TryEquip("), "ForceInvokeSlotContentChangedEvent()",
            "配装后刷新模型用 ForceInvokeSlotContentChangedEvent")
    for key in BOSS_FILES:
        if re.search(r"\.SetItem\(", code[key]):
            errors.append(PATHS[key] + " 不许重复调 CharacterEquipmentController.SetItem（官方只有 += 没有 -=，会累积订阅）")
        if re.search(r"\bcharacter\.transform\.localScale|created\.transform\.localScale|boss\.transform\.localScale", code[key]):
            errors.append(PATHS[key] + " 不许缩放角色 transform（只缩放 characterModel）")
        for token in OLD_LOOT_PATHS:
            if token in code[key]:
                errors.append(PATHS[key] + " 走回了龙王那条掉落老路：" + token)
        if key in ("rules", "loot") and "UnityEngine.Random" in code[key]:
            errors.append(PATHS[key] + " 的掉落抽样不许用 UnityEngine.Random（纯规则 + System.Random 种子）")
    bind = body_of(code["loot"], "internal void Bind(CharacterMainControl character, SkyIslandBossProfile value, int seed)")
    require(errors, bind, "boss.BeforeCharacterSpawnLootOnDead += OnBeforeLoot;", "掉落挂在该角色实例的建箱前事件上")
    require(errors, body_of(code["loot"], "private void OnDestroy()"), "boss.BeforeCharacterSpawnLootOnDead -= OnBeforeLoot;",
            "建箱前事件要在 OnDestroy 退订")
    ordered(errors, body_of(code["loot"], "private void OnBeforeLoot(DamageInfo damage)"),
            ["if (resolved || boss == null || profile == null) return;", "resolved = true;"], "掉落只结算一次")
    require(errors, body_of(code["loot"], "private void OnBeforeLoot(DamageInfo damage)"), "Resolve(random.NextDouble());",
            "死亡结算的抽样走 System.Random 种子")
    resolve = body_of(code["loot"], "private void Resolve(double roll)")
    ordered(errors, resolve, ["SkyIslandBossRules.RollDrop(profile, roll)", "slot.Unplug()", "DestroyTree()"],
            "按权重留一件、其余配装卸下销毁")
    # Dev 演练入口（不走死亡直接结算、只配装不挂招式）只许在 Dev 构建里存在。
    for key, name in (("loot", "DevResolveForDrill"), ("forge", "DevLoadoutForDrill")):
        text = raw[key]
        at = text.find(name)
        opened = text.rfind("#if BOSSRUSH_DEV", 0, at) if at >= 0 else -1
        if at >= 0 and (opened < 0 or text.rfind("#endif", 0, at) > opened):
            errors.append(PATHS[key] + " 的演练入口 %s 必须包在 #if BOSSRUSH_DEV 里（正式构建不许有不走死亡的结算）" % name)
    require(errors, body_of(code["loot"], "private static bool TryAddFresh(Item characterItem, int typeId)"),
            "ItemAssetsCollection.GetPrefab(typeId) == null", "补一件之前先问 prefab")

    # ---- 4. 招式控制器 ----
    for key in ("foreman", "stargazer"):
        require(errors, code[key], "health.OnDeadEvent.AddListener(OnDead);", PATHS[key] + " 要订自己的死亡事件")
        require(errors, body_of(code[key], "private void OnDestroy()"), "health.OnDeadEvent.RemoveListener(OnDead);",
                PATHS[key] + " 要在 OnDestroy 退订死亡事件")
        require(errors, code[key], "SkyIslandBossForge.RaiseDefeated(profile, position);", PATHS[key] + " 倒下时派发击败事件")
    detonate = body_of(code["forge"], "internal static void Detonate(CharacterMainControl source, Vector3 origin, float radius, float damageValue)")
    require(errors, detonate, "damage.isFromBuffOrEffect = true;", "Boss 范围伤害走 buff 通道")
    require(errors, detonate, "CreateExplosion(origin, radius, damage, ExplosionFxTypes.normal, 0f, false);", "Boss 范围伤害 canHurtSelf:false")

    # ---- 5. 剧情 ----
    ordered(errors, body_of(code["world_bosses"], "private void AttachBossEvents()"),
            ["if (bossEventsAttached) return;", "SkyIslandBossForge.Defeated += OnBossDefeated;", "bossEventsAttached = true;"],
            "击败事件订阅幂等")
    require(errors, body_of(code["world_bosses"], "private void DetachBossEvents()"), "SkyIslandBossForge.Defeated -= OnBossDefeated;",
            "击败事件要能退订")
    require(errors, body_of(code["world"], "internal SkyIslandWorldStory(SkyIslandSession session, SkyIslandStoryService story, GameObject root)"),
            "AttachBossEvents();", "剧情 owner 构造时订阅击败事件")
    require(errors, body_of(code["world"], "public void Dispose()"), "DetachBossEvents();", "剧情 owner 销毁时退订击败事件")
    require(errors, body_of(code["service"], "internal bool RecordNote(string id, out string message)"),
            "!SkyIslandBossRules.IsBossNote(id)", "首杀手记 id 要进 RecordNote 白名单")
    require(errors, code["module"], "SkyIslandBossForge.ResetStaticCaches();", "模块销毁兜底清掉击败事件订阅")

    # ---- 6. 物品登记 ----
    all_type_ids = code["item_rules"].split("internal static readonly int[] AllTypeIds", 1)[-1].split("};", 1)[0]
    gear_type_ids = code["rules"].split("internal static readonly int[] AllGearTypeIds", 1)[-1].split("};", 1)[0]
    registry_plan = code["registry"]
    blacklist_json = json.loads(raw["blacklist_json"]).get("itemIds", [])
    for name, value in GEAR.items():
        if not re.search(r"public const int %s = %d;" % (name, value), code["ids"]):
            errors.append("ConfigItemIds 缺 %s = %d" % (name, value))
        if "BossRushItemIds." + name in all_type_ids:
            errors.append("专属装备 %s 混进了 SkyIslandItemRules.AllTypeIds（那是 500068 起连续的岛上克隆物品，注册走另一条管线）" % name)
        if "BossRushItemIds." + name not in gear_type_ids:
            errors.append("SkyIslandBossRules.AllGearTypeIds 缺 " + name)
        if not re.search(r"case BossRushItemIds\.%s: return [1-9]\d*;" % name, code["item_rules"]):
            errors.append("SkyIslandItemRules.ValueOf 缺 %s 的正价值" % name)
        if "BossRushItemIds." + name not in code["blacklist"] or value not in blacklist_json:
            errors.append("掉落黑名单（代码 / JSON）缺 %s：专属装备只该从 Boss 身上来" % name)
    # 动态注册表与装配期入口都在 ModBehaviour partial 预算里：注册表只许一行（按专属装备表登记 bundle），
    # 克隆占位与物品配置器挂在预算之外的同类 owner 上（套装占位注册器、岛上物品配置器）。
    require(errors, registry_plan, 'Add(plans, EquipmentOnly("skyisland_boss_gear"), SkyIslandBossRules.AllGearTypeIds);',
            "动态注册表要按专属装备表登记 bundle（重启后背包里的它才不会退化成 FallbackItem）")
    hooks = {rel: clean_source((ROOT / rel).read_text(encoding="utf-8-sig", errors="ignore"))
             for rel in ("Integration/Bonus/SetBonusPlaceholderRegistry.cs", "Integration/SkyIsland/SkyIslandItems.cs")}
    require(errors, body_of(hooks["Integration/Bonus/SetBonusPlaceholderRegistry.cs"], "public static void EnsureAllRegistered()"),
            "SkyIslandBossGearConfig.EnsureAllRegistered();", "装配期补齐克隆占位（bundle 缺失时物品不退化）")
    require(errors, body_of(hooks["Integration/SkyIsland/SkyIslandItems.cs"], "public static void RegisterConfigurators()"),
            "SkyIslandBossGearConfig.RegisterConfigurators();", "专属装备的物品配置器与岛上物品同一时点登记")
    require(errors, code["factory"], "SkyIslandBossGearConfig.TryConfigure(itemPrefab, baseName);", "装备工厂配置链要配置专属装备")
    require(errors, code["equipment_loc"], "SkyIslandBossGearConfig.InjectLocalization();", "专属装备本地化要挂进装备本地化总入口")
    configure = body_of(code["gear_config"], "private static void Configure(Item item, SkyIslandBossGearSpec spec, bool bindLoadedModel)")
    for token in ("item.Quality = spec.Quality;", "item.Value = SkyIslandItemRules.ValueOf(spec.TypeId);",
                  "EquipmentHelper.EnsureModifierOnItem(item, spec.StatKey, ModifierType.Add, spec.StatValue, true);",
                  "EquipmentHelper.AddRepairableTag(item);"):
        require(errors, configure, token, "专属装备的品质、价值、属性与可维修都要在配置器里显式写（不重蹈龙系「只在 bundle 里」）")
    return errors


def load():
    raw = {}
    for key, rel in PATHS.items():
        path = ROOT / rel
        if not path.exists():
            return None, "缺少文件：" + rel
        raw[key] = path.read_text(encoding="utf-8-sig", errors="ignore")
    return raw, None


def reverse_probes(raw):
    """在内存里把关键接线改坏，确认守卫会红。锚点失效也算红（写法变了要同步这里）。"""
    probes = (
        ("encounters", "if (SkyIslandBossForge.TryApply(created, encounter.Id, index, BossContext())) return;", "", "拆掉身份层分派"),
        ("loot", "boss.BeforeCharacterSpawnLootOnDead += OnBeforeLoot;", "", "掉落不挂建箱前事件"),
        ("loot", "boss.BeforeCharacterSpawnLootOnDead -= OnBeforeLoot;", "", "掉落不退订"),
        ("loot", "resolved = true;", "", "掉落结算不上闩"),
        ("forge", "if (prefab == null) { reason = \"prefab_missing:\" + piece.TypeId; return false; }", "", "配装不先问 prefab"),
        ("forge", "slots[i].ForceInvokeSlotContentChangedEvent();", "slots[i].ForceInvokeSlotContentChangedEvent(); created.GetComponent<CharacterEquipmentController>().SetItem(null);",
         "配装后又调 SetItem"),
        ("loot", "Resolve(random.NextDouble());", "Resolve(UnityEngine.Random.value);", "掉落改用 UnityEngine.Random"),
        ("loot", "#if BOSSRUSH_DEV\n", "", "不走死亡的演练结算漏出 Dev 构建"),
        ("world_bosses", "if (bossEventsAttached) return;", "", "击败事件订阅不幂等"),
        ("content", "SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Lord)", "SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Elite)", "内置表 G 带队改回精英"),
        ("item_rules", "case BossRushItemIds.SkyIslandStargazerLensHelm: return 9000;", "", "价值表漏一件"),
        ("rules", "// 【五栏】核心招式：有视线时远程标记玩家，锁定后在标记处落两发星火",
         "// 【五栏】核心招式：立星炉供能桩给自己加护甲，星焰落点封左右走位，放完两招星炉过热", "两位 Boss 的核心招式写成同一句"),
        ("rules", "            // 【五栏】克制：躲进掩体断开视线就打断标记；贴到 8 米内它不再标记；锁定后 0.6 秒内出圈\n", "", "观星手的五栏少一栏"),
        ("registry", 'Add(plans, EquipmentOnly("skyisland_boss_gear"), SkyIslandBossRules.AllGearTypeIds);', "", "动态注册表漏登记专属装备"),
    )
    errors = []
    for key, before, after, label in probes:
        text = raw[key].replace("\r\n", "\n")
        if before not in text:
            errors.append("反向检查锚点失效（%s）：%s" % (label, before[:60]))
            continue
        altered = dict(raw)
        altered[key] = text.replace(before, after, 1)
        if not check(altered):
            errors.append("反向检查失效：%s 之后守卫仍然全绿" % label)
    return errors


def main():
    raw, missing = load()
    if raw is None:
        print("FAIL SkyIslandBossEcologyGuard: " + missing)
        return 1
    errors = check(raw)
    if not errors:
        errors = reverse_probes(raw)
    if errors:
        print("FAIL SkyIslandBossEcologyGuard")
        for error in errors:
            print("  - " + error)
        return 1
    print("PASS SkyIslandBossEcologyGuard")
    return 0


if __name__ == "__main__":
    sys.exit(main())
