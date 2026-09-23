#!/usr/bin/env python3
r"""compile_official.bat 源码清单的唯一解析器。

仓库里有多个消费者要知道「正式构建到底编了哪些 .cs」：
`tests/OfficialCompileListFileExistenceGuard.py`（清单与磁盘双向一致）、
`tools/verify_syntax.py`（离线语法探针）等。各写一份正则的代价已经付过：

- 2026-09-23 实测：探针的 `^\s*@?echo\((...\.cs)\s*$` 吃不下清单里残留的
  `^` 续行写法（`echo(A.cs ^` + 下一行缩进的 `B.cs ^` …）。cmd 会把这几行拼成
  一条 `echo`，写进响应文件后 csc 按空白切参数，**正式构建照常编译**；
  可探针把 `echo(...SkyIslandJournal.cs ^` 整行丢掉，于是该文件从来没被离线语法
  检查过，而且不报错、不计数差异，静默失明。

所以清单解析只保留这一份实现，规则与 `OfficialCompileListFileExistenceGuard`
原有的正则逐字相同（该 guard 现在也从这里取）。新增消费者一律 import 本模块，
不要再写第二套正则；`tests/SyntaxProbeCompileListParityGuard.py` 会盯着这件事。
"""

import os
import re

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COMPILE_BAT = os.path.join(REPO_ROOT, "compile_official.bat")

# 一条源码项 = 以 .cs 结尾的路径 token，其后只允许「空白 + (续行脱字符 | 换行 | 文件末尾)」。
# 这个后瞻同时覆盖两种写法：
#   echo(Path\To\File.cs                               当前的响应文件写法
#   echo(Path\To\A.cs ^ / <缩进>Path\To\B.cs ^ / ...   历史 ^ 续行写法的残留
# 而 `>>"file"` 行尾重定向会让 .cs 后面跟上 `>`，这里就扫不到——compile_official.bat
# 顶部注释（"写法要点" 第 1 条）已写明不要那么写。
SOURCE_RE = re.compile(r"([A-Za-z0-9_./\\-]+\.cs)(?=\s*(?:\^|\r?\n|$))")


def normalize_source(path):
    r"""统一成正斜杠相对路径：`.\A\B.cs` / `A//B.cs` -> `A/B.cs`。"""
    return re.sub(r"/+", "/", path.replace("\\", "/")).lstrip("./")


def parse_compile_sources(text):
    """按出现顺序返回去重后的源码清单（正斜杠相对路径）。

    返回 list 而不是 set：探针要把清单按顺序喂给 csc，顺序稳定才好比对两次运行的
    输出。需要集合语义的调用方自己 `set(...)`。
    """
    ordered = []
    seen = set()
    for match in SOURCE_RE.finditer(text):
        source = normalize_source(match.group(1))
        if source not in seen:
            seen.add(source)
            ordered.append(source)
    return ordered


def read_compile_text(path=None):
    """读 compile_official.bat 正文。

    用 utf-8-sig：文件当前没有 BOM，但哪天加了也不该让所有消费者一起失明；
    仍是严格解码，编码真坏了就抛出来，不用 errors='ignore' 静默吞字节。
    """
    with open(path or COMPILE_BAT, encoding="utf-8-sig") as fh:
        return fh.read()


def read_compile_sources(path=None):
    """直接读盘并解析，等价于 `parse_compile_sources(read_compile_text(path))`。"""
    return parse_compile_sources(read_compile_text(path))
