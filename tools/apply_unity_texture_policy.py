# -*- coding: utf-8 -*-
"""把「贴图 Max Size / Crunch」策略写进 Unity 作者工程的 `.meta`。

为什么需要这个脚本：
  - 作者工程里的 `TextureImporter` 设置是**唯一**决定玩家最终看到什么的地方。
    仓库里的出图脚本只管生成 PNG，PNG 多大不代表进包多大。
  - 2026-09-19 人工实测反馈两类毛病：
      1. **看不清**——一批 1024 源图的 importer Max Size 被留在 64 / 128，
         进游戏被压成糊图（成就图标 36 张全是 64，霜之哀伤和焚皇的物品图标是 128）。
      2. **有噪点**——图标与新武器模型贴图开着 `crunchedCompression` 且
         `compressionQuality: 50`。Crunch 只压磁盘、不省显存，但 50 档的块状噪点
         在 UI 图标这种近距离看的图上非常明显。
    因此策略同时改两项：把 Max Size 拉回「够用且不超过上限」，并关掉 crunch。

策略口径（owner 2026-09-19 定）：
  - 物品 / 装备 / 图标：128–512，最多 512。
  - 立绘、横幅、海报一类展示图：最多 1024。
  - 环境 / 地形贴图不在本次范围内（见 `--list-untouched`），需要时另行决定。

用法（默认 dry-run，只打印将要改什么）：
    python tools/apply_unity_texture_policy.py --unity-project <作者工程绝对路径>
    python tools/apply_unity_texture_policy.py --unity-project <...> --apply

`--unity-project` 省略时读环境变量 `BOSSRUSH_UNITY_PROJECT`。脚本只改 `.meta`，
不碰 PNG，也不调用 Unity；改完必须在 Editor 里重新导入并重打相关 bundle 才会生效。
"""
from __future__ import annotations

import argparse
import os
import re
import sys

BS = chr(92)

#: (路径前缀或正则, Max Size, 是否关闭 crunch, 说明)。按顺序匹配，第一条命中即生效。
POLICY = [
    # ---------- 成就 / 建筑 / 物品 / UI 图标：近距离看，噪点最显眼 ----------
    (r"^AchievementIcons/", 256, True, "成就图标"),
    (r"^mymod/items/DingdangDrawing/", 1024, True, "叮当立绘"),
    (r"^mymod/items/", 256, True, "自定义物品图标"),
    (r"^mymod/[^/]+\.png$", 256, True, "自定义物品图标（旧位置）"),
    (r"^Items/", 512, True, "自定义物品图标"),
    (r"^Prefabs/", 256, True, "图腾 / buff 图标"),
    (r"^DragonKing/Prefabs/", 256, True, "龙皇特效贴图"),
    (r"^UI/BrokenHeart/", 256, True, "好感度心形图标"),
    (r"^UI/LoveHeart/", 256, True, "好感度心形图标"),
    (r"^UI/Skin/", 128, True, "共享 UI 九宫格底图（源图 ≤48px）"),
    (r"^BossRushWiki/Textures/Book_BG", 1024, True, "百科书页底图"),
    (r"^BossRushWiki/", 256, True, "百科按钮与分类图标"),
    # ---------- 立绘 / 横幅 / 海报：放宽到 1024 ----------
    (r"^UI/Codex/", 512, True, "图鉴立绘（源图 512）"),
    (r"^UI/Campaign/campaign_portrait_", 1024, True, "征程立绘"),
    (r"^UI/Campaign/campaign_poster_", 1024, True, "征程章节海报"),
    (r"_banner\.png$|_Banner\.png$", 1024, True, "模式横幅"),
    # ModeG 专用构建器冻结 256x256；确认页显示 84x84，保留约三倍采样。
    (r"^UI/ModeG/modeg_echo_emblem\.png$", 256, True, "Mode G 徽记（构建契约 256）"),
    (r"_emblem\.png$|_Emblem\.png$", 512, True, "模式徽记（源图 512）"),
    # ---------- 随身装备 / 武器模型贴图：上限 512 ----------
    (r"^SkyIslandBossGear/Models/", 512, True, "天空岛头目装备 albedo"),
    (r"^MeshyImports/new/", 512, True, "五把新武器与冰雷套装模型贴图"),
    (r"^MeshyImports/Frostmourne/", 512, True, "霜之哀伤模型贴图"),
    (r"^MeshyImports/FenHuang/", 512, True, "焚皇断界戟模型贴图"),
]

#: 明确不在本次范围内的目录（环境 / 地形 / NPC 模型 / 场景小地图）。
UNTOUCHED = (
    "SkyIsland/Textures/",
    "SkyIsland/Minimap/",
    "SkyIsland/Models/",
    "TextMesh Pro/",
)


def iter_texture_metas(assets_root):
    for dirpath, _dirnames, filenames in os.walk(assets_root):
        for name in filenames:
            if not name.endswith(".meta"):
                continue
            base = name[:-5]
            if not base.lower().endswith((".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif")):
                continue
            meta_path = os.path.join(dirpath, name)
            try:
                with open(meta_path, encoding="utf-8", newline="") as handle:
                    text = handle.read()
            except OSError:
                continue
            if "TextureImporter" not in text:
                continue
            rel = os.path.relpath(os.path.join(dirpath, base), assets_root).replace(BS, "/")
            yield rel, meta_path, text


