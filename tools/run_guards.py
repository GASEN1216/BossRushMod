#!/usr/bin/env python3
"""聚合式 guard runner。

替代 `for %f in (tests\\*.py) do python %f` 与 validate_refactor_step.bat 里
fail-fast 的循环：那两种写法都有硬伤——

- fail-fast 会让第一个红项**永久遮蔽它之后的所有 guard**。仓库里存在一个既有红项
  `DragonKingBossGunRocketSplitGuard`，按字母序排在 D，后面还有 300+ 个脚本从未被跑到。
- 不聚合结果时，跑完 439 个脚本只能靠人眼翻屏幕找哪个失败了。
- guard 用中文输出，Windows cp936 控制台下会 mojibake。

本 runner 全量跑不中断、聚合 PASS/FAIL、打印失败清单与耗时，并支持用基线文件
把「已知红项」与「新出现的红项」区分开——CI 与本地都只需要关心后者。

用法：
    python tools/run_guards.py                     # 全量跑
    python tools/run_guards.py --changed-only      # 只跑与 git 改动相关的 guard
    python tools/run_guards.py --filter ModeG      # 只跑名字含 ModeG 的
    python tools/run_guards.py --verbose           # 打印失败 guard 的输出

退出码：0 = 没有「新增失败」；1 = 有新增失败或基线文件已过期。
已知红项（tests/known_red_guards.txt）失败不影响退出码，但会单独列出。
"""

import argparse
import os
import re
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor

def _force_utf8_output():
    """guard 与本 runner 都输出中文；Windows 默认 cp936 控制台会 mojibake。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


_force_utf8_output()

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TESTS_DIR = os.path.join(REPO_ROOT, "tests")
KNOWN_RED_FILE = os.path.join(TESTS_DIR, "known_red_guards.txt")

# 需要实机日志/游戏进程的脚本，不属于结构守卫，默认不跑
SKIP_ALWAYS = {"SmokeLogScan.py"}


def load_known_red():
    """读取已知红项基线；每行一个脚本文件名，# 开头为注释。"""
    if not os.path.isfile(KNOWN_RED_FILE):
        return {}
    known = {}
    with open(KNOWN_RED_FILE, encoding="utf-8") as fh:
        for raw in fh:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            if "|" in line:
                name, reason = line.split("|", 1)
                known[name.strip()] = reason.strip()
            else:
                known[line] = ""
    return known


def collect_scripts(filter_text, changed_only):
    names = []
    for fn in sorted(os.listdir(TESTS_DIR)):
        if not fn.endswith(".py"):
            continue
        if fn in SKIP_ALWAYS:
            continue
        if not (fn.endswith("Guard.py") or fn.endswith("PropertyTest.py") or fn.endswith("Tests.py")):
            continue
        names.append(fn)

    if filter_text:
        lowered = filter_text.lower()
        names = [n for n in names if lowered in n.lower()]

    if changed_only:
        names = filter_by_changed_files(names)

    return names


def changed_paths():
    """当前工作区相对 HEAD 的改动文件（含未跟踪）。"""
    paths = set()
    for cmd in (["git", "diff", "--name-only", "HEAD"],
                ["git", "ls-files", "--others", "--exclude-standard"]):
        try:
            out = subprocess.run(cmd, cwd=REPO_ROOT, capture_output=True, text=True, timeout=60)
        except Exception:
            continue
        if out.returncode == 0:
            for line in out.stdout.splitlines():
                line = line.strip()
                if line:
                    paths.add(line)
    return paths


def filter_by_changed_files(names):
    """粗筛：guard 脚本正文里提到了任一改动文件的路径或其文件名主干就跑它。

    宁可多跑不可漏跑——匹配不上时回退为全量。
    """
    changed = changed_paths()
    if not changed:
        return names

    stems = set()
    for p in changed:
        p_norm = p.replace("\\", "/")
        stems.add(p_norm)
        base = os.path.basename(p_norm)
        stems.add(base)
        stem, _ = os.path.splitext(base)
        if len(stem) >= 4:
            stems.add(stem)

    selected = []
    for name in names:
        path = os.path.join(TESTS_DIR, name)
        try:
            text = open(path, encoding="utf-8", errors="ignore").read()
        except Exception:
            selected.append(name)
            continue
        if any(s in text for s in stems):
            selected.append(name)

    return selected or names


