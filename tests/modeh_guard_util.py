#!/usr/bin/env python3
"""
modeh_guard_util — Mode H guard 共用工具。

提供：
- read_text：读文件，缺失返回 None；
- strip_cs_comments：去掉 C# 的 // 与 /* */ 注释（保留字符串字面量），
  这样“禁止引用某符号”的断言不会被文档注释误伤；
- contains_symbol：在去注释后的源码里查找符号。
"""
import io
import os

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read_text(path):
    """读文件；不存在返回 None。"""
    if not os.path.exists(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def strip_cs_comments(source):
    """去掉 C# 注释，保留字符串与字符字面量内容。"""
    if not source:
        return ""
    out = []
    i = 0
    n = len(source)
    in_line_comment = False
    in_block_comment = False
    in_string = False
    in_char = False
    in_verbatim = False
    while i < n:
        ch = source[i]
        nxt = source[i + 1] if i + 1 < n else ""

        if in_line_comment:
            if ch == "\n":
                in_line_comment = False
                out.append(ch)
            i += 1
            continue
        if in_block_comment:
            if ch == "*" and nxt == "/":
                in_block_comment = False
                i += 2
                continue
            if ch == "\n":
                out.append(ch)
            i += 1
            continue
        if in_verbatim:
            out.append(ch)
            if ch == '"':
                if nxt == '"':
                    out.append(nxt)
                    i += 2
                    continue
                in_verbatim = False
                in_string = False
            i += 1
            continue
        if in_string:
            out.append(ch)
            if ch == "\\" and nxt:
                out.append(nxt)
                i += 2
                continue
            if ch == '"':
                in_string = False
            i += 1
            continue
        if in_char:
            out.append(ch)
            if ch == "\\" and nxt:
                out.append(nxt)
                i += 2
                continue
            if ch == "'":
                in_char = False
            i += 1
            continue

        if ch == "/" and nxt == "/":
            in_line_comment = True
            i += 2
            continue
        if ch == "/" and nxt == "*":
            in_block_comment = True
            i += 2
            continue
        if ch == "@" and nxt == '"':
            in_verbatim = True
            in_string = True
            out.append(ch)
            out.append(nxt)
            i += 2
            continue
        if ch == '"':
            in_string = True
            out.append(ch)
            i += 1
            continue
        if ch == "'":
            in_char = True
            out.append(ch)
            i += 1
            continue

        out.append(ch)
        i += 1
    return "".join(out)


def contains_symbol(source, symbol):
    """在去注释后的源码里查找符号。"""
    return symbol in strip_cs_comments(source or "")


def read_modeh_group(*names):
    """把一组 ModeH 源文件按顺序拼接成一个逻辑单元再读。

    Mode H 的数据契约、规范摘要与内容目录都因仓库单文件 1200 行预算被拆成
    多个文件（例如 ModeHStateModel.cs + ModeHStateDtos.cs）。守卫应当按契约
    而不是按物理文件断言，因此这里提供统一的拼接读取入口；任一文件缺失返回 None。
    """
    parts = []
    for name in names:
        text = read_text(os.path.join(REPO_ROOT, "ModeH", name))
        if text is None:
            return None
        parts.append(text)
    return "\n".join(parts)
