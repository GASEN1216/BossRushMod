# -*- coding: utf-8 -*-
"""build_wiki_enemy_stats.py - 在线 Wiki 的敌人数据（生命 / 元素承伤 / 免疫异常）生成器。

为什么要生成，而不是在 Wiki 里手写：
    玩家反馈「敌怪的抗性没标出来」。抗性（官方叫「承伤倍率」）由两部分决定：
      1. 敌人借用的官方底模 preset 上的 elementFactor_* 与 buffResist；
      2. 本 Mod 在那之上做的改动（龙裔 / 龙皇把火焰伤害下一帧补回、幽灵女巫把灵伤压回 ×1、
         残星匠首过热时物理承伤 +0.25……）。
    第 1 部分只存在于游戏资源里，第 2 部分散在各 Boss 的代码里。手抄进 Wiki 的话，
    改一次血量就要记得改三四处（2026-11-19 核对时就发现蚀日花冠速查框写着 5000，代码是 7500）。

    所以这里把两部分合起来算一次，产物写成一份 JSON，Wiki 的三处展示都只读它：
      - wiki-site/docs/.vitepress/data/enemy-stats.json   ← 本脚本产物（提交）
        · infobox.mts 用它填 Boss 速查框的生命 / 抗性 / 弱点行；
        · WikiEnemyTable.vue 用它画各页的抗性表（config.mts 的 enemyStatsSlotPlugin 负责插到哪一节）；
      - WikiContent/zh|en 里各 Boss 页「基础数据」小节的三行（抗性 / 弱点 / 免疫异常）
        由 `--write-content` 回写，游戏内那本 Wiki 书也能看到同样的文字。

数据来源：
    - 代码常量：按 (文件, 常量名) 用正则读当前值，记进产物的 refs；
      tests/WikiEnemyStatsGuard.py 在没有官方快照的机器上（CI、公开仓库）也会逐条复核这些值，
      代码一改、产物没重生成就会红。
    - 官方 preset：docs/reference/duckov-creatures-2026-09-25.html（官方 Wiki 生物图鉴页的快照，
      local-only，走私有仓库同步）。本机没有快照时脚本拒绝生成，免得写出一份缺数的产物。
      与 docs/reference/ModeH官方Boss预制体属性.md 同一份来源：它是官方 Wiki 的展示快照，
      游戏更新后要重新抓一次再重跑本脚本。

用法：
    python tools/build_wiki_enemy_stats.py                 # 重新生成 JSON
    python tools/build_wiki_enemy_stats.py --write-content # 同时回写 WikiContent 里的三行
    python tools/build_wiki_enemy_stats.py --check         # 只比对，不写文件（有差异时退出码 1）
"""
import argparse
import csv
import io
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SNAPSHOT = os.path.join(REPO, "docs", "reference", "duckov-creatures-2026-09-25.html")
SNAPSHOT_SOURCE = "https://escapefromduckov.net/zh/wiki/creatures"
SNAPSHOT_DATE = "2026-09-25"
LOC_EN = os.path.join(REPO, "docs", "reference", "official-localization", "English.csv")
OUT = os.path.join(REPO, "wiki-site", "docs", ".vitepress", "data", "enemy-stats.json")

# 显示顺序：与游戏里「承伤倍率」那几行的习惯一致，物理在前，灵（Psionic）在最后。
ELEMENTS = ["physics", "fire", "ice", "poison", "electricity", "space", "ghost"]
ELEMENT_NAMES = {
    # 官方本地化 Stat_ElementFactor_* 去掉「承伤倍率」后的叫法
    "physics": ("物理", "Physical"),
    "fire": ("火", "Fire"),
    "ice": ("冰", "Ice"),
    "poison": ("毒", "Poison"),
    "electricity": ("电", "Electric"),
    "space": ("空间", "Space"),
    "ghost": ("灵", "Psionic"),
}
# buffResist 里对玩家有意义的那几种异常；名字取官方 Buff_* 的本地化。
# StormProtection / Ghost / Nauseous 不是玩家打得出来的异常，不列。
STATUS_NAMES = {
    "Bleeding": ("出血", "Bleed"),
    "Poison": ("中毒", "Poison"),
    "Electric": ("触电", "Electric Shock"),
    "Burning": ("点燃", "Ignite"),
    "Space": ("扰动", "Disturbance"),
    "Freeze": ("冰冻", "Chilled"),
    "Stun": ("震慑", "Stun"),
    "Pain": ("疼痛", "Pain"),
}
STATUS_ORDER = list(STATUS_NAMES.keys())


# ── 读代码常量 ─────────────────────────────────────────────────────────
_file_cache = {}


def read_source(rel):
    if rel not in _file_cache:
        with open(os.path.join(REPO, rel), "r", encoding="utf-8-sig") as fh:
            _file_cache[rel] = fh.read()
    return _file_cache[rel]


def const_value(rel, symbol):
    """`internal const float BaseHealth = 800f;` 或 `BossHealth = 1000f, BossDamage = ...` 里的数。"""
    text = read_source(rel)
    m = re.search(r"\b%s\s*=\s*(-?\d+(?:\.\d+)?)f?\b" % re.escape(symbol), text)
    if not m:
        raise SystemExit("build_wiki_enemy_stats: %s 里找不到常量 %s" % (rel, symbol))
    v = float(m.group(1))
    return int(v) if v.is_integer() else v


