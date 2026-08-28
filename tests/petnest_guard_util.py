#!/usr/bin/env python3
"""
petnest_guard_util — 遗种巢（PetNest）guard 共用工具。

与 modeh_guard_util 同形，但独立成文件：PetNest 的路径根是 `PetNest/`，
而且部分断言要跨 `Patches/`、`Config/`、`Localization/` 等目录读文件，
共享入口集中在这里，避免每个 guard 重复拼路径。

提供：
- read_text：读文件，缺失返回 None；
- strip_cs_comments：去掉 C# 的 // 与 /* */ 注释（保留字符串字面量），
  这样“禁止引用某符号”的断言不会被文档注释误伤；
- contains_symbol：在去注释后的源码里查找符号；
- petnest_path / read_petnest：按 `PetNest/` 相对名读文件；
- read_petnest_group：把一组 PetNest 源文件拼成一个逻辑单元再断言
  （仓库有单文件行数预算，契约常被拆到多个文件）；
- report：统一的 PASS/FAIL 输出格式与退出码。
"""
import io
import os

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PETNEST_DIR = os.path.join(REPO_ROOT, "PetNest")


def read_text(path):
    """读文件；不存在返回 None。"""
    if not os.path.exists(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def repo_path(*parts):
    """拼仓库根下的路径。"""
    return os.path.join(REPO_ROOT, *parts)


def petnest_path(name):
    """拼 PetNest/ 下的文件路径。"""
    return os.path.join(PETNEST_DIR, name)


def read_petnest(name):
    """读 PetNest/ 下的文件；不存在返回 None。"""
    return read_text(petnest_path(name))


def read_petnest_group(*names):
    """把一组 PetNest 源文件按顺序拼接成一个逻辑单元再读；任一缺失返回 None。"""
    parts = []
    for name in names:
        text = read_petnest(name)
        if text is None:
            return None
        parts.append(text)
    return "\n".join(parts)


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


def report(guard_name, errors):
    """统一输出格式；返回进程退出码。"""
    if errors:
        print("{}: FAIL ({} errors)".format(guard_name, len(errors)))
        for e in errors:
            print("  - " + e)
        return 1
    print("{}: PASS".format(guard_name))
    return 0