def match_policy(rel):
    for pattern, size, kill_crunch, label in POLICY:
        if re.search(pattern, rel):
            return size, kill_crunch, label
    return None


def _rewrite_entry(chunk, target_size, kill_crunch, label, changes):
    """只改一个 platformSettings 条目；条目文本原样返回时表示没有改动。"""
    def _size(match):
        current = int(match.group(2))
        if current == target_size:
            return match.group(0)
        changes.append("%s maxTextureSize %d -> %d" % (label, current, target_size))
        return match.group(1) + str(target_size) + match.group(3)

    chunk = re.sub(r"(?m)^(    maxTextureSize:[ \t]*)(\d+)([ \t]*)(?=\r?$)", _size, chunk, count=1)

    if kill_crunch:
        def _crunch(match):
            if match.group(2) == "0":
                return match.group(0)
            changes.append("%s crunchedCompression on -> off" % label)
            return match.group(1) + "0" + match.group(3)

        chunk = re.sub(r"(?m)^(    crunchedCompression:[ \t]*)(\d+)([ \t]*)(?=\r?$)", _crunch, chunk, count=1)
    return chunk


def rewrite(text, target_size, kill_crunch):
    """返回 (新文本, 改动说明列表)。

    只动三处：顶层 `maxTextureSize`、`DefaultTexturePlatform` 条目，以及
    `overridden: 1` 的平台条目。`overridden: 0` 的平台条目在构建时被忽略，
    改它只会让 diff 变大、看不出真正生效的改动，所以不碰。
    """
    changes = []
    head, sep, tail = text.partition("platformSettings:")
    if not sep:
        return text, changes

    # 顶层 legacy 字段在 Unity 2022.3 中未必随平台 API 更新；与实际平台设置一起保持策略一致。
    def _head_sub(match):
        current = int(match.group(2))
        if current == target_size:
            return match.group(0)
        changes.append("top maxTextureSize %d -> %d" % (current, target_size))
        return match.group(1) + str(target_size) + match.group(3)

    head = re.sub(r"(?m)^(  maxTextureSize:[ \t]*)(\d+)([ \t]*)(?=\r?$)", _head_sub, head, count=1)

    body, sep2, rest = tail.partition("spriteSheet:")
    if not sep2:
        body, sep2, rest = tail, "", ""

    pieces = body.split("- serializedVersion:")
    for index in range(1, len(pieces)):
        chunk = pieces[index]
        target_match = re.search(r"buildTarget:\s*(\S+)", chunk)
        overridden = re.search(r"overridden:\s*(\d+)", chunk)
        name = target_match.group(1) if target_match else "?"
        is_default = name == "DefaultTexturePlatform"
        is_override = overridden is not None and overridden.group(1) == "1"
        if not is_default and not is_override:
            continue
        pieces[index] = _rewrite_entry(chunk, target_size, kill_crunch, name, changes)
    body = "- serializedVersion:".join(pieces)

    return head + "platformSettings:" + body + (sep2 + rest if sep2 else ""), changes


def main(argv=None):
    parser = argparse.ArgumentParser(description="按策略批量修正 Unity 贴图导入设置")
    parser.add_argument("--unity-project", default=os.environ.get("BOSSRUSH_UNITY_PROJECT"),
                        help="Unity 作者工程根目录（含 Assets/）")
    parser.add_argument("--apply", action="store_true", help="真正写盘；缺省只打印")
    parser.add_argument("--list-untouched", action="store_true",
                        help="列出本策略明确不动、但 Max Size 超过 512 的贴图")
    args = parser.parse_args(argv)

    if not args.unity_project:
        print("需要 --unity-project 或环境变量 BOSSRUSH_UNITY_PROJECT", file=sys.stderr)
        return 2
    assets = os.path.join(args.unity_project, "Assets")
    if not os.path.isdir(assets):
        print("找不到 Assets 目录: " + assets, file=sys.stderr)
        return 2

    touched = 0
    scanned = 0
    untouched_big = []
    for rel, meta_path, text in iter_texture_metas(assets):
        scanned += 1
        policy = match_policy(rel)
        if policy is None:
            if rel.startswith(UNTOUCHED):
                match = re.search(r"(?m)^    maxTextureSize:\s*(\d+)\s*$", text)
                if match and int(match.group(1)) > 512:
                    untouched_big.append((rel, int(match.group(1))))
            continue
        target_size, kill_crunch, label = policy
        new_text, changes = rewrite(text, target_size, kill_crunch)
        if not changes:
            continue
        touched += 1
        print("%-64s %-22s %s" % (rel, label, "; ".join(sorted(set(changes)))))
        if args.apply:
            with open(meta_path, "w", encoding="utf-8", newline="") as handle:
                handle.write(new_text)

    print("\n扫描 %d 张，需改动 %d 张%s" % (scanned, touched, "（已写盘）" if args.apply else "（dry-run）"))
    if args.list_untouched:
        print("\n本策略未覆盖且 Max Size > 512（环境 / 地形，需 owner 另行决定）：")
        for rel, size in sorted(untouched_big):
            print("  %5d  %s" % (size, rel))
    if touched and not args.apply:
        print("加 --apply 才会写盘。写盘后必须在 Unity 里重新导入并重打相关 AssetBundle。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