class Refs(object):
    """记下本条数据用到的每个代码常量，产物里原样留档，守卫逐条复核。"""

    def __init__(self):
        self.items = []

    def const(self, rel, symbol):
        v = const_value(rel, symbol)
        self.items.append({"file": rel, "symbol": symbol, "value": v})
        return v

    def literal(self, rel, text):
        """名字等字符串：要求它作为字面量出现在该文件里（守卫同样复核）。"""
        if '"%s"' % text not in read_source(rel):
            raise SystemExit('build_wiki_enemy_stats: %s 里找不到字面量 "%s"' % (rel, text))
        self.items.append({"file": rel, "literal": text})
        return text


# ── 官方快照 ──────────────────────────────────────────────────────────
def load_presets():
    if not os.path.isfile(SNAPSHOT):
        raise SystemExit("build_wiki_enemy_stats: 缺官方快照 %s（local-only，先跑私有仓库同步）" % SNAPSHOT)
    with open(SNAPSHOT, "r", encoding="utf-8") as fh:
        raw = fh.read().replace('\\"', '"')
    decoder = json.JSONDecoder()
    presets = {}
    for m in re.finditer(r'\{"id":"([A-Za-z0-9_ ]+)","name":', raw):
        try:
            obj, _ = decoder.raw_decode(raw, m.start())
        except ValueError:
            continue
        if "elementFactor" in obj and "combatStats" in obj:
            presets[obj["id"]] = obj
    if len(presets) < 100:
        raise SystemExit("build_wiki_enemy_stats: 快照只解析出 %d 个 preset，格式可能变了" % len(presets))
    return presets


def unescape_loc(value):
    v = value.strip()
    if len(v) >= 2 and v[0] == '"' and v[-1] == '"':
        v = v[1:-1]
    out, i = [], 0
    while i < len(v):
        if v[i] == "\\" and i + 1 < len(v):
            out.append(v[i + 1])
            i += 2
        else:
            out.append(v[i])
            i += 1
    return "".join(out).replace("​", "").strip()


def load_english_names():
    table = {}
    if not os.path.isfile(LOC_EN):
        return table
    with open(LOC_EN, "r", encoding="utf-8-sig", newline="") as fh:
        for row in csv.reader(fh):
            if len(row) >= 2 and row[0]:
                table[row[0].strip()] = unescape_loc(row[1])
    return table


def preset_elements(preset):
    ef = preset["elementFactor"]
    return {k: float(ef.get(k, 1)) for k in ELEMENTS}


def preset_status(preset):
    resist = preset.get("buffs", {}).get("buffResist", []) or []
    return [s for s in STATUS_ORDER if s in resist]


def num(v):
    v = round(float(v), 3)
    return int(v) if v.is_integer() else v


# ── 文案：抗性 / 弱点 / 免疫异常 ─────────────────────────────────────
def fmt_factor(v):
    return ("%g" % v)


def summarize(elements, notes, status, lang):
    """把 7 个倍率折成两句人话：低于 1 的是抗性，高于 1 的是弱点，等于 1 的不提。

    倍率相同的元素并成一组（「冰、毒、空间 ×1.35」），组与组之间用分号隔开；
    带 note 的元素（比如被代码补回血的火焰）不按数字说，用 note 的说法排在最前。
    """
    zh = lang == "zh"
    name = lambda k: ELEMENT_NAMES[k][0 if zh else 1]
    inner = "、" if zh else ", "
    outer = "；" if zh else "; "
    resist, weak, special = [], [], []
    for k in ELEMENTS:
        if k in notes:
            special.append(notes[k][lang])
            continue
        v = elements[k]
        if isinstance(v, list):  # 范围：底模有几个同名变体时
            lo, hi = v
            text = "%s ×%s–%s" % (name(k), fmt_factor(lo), fmt_factor(hi))
            (weak if lo >= 1 else resist).append((lo, text, k))
            continue
        if v == 1:
            continue
        (resist if v < 1 else weak).append((v, None, k))

    def group(rows, reverse):
        rows = sorted(rows, key=lambda r: (-r[0] if reverse else r[0], ELEMENTS.index(r[2])))
        out, i = [], 0
        while i < len(rows):
            if rows[i][1] is not None:
                out.append(rows[i][1])
                i += 1
                continue
            j, keys = i, []
            while j < len(rows) and rows[j][1] is None and rows[j][0] == rows[i][0]:
                keys.append(name(rows[j][2]))
                j += 1
            if rows[i][0] == 0:
                out.append(("%s ×0（完全免疫）" if zh else "%s ×0 (immune)") % inner.join(keys))
            else:
                out.append("%s ×%s" % (inner.join(keys), fmt_factor(rows[i][0])))
            i = j
        return out

    none = "无" if zh else "none"
    resist_parts = special + group(resist, False)
    weak_parts = group(weak, True)
    return {
        "resist": outer.join(resist_parts) if resist_parts else none,
        "weak": outer.join(weak_parts) if weak_parts else none,
        "status": inner.join(STATUS_NAMES[s][0 if zh else 1] for s in status) if status else none,
    }


# ── 敌人清单 ──────────────────────────────────────────────────────────
def T(zh, en):
    return {"zh": zh, "en": en}


FIRE_HEAL = T("火焰打掉的血下一帧原数补回，等于免疫", "fire damage is healed back the next frame, so it does nothing")


