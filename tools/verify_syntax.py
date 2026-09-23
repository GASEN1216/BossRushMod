#!/usr/bin/env python3
"""C# 语法层探针（不是编译验证）。

本机通常没有装《鸭科夫》，`compile_official.bat` 需要
`Duckov_Data\\Managed\\Assembly-CSharp.dll` 等游戏程序集，因此无法真正编译。
但「语法是否合法」这一层不需要游戏程序集——用本机 .NET SDK 自带的 Roslyn
把 compile_official.bat 登记的全部源码过一遍，就能抓出括号不配对、关键字拼错、
C# 7.3 不支持的语法等问题。

**重要：本脚本通过 ≠ 编译通过。**
它抓不到类型不存在、方法签名不匹配、重载歧义这些需要真实引用才能发现的问题。
任何依赖本脚本的结论都必须写明「语法通过，未正式编译」。

用法：
    python tools/verify_syntax.py                  # 语法层（CS1xxx）检查
    python tools/verify_syntax.py --with-bcl       # 额外挂 .NET Framework 引用，
                                                   # 可多抓一层 BCL 用法错误
    python tools/verify_syntax.py --ref <dll> ...  # 追加任意引用程序集

退出码：0 = 无语法错误；1 = 有语法错误或环境不满足。
"""

import argparse
import glob
import os
import re
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# 清单解析只有 tools/compile_list.py 一份实现，本探针与
# tests/OfficialCompileListFileExistenceGuard.py 共用同一套规则。
# 历史教训见 compile_list 的模块注释：探针自带的 `echo(...)` 正则漏掉了清单里残留的
# `^` 续行写法，SkyIslandJournal.cs 从来没被语法检查过，而且既不报错也不显示差异。
from compile_list import (  # noqa: E402
    COMPILE_BAT as _SHARED_COMPILE_BAT,
    normalize_source,
    parse_compile_sources,
    read_compile_text,
)


def _force_utf8_output():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


_force_utf8_output()

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COMPILE_BAT = _SHARED_COMPILE_BAT

# 只有 CS1xxx 属于词法/语法层；CS0xxx 基本都是「找不到类型/成员」这类需要引用才能判定的。
# 注意 CS16xx（如 CS1631「无法在 catch 子句体中生成值」）也在这个区间内，
# 但它属于迭代器体分析，**必须挂上 BCL 引用**才会被 Roslyn 走到——
# 这就是 --with-bcl 默认开启的原因（见 main() 里的说明）。
RE_SYNTAX_ERROR = re.compile(r": error (CS1\d{3}):")
RE_ANY_ERROR = re.compile(r": error (CS\d+):")

BCL_ASSEMBLIES = [
    "mscorlib.dll", "System.dll", "System.Core.dll",
    "System.Runtime.Serialization.dll", "System.Xml.dll",
    "System.Xml.Linq.dll", "System.Data.dll", "System.Net.Http.dll",
]


def find_csc():
    """定位本机 .NET SDK 里的 Roslyn csc.dll（取版本号最大的 SDK）。"""
    roots = [
        r"C:\Program Files\dotnet\sdk",
        r"C:\Program Files (x86)\dotnet\sdk",
    ]
    candidates = []
    for root in roots:
        if not os.path.isdir(root):
            continue
        for name in os.listdir(root):
            path = os.path.join(root, name, "Roslyn", "bincore", "csc.dll")
            if os.path.isfile(path):
                candidates.append((name, path))
    if not candidates:
        return None
    candidates.sort(key=lambda item: item[0], reverse=True)
    return candidates[0][1]


def find_bcl_dir():
    base = r"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework"
    if not os.path.isdir(base):
        return None
    versions = sorted((d for d in os.listdir(base) if d.startswith("v")), reverse=True)
    for v in versions:
        candidate = os.path.join(base, v)
        if os.path.isfile(os.path.join(candidate, "mscorlib.dll")):
            return candidate
    return None


def read_source_list():
    """从 compile_official.bat 取源码清单，路径换成 csc 习惯的反斜杠形式。"""
    return [source.replace("/", "\\") for source in parse_compile_sources(read_compile_text(COMPILE_BAT))]


def assert_rsp_covers_compile_list(rsp_sources):
    """断言真正写进响应文件的源码集合 == 编译清单集合。

    read_source_list() 已经直接取自共用解析器，正常情况下这条恒真；它挡的是
    「日后有人在解析与写 rsp 之间插一道过滤/跳过，探针却照样 PASS」——
    探针少检文件必须变红，不能像 SkyIslandJournal.cs 那次一样静默少检。
    """
    expected = set(parse_compile_sources(read_compile_text(COMPILE_BAT)))
    actual = {normalize_source(source) for source in rsp_sources}
    if actual == expected:
        return True

    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    print("[FAIL] 探针实际检查的源码集合与 compile_official.bat 清单不一致"
          "（清单 {0} 个，实检 {1} 个）".format(len(expected), len(actual)))
    if missing:
        print("   漏检 {0} 个：".format(len(missing)))
        for name in missing[:10]:
            print("      " + name)
        if len(missing) > 10:
            print("      ...（还有 {0} 个）".format(len(missing) - 10))
    if extra:
        print("   清单外多检 {0} 个：".format(len(extra)))
        for name in extra[:10]:
            print("      " + name)
        if len(extra) > 10:
            print("      ...（还有 {0} 个）".format(len(extra) - 10))
    return False


