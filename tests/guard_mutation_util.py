"""守卫共用的小工具：切 C# 方法体（跳过字符串里的花括号）与内存变异探针。

内存变异探针只改读进内存的文本，不碰工作区文件，多个会话共用工作区时也安全。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))
from cs_source_util import clean_source  # noqa: E402


def read_raw(rel):
    with open(os.path.join(REPO_ROOT, rel), encoding="utf-8-sig") as fh:
        return fh.read()


def _skip_literal(text, i):
    """i 指向字符串 / 字符字面量开头时返回字面量之后的位置，否则返回 -1。"""
    ch = text[i]
    if ch == "@" and i + 1 < len(text) and text[i + 1] == '"':
        j = i + 2
        while j < len(text):
            if text[j] == '"':
                if j + 1 < len(text) and text[j + 1] == '"':
                    j += 2
                    continue
                return j + 1
            j += 1
        return len(text)
    if ch in "\"'":
        j = i + 1
        while j < len(text) and text[j] != ch:
            j += 2 if text[j] == "\\" else 1
        return j + 1
    return -1


def method_body(text, signature):
    """返回 signature 之后第一对花括号里的内容；找不到时返回空串。"""
    start = text.find(signature)
    if start < 0:
        return ""
    i = text.find("{", start + len(signature))
    if i < 0:
        return ""
    depth = 0
    body_start = i + 1
    while i < len(text):
        end = _skip_literal(text, i)
        if end >= 0:
            i = end
            continue
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[body_start:i]
        i += 1
    return ""


def squash(text):
    return re.sub(r"\s+", " ", text)


def run_guard(name, files, check, probes):
    """files: {key: 相对路径}；check(raw: dict) -> list[str]；probes: [(key, old, new, why, tag)]。

    tag 是期望命中的那条断言的标签（错误信息里的子串），用来确认红在预期的断言上，而不是碰巧别处转红。
    """
    raw = {key: read_raw(path) for key, path in files.items()}
    errors = check(raw)
    for error in errors:
        print("  - " + error)
    if errors:
        print(name + ": FAIL")
        return 1

    dead = []
    for key, old, new, why, tag in probes:
        source = raw[key]
        if source.count(old) != 1:
            dead.append("探针锚点不唯一或已失效（%d 处）: %s :: %s" % (source.count(old), files[key], old))
            continue
        mutated = dict(raw)
        mutated[key] = source.replace(old, new)
        hits = check(mutated)
        if not hits:
            dead.append("变异未被发现: " + why)
        elif not any(tag in hit for hit in hits):
            dead.append("变异红在了别处（期望 %s）: %s -> %s" % (tag, why, hits))
    for item in dead:
        print("  - " + item)
    if dead:
        print(name + ": FAIL（%d 个探针没转红）" % len(dead))
        return 1
    print(name + ": PASS（%d 个变异探针全部转红）" % len(probes))
    return 0


def cleaned(raw):
    """C# 源剥注释与禁用区块；键名以 _bat 结尾的批处理原样保留。"""
    return {key: text if key.endswith("_bat") else clean_source(text) for key, text in raw.items()}