def make(eid, group, page, names, presets, preset_id, health, refs, icon=None, role=None,
         element_override=None, notes_override=None, status=None, health_note=None, note=None, anchor=None):
    preset = presets[preset_id]
    elements = preset_elements(preset)
    for k, v in (element_override or {}).items():
        elements[k] = v
    notes = dict(notes_override or {})
    st = preset_status(preset) if status is None else status
    entry = {
        "id": eid,
        "group": group,
        "zh": names[0],
        "en": names[1],
        "page": page,
        "health": num(health) if not isinstance(health, dict) else health,
        "elements": {k: (num(elements[k]) if not isinstance(elements[k], list) else [num(x) for x in elements[k]])
                     for k in ELEMENTS},
        "status": st,
        "base": {"preset": preset_id, "zh": preset.get("name") or "", "nameKey": preset.get("nameKey") or ""},
        "refs": refs.items,
    }
    if anchor:
        entry["anchor"] = anchor
    if icon:
        entry["icon"] = icon
    if role:
        entry["role"] = role
    if notes:
        entry["elementNotes"] = notes
    if health_note:
        entry["healthNote"] = health_note
    if note:
        entry["note"] = note
    entry["summary"] = {lang: summarize(elements, notes, st, lang) for lang in ("zh", "en")}
    return entry


def custom_bosses(presets):
    out = []
    # 龙裔遗族 / 焚天龙皇：底模都是官方「???」(Cname_Boss_Red)。火焰伤害在 Health.OnHurt 之后按火焰占比
    # 原数加回（DragonDescendantBoss_RuntimeAndCleanup / DragonKingBoss 的 Hurt 回调），净效果为零。
    for eid, cfg, cfg_file, page, icon, zh, en, hurt_file in (
        ("dragon-descendant", "DragonDescendantConfig", "Integration/DragonDescendant/DragonDescendantConfig.cs",
         "/bosses/dragon-descendant", "dragondescendant", "龙裔遗族", "Dragon Descendant",
         "Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs"),
        ("dragon-king", "DragonKingConfig", "Integration/DragonKing/DragonKingConfig.cs",
         "/bosses/dragon-king", "boss_dragonking", "焚天龙皇", "Skyburner Dragon Lord",
         "Integration/DragonKing/DragonKingBoss.cs"),
    ):
        r = Refs()
        names = (r.literal(cfg_file, zh), r.literal(cfg_file, en))
        hp = r.const(cfg_file, "BaseHealth")
        r.literal(cfg_file, "Cname_Boss_Red") if eid == "dragon-descendant" else None
        # 火焰补回：回调里把火焰因子置 0 并延迟 AddHealth，这一句是行为锚点
        if "ElementTypes.fire, 0f" not in read_source(hurt_file):
            raise SystemExit("build_wiki_enemy_stats: %s 的火焰补回逻辑变了，先核对再改文案" % hurt_file)
        r.items.append({"file": hurt_file, "contains": "ElementTypes.fire, 0f"})
        out.append(make(eid, "custom-bosses", page, names, presets, "EnemyPreset_Boss_Red", hp, r, icon=icon,
                        role=T("本 Mod 原创 Boss", "Original boss"),
                        element_override={"fire": 0}, notes_override={"fire": FIRE_HEAL}))

    # 幽灵女巫：底模官方幽灵（Cname_Ghost），物理只吃 15%。灵伤系数原版 6.5，女巫在
    # PhantomWitchAbilityController.NormalizeGhostElementFactor 里压回 1（只对大于 1 的情况）。
    r = Refs()
    cfg_file = "Integration/PhantomWitch/PhantomWitchConfig.cs"
    names = (r.literal(cfg_file, "幽灵女巫"), r.literal(cfg_file, "Phantom Witch"))
    hp = r.const(cfg_file, "BaseHealth")
    r.literal(cfg_file, "Cname_Ghost")
    ctl = "Integration/PhantomWitch/PhantomWitchAbilityController.cs"
    if 'GetStat("ElementFactor_Ghost")' not in read_source(ctl):
        raise SystemExit("build_wiki_enemy_stats: 幽灵女巫的灵伤归一逻辑变了，先核对")
    r.items.append({"file": ctl, "contains": 'GetStat("ElementFactor_Ghost")'})
    ghost = presets["EnemyPreset_Animal_Ghost"]["elementFactor"]["ghost"]
    out.append(make("phantom-witch", "custom-bosses", "/bosses/phantom-witch", names, presets,
                    "EnemyPreset_Animal_Ghost", hp, r, icon="boss_phantomwitch",
                    role=T("本 Mod 原创 Boss", "Original boss"),
                    element_override={"ghost": min(ghost, 1.0)},
                    note=T("底模是官方幽灵：原版灵伤 ×%s，女巫改回 ×1" % fmt_factor(ghost),
                           "built on the vanilla ghost, which takes ×%s Psionic; the witch is set back to ×1"
                           % fmt_factor(ghost))))

    # 蚀日花冠 / 冠军之影：都按 Cname_Boss_Red → Cname_Ghost → Cname_Boss_Shot 的顺序找底模，
    # 官方「???」一直都在，实际取到的就是它。
    r = Refs()
    pool = "Integration/EclipseBloom/EclipseBloomPoolAdapter.cs"
    names = (r.literal(pool, "蚀日花冠"), r.literal(pool, "Eclipse Bloom"))
    hp = r.const("Integration/EclipseBloom/EclipseBloomRules.cs", "BaseHealth")
    r.literal("Integration/EclipseBloom/EclipseBloomBoss.cs", "Cname_Boss_Red")
    out.append(make("eclipse-bloom", "custom-bosses", "/bosses/eclipse-bloom", names, presets,
                    "EnemyPreset_Boss_Red", hp, r, role=T("独立挑战 Boss", "Standalone boss"),
                    health_note=T("鸭王征程里另乘章节倍率", "campaign chapters apply their own multiplier"),
                    note=T("四片花瓣没碎时替核心挡伤害", "unbroken petals shield the core")))

    r = Refs()
    pool = "Integration/SandstormChampion/SandstormChampionPoolAdapter.cs"
    names = (r.literal(pool, "冠军之影"), r.literal(pool, "Shadow of the Champion"))
    hp = r.const("Integration/SandstormChampion/SandstormChampionConfig.cs", "BaseHealth")
    r.literal("Integration/SandstormChampion/SandstormChampionConfig.cs", "Cname_Boss_Red")
    out.append(make("sandstorm-champion", "custom-bosses", "/systems/campaign", names, presets,
                    "EnemyPreset_Boss_Red", hp, r, icon="portrait-champion",
                    role=T("鸭王征程第 6 章", "Duck King Campaign, chapter 6"),
                    health_note=T("鸭王征程里另乘章节倍率", "campaign chapters apply their own multiplier"),
                    note=T("转阶段时短暂无敌", "briefly invulnerable while changing phase"),
                    anchor=T("第 6 章 · 守擂的那个", "Chapter 6 · The One Holding the Ring")))
    return out


