# -*- coding: utf-8 -*-
"""生图模型的单一事实来源。

换模型只改这一处。此前八个出图脚本各写各的 `"gpt-image-2"` 字面量，
换一次要改八个地方、漏一个就出一张画风对不上的图。

用法（脚本与本文件同在 tools/，`python tools/xxx.py` 时 sys.path[0] 就是 tools/）：

    from imagegen_model import IMAGE_MODEL
    subprocess.run([sys.executable, IMAGEGEN, "generate", "--model", IMAGE_MODEL, ...])

做 A/B 时用环境变量临时覆盖，**不要改这里再改回去**：

    BOSSRUSH_IMAGE_MODEL=gpt-image-2 python tools/gen_sky_island_ui_art.py
"""
import os

# 2026-09-13 起：gpt-image-2.5-flare（OpenAI 2026-09-08 发布 GPT Images 2.5）。
#
# 【为什么是 flare 不是 sunburst】官方把 flare 定位成「大多数应用的默认」——同等质量、
# 延迟比 gpt-image-2 低约一半；sunburst 是给「需要跨多轮精确编辑的高端视觉工作流」的，
# 出图更慢。本仓库是批量出素材（一次 13–19 张），flare 的吞吐更重要。
#
# 【实测，2026-09-13，网关 colorflowai.com】
#   - 网关**支持** gpt-image-2.5-flare；同一条提示词 **33 秒**出一张，
#     而 gpt-image-2 约 100 秒（与官方「延迟降低约 50%」的说法方向一致，实测更快）。
#   - **返回尺寸不受 `--size` 控制**：请求 1024x1024 实回 1536x1024
#     （gpt-image-2 在这个网关上是请求 1024 实回 1254²，同一类毛病）。
#     所以调用方必须自己 normalize/裁剪，不能假设拿到的就是请求的尺寸。
#   - 本地 `image_gen.py` 对**非** `gpt-image-2` 的模型只放行 1024x1024 / 1536x1024 /
#     1024x1536 / auto 四种 size（`_validate_size` 的 legacy 分支），gpt-image-2 那套
#     「16 的倍数、≤3840、比例 ≤3:1」的宽松校验走不到。要更自由的尺寸得先改那个脚本。
DEFAULT_MODEL = "gpt-image-2.5-flare"

#: 精修档。需要跨多轮编辑、或对文字渲染/复杂版面有要求时才用，出图更慢。
PRECISION_MODEL = "gpt-image-2.5-sunburst"

#: 上一代。**已有素材是用它出的**——重出旧图时用它才对得上画风，见 docs/AI生图API和密钥.md。
LEGACY_MODEL = "gpt-image-2"

IMAGE_MODEL = os.environ.get("BOSSRUSH_IMAGE_MODEL", DEFAULT_MODEL)
