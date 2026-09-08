#!/usr/bin/env python3
"""把 ArtSource 的预览 PNG 转成 WebP，保持像素尺寸不变。

为什么需要它
------------
`ArtSource/SkyIsland/Previews/` 是 Unity 作者渲染的实拍预览，17 张 PNG 合计约 38 MB，
占 `ArtSource/` 体积的绝大部分。这些图只用于人工目检和文档插图，不进游戏、不参与打包，
无损 PNG 的体积代价没有对应收益。WebP q92 在同尺寸下体积约为 1/7，截图类内容属视觉无损级别。

规则
----
- **不降分辨率**：只换编码，像素尺寸逐张保持原样。
- **只处理预览**：`ThirdParty/` 下的第三方素材（Kenney CC0 原包及其 Preview.png）原样保留，
  不重编码第三方发布物。
- **可重复运行**：作者工程重新导出 PNG 后再跑一次即可；已存在的同名 WebP 会被覆盖。
- 转换后删除源 PNG，并打印逐张体积对照，便于把数字写进 FIX_TRACKER。

用法
----
    python tools/compress_artsource_previews.py            # 实际转换
    python tools/compress_artsource_previews.py --dry-run  # 只报体积，不改文件

依赖本机 Pillow。缺依赖时明确失败，不静默跳过。
"""
import argparse
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "ArtSource")
SKIP_DIRS = {"ThirdParty"}
QUALITY = 92


def collect():
    found = []
    for base, dirs, files in os.walk(ART):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in sorted(files):
            if name.lower().endswith(".png"):
                found.append(os.path.join(base, name))
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="只报告体积，不写文件")
    args = parser.parse_args()

    try:
        from PIL import Image
    except ImportError:
        sys.exit("需要 Pillow：pip install Pillow")

    if not os.path.isdir(ART):
        sys.exit("找不到 ArtSource 目录：" + ART)

    sources = collect()
    if not sources:
        print("没有需要转换的 PNG。")
        return 0

    before = after = 0
    for path in sources:
        target = os.path.splitext(path)[0] + ".webp"
        image = Image.open(path)
        size = image.size
        # 带 alpha 的图保留 alpha；其余按 RGB 编码，避免无谓的第四通道。
        encoded = image if image.mode in ("RGBA", "LA") else image.convert("RGB")
        original = os.path.getsize(path)
        if args.dry_run:
            import io as _io
            buffer = _io.BytesIO()
            encoded.save(buffer, "WEBP", quality=QUALITY, method=6)
            produced = buffer.tell()
        else:
            encoded.save(target, "WEBP", quality=QUALITY, method=6)
            produced = os.path.getsize(target)
            image.close()
            os.remove(path)
        before += original
        after += produced
        print("%-56s %5dx%-5d %8.2f MB -> %6.2f MB" % (
            os.path.relpath(path, ROOT).replace("\\", "/"), size[0], size[1],
            original / 1048576.0, produced / 1048576.0))

    print("-" * 96)
    print("%-56s %13s %8.2f MB -> %6.2f MB  (%.1fx)" % (
        "TOTAL (%d files)" % len(sources), "", before / 1048576.0, after / 1048576.0,
        before / float(after) if after else 0))
    if args.dry_run:
        print("dry-run：未写入任何文件。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