SKY_RULES = "SkyIsland/SkyIslandBossRules.cs"
# SkyIslandCombatPreset.Apply：头目档（Chief / Lord / Champion / Storm）先按参照 Boss 原版 × Multiplier，
# 再抬到 BossHealthFloor，最后乘设置里的 Boss 生命比例；口径写在 build() 的分组说明里。
SKY_BALANCE = "SkyIsland/SkyIslandCombatBalance.cs"


def sky_baseline_health(preset_id):
    text = read_source(SKY_BALANCE)
    m = re.search(r'PresetId = "%s",\s*Health = (\d+(?:\.\d+)?)f' % re.escape(preset_id), text)
    if not m:
        raise SystemExit("build_wiki_enemy_stats: SkyIslandCombatBalance 里没有 %s 的基准" % preset_id)
    return float(m.group(1))


def sky_island(presets):
    rules = read_source(SKY_RULES)
    # 中英名：NameCn / NameEn 两个 switch，加断风游猎的追 / 伏 / 守后缀
    def switch_names(func):
        body = re.search(r"static string %s\(SkyIslandBossKind kind\)(.*?)default:" % func, rules, re.S).group(1)
        return dict(re.findall(r"case SkyIslandBossKind\.(\w+): return \"([^\"]+)\";", body))
    cn, en = switch_names("NameCn"), switch_names("NameEn")
    variants_cn = dict(re.findall(r"case (Windhunter\w+): return \" · (.)\";", rules))
    variants_en = dict(re.findall(r"case (Windhunter\w+): return \" (\w+)\";", rules))
    # 装备数值：TypeId -> (StatKey, StatValue)
    specs = {}
    for m in re.finditer(r"TypeId = BossRushItemIds\.(\w+), Slot = \"(\w+)\", StatKey = \"(\w+)\", StatValue = ([\d.]+)f", rules):
        specs[m.group(1)] = (m.group(3), float(m.group(4)))

    out = []
    r0 = Refs()
    mult = r0.const(SKY_BALANCE, "Multiplier")
    floor = r0.const(SKY_BALANCE, "BossHealthFloor")
    prelude = r0.const(SKY_BALANCE, "PreludeWardenHealthFactor")
    for m in re.finditer(r"new SkyIslandBossProfile\s*\{(.*?)\n            \}", rules, re.S):
        body = m.group(1)
        kind = re.search(r"Kind = SkyIslandBossKind\.(\w+)", body).group(1)
        tier = re.search(r"Tier = SkyIslandEnemyTier\.(\w+)", body).group(1)
        pid = re.search(r'VanillaPresetId = "(\w+)"', body).group(1)
        bid = re.search(r'\bId = "(\w+)"', body).group(1)
        variant = re.search(r"Variant = (Windhunter\w+)", body)
        zh, en_name = cn[kind], en[kind]
        if variant:
            zh += " · " + variants_cn[variant.group(1)]
            en_name += " " + variants_en[variant.group(1)]
        r = Refs()
        r.items.extend(r0.items)
        base = sky_baseline_health(pid)
        r.items.append({"file": SKY_BALANCE, "baseline": pid, "value": num(base)})
        hp = max(base * mult, floor)
        head = body_armor = 0.0
        gear = re.findall(r"Piece\(\"\w+\", BossRushItemIds\.(\w+), \d+\)", body)
        for g in gear:
            key, val = specs.get(g, (None, 0))
            if key == "HeadArmor":
                head += val
            elif key == "BodyArmor":
                body_armor += val
        role = T("岛主", "Island lord") if tier == "Lord" else T("头目", "Chief")
        parts_zh, parts_en = [], []
        if head or body_armor:
            parts_zh.append("护甲 头 %g / 身 %g（专属装备）" % (head, body_armor))
            parts_en.append("armor head %g / body %g (signature gear)" % (head, body_armor))
        override, notes = {}, {}
        if kind == "Foreman":
            shield = r.const(SKY_RULES, "ShieldArmor")
            overheat = r.const(SKY_RULES, "OverheatDamageTaken")
            parts_zh.append("供能桩立着时头身护甲各 +%g；过热几秒内物理承伤 +%g" % (shield, overheat))
            parts_en.append("+%g head and body armor while a pylon stands; +%g physical damage taken while overheated"
                            % (shield, overheat))
        if bid == "WindhunterWarden":
            parts_zh.append("零号区序章那一位血量再 ×%g（%g）" % (prelude, hp * prelude))
            parts_en.append("×%g health (%g) in the Ground Zero prelude" % (prelude, hp * prelude))
        eid = "sky-" + re.sub(r"(?<!^)([A-Z])", r"-\1", bid).lower()
        out.append(make(eid, "sky-island-bosses", "/maps/sky-island", (zh, en_name), presets, pid, hp, r,
                        role=role,
                        note=T("；".join(parts_zh), "; ".join(parts_en)) if parts_zh else None))

    # 剧情对手与噬风：SkyIslandCombatBalance.For 里写死的参照
    story = [
        ("sky-zheling", "EnemyPreset_Boss_Killa", "SkyIsland/SkyIslandEncounters.cs", "折翎", "Zheling",
         T("剧情对手", "Story rival"), None),
        ("sky-bellkeeper", "EnemyPreset_Boss_SnowMan", "SkyIsland/SkyIslandEncounters.cs", "失控的守钟装置",
         "Runaway Bell Engine", T("剧情对手", "Story rival"), None),
    ]
    for eid, pid, name_file, zh, en_name, role, note in story:
        r = Refs()
        r.items.extend(r0.items)
        r.literal(SKY_BALANCE, pid)
        names = (r.literal(name_file, zh), r.literal(name_file, en_name))
        base = sky_baseline_health(pid)
        r.items.append({"file": SKY_BALANCE, "baseline": pid, "value": num(base)})
        out.append(make(eid, "sky-island-bosses", "/maps/sky-island", names, presets, pid, max(base * mult, floor), r,
                        role=role))
    r = Refs()
    r.items.extend(r0.items)
    pid = "EnemyPreset_Boss_Island_Koukou"
    r.literal(SKY_BALANCE, pid)
    names = (r.literal("SkyIsland/SkyIslandEnemyTiers.cs", "噬风"), r.literal("SkyIsland/SkyIslandEnemyTiers.cs", "Windeater"))
    base = sky_baseline_health(pid)
    r.items.append({"file": SKY_BALANCE, "baseline": pid, "value": num(base)})
    out.append(make("sky-windeater", "sky-island-bosses", "/maps/sky-island", names, presets, pid, base * mult, r,
                    role=T("风灾", "The storm"),
                    note=T("回响与首战同一套数值", "its echo uses the same numbers"),
                    anchor=T("噬风", "The Windeater")))
    return out


