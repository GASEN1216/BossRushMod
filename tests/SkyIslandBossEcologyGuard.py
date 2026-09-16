"""天空岛头目 / 岛主（R1 残星匠首、瞭台观星手；R2–R4 悬根猎首、截信人、穗镰、听雨人、蚋笛翁、镜中客、断风三游猎）的结构不变式。

行为由执行回归 SkyIslandStory（SkyIslandBossRulesRegression：掉落精确计频、档次单调、逐圈逃圈速度、防换皮、几何与套装规则）、
SkyIslandEncounters（夜限定带队、换阵营、叫帮手）与 F3 只读用例 SKY_BOSS_PROFILES 验证；这里只钉「接线在不在、顺序对不对、没有走回龙王那条老路」：

1. 档案挂在已有自动组的带队位上：内容表（内置表 + World.json）各组带队档次与档案一致，id / marker / count 不变；
   夜限定（蚋笛翁、镜中客）与换阵营（断风游猎）只是档案上的旗标。
2. 身份层先分派给 SkyIslandBossForge，再走普通档次装饰；具名剧情对手（折翎、钟守）与噬风的分支原样在前。
   夜限定在遭遇层等夜（带队位留着、白天不刷、清场后夜里补刷），Forge 不因为白天跳过装配；断风游猎整组换 Teams.bear。
3. 配装即掉落：
   - 配装先问 prefab 再实例化（缺资源时官方回空壳），刷新模型用 ForceInvokeSlotContentChangedEvent，不重复调 SetItem；
   - 掉落只挂该角色实例的 BeforeCharacterSpawnLootOnDead（官方建箱之前），订阅与退订成对，结算有一次性闩；
   - 抽样走纯规则 RollDrop + System.Random 种子，不用 UnityEngine.Random；没抽中的配装 Unplug 后 DestroyTree；
   - 头目 / 岛主的文件里不出现 BossRush 奖励箱追踪、OnDead 额外掉落处理器或手工建箱（龙王难管的三条老路）。
4. 招式控制器：一种一个控制器且都由 Forge 分派；只订自己身上的 Health 事件并在 OnDestroy 退订（枪声静态事件同样成对）；
   范围伤害走官方爆炸且 buff 通道、不伤自己；预警时长一律过 TelegraphSeconds（静听耳罩）；面罩 / 耳机照官方口径在暴击时磨耐久；
   换位先掐寻路再落位再同步物理；倒影烤网格、不克隆角色。
5. 剧情与岛上用处：击败事件的订阅幂等、在 Dispose 退订；首杀手记 id 进 RecordNote 白名单；镜纹甲是剧情数据的运行时字段；
   旧邮包的第二封信、苔纱面罩挡躲闪、笛声压过灭蚊灯、悬根猎装翻箱、蓑衣农装割草、断风套走桥都有接线。
6. 物品登记：十七件专属装备的常量、中英名、正价值、不进 AllTypeIds、掉落黑名单（代码 + JSON）、动态注册计划（bundle + 克隆兜底）、
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
    "props": SKY + "SkyIslandBossProps.cs",
    "foreman": SKY + "SkyIslandForemanBoss.cs",
    "stargazer": SKY + "SkyIslandStargazerChief.cs",
    "roothunter": SKY + "SkyIslandRootHunterBoss.cs",
    "waylayer": SKY + "SkyIslandWaylayerChief.cs",
    "sickle": SKY + "SkyIslandSickleBoss.cs",
    "listener": SKY + "SkyIslandListenerChief.cs",
    "piper": SKY + "SkyIslandPiperChief.cs",
    "mirror": SKY + "SkyIslandMirrorChief.cs",
    "windhunter": SKY + "SkyIslandWindhunterChief.cs",
    "session": SKY + "SkyIslandSessionBosses.cs",
    "world_bosses": SKY + "SkyIslandWorldStoryBosses.cs",
    "fieldcraft_gear": SKY + "SkyIslandFieldcraftBossGear.cs",
    "fieldcraft": SKY + "SkyIslandFieldcraft.cs",
    "gnats": SKY + "SkyIslandGnats.cs",
    "gnats_lure": SKY + "SkyIslandGnatsLure.cs",
    "reward_crate": SKY + "SkyIslandRewardCrate.cs",
    "letters": SKY + "SkyIslandLetters.cs",
    "story_rules": SKY + "SkyIslandStoryRules.cs",
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

CONTROLLERS = {
    # 档案 Kind -> (路径键, 控制器类型)
    "Foreman": ("foreman", "SkyIslandForemanBoss"),
    "Stargazer": ("stargazer", "SkyIslandStargazerChief"),
    "RootHunter": ("roothunter", "SkyIslandRootHunterBoss"),
    "Waylayer": ("waylayer", "SkyIslandWaylayerChief"),
    "Sickle": ("sickle", "SkyIslandSickleBoss"),
    "Listener": ("listener", "SkyIslandListenerChief"),
    "Piper": ("piper", "SkyIslandPiperChief"),
    "Mirror": ("mirror", "SkyIslandMirrorChief"),
    "Windhunter": ("windhunter", "SkyIslandWindhunterChief"),
}
CONTROLLER_KEYS = tuple(key for key, _ in CONTROLLERS.values())

NEW_SOURCES = ("rules", "forge", "loot", "props", "session", "world_bosses", "fieldcraft_gear", "gear_config", "gnats_lure") + CONTROLLER_KEYS

# 遭遇 id -> (marker, 带队档次, 档案 Kind)。id / marker / 人数与 R1 之前一致，只换带队档次。
PROFILES = {
    "G": ("EnemySpawn_G", "Lord", "Foreman"),
    "S4": ("EnemySpawn_S4", "Chief", "Stargazer"),
    "D": ("EnemySpawn_D", "Lord", "RootHunter"),
    "S2": ("EnemySpawn_S2", "Chief", "Waylayer"),
    "C": ("EnemySpawn_C", "Lord", "Sickle"),
    "S3": ("EnemySpawn_S3", "Chief", "Listener"),
    "S1": ("EnemySpawn_S1", "Chief", "Piper"),
    "F": ("Search_F_02", "Chief", "Mirror"),
    "K1_Relay": ("Relay_K1", "Chief", "Windhunter"),
    "K2_Relay": ("Relay_K2", "Chief", "Windhunter"),
    "K3_Relay": ("Relay_K3", "Chief", "Windhunter"),
}
NIGHT_ONLY = ("S1", "F")
RIVAL_FACTION = ("K1_Relay", "K2_Relay", "K3_Relay")

GEAR = {
    "SkyIslandStarbrassVisorHelm": 500086,
    "SkyIslandStarfurnaceHarness": 500087,
    "SkyIslandStarfurnacePack": 500088,
    "SkyIslandStargazerLensHelm": 500089,
    "SkyIslandRootweaveMask": 500090,
    "SkyIslandVinewovenCuirass": 500091,
    "SkyIslandHangrootQuiver": 500092,
    "SkyIslandOldMailbag": 500093,
    "SkyIslandGreenearStrawHat": 500094,
    "SkyIslandStrawRaincoat": 500095,
    "SkyIslandGrainSack": 500096,
    "SkyIslandRainhushEarmuffs": 500097,
    "SkyIslandMossgauzeMask": 500098,
    "SkyIslandMirrorgrainPlate": 500099,
    "SkyIslandWindbreakHood": 500100,
    "SkyIslandWindbreakMantle": 500101,
    "SkyIslandWindbreakPack": 500102,
}

# 面罩 / 耳机：官方只磨头盔与护甲，这几位的控制器必须照同一口径自己磨（控制器键 -> (槽位, 常量)）。
SOFT_PIECES = {
    "roothunter": ("FaceMask", "SkyIslandRootweaveMask"),
    "listener": ("Headset", "SkyIslandRainhushEarmuffs"),
    "piper": ("FaceMask", "SkyIslandMossgauzeMask"),
}

BOSS_FILES = ("rules", "forge", "loot", "props", "world_bosses", "fieldcraft_gear") + CONTROLLER_KEYS
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


def profile_block(rules, encounter_id):
    """档案表里这一组的 `new SkyIslandBossProfile { ... }` 初始化块（剥过注释的源码）。"""
    match = re.search(r'new SkyIslandBossProfile\s*\{\s*EncounterId = "%s",' % re.escape(encounter_id), rules)
    if not match:
        return ""
    return body_of(rules[match.start():], "new SkyIslandBossProfile") or ""


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
    try:
        encounters = {e["id"]: e for e in json.loads(raw["world_json"])["encounters"]}
    except (ValueError, KeyError, TypeError):
        encounters = None
        errors.append("World.json 解析失败")
    for encounter_id, (marker, tier, kind) in PROFILES.items():
        require(errors, code["content"],
                'Encounter("%s", "%s", 3, false, SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.%s)' % (encounter_id, marker, tier),
                "内置表 %s 组带队是%s" % (encounter_id, "岛主" if tier == "Lord" else "头目"))
        if encounters is not None and encounters.get(encounter_id, {}).get("lead") != tier:
            errors.append("World.json 的 %s 带队档次与内置表不一致（严格绑定下整张表会回退）" % encounter_id)
        pattern = (r'EncounterId = "%s", Index = 0, Kind = SkyIslandBossKind\.%s, Tier = SkyIslandEnemyTier\.%s'
                   % (re.escape(encounter_id), kind, tier))
        if not re.search(pattern, code["rules"]):
            errors.append("档案表缺 %s#0 → %s（%s）" % (encounter_id, kind, tier))
        block = profile_block(code["rules"], encounter_id)
        night = "NightOnly = true" in squash(block).replace("NightOnly=true", "NightOnly = true")
        rival = "RivalFaction = true" in squash(block).replace("RivalFaction=true", "RivalFaction = true")
        if night != (encounter_id in NIGHT_ONLY):
            errors.append("档案 %s 的夜限定旗标不对（只有蚋笛翁与镜中客夜里才出来）" % encounter_id)
        if rival != (encounter_id in RIVAL_FACTION):
            errors.append("档案 %s 的换阵营旗标不对（只有断风游猎整组换阵营）" % encounter_id)
    if len(re.findall(r"new SkyIslandBossProfile\b", code["rules"])) != len(PROFILES):
        errors.append("档案表的条数与本守卫的名册不一致：新增 / 删除档案要同步 PROFILES")
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
        errors.append("有两位 Boss 的核心招式写成了同一句：同一条管线不许只换参数")

    # ---- 2. 身份层分派顺序、夜限定与换阵营 ----
    identity = body_of(code["encounters"], "private void ApplyIdentity(CharacterMainControl created, Encounter encounter, int index, SkyIslandEnemyTier tier)")
    ordered(errors, identity, ['SkyIslandEnemyTiers.ApplyStoryChampion(created, "bellkeeper"',
                               "if (SkyIslandBossForge.TryApply(created, encounter.Id, index, BossContext())) return;",
                               "SkyIslandEnemyTiers.Apply(created, tier);", "AddComponent<SkyIslandStormBoss>().Bind("],
            "身份层：具名对手在前、头目 / 岛主分派其次、普通档次装饰与噬风在后")
    spawn = body_of(code["encounters"], "private async void Spawn(Encounter encounter)")
    ordered(errors, spawn, ["if (actor.Died || actor.Life != null) continue;", "if (LeadWaiting(encounter, i)) continue;",
                            "FindGround(encounter.Marker, i)"],
            "夜限定带队白天不刷：跳过要排在落点与创建之前（位置留着，夜里补刷）")
    ordered(errors, spawn, ["created.SetTeam(Teams.wolf);", "if (encounter.RivalFaction) created.SetTeam(Teams.bear);",
                            "ApplyIdentity(created, encounter, i, tier);"],
            "断风游猎整组换阵营：敌对安全网之后、身份层之前")
    require(errors, body_of(code["encounters"], "internal void Tick()"), "(encounter.Cleared && !NightLeadDue(encounter))",
            "白天清过场的组夜里还要把夜限定带队单独补出来")
    require(errors, body_of(code["encounters"], "private static int CountMissing(Encounter encounter)"), "LeadWaiting(encounter, i)",
            "等夜的带队不算缺人（否则每拍都选中这一组、饿死别的组）")
    require(errors, body_of(code["forge"], "internal static bool LeadWaitsForNight(string encounterId)"),
            "SkyIslandNight.IsNight(SkyIslandLighting.ClockHours())", "判夜只有一个口径")
    if "NightOnly" in (body_of(code["forge"], "internal static bool TryApply(") or ""):
        errors.append("Forge 装配不许因为夜限定跳过：自动组每个位置每趟只刷一次，跳过就把带队位烧成白板拾荒者")

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
    binder = body_of(code["forge"], "private static void BindController(")
    for kind, (key, type_name) in CONTROLLERS.items():
        ordered(errors, binder, ["case SkyIslandBossKind.%s:" % kind, "AddComponent<%s>().Bind(created, profile, context);" % type_name, "break;"],
                "Forge 要把 %s 分派给 %s" % (kind, type_name))
        source = code[key]
        destroy = body_of(source, "private void OnDestroy()")
        require(errors, source, "health.OnDeadEvent.AddListener(OnDead);", PATHS[key] + " 要订自己的死亡事件")
        require(errors, destroy, "health.OnDeadEvent.RemoveListener(OnDead);", PATHS[key] + " 要在 OnDestroy 退订死亡事件")
        require(errors, source, "SkyIslandBossForge.RaiseDefeated(profile, position);", PATHS[key] + " 倒下时派发击败事件")
        require(errors, source, "SkyIslandBossRules.TelegraphSeconds(", PATHS[key] + " 的预警时长要过 TelegraphSeconds（静听耳罩让所有头目的预警更久）")
        if squash("health.OnHurtEvent.AddListener(OnHurt);") in squash(source):
            require(errors, destroy, "health.OnHurtEvent.RemoveListener(OnHurt);", PATHS[key] + " 的受击回调要在 OnDestroy 退订")
        if squash("ItemAgent_Gun.OnMainCharacterShootEvent += OnPlayerShot;") in squash(source):
            require(errors, destroy, "ItemAgent_Gun.OnMainCharacterShootEvent -= OnPlayerShot;", PATHS[key] + " 的枪声静态事件要在 OnDestroy 退订")
            require(errors, body_of(source, "private void OnDead(DamageInfo damage)"), "ItemAgent_Gun.OnMainCharacterShootEvent -= OnPlayerShot;",
                    PATHS[key] + " 倒下时就退订枪声静态事件")
    for key, (slot, constant) in SOFT_PIECES.items():
        require(errors, code[key], "health.OnHurtEvent.AddListener(OnHurt);", PATHS[key] + " 要订受击回调来磨%s" % slot)
        require(errors, body_of(code[key], "private void OnHurt(DamageInfo damage)"),
                'SkyIslandBossProps.WearSoftPiece(boss, damage, "%s", BossRushItemIds.%s);' % (slot, constant),
                PATHS[key] + " 的%s要照官方口径在暴击时磨耐久" % slot)
    detonate = body_of(code["forge"], "internal static void Detonate(CharacterMainControl source, Vector3 origin, float radius, float damageValue)")
    require(errors, detonate, "damage.isFromBuffOrEffect = true;", "Boss 范围伤害走 buff 通道")
    require(errors, detonate, "CreateExplosion(origin, radius, damage, ExplosionFxTypes.normal, 0f, false);", "Boss 范围伤害 canHurtSelf:false")
    teleport = body_of(code["props"], "internal static bool Teleport(CharacterMainControl character, Vector3 target, BossAIController pause)")
    ordered(errors, teleport, ["path.seeker.CancelCurrentPathRequest(true);", "pause.Pause();", "character.SetPosition(target);",
                               "Physics.SyncTransforms();"], "换位：先掐在途寻路请求、再暂停、再落位、最后同步物理")
    wear = body_of(code["props"], "internal static void WearSoftPiece(CharacterMainControl boss, DamageInfo damage, string slotKey, int typeId)")
    for token in ("damage.crit <= 0", "damage.damageType == DamageTypes.realDamage", "damage.ignoreArmor", "damage.armorBreak"):
        require(errors, wear, token, "面罩 / 耳机磨耐久要照官方磨头盔的口径（暴击、非真实伤害、不无视护甲、按 armorBreak）")
    decoy = body_of(code["props"], "internal static GameObject CreateDecoy(")
    require(errors, decoy, "skin.BakeMesh(mesh, true);", "倒影把蒙皮网格烤成静态网格")
    if decoy is None or "Instantiate(" in decoy:
        errors.append("倒影不许克隆角色（会把身上的装备 Item / ItemAgent 一起复制出来）")
    slow = body_of(code["props"], "internal void Apply(CharacterMainControl player, float percent, float seconds)")
    for stat in ("ZombieModeStatNames.WalkSpeed", "ZombieModeStatNames.RunSpeed"):
        require(errors, slow, stat, "玩家减速只改官方 WalkSpeed / RunSpeed（MoveSpeed 是动画参数）")

    # ---- 5. 剧情与岛上用处 ----
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
    require(errors, body_of(code["forge"], "internal static void ResetStaticCaches()"), "SkyIslandBossGearWorn.Reset();",
            "模块销毁兜底复位主角穿戴快照")
    require(errors, raw["story_rules"], "[NonSerialized] internal bool wearsMirrorArmor;", "镜纹甲是剧情数据的运行时字段（不进存档）")
    require(errors, body_of(code["story_rules"], "internal SkyIslandStoryData Copy()"), "wearsMirrorArmor = wearsMirrorArmor",
            "候选状态从 Copy 来：运行时字段漏拷就等于没穿")
    require(errors, code["story_rules"], "else if (!source.wearsMirrorArmor && !source.Has(SkyIslandStoryFlag.OldLetter | SkyIslandStoryFlag.RouteChart))",
            "穿着镜纹甲去见折翎，不带旧信与航路图也能和解")
    require(errors, code["letters"], "internal static SkyIslandLetter NextSameRaidFor(SkyIslandStoryData data, bool mailbagCarried)",
            "旧邮包的第二封信走纯规则重载")
    require(errors, body_of(code["world"], "private void RearmPigeonIfStoryLetterWaiting()"),
            "if (SkyIslandLetters.NextSameRaidFor(story.Current) == null && MailbagLetterThisRaid() == null) return;",
            "收信之后先走原口径，原口径不给才轮到旧邮包（每趟多一封）")
    ordered(errors, body_of(code["world_bosses"], "private SkyIslandLetter MailbagLetterThisRaid()"),
            ["SkyIslandBossGearWorn.Mailbag", "SkyIslandLetters.NextSameRaidFor(story.Current, true)", "mailbagLetterUsed = true;"],
            "旧邮包每趟只多送一封")
    sample = body_of(code["fieldcraft_gear"], "private void SampleBossGear(CharacterMainControl player)")
    for slot in ("Helmat", "Armor", "Backpack", "FaceMask", "Headset"):
        require(errors, sample, 'TypeIn(item, "%s")' % slot, "穿戴快照要读主角五个装备槽")
    require(errors, sample, "data.wearsMirrorArmor = SkyIslandBossGearWorn.MirrorPlate;", "镜纹甲按穿戴采样写进剧情数据的运行时字段")
    require(errors, body_of(code["fieldcraft_gear"], "private void DisposeBossGear()"), "SkyIslandBossGearWorn.Reset();", "离岛复位穿戴快照")
    require(errors, body_of(code["fieldcraft"], "internal void Tick()"), "TickBossGear(player, now, elapsed);", "局内 owner 按节拍采样穿戴")
    require(errors, body_of(code["fieldcraft"], "public void Dispose()"), "DisposeBossGear();", "离岛摘掉断风套加成与星标")
    require(errors, body_of(code["fieldcraft"], "private bool Harvest(SkyIslandGatherNode node)"), "WithSickleBonus(node,", "蓑衣农装割青穗草多一份")
    require(errors, code["reward_crate"], "SkyIslandBossRules.HunterIslandExtraRoll(", "悬根猎装翻箱出特产的机会翻倍")
    require(errors, body_of(code["gnats"], "private bool Steer("), "!SkyIslandBossGearWorn.MossgauzeMask", "苔纱面罩：身边云蚋不躲枪口")
    require(errors, body_of(code["gnats"], "private Vector3 Cruise("), "LureOverridesZappers(now) ? null : NearestLure(gnat.Position)",
            "笛声压着的这几秒云蚋不理灭蚊灯")
    require(errors, body_of(code["gnats"], "internal void Sample("), "if (HostileLureActive) enemiesNear = false;", "笛声压着的这几秒附近有敌人也不散群")

    # ---- 6. 物品登记 ----
    all_type_ids = code["item_rules"].split("internal static readonly int[] AllTypeIds", 1)[-1].split("};", 1)[0]
    gear_type_ids = code["rules"].split("internal static readonly int[] AllGearTypeIds", 1)[-1].split("};", 1)[0]
    registry_plan = code["registry"]
    blacklist_json = json.loads(raw["blacklist_json"]).get("itemIds", [])
    if len(re.findall(r"BossRushItemIds\.\w+", gear_type_ids)) != len(GEAR):
        errors.append("SkyIslandBossRules.AllGearTypeIds 的件数与本守卫的专属装备表不一致")
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
        if "case BossRushItemIds.%s:" % name not in code["gear_config"]:
            errors.append("SkyIslandBossGearConfig.Description 缺 %s 的玩家描述（从哪来、穿上做什么）" % name)
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
        ("encounters", "if (LeadWaiting(encounter, i)) continue;", "", "夜限定带队白天也刷"),
        ("encounters", "if (encounter.RivalFaction) created.SetTeam(Teams.bear);", "", "断风游猎不换阵营"),
        ("encounters", "(encounter.Cleared && !NightLeadDue(encounter))", "encounter.Cleared", "清场后夜里不补刷夜限定带队"),
        ("loot", "boss.BeforeCharacterSpawnLootOnDead += OnBeforeLoot;", "", "掉落不挂建箱前事件"),
        ("loot", "boss.BeforeCharacterSpawnLootOnDead -= OnBeforeLoot;", "", "掉落不退订"),
        ("loot", "resolved = true;", "", "掉落结算不上闩"),
        ("forge", "if (prefab == null) { reason = \"prefab_missing:\" + piece.TypeId; return false; }", "", "配装不先问 prefab"),
        ("forge", "slots[i].ForceInvokeSlotContentChangedEvent();", "slots[i].ForceInvokeSlotContentChangedEvent(); created.GetComponent<CharacterEquipmentController>().SetItem(null);",
         "配装后又调 SetItem"),
        ("forge", "AddComponent<SkyIslandMirrorChief>()", "AddComponent<SkyIslandStargazerChief>()", "镜中客分派成了观星手的控制器"),
        ("loot", "Resolve(random.NextDouble());", "Resolve(UnityEngine.Random.value);", "掉落改用 UnityEngine.Random"),
        ("loot", "#if BOSSRUSH_DEV\n", "", "不走死亡的演练结算漏出 Dev 构建"),
        ("world_bosses", "if (bossEventsAttached) return;", "", "击败事件订阅不幂等"),
        ("content", "SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Lord)", "SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Elite)", "内置表的岛主带队改回精英"),
        ("item_rules", "case BossRushItemIds.SkyIslandStargazerLensHelm: return 9000;", "", "价值表漏一件"),
        ("item_rules", "case BossRushItemIds.SkyIslandWindbreakPack: return 7000;", "", "价值表漏一件 R4 装备"),
        ("rules", "// 【五栏】核心招式：有视线时远程标记玩家，锁定后在标记处落两发星火",
         "// 【五栏】核心招式：立星炉供能桩给自己加护甲，星焰落点封左右走位，放完两招星炉过热", "两位 Boss 的核心招式写成同一句"),
        ("rules", "            // 【五栏】克制：躲进掩体断开视线就打断标记；贴到 8 米内它不再标记；锁定后 0.6 秒内出圈\n", "", "观星手的五栏少一栏"),
        ("rules", "NightOnly = true,", "", "蚋笛翁不再夜限定"),
        ("registry", 'Add(plans, EquipmentOnly("skyisland_boss_gear"), SkyIslandBossRules.AllGearTypeIds);', "", "动态注册表漏登记专属装备"),
        ("props", "if (path != null && path.seeker != null) path.seeker.CancelCurrentPathRequest(true);", "", "换位不掐在途寻路"),
        ("props", "skin.BakeMesh(mesh, true);", "UnityEngine.Object.Instantiate(skin.gameObject);", "倒影改成克隆角色"),
        ("story_rules", "!source.wearsMirrorArmor && ", "", "镜纹甲不放行折翎"),
        ("story_rules", ", wearsMirrorArmor = wearsMirrorArmor", "", "候选状态漏拷运行时字段"),
        ("gnats", "!SkyIslandBossGearWorn.MossgauzeMask", "true", "苔纱面罩不挡躲闪"),
        ("fieldcraft_gear", 'headset = TypeIn(item, "Headset");', "", "穿戴快照漏读耳机槽"),
        ("stargazer", "SkyIslandBossRules.TelegraphSeconds(", "SkyIslandBossRules.EscapeSpeed(", "观星手的预警不过静听耳罩"),
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
