#!/usr/bin/env python3
"""RectTransformDefaultSizeGuard — 新建的 RectTransform 设了拉伸锚点就必须定尺寸。

`new GameObject(...)` 之后 `AddComponent<RectTransform>()`（或加 Graphic 后 `GetComponent<RectTransform>()`）
拿到的 RectTransform，sizeDelta 默认是 100×100。锚点在某一轴拉开（anchorMin != anchorMax）却不设
sizeDelta / offsetMin / offsetMax，这一轴就比父级多出 100、两边各溢出 50。

2026-11-25 owner 实机：成就页滚动内容区横向拉伸却没设 sizeDelta，整列卡片比视口宽 100——左侧图标被裁掉一半，
右侧「已授」印章与「√ 已领取」被切掉；Boss 池列表同病，图鉴网格居中看不出但一样多出 100。三处都是 1f24eec0
把克隆官方 ScrollRect 预制件改成手搭视口时暴露的（预制件自带的 Content 尺寸是对的）。

判据：编译清单里的每个 .cs（clean_source 剥注释与禁用区块），找「本方法里刚 new 出来的 GameObject 上取到的
RectTransform 变量」，往下到方法结束或变量被重新赋值为止：
  - anchorMin / anchorMax 都写成字面量且至少一轴拉开；
  - 却没有 sizeDelta / offsetMin / offsetMax / SetSizeWithCurrentAnchors / SetInsetAndSizeFromParentEdge。
命中即红。没有白名单：父级 LayoutGroup 会接管尺寸的情况也照样显式写 sizeDelta = Vector2.zero，读代码的人不用去猜。

上限：只认字面量锚点（变量锚点跳过）；只看到同一方法内的写法；挡不住「尺寸写了但写错」。实际观感靠实机清单。
"""
import io
import os
import re
import sys
from pathlib import Path

from cs_source_util import clean_source

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from compile_list import read_compile_sources  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]

DECL = re.compile(
    r"(?:RectTransform\s+)?\b([A-Za-z_][\w\.]*)\s*=\s*[^;=]*?"
    r"(?:AddComponent<RectTransform>\(\)|GetComponent<RectTransform>\(\)|\.transform\s+as\s+RectTransform)\s*;")
METHOD_HEAD = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|internal|protected|static|override|virtual|sealed|async|unsafe|new)\s+)+"
    r"[\w<>\[\],\.\s]+\s+\w+\s*\(")
SIZE_MEMBERS = ("sizeDelta", "offsetMin", "offsetMax", "SetSizeWithCurrentAnchors", "SetInsetAndSizeFromParentEdge")
LOOKBACK = 8
LOOKAHEAD = 60


def parse_vec(expr):
    expr = expr.strip()
    named = {"Vector2.zero": (0.0, 0.0), "Vector2.one": (1.0, 1.0), "Vector2.up": (0.0, 1.0), "Vector2.right": (1.0, 0.0)}
    if expr in named:
        return named[expr]
    m = re.fullmatch(r"new\s+Vector2\(\s*(-?[\d\.]+)f?\s*,\s*(-?[\d\.]+)f?\s*\)", expr)
    if not m:
        return None
    try:
        return float(m.group(1)), float(m.group(2))
    except ValueError:
        return None


def find_violations(code):
    """返回 [(行号, 变量名, 拉伸轴)]：新建 RectTransform 拉伸锚点却没定尺寸。"""
    lines = code.split("\n")
    hits = []
    for i, line in enumerate(lines):
        m = DECL.search(line)
        if not m:
            continue
        var = m.group(1)
        window_back = "\n".join(lines[max(0, i - LOOKBACK):i + 1])
        if "new GameObject(" not in window_back:
            continue
        esc = re.escape(var)
        reassign = re.compile(r"(?<![\w\.])" + esc + r"\s*=(?!=)")
        amin = amax = None
        sized = False
        for j in range(i + 1, min(len(lines), i + LOOKAHEAD)):
            l = lines[j]
            if METHOD_HEAD.match(l):
                break
            if reassign.search(l):
                break
            mm = re.search(r"(?<![\w\.])" + esc + r"\.anchorMin\s*=\s*([^;]+);", l)
            if mm:
                amin = parse_vec(mm.group(1))
            mm = re.search(r"(?<![\w\.])" + esc + r"\.anchorMax\s*=\s*([^;]+);", l)
            if mm:
                amax = parse_vec(mm.group(1))
            if any(re.search(r"(?<![\w\.])" + esc + r"\." + k + r"\b", l) for k in SIZE_MEMBERS):
                sized = True
        if amin is None or amax is None or sized:
            continue
        axes = "".join(axis for axis, k in (("x", 0), ("y", 1)) if amin[k] != amax[k])
        if axes:
            hits.append((i + 1, var, axes))
    return hits


SELF_TEST_BAD = """
void Build() {
    GameObject contentObj = new GameObject("Content");
    contentObj.transform.SetParent(viewport.transform, false);
    contentContainer = contentObj.AddComponent<RectTransform>();
    contentContainer.anchorMin = new Vector2(0f, 1f);
    contentContainer.anchorMax = new Vector2(1f, 1f);
    contentContainer.pivot = new Vector2(0.5f, 1f);
}
"""
SELF_TEST_GOOD = SELF_TEST_BAD.replace(
    "contentContainer.pivot", "contentContainer.sizeDelta = Vector2.zero;\n    contentContainer.pivot")


def main():
    errors = []
    if find_violations(SELF_TEST_BAD) != [(5, "contentContainer", "x")]:
        errors.append("自检失败：判据认不出「横向拉伸却没设 sizeDelta」的样例 -> " + repr(find_violations(SELF_TEST_BAD)))
    if find_violations(SELF_TEST_GOOD):
        errors.append("自检失败：设了 sizeDelta 的样例被误报 -> " + repr(find_violations(SELF_TEST_GOOD)))

    sources = read_compile_sources()
    if len(sources) < 100:
        errors.append("编译清单解析异常：只读到 %d 个源文件" % len(sources))
    scanned = 0
    for rel in sources:
        path = ROOT / rel
        if not path.is_file():
            continue
        with io.open(str(path), "r", encoding="utf-8-sig", errors="ignore") as fh:
            code = clean_source(fh.read())
        scanned += 1
        for line_no, var, axes in find_violations(code):
            errors.append("%s:%d `%s` 锚点在 %s 轴拉伸却没设 sizeDelta / offset（默认 100×100，会比父级多出 100、两边各溢出 50）"
                          % (rel, line_no, var, axes))

    if errors:
        print("RectTransformDefaultSizeGuard: FAIL")
        for e in errors:
            print("  - " + e)
        return 1
    print("RectTransformDefaultSizeGuard: PASS（扫描 %d 个源文件）" % scanned)
    return 0


if __name__ == "__main__":
    sys.exit(main())