def sky_pool(presets, english):
    """天空岛小兵、巡守与断风游猎：从官方 Boss 池按位置抽一位（SkyIslandEnemySources.IsPoolBoss）。"""
    src = read_source("SkyIsland/SkyIslandEnemySources.cs")
    for needle in ('"_NPC_"', '"Koukou"', '"_Test"', "Teams.player", "Teams.middle"):
        if needle not in src:
            raise SystemExit("build_wiki_enemy_stats: SkyIslandEnemySources.IsPoolBoss 的口径变了（缺 %s）" % needle)
    mult = const_value(SKY_BALANCE, "Multiplier")
    rows = {}
    for pid, p in sorted(presets.items()):
        if not p.get("isBoss") or not pid.startswith("EnemyPreset_"):
            continue
        if p.get("team") in ("player", "middle"):
            continue
        if "_NPC_" in pid or "koukou" in pid.lower() or pid.lower().endswith("_test"):
            continue
        key = (p.get("nameKey"), json.dumps(p["elementFactor"], sort_keys=True), tuple(preset_status(p)))
        hp = p["combatStats"]["health"] * mult
        if key in rows:
            rows[key]["presets"].append(pid)
            rows[key]["hp"].add(num(hp))
            continue
        rows[key] = {"p": p, "presets": [pid], "hp": {num(hp)}}
    out = []
    for key, row in rows.items():
        p = row["p"]
        nk = p.get("nameKey") or ""
        zh = p.get("name") or nk
        en_name = english.get(nk) or zh
        hps = sorted(row["hp"])
        health = hps[0] if len(hps) == 1 else {"min": hps[0], "max": hps[-1]}
        refs = Refs()
        entry = make("pool-" + row["presets"][0][len("EnemyPreset_"):].lower().replace(" ", "-"), "sky-island-pool",
                     "/maps/sky-island", (zh, en_name), presets, row["presets"][0], health, refs,
                     icon=nk.lower() if nk else None)
        entry["base"]["presets"] = row["presets"]
        out.append(entry)
    out.sort(key=lambda e: (e["zh"], e["id"]))
    return out


