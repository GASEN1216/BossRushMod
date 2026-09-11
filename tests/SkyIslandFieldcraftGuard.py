"""天空岛内容批次三（采集点 / 群岛材料 / 合成台 / 局内耗材 / 夜风）的接线守卫。

行为由执行回归 `tests/fixtures/SkyIslandStory` 验证（采集点表、产出随档次单调、同种子同产出、夜里只改附带、
一趟期望产出与价值、配方经济倍率、每件新物品都有获取途径、夜风积满时长与滞回、英文无中文）；落点由
`SkyIslandInteractionCompetitionPropertyTest` 按真实几何复算（30 处采集点与全部静态交互体两两不抢、离撤离环足够远）；
面板由 `SkyIslandStoryPanelLayoutPropertyTest` 按最坏文案复算。这里只钉**接线与不变式**，期望值尽量从数据源推导：

0. 登记：编译清单、本地化守卫、隔离回归链接、F3 只读守卫的写入口名单、静态引用 owner、stat key 常量。
1. 纯规则无 Unity 依赖；采集点锚点都在作者布局里；配方的输入输出都是登记过的天空岛物品。
2. TypeID：常量值、`ItemRules` 名称与价值、克隆注册表、掉落黑名单（代码 + JSON + 黑名单守卫映射）、AGENTS 台账。
3. 物品配置：材料没有使用行为、耗材挂 `SkyIslandFieldcraftUsage` 并写入 buff、价值只取 `ItemRules.ValueOf`、图标与生图清单一致。
4. 采集：落点复用 `TryFindCratePosition`、按距离门控一次建一个、读条时长写进官方字段并读回核对、先问 prefab、
   产出进背包放不下落脚边（**不寄基地仓库**）。
5. 合成：先点清材料、先造成品再扣材料、整堆扣掉的 DestroyTree、面板过战斗门、做成才重开、居民与兜底装置各挂一份。
6. 耗材与夜风：计时走 `Time.time`、离岛 `CanBeUsed` 为 false、护符不叠加、只动耐力恢复与饥饿（不碰跑速、不扣血、不免费回满）、
   Dispose 摘除全部 Modifier 与灯。
7. `SkyIslandSession.cs` 主文件不加接线（仍 ≤ 1200 行、不引用批次三类型）；owner 挂在 WorldStory 上并随它销毁。

反向验证：把任一条接线改回错误写法（例如产出寄回仓库、先扣材料再造成品、耗材计时改 unscaledTime、合成面板去掉战斗门），本守卫必红。
"""
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]

NEW_IDS = {
    "SkyIslandCloudmossFiber": 500073, "SkyIslandGreenearSheaf": 500074, "SkyIslandDriftwood": 500075,
    "SkyIslandBrassScrap": 500076, "SkyIslandWindcrystalShard": 500077, "SkyIslandStardust": 500078,
    "SkyIslandQinglanWindcrystal": 500079, "SkyIslandWindLantern": 500080, "SkyIslandWindwardIncense": 500081,
    "SkyIslandQinglanCharm": 500082,
}
MATERIALS = ("SkyIslandCloudmossFiber", "SkyIslandGreenearSheaf", "SkyIslandDriftwood", "SkyIslandBrassScrap",
             "SkyIslandWindcrystalShard", "SkyIslandStardust", "SkyIslandQinglanWindcrystal")
CONSUMABLES = {"SkyIslandWindLantern": "Lantern", "SkyIslandWindwardIncense": "Incense", "SkyIslandQinglanCharm": "Charm"}
NEW_SOURCES = ("DebugAndTools\\SkyIsland\\SkyIslandFieldcraftRules.cs", "DebugAndTools\\SkyIsland\\SkyIslandGathering.cs",
               "DebugAndTools\\SkyIsland\\SkyIslandFieldcraft.cs", "Integration\\SkyIsland\\SkyIslandFieldcraftUsage.cs")


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8-sig")


