"""词缀锻造：词缀名那一行的框高必须放得下一行中文。

2026-09-22 实测「词缀名不见了」：名字 26 号、关掉自动缩放、框高只有「字号 + 6」= 32（TMP 上下 margin 各 2，
内高 28），而游戏中文字体一行约 1.45 倍字号 ≈ 38。TMP 的 Ellipsis 在**第一行都放不下**时会把整串替换成
结束符——不是截断，是整行消失；说明文字那一行用的是量高度的 Overflow，所以只剩说明。

守卫的是结构不变式（不代替实机看字）：
  1. 名字行最小框高常量 >= ceil(字号 * 1.45) + 4；
  2. 建行时名字的矩形与 LayoutElement 都用这个常量，而不是「字号 + 6」；
  3. 刷新时按实测行高重设名字行高度（RefreshAffixPanel 里调用 MeasureAffixNameHeight 并写 NameLayout）。
"""
from pathlib import Path
import math
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
FORGE = ROOT / "Integration/Reforge/ReforgeUIManager_AffixForge.cs"
PANEL = ROOT / "Integration/Reforge/ReforgeUIManager_AffixForgePanel.cs"
CJK_LINE_RATIO = 1.45
TMP_VERTICAL_MARGIN = 4


def method_body(text, signature):
    start = text.index(signature)
    begin = text.index("{", start)
    depth, end = 1, begin + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[begin:end]


def main():
    errors = []
    forge = clean_source(FORGE.read_text(encoding="utf-8-sig"))
    panel = clean_source(PANEL.read_text(encoding="utf-8-sig"))

    font = re.search(r"const int AFFIX_NAME_FONT_SIZE = (\d+);", forge)
    minimum = re.search(r"const int AFFIX_NAME_MIN_HEIGHT = \(int\)\(AFFIX_NAME_FONT_SIZE \* ([\d.]+)f\) \+ (\d+);", forge)
    if not font or not minimum:
        errors.append("缺少 AFFIX_NAME_FONT_SIZE / AFFIX_NAME_MIN_HEIGHT 常量（名字行高度必须由字号推出来）")
    else:
        size = int(font.group(1))
        height = int(size * float(minimum.group(1))) + int(minimum.group(2))
        need = math.ceil(size * CJK_LINE_RATIO) + TMP_VERTICAL_MARGIN
        if height < need:
            errors.append("名字行最小框高 %d < 一行中文 %d（%d 号字）：TMP 会把整行名字清空" % (height, need, size))

    row = method_body(panel, "private static AffixRowWidgets BuildAffixRow(")
    name_block = row[row.index('"Name",'):row.index('"Desc",')]
    if "AFFIX_NAME_FONT_SIZE + 6" in name_block:
        errors.append("名字行又用回了「字号 + 6」的框高")
    if name_block.count("AFFIX_NAME_MIN_HEIGHT") < 2:
        errors.append("名字行的矩形与 LayoutElement 都必须用 AFFIX_NAME_MIN_HEIGHT")
    if "widgets.NameLayout = nameObj.GetComponent<LayoutElement>();" not in row:
        errors.append("建行时必须记下名字行的 LayoutElement，刷新时才能按实测改高度")

    refresh = method_body(forge, "private static void RefreshAffixPanel()")
    measure = re.search(r"float nameHeight = MeasureAffixNameHeight\(row\.NameText\);", refresh)
    apply = re.search(r"row\.NameLayout\.minHeight = row\.NameLayout\.preferredHeight = nameHeight;", refresh)
    if not measure or not apply or measure.start() > apply.start():
        errors.append("RefreshAffixPanel 必须先量名字行高再写回 NameLayout")
    else:
        if refresh.index("RefreshAffixRow(row, hasSlot, view);") > measure.start():
            errors.append("名字行高度要在填好文字（RefreshAffixRow）之后再量")
        if not re.search(r"row\.RowLayout\.minHeight = row\.RowLayout\.preferredHeight = Mathf\.Max\(AFFIX_ROW_HEIGHT,\s*"
                         r"descriptionHeight \+ nameHeight \+ \d+\);", refresh[apply.end():]):
            errors.append("整行高度必须把名字行的实测高度算进去")

    if errors:
        for error in errors:
            print("[FAIL] " + error)
        print("AffixForgeNameRowGuard: FAIL")
        return 1
    print("AffixForgeNameRowGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
