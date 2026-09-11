"""天空岛内容批次四「云蚋」（夜里的蚊群）的接线守卫：内容图、单一判夜口径、叮咬安全、蚊群目标与弹道钩子、灶火、美术与声音、布局表。

期望值尽量从数据源推导（配方表、风晶灯表、物品定义表、TypeID 常量、作者布局 layout.json、像素画生成脚本、音效生成脚本）：

0. 登记：编译清单（保持 CRLF）、本地化守卫、两个隔离回归夹具的链接与调用、F3 只读守卫的写入口名单、静态缓存复位；纯规则只依赖 System。
1. **判夜只有一个口径**：`SkyIslandNight.IsNight`（读不到时钟 → NaN → 不是夜里）。光照、夜风、云蚋、蛙卵都经
   `SkyIslandLighting.ClockHours()` 读钟；全岛源码不出现官方 `AtNight`，`GameClock.TimeOfDay` 只在 ClockHours 读一次；
   开发开关「强制夜里」只在 `#if BOSSRUSH_DEV` 里写。
2. **内容图**：批次四的新 TypeID 从 500083 连续、至多 4 件；每件都有来源（岛上某条配方产出它）和不卖钱的用处
   （随身装备在蚊群 owner 里数背包并真的改叮咬；耗材 / 工具在局内 owner 里转给蚊群 owner）。
   弱链补丁：每种群岛材料至少被 2 处消耗（配方 + 风晶灯 + 蛙卵）；残铜片、星屑、晴岚风晶都有批次四的新消耗。
   驱风香、风灯、灶火的烟、风晶灯、灭蚊灯都进了刷新权重；药膏（止痒且一阵内再叮不痒）与苔药（只止当下的痒）分工。
3. 青蛙：镜水寺池边捧蛙卵、蛙鸣池边放回，两处面板装置分别在对应水面的岛上、离水面不远（从布局推导）；
   **写屏障先挡、先记手记再放下**；手记只收登记过的 `Frog_n`；刷新权重读放生数；放满之后名册、手记、居民都有回应。
4. 叮咬安全：伤害只在 TryBite 与 HurtGnat 两处；叮咬是 `DamageInfo(null)` 真伤、不随难度、先判「到点」与「血量下限」再下嘴；
   云蚋被打死不掉东西。
5. 蚊群目标：先建成失活再激活、伤害接收体层的非触发球碰撞体 + 运动学刚体 + 简易血量、阵营 wolf；挪完 `Physics.SyncTransforms()`；
   弹道钩子是 `Projectile.Init(ProjectileContext)` 的后缀、先判空；逐帧路径不查场景、不用 LINQ / 闭包。
6. 灶火看得见：火与烟经 `SkyIslandHearthFx` 生在装置旁、点光挪到火上；锚点缺失打警告；灶火与风晶灯分表判烟 / 判灯。
7. 美术与声音：像素画生成脚本自检（直接 import 复用）、帧常量与帧序一致、本地产物与重画逐像素一致；嗡声登记为无缝循环。
8. 布局表：静水表与岛框表逐项从 layout.json 推导核对。
9. 会话主文件不超过 1200 行、不接云蚋；手工验收清单有 M_SKY_ISLAND_13，第一项是「夜里看不看得见云蚋」。

行为（躲闪模拟、权重与叮咬数值、蛙卵进度、判夜等价）由执行回归 `tests/fixtures/SkyIslandStory` 与 `SkyIslandLighting` 验证；这里只钉接线。
反向验证：把判夜改回照读官方时钟、叮咬去掉血量下限、先放下蛙卵再记手记、弹道钩子改挂别的方法、灶火不再生烟、
灭蚊灯配方去掉残铜片、静水表挪一个池子，本守卫必红。
"""
import importlib.util
import json
import math
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
SKY = "DebugAndTools/SkyIsland/"
STRING = r'"(?:[^"\\]|\\.)*"'
NEW_FILES = ("SkyIslandNight.cs", "SkyIslandMosquitoRules.cs", "SkyIslandGnats.cs", "SkyIslandGnatProjectilePatch.cs",
             "SkyIslandHearthFx.cs")
BATCH_FOUR_FIRST = 500083
MAX_NEW_TYPEIDS = 4
SESSION_LINE_BUDGET = 1200
# 评估报告与串联落地记录点名的弱链：残铜片过剩、星屑只有一处用、晴岚风晶只进灯。批次四的配方必须各给一处新消耗。
WEAK_LINK_MATERIALS = ("SkyIslandBrassScrap", "SkyIslandStardust", "SkyIslandQinglanWindcrystal")
# 蛙卵两处：（面板装置，对应的水面，面板上挂的选项）。水面必须在静水表里，装置与水面同岛且离水面框不超过 FROG_SITE_REACH 米。
FROG_SITES = (("Search_F_02", "F_MirrorPool", "SpawnChoice"), ("Search_S1", "S1_FrogPond", "ReleaseChoice"))
FROG_SITE_REACH = 15.0
HOT_PATH = ("internal void Frame(", "private bool HasFrameWork(", "private bool Steer(", "private Vector3 Cruise(", "private void TryBite(", "private void Animate(",
            "private void TickZappers(", "private void TickSplats(", "private void TickBuzz(", "internal void Sample(",
            "private void Scatter(", "internal void OnProjectile(")
HOT_PATH_FORBIDDEN = ("FindObjectsOfType", "FindObjectOfType", "GetComponentsInChildren", "=>", "delegate", "new List<", ".ToArray()")


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


