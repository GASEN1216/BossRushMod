#!/usr/bin/env python3
"""
IteratorYieldInCatchGuard — 禁止在 catch 子句体内 yield（CS1631）。

背景（真实事故，2026-09-01）：
  `F3GameplayValidationStages.RunIsolatedCase` 在两个 catch 体里写了
  `yield return ForceReclaimArena();`，实机编译直接报两条
  CS1631「无法在 catch 子句体中生成值」。

  更麻烦的是 **`verify_syntax.bat` 结构上抓不到这一类错误**：
  本仓库缺 `Duckov_Data\\Managed` 游戏程序集，几乎每个文件都有未解析类型
  （CS0246）。Roslyn 一旦解析不出类型/基类，就会在绑定阶段停下，
  **根本走不到迭代器方法体分析**，于是 CS16xx 全部报不出来。
  已实测确认：把 CS1631 注回源码，`verify_syntax.bat`（含 --with-bcl）仍报 PASS。

  所以这条规则只能用静态文本守卫兜住——本文件的存在理由就是这个。

规则：
- 任何 `.cs` 里，`catch (...) { ... }` 块体内不得出现 **`yield return`**；
- 正确写法：catch 里只置标志 / 记账，把 `yield return` 移到 catch 之外执行
  （参照 `RunIsolatedCase` 的 `needsReclaim` 模式）。

两个**合法**形态，本 guard 刻意不管：
- `catch { ...; yield break; }` —— `yield break` 不产生值，CS1631 不适用，仓库里多处在用；
- `try { yield return ...; } finally { ... }`（无 catch 子句）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXCLUDE_DIRS = {"Build", ".codex_tmp", ".git", ".kiro", "docs", "tests",
                "鸭科夫源码", "wiki-site", "output", "tmp", ".qoder"}

CATCH_HEAD = re.compile(r"\bcatch\b\s*(\([^)]*\))?\s*\{")


def strip_comments_and_strings(source):
    """去掉注释与字符串字面量，避免注释里提到 yield 造成误报。"""
    out = []
    i = 0
    n = len(source)
    while i < n:
        two = source[i:i + 2]
        if two == "//":
            j = source.find("\n", i)
            i = n if j < 0 else j
            continue
        if two == "/*":
            j = source.find("*/", i + 2)
            i = n if j < 0 else j + 2
            out.append(" ")
            continue
        ch = source[i]
        if ch == '"':
            # 逐字字符串 @"..."（含 "" 转义）
            if i > 0 and source[i - 1] == "@":
                j = i + 1
                while j < n:
                    if source[j] == '"':
                        if source[j:j + 2] == '""':
                            j += 2
                            continue
                        break
                    j += 1
                i = j + 1
                out.append(' " " ')
                continue
            j = i + 1
            while j < n:
                if source[j] == "\\":
                    j += 2
                    continue
                if source[j] == '"':
                    break
                j += 1
            i = j + 1
            out.append(' " " ')
            continue
        if ch == "'":
            j = i + 1
            while j < n:
                if source[j] == "\\":
                    j += 2
                    continue
                if source[j] == "'":
                    break
                j += 1
            i = j + 1
            out.append(" ' ' ")
            continue
        out.append(ch)
        i += 1
    return "".join(out)


def find_block_end(source, open_brace_index):
    depth = 0
    for j in range(open_brace_index, len(source)):
        if source[j] == "{":
            depth += 1
        elif source[j] == "}":
            depth -= 1
            if depth == 0:
                return j
    return -1


def scan(path):
    raw = open(path, encoding="utf-8", errors="ignore").read()
    if "catch" not in raw or "yield" not in raw:
        return []
    code = strip_comments_and_strings(raw)
    hits = []
    for match in CATCH_HEAD.finditer(code):
        brace = match.end() - 1
        end = find_block_end(code, brace)
        if end < 0:
            continue
        body = code[brace:end + 1]
        # 只查 `yield return`。`yield break` 不产生值，CS1631 不适用。
        if re.search(r"\byield\s+return\b", body):
            line = code[:match.start()].count("\n") + 1
            hits.append(line)
    return hits


def main():
    failures = []
    for root, dirs, files in os.walk(REPO_ROOT):
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            hits = scan(path)
            if hits:
                rel = os.path.relpath(path, REPO_ROOT).replace("\\", "/")
                failures.append((rel, hits))

    if failures:
        for rel, hits in failures:
            print("  - {0}: catch 子句体内出现 yield（CS1631），行 {1}".format(
                rel, ",".join(str(h) for h in hits)))
        print("  提示：catch 里只置标志/记账，把 yield 移到 catch 之外执行"
              "（参照 RunIsolatedCase 的 needsReclaim 模式）")
        print("IteratorYieldInCatchGuard: FAIL")
        return 1

    print("IteratorYieldInCatchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