def tidegate(presets):
    ident = "Tidegate/TidegateEnemyIdentity.cs"
    tune = "Tidegate/TidegateCombatTuning.cs"
    scav = presets["EnemyPreset_Scav"]["combatStats"]["health"]
    wolf = presets["EnemyPreset_Animal_Wolf"]["combatStats"]["health"]
    out = []
    groups = [
        ("tidegate-regular", ["突击队员", "码头游荡者", "芦苇伏兵", "淘沙帮伙计", "淘沙帮投掷手", "岬角巡哨", "水厂狙击手"],
         ["Assault Trooper", "Dock Prowler", "Reed Ambusher", "Dredge Gang Hand", "Dredge Grenadier", "Cape Scout",
          "Waterworks Marksman"], "EnemyPreset_Scav", "HealthFactor", scav),
        ("tidegate-sturdy", ["突击队长", "水厂警卫", "灯塔守卫"], ["Assault Captain", "Waterworks Guard", "Lighthouse Guard"],
         "EnemyPreset_Scav", "GuardHealthFactor", scav),
        ("tidegate-hound", ["巡街猎犬"], ["Patrol Hound"], "EnemyPreset_Animal_Wolf", "HealthFactor", wolf),
    ]
    for eid, zhs, ens, pid, factor, base in groups:
        r = Refs()
        for z, e in zip(zhs, ens):
            r.literal(ident, z)
            r.literal(ident, e)
        f = r.const(tune, factor)
        if pid == "EnemyPreset_Animal_Wolf":
            r.literal("Tidegate/TidegateHound.cs", pid)
        else:
            r.literal("Tidegate/TidegateSandBoss.cs", pid)
        entry = make(eid, "tidegate", "/maps/tidegate", ("、".join(zhs), " / ".join(ens)), presets, pid, base * f, r,
                     health_note=T("官方%s %g 血 ×%g" % ("狼" if "Wolf" in pid else "拾荒者", base, f),
                                   "vanilla %s %g HP ×%g" % ("wolf" if "Wolf" in pid else "scavenger", base, f)))
        entry["members"] = [{"zh": z, "en": e} for z, e in zip(zhs, ens)]
        out.append(entry)
    r = Refs()
    names = (r.literal(ident, "沙老大·铅锤"), r.literal(ident, "Leadline, the Dredge Boss"))
    hp = r.const("Tidegate/TidegateSandBoss.cs", "BossHealth")
    r.literal("Tidegate/TidegateSandBoss.cs", "EnemyPreset_Scav")
    out.append(make("tidegate-boss", "tidegate", "/maps/tidegate", names, presets, "EnemyPreset_Scav", hp, r,
                    role=T("地图 Boss", "Map boss"), note=T("底模是官方拾荒者，只把血量定成 %g" % hp,
                                                          "a vanilla scavenger body with health set to %g" % hp)))
    return out


def last_shift(presets):
    ident = "LastShiftYard/LastShiftEnemyIdentity.cs"
    scav = presets["EnemyPreset_Scav"]["combatStats"]["health"]
    out = []
    for eid, zhs, ens, factor in (
        ("lastshift-regular", ["巡道工", "检车员"], ["Track Walker", "Carriage Inspector"], "HealthFactor"),
        ("lastshift-escort", ["押运员", "押运队长"], ["Escort Guard", "Escort Captain"], "EscortHealthFactor"),
        ("lastshift-leader", ["信号工长", "调车长", "押运班长"], ["Signal Foreman", "Shunting Master", "Escort Sergeant"],
         "LeaderHealthFactor"),
    ):
        r = Refs()
        for z, e in zip(zhs, ens):
            r.literal(ident, z)
            r.literal(ident, e)
        f = r.const(ident, factor)
        r.literal("LastShiftYard/LastShiftEnemies.cs", "EnemyPreset_Scav")
        entry = make(eid, "last-shift", "/maps/last-shift-yard", ("、".join(zhs), " / ".join(ens)), presets,
                     "EnemyPreset_Scav", scav * f, r,
                     health_note=T("官方拾荒者 %g 血 ×%g" % (scav, f), "vanilla scavenger %g HP ×%g" % (scav, f)))
        entry["members"] = [{"zh": z, "en": e} for z, e in zip(zhs, ens)]
        out.append(entry)
    r = Refs()
    names = (r.literal(ident, "押运长"), r.literal(ident, "Chief Escort"))
    hp = r.const("LastShiftYard/LastShiftEscortChief.cs", "BossHealth")
    r.literal("LastShiftYard/LastShiftEscortChief.cs", "EnemyPreset_Boss_Roadblock")
    out.append(make("lastshift-chief", "last-shift", "/maps/last-shift-yard", names, presets,
                    "EnemyPreset_Boss_Roadblock", hp, r, role=T("地图 Boss", "Map boss"),
                    note=T("底模是官方「路障」", "built on the vanilla Roadblock boss")))
    return out


