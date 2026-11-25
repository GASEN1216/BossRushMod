"""BossPoolHotkeyHintGuard — 玩家看到的 Ctrl+F10 提示必须说清它打开的是「Boss池设置」。

2026-11-25 文案优化方案 P0-1 / P2-P3-3 第 3 项：三处 Boss 池为空的提示原来写「(Ctrl+F10 打开设置)」，
玩家会去翻 ModConfig 设置面板；实际 Ctrl+F10 打开的是 `BossFilter` 的「Boss池设置」窗口
（`BossFilter/BossFilterUi.cs` 标题 `L10n.T("Boss池设置", "Boss Pool Settings")`）。

规则：生产 C# 里（注释先剥掉）凡是带 `Ctrl+F10` 的字符串字面量，中文要写「Boss池设置」、
英文要写 "Boss Pool Settings"，并且不许再出现「打开设置」/ "open settings" 这种泛指写法。
窗口标题本身改名时同步本守卫的 TITLE_CN / TITLE_EN。
"""
from pathlib import Path
import os
import re
import sys

from cs_source_util import strip_comments

ROOT = Path(__file__).resolve().parents[1]
TITLE_FILE = ROOT / "BossFilter" / "BossFilterUi.cs"
TITLE_CN = "Boss池设置"
TITLE_EN = "Boss Pool Settings"
SKIP_DIRS = {"tests", "Build", "tmp", "archive", "鸭科夫源码", "wiki-site", "node_modules", ".git", ".claude", ".qoder"}
STRING_LITERAL = re.compile(r'"((?:[^"\\\n]|\\.)*)"')
STALE = [re.compile("打开设置"), re.compile(r"open settings", re.IGNORECASE)]


def production_sources():
    # 顶层先剪掉 Build / tmp 等目录再往下走：rglob 会把 Build 里成百上千份源码副本也遍历一遍
    for dirpath, dirnames, filenames in os.walk(ROOT):
        if Path(dirpath) == ROOT:
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            if name.endswith(".cs"):
                yield Path(dirpath) / name


def main():
    errors = []
    title_src = TITLE_FILE.read_text(encoding="utf-8-sig") if TITLE_FILE.is_file() else ""
    if 'L10n.T("%s", "%s")' % (TITLE_CN, TITLE_EN) not in strip_comments(title_src):
        errors.append("BossFilterUi.cs 里找不到窗口标题 L10n.T(\"%s\", \"%s\")：标题改了就同步本守卫" % (TITLE_CN, TITLE_EN))

    hits = 0
    for path in production_sources():
        source = strip_comments(path.read_text(encoding="utf-8-sig", errors="replace"))
        if "Ctrl+F10" not in source:
            continue
        rel = path.relative_to(ROOT).as_posix()
        for line_no, line in enumerate(source.splitlines(), 1):
            for m in STRING_LITERAL.finditer(line):
                text = m.group(1)
                if "Ctrl+F10" not in text:
                    continue
                hits += 1
                where = "%s:%d" % (rel, line_no)
                if TITLE_CN not in text and TITLE_EN not in text:
                    errors.append("%s 的 Ctrl+F10 提示没写明「%s」/ %s：%s" % (where, TITLE_CN, TITLE_EN, text[:80]))
                for pattern in STALE:
                    if pattern.search(text):
                        errors.append("%s 仍用泛指的「%s」，Ctrl+F10 打开的是 Boss 池设置：%s" % (where, pattern.pattern, text[:80]))

    if hits == 0:
        errors.append("生产代码里一条带 Ctrl+F10 的字符串都没扫到：提示被删或扫描范围变了，就同步本守卫")

    if errors:
        print("BossPoolHotkeyHintGuard: FAIL")
        for e in errors:
            print("  - " + e)
        return 1
    print("BossPoolHotkeyHintGuard: PASS（%d 条 Ctrl+F10 提示都指向「%s」）" % (hits, TITLE_CN))
    return 0


if __name__ == "__main__":
    sys.exit(main())
