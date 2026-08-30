#!/usr/bin/env python3
"""
ModeHCompileManifestGuard — Mode H 编译清单守卫（设计提案 §26.1）。

不变式：
- 每个新增 Mode H `.cs` 在 compile_official.bat 中出现且**只出现一次**；
- 清单里不得出现已删除的 Mode H 源文件；
- 每条 Mode H 行的格式与整份清单一致（4 空格 + 路径 + ` ^`）；
- Localization/ModeHLocalization.cs 也必须登记；
- Mode H 的数据目录与展示 bundle 都有显式部署块。
"""
import io
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
COMPILE_BAT = os.path.join(REPO_ROOT, "compile_official.bat")
EXTRA_SOURCES = [r"Localization\ModeHLocalization.cs"]


def read_bat():
    with io.open(COMPILE_BAT, "r", encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def main():
    errors = []

    if not os.path.isdir(MODEH_DIR):
        print("ModeHCompileManifestGuard: FAIL (1 errors)")
        print("  - [File] 缺少 ModeH/ 目录")
        return 1

    text = read_bat()
    lines = text.splitlines()

    # 清单里登记的 Mode H 行
    listed = {}
    for line in lines:
        stripped = line.strip()
        # 清单自 2026-08-30 起改经 Roslyn 响应文件传递，每行形如 `echo(<路径>`
        # （原因见 compile_official.bat 顶部注释：拼成一条命令行会超进程创建上限）。
        if stripped.startswith("echo("):
            stripped = stripped[len("echo("):].strip()
        if not stripped.startswith("ModeH\\") and not stripped.startswith("Localization\\ModeH"):
            continue
        path = stripped[:-2].strip() if stripped.endswith("^") else stripped
        listed[path] = listed.get(path, 0) + 1
        # 格式：4 空格缩进 + 路径 + " ^"
        if not re.match(r"^echo\([\w\\.]+\.cs$", line):
            errors.append("[Format] 清单行格式不符: " + repr(line))

    # 磁盘上的 Mode H 源文件
    on_disk = [
        "ModeH\\" + name
        for name in sorted(os.listdir(MODEH_DIR))
        if name.endswith(".cs")
    ]
    on_disk.extend(EXTRA_SOURCES)

    for path in on_disk:
        count = listed.get(path, 0)
        if count == 0:
            errors.append("[Missing] 未登记进 compile_official.bat: " + path)
        elif count > 1:
            errors.append("[Duplicate] 在清单中出现 {} 次: {}".format(count, path))

    for path in sorted(listed.keys()):
        if path in on_disk:
            continue
        errors.append("[Stale] 清单登记了不存在的源文件: " + path)

    # 数据与展示资源的部署块
    for required, desc in [
        ("Assets\\Data\\ModeH", "Mode H 数据目录部署块"),
        ("Assets\\ui\\modeh_presentation", "Mode H 展示 bundle 部署块"),
    ]:
        if required not in text:
            errors.append("[Deploy] compile_official.bat 缺少 " + desc)

    if errors:
        print("ModeHCompileManifestGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHCompileManifestGuard: PASS ({} 个 Mode H 源文件已登记)".format(len(on_disk)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
