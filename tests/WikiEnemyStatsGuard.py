# -*- coding: utf-8 -*-
"""WikiEnemyStatsGuard - 在线 Wiki 的敌人数据（生命 / 承伤倍率 / 免疫异常）必须与代码一致。

背景：
    玩家反馈「敌怪的抗性没标出来」后，Wiki 的抗性表、Boss 速查框与 Boss 页「基础数据」里的
    抗性三行都改由 tools/build_wiki_enemy_stats.py 生成，产物是
    wiki-site/docs/.vitepress/data/enemy-stats.json（提交进仓库）。

    生成器要读官方 preset 快照（docs/reference/，local-only），CI 与公开仓库里重跑不了；
    但产物里的每个数都记下了它取自哪个代码常量（refs）。这个 guard 不需要快照：
      1. 逐条复核 refs：常量的当前值、名字字面量、行为锚点、天空岛战斗基准，任何一条
         与当前代码对不上就红——改了 Boss 血量却忘了重跑生成器，是最常见的漂移；
      2. Boss 页「基础数据」里由生成器回写的四行（生命 / 抗性 / 弱点 / 免疫异常），
         与产物里的摘要逐字相同，中英两版都查；
      3. data/enemy-tables.mts 登记的每处抗性表：分组存在，目标小节标题在中英两版里都在
         （标题改名时表会悄悄消失，渲染期只 warn）；
      4. 接线：config.mts 登记了 enemyStatsSlotPlugin，theme/index.ts 全局注册了 WikiEnemyTable。

修法：重跑 `python tools/build_wiki_enemy_stats.py --write-content`（需要本机有快照）。
"""
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(REPO_ROOT, "wiki-site")
DATA = os.path.join(SITE, "docs", ".vitepress", "data", "enemy-stats.json")
TABLES = os.path.join(SITE, "docs", ".vitepress", "data", "enemy-tables.mts")
CONFIG = os.path.join(SITE, "docs", ".vitepress", "config.mts")
THEME_INDEX = os.path.join(SITE, "docs", ".vitepress", "theme", "index.ts")
ENTRY_MAP = os.path.join(SITE, "scripts", "entry-map.mjs")
HUB_DIR = os.path.join(SITE, "hubs")

sys.path.insert(0, os.path.join(REPO_ROOT, "tools"))
import build_wiki_enemy_stats as gen  # noqa: E402  共用常量读取与「基础数据」行的格式，避免两份实现漂移

ELEMENTS = ["physics", "fire", "ice", "poison", "electricity", "space", "ghost"]
PLACEMENT_RE = re.compile(
    r"\{\s*page:\s*'([^']+)',\s*zh:\s*'([^']+)',\s*en:\s*(['\"])(.+?)\3,\s*group:\s*'([^']+)'")


def fail(message):
    print("WikiEnemyStatsGuard: FAIL - " + message)
    return 1


def read(path):
    with open(path, "r", encoding="utf-8-sig") as fh:
        return fh.read()


def page_sources():
    """规范路径 -> {zh: WikiContent 源文件, en: ...}；类目主页走 hubs/。"""
    text = read(ENTRY_MAP)
    out = {}
    for entry_id, route in re.findall(r"'([a-z_0-9]+)':\s*'([^']+\.md)'", text):
        canonical = "/" + re.sub(r"(/index)?\.md$", "", route)
        sub = entry_id.split("__", 1)[0]
        for lang in ("zh", "en"):
            for cand in (os.path.join(REPO_ROOT, "WikiContent", lang, sub, entry_id + ".md"),
                         os.path.join(REPO_ROOT, "WikiContent", lang, entry_id + ".md")):
                if os.path.isfile(cand):
                    out.setdefault(canonical, {})[lang] = cand
                    break
    for name in os.listdir(HUB_DIR):
        m = re.match(r"(\w+)\.(zh|en)\.md$", name)
        if m:
            out.setdefault("/" + m.group(1), {})[m.group(2)] = os.path.join(HUB_DIR, name)
    return out


def headings(path):
    return [re.sub(r"\*\*|`", "", m.group(1)).strip()
            for m in re.finditer(r"^#{2,6}\s+(.+?)\s*$", read(path), re.M)]


