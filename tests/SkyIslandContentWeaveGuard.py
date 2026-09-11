"""天空岛内容串联的接线守卫：批次二、三的每件东西都要有岛上的用处，并且彼此串得起来。

owner 的要求是「不要为了新增而新增」。这份守卫把它变成机器可查的不变式，期望值尽量从数据源推导：

0. 登记：编译清单、本地化守卫、隔离回归链接、F3 只读守卫的写入口名单；岛上的灯是纯规则（只依赖 System）。
1. **没有只为卖钱的物品**：十五件天空岛物品按形态逐件找「岛上的用处」——
   材料必须被某条配方或某盏风晶灯吃掉；耗材的效果必须在局内 owner 里真的有分支；纪念品必须有「带在身上」的效果接线；
   罗盘挂罗盘行为、便当挂归航菜行为、药膏真的回血；群岛手记「群岛之物」一页逐件写了用处。
2. 岛上的灯：七盏风晶灯各挂在一处有面板的装置旁、各对应一封不同的信、各烧恰好一块晴岚风晶；三处灶火 + 七盏 = 十盏。
3. 点灯：写屏障先挡、先点清材料、**先记手记再扣材料**、再补建灯光；手记只收登记过的灯 id。
4. 剧情 ⇄ 采集 / 合成：采集产出读剧情进度、配方门槛（便当等种植记录、晴岚风晶等星灯、罗盘等第一只）在合成与面板两处都生效。
5. 风：夜风读「岛上的灯」与噬风之核，暖和分三档（灶火 / 灯 / 驱风香全挡，风灯半挡）；护符替玩家挡噬风的风暴。
6. 航徽半价走同一个定价函数、在报价之后付款之前；便当那一顿与晴禾的归航菜共用一次。
7. 物品描述里写给玩家的数字（护符减伤、航徽折扣）与规则常量一致。

行为（加成数值、门槛、灯的计数、寒意时长、手记页）由执行回归 `tests/fixtures/SkyIslandStory` 验证；
这里只钉接线。反向验证：把任一条改回「为新增而新增」的写法（例如删掉某盏灯的晴岚风晶、航徽去掉半价接线、
先扣材料再记手记、护符不再减噬风伤害、配方门槛只挂在面板不挂在合成），本守卫必红。
"""
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
STRING = r'"(?:[^"\\]|\\.)*"'


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

    lights_raw = read("DebugAndTools/SkyIsland/SkyIslandLights.cs")
    lights = clean_source(lights_raw)
    rules = clean_source(read("DebugAndTools/SkyIsland/SkyIslandFieldcraftRules.cs"))
    fieldcraft = clean_source(read("DebugAndTools/SkyIsland/SkyIslandFieldcraft.cs"))
    world = clean_source(read("DebugAndTools/SkyIsland/SkyIslandWorldStory.cs"))
    service = clean_source(read("DebugAndTools/SkyIsland/SkyIslandStoryService.cs"))
    services = clean_source(read("DebugAndTools/SkyIsland/SkyIslandServices.cs"))
    boss = clean_source(read("DebugAndTools/SkyIsland/SkyIslandStormBoss.cs"))
    journal = clean_source(read("DebugAndTools/SkyIsland/SkyIslandJournal.cs"))
    item_rules = clean_source(read("DebugAndTools/SkyIsland/SkyIslandItemRules.cs"))
    letters = clean_source(read("DebugAndTools/SkyIsland/SkyIslandLetters.cs"))
    items = clean_source(read("Integration/SkyIsland/SkyIslandItems.cs"))
    gnats = clean_source(read("DebugAndTools/SkyIsland/SkyIslandGnats.cs"))
    layout = json.loads(read("ArtSource/SkyIsland/layout.json"))
    author_markers = {m["id"] for m in layout["markers"]}

    # ---- 0. 登记 ----
    if "echo(DebugAndTools\\SkyIsland\\SkyIslandLights.cs" not in read("compile_official.bat"):
        errors.append("编译清单缺 SkyIslandLights.cs（新增 .cs 不登记就静默不参与编译）")
    if '"SkyIslandLights.cs"' not in read("tests/SkyIslandLocalizationGuard.py"):
        errors.append("SkyIslandLocalizationGuard.FILES 缺 SkyIslandLights.cs")
    if "DebugAndTools/SkyIsland/SkyIslandLights.cs" not in read("tests/fixtures/SkyIslandStory/Regression.csproj"):
        errors.append("隔离回归没有链接 SkyIslandLights.cs（岛上的灯没人执行）")
    suite_guard = read("tests/SkyIslandValidationSuiteGuard.py")
    for member in ("LightLamp", "LightChoice", "PackedMeal"):
        if '("%s",' % member not in suite_guard:
            errors.append("F3 只读守卫的写入口名单缺 " + member)
    usings = re.findall(r"^using\s+([\w.]+)\s*;", lights_raw, re.M)
    if sorted(usings) != ["System", "System.Collections.Generic", "System.Text"]:
        errors.append("SkyIslandLights 必须只依赖 System（隔离回归直接链接），实际 using：%r" % usings)

    # ---- 1. 没有只为卖钱的物品 ----
    all_names = re.findall(r"BossRushItemIds\.(\w+)",
                           item_rules.split("internal static readonly int[] AllTypeIds", 1)[1].split("};", 1)[0])
    kinds = dict(re.findall(r"Make\(BossRushItemIds\.(\w+),\s*Kind\.(\w+),", items))
    for name in re.findall(r"Material\(BossRushItemIds\.(\w+),", items):
        kinds[name] = "Material"
    buffs = dict(re.findall(r"Consumable\(BossRushItemIds\.(\w+),\s*SkyIslandFieldBuff\.(\w+),", items))
    for name in buffs:
        kinds[name] = "Consumable"
    # 内容批次四：随身装备（云苔纱笠，带在背包里生效）与岛上的工具（药烟蒲扇，使用不消耗）。
    for name in re.findall(r"Gear\(BossRushItemIds\.(\w+),", items):
        kinds[name] = "Gear"
    tools = dict(re.findall(r"Tool\(BossRushItemIds\.(\w+),\s*SkyIslandFieldBuff\.(\w+),", items))
    for name in tools:
        kinds[name] = "Tool"
    if len(all_names) != 18 or set(all_names) != set(kinds):
        errors.append("物品形态表没解析全：AllTypeIds %d 件，定义表 %d 件（正则与源码失步）" % (len(all_names), len(kinds)))
    recipes_block = rules.split("private static readonly SkyIslandRecipe[] recipes", 1)[1] \
        .split("internal static SkyIslandRecipe[] Recipes", 1)[0] if "private static readonly SkyIslandRecipe[] recipes" in rules else ""
    recipe_inputs = set(re.findall(r"In\(BossRushItemIds\.(\w+),", recipes_block))
    lamp_rows = re.findall(r'Lamp\("(Light_\w+)",\s*"(\w+)",\s*"(\w+)",\s*"(Letter_\d+)",\s*' + STRING + r',\s*' + STRING +
                           r',\s*((?:In\([^)]*\),?\s*)+)\)', lights)
    crystal_alias = re.search(r"int crystal = BossRushItemIds\.(\w+);", lights)
    lamp_inputs = set()
    for _id, _marker, _region, _letter, inputs in lamp_rows:
        for token, _count in re.findall(r"In\((\w+(?:\.\w+)?),\s*(\d+)\)", inputs):
            lamp_inputs.add(crystal_alias.group(1) if token == "crystal" and crystal_alias else token.split(".")[-1])
    use_consumable = need_body(fieldcraft, "internal bool UseConsumable(SkyIslandFieldBuff buff)", "耗材效果")
    configure = need_body(items, "private static void ConfigureItem(int typeId, Item item)", "物品配置")

    def case_body(kind):
        marker = squash("case Kind.%s:" % kind)
        return configure.split(marker, 1)[1].split("break;", 1)[0] if marker in configure else ""

    carried_sources = squash(services) + squash(fieldcraft)
    for name in all_names:
        kind = kinds.get(name)
        if kind == "Material":
            if name not in recipe_inputs and name not in lamp_inputs:
                errors.append("群岛材料 %s 没有任何配方或风晶灯要用它：只能卖钱，是为新增而新增" % name)
        elif kind == "Consumable":
            buff = buffs.get(name, "None")
            if squash("case SkyIslandFieldBuff.%s:" % buff) not in use_consumable:
                errors.append("耗材 %s 的效果 %s 在局内 owner 的 UseConsumable 里没有分支：吃了没用" % (name, buff))
        elif kind == "Keepsake":
            if not re.search(r"(?:GetItemCountInInventory|CountInPack)\(BossRushItemIds\.%s\)" % name, carried_sources):
                errors.append("纪念品 %s 没有「带在身上」的效果接线（服务或局内 owner 里数背包）：只能卖钱" % name)
        elif kind == "Compass":
            if "SkyIslandCompassUsage" not in case_body("Compass"):
                errors.append("风标罗盘没有挂罗盘使用行为")
        elif kind == "Food":
            food = case_body("Food")
            for token in ("Component<SkyIslandFieldcraftUsage>(item)", "meal.buff = (int)SkyIslandFieldBuff.Meal;",
                          "AttachUsage(item, def.UseTime, food, snack, meal);"):
                require(food, token, "归航菜便当要挂归航菜行为（菜畦开张后在岛上吃算作晴禾那一顿）")
        elif kind == "Medicine":
            require(case_body("Medicine"), "drug.healValue = def.Heal;", "星苔药膏要真的回血")
        elif kind == "Gear":
            if not re.search(r"(?:GetItemCountInInventory|CountInPack)\(BossRushItemIds\.%s\)" % name, squash(gnats)):
                errors.append("随身装备 %s 没有「带在身上」的效果接线（云蚋 owner 里数背包）：只能卖钱" % name)
        elif kind == "Tool":
            buff = tools.get(name, "None")
            if squash("case SkyIslandFieldBuff.%s:" % buff) not in use_consumable:
                errors.append("工具 %s 的效果 %s 在局内 owner 的 UseConsumable 里没有分支：用了没用" % (name, buff))
            for token in ("item.MaxDurability = NonConsumableDurability;", "Component<SkyIslandFieldcraftUsage>(item)",
                          "swing.buff = (int)def.Buff;", "AttachUsage(item, def.UseTime, swing);"):
                require(case_body("Tool"), token, "岛上的工具要挂使用行为、且不消耗（耐久同罗盘）")
        else:
            errors.append("物品 %s 的形态 %s 不认识：新形态要在这里说清楚它在岛上拿来做什么" % (name, kind))
    if squash("if (buff == SkyIslandFieldBuff.Meal)") not in use_consumable or "session.Services.PackedMeal()" not in use_consumable:
        errors.append("便当那一顿要在 UseConsumable 里交给归航菜服务（PackedMeal）")
    use_rows = set(re.findall(r"Use\(text,\s*BossRushItemIds\.(\w+),", journal))
    if use_rows != set(all_names):
        errors.append("群岛手记「群岛之物」一页必须逐件写用处：缺 %r / 多 %r" % (sorted(set(all_names) - use_rows), sorted(use_rows - set(all_names))))

    # ---- 2. 岛上的灯 ----
    point_keys = set(re.findall(r'case "(Search_[A-Z0-9_]+)": return', world.split("internal static string PointName(", 1)[1]
                                .split("private bool BlockedByCombat", 1)[0])) if "internal static string PointName(" in world else set()
    letter_ids = set(re.findall(r'Letter\("(Letter_\d+)",', letters))
    if len(lamp_rows) != 7 or len({row[0] for row in lamp_rows}) != 7:
        errors.append("风晶灯必须是 7 盏、id 不重复，实际 %d" % len(lamp_rows))
    seen_letters = set()
    for lamp_id, marker, region, letter, inputs in lamp_rows:
        if marker not in point_keys or marker not in author_markers:
            errors.append("风晶灯 %s 挂的 %s 不是有面板的装置（PointName / 作者布局里找不到）" % (lamp_id, marker))
        if marker != "Search_" + region or lamp_id != "Light_" + region:
            errors.append("风晶灯 %s 的 id、装置与区域对不上：%s / %s" % (lamp_id, marker, region))
        if letter not in letter_ids or letter in seen_letters:
            errors.append("风晶灯 %s 必须对应一封存在且不与别的灯重复的信：%s" % (lamp_id, letter))
        seen_letters.add(letter)
        crystals = re.findall(r"In\(crystal,\s*(\d+)\)", inputs)
        if crystals != ["1"] or len(re.findall(r"In\(", inputs)) < 2:
            errors.append("风晶灯 %s 必须恰好烧一块晴岚风晶、再配这处地方的东西：%s" % (lamp_id, inputs.strip()))
    if not crystal_alias or crystal_alias.group(1) != "SkyIslandQinglanWindcrystal":
        errors.append("风晶灯的灯芯必须是晴岚风晶")
    hearths = re.findall(r'"(\w+)"', lights.split("internal static readonly string[] HearthMarkers", 1)[1].split("};", 1)[0]) \
        if "internal static readonly string[] HearthMarkers" in lights else []
    if hearths != ["Search_A", "Search_C", "POI_D"] or any(h not in author_markers for h in hearths):
        errors.append("三处灶火必须是浮舟的码头、晴禾的菜畦、眠苔的站位：%r" % hearths)
    target = re.search(r"internal const int Target = (\d+);", lights)
    if not target or int(target.group(1)) != len(hearths) + len(lamp_rows):
        errors.append("岛上的灯目标数必须等于灶火 + 风晶灯（蛙鸣池的孩子数到十）")

    # ---- 3. 点灯 ----
    record_note = need_body(service, "internal bool RecordNote(string id, out string message)", "RecordNote")
    require(record_note, "SkyIslandLights.Find(id) == null", "手记只收登记过的灯 id")
    light_lamp = need_body(fieldcraft, "internal bool LightLamp(SkyIslandLight light, out string message)", "点灯")
    ordered(light_lamp, ["if (SkyIslandLights.Lit(story.Current, light.Id))", "if (!story.CanWrite)",
                         "SkyIslandFieldcraftRules.Missing(light.Inputs, CountInPack);", "story.RecordNote(light.Id, out note)",
                         "ConsumeFromPack(", "AddFire(light.Marker, LampColor);", "lightsLit = SkyIslandLights.LitCount(story.Current);"],
            "点灯：已亮不重点、写屏障先挡、先点清材料、先记手记再扣材料、再补建灯光与计数")
    for token in ("SendToPlayerStorage", "SendToPlayer("):
        forbid(light_lamp, token, "点灯不发物品")
    read_point = need_body(world, "internal void ReadPoint(string key, Action recorded)", "ReadPoint")
    ordered(read_point, ["LightChoice(choices, key);", "presentation.Show(PointName(key)"], "装置面板在显示之前挂上点灯选项")
    light_choice = need_body(world, "private void LightChoice(List<SkyIslandStoryPresentation.Choice> choices, string key)", "点灯选项")
    ordered(light_choice, ["SkyIslandLights.ForMarker(key)", "SkyIslandLights.Lit(story.Current, light.Id)) return;",
                           "string guarded = OverlookGuarded(key);", "fieldcraft.LightLamp(light, out message)", "Refreshed(lit, message)"],
            "点灯选项：只挂在还缺灯的装置上、残星瞭台先清守卫、点亮才重开面板")
    ctor = need_body(fieldcraft, "internal SkyIslandFieldcraft(SkyIslandSession owner, SkyIslandStoryService storyService, Transform worldRoot)",
                     "局内 owner 装配")
    ordered(ctor, ["lightsLit = story != null ? SkyIslandLights.LitCount(story.Current)", "PlaceFires();", "Current = this;"],
            "进岛先按存档数灯、建灯光，再对耗材公开 owner")
    place = need_body(fieldcraft, "private void PlaceFires()", "灯光")
    require(place, "SkyIslandLights.HearthMarkers", "灶火从灯表里取，不另写一份")
    require(place, "SkyIslandLights.Lit(story.Current, lamps[i].Id)) AddFire(lamps[i].Marker, LampColor);", "点过的风晶灯进岛就亮")
    journal_panel = need_body(world, "private void OpenJournal()", "群岛手记面板")
    if journal_panel.count("choices.Add(") != 3 or "for(int i = 0;i < SkyIslandJournal.Chapters.Length;i++)" not in journal_panel:
        errors.append("群岛手记面板必须是 4 个见闻章节 + 2 项，共 6 个选项（面板布局按最坏 6 个选项复算）")
    require(journal_panel, "SkyIslandLights.Chapter(story.Current, SkyIslandSession.RegionLabel)", "手记要有「岛上的灯」一页（地名唯一来源是会话）")
    require(journal_panel, "SkyIslandJournal.Uses()", "手记要有「群岛之物」一页")

    # ---- 4. 剧情 ⇄ 采集 / 合成 ----
    harvest = need_body(fieldcraft, "private bool Harvest(SkyIslandGatherNode node)", "采集产出")
    ordered(harvest, ["SkyIslandStoryData data = story != null ? story.Current : null;", ", IsNight(), data);",
                      "SkyIslandFieldcraftRules.StoryBonusReason(node, data)"], "采集产出读剧情进度，字幕说出为什么长得更旺")
    for recipe_id, gate in (("Windcrystal", ".After(SkyIslandStoryFlag.StarLamp)"), ("Bento", ".After(SkyIslandStoryFlag.PlantingDelivered)"),
                            ("Compass", ".AfterNote(SkyIslandItemRules.CompassKeepsake)")):
        match = re.search(r'Recipe\("%s",.*?\)\)\s*(\.After(?:Note)?\([^)]*\))' % recipe_id, squash(recipes_block))
        if not match or match.group(1) != squash(gate):
            errors.append("配方 %s 必须挂门槛 %s" % (recipe_id, gate))
    craft = need_body(fieldcraft, "internal bool Craft(SkyIslandRecipe recipe, out string message)", "合成")
    ordered(craft, ["SkyIslandFieldcraftRules.Unlocked(recipe, story != null ? story.Current : null)", "SkyIslandFieldcraftRules.LockedMessage(recipe)",
                    "SkyIslandFieldcraftRules.Missing(recipe, CountInPack);"], "合成先看会不会做（门槛只挂在面板上会被绕过）")
    open_crafting = need_body(world, "private void OpenCrafting(SkyIslandCraftStation station)", "合成面板")
    ordered(open_crafting, ["SkyIslandFieldcraftRules.Unlocked(recipe, story.Current)", "SkyIslandFieldcraftRules.LockedLabel(recipe)",
                            "continue;", "fieldcraft.Craft(recipe, out message)"], "合成面板把还不会做的配方写明要等什么")
    compass = need_body(world, "internal string CompassReading(Vector3 from)", "罗盘读数")
    ordered(compass, ["SkyIslandMapMarkers.SideTargets(story.Current)", "SkyIslandLights.UnlitMarkers(story.Current)",
                      "fieldcraft.TryNearestUnharvested(SkyIslandGatherKind.Crystal"],
            "罗盘在主线支线之后指还缺灯的地方与没采的风晶簇")

    # ---- 5. 风 ----
    wind = need_body(fieldcraft, "private void TickWind(CharacterMainControl player, bool night, float elapsed)", "夜风")
    ordered(wind, ["SkyIslandFieldcraftRules.NightWind(night, lightsLit)", "SkyIslandFieldcraftRules.CoreEased(", "CarriesCore()",
                   "SkyIslandFieldcraftRules.Warmth(NearFire(player.transform.position), incenseUntil > 0f, lanternUntil > 0f)",
                   "SkyIslandFieldcraftRules.StepExposure(exposure, level, warmth, elapsed)"],
            "夜风读岛上的灯与噬风之核，暖和分灶火灯香 / 风灯两档")
    carries = need_body(fieldcraft, "private bool CarriesCore()", "噬风之核")
    ordered(carries, ["if (now < nextCarryCheck) return coreCarried;", "CountInPack(BossRushItemIds.SkyIslandWindeaterCore)"],
            "数背包里的噬风之核要节流")
    if squash(fieldcraft).count("CarriesCore()") != 2:
        errors.append("CarriesCore() 只许在夜风推进里调用一次（外加定义），不许进每帧路径")
    detonate = need_body(boss, "private void Detonate(int wave)", "噬风风暴")
    require(detonate, "damage.damageValue = SkyIslandFieldcraftRules.StormPulseDamage(PulseDamage, SkyIslandFieldcraft.StormWarded);",
            "晴岚护符要替玩家挡噬风的风暴")
    require(need_body(fieldcraft, "internal static bool StormWarded", "护符挡风暴"), "current.charmWorn", "挡风暴读本趟是否系着护符")

    # ---- 6. 航徽与归航菜 ----
    for signature, label in (("internal string Repair()", "整备"), ("internal string Heal()", "苔药")):
        body = need_body(services, signature, label)
        ordered(body, ["bool badge = CarriesBadge();", "SkyIslandItemRules.ServicePrice(", "EconomyManager.IsEnough(", "EconomyManager.Pay("],
                label + "：带着航徽的折扣要在报价之后、验钱付款之前")
    require(need_body(services, "private static bool CarriesBadge()", "航徽"),
            "ItemFactory.GetItemCountInInventory(BossRushItemIds.SkyIslandHomecomingBadge)", "航徽按背包顶层数（与合成台同一口径）")
    for signature in ("internal string Meal(bool plantingDelivered)", "internal string PackedMeal()"):
        require(need_body(services, signature, "归航菜"), "ApplyMeal()", "晴禾那一顿与便当共用同一份加成")
    if squash(services).count("mealUsed = true;") != 1:
        errors.append("mealUsed 只许在 ApplyMeal 里置位一次：便当与晴禾那一顿共用本趟一次")
    require(need_body(fieldcraft, "internal bool CanUse(SkyIslandFieldBuff buff)", "耗材可用性"),
            "return session.HasPlantingDelivered && session.Services != null && !session.Services.MealEaten;",
            "便当那一顿要菜畦重新开张、且这一趟还没吃过归航菜")

    # ---- 7. 写给玩家的数字与常量一致 ----
    ward = re.search(r"internal const float CharmStormWard = ([0-9.]+)f;", rules)
    rate = re.search(r"internal const double BadgeServiceRate = ([0-9.]+);", item_rules)
    descriptions = squash(items)
    if not ward:
        errors.append("缺 CharmStormWard 常量")
    else:
        percent = int(round(float(ward.group(1)) * 100))
        for token in ("−%d%%" % percent, "%d%% less damage from the Windeater's storm" % percent):
            if token not in descriptions:
                errors.append("晴岚护符的描述要写明噬风风暴减伤 %d%%（与 CharmStormWard 一致）：缺 %s" % (percent, token))
    if not rate or float(rate.group(1)) != 0.5:
        errors.append("航徽描述写的是「半价」，BadgeServiceRate 必须是 0.5（改了要同步描述、字幕与 Wiki）")
    for token, why in (("渡口整备与眠苔的苔药都只收半价", "航徽描述要写明带在身上的效果"),
                       ("大风对你只算微风", "噬风之核描述要写明带在身上的效果"),
                       ("群岛上七处装置旁各缺一盏风晶灯", "晴岚风晶描述要写明它是风晶灯的灯芯")):
        if token not in descriptions:
            errors.append(why + "（缺「%s」）" % token)

    # ---- 8. 晴岚航徽：拉缆绳回码头（2026-09-11 拍板） ----
    if "echo(DebugAndTools\\SkyIsland\\SkyIslandSessionRecall.cs" not in read("compile_official.bat"):
        errors.append("编译清单缺 SkyIslandSessionRecall.cs")
    if '("TryRecallToDock",' not in suite_guard:
        errors.append("F3 只读守卫的写入口名单缺 TryRecallToDock（它会搬动玩家）")
    recall = clean_source(read("DebugAndTools/SkyIsland/SkyIslandSessionRecall.cs"))
    require(need_body(recall, "internal bool RecallAvailable", "拉缆绳可用性"), "!recallUsed", "航徽的缆绳每趟只能拉一次")
    ordered(need_body(recall, "internal bool TryRecallToDock(out string message)", "拉缆绳回码头"),
            ["if (!RecallAvailable)", "if (!CanOpenStoryPanel(out reason))", "VerifyGround(playerSpawn);",
             "player.SetPosition(safePosition);", "recallUsed = true;"],
            "拉缆绳：这趟没拉过、附近没敌人（与剧情面板同一道战斗门，不是逃生键）、先核码头落点的地面，再搬人并记下这趟已用")
    if not re.search(r"WithUse\(Make\(BossRushItemIds\.SkyIslandHomecomingBadge,.*?\),SkyIslandFieldBuff\.Recall\)", squash(items)):
        errors.append("晴岚航徽的定义要挂上「拉缆绳回码头」的使用效果（WithUse(..., SkyIslandFieldBuff.Recall)）")
    keepsake = case_body("Keepsake")
    for token in ("item.MaxDurability = NonConsumableDurability;", "Component<SkyIslandFieldcraftUsage>(item)",
                  "recall.buff = (int)def.Buff;", "AttachUsage(item, def.UseTime, recall);"):
        require(keepsake, token, "带使用效果的纪念品要挂使用行为、且不消耗（耐久同罗盘）")
    require(need_body(fieldcraft, "internal bool CanUse(SkyIslandFieldBuff buff)", "耗材可用性"),
            "if (buff == SkyIslandFieldBuff.Recall) return session.RecallAvailable;", "航徽的使用按钮只在这趟还没拉过缆绳时亮")
    require(use_consumable, "session.TryRecallToDock(out pulled)", "航徽的使用交给会话拉缆绳")
    require(need_body(clean_source(read("Integration/SkyIsland/SkyIslandFieldcraftUsage.cs")), "protected override void OnUse(Item item, object user)",
                      "群岛物品使用"), "item.Durability = item.MaxDurability;",
            "更新前发出的航徽没有耐久记录：使用前补满，免得官方 CA_UseItem 用完就把它销毁")

    print("SkyIslandContentWeaveGuard: " + ("FAIL\n  - " + "\n  - ".join(errors) if errors else
                                             "PASS (18 件物品各有用处 / 风晶灯 7 + 灶火 3 / 剧情门槛 3 / 风三档 / 航徽与便当接线 / 拉缆绳回码头)"))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