def main():
    parser = argparse.ArgumentParser(description="C# 语法层探针（非编译验证）")
    # BCL 引用默认开启。原因（2026-09-01 踩过）：不挂引用时 Roslyn 在
    # 「找不到 System.Object / IEnumerator」阶段就停下，根本走不到迭代器体分析，
    # 于是 CS1631（catch 子句体内 yield）这类错误**完全检测不到**——
    # 本探针曾据此给出「语法通过」，实机编译却直接报错。
    parser.add_argument("--with-bcl", action="store_true", default=True,
                        help="挂上 .NET Framework 引用程序集（默认开启）")
    parser.add_argument("--no-bcl", dest="with_bcl", action="store_false",
                        help="不挂 BCL 引用（会漏掉 CS16xx 类迭代器错误，仅排查探针自身问题时用）")
    parser.add_argument("--ref", action="append", default=[],
                        help="追加引用程序集路径，可重复")
    parser.add_argument("--show", type=int, default=20, help="最多打印多少条错误")
    args = parser.parse_args()

    csc = find_csc()
    if not csc:
        print("[FAIL] 未找到 Roslyn csc.dll（需要安装 .NET SDK）")
        return 1

    sources = read_source_list()
    if not sources:
        print("[FAIL] 未能从 compile_official.bat 解析出源码清单")
        return 1

    missing = [s for s in sources if not os.path.isfile(os.path.join(REPO_ROOT, s))]
    if missing:
        print("[FAIL] 编译清单里有 {0} 个文件在磁盘上不存在：".format(len(missing)))
        for m in missing[:10]:
            print("   " + m)
        return 1

    print("语法探针: csc={0}".format(csc))
    print("          源文件 {0} 个（来自 compile_official.bat）".format(len(sources)))

    refs = list(args.ref)
    if args.with_bcl:
        bcl = find_bcl_dir()
        if not bcl:
            print("[WARN] 未找到 .NET Framework 引用程序集，跳过 --with-bcl")
        else:
            for dll in BCL_ASSEMBLIES:
                path = os.path.join(bcl, dll)
                if os.path.isfile(path):
                    refs.append(path)
            print("          BCL 引用 {0} 个（{1}）".format(len(refs), bcl))

    tmpdir = tempfile.mkdtemp(prefix="bossrush_syntax_")
    rsp_path = os.path.join(tmpdir, "syntax.rsp")
    out_dll = os.path.join(tmpdir, "syntax_probe.dll")

    rsp_sources = []
    with open(rsp_path, "w", encoding="utf-8") as fh:
        fh.write("/target:library\n")
        fh.write("/langversion:7.3\n")
        fh.write("/noconfig\n")
        fh.write("/nowarn:CS0436,CS0162,CS0414\n")
        if not refs:
            fh.write("/nostdlib+\n")
        fh.write('/out:"{0}"\n'.format(out_dll))
        for ref in refs:
            fh.write('/reference:"{0}"\n'.format(ref))
        for src in sources:
            fh.write('"{0}"\n'.format(os.path.join(REPO_ROOT, src)))
            rsp_sources.append(src)

    # 在启动 csc 之前核对：写进响应文件的就是编译清单本身，一个不少、一个不多。
    if not assert_rsp_covers_compile_list(rsp_sources):
        print("")
        print("verify_syntax: FAIL")
        return 1

    proc = subprocess.run(["dotnet", csc, "@" + rsp_path],
                          cwd=REPO_ROOT, capture_output=True, text=True,
                          encoding="utf-8", errors="replace")
    output = (proc.stdout or "") + (proc.stderr or "")

    syntax_errors = [line for line in output.splitlines() if RE_SYNTAX_ERROR.search(line)]

    codes = {}
    for line in output.splitlines():
        m = RE_ANY_ERROR.search(line)
        if m:
            codes[m.group(1)] = codes.get(m.group(1), 0) + 1

    total_errors = sum(codes.values())
    top = sorted(codes.items(), key=lambda kv: kv[1], reverse=True)[:6]
    print("          诊断总计 {0} 条；错误码分布 {1}".format(
        total_errors, ", ".join("{0}x{1}".format(c, n) for c, n in top) or "无"))

    if syntax_errors:
        print("")
        print("=== 语法错误（CS1xxx）{0} 条 ===".format(len(syntax_errors)))
        for line in syntax_errors[:args.show]:
            print("  " + line.strip())
        if len(syntax_errors) > args.show:
            print("  ...（还有 {0} 条）".format(len(syntax_errors) - args.show))
        print("")
        print("verify_syntax: FAIL")
        return 1

    print("")
    print("verify_syntax: PASS（语法层 CS1xxx 零错误）")
    print("提醒: 这不是编译验证。类型/签名/重载问题需要游戏程序集，")
    print("      必须在装有《鸭科夫》的 Windows 机器上跑 compile_official.bat 才算编译通过。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
