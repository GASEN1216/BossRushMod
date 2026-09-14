# -*- coding: utf-8 -*-
u"""噬风·回响（2026-09-14 B 轮）的接线守卫：结局后每趟能在鸣风栈道引一次的噬风。

钉住的不变式（行为由执行回归 SkyIslandStory / SkyIslandEncounters / SkyIslandValidationJudges 验证，几何由
tests/SkyIslandStormEchoEscapePropertyTest.py 复算；这里只钉接线）：

1. **开启判据五项全在一处**：`SkyIslandStoryRules.CanSummonStormEcho` 同时检查敲过钟、StormResolved、本趟没用过、
   核在背包里、风晶够烧；装置面板挂不挂（SkyIslandWorldStoryEcho）与点下去那一次（SkyIslandSessionEcho）都只问它。
2. **不碰首战口径**：遭遇 id 独立（"StormEcho"），首战的「双航标 + 一次性」判断原样；BeginStoryChallenge 拒绝回响；
   清场不写存档（EncounterWasSaved 读本趟布尔、OnEncounterCleared 分流）；本体倒下分流到回响遗存，不碰 StormSlain。
3. **先占住风晶再开战、开成了才扣**：判据 → 预留 → 遭遇 owner 开战 → Commit → 本趟计数；finally 归还没提交的预留。
4. **不写存档**：回响的四个文件里没有任何剧情写入口。
5. **复用首战编排，只多一个模式位**：相位 / 脉冲 / 预警常量不复制；回响的风眼钉在预警开始的位置（R-12），
   三波之后在原地多响一声（半径同最后一波），不改血量。
6. **奖励只进已有的消耗链**：回响遗存只装登记过的天空岛物品，不抽官方物资池；每件价值在岛上物资池上限之内；
   还回来的风晶碎片少于一块风晶。
7. **串联是真实连接**：核（数背包开门）、晴岚风晶（烧掉）、晴岚护符（回响走同一个 Detonate，减伤照读）、
   驱风香 / 风灯（回响在场时栈道重新起大风）、归航菜便当（回响遗存会抽到）、钟守（官方对话台词按同一个解锁口径）、
   手记「群岛之物」与名册第一页、中英 Wiki。
8. F3 只读用例 SKY_STORM_ECHO 在编排里、判据在纯判据区。

反向检查在内存里逐条恢复错误写法，确认断言真的抓得住。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

SKY = "DebugAndTools/SkyIsland/"
RULES = SKY + "SkyIslandStoryRules.cs"
ECHO_RULES = SKY + "SkyIslandStormEchoRules.cs"
ECHO_REWARD = SKY + "SkyIslandStormEchoReward.cs"
SESSION_ECHO = SKY + "SkyIslandSessionEcho.cs"
WORLD_ECHO = SKY + "SkyIslandWorldStoryEcho.cs"
SESSION = SKY + "SkyIslandSession.cs"
WORLD = SKY + "SkyIslandWorldStory.cs"
ENCOUNTERS = SKY + "SkyIslandEncounters.cs"
CONTENT = SKY + "SkyIslandContent.cs"
BOSS = SKY + "SkyIslandStormBoss.cs"
FIELDCRAFT = SKY + "SkyIslandFieldcraft.cs"
FIELDCRAFT_RULES = SKY + "SkyIslandFieldcraftRules.cs"
ITEM_RULES = SKY + "SkyIslandItemRules.cs"
LOOT_TABLES = SKY + "SkyIslandLootTables.cs"
CRATE = SKY + "SkyIslandRewardCrate.cs"
SERVICE = SKY + "SkyIslandStoryService.cs"
CREW = SKY + "SkyIslandCrew.cs"
JOURNAL = SKY + "SkyIslandJournal.cs"
SUITE = "DebugAndTools/F3GameplayValidationSkyIsland.cs"
RUNTIME = "DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs"
WIKI_ZH = "WikiContent/zh/map__sky_island.md"
WIKI_EN = "WikiContent/en/map__sky_island.md"
BAT = "compile_official.bat"
L10N_GUARD = "tests/SkyIslandLocalizationGuard.py"
SUITE_GUARD = "tests/SkyIslandValidationSuiteGuard.py"
STORY_CSPROJ = "tests/fixtures/SkyIslandStory/Regression.csproj"
JUDGES_CSPROJ = "tests/fixtures/SkyIslandValidationJudges/Regression.csproj"
ENCOUNTERS_CSPROJ = "tests/fixtures/SkyIslandEncounters/Regression.csproj"

PATHS = [RULES, ECHO_RULES, ECHO_REWARD, SESSION_ECHO, WORLD_ECHO, SESSION, WORLD, ENCOUNTERS, CONTENT, BOSS, FIELDCRAFT,
         FIELDCRAFT_RULES, ITEM_RULES, LOOT_TABLES, CRATE, SERVICE, CREW, JOURNAL, SUITE, RUNTIME, WIKI_ZH, WIKI_EN, BAT,
         L10N_GUARD, SUITE_GUARD, STORY_CSPROJ, JUDGES_CSPROJ, ENCOUNTERS_CSPROJ]
CS = frozenset(p for p in PATHS if p.endswith(".cs"))


def squash(text):
    text = re.sub(r"\s+", " ", text or "")
    return re.sub(r"\s*([(){};:,.\[\]<>=!?|&+*/-])\s*", r"\1", text)


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


def check(sources):
    errors = []
    src = {path: (clean_source(text) if path in CS else text) for path, text in sources.items()}

    def need_body(path, signature, label):
        body = body_of(src[path], signature)
        if body is None:
            errors.append("%s 找不到 %s（%s）" % (path, label, signature))
            return ""
        return squash(body)

    def require(text, token, why):
        if squash(token) not in (text or ""):
            errors.append("%s（缺 %s）" % (why, token))

    def forbid(text, token, why):
        if text and squash(token) in text:
            errors.append("%s（不应出现 %s）" % (why, token))

    def ordered(text, tokens, why):
        position = -1
        for token in tokens:
            found = (text or "").find(squash(token), position + 1)
            if found < 0:
                errors.append("%s（缺或顺序不对：%s）" % (why, token))
                return
            position = found

    # ---- 0. 登记 ----
    for name in ("SkyIslandStormEchoRules.cs", "SkyIslandStormEchoReward.cs", "SkyIslandSessionEcho.cs",
                 "SkyIslandWorldStoryEcho.cs", "SkyIslandSessionLabels.cs"):
        if "echo(DebugAndTools\\SkyIsland\\" + name not in sources[BAT]:
            errors.append("编译清单缺 " + name + "（新增 .cs 不登记就静默不参与编译）")
        if '"%s"' % name not in sources[L10N_GUARD]:
            errors.append("SkyIslandLocalizationGuard.FILES 缺 " + name)
    for csproj, names in ((STORY_CSPROJ, ("SkyIslandStormEchoRules.cs", "SkyIslandStormEchoReward.cs")),
                          (JUDGES_CSPROJ, ("SkyIslandStormEchoRules.cs", "SkyIslandStormEchoReward.cs")),
                          (ENCOUNTERS_CSPROJ, ("SkyIslandStormEchoRules.cs",))):
        for name in names:
            if "DebugAndTools/SkyIsland/" + name not in sources[csproj]:
                errors.append(csproj + " 没有链接 " + name + "（回响的纯规则没人执行）")
    for member in ("TryBeginStormEcho", "StormEchoChoice", "OnStormEchoDefeated", "RecordStormEchoCleared", "CreateWithGoods"):
        if '("%s",' % member not in sources[SUITE_GUARD]:
            errors.append("F3 只读守卫的写入口名单缺 " + member)

    # ---- 1. 开启判据五项全在一处 ----
    rule = need_body(RULES, "internal static bool CanSummonStormEcho(", "引风判据")
    ordered(rule, ["blocker = null;", "if (data == null || !data.StormResolved) return false;",
                   "if (!data.Has(SkyIslandStoryFlag.Ending))", "return false;", "if (usedThisRaid) return false;",
                   "if (!coreCarried)", "return false;", "if (windcrystals < StormEchoWindcrystalCost)", "return false;", "return true;"],
            "引风判据必须依次检查 StormResolved、结局、本趟没用过、核在背包里、风晶够烧，全过才返回 true")
    require(squash(src[RULES]), "internal const int StormEchoWindcrystalCost = 1;", "引风烧几块晴岚风晶只许有一个常量")
    can = need_body(SESSION_ECHO, "internal bool CanSummonStormEcho(out string blocker)", "会话的引风判据")
    ordered(can, ["if (!IsSessionValid() || story == null) return false;",
                  "bool core = PackCount(BossRushItemIds.SkyIslandWindeaterCore) > 0;",
                  "int crystals = PackCount(BossRushItemIds.SkyIslandQinglanWindcrystal);",
                  "return SkyIslandStoryRules.CanSummonStormEcho(story.Current, stormEchoStarts >= SkyIslandStormEchoRules.MaxPerRaid, core, crystals, out blocker);"],
            "会话的引风判据要数背包里的核与风晶、按本趟计数，再交给同一份纯规则")
    choice = need_body(WORLD_ECHO, "private void StormEchoChoice(List<SkyIslandStoryPresentation.Choice> choices)", "引风选项")
    ordered(choice, ["if (!session.CanSummonStormEcho(out blocker))", "Hint(blocker);", "return;", "choices.Add(",
                     "session.TryBeginStormEcho(out message)", "presentation.Dispose();", "session.Announce(message, false);"],
            "引风选项先判断再挂、挂不出来留「还差什么」，点下去交给会话、开战后收起面板改走字幕")
    require(squash(src[WORLD]).split(squash('case "Search_E":'), 1)[-1].split("case", 1)[0] if squash('case "Search_E":') in squash(src[WORLD]) else "",
            "StormEchoChoice(choices);", "鸣风栈道双航标门装置（Search_E）上要挂引风选项")

    # ---- 2. 不碰首战口径 ----
    session = squash(src[SESSION])
    require(session, 'if (id == "Storm" && (!story.Current.BothBeacons || story.Current.StormResolved)) return false;',
            "首战的「双航标 + 打过就不再来」一字不动")
    begin_story = need_body(SESSION, "internal bool BeginStoryChallenge(string id)", "剧情挑战入口")
    ordered(begin_story, ["if (SkyIslandStormEchoRules.IsEcho(id)) return false;", "return encounters.BeginChallenge(id);"],
            "剧情挑战入口必须拒绝回响：回响只能经引风（先判五项、再烧风晶）开战")
    saved = need_body(SESSION, "private bool EncounterWasSaved(string id)", "遭遇已记下")
    ordered(saved, ['if (id == "Storm") return story.Current.StormResolved;',
                    "if (SkyIslandStormEchoRules.IsEcho(id)) return stormEchoCleared;",
                    "return story.Current.EncounterCleared(id);"],
            "回响的「已记下」读本趟布尔，排在读存档清场表之前（沿用 Storm 或读存档都会被当成永久已清）")
    cleared = need_body(SESSION, "private void OnEncounterCleared(string id)", "清场回调")
    require(cleared, "else if (!RecordStormEchoCleared(id)) story.RecordEncounterCleared(id);",
            "回响清场不得写进存档的清场表")
    record = need_body(SESSION_ECHO, "private bool RecordStormEchoCleared(string id)", "回响清场记账")
    ordered(record, ["if (!SkyIslandStormEchoRules.IsEcho(id)) return false;", "stormEchoCleared = true;", "return true;"],
            "回响清场只记本趟布尔")
    defeated = need_body(SESSION, "private void OnStormDefeated(string id, Vector3 position)", "噬风倒下")
    ordered(defeated, ["if (SkyIslandStormEchoRules.IsEcho(id)) { OnStormEchoDefeated(position); return; }",
                       "SkyIslandStoryAction.StormSlain", "SkyIslandStormBoss.DropTrophy(root.transform, drop, raidSeed)"],
            "本体倒下时回响先分流到回响遗存，首战才记 StormSlain、发星工遗存")
    require(squash(src[ECHO_RULES]), 'internal const string EncounterId = "StormEcho";', "回响用独立的遭遇 id")
    identity = need_body(ENCOUNTERS, "private void ApplyIdentity(CharacterMainControl created, Encounter encounter, int index, SkyIslandEnemyTier tier)", "身份层")
    ordered(identity, ["string encounterId = encounter.Id;", "AddComponent<SkyIslandStormBoss>().Bind(created, valid, report, delegate",
                       "stormDefeated(encounterId, bossTransform.position);", "SkyIslandStormEchoRules.IsEcho(encounterId));"],
            "回响由遭遇 owner 以回响模式绑定同一套编排，倒下回调带上 id")
    require(squash(src[CONTENT]), 'Encounter("StormEcho", "POI_E", 3, true, SkyIslandEnemyTier.Elite, SkyIslandEnemyTier.Storm)',
            "内置表要有回响这一组（同一处风眼、首战编成、手动组）")

    # ---- 3. 先占住风晶再开战、开成了才扣 ----
    begin = need_body(SESSION_ECHO, "internal bool TryBeginStormEcho(out string message)", "引风")
    ordered(begin, ["if (!CanSummonStormEcho(out blocker))", "SkyIslandInventoryTransaction.TryReserve(player, SkyIslandStormEchoReward.Cost, out crystal)",
                    "if (!IsSessionValid() || encounters == null || !encounters.BeginChallenge(SkyIslandStormEchoRules.EncounterId))",
                    "crystal.Commit();", "stormEchoStarts++;", "finally", "crystal.Dispose();"],
            "引风：同一份判据 → 预留风晶 → 开战 → 开成了才扣、才计本趟；没提交的预留在 finally 归还")
    forbid(begin, "ConsumeFromPack", "引风不许绕过事务直接扣材料")

    # ---- 4. 回响不写存档 ----
    for path in (ECHO_RULES, ECHO_REWARD, SESSION_ECHO, WORLD_ECHO):
        text = squash(src[path])
        for token in ("TryApply(", "RecordNote(", "RecordSearch(", "RecordEncounterCleared(", "RecordRegionVisited(",
                      "SavesSystem", "store.Store(", "RemoveNote("):
            forbid(text, token, path + " 不得写存档（回响按本趟计）")

    # ---- 5. 复用首战编排，只多一个模式位 ----
    boss = squash(src[BOSS])
    for token in ("PhaseThresholds = { 0.80f, 0.60f, 0.40f, 0.20f }", "PulseRadius = 7f", "PulseDamage = 38f",
                  "PulseWaves = 3", "PulseTelegraph = 1.4f", "internal const float WaveGap = 0.45f;"):
        if boss.count(squash(token)) != 1:
            errors.append("噬风的相位 / 脉冲 / 预警常量只许定义一次（回响复用它们，不复制）：" + token)
    for rogue in ("EchoPulseRadius", "EchoTelegraph", "EchoThresholds", "EchoDamage"):
        forbid(boss, rogue, "回响不许另起一套相位 / 脉冲常量")
    routine = need_body(BOSS, "private IEnumerator PulseRoutine()", "脉冲编排")
    ordered(routine, ["eyeOrigin = boss.transform.position;", "while (Time.time - started < PulseTelegraph && !Aborted())",
                      "for (int wave = 0; wave < PulseWaves && !Aborted(); wave++)", "float waveUntil = Time.time + WaveGap;",
                      "if (echo && !Aborted())", "Detonate(PulseWaves - 1);"],
            "回响的风眼在预警开始时钉住，三波之后在原地再响一声（半径同最后一波，间隔同首战）")
    forbid(routine, "Time.time + 0.45f", "波间隔要读 WaveGap，不许再写字面量")
    detonate = need_body(BOSS, "private void Detonate(int wave)", "风暴脉冲")
    require(detonate, "Vector3 origin = echo ? eyeOrigin : boss.transform.position;", "回响的圆心钉在风眼，首战照旧每波重读本体位置")
    require(detonate, "damage.damageValue = SkyIslandFieldcraftRules.StormPulseDamage(PulseDamage, SkyIslandFieldcraft.StormWarded);",
            "晴岚护符对回响同样减伤（同一个 Detonate）")
    for token in ("SetHealth(", "BaseValue", "HealthMultiplier("):
        forbid(boss, token, "回响不许靠堆血量拖时长")

    # ---- 6. 奖励只进已有的消耗链 ----
    reward = src[ECHO_REWARD]
    for_body = body_of(reward, "internal static SkyIslandYield[] For(double kitRoll)") or ""
    cost_body = body_of(reward, "internal static SkyIslandIngredient[] Cost") or ""
    reward_ids = set(re.findall(r"BossRushItemIds\.(\w+)", for_body))
    all_ids = set(re.findall(r"BossRushItemIds\.(\w+)", src[ITEM_RULES].split("internal static readonly int[] AllTypeIds", 1)[-1].split("};", 1)[0]))
    if not reward_ids or not reward_ids <= all_ids:
        errors.append("回响遗存只许装登记过的天空岛物品（不给新 TypeID）：%r 不在 AllTypeIds 里" % sorted(reward_ids - all_ids))
    if "SkyIslandQinglanWindcrystal" not in cost_body:
        errors.append("引风烧的必须是晴岚风晶")
    for path in (ECHO_REWARD, SESSION_ECHO):
        for token in ("SkyIslandLootPools", "GetAllTypeIds", "ItemAssetsCollection.Search("):
            forbid(squash(src[path]), token, path + " 不许抽官方物资池（池里有只能卖钱的收藏品）")
    values = dict(re.findall(r"case BossRushItemIds\.(\w+): return (\d+);", src[ITEM_RULES].split("internal static int ValueOf(int typeId)", 1)[-1]))
    cap = re.search(r"internal const int MaxPoolItemValue = (\d+);", src[LOOT_TABLES])
    for item in sorted(reward_ids):
        if item not in values or not cap or int(values[item]) > int(cap.group(1)) or int(values[item]) <= 0:
            errors.append("回响遗存里的 %s 必须有正价值且不超过岛上物资池单件上限" % item)
    shards = re.search(r"internal const int Shards = (\d+);", reward)
    per = re.search(r'Recipe\("Windcrystal",[^;]*?In\(BossRushItemIds\.SkyIslandWindcrystalShard, (\d+)\)', src[FIELDCRAFT_RULES], re.S)
    if not shards or not per or int(shards.group(1)) >= int(per.group(1)):
        errors.append("回响遗存还回来的风晶碎片必须少于一块晴岚风晶折合的片数（防刷：回响养不活自己）")
    drop = need_body(SESSION_ECHO, "private void OnStormEchoDefeated(Vector3 position)", "回响遗存")
    ordered(drop, ["SkyIslandRewardCrate.TryFindCratePosition(", "SkyIslandStormEchoReward.For(",
                   "SkyIslandRewardCrate.CreateWithGoods(root.transform, drop,"],
            "回响遗存：先退开尸体箱的交互间距，再装回响遗存表里的东西")
    for token in ("DropTrophy(", "SkyIslandRewardCrate.Create(", "SkyIslandLootTier.Starworks"):
        forbid(drop, token, "回响遗存不发星工遗存（不抽官方物资池）")
    goods = need_body(CRATE, "internal static bool CreateWithGoods(Transform parent, Vector3 position, string name, SkyIslandYield[] goods)", "回响遗存建箱")
    ordered(goods, ["Build(parent, position, 0f, name, out error);", "AddGoods(box,", "if (added == 0)", "Destroy(box.gameObject);", "return false;", "return true;"],
            "回响遗存建箱复用共享建箱器，一件都没装进去就收回空箱")
    add_goods = need_body(CRATE, "private static int AddGoods(InteractableLootbox box, int typeId, int count)", "回响遗存装箱")
    ordered(add_goods, ["ItemAssetsCollection.GetPrefab(typeId) == null", "ItemAssetsCollection.InstantiateSync(typeId)", "item.TypeID != typeId"],
            "回响遗存装箱先问 prefab 再实例化、回读 TypeID")

    # ---- 7. 串联是真实连接 ----
    pending = need_body(SESSION_ECHO, "internal bool StormWindPending", "回响带回来的风")
    require(pending, "return BothBeaconsLit && (!StormResolved || StormEchoActive);", "回响在场时栈道与桥上重新起大风")
    require(need_body(SESSION_ECHO, "internal bool StormEchoActive", "回响在场"),
            "encounters.IsBusy(SkyIslandStormEchoRules.EncounterId)", "回响在场读遭遇 owner 的同一个事实")
    wind = need_body(FIELDCRAFT, "private void TickWind(CharacterMainControl player, bool night, float elapsed)", "夜风")
    require(wind, "bool stormPending = session.StormWindPending;", "夜风的「噬风将至」要读会话的 StormWindPending（驱风香 / 风灯 / 噬风之核因此对回响有用）")
    forbid(wind, "session.BothBeaconsLit && !session.StormResolved", "夜风不许绕过回响另写一份口径")
    if "SkyIslandHomecomingBento" not in reward_ids or "SkyIslandQinglanCharm" not in reward_ids or "SkyIslandWindwardIncense" not in reward_ids:
        errors.append("回响遗存要能抽到归航菜便当、晴岚护符与驱风香（打下一趟用得上的东西）")
    service = squash(src[SERVICE])
    npc = need_body(SERVICE, "internal string DescribeNpc(string id)", "居民台词")
    bell = npc.split(squash('case "sky_bellkeeper":'), 1)[-1] if squash('case "sky_bellkeeper":') in npc else ""
    require(bell, "BellKeeperEchoLine(data)", "钟守结局后要提一句回响（走官方对话）")
    require(need_body(SERVICE, "private static string BellKeeperEchoLine(SkyIslandStoryData data)", "钟守回响台词"),
            "if (!SkyIslandStormEchoRules.UnlockedBySave(data)) return string.Empty;", "钟守的回响台词与引风同一个解锁口径")
    crew = need_body(CREW, "internal static string Page(int index, SkyIslandStoryData data)", "名册")
    first_page = crew.split(squash("case 1:"), 1)[0]
    require(first_page, "if (SkyIslandStormEchoRules.UnlockedBySave(data))", "名册第一页（老舵手）要记下回响，与引风同一个解锁口径")
    uses = need_body(JOURNAL, "internal static string Uses()", "群岛之物")
    rows = uses.split("Use(text,BossRushItemIds.")
    for item, token in (("SkyIslandWindeaterCore", "引风"), ("SkyIslandQinglanWindcrystal", "引风"), ("SkyIslandWindcrystalShard", "回响遗存")):
        # 英文文案里有 ASCII 分号，不能用 [^;]* 截行：按 Use( 切段，取以这件物品开头的那一段。
        row = next((r for r in rows[1:] if r.startswith(item + ",")), None)
        if not row or token not in row:
            errors.append("手记「群岛之物」里 %s 那一行要记下回响（缺「%s」）" % (item, token))
    if "噬风·回响" not in sources[WIKI_ZH] or "Windeater's echo" not in sources[WIKI_EN]:
        errors.append("中英 Wiki 天空岛页都要写噬风·回响")

    # ---- 8. F3 只读用例 ----
    if not re.search(r'RunSkyIslandSync\("SKY_STORM_ECHO", ValidateSkyIslandStormEcho\);', src[SUITE]):
        errors.append("岛内验收编排缺 SKY_STORM_ECHO")
    pure = src[RUNTIME].split("#region 纯判据", 1)[-1].split("#endregion", 1)[0] if "#region 纯判据" in sources[RUNTIME] else ""
    if "internal static bool JudgeStormEcho(" not in pure:
        errors.append("SKY_STORM_ECHO 的判据必须写在纯判据区（隔离回归逐字抽出执行）")
    return errors


def main():
    sources = {}
    for rel in PATHS:
        path = ROOT / rel
        if not path.is_file():
            print("SkyIslandStormEchoGuard: FAIL - 找不到 " + rel)
            return 1
        sources[rel] = path.read_text(encoding="utf-8-sig")
    errors = check(sources)

    probes = [
        (RULES, "if (usedThisRaid) return false;", ""),
        (RULES, "            if (!coreCarried)", "            if (false)"),
        (RULES, "windcrystals < StormEchoWindcrystalCost", "windcrystals < 0"),
        (RULES, "if (!data.Has(SkyIslandStoryFlag.Ending))", "if (false)"),
        (SESSION_ECHO, "stormEchoStarts >= SkyIslandStormEchoRules.MaxPerRaid", "false"),
        (WORLD_ECHO, "if (!session.CanSummonStormEcho(out blocker))", "if (false)"),
        (SESSION, "            if (SkyIslandStormEchoRules.IsEcho(id)) return false;\n", ""),
        (SESSION, "if (SkyIslandStormEchoRules.IsEcho(id)) return stormEchoCleared;", ""),
        (SESSION, "else if (!RecordStormEchoCleared(id)) story.RecordEncounterCleared(id);", "else story.RecordEncounterCleared(id);"),
        (SESSION_ECHO, "                crystal.Commit();\n", ""),
        # 预留的不是回响的代价（白嫖一次引风）
        (SESSION_ECHO, "TryReserve(player, SkyIslandStormEchoReward.Cost, out crystal)", "TryReserve(player, new SkyIslandIngredient[0], out crystal)"),
        # 没提交的预留不归还
        (SESSION_ECHO, "if (crystal != null) crystal.Dispose();", "if (crystal != null) { }"),
        (SESSION_ECHO, "stormEchoCleared = true;", "stormEchoCleared = true; story.RecordEncounterCleared(id);"),
        (ECHO_RULES, 'internal const string EncounterId = "StormEcho";', 'internal const string EncounterId = "Storm";'),
        (BOSS, "Vector3 origin = echo ? eyeOrigin : boss.transform.position;", "Vector3 origin = boss.transform.position;"),
        (BOSS, "                try { Detonate(PulseWaves - 1); }", "                try { }"),
        (BOSS, "float waveUntil = Time.time + WaveGap;", "float waveUntil = Time.time + 0.45f;"),
        (ECHO_REWARD, "new SkyIslandYield(BossRushItemIds.SkyIslandHomecomingBento, 1)", "new SkyIslandYield(BossRushItemIds.SkyIslandDeepVaultKey, 1)"),
        (ECHO_REWARD, "internal const int Shards = 3;", "internal const int Shards = 5;"),
        (SESSION_ECHO, "SkyIslandRewardCrate.CreateWithGoods(root.transform, drop,", "SkyIslandStormBoss.DropTrophy(root.transform, drop, raidSeed); SkyIslandRewardCrate.CreateWithGoodsX(root.transform, drop,"),
        (FIELDCRAFT, "bool stormPending = session.StormWindPending;", "bool stormPending = session.BothBeaconsLit && !session.StormResolved;"),
        (SERVICE, "if (!SkyIslandStormEchoRules.UnlockedBySave(data)) return string.Empty;", "if (data == null) return string.Empty;"),
        (CREW, "if (SkyIslandStormEchoRules.UnlockedBySave(data))", "if (data.StormResolved)"),
        (SUITE, '            RunSkyIslandSync("SKY_STORM_ECHO", ValidateSkyIslandStormEcho);\n', ""),
        (SUITE_GUARD, '("TryBeginStormEcho", "引风：烧风晶并开战"), ', ""),
    ]
    for path, before, after in probes:
        text = sources[path].replace("\r\n", "\n")
        if before not in text:
            errors.append(u"反向检查锚点失效：" + path + " -> " + before[:70])
            continue
        altered = dict(sources)
        altered[path] = text.replace(before, after, 1)
        if not check(altered):
            errors.append(u"未拦截错误写法：" + path + " -> " + before[:70])

    if errors:
        print("SkyIslandStormEchoGuard: FAIL\n  - " + "\n  - ".join(errors))
        return 1
    print("SkyIslandStormEchoGuard: PASS（五项判据一处 / 首战口径不动 / 先预留后开战 / 不写存档 / 复用编排只多模式位 / "
          "回响遗存只进消耗链 / 串联七条 / F3 只读用例；%d 个反向检查）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
