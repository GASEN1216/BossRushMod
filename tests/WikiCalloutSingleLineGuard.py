# -*- coding: utf-8 -*-
"""WikiCalloutSingleLineGuard - WikiContent 的 [tip]/[warn] 必须写成单行。

为什么是不变式：
    wiki-site/scripts/sync-content.mjs 的 callout 转换是

        content.replace(/^\\[tip\\]\\s*(.+)$/gm, '::: tip\\n$1\\n:::')

    `.` 不匹配换行，`(.+)$` 只吃到**第一行**。源文件里把一条 callout 折成两行时，
    第二行会被留在闭合 ':::' **之后**，在线 Wiki 上渲染成一个提示框 + 一段游离的
    正文——而且往往是从逗号处断开，读起来像排版事故。

    修在源头而不是改正则：那个 transform 被 tests/ZombieModeMutantWikiGuard.py 用
    Python 逐字节镜像，而 JS 与 Python 在 `$` + MULTILINE 下语义不同，动正则容易让
    两边静默漂移。单行 callout 本来也是本仓库的多数写法。

    历史：2026-09-03 一次性折平 42 处（CR-2026-09-03-017）。

判据：
    以 `[tip]` 或 `[warn]` 开头的行，其下一行必须为空行、文件结尾，或另一个
    markdown 块级起始（列表 / 引用 / 表格 / 标题 / 围栏代码 / 另一条 callout）。
    注意 `**bold**` 不是列表项——`*` 必须后跟空白才算 bullet。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WIKI_ROOT = os.path.join(REPO_ROOT, "WikiContent")

CALLOUT_RE = re.compile(r"^\[(tip|warn)\]")
BLOCK_START_RE = re.compile(
    r"^\s*(?:[-*+][ \t]|>[ \t]?|\||#{1,6}[ \t]|\d+\.[ \t]|```|\[(?:tip|warn)\])")


def fail(message):
    print("WikiCalloutSingleLineGuard: FAIL - " + message)
    return 1


def main():
    if not os.path.isdir(WIKI_ROOT):
        return fail("WikiContent 目录不存在: " + WIKI_ROOT)

    offenders = []
    scanned = 0
    callouts = 0

    for current_dir, _dirs, files in os.walk(WIKI_ROOT):
        for name in sorted(files):
            if not name.endswith(".md"):
                continue
            path = os.path.join(current_dir, name)
            rel = os.path.relpath(path, REPO_ROOT).replace(os.sep, "/")
            scanned += 1

            with open(path, "r", encoding="utf-8") as handle:
                lines = handle.read().split("\n")

            for index, line in enumerate(lines):
                if not CALLOUT_RE.match(line):
                    continue
                callouts += 1
                nxt_index = index + 1
                if nxt_index >= len(lines):
                    continue
                nxt = lines[nxt_index]
                if not nxt.strip():
                    continue
                if BLOCK_START_RE.match(nxt):
                    continue
                offenders.append((rel, index + 1, line.strip()[:48], nxt.strip()[:48]))

    if scanned == 0:
        return fail("WikiContent 下没有扫描到任何 .md，路径或后缀判据可能已失效")
    if callouts == 0:
        return fail("没有扫描到任何 [tip]/[warn]，callout 语法可能已改，本 guard 需同步")

    if offenders:
        print("WikiCalloutSingleLineGuard: FAIL - "
              + str(len(offenders)) + " 处 callout 被折成多行，"
              "同步到 wiki-site 后续行会掉出 ::: 容器：")
        for rel, line_no, head, cont in offenders:
            print("  " + rel + ":" + str(line_no))
            print("      callout: " + head)
            print("      续行  : " + cont)
        print("  修法：把续行并回 callout 所在的那一行（中文不加空格，西文加一个空格）。")
        return 1

    print("WikiCalloutSingleLineGuard: PASS - "
          + str(scanned) + " 篇 / " + str(callouts) + " 条 callout 全部单行")
    return 0


if __name__ == "__main__":
    sys.exit(main())
