#!/usr/bin/env python3
"""
ModeGPresentationAssetGuard — Mode G 展示资源守卫（规格 §20 第 10 条）。

不变式：
- bundle 名 modeg_presentation、路径 Assets/ui/modeg_presentation；
- 首版 bundle <256 KiB（磁盘文件存在时实测）；
- 缺包 fail-closed：TryPreflight 失败返回 false 且缓存结果；
- 每 runtime 至多一次 LoadFromFile（_loadAttempted 单次闸门）；
- raw PNG fallback 仅开发构建（AllowDevRawPngFallback 门控）；
- Unload 幂等（终局调用，重置全部缓存标记）；
- 徽记/横幅资产名冻结：modeg_echo_emblem / modeg_echo_banner。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(REPO_ROOT, "ModeG", "ModeGPresentationAssetCache.cs")
INTERACTABLE = os.path.join(REPO_ROOT, "ModeG", "ModeGInteractable.cs")
BUNDLE_PATH = os.path.join(REPO_ROOT, "Assets", "ui", "modeg_presentation")
MAX_BUNDLE_BYTES = 256 * 1024


def main():
    errors = []
    if not os.path.exists(CACHE):
        errors.append("ModeGPresentationAssetCache.cs 不存在")
        print("ModeGPresentationAssetGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    with open(CACHE, "r", encoding="utf-8", errors="replace") as fh:
        content = fh.read()
    if not os.path.exists(INTERACTABLE):
        errors.append("[InteractableMissing] ModeGInteractable.cs 不存在")
        interactable = ""
    else:
        with open(INTERACTABLE, "r", encoding="utf-8", errors="replace") as fh:
            interactable = fh.read()

    checks = [
        ("BundleName",
         r'public const string PresentationBundleName = "modeg_presentation"',
         "bundle 名冻结"),
        ("BundlePath",
         r'BundleRelativePath = "Assets/ui/modeg_presentation"',
         "bundle 路径冻结"),
        ("EmblemAsset",
         r'EmblemAssetName = "modeg_echo_emblem"',
         "徽记资产名冻结"),
        ("BannerAsset",
         r'BannerAssetName = "modeg_echo_banner"',
         "横幅资产名冻结"),
        ("PreflightFailClosed",
         r"public static bool TryPreflight\(\)[\s\S]*?_preflightResult = false",
         "TryPreflight 缺包 fail-closed"),
        ("SingleLoadGate",
         r"if \(_loadAttempted\) return false;\s*\n\s*_loadAttempted = true;",
         "每 runtime 至多一次 LoadFromFile"),
        ("RawFallbackDevOnly",
         r"if \(!ModeGAvailability\.AllowDevRawPngFallback\) return null;",
         "raw PNG fallback仅开发构建"),
        ("UnloadIdempotent",
         r"public static void Unload\(\)[\s\S]*?_loadAttempted = false;"
         r"[\s\S]*?_preflightAttempted = false;",
         "Unload 幂等并重置缓存标记"),
        ("UnloadTrue",
         r"_bundle\.Unload\(true\)",
         "Unload 完全卸载"),
        ("BothSpritesPreflight",
         r"Sprite emblem = GetEmblemSprite\(\);\s*"
         r"Sprite banner = GetBannerSprite\(\);\s*"
         r"if \(emblem != null && banner != null\)",
         "预检必须同时实取 emblem/banner 两张 Sprite"),
    ]
    for name, pattern, desc in checks:
        if not re.search(pattern, content):
            errors.append("[{}] 不满足: {}".format(name, desc))

    if interactable:
        for name, pattern, desc in [
                ("EmblemPresented", r"GetEmblemSprite\(\)[\s\S]{0,700}?emblemImage\.sprite = emblem;",
                 "确认页实际展示 emblem"),
                ("BannerPresented", r"GetBannerSprite\(\)[\s\S]{0,700}?bannerImage\.sprite = banner;",
                 "确认页实际展示 banner")]:
            if not re.search(pattern, interactable):
                errors.append("[{}] 不满足: {}".format(name, desc))

    # bundle 体积首版 <256 KiB（文件不存在时仅告警磁盘资产缺失）
    if os.path.exists(BUNDLE_PATH):
        size = os.path.getsize(BUNDLE_PATH)
        if size > MAX_BUNDLE_BYTES:
            errors.append("[BundleSize] bundle {} B 超过首版 256 KiB 上限".format(size))
    else:
        errors.append("[BundleMissing] 展示 bundle 不存在（fail-closed 前提）: " + BUNDLE_PATH)

    if errors:
        print("ModeGPresentationAssetGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGPresentationAssetGuard: PASS")
    if os.path.exists(BUNDLE_PATH):
        print("  bundle 体积: {} B (<256 KiB)".format(os.path.getsize(BUNDLE_PATH)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
