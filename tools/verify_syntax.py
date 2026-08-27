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


def _force_utf8_output():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


_force_utf8_output()

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COMPILE_BAT = os.path.join(REPO_ROOT, "compile_official.bat")

# compile_official.bat 用 `^` 续行列出源码；最后一条没有续行符
RE_CONTINUED = re.compile(r"^\s+(\S+\.cs)\s+\^\s*$", re.M)
RE_LAST = re.compile(r"^\s+(\S+\.cs)\s*$", re.M)

# 只有 CS1xxx 属于词法/语法层；CS0xxx 基本都是「找不到类型/成员」这类需要引用才能判定的
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
    text = open(COMPILE_BAT, encoding="utf-8", errors="ignore").read()
    names = RE_CONTINUED.findall(text) + RE_LAST.findall(text)
    seen = set()
    ordered = []
    for name in names:
        norm = name.replace("/", "\\")
        if norm not in seen:
            seen.add(norm)
            ordered.append(norm)
    return ordered


def main():
    parser = argparse.ArgumentParser(description="C# 语法层探针（非编译验证）")
    parser.add_argument("--with-bcl", action="store_true",
                        help="挂上 .NET Framework 引用程序集，可多抓一层 BCL 用法错误")
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
