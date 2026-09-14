"""UniTask 的 awaiter 取完结果以后不得再读它的状态（2026-09-14 实机）。

UniTask 的异步状态机取完结果（GetResult）就回对象池、令牌作废，之后再读 IsCompleted / GetResult 会抛
InvalidOperationException："Token version is not matched, can not await twice or get Status after await"。
2026-09-14 岛内 Dev 演练 SKY_DRILL_OFFICIAL_DIALOGUE 就是这样崩的：取消那一步取完结果，写报告时又读了一次完成状态，
整条演练记成 UNHANDLED，前面几步的断言一条都没留下。

规则：凡是把 `.GetAwaiter()` 存进变量的地方，在变量的作用域里第一次 `变量.GetResult()` 之后，
不得再出现 `变量.IsCompleted` 或 `变量.GetResult()`（中间重新赋值的除外）。完成状态要在取结果之前读进局部变量。
纯文本扫描、不理解控制流：循环里「这一轮取结果、下一轮再读状态」看不出来，靠审查。
"""
import os
import re
import sys
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
DRILL = "DebugAndTools/F3GameplayValidationSkyIslandDrill.cs"
TOP_SKIP = {"鸭科夫源码", "Build", "tests", ".git", ".qoder", ".codex_tmp", "wiki-site", "docs", "node_modules",
            "output", "ArtSource", "Assets", "skills", "codex-skills"}
DECLARATION = re.compile(r"(?:\bvar|\b[\w.]+(?:<[^;{}()]*>)?\.Awaiter|\bAwaiter)\s+(\w+)\s*=\s*[^;{}]*?\.GetAwaiter\(\)\s*;")
STRING = re.compile(r'"(?:\\.|[^"\\\n])*"')


def blank_strings(code):
    """字符串字面量换成等长空白：里面的分号和花括号不参与作用域判断。"""
    return STRING.sub(lambda m: '"' + " " * (len(m.group(0)) - 2) + '"', code)


def scope_end(code, start):
    depth = 0
    for index in range(start, len(code)):
        char = code[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth < 0:
                return index
    return len(code)


def violations(source):
    code = blank_strings(clean_source(source))
    found, declared = [], 0
    for match in DECLARATION.finditer(code):
        declared += 1
        name = re.escape(match.group(1))
        scope = code[match.end():scope_end(code, match.end())]
        first = re.search(r"\b" + name + r"\.GetResult\(\)", scope)
        if not first:
            continue
        rest = scope[first.end():]
        reassigned = re.search(r"\b" + name + r"\s*=(?!=)", rest)
        window = rest[:reassigned.start()] if reassigned else rest
        later = re.search(r"\b" + name + r"\.(IsCompleted|GetResult\(\))", window)
        if later:
            offset = match.end() + first.end() + later.start()
            found.append((match.group(1), later.group(1), code.count("\n", 0, offset) + 1))
    return found, declared


def scan():
    sources = []
    for directory, subdirs, files in os.walk(ROOT):
        if Path(directory) == ROOT:
            subdirs[:] = [d for d in subdirs if d not in TOP_SKIP]
        subdirs[:] = [d for d in subdirs if d not in ("bin", "obj")]
        for name in files:
            if name.endswith(".cs"):
                path = Path(directory) / name
                sources.append((path.relative_to(ROOT).as_posix(), path.read_text(encoding="utf-8-sig", errors="ignore")))
    return sources


def main():
    errors = []
    sources = scan()
    declared_total, drill_declared, drill_text = 0, 0, None
    for rel, text in sources:
        found, declared = violations(text)
        declared_total += declared
        if rel == DRILL:
            drill_declared, drill_text = declared, text
        for name, member, line in found:
            errors.append("%s:约第 %d 行（去注释后）：%s 取完结果之后又读了 %s——完成状态要在 GetResult 之前读进局部变量"
                          % (rel, line, name, member))
    if drill_declared < 2:
        errors.append(DRILL + " 里存起来的 awaiter 少于 2 个：守卫锚点失效（演练换了写法就同步这条守卫）")

    probes = (
        ("取完结果又读状态",
         "void M(){ var a = t.GetAwaiter(); if (a.IsCompleted) { a.GetResult(); } Log(a.IsCompleted); }", True),
        ("取完结果又取一次（参数里带分号的字符串）",
         "void M(){ UniTask<int>.Awaiter a = F(1,\n \"x;y}\").GetAwaiter(); try { a.GetResult(); } catch {} a.GetResult(); }", True),
        ("先读进局部变量再取结果",
         "void M(){ var a = t.GetAwaiter(); bool done = a.IsCompleted; if (done) { a.GetResult(); } Log(done); }", False),
        ("取完结果后重新赋值",
         "void M(){ var a = t.GetAwaiter(); a.GetResult(); a = u.GetAwaiter(); if (a.IsCompleted) {} }", False),
        ("注释里提到不算",
         "void M(){ var a = t.GetAwaiter(); a.GetResult(); // a.IsCompleted\n }", False),
    )
    for label, snippet, should_flag in probes:
        flagged = bool(violations(snippet)[0])
        if flagged != should_flag:
            errors.append("反向检查失效（%s）：期望%s，实际%s" % (label, "报出" if should_flag else "放过", "报出" if flagged else "放过"))
    anchor = '"cancel_completed=" + cancelCompleted'
    if drill_text is None or anchor not in drill_text:
        errors.append("反向检查锚点失效：演练里找不到 " + anchor)
    elif not violations(drill_text.replace(anchor, '"cancel_completed=" + cancelled.IsCompleted', 1))[0]:
        errors.append("反向检查失效：把演练改回「取完结果再读 cancelled.IsCompleted」之后守卫仍然全绿")

    if errors:
        print("UniTaskAwaiterReuseGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("UniTaskAwaiterReuseGuard: PASS（扫描 %d 个 .cs、%d 个存起来的 awaiter；%d 个反向检查）"
          % (len(sources), declared_total, len(probes) + 1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