def zombie(presets):
    """丧尸模式所有丧尸（普通 / 特殊 / 精英 / 五种 Boss）都从同一个 Cname_Zombie 底模刷
    （ZombieModeSpawner 的 ZOMBIE_MODE_NORMAL_PRESET_NAME）。游戏里有五个同名变体，
    ModeD 的预设缓存取到哪一个由引擎的枚举顺序决定，所以毒伤写成范围。"""
    r = Refs()
    r.literal("ZombieMode/ZombieModeSpawner.cs", "Cname_Zombie")
    variants = sorted(pid for pid, p in presets.items() if p.get("nameKey") == "Cname_Zombie")
    elements = {}
    for k in ELEMENTS:
        vals = sorted({float(presets[v]["elementFactor"].get(k, 1)) for v in variants})
        elements[k] = vals[0] if len(vals) == 1 else [vals[0], vals[-1]]
    common_status = [s for s in STATUS_ORDER if all(s in preset_status(presets[v]) for v in variants)]
    entry = make("zombie", "zombie", "/game-modes/zombie-mode", ("丧尸（全部种类）", "Zombies (every type)"),
                 presets, variants[0], T("见上文倍率", "see the multipliers above"), r,
                 element_override=elements, status=common_status,
                 note=T("普通、特殊、精英和五种 Boss 共用这一底模；毒伤倍率随游戏载入的丧尸变体在 %s 之间"
                        % "–".join(fmt_factor(x) for x in elements["poison"]) if isinstance(elements["poison"], list) else
                        "普通、特殊、精英和五种 Boss 共用这一底模",
                        "normal, special, elite zombies and all five bosses share this body; poison varies %s with "
                        "the zombie variant the game loads" % "–".join(fmt_factor(x) for x in elements["poison"])
                        if isinstance(elements["poison"], list) else
                        "normal, special, elite zombies and all five bosses share this body"))
    entry["base"]["presets"] = variants
    return [entry]


def mimic(presets):
    rules = "Integration/FieldAdventures/Mimic/GreedyMimicRules.cs"
    r = Refs()
    enc = "Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs"
    names = (r.literal(enc, "贪吃宝箱怪"), r.literal(enc, "Gluttonous Mimic"))
    m = re.search(r"MaximumHealth \{ get \{ return (\d+)f \+ Meals \* (\d+)f; \} \}", read_source(rules))
    if not m:
        raise SystemExit("build_wiki_enemy_stats: GreedyMimicRules.MaximumHealth 的写法变了")
    base, per = int(m.group(1)), int(m.group(2))
    meals = r.const(rules, "MaximumMeals")
    shell = r.const(rules, "ShellArmor")
    r.items.append({"file": rules, "contains": m.group(0)})
    # 它不是官方角色，用的是 HealthSimpleBase：不吃元素倍率，也挂不上任何异常状态。
    entry = {
        "id": "mimic", "group": "mimic", "zh": names[0], "en": names[1], "page": "/systems/field-adventures",
        "health": {"min": base, "max": base + per * meals},
        "healthNote": T("每喂一样东西 +%d，最多喂 %d 次" % (per, meals), "+%d per meal, up to %d meals" % (per, meals)),
        "elements": {k: 1 for k in ELEMENTS},
        "status": [],
        "statusAll": True,
        "note": T("闭着嘴时所有伤害 ×%g，张嘴时全额；只有你打的伤害算数" % shell,
                  "all damage ×%g while its mouth is shut, full damage while open; only your own hits count" % shell),
        "base": {"preset": "", "zh": "", "nameKey": ""},
        "refs": r.items,
        "anchor": T("贪吃宝箱怪", "Gluttonous Mimic"),
    }
    entry["summary"] = {
        "zh": {"resist": "闭嘴时所有伤害 ×%g" % shell, "weak": "无", "status": "全部（挂不上异常）"},
        "en": {"resist": "all damage ×%g while its mouth is shut" % shell, "weak": "none",
               "status": "all (statuses never stick)"},
    }
    return [entry]


GROUPS = [
    ("custom-bosses", T("原创 Boss 承伤倍率", "Original bosses: damage taken"), ),
    ("sky-island-bosses", T("天空岛头目、岛主与剧情对手", "Sky Islands chiefs, lords and story rivals")),
    ("sky-island-pool", T("岛上小兵与巡守可能抽到的官方 Boss", "Vanilla bosses that can fill island patrol slots")),
    ("tidegate", T("潮闸镇的敌人", "Tidegate enemies")),
    ("last-shift", T("末班编组站的敌人", "Last Shift Yard enemies")),
    ("zombie", T("丧尸的承伤倍率", "Zombie damage taken")),
    ("mimic", T("贪吃宝箱怪", "Gluttonous Mimic")),
]


def build():
    presets = load_presets()
    english = load_english_names()
    enemies = []
    enemies += custom_bosses(presets)
    enemies += sky_island(presets)
    enemies += sky_pool(presets, english)
    enemies += tidegate(presets)
    enemies += last_shift(presets)
    enemies += zombie(presets)
    enemies += mimic(presets)
    ids = [e["id"] for e in enemies]
    if len(ids) != len(set(ids)):
        raise SystemExit("build_wiki_enemy_stats: 敌人 id 重复")
    # 整组共用的说明写在表下，不在每一行重复（天空岛头目档的生命口径、巡守的 ×1.5）
    mult = const_value(SKY_BALANCE, "Multiplier")
    floor = const_value(SKY_BALANCE, "BossHealthFloor")
    group_notes = {
        "sky-island-bosses": T("生命按参照 Boss 的原版 ×%g 算，不足 %g 的抬到 %g。" % (mult, floor, floor),
                               "Health is the reference boss's vanilla value ×%g, raised to %g if lower."
                               % (mult, floor)),
        "sky-island-pool": T("生命已按岛上的 ×%g 算好。" % mult, "Health already includes the islands' ×%g." % mult),
    }
    groups = []
    for gid, title in GROUPS:
        g = {"id": gid, "zh": title["zh"], "en": title["en"],
             "enemies": [e["id"] for e in enemies if e["group"] == gid]}
        if gid in group_notes:
            g["note"] = group_notes[gid]
        groups.append(g)
    return {
        "schema": 1,
        "generatedBy": "tools/build_wiki_enemy_stats.py",
        "snapshot": {"source": SNAPSHOT_SOURCE, "fetchedAt": SNAPSHOT_DATE},
        "elements": [{"key": k, "zh": ELEMENT_NAMES[k][0], "en": ELEMENT_NAMES[k][1]} for k in ELEMENTS],
        "statuses": [{"key": s, "zh": STATUS_NAMES[s][0], "en": STATUS_NAMES[s][1]} for s in STATUS_ORDER],
        "groups": groups,
        "enemies": enemies,
    }


