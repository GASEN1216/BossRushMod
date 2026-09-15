"""天空岛世界空间文字跟随语言切换的守卫。

2026-09-14 全自动验收首轮（runId 20260914_143303_766）：复拍切成英文后，75 块世界文字仍是中文。
桥口木牌、采集点与搜刮点的浮空字、纪念物与信鸽的标签，都只在建出来或门状态变化时写一次字。
玩家在岛上切语言时同样看得到（AGENTS §4.4：语言在取用时解析）。

这里钉住各 owner 在已有推进里比较语言、变了就重写的接线。只查结构；行为由实机英文扫描
SKY_LOCALIZATION_EN 判。
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

SKY = "DebugAndTools/SkyIsland/"
errors = []


def read(rel):
    path = ROOT / rel
    if not path.exists():
        errors.append("读不到 " + rel)
        return ""
    return clean_source(path.read_text(encoding="utf-8-sig"))


def body(source, signature, what):
    start = source.find(signature)
    if start < 0:
        errors.append("缺少 %s：%s" % (what, signature))
        return ""
    open_brace = source.find("{", start + len(signature))
    depth = 0
    for i in range(open_brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[open_brace:i + 1]
    errors.append("%s 的方法体没有闭合" % what)
    return ""


def ordered(text, tokens, what):
    position = 0
    for token in tokens:
        found = text.find(token, position)
        if found < 0:
            errors.append("%s：缺少或顺序不对 -> %s" % (what, token))
            return
        position = found + len(token)


def main():
    gates = read(SKY + "SkyIslandGates.cs")
    ordered(body(gates, "internal void Apply(SkyIslandStoryData story)", "剧情门刷新"),
            ["bool chinese = L10n.IsChinese;", "if (flags == relevant)",
             "if (chinese != labelsChinese) WriteLabels(story, chinese);", "return;",
             "WriteLabels(story, chinese);", "navigation.SetBlockedAreas("],
            "桥口木牌：门没变时切了语言只重写字、不重扫导航；门变了照常写字再重扫")
    ordered(body(gates, "private void WriteLabels(SkyIslandStoryData story, bool chinese)", "木牌写字"),
            ["label.text = Notice(gate.Id, open, story);", "labelsChinese = chinese;"],
            "木牌按当前语言写字，并记下写的是哪种语言")

    gathering = read(SKY + "SkyIslandGathering.cs")
    ordered(body(gathering, "internal void Tick(Vector3 origin, bool night)", "采集点推进"),
            ["if (best != null) Build(best, night);", "bool chinese = L10n.IsChinese;",
             "if (chinese != labelsChinese) Relabel(chinese);", "if (night == glowNight) return;"],
            "采集点：语言检查必须排在昼夜早退之前（否则白天切语言永远走不到）")
    ordered(body(gathering, "private void Relabel(bool chinese)", "采集点重写字"),
            ["labelsChinese = chinese;", "SkyIslandFieldcraftRules.GatherLabel(spot.Node.Kind)",
             "text.text = label;", "point.Relabel(label);"],
            "采集点：浮空字与官方交互名一起重写")
    if "internal void Relabel(string title) { label = title; }" not in gathering:
        errors.append("采集交互体缺少 Relabel：官方交互名不会跟着换语言")

    scav = read(SKY + "SkyIslandScavenging.cs")
    ordered(body(scav, "internal void Tick()", "搜刮点推进"),
            ["UpdateLabels(origin);", "bool chinese = L10n.IsChinese;",
             "if (chinese != labelsChinese) RelabelPoints(chinese);"],
            "搜刮点：推进里比较语言")
    ordered(body(scav, "private void RelabelPoints(bool chinese)", "搜刮点重写字"),
            ["labelsChinese = chinese;", "text.text = TierLabel(points[i].Anchor.Tier);"],
            "搜刮点：暂时隐藏的牌子也一起重写，下次显形就是对的")
    if "text.text = TierLabel(tier);" not in scav:
        errors.append("搜刮点建牌与重写必须共用 TierLabel，两处口径不能分叉")

    world = read(SKY + "SkyIslandWorldStory.cs")
    ordered(body(world, "internal void Tick()", "剧情推进"),
            ["TickFieldcraft();", "if (displayedFlags >= 0 && L10n.IsChinese != feedbackChinese)", "RebuildFeedback();",
             "bird.Relabel(PigeonTitle());", "if (displayedFlags == story.Current.flags) return;", "GrantKeepsakes();",
             "RebuildFeedback();"],
            "纪念物与信鸽：切了语言先按当前语言重建、换字（旗标没变，不重播回话、不补发纪念品），旗标变化照常重建")
    ordered(body(world, "private void RebuildFeedback()", "纪念物重建"),
            ["feedbackChinese = L10n.IsChinese;", "feedback.Clear();", 'Beacon("Search_D"'],
            "纪念物重建先记下语言，再整组重建")
    if "PigeonTitle(), delegate { ReadLetter(letter); });" not in world:
        errors.append("信鸽建出来与换语言必须共用 PigeonTitle")
    presentation = read(SKY + "SkyIslandStoryPresentation.cs")
    ordered(body(presentation, "internal void Relabel(string title)", "剧情交互体换字"),
            ["label = title;", "text.text = title;"], "剧情交互体：官方交互名与头顶的字一起换")

    if errors:
        print("SkyIslandWorldTextLanguageGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("SkyIslandWorldTextLanguageGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
