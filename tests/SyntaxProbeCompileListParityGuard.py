#!/usr/bin/env python3
"""离线语法探针实际检查的文件集合，必须等于 compile_official.bat 的源码清单。

2026-09-23 实测的失明案例：`tools/verify_syntax.py` 自带一套 `echo(...\\.cs` 正则，
吃不下清单里残留的 `^` 续行写法（`echo(A.cs ^` + 下一行缩进的 `B.cs ^` …）。
cmd 会把这几行拼成一条 echo，写进响应文件后 csc 按空白切参数，所以**正式构建照常
编译**；但探针把 `echo(DebugAndTools\\SkyIsland\\SkyIslandJournal.cs ^` 整行丢掉了，
该文件因此从来没被离线语法检查过——探针既不报错，也不显示 979 与 980 的差。

本 guard 钉三件事：
  1. 探针取到的清单 == 共用解析器取到的清单（少一个就红，数字差多少都点名）；
  2. `OfficialCompileListFileExistenceGuard` 与探针共用同一份解析器，不各写一套；
  3. 探针没有偷偷重新长出自己的 `.cs` 正则，且 main() 里那道响应文件核对还在。

第 3 条用 AST 判，不用子串：注释掉、改名、被字符串挡住都糊弄不过去。
"""

import ast
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.path.insert(0, str(ROOT / "tests"))

from compile_list import normalize_source, parse_compile_sources, read_compile_sources, read_compile_text  # noqa: E402

PROBE = ROOT / "tools" / "verify_syntax.py"
SHARED = ROOT / "tools" / "compile_list.py"
EXISTENCE_GUARD = ROOT / "tests" / "OfficialCompileListFileExistenceGuard.py"
PARITY_CALL = "assert_rsp_covers_compile_list"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def brief(names, limit=8):
    shown = ", ".join(sorted(names)[:limit])
    if len(names) > limit:
        shown += " ...（共 {0} 个）".format(len(names))
    return shown


def imports_shared_parser(tree, module_path):
    """AST 判定：该模块确实从 compile_list 取 parse_compile_sources。"""
    for node in ast.walk(tree):
        if isinstance(node, ast.ImportFrom) and node.module == "compile_list":
            if any(alias.name == "parse_compile_sources" for alias in node.names):
                return True
        if isinstance(node, ast.Import):
            if any(alias.name == "compile_list" for alias in node.names):
                return True
    raise AssertionError(
        "{0} 必须从 tools/compile_list.py 取 parse_compile_sources，不要另写清单正则".format(module_path.name))


def no_private_source_regex(tree, module_path):
    """AST 判定：模块里没有任何匹配 `.cs` 的正则字面量（=没有第二套清单解析器）。"""
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        func = node.func
        name = func.attr if isinstance(func, ast.Attribute) else getattr(func, "id", None)
        if name not in {"compile", "findall", "finditer", "search", "match", "fullmatch", "split", "sub"}:
            continue
        for arg in node.args:
            if isinstance(arg, ast.Constant) and isinstance(arg.value, str):
                pattern = arg.value.replace("\\.", ".")
                require(".cs" not in pattern,
                        "{0} 又自带了匹配 .cs 的正则 `{1}`；清单解析只能走 "
                        "tools/compile_list.py".format(module_path.name, arg.value))


def parity_check_is_wired(tree):
    """AST 判定：main() 里仍然调用了响应文件核对，并且失败时返回非零。"""
    main_fn = next((n for n in ast.walk(tree)
                    if isinstance(n, ast.FunctionDef) and n.name == "main"), None)
    require(main_fn is not None, "tools/verify_syntax.py 里找不到 main()")

    called = any(
        isinstance(n, ast.Call)
        and (getattr(n.func, "id", None) == PARITY_CALL or getattr(n.func, "attr", None) == PARITY_CALL)
        for n in ast.walk(main_fn))
    require(called, "tools/verify_syntax.py 的 main() 必须调用 " + PARITY_CALL + "()")

    guarded = False
    for node in ast.walk(main_fn):
        if not isinstance(node, ast.If):
            continue
        if not any(isinstance(n, ast.Call)
                   and (getattr(n.func, "id", None) == PARITY_CALL
                        or getattr(n.func, "attr", None) == PARITY_CALL)
                   for n in ast.walk(node.test)):
            continue
        for stmt in ast.walk(node):
            if (isinstance(stmt, ast.Return) and isinstance(stmt.value, ast.Constant)
                    and stmt.value.value not in (0, None, False)):
                guarded = True
    require(guarded, PARITY_CALL + "() 的结果必须直接决定退出码（失败分支要 return 非零）")


def main():
    require(SHARED.is_file(), "缺少共用清单解析器 tools/compile_list.py")

    import verify_syntax
    import OfficialCompileListFileExistenceGuard as existence_guard

    expected = read_compile_sources()
    require(len(expected) > 0, "compile_official.bat 解析不出任何源码，解析器可能已失效")

    # 1) 探针实检集合 == 清单集合
    probed = {normalize_source(source) for source in verify_syntax.read_source_list()}
    wanted = set(expected)
    missing = wanted - probed
    extra = probed - wanted
    require(not missing,
            "语法探针漏检 {0} 个编译清单里的文件：{1}".format(len(missing), brief(missing)))
    require(not extra,
            "语法探针检查了 {0} 个清单外的文件：{1}".format(len(extra), brief(extra)))

    # 2) 存在性 guard 与探针同源
    text = read_compile_text()
    require(existence_guard.iter_compile_sources(text) == set(parse_compile_sources(text)),
            "OfficialCompileListFileExistenceGuard 的清单解析已与 tools/compile_list.py 分叉")

    # 3) 探针没有重新长出第二套解析器，核对逻辑仍决定退出码
    probe_tree = ast.parse(PROBE.read_text(encoding="utf-8"))
    imports_shared_parser(probe_tree, PROBE)
    no_private_source_regex(probe_tree, PROBE)
    parity_check_is_wired(probe_tree)

    guard_tree = ast.parse(EXISTENCE_GUARD.read_text(encoding="utf-8"))
    imports_shared_parser(guard_tree, EXISTENCE_GUARD)
    no_private_source_regex(guard_tree, EXISTENCE_GUARD)

    print("SyntaxProbeCompileListParityGuard: PASS（探针与编译清单同为 {0} 个源文件）".format(len(expected)))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (AssertionError, ValueError, OSError, ImportError, SyntaxError) as error:
        print("SyntaxProbeCompileListParityGuard: FAIL:", error)
        sys.exit(1)
