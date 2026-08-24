#!/usr/bin/env python3
"""
ModeGReleaseAvailabilityGuard — Mode G 发布可用性门控守卫（规格 §20 第 2 条）。

不变式：
- IsProductionReady 为 const bool（切换只能显式改源码）；
- 正式构建 AllowDevTestEntry / AllowDevRawPngFallback 必须 false；
- CurrentVerificationRevision 非空冻结字符串；
- 独立交互入口与 TryStartModeG 均消费 IsProductionReady 门控；
- 生产池不得使用开发署名兜底（!IsProductionReady && AllowDevTestEntry 双条件）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")

CHECKS = [
    ("IsProductionReadyEnabled",
     "ModeGAvailability.cs",
     r"public const bool IsProductionReady = true",
     "Owner 已批准正式入口"),
    ("DevTestEntryFrozenOff",
     "ModeGAvailability.cs",
     r"public const bool AllowDevTestEntry = false",
     "正式构建不得开放开发测试入口"),
    ("DevRawPngFallbackFrozenOff",
     "ModeGAvailability.cs",
     r"public const bool AllowDevRawPngFallback = false",
     "正式构建不得静默使用 raw PNG 冒充发布 bundle"),
    ("VerificationRevisionFrozen",
     "ModeGAvailability.cs",
     r'public const string CurrentVerificationRevision = "2026-08-10\.v1"',
     "verification revision 冻结值"),
    ("MapSelectionMapsApproved",
     "ModeGMapSupportRegistry.cs",
     r"ModBehaviour\.GetAllMapConfigs\(\)[\s\S]{0,1800}?ModeGMapSupportStatus\.Verified,",
     "地图选择 UI 配置由 owner 批准为正式 Mode G 地图"),
    ("InteractableGated",
     "ModeGInteractable.cs",
     r"!ModeGAvailability\.IsProductionReady\s*&&\s*!ModeGAvailability\.AllowDevTestEntry",
     "独立交互入口受 IsProductionReady/AllowDevTestEntry 双门控"),
    ("EntryGated",
     "ModeGEntry.cs",
     r"if\s*\(!ModeGAvailability\.IsProductionReady\s*&&\s*"
     r"!ModeGAvailability\.AllowDevTestEntry\)",
     "TryStartModeG 路径受 IsProductionReady/AllowDevTestEntry 双门控"),
    ("ProductionPoolNoDevFallback",
     "ModeGEncounterVariation.cs",
     r"!ModeGAvailability\.IsProductionReady\s+&&\s*ModeGAvailability\.AllowDevTestEntry",
     "开发署名兜底仅在 !IsProductionReady && AllowDevTestEntry"),
    ("RawPngFallbackGated",
     "ModeGPresentationAssetCache.cs",
     r"if\s*\(!ModeGAvailability\.AllowDevRawPngFallback\)\s*return null",
     "raw PNG fallback 受 AllowDevRawPngFallback 门控"),
]


def main():
    errors = []
    for name, filename, pattern, desc in CHECKS:
        path = os.path.join(MODEG_DIR, filename)
        if not os.path.exists(path):
            errors.append("[{}] 文件不存在: {}".format(name, filename))
            continue
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            content = fh.read()
        if not re.search(pattern, content):
            errors.append("[{}] 不满足 ({}): {}".format(name, desc, filename))

    if errors:
        print("ModeGReleaseAvailabilityGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGReleaseAvailabilityGuard: PASS")
    print("  门控检查: {} 项".format(len(CHECKS)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
