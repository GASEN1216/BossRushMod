"""天空岛内容批次二的接线守卫（信鸽来信 / 秘境谜题 / 归航船名册 / 群岛手记 / 天空岛物品 / R-1 R-7 R-8 R-14）。

行为由执行回归 `tests/fixtures/SkyIslandStory` 验证（每一道谜题的每一种选法、来信顺序、名册分支、纪念品台账、
手记落盘与重进）；落点由 `SkyIslandInteractionCompetitionPropertyTest` 按真实几何复算（12 处信鸽落点、名册纪念物）。
这里只钉**接线与不变式**，而且尽量从数据源推导期望值，不在守卫里再写一份会过期的常量：

1. 折翎站位与他那一战的遭遇锚点、旧腰牌纪念物三者同点——期望值读 `Assets/Data/SkyIsland/World.json`（R-1）。
2. 信鸽只在存档可写时放、先记手记再放飞、会话结束收回；12 封信的锚点都真实存在于作者布局。
3. 谜题只挂在支线物证点上，解开之后才走原收录动作，旗标与 `TrySearchAction` → `TryApply` 同一映射；残星瞭台仍先清守卫。
4. 手记章节恰好覆盖 `PointName` 里的 20 处见闻；手记入口挂在苇白与码头装置上。
5. 纪念品先记手记再发物品、写屏障下不发；物品 TypeID 登记在克隆注册表、配置器、本地化与掉落黑名单（代码与 JSON）。
6. 岛上特产走独立随机流，不改变箱子原有件数的抽样；物品发放先问 prefab。
7. 爆炸遮挡补丁注释里的岛面高度与布局表一致（R-8，从 layout.json 算，不写死）。

反向验证：把任一条接线改回旧写法（例如折翎站位改回 POI_F、先发物品再记手记、谜题解开前就 RecordSearch），本守卫必红。
"""
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
SKY = ROOT / "DebugAndTools" / "SkyIsland"


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

    def ordered(text, tokens, why):
        if not text:
            return
        position = -1
        for token in tokens:
            found = text.find(squash(token), position + 1)
            if found < 0:
                errors.append("%s（缺 %s）" % (why, token))
                return
            if found <= position:
                errors.append("%s（顺序不对：%s）" % (why, token))
                return
            position = found

    world = clean_source(read("DebugAndTools/SkyIsland/SkyIslandWorldStory.cs"))
    service = clean_source(read("DebugAndTools/SkyIsland/SkyIslandStoryService.cs"))
    rules = clean_source(read("DebugAndTools/SkyIsland/SkyIslandStoryRules.cs"))
    residents = clean_source(read("DebugAndTools/SkyIsland/SkyIslandResidents.cs"))
    letters = clean_source(read("DebugAndTools/SkyIsland/SkyIslandLetters.cs"))
    puzzles = clean_source(read("DebugAndTools/SkyIsland/SkyIslandPuzzles.cs"))
    journal = clean_source(read("DebugAndTools/SkyIsland/SkyIslandJournal.cs"))
    item_rules = clean_source(read("DebugAndTools/SkyIsland/SkyIslandItemRules.cs"))
    crate = clean_source(read("DebugAndTools/SkyIsland/SkyIslandRewardCrate.cs"))
    markers = clean_source(read("DebugAndTools/SkyIsland/SkyIslandMapMarkers.cs"))
    session = clean_source(read("DebugAndTools/SkyIsland/SkyIslandSession.cs"))
    items = clean_source(read("Integration/SkyIsland/SkyIslandItems.cs"))
    compass = clean_source(read("Integration/SkyIsland/SkyIslandCompassUsage.cs"))
    layout = json.loads(read("ArtSource/SkyIsland/layout.json"))
    world_table = json.loads(read("Assets/Data/SkyIsland/World.json"))
    author_markers = {m["id"]: m["position"] for m in layout["markers"]}

    # ---- 0. 登记：编译清单、本地化守卫、隔离回归 ----
    bat = read("compile_official.bat")
    for rel in ("DebugAndTools\\SkyIsland\\SkyIslandLetters.cs", "DebugAndTools\\SkyIsland\\SkyIslandPuzzles.cs",
                "DebugAndTools\\SkyIsland\\SkyIslandCrew.cs", "DebugAndTools\\SkyIsland\\SkyIslandJournal.cs",
                "DebugAndTools\\SkyIsland\\SkyIslandItemRules.cs", "Integration\\SkyIsland\\SkyIslandItems.cs",
                "Integration\\SkyIsland\\SkyIslandCompassUsage.cs"):
        if "echo(" + rel not in bat:
            errors.append("编译清单缺 " + rel)
    l10n_guard = read("tests/SkyIslandLocalizationGuard.py")
    for name in ("SkyIslandLetters.cs", "SkyIslandPuzzles.cs", "SkyIslandCrew.cs", "SkyIslandJournal.cs",
                 "SkyIslandItemRules.cs", "SkyIslandMapMarkers.cs"):
        if '"%s"' % name not in l10n_guard:
            errors.append("SkyIslandLocalizationGuard.FILES 缺 " + name)
    csproj = read("tests/fixtures/SkyIslandStory/Regression.csproj")
    for name in ("SkyIslandLetters.cs", "SkyIslandPuzzles.cs", "SkyIslandCrew.cs", "SkyIslandJournal.cs", "SkyIslandItemRules.cs"):
        if "DebugAndTools/SkyIsland/" + name not in csproj:
            errors.append("隔离回归没有链接 " + name + "（纯逻辑没人执行）")
    suite_guard = read("tests/SkyIslandValidationSuiteGuard.py")
    for member in ("RecordNote", "UseCompass"):
        if '("%s",' % member not in suite_guard:
            errors.append("F3 只读守卫的写入口名单缺 " + member)

    # ---- 1. R-1：折翎站位 = 遭遇锚点 = 旧腰牌纪念物 ----
    zheling = [e for e in world_table["encounters"] if e["id"] == "Zheling"]
    ids = re.findall(r'"(sky_\w+)"', residents.split("private static readonly string[] Ids", 1)[1].split("};", 1)[0])
    spots = re.findall(r'"(\w+)"', residents.split("private static readonly string[] Markers", 1)[1].split("};", 1)[0])
    if len(zheling) != 1 or "sky_zheling" not in ids or len(ids) != len(spots):
        errors.append("读不到折翎的遭遇锚点或居民站位表")
    else:
        encounter_marker = zheling[0]["marker"]
        stand = spots[ids.index("sky_zheling")]
        a, b = author_markers.get(stand), author_markers.get(encounter_marker)
        if a is None or b is None:
            errors.append("折翎站位 %s 或遭遇锚点 %s 不在作者布局里" % (stand, encounter_marker))
        elif ((a[0] - b[0]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5 > 10.0:
            errors.append("折翎站位 %s 离他那一战的锚点 %s 超过 10 m：面前选「挑战」时本人消失、战斗体刷在远处（R-1）"
                          % (stand, encounter_marker))
        badge = re.search(r'Beacon\("(\w+)",L10n\.T\("折翎的旧腰牌"', squash(world))
        if not badge or badge.group(1) != encounter_marker:
            errors.append("折翎旧腰牌必须落在他那一战的锚点 %s 上" % encounter_marker)

    # ---- 2. 信鸽来信 ----
    tick = need_body(world, "internal void Tick()", "WorldStory.Tick")
    ordered(tick, ["TickPigeon();", "if (displayedFlags == story.Current.flags) return;"],
            "信鸽必须在剧情位早退之前每帧推进（否则旗标不变的趟里永远不落地）")
    pigeon_tick = need_body(world, "private void TickPigeon()", "信鸽推进")
    require(pigeon_tick, "SkyIslandLetter letter = story.CanWrite ? SkyIslandLetters.NextFor(story.Current) : null;",
            "存档不可写时不放信鸽：收不下的信不该出现")
    place = need_body(world, "private bool PlacePigeon(SkyIslandLetter letter)", "信鸽落点")
    for token in ("SkyIslandRewardCrate.TryFindCratePosition(", "SkyIslandLootTables.StableHash(letter.Id) % 360",
                  "SkyIslandRewardCrate.InteractableSeparation"):
        require(place, token, "信鸽落点必须复用纪念物的放置算法（交互竞争属性测试按同一算法复算）")
    read_letter = need_body(world, "private void ReadLetter(SkyIslandLetter letter)", "读信")
    require(read_letter, "if (BlockedByCombat()) return;", "读信面板同样要过战斗门")
    ordered(read_letter, ["story.RecordNote(letter.Id, out message)", "ReleasePigeon();", "GrantKeepsakes();"],
            "收下信：先写手记，再放飞信鸽，再补查纪念品")
    dispose = need_body(world, "public void Dispose()", "WorldStory.Dispose")
    require(dispose, "ReleasePigeon();", "会话结束必须收回信鸽")
    letter_rows = re.findall(r'Letter\("(Letter_\d+)",\s*"(\w+)",\s*"(\w+)"', letters)
    if len(letter_rows) != 12 or len({row[0] for row in letter_rows}) != 12:
        errors.append("信鸽来信必须是 12 封、id 不重复，实际 %d" % len(letter_rows))
    for letter_id, anchor, region in letter_rows:
        if anchor not in author_markers:
            errors.append("%s 的落点锚点 %s 不在作者布局里" % (letter_id, anchor))
        if region not in ("A", "B", "C", "D", "E", "F", "G", "H", "S1", "S2", "S3", "S4"):
            errors.append("%s 的区域 %s 不是 12 个区域之一" % (letter_id, region))

    # ---- 3. 秘境谜题 ----
    read_point = need_body(world, "internal void ReadPoint(string key, Action recorded)", "ReadPoint")
    require(read_point, "SkyIslandPuzzle puzzle = SkyIslandPuzzles.For(key);", "物证点先查有没有谜题")
    require(read_point, "bool solving = puzzle != null && !story.Current.Has(puzzle.Flag) && !puzzles.IsSolved(puzzle);",
            "只有物证还没拿到、谜题也没解开时才出谜题")
    require(read_point, "string guarded = OverlookGuarded(key);", "普通收录仍要过残星瞭台的守卫门")
    puzzle_choices = need_body(world, "private void PuzzleChoices(", "谜题选项")
    ordered(puzzle_choices, ["string guarded = OverlookGuarded(key);", "puzzles.Choose(puzzle, option, out feedback);",
                             "if (outcome == SkyIslandPuzzleOutcome.Solved)", "story.RecordSearch(key, out message);"],
            "谜题：先过守卫门，再判答案，最后一步答对才走原收录动作")
    if puzzle_choices.count("story.RecordSearch(") != 1:
        errors.append("谜题里只能在解开的那一支调用 RecordSearch")
    search_actions = dict(re.findall(r'case "(Search_S\d)": action = SkyIslandStoryAction\.(\w+); return true;', rules))
    action_flags = dict(re.findall(r'case SkyIslandStoryAction\.(\w+):\s*flag = SkyIslandStoryFlag\.(\w+);', rules))
    puzzle_flags = dict(re.findall(r'Puzzle\("(Search_S\d)", SkyIslandStoryFlag\.(\w+),', puzzles))
    if len(puzzle_flags) != 4 or set(puzzle_flags) != set(search_actions):
        errors.append("谜题必须恰好挂在四个支线物证点上：%r / %r" % (sorted(puzzle_flags), sorted(search_actions)))
    for key, flag in puzzle_flags.items():
        expected = action_flags.get(search_actions.get(key, ""), None)
        if expected != flag:
            errors.append("谜题 %s 的旗标 %s 与它解开后的收录动作写下的 %s 不一致" % (key, flag, expected))

    # ---- 4. 群岛手记 ----
    point_keys = set(re.findall(r'case "(Search_[A-Z0-9_]+)": return', world.split("internal static string PointName(", 1)[1]
                                .split("private bool BlockedByCombat", 1)[0]))
    chapter_keys = re.findall(r'"(Search_[A-Z0-9_]+)"', journal.split("internal static readonly string[][] Chapters", 1)[1]
                              .split("};", 1)[0])
    if len(point_keys) != 20 or sorted(chapter_keys) != sorted(point_keys):
        errors.append("手记章节必须恰好覆盖 PointName 里的 20 处见闻：章节 %d 条，见闻 %d 处" % (len(chapter_keys), len(point_keys)))
    talk = need_body(world, "internal void Talk(string id, Transform speaker)", "Talk")
    weibai = talk.split('else if(id == "sky_weibai")', 1)[1].split("else if", 1)[0] if 'else if(id == "sky_weibai")' in talk else ""
    require(weibai, "JournalChoice(choices);", "苇白要能翻群岛手记")
    require(read_point.split('case "Search_A":', 1)[1].split("case", 1)[0] if 'case "Search_A":' in read_point else "",
            "JournalChoice(choices);", "码头装置要能翻群岛手记（每趟必经）")

    # ---- 5. 归航船名册与纪念品 ----
    require(tick, 'Beacon("Lamp_A_02", L10n.T("归航船 · 船员名册", "Homecoming boat · crew roster"), BossRushUIColors.Accent,',
            "结局后码头要挂归航船名册")
    crew_choices = need_body(world, "private List<SkyIslandStoryPresentation.Choice> CrewChoices()", "名册选项")
    require(crew_choices, "story.RecordNote(SkyIslandCrew.NoteId(page), out message)", "名册翻页要记进手记")
    ordered(tick, ["AnnounceCombatOutcomes(added);", "GrantKeepsakes();"], "旗标变化后补查纪念品（旧存档第一次进岛同样补发）")
    grant = need_body(world, "private void GrantKeepsakes()", "纪念品发放")
    ordered(grant, ["if (!story.CanWrite) return;", "SkyIslandItemRules.Due(story.Current, all[i])",
                    "story.RecordNote(all[i].NoteId, out message)", "SkyIslandItems.TryGive(all[i].TypeId, all[i].ToStorage)"],
            "纪念品必须先记手记再发物品：写屏障下每趟重发一件能卖钱的东西是经济漏洞")
    record_note = need_body(service, "internal bool RecordNote(string id, out string message)", "RecordNote")
    ordered(record_note, ["SkyIslandLetters.Find(id) == null && SkyIslandCrew.IndexOf(id) < 0 && SkyIslandItemRules.FindKeepsake(id) == null",
                          "return RecordSearch(id, out message);"],
            "RecordNote 只收登记过的来信 / 名册 / 纪念品 id，再复用见闻的去重与去抖")

    # ---- 6. 天空岛物品：TypeID 登记与发放纪律 ----
    ids_src = read("Config/ConfigItemIds.cs")
    expected_ids = {
        "SkyIslandHomecomingBadge": 500068, "SkyIslandWindeaterCore": 500069, "SkyIslandWindVaneCompass": 500070,
        "SkyIslandHomecomingBento": 500071, "SkyIslandStarmossSalve": 500072,
    }
    registry = read("Integration/BossRushDynamicItemRegistry.cs")
    blacklist = read("Config/LootBlacklistRegistry.cs")
    blacklist_json = json.loads(read("Assets/Data/LootBlacklist.json"))["itemIds"]
    for name, value in expected_ids.items():
        if not re.search(r"public const int %s = %d;" % (name, value), ids_src):
            errors.append("ConfigItemIds 缺 %s = %d" % (name, value))
        if "BossRushItemIds." + name not in registry.split("SkyIslandItems.EnsureRuntimeRegistration", 1)[-1][:900]:
            errors.append("克隆注册表没有登记 %s：重启后背包里的它会退化成官方 FallbackItem" % name)
        if "BossRushItemIds." + name not in blacklist:
            errors.append("掉落黑名单（代码兜底）缺 " + name)
        if value not in blacklist_json:
            errors.append("掉落黑名单（JSON）缺 %d" % value)
    if "SkyIslandItems.RegisterConfigurators();" not in read("Integration/Items/ItemContentRegistry.cs"):
        errors.append("物品配置器没有登记（只注册不配置）")
    if "SkyIslandItems.InjectLocalization();" not in read("Integration/BossRushIntegration_StartAndScene.cs"):
        errors.append("物品名没有注入本地化（游戏里会显示 *BossRush_SkyIsland_...*）")
    if "SkyIslandItems.ResetStaticCaches();" not in read("DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs"):
        errors.append("SkyIslandItems 的静态缓存没有生命周期 owner")
    give = need_body(items, "internal static bool TryGive(int typeId, bool toStorage)", "物品发放")
    ordered(give, ["ItemAssetsCollection.GetPrefab(typeId) == null", "ItemAssetsCollection.InstantiateSync(typeId);",
                   "item.TypeID != typeId"], "发放物品必须先问 prefab：缺资源时的空壳带着同一个 TypeID")
    configure = need_body(items, "private static void ConfigureItem(int typeId, Item item)", "物品配置")
    for token in ("ModeFItemConfigHelper.ClearInheritedUsage(item);", "item.MaxDurability = NonConsumableDurability;",
                  "Component<FoodDrink>(item)", "Component<Drug>(item)", "EquipmentHelperIcon.TryInjectIcon(item, null, def.IconName);"):
        require(configure, token, "物品配置：清克隆源用法、罗盘不消耗、特产复用官方行为、专属图标")
    icon_names = set(re.findall(r'"(sky_island_[a-z_]+)"', items))
    generator = set(re.findall(r'\("(sky_island_[a-z_]+)",', read("tools/gen_sky_island_item_icons.py")))
    if len(icon_names) != 5 or icon_names != generator:
        errors.append("物品图标名与生图脚本清单不一致：%r / %r" % (sorted(icon_names), sorted(generator)))
    fill = need_body(crate, "internal static int Fill(", "装箱")
    ordered(fill, ['SkyIslandLootTables.CreateStream(raidSeed, streamId + "#island").NextDouble()',
                   "int total = extra != 0 ? count + 1 : count;",
                   "int typeId = i < count ? source[random.Next(source.Length)] : extra;"],
            "岛上特产必须走独立随机流、只追加一件，原有 count 件的抽样一件不变")
    use_compass = need_body(session, "internal bool UseCompass()", "罗盘读数")
    require(use_compass, "Status(worldStory.CompassReading(player.transform.position), false);", "罗盘读数走本岛唯一的提示出口")
    on_use = need_body(compass, "protected override void OnUse(Item item, object user)", "罗盘使用")
    require(on_use, "session.UseCompass()", "罗盘使用要交给会话读数")
    if "Update(" in compass or "Tick(" in compass:
        errors.append("罗盘不得在每帧路径上找会话")
    if "IslandExtraFor(SkyIslandLootTier tier, double roll)" not in item_rules:
        errors.append("岛上特产的概率表必须留在纯规则里（隔离回归逐档核对）")

    # ---- 7. R-14 / R-7 / R-8 ----
    apply = need_body(markers, "internal void Apply(SkyIslandStoryData data, Transform dock, Transform bell, Transform wind, Transform star)",
                      "地图标记")
    require(apply, "foreach (string target in SideTargets(data))", "官方地图要圈可选目标（R-14）")
    side = need_body(markers, "internal static IEnumerable<string> SideTargets(SkyIslandStoryData data)", "可选目标")
    require(side, 'if (data.BothBeacons && !data.StormResolved) yield return "POI_E";', "双航标后没打噬风要圈风眼")
    for key in ("Search_S1", "Search_S2", "Search_S3", "Search_S4"):
        require(side, 'yield return "%s";' % key, "结局后要圈未拿到的支线物证")
    if apply.count("step into the green ring to extract") != 2:
        errors.append("航标广场撤离提示的英文要带「站进绿环即可撤离」那半句（R-7）")
    controls = read("DebugAndTools/SkyIsland/SkyIslandControls.cs")
    if "敲响归航钟后钟庭的绿环" in controls or "After the Homecoming Bell rings, the green ring" in controls:
        errors.append("F3 面板说明仍是布局 v1 的撤离口径（R-8）")
    objective = need_body(rules, "internal static string Objective(SkyIslandStoryData data)", "目标句")
    if "航标广场" not in objective or "beacon plaza" not in objective:
        errors.append("结局目标句要写上两处航标广场出口（R-8）")
    heights = sorted({int(round(island["height"])) for island in layout["islands"]})
    expected_line = "y = " + " / ".join(str(h) for h in heights)
    if expected_line not in read("DebugAndTools/SkyIsland/SkyIslandExplosionObstaclePatch.cs"):
        errors.append("爆炸遮挡补丁注释里的岛面高度与布局表不一致，应为「%s」（R-8）" % expected_line)

    print("SkyIslandContentPackGuard: " + ("FAIL\n  - " + "\n  - ".join(errors) if errors else
                                             "PASS (信鸽 12 / 谜题 4 / 手记 20 / 纪念品 3 / 物品 5 与 R-1 R-7 R-8 R-14 接线)"))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