def squash(text):
    """规范空白：连续空白压成一个空格，括号、点号、冒号、逗号两侧的空白去掉。"""
    text = re.sub(r"\s+", " ", text or "")
    return re.sub(r"\s*([(){};:,.\[\]])\s*", r"\1", text)


def body_of(source, signature):
    start = source.find(signature)
    if start < 0:
        return None
    brace = source.find("{", start + len(signature))
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def main():
    errors = []

    def need_body(source, signature, label):
        body = body_of(source, signature)
        if body is None:
            errors.append("找不到 %s（%s）" % (label, signature))
            return ""
        return squash(body)

    def require(text, token, why):
        if text and squash(token) not in text:
            errors.append("%s（缺 %s）" % (why, token))

    def forbid(text, token, why):
        if text and squash(token) in text:
            errors.append("%s（不应出现 %s）" % (why, token))

    def ordered(text, tokens, why):
        if not text:
            return
        position = -1
        for token in tokens:
            found = text.find(squash(token), position + 1)
            if found < 0:
                errors.append("%s（缺或顺序不对：%s）" % (why, token))
                return
            position = found

    rules_raw = read("DebugAndTools/SkyIsland/SkyIslandFieldcraftRules.cs")
    rules = clean_source(rules_raw)
    gathering = clean_source(read("DebugAndTools/SkyIsland/SkyIslandGathering.cs"))
    fieldcraft = clean_source(read("DebugAndTools/SkyIsland/SkyIslandFieldcraft.cs"))
    usage = clean_source(read("Integration/SkyIsland/SkyIslandFieldcraftUsage.cs"))
    world = clean_source(read("DebugAndTools/SkyIsland/SkyIslandWorldStory.cs"))
    items = clean_source(read("Integration/SkyIsland/SkyIslandItems.cs"))
    item_rules = clean_source(read("DebugAndTools/SkyIsland/SkyIslandItemRules.cs"))
    session_raw = read("DebugAndTools/SkyIsland/SkyIslandSession.cs")
    layout = json.loads(read("ArtSource/SkyIsland/layout.json"))
    author_markers = {m["id"] for m in layout["markers"]}

    # ---- 0. 登记 ----
    bat = read("compile_official.bat")
    for rel in NEW_SOURCES:
        if "echo(" + rel not in bat:
            errors.append("编译清单缺 " + rel + "（新增 .cs 不登记就静默不参与编译）")
    l10n_guard = read("tests/SkyIslandLocalizationGuard.py")
    for name in ("SkyIslandFieldcraftRules.cs", "SkyIslandGathering.cs", "SkyIslandFieldcraft.cs"):
        if '"%s"' % name not in l10n_guard:
            errors.append("SkyIslandLocalizationGuard.FILES 缺 " + name)
    if "DebugAndTools/SkyIsland/SkyIslandFieldcraftRules.cs" not in read("tests/fixtures/SkyIslandStory/Regression.csproj"):
        errors.append("隔离回归没有链接 SkyIslandFieldcraftRules.cs（纯规则没人执行）")
    suite_guard = read("tests/SkyIslandValidationSuiteGuard.py")
    for member in ("Harvest", "Craft", "UseConsumable", "OpenCrafting", "CraftChoice"):
        if '("%s",' % member not in suite_guard:
            errors.append("F3 只读守卫的写入口名单缺 " + member)
    if "SkyIslandFieldcraft.ResetStaticCaches();" not in clean_source(read("DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs")):
        errors.append("SkyIslandFieldcraft 的静态引用没有模块销毁 owner")
    tuning = clean_source(read("ZombieMode/ZombieModeTuning.cs"))
    for constant, literal in (("StaminaRecoverRate", "StaminaRecoverRate"), ("EnergyCost", "EnergyCost")):
        if 'public const string %s = "%s";' % (constant, literal) not in tuning:
            errors.append("ZombieModeStatNames 缺 %s（StatKeyExistenceGuard 靠它核对官方 stat 名）" % constant)
    if "batch_three_strings()" not in read("tests/SkyIslandStoryPanelLayoutPropertyTest.py"):
        errors.append("面板布局属性测试没有纳入批次三的合成台文案")
    if "GATHER_NODES" not in read("tests/SkyIslandInteractionCompetitionPropertyTest.py"):
        errors.append("交互竞争属性测试没有纳入 30 处采集点")

    # ---- 1. 纯规则：无 Unity 依赖；采集点与配方表 ----
    usings = re.findall(r"^using\s+([\w.]+)\s*;", rules_raw, re.M)
    if sorted(usings) != ["System", "System.Collections.Generic"]:
        errors.append("SkyIslandFieldcraftRules 必须只依赖 System（隔离回归直接链接），实际 using：%r" % usings)
    nodes = re.findall(r'Node\("([A-Za-z0-9]+)",\s*"([A-Za-z0-9_]+)",\s*([0-9.]+)f,\s*([0-9.]+)f,\s*'
                       r'SkyIslandGatherKind\.(\w+),\s*SkyIslandLootTier\.(\w+),\s*"(\w+)"\)', rules)
    if len(nodes) != 30 or len({n[0] for n in nodes}) != 30:
        errors.append("采集点必须是 30 处、id 不重复，实际 %d" % len(nodes))
    for node_id, marker, _bearing, _distance, kind, tier, region in nodes:
        if marker not in author_markers:
            errors.append("采集点 %s 的锚点 %s 不在作者布局里（运行时 Find 返回 null，这一处静默消失）" % (node_id, marker))
        if kind not in ("Grass", "Driftwood", "Moss", "Ore", "Crystal") or tier not in ("Supply", "Voyage", "Starworks"):
            errors.append("采集点 %s 的外观或档次不在枚举里" % node_id)
        if region not in ("A", "B", "C", "D", "E", "F", "G", "H", "S1", "S2", "S3", "S4"):
            errors.append("采集点 %s 的区域 %s 不是 12 个区域之一" % (node_id, region))
    recipes = re.findall(r'Recipe\("(\w+)",\s*SkyIslandCraftStation\.(\w+),\s*BossRushItemIds\.(\w+),\s*(\d+),'
                         r'((?:\s*In\(BossRushItemIds\.\w+,\s*\d+\),?)+)\)', rules)
    all_names = set(re.findall(r"BossRushItemIds\.(\w+)", item_rules.split("internal static readonly int[] AllTypeIds", 1)[1]
                               .split("};", 1)[0]))
    # 批次三 8 条 + 内容批次四 3 条（云苔纱笠、风晶灭蚊灯、药烟蒲扇，逐条接线另见 SkyIslandMosquitoGuard）。
    if len(recipes) != 11:
        errors.append("配方必须是 11 条（批次三 8 + 批次四 3），实际 %d" % len(recipes))
    for recipe_id, station, output, _count, inputs in recipes:
        if output not in all_names:
            errors.append("配方 %s 的成品 %s 不是登记过的天空岛物品" % (recipe_id, output))
        for input_name in re.findall(r"In\(BossRushItemIds\.(\w+),", inputs):
            if input_name not in MATERIALS:
                errors.append("配方 %s 的材料 %s 不是群岛材料" % (recipe_id, input_name))
    for station in ("Dock", "Stove", "Mortar"):
        count = sum(1 for r in recipes if r[1] == station)
        # 批次四给渡口工台加了灭蚊灯（5 条）。合成面板上只有配方按钮，面板布局属性测试按最坏 6 条选项复算。
        if not 2 <= count <= 5:
            errors.append("合成台 %s 的配方数 %d 不在 2–5（面板按最坏 6 条选项复算）" % (station, count))

    # ---- 2. TypeID 与台账 ----
    ids_src = clean_source(read("Config/ConfigItemIds.cs"))
    registry = clean_source(read("Integration/BossRushDynamicItemRegistry.cs"))
    # 天空岛那一条克隆注册计划：从兜底加载器开始，到下一条 Add(plans 为止（兜底委托自己就带着 `);`，不能按它截）。
    plan = registry.split("SkyIslandItems.EnsureRuntimeRegistration", 1)[-1].split("Add(plans", 1)[0] \
        if "SkyIslandItems.EnsureRuntimeRegistration" in registry else ""
    blacklist = clean_source(read("Config/LootBlacklistRegistry.cs"))
    blacklist_json = json.loads(read("Assets/Data/LootBlacklist.json"))["itemIds"]
    blacklist_guard = read("tests/LootBlacklistDataRegistryGuard.py")
    name_cn = item_rules.split("internal static string NameCn(", 1)[1].split("internal static string NameEn(", 1)[0]
    name_en = item_rules.split("internal static string NameEn(", 1)[1].split("internal static string Name(", 1)[0]
    value_of = item_rules.split("internal static int ValueOf(", 1)[1].split("\n        }", 1)[0] \
        if "internal static int ValueOf(" in item_rules else ""
    for name, value in NEW_IDS.items():
        if not re.search(r"public const int %s = %d;" % (name, value), ids_src):
            errors.append("ConfigItemIds 缺 %s = %d" % (name, value))
        if "BossRushItemIds." + name not in plan:
            errors.append("克隆注册表没有登记 %s：重启后背包里的它会退化成官方 FallbackItem" % name)
        if "BossRushItemIds." + name not in blacklist:
            errors.append("掉落黑名单（代码兜底）缺 " + name)
        if value not in blacklist_json:
            errors.append("掉落黑名单（JSON）缺 %d" % value)
        if '"BossRushItemIds.%s": %d' % (name, value) not in blacklist_guard:
            errors.append("LootBlacklistDataRegistryGuard 的常量映射缺 " + name)
        if name not in all_names:
            errors.append("SkyIslandItemRules.AllTypeIds 缺 " + name)
        if "case BossRushItemIds.%s: return" % name not in name_cn or "case BossRushItemIds.%s: return" % name not in name_en:
            errors.append("SkyIslandItemRules 的中英物品名缺 " + name)
        if not re.search(r"case BossRushItemIds\.%s: return [1-9]\d*;" % name, value_of):
            errors.append("SkyIslandItemRules.ValueOf 缺 %s 的正价值（NPC 商店会标价 0）" % name)
    agents = read("AGENTS.md")
    # 台账要覆盖批次三的全部 TypeID，且「下一可用」紧接登记上限（后续批次往后接，这里不钉死上限）。
    ledger = re.search(r"当前登记范围：`500001-(\d+)`", agents)
    upcoming = re.search(r"下一可用：`(\d+)`", agents)
    if not ledger or not upcoming or int(ledger.group(1)) < max(NEW_IDS.values()) or int(upcoming.group(1)) != int(ledger.group(1)) + 1:
        errors.append("AGENTS.md §4.3 的 TypeID 台账没有覆盖到 500082，或「下一可用」没有紧接登记上限")
    # 另两份台账与 AGENTS 同一口径：覆盖批次三、登记上限一致、「下一可用」紧接上限（内容批次四起往后接，不钉死 500082 / 500083）。
    contracts = ROOT / "docs/contracts.md"
    if contracts.exists():  # docs/ 是 local-only：干净签出上没有就不查
        registered = re.search(r"已登记范围：`500001-(\d+)`", contracts.read_text(encoding="utf-8-sig"))
        if not registered or int(registered.group(1)) < max(NEW_IDS.values()) or (ledger and registered.group(1) != ledger.group(1)):
            errors.append("docs/contracts.md 的 TypeID 台账没有覆盖到 500082，或与 AGENTS.md §4.3 的登记上限不一致")
    id_table = ROOT / "docs/Bossrush使用物品ID表.md"
    if id_table.exists():
        table_text = id_table.read_text(encoding="utf-8-sig")
        next_free = re.search(r"下一可用 ID：\*\*(\d+)\*\*", table_text)
        if "500073-500082" not in table_text or not next_free or not ledger or int(next_free.group(1)) != int(ledger.group(1)) + 1:
            errors.append("docs/Bossrush使用物品ID表.md 缺批次三那一段（500073-500082），或「下一可用 ID」没有紧接登记上限")

    # ---- 3. 物品配置 ----
    for name in MATERIALS:
        if not re.search(r"Material\(BossRushItemIds\.%s," % name, items):
            errors.append("SkyIslandItems 定义表缺材料 " + name)
    for name, buff in CONSUMABLES.items():
        if not re.search(r"Consumable\(BossRushItemIds\.%s,\s*SkyIslandFieldBuff\.%s," % (name, buff), items):
            errors.append("SkyIslandItems 定义表里 %s 必须是耗材且效果为 %s" % (name, buff))
    build = squash(body_of(items, "private static Definition[] BuildDefinitions()") or "")
    string_arg = r'"(?:[^"\\]|\\.)*"'
    make_rows = re.findall(r'Make\(BossRushItemIds\.(\w+),Kind\.\w+,' + string_arg + ',' + string_arg + ',' + string_arg +
                           r',"sky_island_[a-z_]+",([^,]+),', build)
    if len(make_rows) != 5:
        errors.append("批次二五件物品的 Make(...) 行没解析全：%d（正则与源码失步）" % len(make_rows))
    for name, literal in make_rows:
        if literal != "SkyIslandItemRules.ValueOf(BossRushItemIds.%s)" % name:
            errors.append("物品 %s 的价值必须取 SkyIslandItemRules.ValueOf（不在定义表里另写数字）：%s" % (name, literal))
    for helper in ("private static Definition Material(", "private static Definition Consumable("):
        require(need_body(items, helper, "物品定义辅助"), "SkyIslandItemRules.ValueOf(typeId)", "材料与耗材的价值取 ItemRules.ValueOf")
    configure = need_body(items, "private static void ConfigureItem(int typeId, Item item)", "物品配置")
    consumable_case = configure.split("case Kind.Consumable:", 1)[1].split("break;", 1)[0] if "case Kind.Consumable:" in configure else ""
    if not consumable_case:
        errors.append("物品配置缺 Kind.Consumable 分支")
    for token in ("Component<SkyIslandFieldcraftUsage>(item)", "fieldUse.buff = (int)def.Buff;", "AttachUsage(item, def.UseTime, fieldUse);"):
        require(consumable_case, token, "耗材要挂群岛耗材使用行为并写入效果种类")
    if "case Kind.Material:" in configure:
        errors.append("材料不应有使用行为分支（克隆源的用法已被 ClearInheritedUsage 清掉）")
    icon_names = set(re.findall(r'"(sky_island_[a-z_]+)"', items))
    generator = set(re.findall(r'\("(sky_island_[a-z_]+)",', read("tools/gen_sky_island_item_icons.py")))
    if len(icon_names) != 18 or icon_names != generator:
        errors.append("物品图标名与生图脚本清单不一致：%r / %r" % (sorted(icon_names - generator), sorted(generator - icon_names)))
    can_use = need_body(usage, "public override bool CanBeUsed(Item item, object user)", "耗材 CanBeUsed")
    require(can_use, "SkyIslandFieldcraft.Current", "耗材要看本趟 owner：离岛时按钮置灰，不白吃一件")
    require(can_use, "owner.CanUse(Buff)", "耗材可用性交给 owner（护符不叠加）")
    require(need_body(usage, "protected override void OnUse(Item item, object user)", "耗材 OnUse"), "owner.UseConsumable(Buff)",
            "耗材效果交给 owner 执行")
    if "FindObjectOfType" in usage or "Update(" in usage:
        errors.append("耗材不得全场景查找 owner，也不得挂每帧路径")

    # ---- 4. 采集 ----
    ctor = need_body(gathering, "internal SkyIslandGathering(Transform worldRoot, int groundMask, Func<SkyIslandGatherNode, bool> onHarvest)",
                     "采集点落点")
    require(ctor, "SkyIslandRewardCrate.TryFindCratePosition(root, marker.position, nodes[i].Bearing,", "采集点落点复用纪念物 / 信鸽的放置算法")
    tick = need_body(gathering, "internal void Tick(Vector3 origin, bool night)", "采集点推进")
    require(tick, "SkyIslandFieldcraftRules.ActivationRange", "采集交互体按距离门控（AGENTS 4.12）")
    require(tick, "if (best != null) Build(best, night);", "一次推进最多建一个采集点")
    build_spot = need_body(gathering, "private void Build(Spot spot, bool night)", "采集点建造")
    require(build_spot, "trigger.size = new Vector3(SkyIslandFieldcraftRules.NodeTriggerSize, 2f, SkyIslandFieldcraftRules.NodeTriggerSize);",
            "触发盒尺寸必须取规则常量（交互竞争属性测试按它算半宽）")
    require(build_spot, "SkyIslandProximityLabel.Attach(sign", "采集点的字走近才浮现")
    bind = need_body(gathering, "internal void Bind(string title, float seconds, Action action)", "采集读条")
    ordered(bind, ['ModeFItemConfigHelper.SetHiddenMember(this, "interactTime", seconds);', "Mathf.Abs(InteractTime - seconds) > 0.01f",
                   "Debug.LogWarning("], "读条时长写进官方私有字段后必须读回核对（改名时有声）")
    ordered(need_body(gathering, "private void OnGathered(Spot spot)", "采集完成"),
            ["if (!harvest(spot.Node)) return;", "spot.Harvested = true;", "UnityEngine.Object.Destroy(spot.Root);"],
            "先发出产出、再标记采过、最后收掉交互体")
    if 'InteractionGroupLabel { get { return "[SkyIslandGather]"; } }' not in gathering:
        errors.append("采集点必须走官方交互组（InteractionGroupLabel）")
    give = need_body(fieldcraft, "private static int Give(int typeId, int count)", "产出发放")
    ordered(give, ["ItemAssetsCollection.GetPrefab(typeId) == null", "ItemAssetsCollection.InstantiateSync(typeId);",
                   "item.TypeID != typeId", "ItemUtilities.SendToPlayer(item, false, false);"],
            "产出先问 prefab，再放进背包、放不下落在脚边")
    flat_fieldcraft = squash(fieldcraft)
    for token in ("SendToPlayerStorage", "SendToPlayer(item)", "SendToPlayer(output)", "SendToPlayer(item, false, true)",
                  "SendToPlayer(output, false, true)"):
        # 两边都规范空白再比：只规范一边的话 `SendToPlayer(item, false, true)` 永远匹配不上，这条禁令形同虚设。
        if squash(token) in flat_fieldcraft:
            errors.append("出击里得到的产出与成品不得寄回基地仓库（免死金牌）：" + token)
    require(need_body(fieldcraft, "private bool Harvest(SkyIslandGatherNode node)", "采集产出"),
            'SkyIslandLootTables.CreateStream(seed, "gather:" + node.Id)', "产出走本趟种子 + 采集点 id 的独立随机流")

    # ---- 5. 合成 ----
    craft = need_body(fieldcraft, "internal bool Craft(SkyIslandRecipe recipe, out string message)", "合成")
    ordered(craft, ["SkyIslandFieldcraftRules.Missing(recipe, CountInPack);", "if (missing.Count > 0)",
                    "ItemAssetsCollection.GetPrefab(recipe.OutputTypeId) == null", "ConsumeFromPack(",
                    "ItemUtilities.SendToPlayer(output, false, false);"],
            "合成：先点清材料、先造出成品、再扣材料、最后放进背包")
    ordered(need_body(fieldcraft, "private static bool ConsumeFromPack(int typeId, int count)", "扣材料"),
            ["inventory.RemoveItem(item);", "item.DestroyTree();"], "整堆扣掉的材料要 DestroyTree，不留孤儿物品")
    require(need_body(fieldcraft, "internal int CountInPack(int typeId)", "背包计数"), "ItemFactory.GetItemCountInInventory(typeId)",
            "背包计数复用 ItemFactory（只数背包顶层，不数基地仓库）")
    open_crafting = need_body(world, "private void OpenCrafting(SkyIslandCraftStation station)", "合成面板")
    if not open_crafting.strip().startswith(squash("if (BlockedByCombat() || fieldcraft == null) return;")):
        errors.append("合成面板第一句必须过战斗门（面板会把 timeScale 压到 0，不能当战斗中的暂停键）")
    for token in ("reopen = delegate { OpenCrafting(station); };", "fieldcraft.Craft(recipe, out message)", "Refreshed(crafted, message)"):
        require(open_crafting, token, "做成才重开面板刷新件数")
    read_point = need_body(world, "internal void ReadPoint(string key, Action recorded)", "ReadPoint")
    talk = need_body(world, "internal void Talk(string id, Transform speaker)", "Talk")

    def case_slice(text, label):
        marker = squash('case "%s":' % label)
        return text.split(marker, 1)[1].split("break;", 1)[0] if marker in text else ""

    def talk_slice(resident):
        marker = squash('if (id == "%s")' % resident)
        return talk.split(marker, 1)[1].split("else if", 1)[0] if marker in talk else ""

    for scope, token, why in ((case_slice(read_point, "Search_A"), "CraftChoice(choices, SkyIslandCraftStation.Dock);", "码头装置兜底渡口工台"),
                              (case_slice(read_point, "Search_C"), "CraftChoice(choices, SkyIslandCraftStation.Stove);", "菜畦兜底灶台"),
                              (case_slice(read_point, "Search_D_02"), "CraftChoice(choices, SkyIslandCraftStation.Mortar);", "悬根林见闻点兜底药臼"),
                              (talk_slice("sky_fuzhou"), "CraftChoice(choices, SkyIslandCraftStation.Dock);", "浮舟开渡口工台"),
                              (talk_slice("sky_qinghe"), "CraftChoice(choices, SkyIslandCraftStation.Stove);", "晴禾开灶台"),
                              (talk_slice("sky_miantai"), "CraftChoice(choices, SkyIslandCraftStation.Mortar);", "眠苔开药臼")):
        if squash(token) not in scope:
            errors.append("合成台接线缺：%s（%s）" % (why, token))

    # ---- 6. 耗材与夜风 ----
    for label, source in (("SkyIslandFieldcraft.cs", fieldcraft), ("SkyIslandGathering.cs", gathering)):
        if "unscaledTime" in source or "unscaledDeltaTime" in source:
            errors.append("%s 的玩法计时必须走游戏时间（暂停与剧情面板时不许在背后燃尽或积满寒意）" % label)
    field_tick = need_body(fieldcraft, "internal void Tick()", "批次三推进")
    ordered(field_tick, ["if (disposed || !session.IsReady) return;", "float now = Time.time;", "gathering.Tick(", "TickBuffs(now);",
                         "TickWind("], "推进：会话有效才走、游戏时间节流、再推进采集 / 耗材 / 夜风")
    require(need_body(fieldcraft, "internal bool CanUse(SkyIslandFieldBuff buff)", "耗材可用性"),
            "return buff != SkyIslandFieldBuff.Charm || !charmWorn;", "晴岚护符不叠加")
    stats = set(re.findall(r"ZombieModeStatNames\.(\w+)", fieldcraft))
    if not stats or not stats <= {"StaminaRecoverRate", "EnergyCost", "MaxHealth"}:
        errors.append("批次三只许动耐力恢复、饥饿与护符的生命上限，实际挂了：%r" % sorted(stats))
    for token, why in (("RunSpeed", "夜风不许减跑速（噬风第一圈靠跑出去）"), ("WalkSpeed", "夜风不许减跑速"),
                       ("Moveability", "夜风不许减跑速"), ("SetHealth(player.Health.MaxHealth)", "护符不许免费回满（苔药按缺失比例收钱）"),
                       ("DamageInfo", "夜风不许掉血"), (".Hurt(", "夜风不许掉血")):
        if token in fieldcraft:
            errors.append(why + "（出现了 %s）" % token)
    wind = need_body(fieldcraft, "private void TickWind(CharacterMainControl player, bool night, float elapsed)", "夜风")
    for token in ("SkyIslandStoryService.GroundRegionOf(colliderName)", "SkyIslandFieldcraftRules.WindLevel(",
                  "SkyIslandFieldcraftRules.StepExposure(", "SkyIslandFieldcraftRules.NextChilled("):
        require(wind, token, "夜风的风力、寒意与滞回只读纯规则（隔离回归执行的就是这几条）")
    dispose = need_body(fieldcraft, "public void Dispose()", "批次三销毁")
    for token in ("if (Current == this) Current = null;", "RuntimeStatModifierTracker.RemoveAll(chillRecords, ModifierContext);",
                  "RuntimeStatModifierTracker.RemoveAll(incenseRecords, ModifierContext);",
                  "RuntimeStatModifierTracker.RemoveAll(charmRecords, ModifierContext);", "DestroyLantern();", "gathering.Dispose();"):
        require(dispose, token, "离岛时摘掉本趟全部增益、灯与采集点")
    ctor_field = need_body(fieldcraft, "internal SkyIslandFieldcraft(SkyIslandSession owner, SkyIslandStoryService storyService, Transform worldRoot)",
                           "批次三装配")
    ordered(ctor_field, ["gathering = new SkyIslandGathering(", "Current = this;"], "装配完成后才对耗材公开 owner")

    # ---- 7. 会话主文件不动；owner 挂在 WorldStory 上 ----
    if len(session_raw.splitlines()) > 1200:
        errors.append("SkyIslandSession.cs 超过 1200 行预算")
    session = clean_source(session_raw)
    for token in ("Fieldcraft", "SkyIslandGathering", "SkyIslandRecipe"):
        if token in session:
            errors.append("批次三的接线不得进 SkyIslandSession.cs 主文件（出现 %s）" % token)
    world_tick = need_body(world, "internal void Tick()", "WorldStory.Tick")
    ordered(world_tick, ["TickPigeon();", "TickFieldcraft();", "if (displayedFlags == story.Current.flags) return;"],
            "批次三要在剧情位早退之前每帧推进")
    tick_fieldcraft = need_body(world, "private void TickFieldcraft()", "批次三推进入口")
    ordered(tick_fieldcraft, ["if (fieldcraftFailed || !session.IsReady) return;", "new SkyIslandFieldcraft(session, story, root.transform)",
                              "fieldcraftFailed = true;", "fieldcraft.Tick();"], "会话就绪后才装配，装配失败只放弃一次、不拖垮剧情")
    require(need_body(world, "public void Dispose()", "WorldStory.Dispose"), "fieldcraft.Dispose();", "批次三 owner 随剧情 owner 销毁")

    print("SkyIslandFieldcraftGuard: " + ("FAIL\n  - " + "\n  - ".join(errors) if errors else
                                           "PASS (采集点 30 / 配方 8 / 物品 10 / 耗材 3 与夜风接线)"))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
