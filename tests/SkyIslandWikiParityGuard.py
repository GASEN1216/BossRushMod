"""天空岛玩家 Wiki 的中英两版必须**内容对齐**，不只是"两边都有这个文件"。

背景（CR-2026-09-12-011）：`WikiContent/en/map__sky_island.md` 的「The first stretch」自 2026-09-09
首版起就**整段缺了中文版的四条路线要点**——「先清威胁再操作风标台 / 星灯台」「捷径怎么开」
「双航标后去归航钟庭」。中文玩家从 Wiki 补得上"航标台要先清守卫"这一句（目标卡不说、要到装置
面板才知道），英文玩家补不上。仓库里此前**没有任何守卫比对中英 Wiki 的正文**，
`SkyIslandPlayerEntryGuard` 只看两边各自含哪些关键词，`WikiSiteStructureGuard` 只看导航结构，
于是这个缺口连过三轮审核没人发现。

**本守卫只钉 `map__sky_island.md`**，不做全仓 zh/en 对齐：
`item__consumables.md` 这类页面英文版**刻意**把「堆叠」「使用时间」合成一行（`Stack 10 / Use time 3s`），
内容是全的、形状不同；按条数一刀切会把排版约定当成缺陷，属于把守卫写成假不变式。
天空岛地图页的两版是 1:1 写的（17 个章节逐条对应），这里的条数相等**是真不变式**。
全仓其余 33 对页面的 zh/en 形状差异登记在 `CODE_REVIEW_FINDINGS.md` 的 UNVERIFIED 区，不在本守卫范围。

断言：
1. 中英两版章节数相同，且逐章节的 `- ` 条目数、表格数据行数、`[tip]` 数完全相等。
2. 「第一段旅程 / The first stretch」那四条路线要点**按语义锚点**各自存在——
   条数相等挡不住"换掉正文但条数没变"。
3. `wiki-site/` 的两份镜像与 `WikiContent/` 逐章节条数一致：镜像由 `scripts/sync-content.mjs`
   从 `WikiContent/` 重生成，手改镜像而不改权威源会在这里转红。

反向验证：删掉英文版任意一条路线要点、或把某条正文换成别的内容，本守卫必红。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

ZH = ROOT / "WikiContent" / "zh" / "map__sky_island.md"
EN = ROOT / "WikiContent" / "en" / "map__sky_island.md"
ZH_MIRROR = ROOT / "wiki-site" / "docs" / "maps" / "sky-island.md"
EN_MIRROR = ROOT / "wiki-site" / "docs" / "en" / "maps" / "sky-island.md"

HEADING = re.compile(r"^#{1,6} ")
# 表格分隔行（|---|---|）不是数据行。
TABLE_RULE = re.compile(r"^\|[\s:|-]*\|?\s*$")
CALLOUT = ("[tip]", "[note]", "[warning]", "[danger]")
# 在线站镜像由 sync-content.mjs 把 `[tip] ...` 转成 VitePress 的 `::: tip` 容器，
# 所以镜像侧要认这一种写法，否则同一条提示在两边被数成 1 和 0。
CALLOUT_MIRROR = (":::tip", "::: tip", ":::note", "::: note",
                  ":::warning", "::: warning", ":::danger", "::: danger")

failures = []


def sections(path):
    """按标题切段，每段统计条目 / 表格数据行 / 提示框数量。"""
    current = None
    result = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if HEADING.match(line):
            current = {"title": line.strip(), "bullets": 0, "rows": 0, "callouts": 0}
            result.append(current)
            continue
        if current is None:
            continue
        stripped = line.strip()
        if stripped.startswith("- "):
            current["bullets"] += 1
        elif stripped.startswith("|") and not TABLE_RULE.match(stripped):
            current["rows"] += 1
        elif stripped.startswith(CALLOUT) or stripped.startswith(CALLOUT_MIRROR):
            current["callouts"] += 1
    return result


def shape(section):
    return section["bullets"], section["rows"], section["callouts"]


def compare(left_path, left, right_path, right, label):
    if len(left) != len(right):
        failures.append(
            "%s：章节数不一致 —— %s 有 %d 个、%s 有 %d 个"
            % (label, left_path.name, len(left), right_path.name, len(right)))
        return
    for index, (a, b) in enumerate(zip(left, right)):
        if shape(a) == shape(b):
            continue
        failures.append(
            "%s：第 %d 个章节形状不一致 —— %s %s 是 (条目 %d, 表行 %d, 提示 %d)，"
            "%s %s 是 (条目 %d, 表行 %d, 提示 %d)"
            % (label, index + 1, left_path.name, a["title"], a["bullets"], a["rows"], a["callouts"],
               right_path.name, b["title"], b["bullets"], b["rows"], b["callouts"]))


for path in (ZH, EN, ZH_MIRROR, EN_MIRROR):
    if not path.exists():
        failures.append("缺少 Wiki 文件：%s" % path.relative_to(ROOT))

if not failures:
    zh_sections = sections(ZH)
    en_sections = sections(EN)

    # 1. 权威源两版逐章节对齐。
    compare(ZH, zh_sections, EN, en_sections, "中英权威源")

    # 2. 四条路线要点按语义锚点各自存在（条数相等挡不住换掉正文）。
    zh_text = ZH.read_text(encoding="utf-8")
    en_text = EN.read_text(encoding="utf-8")
    # (中文锚点, 英文锚点, 这一条在说什么)
    ROUTE_POINTS = [
        (("悬根林", "清除", "风标台"),
         ("Hanging Root Wood", "clear the threats", "beacon console"),
         "悬根林：先清威胁再操作风标台"),
        (("残星工坊", "清除", "星灯台"),
         ("Fallen Star Workshop", "clear the threats", "lamp console"),
         "残星工坊：先清威胁再操作星灯台"),
        (("回程捷径", "绳桥"),
         ("Return shortcuts", "rope bridge"),
         "回程捷径：修复条件满足后开绳桥 / 栅门"),
        (("鸣风栈道", "归航钟庭"),
         ("Windsong Boardwalk", "Bell Court"),
         "鸣风栈道：双航标后前往归航钟庭"),
        (("中继平台", "搜刮点"),
         ("Relay platforms", "scavenging point"),
         "中继平台：伏击与三处搜刮点"),
    ]
    for zh_anchors, en_anchors, description in ROUTE_POINTS:
        missing_zh = [a for a in zh_anchors if a not in zh_text]
        missing_en = [a for a in en_anchors if a not in en_text]
        if missing_zh:
            failures.append("中文版「第一段旅程」缺少要点「%s」（找不到：%s）"
                            % (description, "、".join(missing_zh)))
        if missing_en:
            failures.append("英文版「第一段旅程」缺少要点「%s」（找不到：%s）"
                            % (description, ", ".join(missing_en)))

    # 3. 在线站镜像与权威源逐章节一致（镜像是 sync-content.mjs 生成物，手改会在这里转红）。
    compare(ZH, zh_sections, ZH_MIRROR, sections(ZH_MIRROR), "中文权威源 -> 在线站镜像")
    compare(EN, en_sections, EN_MIRROR, sections(EN_MIRROR), "英文权威源 -> 在线站镜像")

if failures:
    print("SkyIslandWikiParityGuard: FAIL")
    for failure in failures:
        print("  " + failure)
    sys.exit(1)

print("SkyIslandWikiParityGuard: PASS (天空岛地图页中英 %d 个章节逐条对齐，四条路线要点齐备)"
      % len(sections(ZH)))