# ── 回写 WikiContent：Boss 页「基础数据」里的三行 ───────────────────────
CONTENT_PAGES = {
    # enemy id -> (源文件相对 WikiContent/<lang>/，所在小节标题 zh / en)
    "dragon-descendant": ("boss/boss__dragon_descendant.md", "### 基础数据", "### Base Stats"),
    "dragon-king": ("boss/boss__dragon_king.md", "### 基础数据", "### Base Stats"),
    "phantom-witch": ("boss/boss__phantom_witch.md", "### 基础数据", "### Base Stats"),
}
LINE_LABELS = {
    "zh": {"health": "- 生命值：", "resist": "- 抗性：", "weak": "- 弱点：", "status": "- 免疫异常："},
    "en": {"health": "- HP: ", "resist": "- Resists: ", "weak": "- Weak to: ", "status": "- Status immunities: "},
}


def content_lines(enemy, lang):
    s = enemy["summary"][lang]
    lab = LINE_LABELS[lang]
    h = enemy["health"]
    return [
        (lab["health"], "%g" % h if isinstance(h, (int, float)) else None),
        (lab["resist"], s["resist"]),
        (lab["weak"], s["weak"]),
        (lab["status"], s["status"]),
    ]


def rewrite_section(text, heading, lines):
    """heading 小节里：已有同标签的行原地换掉值；没有的追加在小节第一个列表的末尾。"""
    src = text.split("\n")
    try:
        start = next(i for i, l in enumerate(src) if l.strip() == heading)
    except StopIteration:
        raise SystemExit("build_wiki_enemy_stats: 找不到小节 %s" % heading)
    end = next((i for i in range(start + 1, len(src)) if src[i].startswith("#")), len(src))
    body = src[start + 1:end]
    pending = []
    for label, value in lines:
        if value is None:
            continue
        hit = next((i for i, l in enumerate(body) if l.startswith(label)), None)
        if hit is None:
            pending.append(label + value)
        else:
            body[hit] = label + value
    if pending:
        last_item = None
        for i, l in enumerate(body):
            if l.startswith("- "):
                last_item = i
            elif last_item is not None and l.strip() and not l.startswith("  "):
                break
        if last_item is None:
            raise SystemExit("build_wiki_enemy_stats: %s 小节里没有列表" % heading)
        body = body[:last_item + 1] + pending + body[last_item + 1:]
    return "\n".join(src[:start + 1] + body + src[end:])


def write_content(data, check_only):
    by_id = {e["id"]: e for e in data["enemies"]}
    changed = []
    for eid, (rel, h_zh, h_en) in CONTENT_PAGES.items():
        for lang, heading in (("zh", h_zh), ("en", h_en)):
            path = os.path.join(REPO, "WikiContent", lang, rel)
            with io.open(path, "r", encoding="utf-8", newline="") as fh:
                raw = fh.read()
            crlf = "\r\n" in raw
            text = raw.replace("\r\n", "\n")
            new = rewrite_section(text, heading, content_lines(by_id[eid], lang))
            if new != text:
                changed.append(os.path.relpath(path, REPO))
                if not check_only:
                    with io.open(path, "w", encoding="utf-8", newline="") as fh:
                        fh.write(new.replace("\n", "\r\n") if crlf else new)
    return changed


def dump(data):
    return json.dumps(data, ensure_ascii=False, indent=1) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只比对产物与 WikiContent，不写文件")
    ap.add_argument("--write-content", action="store_true", help="同时回写 WikiContent 里 Boss 页的抗性三行")
    args = ap.parse_args()
    data = build()
    text = dump(data)
    old = None
    if os.path.isfile(OUT):
        with io.open(OUT, "r", encoding="utf-8") as fh:
            old = fh.read()
    if args.check:
        drift = write_content(data, True)
        if old != text or drift:
            print("build_wiki_enemy_stats: FAIL - 产物或 WikiContent 与代码不一致，重跑本脚本（加 --write-content）")
            for d in drift:
                print("  " + d)
            return 1
        print("build_wiki_enemy_stats: OK - %d 个敌人条目与代码一致" % len(data["enemies"]))
        return 0
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print("build_wiki_enemy_stats: 写出 %d 个敌人条目 -> %s" % (len(data["enemies"]), os.path.relpath(OUT, REPO)))
    if args.write_content:
        for c in write_content(data, False):
            print("  回写 " + c)
    return 0


if __name__ == "__main__":
    sys.exit(main())
