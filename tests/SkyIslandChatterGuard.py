"""天空岛头顶气泡（居民漫步 + 全岛话语）的结构不变式。

## 为什么要有这份

这张图此前是静止的：居民钉在标记上不出声，敌人从生成到倒下一个字都没有。
2026-09-17 加上「活人感」之后，最容易悄悄坏掉的不是代码而是**话语表本身**：

- 少写一个说话者（新加一位头目忘了配台词）——编译绿、守卫绿，游戏里那一位全程哑巴；
- 台词写长了——官方气泡定宽，头顶糊成三行，没有任何报错；
- 复制粘贴——两位头目说同一句，人设直接塌掉；
- 把四道门（静音 / 同屏上限 / 距离 / 单人冷却）删掉一条——聚落里几个人抢着刷屏；
- 无声钟守配上台词、噬风（一股风）配上台词——人设被推翻，而这是纯文本，没人会编译报错。

所以本守卫钉住五件事：**每个说话者都有人设与台词、台词短且不重复、该闭嘴的闭嘴、
四道门都在、居民「4 走 2 站」与生产一致**。

它不证明观感：气泡什么时候冒出来好不好看，只能实机看。
挑选规则（不连着重复上一句、按剧情进度换池子）由隔离回归逐条复算
（tests/fixtures/SkyIslandStory/SkyIslandChatterRegression.cs）。

## 反向验证（2026-09-17 逐条做过）

删掉蚋笛翁的倒下那句 / 把某句写到 30 字 / 把两位头目的出场句写成同一句 /
去掉 `BossRushUI.IsGamePaused()` 这道门 / 把折翎改成 `canWander: true` —— 各跑一次都转红，
再按字节还原。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

SKY = ROOT / "DebugAndTools" / "SkyIsland"
errors = []


def read(name, folder=SKY):
    path = folder / name
    if not path.exists():
        errors.append("读不到 " + name)
        return ""
    return path.read_text(encoding="utf-8-sig")


LINES_RAW = read("SkyIslandChatterLines.cs")
LINES = clean_source(LINES_RAW)
CHATTER = clean_source(read("SkyIslandChatter.cs"))
VOICE = clean_source(read("SkyIslandBossVoice.cs"))
ENCOUNTERS = clean_source(read("SkyIslandEncounters.cs"))
RESIDENTS = clean_source(read("SkyIslandResidents.cs"))
SESSION = clean_source(read("SkyIslandSession.cs"))
FORGE = clean_source(read("SkyIslandBossForge.cs"))
RULES = clean_source(read("SkyIslandBossRules.cs"))


def require(source, token, what):
    if token not in source:
        errors.append("%s：缺少 `%s`" % (what, token))


def constant(source, name, default=None):
    match = re.search(r"const\s+int\s+" + name + r"\s*=\s*(\d+)\s*;", source)
    if match:
        return int(match.group(1))
    if default is None:
        errors.append("读不到常量 " + name)
    return default


# ---------------------------------------------------------------------------
# 1. 话语表：谁说话、说几句、每句多长
# ---------------------------------------------------------------------------

MAX_CN = constant(LINES, "MaxChineseChars", 20)
MAX_EN = constant(LINES, "MaxEnglishChars", 56)

MARKER = re.compile(
    r'(?:internal|private) static string\[\] (?P<method>\w+)\('
    r'|case "(?P<caseid>[^"]+)":'
    r'|case SkyIslandBossKind\.(?P<kind>\w+):'
    r'|case SkyIslandChatterMoment\.(?P<casemoment>\w+):'
    r'|if \(moment == SkyIslandChatterMoment\.(?P<ifmoment>\w+)\)'
    r'|L10n\.T\("(?P<cn>(?:[^"\\]|\\.)*)",\s*"(?P<en>(?:[^"\\]|\\.)*)"\)'
)

# 非语言说话者的「台词」（钟声、省略号）：允许在两处出现同一条——无声钟守本人与失控的守钟装置
# 敲的本来就是同一口钟。除此之外重复一律算复制粘贴。
NON_VERBAL = re.compile(r"^[…\.—…。铛嗡、]+$")

pools = {}          # (method, outer, moment) -> [(cn, en)]
windhunter_lines = 0
method = outer = moment = None
for match in MARKER.finditer(LINES):
    if match.group("method"):
        method, outer, moment = match.group("method"), None, None
    elif match.group("caseid"):
        outer, moment = match.group("caseid"), None
    elif match.group("kind"):
        outer, moment = match.group("kind"), None
    elif match.group("casemoment"):
        moment = match.group("casemoment")
    elif match.group("ifmoment"):
        moment = match.group("ifmoment")
    else:
        if method == "Windhunter":
            windhunter_lines += 1
            continue
        pools.setdefault((method, outer, moment), []).append((match.group("cn"), match.group("en")))

if not pools:
    errors.append("话语表里一条台词都没解析出来（marker 正则与源码失步了？）")

# 长度与重复：气泡定宽，长句在头顶糊成三行；重复 = 复制粘贴，人设会塌。
seen_cn = {}
for key, lines in pools.items():
    where = "/".join(str(part) for part in key)
    for cn, en in lines:
        visible = len([ch for ch in cn if not ch.isspace()])
        if visible > MAX_CN:
            errors.append("%s 的中文台词 %d 字，超过 %d：%s" % (where, visible, MAX_CN, cn))
        if len(en) > MAX_EN:
            errors.append("%s 的英文台词 %d 字符，超过 %d：%s" % (where, len(en), MAX_EN, en))
        if not en.strip():
            errors.append("%s 有一句没写英文：%s" % (where, cn))
        if cn in seen_cn and seen_cn[cn] != where and not NON_VERBAL.match(cn):
            errors.append("同一句中文出现在两个说话者上（%s 与 %s）：%s" % (seen_cn[cn], where, cn))
        seen_cn.setdefault(cn, where)


def pool(method_name, outer_name, moment_name):
    return pools.get((method_name, outer_name, moment_name), [])


def need(method_name, outer_name, moment_name, minimum, what):
    got = len(pool(method_name, outer_name, moment_name))
    if got < minimum:
        errors.append("%s 只有 %d 句，至少要 %d 句" % (what, got, minimum))


# 居民：id 从生产的居民表读，新增一位居民忘了配台词必红。
RESIDENT_IDS = re.findall(r'"(sky_\w+)"', re.search(
    r"private static readonly string\[\] Ids = \{(.*?)\};", RESIDENTS, re.S).group(1)) \
    if re.search(r"private static readonly string\[\] Ids = \{(.*?)\};", RESIDENTS, re.S) else []
if len(RESIDENT_IDS) != 6:
    errors.append("居民 id 表解析异常，读到 %d 个" % len(RESIDENT_IDS))
for npc_id in RESIDENT_IDS:
    # 无声钟守只有钟声与省略号，三条就够；会说话的至少四句，否则站一会儿就开始重复。
    #
    # 注意这里数的是**整个 case 里的全部台词**（源码按剧情分支 return 不同数组，本守卫看不出分支）。
    # 「每个分支各自至少四句」由隔离回归按真实返回值判（SkyIslandChatterRegression.ResidentPoolsFollowStory）——
    # 只靠这里的聚合数，某个分支瘦到两三句照样能藏过去。
    need("Resident", npc_id, None, 3 if npc_id == "sky_bellkeeper" else 4, "居民 " + npc_id + " 的闲聊")

# 无声钟守：名字里就写着「无声」。他的气泡只许是钟声与省略号，出现任何汉字词都算人设被推翻。
for cn, en in pool("Resident", "sky_bellkeeper", None):
    if re.search(r"[一-鿿]", cn.replace("铛", "")):
        errors.append("无声钟守的气泡出现了台词（只许钟声与省略号）：" + cn)

# 小兵共享库：闲话要够多（同一片营地站久了不能来回两句），战斗两类少一些。
need("Scav", None, "Idle", 8, "云沿拾荒者的闲话")
need("Scav", None, "Noticed", 4, "云沿拾荒者「注意到你」")
need("Scav", None, "AllyDown", 3, "云沿拾荒者「身边倒下一个」")
need("Ranger", None, "Idle", 4, "断风游猎的闲话")
need("Ranger", None, "Noticed", 3, "断风游猎「注意到你」")
need("Ranger", None, "AllyDown", 2, "断风游猎「身边倒下一个」")

# 头目 / 岛主：档案表里有几类就要有几类台词，三个时刻一个都不能少。
KINDS = re.findall(r"^\s{8}(\w+) = \d+,?$", re.search(
    r"internal enum SkyIslandBossKind\s*\{(.*?)\n    \}", RULES, re.S).group(1), re.M) \
    if re.search(r"internal enum SkyIslandBossKind\s*\{(.*?)\n    \}", RULES, re.S) else []
if len(KINDS) < 9:
    errors.append("头目类型枚举解析异常，读到 %d 个：%s" % (len(KINDS), KINDS))
for kind in KINDS:
    if kind == "Windhunter":
        # 断风三变体共用一个方法，按总条数核对（3 变体 × 3 时刻）。
        if windhunter_lines < 9:
            errors.append("断风游猎三个变体一共只有 %d 句，至少 9 句（追 / 伏 / 守各三个时刻）" % windhunter_lines)
        continue
    for moment_name in ("Noticed", "Wounded", "Down"):
        need("Boss", kind, moment_name, 1, "头目 %s 的「%s」" % (kind, moment_name))

# 具名剧情对手：折翎的战斗体与失控的守钟装置。噬风（一股风）不该出现在表里。
for champion in ("zheling", "bellkeeper"):
    for moment_name in ("Noticed", "Wounded", "Down"):
        need("Champion", champion, moment_name, 1, "具名对手 %s 的「%s」" % (champion, moment_name))
for cn, en in pool("Champion", "bellkeeper", "Noticed") + pool("Champion", "bellkeeper", "Wounded") \
        + pool("Champion", "bellkeeper", "Down"):
    if re.search(r"[一-鿿]", cn.replace("铛", "").replace("嗡", "")):
        errors.append("失控的守钟装置是一台机器，不该有台词：" + cn)
if re.search(r'case "storm"|SkyIslandBossKind\.Storm|噬风.*return new\[\]', LINES):
    errors.append("噬风是一股风，话语表里不该给它台词")

# 人设：每个说话者上方都要有一行 【声音】，写清他是谁、怎么说话。没有它，下一个人写台词没有依据。
for anchor in [('case "%s":' % npc_id) for npc_id in RESIDENT_IDS] \
        + [("case SkyIslandBossKind.%s:" % kind) for kind in KINDS] \
        + ["internal static string[] Scav(", "internal static string[] Ranger(",
           'case "zheling":', 'case "bellkeeper":']:
    at = LINES_RAW.find(anchor)
    if at < 0:
        errors.append("话语表里找不到说话者：" + anchor)
        continue
    if "【声音】" not in LINES_RAW[max(0, at - 900):at]:
        errors.append("说话者 %s 上方没有【声音】人设注释" % anchor)

# ---------------------------------------------------------------------------
# 2. 调度器的四道门
# ---------------------------------------------------------------------------

for token, what in [
    ("DialogueManager.IsDialogueActive", "静音门：官方对话进行中不说话"),
    ("BossRushUI.IsGamePaused()", "静音门：暂停（剧情面板压 timeScale）不说话"),
    ("valid != null && !valid()", "静音门：会话失效不说话"),
    ("Time.time < busyUntil", "同屏上限：这个 owner 已经有一个气泡挂着就不说"),
    ("SpeakRange * SpeakRange", "距离门：玩家看不见的地方不说（平方比较）"),
    ("nextAt.TryGetValue(key, out next) && Time.time < next", "单人冷却"),
    ("DialogueBubblesManager.Show", "出口复用官方气泡"),
]:
    require(CHATTER, token, "气泡调度器")
if "BossRushUILayers" in CHATTER:
    errors.append("气泡调度器不该引用画布层级常量：它走的是官方气泡，不是我们自绘的 HUD")
for name, minimum in [("ResidentCooldownMin", 10), ("EnemyCooldownMin", 30)]:
    match = re.search(r"const float " + name + r" = (\d+(?:\.\d+)?)f;", CHATTER)
    if not match:
        errors.append("读不到冷却常量 " + name)
    elif float(match.group(1)) < minimum:
        errors.append("%s = %s，比 %d 秒还短：气泡会吵" % (name, match.group(1), minimum))

# ---------------------------------------------------------------------------
# 3. 头目台词组件：订阅必须成对
# ---------------------------------------------------------------------------

for token, what in [
    ("health.OnDeadEvent.AddListener(OnDead)", "头目台词订阅倒下"),
    ("health.OnHurtEvent.AddListener(OnHurt)", "头目台词订阅受伤（血线）"),
    ("health.OnDeadEvent.RemoveListener(OnDead)", "OnDestroy 退订倒下"),
    ("health.OnHurtEvent.RemoveListener(OnHurt)", "OnDestroy 退订受伤"),
    ("Say(SkyIslandChatterMoment.Down, true)", "倒下那句强制说出口（跳过同屏上限与冷却）"),
    ("GetComponentInChildren<AICharacterController>()", "官方 AI 挂在子物体上，根节点取不到"),
    # 与小兵同一个坑：`noticed` 是「听见动静或挨打」永久置位，队友开枪也会点亮。
    ("ai.NoticeFromCharacter != CharacterMainControl.Main", "出场那句要认 NoticeFromCharacter，不能只看 noticed"),
    # 说不出口（玩家还在二十米外）时不能记成已出场，否则走到跟前它再也不开口。
    ("if (Say(SkyIslandChatterMoment.Noticed, false)) announced = true;", "出场那句说成了才记「已出场」"),
]:
    require(VOICE, token, "头目台词组件")

# ---------------------------------------------------------------------------
# 4. 接线：没有这几处，上面的表一句也播不出来
# ---------------------------------------------------------------------------

require(SESSION, "residents.Tick(player.transform.position", "会话推进居民气泡")
require(RESIDENTS, "chatter.TrySay(speaker, ResidentBubbleHeight", "居民 owner 说话")
require(RESIDENTS, "chatter.Muted", "居民侧先问一次静音门，别让六位居民各问一遍会话有效性")
require(RESIDENTS, "movement.HoldForDialogue()", "说话时居民停下脚步")
require(RESIDENTS, "movement.ReleaseFromDialogue(1f)", "说完之后继续走")
require(RESIDENTS, "chatter.Clear()", "离岛时释放居民气泡预算")
require(CHATTER, "internal bool Muted", "整个 owner 的静音门要能被驱动方问到")
require(ENCOUNTERS, "TickChatter();", "遭遇 owner 推进小兵气泡")
# 官方 `noticed` 是「听见动静或挨打」就永久置位，队友开枪也会点亮它；只按它判会让小兵对着队友的枪声
# 喊「有人上来了」。认 NoticeFromCharacter 才说得清是冲着谁来的。
require(ENCOUNTERS, "actor.Ai.NoticeFromCharacter == player",
        "「第一次注意到你」要认 NoticeFromCharacter，不能只看 noticed")
# 一次性事件不能在静音期间被记成已发生：玩家正跟居民说话时有敌人察觉到他，那句喊话不该被永久吃掉。
require(ENCOUNTERS, "chatter.Muted) return;", "静音时整条早退，不在遍历里消费「注意到你」")
require(ENCOUNTERS, "actor.Noticed = false;", "补刷同一个槽位要复位「已喊过」，否则新刷的那位永远不吭声")
require(ENCOUNTERS, "actor.Silent = tier == SkyIslandEnemyTier.Storm", "噬风与头目不走小兵共享库")
require(ENCOUNTERS, "SkyIslandChatterLines.Mob(rival, moment)", "小兵按阵营取共享库")
require(ENCOUNTERS, "chatter.Clear()", "离岛时释放敌人气泡预算")
require(ENCOUNTERS, 'SkyIslandBossForge.BindVoice(created, null, "zheling"', "折翎战斗体接台词")
require(ENCOUNTERS, 'SkyIslandBossForge.BindVoice(created, null, "bellkeeper"', "守钟装置接钟声")
require(FORGE, "BindVoice(created, profile, null, context);", "头目 / 岛主装配时接台词")
require(FORGE, "internal SkyIslandBarkDelegate Bark;", "招式控制器经上下文拿气泡通道")

# 优先级：有人刚倒下的时候还在念叨残铜值几个钱，比不说话更假。
order = [ENCOUNTERS.find("; moment = SkyIslandChatterMoment.AllyDown;"),
         ENCOUNTERS.find("; moment = SkyIslandChatterMoment.Noticed;"),
         ENCOUNTERS.find("; moment = SkyIslandChatterMoment.Idle;")]
if -1 in order or order != sorted(order):
    errors.append("小兵气泡的优先级必须是「身边倒下一个 > 注意到你 > 闲话」")

# ---------------------------------------------------------------------------
# 5. 居民「4 走 2 站」
# ---------------------------------------------------------------------------

NPCS = {row["id"]: row for row in json.loads(
    (ROOT / "Assets/Data/DuckNpcs.json").read_text(encoding="utf-8-sig"))["npcs"]}
# 折翎守着旧航路、无声钟守守着钟：这两位站定是人设，不是漏配。
WANDER = {"sky_qinghe": True, "sky_weibai": True, "sky_fuzhou": True, "sky_miantai": True,
          "sky_zheling": False, "sky_bellkeeper": False}
for npc_id, expected in WANDER.items():
    row = NPCS.get(npc_id)
    if row is None:
        errors.append("缺少居民蓝图 " + npc_id)
        continue
    if bool(row.get("canWander")) != expected:
        errors.append("%s 的 canWander 应为 %s（走动的 4 位 / 按人设站定的 2 位）" % (npc_id, expected))
    radius = float(row.get("wanderRadius") or 0)
    # 实测居民离最近的交互体 ≥ 8 m（SkyIslandInteractionCompetitionPropertyTest）；
    # 半径放大就可能走到别人的交互体上，官方按最近距离取唯一目标，会把装置挡掉。
    if expected and radius > 3.0:
        errors.append("%s 的漫步半径 %.1f m 太大，会走到别的交互体跟前" % (npc_id, radius))

if errors:
    print("SkyIslandChatterGuard: FAIL")
    for line in errors:
        print("  - " + line)
    raise SystemExit(1)
print("SkyIslandChatterGuard: PASS (%d 个话语池，%d 句台词)" % (len(pools), len(seen_cn) + windhunter_lines))
