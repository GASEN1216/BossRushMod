"""创意工坊简介长度预算检查。

用法：python tools/workshop_description_budget.py ../a.md ../b.md

Steamworks 的说明上限 k_cchPublishedDocumentDescriptionMax = 8000，单位是 UTF-8 字节（中文一个字 3 字节），
不是字数。网页表单提交时换行按 CRLF 计（每行多 1 字节），引号、& 和尖括号可能被转成 HTML 实体。
2026-10-15 的 a.md 按 LF 算正好 7999 字节，按 CRLF 算是 8097，b.md 是 8715，两份都贴不进工坊描述。

预算按最坏口径计，再留 BUDGET_MARGIN 的余量；超了就退出码 1。
"""

import sys
from pathlib import Path

STEAM_LIMIT_BYTES = 8000
BUDGET_MARGIN = 300
ESCAPE_EXTRA = {'"': 5, "'": 4, "&": 4, "<": 3, ">": 3}  # &quot; &#39; &amp; &lt; &gt;


def effective_bytes(text):
    text = text.replace("\r\n", "\n").lstrip("﻿")
    size = len(text.encode("utf-8"))
    size += text.count("\n")  # 提交时 LF -> CRLF
    size += sum(text.count(ch) * extra for ch, extra in ESCAPE_EXTRA.items())
    return size


def main(paths):
    if not paths:
        print(__doc__)
        return 2
    budget = STEAM_LIMIT_BYTES - BUDGET_MARGIN
    failed = False
    for raw in paths:
        path = Path(raw)
        text = path.read_text(encoding="utf-8-sig")
        size = effective_bytes(text)
        ok = size <= budget
        failed |= not ok
        print("{} {}: {} / {} 字节（Steam 上限 {}，留 {} 余量）".format(
            "OK  " if ok else "OVER", path.name, size, budget, STEAM_LIMIT_BYTES, BUDGET_MARGIN))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