def run_one(name):
    path = os.path.join(TESTS_DIR, name)
    env = dict(os.environ)
    # guard 输出中文；cp936 控制台下不强制 UTF-8 会 mojibake
    env["PYTHONIOENCODING"] = "utf-8"
    env["PYTHONUTF8"] = "1"
    start = time.time()
    try:
        proc = subprocess.run(
            [sys.executable, path],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=env,
            timeout=300,
        )
        code = proc.returncode
        output = (proc.stdout or "") + (proc.stderr or "")
    except subprocess.TimeoutExpired:
        code = 124
        output = "TIMEOUT: 超过 300 秒未结束"
    except Exception as e:
        code = 125
        output = "RUNNER ERROR: " + str(e)
    return name, code, output, time.time() - start


def main():
    parser = argparse.ArgumentParser(description="BossRushMod guard 聚合运行器")
    parser.add_argument("--filter", default="", help="只跑名字包含该子串的 guard")
    parser.add_argument("--changed-only", action="store_true",
                        help="只跑与当前 git 改动相关的 guard（匹配不到时回退全量）")
    parser.add_argument("--verbose", action="store_true", help="打印失败 guard 的完整输出")
    parser.add_argument("--jobs", type=int, default=min(8, (os.cpu_count() or 4)),
                        help="并发进程数（默认 CPU 数，上限 8）")
    parser.add_argument("--list-red", action="store_true", help="只列出当前失败项，不打印其它内容")
    args = parser.parse_args()

    scripts = collect_scripts(args.filter, args.changed_only)
    if not scripts:
        print("没有匹配的 guard 脚本")
        return 0

    known_red = load_known_red()

    if not args.list_red:
        print("guard runner: 共 {0} 个脚本，并发 {1}".format(len(scripts), args.jobs))
        if args.changed_only:
            print("  模式: --changed-only")
        if known_red:
            print("  已知红项基线: {0} 条（tests/known_red_guards.txt）".format(len(known_red)))
        print("")

    started = time.time()
    results = []
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for result in pool.map(run_one, scripts):
            results.append(result)

    passed = [r for r in results if r[1] == 0]
    failed = [r for r in results if r[1] != 0]

    new_failures = [r for r in failed if r[0] not in known_red]
    known_failures = [r for r in failed if r[0] in known_red]
    # 基线里登记了但现在已经绿了 —— 基线过期，应当清理，否则会掩盖回归
    stale_baseline = sorted(set(known_red) & {r[0] for r in passed})

    elapsed = time.time() - started

    if known_failures:
        print("=== 已知红项（不计入失败） ===")
        for name, code, _out, dur in sorted(known_failures):
            reason = known_red.get(name) or "未登记原因"
            print("  [KNOWN-RED] {0}  ({1})".format(name, reason))
        print("")

    if new_failures:
        print("=== 新增失败 ===")
        for name, code, out, dur in sorted(new_failures):
            print("  [FAIL] {0}  exit={1}  {2:.1f}s".format(name, code, dur))
            if args.verbose:
                for line in out.strip().splitlines()[-25:]:
                    print("         " + line)
            else:
                tail = [l for l in out.strip().splitlines() if l.strip()]
                if tail:
                    print("         " + tail[-1][:200])
        print("")

    if stale_baseline:
        print("=== 基线过期（已转绿，请从 tests/known_red_guards.txt 移除） ===")
        for name in stale_baseline:
            print("  [STALE-BASELINE] " + name)
        print("")

    print("汇总: PASS={0}  NEW-FAIL={1}  KNOWN-RED={2}  用时 {3:.1f}s".format(
        len(passed), len(new_failures), len(known_failures), elapsed))

    if new_failures or stale_baseline:
        print("guard runner: FAIL")
        return 1

    print("guard runner: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