def snake(name):
    return re.sub(r"(?<!^)([A-Z])", r"_\1", name).lower()


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

    def number(source, name, owner="SkyIslandMosquitoRules"):
        match = re.search(r"\b%s\s*=\s*(-?\d+(?:\.\d+)?)[fd]?\s*[;,]" % name, source)
        if not match:
            errors.append("%s 常量 %s 没解析到（改名了就同步守卫）" % (owner, name))
            return float("nan")
        return float(match.group(1))

    gnats_raw = read(SKY + "SkyIslandGnats.cs")
    gnats = clean_source(gnats_raw)
    gnats_s = squash(gnats)
    rules_raw = read(SKY + "SkyIslandMosquitoRules.cs")
    rules = clean_source(rules_raw)
    night_raw = read(SKY + "SkyIslandNight.cs")
    night = clean_source(night_raw)
    patch = squash(clean_source(read(SKY + "SkyIslandGnatProjectilePatch.cs")))
    hearth = clean_source(read(SKY + "SkyIslandHearthFx.cs"))
    fieldcraft = clean_source(read(SKY + "SkyIslandFieldcraft.cs"))
    fieldcraft_s = squash(fieldcraft)
    craft_rules = clean_source(read(SKY + "SkyIslandFieldcraftRules.cs"))
    lighting = clean_source(read(SKY + "SkyIslandLighting.cs"))
    lights = clean_source(read(SKY + "SkyIslandLights.cs"))
    services = squash(clean_source(read(SKY + "SkyIslandServices.cs")))
    service = squash(clean_source(read(SKY + "SkyIslandStoryService.cs")))
    world = clean_source(read(SKY + "SkyIslandWorldStory.cs"))
    journal = squash(clean_source(read(SKY + "SkyIslandJournal.cs")))
    crew = squash(clean_source(read(SKY + "SkyIslandCrew.cs")))
    module = clean_source(read(SKY + "SkyIslandRuntimeModule.cs"))
    item_rules = clean_source(read(SKY + "SkyIslandItemRules.cs"))
    items = clean_source(read("Integration/SkyIsland/SkyIslandItems.cs"))
    ids = clean_source(read("Config/ConfigItemIds.cs"))
    layout = json.loads(read("ArtSource/SkyIsland/layout.json"))

    # ---- 0. 登记 ----
    bat_raw = (ROOT / "compile_official.bat").read_bytes()
    bat = bat_raw.decode("utf-8", errors="replace")
    for name in NEW_FILES:
        if "echo(DebugAndTools\\SkyIsland\\%s" % name not in bat:
            errors.append("编译清单缺 %s（新增 .cs 不登记就静默不参与编译）" % name)
    if bat_raw.count(b"\n") != bat_raw.count(b"\r\n"):
        errors.append("compile_official.bat 混进了 LF 换行（cmd 会把行尾拆坏，报与代码无关的错）")
    for folder in ("Assets\\ui\\SkyIsland", "Assets\\Sounds\\SkyIsland\\*.wav"):
        if folder not in bat:
            errors.append("正式编译脚本没有部署 %s（精灵表 / 嗡声到不了游戏目录）" % folder)
    loc_guard = read("tests/SkyIslandLocalizationGuard.py")
    for name in NEW_FILES:
        if '"%s"' % name not in loc_guard:
            errors.append("SkyIslandLocalizationGuard.FILES 缺 %s（玩家可见文案没人查中英成对）" % name)
    story_proj = read("tests/fixtures/SkyIslandStory/Regression.csproj").replace("\\", "/")
    for name in ("SkyIslandNight.cs", "SkyIslandMosquitoRules.cs"):
        if "DebugAndTools/SkyIsland/" + name not in story_proj:
            errors.append("隔离回归 SkyIslandStory 没有链接 %s（云蚋规则没人执行）" % name)
    if "DebugAndTools/SkyIsland/SkyIslandNight.cs" not in read("tests/fixtures/SkyIslandLighting/Regression.csproj").replace("\\", "/"):
        errors.append("隔离回归 SkyIslandLighting 没有链接 SkyIslandNight.cs（光照与判夜的等价没人执行）")
    if "SkyIslandNight." not in clean_source(read("tests/fixtures/SkyIslandLighting/Program.cs")):
        errors.append("隔离回归 SkyIslandLighting 没有断言判夜口径")
    for name in ("SkyIslandMosquitoRegression.cs", "SkyIslandGnatDodgeSimulation.cs"):
        if not (ROOT / "tests/fixtures/SkyIslandStory" / name).is_file():
            errors.append("隔离回归缺 %s" % name)
    program = squash(clean_source(read("tests/fixtures/SkyIslandStory/Program.cs")))
    for call in ("SkyIslandMosquitoRegression.Run(Check);", "SkyIslandGnatDodgeSimulation.Run(Check);"):
        require(program, call, "隔离回归主程序没调用云蚋回归 / 躲闪模拟（写了没人跑）")
    for name, raw in (("SkyIslandMosquitoRules.cs", rules_raw), ("SkyIslandNight.cs", night_raw)):
        usings = re.findall(r"^using\s+([\w.]+)\s*;", raw, re.M)
        if usings != ["System"]:
            errors.append("%s 必须只依赖 System（隔离回归直接链接），实际 using：%r" % (name, usings))
    for pattern in (r"\bUnityEngine\b", r"\bMathf\.", r"\bVector3\b", r"\bTime\."):
        if re.search(pattern, rules) or re.search(pattern, night):
            errors.append("纯规则里出现了 Unity 依赖 %s：离线模拟跑的就不是生产代码了" % pattern)
    suite_guard = read("tests/SkyIslandValidationSuiteGuard.py")
    for member in ("TakeSpawn", "ReleaseSpawn", "SpawnChoice", "ReleaseChoice", "DeployZapper", "SwingFan", "Soothe",
                   "RemedyClearsItch", "OnProjectile", "Sample", "Frame"):
        if '("%s",' % member not in suite_guard:
            errors.append("F3 只读守卫的写入口名单缺 " + member)
    on_destroy = need_body(module, "void OnDestroy()", "运行时模块销毁")
    require(on_destroy, "SkyIslandGnats.ResetStaticCaches();", "云蚋的精灵表、材质与单例要随模块销毁清掉")
    require(on_destroy, "SkyIslandNight.ResetStaticCaches();", "强制夜里开关要随模块销毁复位")
    reset = need_body(gnats, "internal static void ResetStaticCaches()", "云蚋静态缓存复位")
    for token in ("Current = null;", "frames = null;", "sheet = null;", "spriteMaterial = trailMaterial = null;", "artAttempted = false;"):
        require(reset, token, "云蚋静态缓存复位不全")
    require(need_body(night, "internal static void ResetStaticCaches()", "判夜开关复位"), "DevForceNight = false;", "强制夜里开关不许跨进程残留")

    # ---- 1. 判夜只有一个口径 ----
    ordered(need_body(night, "internal static bool IsNight(double hours)", "唯一判夜"),
            ("double.IsNaN(hours)", "return false;", "return hours >= StartHour || hours < EndHour;"),
            "读不到时钟（NaN）必须先判成不是夜里，再按 21–5 点判")
    ordered(need_body(night, "internal static double EffectiveHours(bool clockAvailable, double clockHours)", "本 Mod 读到的钟点"),
            ("if (DevForceNight) return ForcedHour;", "return clockAvailable ? clockHours : double.NaN;"),
            "没有官方时钟实例时必须返回 NaN（TimeOfDay 恒为 00:00，照读会整趟判成夜里）")
    if number(night, "StartHour", "SkyIslandNight") != 21 or number(night, "EndHour", "SkyIslandNight") != 5:
        errors.append("夜里的钟点不再是 21–5 点：光照的星夜整档、星屑夜里加成与文案都按 21–5 写")
    clock = need_body(lighting, "internal static double ClockHours()", "光照读钟")
    require(clock, "bool available = GameClock.Instance != null;", "读钟要先判官方时钟实例")
    require(clock, "return SkyIslandNight.EffectiveHours(available, available ? GameClock.TimeOfDay.TotalHours : double.NaN);",
            "读钟要经 SkyIslandNight.EffectiveHours")
    for label, source, pattern in (
            ("光照的昼夜档", lighting, r"SkyIslandNight\.IsNight\(hours\)"),
            ("光照每帧读钟", lighting, r"ResolveTimeBlend\(ClockHours\(\)"),
            ("夜风（局内 owner）", fieldcraft, r"bool IsNight\(\)\s*\{\s*try\s*\{\s*return SkyIslandNight\.IsNight\(SkyIslandLighting\.ClockHours\(\)\);"),
            ("夜风规则", craft_rules, r"static bool IsNight\(double hours\)\s*\{\s*return SkyIslandNight\.IsNight\(hours\);\s*\}"),
            ("云蚋与蛙卵", gnats, r"bool NightNow\s*\{\s*get\s*\{\s*return SkyIslandNight\.IsNight\(SkyIslandLighting\.ClockHours\(\)\);")):
        if not re.search(pattern, source):
            errors.append("%s 没有走唯一判夜口径 SkyIslandNight.IsNight / SkyIslandLighting.ClockHours" % label)
    clock_reads = 0
    for path in sorted((ROOT / "DebugAndTools/SkyIsland").glob("*.cs")) + sorted((ROOT / "Integration/SkyIsland").glob("*.cs")):
        code = clean_source(path.read_text(encoding="utf-8-sig"))
        if re.search(r"\bAtNight\b", code):
            errors.append("%s 用了官方 AtNight（19–5 点，与岛上的光和风差两小时）" % path.name)
        hits = len(re.findall(r"GameClock\.TimeOfDay", code))
        clock_reads += hits
        if hits and path.name != "SkyIslandLighting.cs":
            errors.append("%s 直接读了 GameClock.TimeOfDay：钟点只许经 SkyIslandLighting.ClockHours() 读" % path.name)
    if clock_reads != 1:
        errors.append("GameClock.TimeOfDay 全岛应只读一次（ClockHours），实际 %d 处" % clock_reads)
    for path in sorted((ROOT / "DebugAndTools/SkyIsland").glob("*.cs")):
        if path.name == "SkyIslandNight.cs":
            continue
        stack = []
        for line_no, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            code = line.split("//", 1)[0].strip()
            if code.startswith("#if"):
                mentions = "BOSSRUSH_DEV" in code
                stack.append([mentions and "!" not in code, mentions])
            elif code.startswith("#else") and stack and stack[-1][1]:
                stack[-1][0] = not stack[-1][0]
            elif code.startswith("#endif") and stack:
                stack.pop()
            elif re.search(r"DevForceNight\s*=(?!=)", code) and not any(frame[0] for frame in stack):
                errors.append("%s:%d 在 #if BOSSRUSH_DEV 之外写了强制夜里开关（正式构建不许有入口）" % (path.name, line_no))

    # ---- 2. 内容图 ----
    id_values = {name: int(value) for name, value in re.findall(r"\b(SkyIsland\w+)\s*=\s*(5000\d\d)\b", ids)}
    all_block = item_rules.split("internal static readonly int[] AllTypeIds", 1)
    all_names = re.findall(r"BossRushItemIds\.(\w+)", all_block[1].split("};", 1)[0]) if len(all_block) == 2 else []
    batch_four = sorted((id_values[name], name) for name in all_names if id_values.get(name, 0) >= BATCH_FOUR_FIRST)
    numbers = [value for value, _ in batch_four]
    if not 1 <= len(numbers) <= MAX_NEW_TYPEIDS or numbers != list(range(BATCH_FOUR_FIRST, BATCH_FOUR_FIRST + len(numbers))):
        errors.append("批次四 TypeID 必须从 %d 连续、1–%d 件，实际 %r" % (BATCH_FOUR_FIRST, MAX_NEW_TYPEIDS, numbers))
    stray = sorted(name for name, value in id_values.items() if value >= BATCH_FOUR_FIRST and name not in all_names)
    if stray:
        errors.append("TypeID 常量 %r 没进 SkyIslandItemRules.AllTypeIds（注册、黑名单、图标都会漏）" % stray)
    batch_names = {name for _, name in batch_four}

    recipe_block = craft_rules.split("private static readonly SkyIslandRecipe[] recipes", 1)
    recipe_block = recipe_block[1].split("internal static SkyIslandRecipe[] Recipes", 1)[0] if len(recipe_block) == 2 else ""
    recipes = []
    for match in re.finditer(r'Recipe\("(\w+)",\s*SkyIslandCraftStation\.(\w+),\s*BossRushItemIds\.(\w+),\s*(\d+),'
                             r'((?:\s*In\(BossRushItemIds\.\w+,\s*\d+\),?)+)\)((?:\.\w+\([^)]*\))*)', recipe_block):
        rid, station, output, count, inputs, gates = match.groups()
        recipes.append((rid, station, output, int(count), re.findall(r"In\(BossRushItemIds\.(\w+),\s*(\d+)\)", inputs), gates))
    if not recipes or len(recipes) != recipe_block.count('Recipe("'):
        errors.append("配方表没解析全：%d / %d（正则与源码失步）" % (len(recipes), recipe_block.count('Recipe("')))
    lamp_rows = re.findall(r'Lamp\("(Light_\w+)",\s*"(\w+)",\s*"(\w+)",\s*"(Letter_\d+)",\s*' + STRING + r',\s*' + STRING +
                           r',\s*((?:In\([^)]*\),?\s*)+)\)', lights)
    crystal_alias = re.search(r"int crystal = BossRushItemIds\.(\w+);", lights)
    if len(lamp_rows) != 7 or not crystal_alias:
        errors.append("风晶灯表没解析全：%d 盏（正则与源码失步）" % len(lamp_rows))
    consumers = {}
    for rid, _station, _output, _count, inputs, _gates in recipes:
        for material, _n in inputs:
            consumers.setdefault(material, set()).add("配方 " + rid)
    for lamp_id, _marker, _region, _letter, inputs in lamp_rows:
        for token, _n in re.findall(r"In\((\w+(?:\.\w+)?),\s*(\d+)\)", inputs):
            material = crystal_alias.group(1) if token == "crystal" and crystal_alias else token.split(".")[-1]
            consumers.setdefault(material, set()).add("风晶灯 " + lamp_id)
    take_spawn = need_body(gnats, "internal bool TakeSpawn(out string message)", "捧蛙卵")
    for material in re.findall(r"ConsumeOne\(BossRushItemIds\.(\w+)\)", take_spawn):
        consumers.setdefault(material, set()).add("蛙卵")
    materials = re.findall(r"Material\(BossRushItemIds\.(\w+),", items)
    for material in materials:
        if len(consumers.get(material, ())) < 2:
            errors.append("群岛材料 %s 只有 %d 处消耗（%s）：弱链没补上" % (material, len(consumers.get(material, ())),
                                                                     sorted(consumers.get(material, ()))))
    new_inputs = {material for _rid, _st, output, _c, inputs, _g in recipes if output in batch_names for material, _n in inputs}
    for material in WEAK_LINK_MATERIALS:
        if material not in materials:
            errors.append("弱链名单里的 %s 不是群岛材料（名单过期）" % material)
        elif material not in new_inputs:
            errors.append("弱链 %s 没有批次四的新消耗（评估报告点名：残铜片过剩 / 星屑一处用 / 晴岚风晶只进灯）" % material)

    kinds = {}
    for name in re.findall(r"Gear\(BossRushItemIds\.(\w+),", items):
        kinds[name] = ("Gear", None)
    for name, buff in re.findall(r"Consumable\(BossRushItemIds\.(\w+),\s*SkyIslandFieldBuff\.(\w+),", items):
        kinds[name] = ("Consumable", buff)
    for name, buff in re.findall(r"Tool\(BossRushItemIds\.(\w+),\s*SkyIslandFieldBuff\.(\w+),", items):
        kinds[name] = ("Tool", buff)
    use_consumable = need_body(fieldcraft, "internal bool UseConsumable(SkyIslandFieldBuff buff)", "耗材效果")
    use_against = need_body(fieldcraft, "private bool UseAgainstGnats(SkyIslandFieldBuff buff, CharacterMainControl player, out string said)", "转交蚊群 owner")
    for token in ("return gnats.DeployZapper(player, out said);", "return gnats.SwingFan(player, out said);"):
        require(use_against, token, "转交蚊群效果必须保留成功/失败结果")
    ordered(use_consumable, ("bool applied = UseAgainstGnats(buff, player, out said);", "ReportConsumable(buff, said, applied);", "return applied;"),
            "放灯/挥扇失败必须警示、返回失败并且不记录成功使用")
    report_consumable = need_body(fieldcraft, "private void ReportConsumable(SkyIslandFieldBuff buff, string message, bool applied)", "耗材结果提示")
    ordered(report_consumable, ("session.Announce(message, !applied);",
                                'if (applied && story != null) story.LogTiming("consumable", buff.ToString());'),
            "耗材结果统一提示，失败警示且不记录成功使用")
    can_use = need_body(fieldcraft, "internal bool CanUse(SkyIslandFieldBuff buff)", "耗材可用")
    for _value, name in batch_four:
        if not any(output == name for _rid, _st, output, _c, _in, _g in recipes):
            errors.append("批次四物品 %s 没有来源：岛上没有任何配方产出它" % name)
        kind, buff = kinds.get(name, (None, None))
        if kind == "Gear":
            carried = re.search(r"(\w+)\s*=\s*owner\.CountInPack\(BossRushItemIds\.%s\)\s*>\s*0" % name, gnats)
            if not carried:
                errors.append("随身装备 %s 没有在蚊群 owner 里数背包：只能卖钱" % name)
            else:
                flag = carried.group(1)
                for call in ("BiteReady", "BiteDelay", "OrbitRadiusFor"):
                    if not re.search(r"SkyIslandMosquitoRules\.%s\([^;]*\b%s\b" % (call, flag), gnats):
                        errors.append("随身装备 %s 数到了却没改叮咬（%s 没读 %s）" % (name, call, flag))
        elif kind in ("Consumable", "Tool"):
            if squash("case SkyIslandFieldBuff.%s:" % buff) not in use_consumable:
                errors.append("%s 的效果 %s 在 UseConsumable 里没有分支：用了没用" % (name, buff))
            if not re.search(r"SkyIslandFieldBuff\.%s\)return gnats\.\w+\(" % buff, use_against):
                errors.append("%s 的效果 %s 没有转给蚊群 owner" % (name, buff))
            if not re.search(r"SkyIslandFieldBuff\.%s\)return gnats != null && gnats\.\w+;" % buff, can_use):
                errors.append("%s 的效果 %s 在 CanUse 里没问蚊群 owner（不成立时会白吃一件）" % (name, buff))
        else:
            errors.append("批次四物品 %s 的形态 %s 不认识：新形态要在这里说清楚它在岛上拿来做什么" % (name, kind))
    zapper_recipes = [gates for _rid, _st, output, _c, _in, gates in recipes if output == "SkyIslandGnatZapper"]
    if not zapper_recipes or not all(".AfterLamps(" in gates for gates in zapper_recipes):
        errors.append("风晶灭蚊灯的配方必须等岛上点亮风晶灯（苇白调灯芯）：缺 .AfterLamps(...)")
    if "WeibaiZapperLine" not in service:
        errors.append("苇白没有提灭蚊灯的台词（灭蚊灯与苇白的串联断了）")

    sample_signature = ("internal void Sample(bool isNight, int windLevel, string region, CharacterMainControl player, bool incense, "
                        "bool lanternLit, bool inSmoke, bool nearLamp, float elapsed)")
    require(gnats_s, sample_signature, "Sample 的参数顺序变了，调用方会把驱风香 / 风灯 / 烟 / 灯传错位")
    require(fieldcraft_s, "gnats.Sample(night, gale, region, player, incenseUntil > 0f, lanternUntil > 0f, "
                          "NearHearth(player.transform.position), NearLamp(player.transform.position), elapsed);",
            "夜风推进要把驱风香、风灯、灶火的烟、风晶灯按参数顺序交给蚊群 owner")
    require(fieldcraft_s, "if (gnats != null) gnats.Frame(now, Time.deltaTime, CharacterMainControl.Main);", "蚊群要逐帧推进（冲刺按帧走）")
    require(fieldcraft_s, "if (gnats != null) gnats.Dispose();", "局内 owner 销毁时要带走蚊群")
    sample = need_body(gnats, "internal void Sample(", "蚊群采样")
    for token in ("bool enemiesNear = !session.CanOpenStoryPanel(out reason);", "Scatter(position, now, windLevel, enemiesNear, inSmoke, incense);",
                  "Vector3 local = root.InverseTransformPoint(position);", "InSmoke = inSmoke", "Incense = incense", "EnemiesNear = enemiesNear",
                  "WaterEdgeDistance = SkyIslandMosquitoRules.WaterEdgeDistance(local.x, local.z)",
                  "FrogsReleased = story != null ? SkyIslandMosquitoRules.FrogsReleased(story.Current) : 0",
                  "IslandCore = SkyIslandMosquitoRules.IsIslandCore(region, local.x, local.z)", "Lantern = lanternLit", "NearLamp = nearLamp",
                  "NearZapper = NearestZapper(position, SkyIslandMosquitoRules.ZapperLureRadius) != null",
                  "float weight = SkyIslandMosquitoRules.SpawnWeight(site);", "SkyIslandMosquitoRules.RoomFor(alive,"):
        require(sample, token, "刷新要把现场的每一项都交给规则")
    spawn_weight = need_body(rules, "internal static float SpawnWeight(SkyIslandGnatSite site)", "刷新权重")
    for token in ("if (!site.Night || site.InSmoke || site.Incense || site.EnemiesNear || site.WindLevel >= 2) return 0f;",
                  "if (site.WaterEdgeDistance <= WaterReach) weight *= WaterBoost(site.FrogsReleased);",
                  "if (site.IslandCore) weight *= IslandCoreFactor;", "if (site.Lantern) weight *= LanternFactor;",
                  "if (site.NearLamp) weight *= LampFactor;", "if (site.NearZapper) weight *= ZapperFactor;"):
        require(spawn_weight, token, "刷新权重漏了一项现场条件")
    require(need_body(gnats, "private void Scatter(", "散开"), "bool leave = !night || windLevel >= 2 || enemiesNear || inSmoke || incense;",
            "天亮、二级风、敌人、烟、驱风香都要让已有的蚊群散开")
    steer = need_body(gnats, "private bool Steer(", "逐只推进")
    require(steer, "motor.Dazzled = lantern &&", "风灯照着的云蚋要晃眼（风灯的另一半用处）")
    require(steer, "if (step.sqrMagnitude <= 0f && (gnat.Leaving || motor.CanAct)) step = Cruise(",
            "巡飞必须等眩晕与喘息结束；散场仍能飞走")
    require(steer, "if (!gnat.Leaving && motor.CanAct) TryBite(", "眩晕与喘息期间不许叮咬")
    require(steer, "if (moved) gnat.Root.transform.position = gnat.Position;", "没移动不写物理目标变换")
    require(squash(rules), "internal bool CanAct { get { return Phase == SkyIslandGnatPhase.Idle && StunnedFor <= 0f; } }",
            "主动行为门必须同时检查阶段与眩晕")
    require(squash(rules), "return CanAct && !Dazzled && Budget >= 1f && Cooldown <= 0f;", "躲闪也要遵循主动行为门")
    require(squash(clean_source(read("tests/fixtures/SkyIslandStory/SkyIslandGnatDodgeSimulation.cs"))), "else if (motor.CanAct)",
            "离线模拟与运行时必须共用主动巡飞门")
    for name in ("LanternFactor", "LampFactor", "ZapperFactor"):
        if not number(rules, name) > 1:
            errors.append("%s 必须大于 1：风灯 / 风晶灯 / 灭蚊灯是招蚋的" % name)
    frog_target = number(rules, "FrogTarget")
    if not number(rules, "WaterBoostStep") > 0 or abs(number(rules, "WaterBoostMax") - number(rules, "WaterBoostStep") * frog_target - 1.0) > 1e-6:
        errors.append("近水倍率必须随放生递减、放满回到 1")

    configure = need_body(items, "private static void ConfigureItem(int typeId, Item item)", "物品配置")
    medicine = configure.split(squash("case Kind.Medicine:"), 1)[1].split("break;", 1)[0] if squash("case Kind.Medicine:") in configure else ""
    for token in ("soothe.buff = (int)SkyIslandFieldBuff.Soothe;", "AttachUsage(item, def.UseTime, drug, soothe);"):
        require(medicine, token, "星苔药膏要挂止痒行为（药膏管痒）")
    ordered(need_body(gnats, "internal string Soothe()", "药膏止痒"),
            ("ClearItch();", "sootheUntil = Time.time + SkyIslandMosquitoRules.SalveSootheSeconds;"), "药膏要止痒并给一阵防叮痒")
    remedy = need_body(gnats, "internal static void RemedyClearsItch()", "苔药止痒")
    require(remedy, "current.ClearItch();", "眠苔的苔药要顺手止痒")
    forbid(remedy, "sootheUntil", "苔药不给「一阵内再叮不痒」（那是药膏的分工）")
    ordered(services, ("player.Health.SetHealth(player.Health.MaxHealth);", "SkyIslandGnats.RemedyClearsItch();"), "苔药敷上之后才止痒")
    require(need_body(gnats, "private void TickItch(", "痒"), "SkyIslandMosquitoRules.StepItch(itch, bitesSinceSample, elapsed, now < sootheUntil)",
            "痒要读药膏的防叮痒时段")

    # ---- 3. 青蛙 ----
    obstacles = {o["id"]: o for o in layout["obstacles"]}
    markers = {m["id"]: m for m in layout["markers"]}
    water_rows = re.findall(r'MakeWater\("(\w+)",\s*(-?[\d.]+)f,\s*(-?[\d.]+)f,\s*(-?[\d.]+)f\)', rules)
    water_ids = {row[0] for row in water_rows}
    world_s = squash(world)
    for marker_id, water_id, choice in FROG_SITES:
        require(world_s, 'case "%s": %s(choices); break;' % (marker_id, choice), "蛙卵的面板选项没挂在 %s 上" % marker_id)
        marker, water = markers.get(marker_id), obstacles.get(water_id)
        if marker is None or water is None or water_id not in water_ids:
            errors.append("蛙卵地点 %s / %s 在作者布局或静水表里找不到" % (marker_id, water_id))
            continue
        min_x, min_z, max_x, max_z = water["bounds"]
        x, z = marker["position"][0], marker["position"][2]
        gap = math.hypot(max(min_x - x, 0.0, x - max_x), max(min_z - z, 0.0, z - max_z))
        if marker["island"] != water["island"] or gap > FROG_SITE_REACH:
            errors.append("蛙卵面板 %s 不在 %s 边上（岛 %s / %s，离水面框 %.1f m > %.0f m）"
                          % (marker_id, water_id, marker["island"], water["island"], gap, FROG_SITE_REACH))
    ordered(take_spawn, ("SkyIslandMosquitoRules.FrogsComplete(story.Current)", "if (carryingSpawn)", "if (!NightNow)", "if (!story.CanWrite)",
                         "owner.ConsumeOne(BossRushItemIds.SkyIslandCloudmossFiber)", "carryingSpawn = true;"),
            "捧蛙卵：放满/已捧/白天/记录只读都先拒绝，再扣纤维；不得让玩家支付注定无法交付的委托")
    release = need_body(gnats, "internal bool ReleaseSpawn(out string message)", "放回蛙卵")
    ordered(release, ("SkyIslandMosquitoRules.NextFrogNote(story.Current)", "if (!story.CanWrite)", "story.RecordNote(note, out recorded)",
                      "carryingSpawn = false;"), "放回蛙卵必须写屏障先挡、先记手记再放下")
    guarded = release.split(squash("if (!story.CanWrite)"), 1)
    if len(guarded) == 2 and squash("carryingSpawn = false;") in guarded[1].split(squash("story.RecordNote(note, out recorded)"), 1)[0]:
        errors.append("写屏障与记手记之间放下了蛙卵：存档写不进时蛙卵会凭空消失")
    require(service, "!SkyIslandMosquitoRules.IsFrogNote(id)", "手记白名单要收登记过的 Frog_n")
    is_frog = need_body(rules, "internal static bool IsFrogNote(string id)", "蛙卵手记 id")
    require(is_frog, "string.Equals(FrogNoteId(i), id, StringComparison.Ordinal)", "手记只收 Frog_1..FrogTarget，不按前缀放行")
    forbid(is_frog, "StartsWith(", "手记白名单不许按前缀放行")
    if not re.search(r'CarryingSpawn\s*\?\s*root\.transform\.Find\("Search_S1"\)', world):
        errors.append("捧着蛙卵时罗盘要指向蛙鸣池（Search_S1）")
    # 携带中的局内交付物优先于另一个收集目标：不能让未收的信鸽截走蛙卵返程指引。
    compass = need_body(world, "internal string CompassReading(Vector3 from)", "罗盘交付指引")
    ordered(compass, ('swarm.CarryingSpawn ? root.transform.Find("Search_S1")', "if (pool != null)",
                      "toPool.x, toPool.z", "if (pigeon != null)", "SkyIslandMapMarkers.ObjectiveTargets(story.Current)"),
            "罗盘必须先处理正在送回的蛙卵，再指信鸽与常规目标")
    for label, text in (("名册", crew), ("手记", journal), ("居民台词", service)):
        if "SkyIslandMosquitoRules.FrogsComplete(data)" not in text:
            errors.append("三团蛙卵放满之后%s没有回应" % label)
    require(journal, "SkyIslandMosquitoRules.FrogProgress(data)", "手记总览要写蛙鸣池的进度")

    # ---- 4. 叮咬安全 ----
    if len(re.findall(r"\.Hurt\(", gnats)) != 2:
        errors.append("SkyIslandGnats 只许两处 Hurt（叮咬 + 打蚊子），实际 %d" % len(re.findall(r"\.Hurt\(", gnats)))
    if sorted(re.findall(r"new DamageInfo\((\w*)\)", gnats)) != ["null", "player"]:
        errors.append("伤害来源必须只有叮咬的 DamageInfo(null) 与打蚊子的 DamageInfo(player)")
    ordered(need_body(gnats, "private void TryBite(", "叮咬"),
            ("SkyIslandMosquitoRules.BiteReady(now, gnat.BiteReadyAt, lastBiteAt, veilCarried)",
             "gnat.BiteReadyAt = now + SkyIslandMosquitoRules.BiteDelay(",
             "if (!SkyIslandMosquitoRules.HealthAllowsBite(health.CurrentHealth, health.MaxHealth)) return;", "lastBiteAt = now;",
             "DamageInfo bite = new DamageInfo(null);", "bite.damageValue = SkyIslandMosquitoRules.BiteDamage;",
             "bite.damageType = DamageTypes.realDamage;", "bite.ignoreDifficulty = true;", "health.Hurt(bite);"),
            "叮咬必须先判到点与血量下限再下嘴，一口真伤、不随难度")
    require(need_body(rules, "internal static bool HealthAllowsBite(float currentHealth, float maxHealth)", "血量下限"),
            "return maxHealth > 0f && currentHealth > maxHealth * BiteHealthFloor && currentHealth - BiteDamage > 0f;", "云蚋叮不死人")
    if number(rules, "BiteDamage") != 1 or not 0 < number(rules, "BiteHealthFloor") < 1:
        errors.append("叮咬一口 1 点、血量下限在 0–1 之间")
    if not 1.5 <= number(rules, "BiteIntervalMin") < number(rules, "BiteIntervalMax") <= 2.5 or not number(rules, "GlobalBiteGap") > 0:
        errors.append("叮咬间隔必须在 1.5–2.5 秒、且有全局间隔")
    if not 0 < number(rules, "MaxAlive") <= 6 or number(rules, "GroupMin") != 2 or number(rules, "GroupMax") != 4:
        errors.append("同时至多 6 只、一群 2–4 只")
    gnat_health = number(rules, "GnatHealth")
    if not 0 < gnat_health <= 3 or number(rules, "FanDamage") < gnat_health or number(rules, "ZapperDamage") < gnat_health:
        errors.append("云蚋 1–3 下打死，蒲扇扑落与灭蚊灯电落都要一下打死")
    if not number(rules, "MaxDash") <= 3 or not 20 <= number(rules, "DashSpeed") <= 25:
        errors.append("侧闪冲刺至多 3 米、速度 20–25 m/s")
    for token in ("ItemAssetsCollection", "SkyIslandLootPools", "InstantiateItem", ".Drop(", "OnDeadEvent.AddListener"):
        if token in gnats:
            errors.append("云蚋被打死不掉东西（出现了 %s）" % token)

    # ---- 5. 蚊群目标与弹道钩子 ----
    require(gnats_s, 'receiverLayer = LayerMask.NameToLayer("DamageReceiver");', "云蚋要放在伤害接收体层")
    ordered(need_body(gnats, "private bool Spawn(Vector3 at, float now)", "生成云蚋"),
            ('go = new GameObject("SkyIslandGnat");', "go.SetActive(false);", "if (receiverLayer >= 0) go.layer = receiverLayer;",
             "go.AddComponent<SphereCollider>()", "collider.isTrigger = false;", "body.isKinematic = true;", "receiver.useSimpleHealth = true;",
             "health.team = Teams.wolf;", "health.maxHealthValue = SkyIslandMosquitoRules.GnatHealth;", "receiver.simpleHealth = health;",
             "go.SetActive(true);"),
            "云蚋必须先建成失活、配齐可被打中的组件再激活（HealthSimpleBase.Awake 立刻取接收体）")
    frame = need_body(gnats, "internal void Frame(", "逐帧")
    require(frame, "if (moved) Physics.SyncTransforms();", "挪完蚊群要同步物理变换，否则子弹扫不到新位置")
    ordered(frame, ("!HasFrameWork()) return;", "Quaternion view = ViewRotation();", "if (alive > 0) ReadAim(player, out muzzle, out aim);"),
            "空闲帧必须先退出，只有活着的蚊群才读准星与枪口")
    require(frame, "moved |= Steer(", "只有蚊群确实移动才同步物理变换")
    idle = need_body(gnats, "private bool HasFrameWork()", "空闲帧门")
    for token in ("alive > 0", "buzzing", "fanArc.enabled", "zappers[i] != null", "splats[i].Root.activeSelf"):
        require(idle, token, "空闲帧门必须保留仍在播放或需要收尾的表现")
    deploy = need_body(gnats, "internal bool DeployZapper(", "放置灭蚊灯")
    ordered(deploy, ("if (!Physics.Raycast(", "message = SkyIslandMosquitoRules.ZapperNoGround;", "return false;",
                     'go = new GameObject("SkyIslandGnatZapper");', "go.transform.position = hit.point;", "zappers[slot] = new Zapper"),
            "无地面不能放灯：必须在创建与占槽前返回失败，由耗材调用方保留物品")
    ordered(deploy, ("catch (Exception e)", "zappers[slot] = null;", "UnityEngine.Object.Destroy(go);", 'Fail("zapper", e);', "return false;"),
            "灭蚊灯创建失败要清理占槽与半成品并返回失败")
    ordered(need_body(gnats, "internal void OnProjectile(", "弹道预测"),
            ("if (!Usable || alive == 0 || projectile == null) return;",
             "SkyIslandMosquitoRules.ShouldTrackProjectile(mine, context.gravity, context.explosionRange)",
             "context.firstFrameCheck ? context.firstFrameCheckStartPoint : projectile.transform.position", "gnat.Motor.OnShot("),
            "弹道预测只看主角自己的直线弹、起点对齐官方首帧扫掠")
    require(need_body(rules, "internal static bool ShouldTrackProjectile(bool fromMainCharacter, float gravity, float explosionRange)", "弹道筛选"),
            "return fromMainCharacter && !(gravity > 0f) && !(explosionRange > 0f);", "抛物线弹与爆炸弹不躲（爆炸必须打得中）")
    require(patch, '[HarmonyPatch(typeof(Projectile), "Init", new Type[] { typeof(ProjectileContext) })]', "弹道钩子必须挂在 Projectile.Init(ProjectileContext)")
    ordered(patch, ("[HarmonyPostfix]", "SkyIslandGnats swarm = SkyIslandGnats.Current;", "if (swarm == null) return;",
                    "swarm.OnProjectile(__instance, _context);"), "弹道钩子是后缀、先判空再转交")
    for signature in HOT_PATH:
        body = body_of(gnats, signature)
        if body is None:
            errors.append("找不到逐帧路径 %s" % signature)
            continue
        for token in HOT_PATH_FORBIDDEN:
            if token in body:
                errors.append("逐帧路径 %s 里出现了 %s（查场景 / 分配 / 闭包）" % (signature, token))
    for token in ("using System.Linq", "Time.unscaled", "OnGUI", "Interactable"):
        if token in gnats_raw.replace("\r", "") and token in gnats:
            errors.append("SkyIslandGnats 里出现了 %s（玩法计时走游戏时间、不自绘 HUD、不新增交互体）" % token)

    # ---- 6. 灶火看得见 ----
    ordered(need_body(fieldcraft, "private void AddFire(string markerName, Color color)", "灶火与风晶灯"),
            ("if (marker == null)", "Debug.LogWarning(", "return;", "SkyIslandHearthFx.FindSpot(root, marker, groundMask);",
             "go.transform.position = spot + Vector3.up * 1.2f;", "SkyIslandHearthFx.Build(go.transform, spot);",
             "Light light = go.AddComponent<Light>();", "if (hearth) hearthFires.Add(light);", "else lampFires.Add(light);"),
            "锚点缺失要打警告；灶火的火与烟生在装置旁、点光挪过去；灶火与风晶灯分表")
    require(need_body(fieldcraft, "private bool NearHearth(Vector3 position)", "灶火的烟"),
            "return NearAny(hearthFires, position, SkyIslandMosquitoRules.SmokeRadius);", "烟的判定只看灶火")
    require(need_body(fieldcraft, "private bool NearLamp(Vector3 position)", "风晶灯附近"),
            "return NearAny(lampFires, position, SkyIslandMosquitoRules.LampRadius);", "招蚋的灯只看风晶灯")
    hearth_s = squash(hearth)
    for token in ('Emitter(fx.transform, "Flame",', 'Emitter(fx.transform, "Smoke",', "RingParticleEffect.GetSharedParticleMaterial()",
                  "SkyIslandRewardCrate.TryFindCratePosition("):
        require(hearth_s, token, "灶火要有火苗与烟、复用共享粒子材质与经过几何回归的放置算法")
    for token in ("Collider", "Interactable"):
        if token in hearth:
            errors.append("灶火的火与烟不许带 %s（不挡路、不参与交互竞争）" % token)

    # ---- 7. 美术与声音 ----
    spec = importlib.util.spec_from_file_location("gen_sky_island_gnat_sprites", ROOT / "tools/gen_sky_island_gnat_sprites.py")
    sprites = None
    try:
        sprites = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(sprites)
    except Exception as e:  # noqa: BLE001 - 缺 Pillow 也要报成守卫失败而不是崩
        errors.append("像素画生成脚本导入失败：%s" % e)
    if sprites is not None:
        sheet = sprites.render()
        errors.extend("像素画：" + message for message in sprites.check_sheet(sheet))
        frame_consts = dict(re.findall(r"\bFrame([A-Z]\w*)\s*=\s*(\d+)", gnats))
        frame_consts.pop("Count", None)
        frame_consts.pop("Pixels", None)
        mapped = {snake(name): int(index) for name, index in frame_consts.items()}
        if mapped != {name: i for i, name in enumerate(sprites.FRAME_NAMES)}:
            errors.append("SkyIslandGnats 的帧常量与生成脚本的帧序对不上：%r / %r" % (mapped, sprites.FRAME_NAMES))
        if number(gnats, "FrameCount", "SkyIslandGnats") != len(sprites.FRAME_NAMES) or number(gnats, "FramePixels", "SkyIslandGnats") != sprites.FRAME:
            errors.append("SkyIslandGnats 的帧数 / 帧边长与生成脚本不一致")
        sheet_file = re.search(r'SheetFile\s*=\s*"([^"]+)"', gnats)
        if not sheet_file or sheet_file.group(1) != sprites.OUT.name or '"Assets/ui/SkyIsland"' not in gnats:
            errors.append("SkyIslandGnats 读的精灵表与生成脚本的产物不是同一个文件")
        if sprites.OUT.exists():
            from PIL import Image
            on_disk = Image.open(sprites.OUT).convert("RGBA")
            if on_disk.size != sheet.size or on_disk.tobytes() != sheet.tobytes():
                errors.append("本地 %s 与脚本重画结果不一致（手改过或脚本改了没重跑）" % sprites.OUT.relative_to(ROOT))
    for token in ("texture.filterMode = FilterMode.Point;", 'Shader.Find("Sprites/Default")'):
        require(gnats_s, token, "像素画要点采样、用精灵着色器")
    require(need_body(gnats, "private static bool EnsureArt()", "精灵表"), "ModBehaviour.CriticalLog(", "缺精灵表要硬失败并报出来")
    require(gnats_s, "usable = EnsureArt();", "缺精灵表时整趟不刷云蚋（看不见却会叮人的蚊子比没有更糟）")
    sfx = read("tools/gen_sky_island_sfx.py")
    buzz = re.search(r'BuzzFile\s*=\s*"([^"]+)"', gnats)
    loops = re.search(r"LOOPS\s*=\s*\{([^}]*)\}", sfx)
    if not buzz or '"%s"' % buzz.group(1) not in sfx or not loops or buzz.group(1) not in loops.group(1):
        errors.append("嗡声文件没登记进音效生成脚本，或没登记为无缝循环")
    if '"Assets/Sounds/SkyIsland"' not in gnats or 'Assets/Sounds/SkyIsland"' not in sfx:
        errors.append("嗡声的读取目录与音效生成脚本的产物目录不一致")
    if number(gnats, "BuzzRange", "SkyIslandGnats") != 15:
        errors.append("嗡声只在 15 米内响")

    # ---- 8. 布局表 ----
    if not water_rows:
        errors.append("静水表没解析到（正则与源码失步）")
    for water_id, x, z, radius in water_rows:
        obstacle = obstacles.get(water_id)
        if obstacle is None:
            errors.append("静水 %s 在作者布局 obstacles 里找不到" % water_id)
            continue
        anchor = obstacle.get("position") or obstacle["center"]
        min_x, min_z, max_x, max_z = obstacle["bounds"]
        expected = math.hypot(max_x - min_x, max_z - min_z) / 2
        if abs(float(x) - anchor[0]) > 0.01 or abs(float(z) - anchor[2]) > 0.01 or abs(float(radius) - expected) > 0.01:
            errors.append("静水 %s 与布局对不上：表 (%s, %s, r %s) / 布局 (%.3f, %.3f, r %.3f)"
                          % (water_id, x, z, radius, anchor[0], anchor[2], expected))
    pools = {o["id"] for o in layout["obstacles"] if o["kind"] == "pool"}
    if not pools <= water_ids:
        errors.append("作者布局里的池子 %r 没进静水表" % sorted(pools - water_ids))
    island_rows = re.findall(r'MakeIsland\("(\w+)",\s*(-?[\d.]+)f,\s*(-?[\d.]+)f,\s*(-?[\d.]+)f\)', rules)
    islands = {i["id"]: i for i in layout["islands"]}
    if {row[0] for row in island_rows} != set(islands):
        errors.append("岛框表与作者布局的岛不是同一组：%r / %r" % (sorted(row[0] for row in island_rows), sorted(islands)))
    for region, x, z, half in island_rows:
        island = islands.get(region)
        if island is None:
            continue
        if abs(float(x) - island["center"][0]) > 0.01 or abs(float(z) - island["center"][2]) > 0.01 or \
                abs(float(half) - min(island["size"]) / 2) > 0.01:
            errors.append("岛框 %s 与布局对不上：表 (%s, %s, %s) / 布局 %r %r" % (region, x, z, half, island["center"], island["size"]))

    # ---- 9. 会话主文件与手工验收 ----
    session_raw = read(SKY + "SkyIslandSession.cs")
    session_lines = len(session_raw.splitlines())
    if session_lines > SESSION_LINE_BUDGET:
        errors.append("SkyIslandSession.cs %d 行，超过 %d 行预算" % (session_lines, SESSION_LINE_BUDGET))
    if re.search(r"SkyIslandGnat|SkyIslandMosquito|SkyIslandNight|SkyIslandHearthFx", clean_source(session_raw)):
        errors.append("会话主文件不许接云蚋（owner 在局内 owner 与蚊群 owner）")
    coverage = json.loads(read("Assets/Data/GameplayCoverage.json"))
    manual = {m["id"]: m for feature in coverage.get("features", []) for m in feature.get("manual", [])}
    entry = manual.get("M_SKY_ISLAND_13")
    if entry is None:
        errors.append("GameplayCoverage 缺手工验收 M_SKY_ISLAND_13（云蚋）")
    else:
        steps = entry.get("steps", "")
        first = steps.split("②", 1)[0]
        if "②" not in steps or not all(token in first for token in ("夜里", "云蚋", "看得见")):
            errors.append("M_SKY_ISLAND_13 的第一项必须是「夜里看不看得见云蚋」")
        for token in ("躲", "叮", "驱风香", "风灯", "蒲扇", "灭蚊灯", "纱笠", "药膏", "苔药", "蛙卵", "英文"):
            if token not in steps:
                errors.append("M_SKY_ISLAND_13 的步骤没覆盖「%s」" % token)
        if not entry.get("expected"):
            errors.append("M_SKY_ISLAND_13 缺预期结果")

    if errors:
        print("SkyIslandMosquitoGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("SkyIslandMosquitoGuard: PASS (批次四 %d 件 TypeID %d–%d 各有来源与用处 / %d 种材料各 ≥2 处消耗 / 判夜单一口径 / "
          "叮咬先判下限 / 蚊群目标与弹道钩子 / 灶火生烟 / 静水 %d + 岛框 %d 对齐布局 / 精灵表 %d 帧自检)"
          % (len(batch_four), numbers[0], numbers[-1], len(materials), len(water_rows), len(island_rows),
             len(sprites.FRAME_NAMES) if sprites is not None else 0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
