r"""C# 源码文本处理的共享工具（守卫用）。

守卫断言接线是否存在时，必须先剥掉注释——否则把接线整段注释掉守卫照样全绿，
等于没有断言。本项目已经因此漏检过两次。

**为什么不用正则**：正则版形如
    r'"(?:\\.|[^"\\])*"|/\*.*?\*/|//[^\n]*'
在普通输入上是对的，但它没有状态，遇到以下两种真实写法会与源码失步，
之后整份文件的分类全错（既可能假绿，也可能假红）：

  1. char 字面量 `'"'`：里面那个引号被当成字符串起始，一路吃到下一个引号，
     中间的注释行被当作字符串原样保留。
  2. 逐字字符串 `@"Assets\Sounds\"`：逐字字符串里反斜杠**不是**转义，
     但正则的 `\\.` 会把 `\"` 当成一个转义对吞掉，于是终止引号被吃掉。
     Windows 路径写成 `@"...\"` 很自然，这不是刻意攻击。

所以这里用一个小型状态机逐字扫描。字符串字面量整体保留（守卫经常要断言字符串常量），
注释替换成等量换行以保持行号。
"""

import re


def strip_comments(code):
    """去掉 C# 的行注释与块注释，保留字符串/字符字面量与行号结构。"""
    out = []
    i = 0
    n = len(code)

    while i < n:
        ch = code[i]

        # ---- 行注释 ----
        if ch == "/" and i + 1 < n and code[i + 1] == "/":
            j = code.find("\n", i)
            i = n if j == -1 else j
            continue

        # ---- 块注释 ----
        if ch == "/" and i + 1 < n and code[i + 1] == "*":
            j = code.find("*/", i + 2)
            segment = code[i:] if j == -1 else code[i:j + 2]
            out.append("\n" * segment.count("\n"))
            i = n if j == -1 else j + 2
            continue

        # ---- 逐字字符串 @"..."：反斜杠不转义，"" 表示一个引号 ----
        if ch == "@" and i + 1 < n and code[i + 1] == '"':
            start = i
            i += 2
            while i < n:
                if code[i] == '"':
                    if i + 1 < n and code[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            out.append(code[start:i])
            continue

        # ---- 普通字符串 "..."：反斜杠转义；未闭合时按到行尾处理，避免整份文件失步 ----
        if ch == '"':
            start = i
            i += 1
            while i < n:
                c = code[i]
                if c == "\\" and i + 1 < n:
                    i += 2
                    continue
                if c == '"':
                    i += 1
                    break
                if c == "\n":
                    break
                i += 1
            out.append(code[start:i])
            continue

        # ---- 字符字面量 '...'：'"' 与 '\\' 都要正确吃掉 ----
        if ch == "'":
            start = i
            i += 1
            while i < n:
                c = code[i]
                if c == "\\" and i + 1 < n:
                    i += 2
                    continue
                if c == "'":
                    i += 1
                    break
                if c == "\n":
                    break
                i += 1
            out.append(code[start:i])
            continue

        out.append(ch)
        i += 1

    return "".join(out)


_IF_FALSE = re.compile(r"^[ \t]*#if\s+false\b", re.M)


def strip_disabled_regions(code):
    """去掉 `#if false ... #endif` 区块（编译期就不存在的代码不算接线）。

    只处理字面 `#if false`，嵌套按 #if/#endif 配对计数。其它条件编译不动——
    那些在某些构建里是真的会编译的。
    """
    while True:
        m = _IF_FALSE.search(code)
        if not m:
            return code

        depth = 0
        idx = m.start()
        end = None
        for line_match in re.finditer(r"^[ \t]*#(if|endif)\b[^\n]*$", code[idx:], re.M):
            token = line_match.group(1)
            if token == "if":
                depth += 1
            else:
                depth -= 1
                if depth == 0:
                    end = idx + line_match.end()
                    break
        if end is None:
            end = len(code)

        removed = code[idx:end]
        code = code[:idx] + "\n" * removed.count("\n") + code[end:]
        # 下一轮从头重扫，处理同一文件里的多个 #if false


def clean_source(code):
    """守卫读 C# 时的标准清洗：先去禁用区块，再去注释。"""
    return strip_comments(strip_disabled_regions(code))
