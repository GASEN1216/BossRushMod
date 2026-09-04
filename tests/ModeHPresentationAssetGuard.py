#!/usr/bin/env python3
"""
ModeHPresentationAssetGuard — Mode H 展示资源守卫（设计提案 §23.3、§26.1）。

不变式：
- bundle / 路径 / 两个 Sprite 短名冻结；
- 运行时缓存每 runtime 最多一次 LoadFromFile，入口预检必须同时取到两张 Sprite；
- 生产缺包 fail-closed；AllowDevRawPngFallback 是编译期常量且恒为 false；
- 幂等 Unload(true)：先销毁引用徽记/横幅的 UI 根、清空 Sprite 引用，再卸载；
- compile_official.bat / test_bossrush_official.bat 都有 bundle 部署路径，
  且缺包时明确失败或把 Mode H 标成不可用，不能静默打包；
- 兄弟 Unity 工程的构建器存在，且断言 label / 两条完整 asset path / 空依赖 /
  尺寸 / 体积 / 两条固定短名可 LoadAsset。

**交付说明**：bundle 本身是 local-only 制品，由同机兄弟 Unity 工程生成后复制。
资源尚未落位时，只有“bundle 文件存在”这一条断言为红（PENDING-RESOURCE），
其余断言必须全绿；资源落位后本守卫应当整体转绿。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

CACHE = os.path.join(REPO_ROOT, "ModeH", "ModeHPresentationAssetCache.cs")
AVAILABILITY = os.path.join(REPO_ROOT, "ModeH", "ModeHAvailability.cs")
COMPILE_BAT = os.path.join(REPO_ROOT, "compile_official.bat")
TEST_BAT = os.path.join(REPO_ROOT, "test_bossrush_official.bat")
BUNDLE = os.path.join(REPO_ROOT, "Assets", "ui", "modeh_presentation")

UNITY_PROJECT = os.path.join(
    os.path.dirname(REPO_ROOT), "duckov_modding-main", "UnityFiles", "BossRush")
BUILDER = os.path.join(UNITY_PROJECT, "Assets", "Editor", "ModeHPresentationBundleBuilder.cs")
ART_MANIFEST = os.path.join(UNITY_PROJECT, "ArtSource", "ModeH", "asset-manifest.json")

BUNDLE_NAME = "modeh_presentation"
EMBLEM_SHORT_NAME = "ModeH_BlackMarketCup_Emblem"
BANNER_SHORT_NAME = "ModeH_BlackMarketCup_Banner"
MAX_BUNDLE_BYTES = 256 * 1024


def check_cache(errors):
    source = read_text(CACHE)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHPresentationAssetCache.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r'"{}"'.format(BUNDLE_NAME), "冻结 bundle 名"),
        (r'"{}"'.format(EMBLEM_SHORT_NAME), "冻结徽记短名"),
        (r'"{}"'.format(BANNER_SHORT_NAME), "冻结横幅短名"),
        (r"AssetBundle\.LoadFromFile", "经官方 API 加载"),
        (r"LoadAsset<Sprite>", "按短名取 Sprite"),
        (r"Unload\(true\)", "幂等卸载"),
        (r"ModeHAvailability\.AllowDevRawPngFallback", "raw fallback 只由编译期常量控制"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Cache] 不满足: " + desc)

    # 每 runtime 最多一次 LoadFromFile
    if len(re.findall(r"AssetBundle\.LoadFromFile", code)) != 1:
        errors.append("[Cache] LoadFromFile 只允许一个调用点")
    if not re.search(r"if \(_loadAttempted\)|_loadAttempted = true;", code):
        errors.append("[Cache] 必须有“每 runtime 最多加载一次”的一次性标记")

    # 卸载顺序：先清 Sprite 引用，再 Unload(true)
    unload = re.search(r"(?:public|internal) static void Unload[\s\S]*?\n        \}", code)
    if unload:
        body = unload.group(0)
        emblem_at = body.find("_emblemSprite = null")
        unload_at = body.find("Unload(true)")
        if emblem_at < 0 or unload_at < 0 or emblem_at > unload_at:
            errors.append("[Cache] 必须先清空 Sprite 引用再 Unload(true)")

    # 入口预检必须同时取到两张
    preflight = re.search(
        r"(?:public|internal) static bool (?:EnsureLoaded|Preflight)\([\s\S]*?\n        \}", code)
    if preflight:
        body = preflight.group(0)
        if EMBLEM_SHORT_NAME not in body or BANNER_SHORT_NAME not in body:
            errors.append("[Cache] 入口预检必须同时取到徽记与横幅")


def check_dev_gate(errors):
    source = read_text(AVAILABILITY)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHAvailability.cs")
        return
    code = strip_cs_comments(source)
    if not re.search(r"public const bool AllowDevRawPngFallback = false;", code):
        errors.append("[DevGate] AllowDevRawPngFallback 必须是编译期常量且为 false")
    if not re.search(r"public const bool AllowDevControlPointHarness = false;", code):
        errors.append("[DevGate] AllowDevControlPointHarness 必须是编译期常量且为 false")


def check_bats(errors):
    for path, label in [(COMPILE_BAT, "compile_official.bat"), (TEST_BAT, "test_bossrush_official.bat")]:
        text = read_text(path)
        if text is None:
            errors.append("[Bat] 缺少 " + label)
            continue
        if "Assets\\ui\\" + BUNDLE_NAME not in text:
            errors.append("[Bat] {} 缺少 bundle 部署路径".format(label))
        # 缺包时必须明确失败或把 Mode H 标成不可用，不能静默
        window = ""
        index = text.find(BUNDLE_NAME)
        while index >= 0:
            window += text[max(0, index - 400):index + 400]
            index = text.find(BUNDLE_NAME, index + 1)
        if "fails closed" not in window and "fail" not in window.lower() \
                and "WARNING" not in window:
            errors.append("[Bat] {} 缺包时必须明确失败或标记 Mode H 不可用".format(label))


def check_builder(errors):
    source = read_text(BUILDER)
    if source is None:
        errors.append("[Builder] 缺少兄弟 Unity 工程的 ModeHPresentationBundleBuilder.cs")
        return
    checks = [
        (r'BundleName = "{}"'.format(BUNDLE_NAME), "冻结 bundle 名"),
        (r'Assets/UI/ModeH/{}\.png'.format(EMBLEM_SHORT_NAME), "徽记完整 asset path"),
        (r'Assets/UI/ModeH/{}\.png'.format(BANNER_SHORT_NAME), "横幅完整 asset path"),
        (r'EmblemShortName = "{}"'.format(EMBLEM_SHORT_NAME), "徽记短名"),
        (r'BannerShortName = "{}"'.format(BANNER_SHORT_NAME), "横幅短名"),
        (r"MaxBundleBytes = 256L \* 1024L", "256 KiB 上限"),
        (r"BuildTarget\.StandaloneWindows64", "固定构建目标"),
        (r"ValidateDependencies", "空依赖断言"),
        (r"LoadAsset<Sprite>\(EmblemShortName\)", "实测徽记短名可取"),
        (r"LoadAsset<Sprite>\(BannerShortName\)", "实测横幅短名可取"),
        (r"RequireAssetPath", "断言两条完整 asset path"),
        (r"public static void BuildOnlyAndExit\(\)", "命令行入口"),
        (r"emblem\.texture\.width != EmblemSize", "徽记尺寸断言"),
        (r"banner\.texture\.width != BannerWidth", "横幅尺寸断言"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, source):
            errors.append("[Builder] 不满足: " + desc)

    if read_text(ART_MANIFEST) is None:
        errors.append("[Builder] 缺少 ArtSource/ModeH/asset-manifest.json")
    else:
        manifest = read_text(ART_MANIFEST)
        for field in ["promptSha256", "sourceSha256", "unityInputSha256", "approvedAtUtc"]:
            if field not in manifest:
                errors.append("[Builder] art manifest 缺少字段: " + field)
        # 只禁止把密钥当成**字段**写进去；正文里说明“不记录 token”是允许的
        for forbidden in ["apiKey", "apiSecret", "token", "authorization", "bearer"]:
            if re.search(r'"{}"\s*:'.format(forbidden), manifest, re.IGNORECASE):
                errors.append("[Builder] art manifest 不得记录密钥/token 字段: " + forbidden)


def check_bundle_present(pending):
    """bundle 是 local-only 制品，未落位时记为待资源门而不是实现缺陷。"""
    if not os.path.exists(BUNDLE):
        pending.append(
            "[PENDING-RESOURCE] 缺少 Assets/ui/{}（由兄弟 Unity 工程生成后复制；"
            "接线已就位，资源落位后本条自动转绿）".format(BUNDLE_NAME))
        return
    size = os.path.getsize(BUNDLE)
    if size <= 0:
        pending.append("[PENDING-RESOURCE] bundle 为空文件")
        return
    if size > MAX_BUNDLE_BYTES:
        pending.append("[PENDING-RESOURCE] bundle 超过 256 KiB: {}".format(size))
        return
    with open(BUNDLE, "rb") as fh:
        header = fh.read(7)
    if header != b"UnityFS":
        pending.append("[PENDING-RESOURCE] bundle 不是 UnityFS 格式")


def main():
    errors = []
    pending = []
    check_cache(errors)
    check_dev_gate(errors)
    check_bats(errors)
    source_only = os.environ.get("BOSSRUSH_GUARD_SOURCE_ONLY") == "1"
    if not source_only:
        check_builder(errors)
        check_bundle_present(pending)

    if errors:
        print("ModeHPresentationAssetGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        for p in pending:
            print("  - " + p)
        return 1

    if pending:
        print("ModeHPresentationAssetGuard: FAIL ({} errors)".format(len(pending)))
        for p in pending:
            print("  - " + p)
        return 1

    if source_only:
        print("ModeHPresentationAssetGuard: PARTIAL (源码已验证；兄弟 Unity 工程构建器及 Mode H bundle 未验证)")
        return 2
    print("ModeHPresentationAssetGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