def check_ref(ref):
    path = ref["file"]
    if not os.path.isfile(os.path.join(REPO_ROOT, path)):
        return "%s 不存在" % path
    src = gen.read_source(path)
    if "symbol" in ref:
        try:
            v = gen.const_value(path, ref["symbol"])
        except SystemExit:
            return "%s 里找不到常量 %s" % (path, ref["symbol"])
        if float(v) != float(ref["value"]):
            return "%s.%s 现在是 %s，产物记的是 %s" % (path, ref["symbol"], v, ref["value"])
    elif "literal" in ref:
        if '"%s"' % ref["literal"] not in src:
            return '%s 里不再有字面量 "%s"' % (path, ref["literal"])
    elif "contains" in ref:
        if ref["contains"] not in src:
            return "%s 的行为锚点变了：%s" % (path, ref["contains"])
    elif "baseline" in ref:
        m = re.search(r'PresetId = "%s",\s*Health = (\d+(?:\.\d+)?)f' % re.escape(ref["baseline"]), src)
        if not m or float(m.group(1)) != float(ref["value"]):
            return "%s 里 %s 的生命基准变了" % (path, ref["baseline"])
    else:
        return "无法识别的 ref：%s" % ref
    return None


def main():
    if not os.path.isfile(DATA):
        return fail("缺 enemy-stats.json：先跑 tools/build_wiki_enemy_stats.py")
    data = json.loads(read(DATA))
    enemies = data.get("enemies", [])
    if len(enemies) < 20:
        return fail("enemy-stats.json 只有 %d 个敌人，产物被清空了？" % len(enemies))
    group_ids = {g["id"] for g in data.get("groups", [])}

    # 1. 每条数据的出处仍与代码一致
    problems = []
    checked = 0
    for e in enemies:
        for key in ("id", "group", "zh", "en", "page", "health", "elements", "status", "summary"):
            if key not in e:
                problems.append("%s 缺字段 %s" % (e.get("id"), key))
        if set(e.get("elements", {})) != set(ELEMENTS):
            problems.append("%s 的元素倍率不是七项" % e.get("id"))
        if e.get("group") not in group_ids:
            problems.append("%s 的分组 %s 不在 groups 里" % (e.get("id"), e.get("group")))
        for ref in e.get("refs", []):
            err = check_ref(ref)
            checked += 1
            if err:
                problems.append("%s：%s" % (e["id"], err))
    if problems:
        return fail("产物与代码不一致（重跑 tools/build_wiki_enemy_stats.py --write-content）：\n  "
                    + "\n  ".join(problems[:20]))

    # 2. Boss 页「基础数据」的生成行与产物一致
    by_id = {e["id"]: e for e in enemies}
    for eid, (rel, h_zh, h_en) in gen.CONTENT_PAGES.items():
        for lang, heading in (("zh", h_zh), ("en", h_en)):
            path = os.path.join(REPO_ROOT, "WikiContent", lang, rel)
            lines = read(path).replace("\r\n", "\n").split("\n")
            try:
                start = next(i for i, l in enumerate(lines) if l.strip() == heading)
            except StopIteration:
                return fail("%s 没有小节 %s" % (rel, heading))
            end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("#")), len(lines))
            section = lines[start + 1:end]
            for label, value in gen.content_lines(by_id[eid], lang):
                if value is None:
                    continue
                want = label + value
                if want not in section:
                    have = next((l for l in section if l.startswith(label)), "（没有这一行）")
                    return fail("%s/%s「%s」小节应为\n    %s\n  实际是\n    %s"
                                % (lang, rel, heading, want, have))

    # 3. 抗性表的摆放
    sources = page_sources()
    placements = PLACEMENT_RE.findall(read(TABLES))
    if not placements:
        return fail("enemy-tables.mts 里一处摆放都没解析到")
    for page, zh, _q, en, group in placements:
        if group not in group_ids:
            return fail("enemy-tables.mts 引用了不存在的分组 %s" % group)
        src = sources.get(page.rstrip("/") or "/")
        if not src or "zh" not in src or "en" not in src:
            return fail("enemy-tables.mts 的页面 %s 找不到中英源文件" % page)
        for lang, title in (("zh", zh), ("en", en.replace("\\'", "'"))):
            if title not in headings(src[lang]):
                return fail("%s 的%s版没有小节「%s」，抗性表 %s 会悄悄消失"
                            % (page, "中文" if lang == "zh" else "英文", title, group))
    used = {p[4] for p in placements}
    orphan = sorted(group_ids - used)
    if orphan:
        return fail("这些分组没有任何页面摆放：" + ", ".join(orphan))

    # 4. 接线
    config_src = read(CONFIG)
    if "md.use(enemyStatsSlotPlugin)" not in config_src or "function enemyStatsSlotPlugin" not in config_src:
        return fail("config.mts 没有登记 enemyStatsSlotPlugin：抗性表不会被插进正文")
    if "app.component('WikiEnemyTable'" not in read(THEME_INDEX):
        return fail("theme/index.ts 没有全局注册 WikiEnemyTable：插进正文的标签会渲染成未知元素")

    print("WikiEnemyStatsGuard: OK - %d 个敌人、%d 条代码出处、%d 处抗性表摆放都对得上"
          % (len(enemies), checked, len(placements)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
