#!/usr/bin/env python3
"""构建之后实查 DLL 里有没有全自动实机验收的 Dev 专用标识（2026-09-14）。

全自动实机验收（DebugAndTools/F3GameplayValidationAutotest*.cs 等）整份包在 #if BOSSRUSH_DEV 里：会写专用测试档的剧情与背包，
正式构建里必须一个标识都查不到。守卫（tests/F3AutotestOrchestratorGuard.py）只能看源码；这里看编出来的 DLL。

.NET 程序集的类型名、成员名在 #Strings 堆里是 UTF-8，字符串字面量与常量在 #US 堆 / Constant 表里是 UTF-16LE，两种编码都搜。
Dev 构建用 --expect present 跑一次，证明探针本身找得到（否则 absent 的绿没有意义）。

用法：
  python tools/check_dll_identifiers.py --dll Build/BossRush.dll --expect absent
  python tools/check_dll_identifiers.py --dll <Dev 构建的 BossRush.dll> --expect present
"""
import argparse
import hashlib
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# 类型名、方法名与只在 Dev 代码里出现的字符串常量。
IDENTIFIERS = (
    "ResourcePerformanceWindow",
    "ResourcePerformanceMetrics",
    "SampleResourcePerformance",
    "F3AutotestJudges",
    "F3AutotestSnapshotRecord",
    "AutotestWriteAllowed",
    "RunSkyIslandAutotestLeg",
    "FinishAutotestRestoreSynchronously",
    "CaptureAutotestShot",
    "DevAutotestReplace",
    "DevAutotestTeleport",
    "BossRush_Validation_AutotestSnapshot_v1",
    "SkyIslandAutotest.json",
    "bossrush-autotest-manifest/1",
)


def scan(data):
    found = []
    for name in IDENTIFIERS:
        if name.encode("utf-8") in data or name.encode("utf-16-le") in data:
            found.append(name)
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dll", type=Path, default=ROOT / "Build" / "BossRush.dll", help="要检查的 BossRush.dll")
    parser.add_argument("--expect", choices=("absent", "present"), default="absent",
                        help="absent：正式构建一个都不许有；present：Dev 构建必须全部找得到")
    args = parser.parse_args()
    if not args.dll.is_file():
        print("check_dll_identifiers: FAIL 找不到 DLL：%s" % args.dll)
        return 2
    data = args.dll.read_bytes()
    found = scan(data)
    missing = [name for name in IDENTIFIERS if name not in found]
    digest = hashlib.sha256(data).hexdigest().upper()
    print("check_dll_identifiers: dll=%s size=%d sha256=%s" % (args.dll, len(data), digest))
    if args.expect == "absent":
        if found:
            print("check_dll_identifiers: FAIL 正式构建里查到了 Dev 专用标识：%s" % ", ".join(found))
            return 1
        print("check_dll_identifiers: PASS 正式构建里 %d 个 Dev 专用标识一个都没有" % len(IDENTIFIERS))
        return 0
    if missing:
        print("check_dll_identifiers: FAIL Dev 构建里缺少标识（探针失效或代码没编进去）：%s" % ", ".join(missing))
        return 1
    print("check_dll_identifiers: PASS Dev 构建里 %d 个标识全部找得到（探针有效）" % len(IDENTIFIERS))
    return 0


if __name__ == "__main__":
    sys.exit(main())
