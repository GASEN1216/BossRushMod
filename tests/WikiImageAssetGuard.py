# -*- coding: utf-8 -*-
"""WikiImageAssetGuard - 在线 Wiki 配图的清单、产物与引用三者必须对得上。

背景：
    在线 Wiki 的配图走「离线转换 + 产物进仓库」这条路（见 tools/build_wiki_images.py）：
    源图在 /Assets/* 下被 .gitignore 挡着，GitHub Actions 构建时根本看不到，
    所以 wiki-site/docs/public/images/ 下的 WebP 与 wiki-site/scripts/image-manifest.json
    都是**提交进仓库的产物**。

    三者任何一处漂移都不会报错，只会在线上悄悄变成裂图或空白：
      - 清单里有、产物没有  -> 线上 404 裂图
      - 产物有、清单没有    -> 白占仓库体积，且没有任何页面会用到
      - WikiContent/illustrations.json 里引用的 key / group 在清单里不存在 -> 那一处配图静默消失
        （injectImages 只 console.warn，退化成不配图，同步照样 exit 0）

    最后一条是最阴的：改名/删图之后 sync 依旧绿，页面就是少了张图。

判据：
    1. 清单里每条 src 都能在 wiki-site/docs/public/ 下找到真实文件；
    2. public/images 下每个 .webp 都被清单收录（无孤儿产物）；
    3. sync-content.mjs 读取 WikiContent/illustrations.json，且后者引用的 key / group 与 webSrcEn 都在清单与产物中存在；
    4. 清单与 illustrations.json 均非空（被清空时应当红，而不是"零条也算通过"）。
    5. 游戏内冒险家日志用的 asset / assetEn 路径都登记在 tools/production_icon_manifest.json：
       正式构建按路径取图只认 production_icons 压缩包（包在时不读散图），漏登记的插图在游戏里静默不出现
       （WikiIllustrations.ResolveAssets 跳过取不到图的条目）。

    本 guard **不**校验图片内容或尺寸——那是 build_wiki_images.py 的职责。
"""
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MANIFEST = os.path.join(REPO_ROOT, "wiki-site", "scripts", "image-manifest.json")
PUBLIC_DIR = os.path.join(REPO_ROOT, "wiki-site", "docs", "public")
IMAGES_DIR = os.path.join(PUBLIC_DIR, "images")
SYNC_SCRIPT = os.path.join(REPO_ROOT, "wiki-site", "scripts", "sync-content.mjs")

PLACEMENT_RE = re.compile(r"['\"]WikiContent['\"]\s*,\s*['\"]illustrations\.json['\"]")
ILLUSTRATIONS = os.path.join(REPO_ROOT, "WikiContent", "illustrations.json")
ICON_MANIFEST = os.path.join(REPO_ROOT, "tools", "production_icon_manifest.json")
VALID_MODES = {"after-heading", "section-end"}
VALID_KINDS = {"gallery", "figure", "icon"}


def fail(message):
    print("WikiImageAssetGuard: FAIL - " + message)
    return 1


def main():
    if not os.path.isfile(MANIFEST):
        return fail("配图清单缺失：wiki-site/scripts/image-manifest.json"
                    "（跑 python tools/build_wiki_images.py 生成）")

    with open(MANIFEST, "r", encoding="utf-8") as handle:
        try:
            manifest = json.load(handle)
        except ValueError as exc:
            return fail("image-manifest.json 不是合法 JSON: " + str(exc))

    if not isinstance(manifest, dict):
        return fail("image-manifest.json 顶层应当是对象（分组名 -> 条目数组）")

    # ── 1. 清单 -> 产物 ────────────────────────────────────────────
    known_keys = set()
    referenced = set()
    entry_count = 0
    for group, items in manifest.items():
        if not isinstance(items, list):
            return fail("分组 " + group + " 的值不是数组")
        for item in items:
            entry_count += 1
            src = item.get("src", "")
            key = item.get("key", "")
            if not src or not key:
                return fail("分组 " + group + " 里有条目缺 src 或 key")
            known_keys.add(key)
            rel = src.lstrip("/")
            abs_path = os.path.join(PUBLIC_DIR, rel.replace("/", os.sep))
            if not os.path.isfile(abs_path):
                return fail("清单条目 " + key + " 指向的产物不存在: " + src
                            + "（跑 python tools/build_wiki_images.py 重建）")
            referenced.add(os.path.normcase(os.path.abspath(abs_path)))

    if entry_count == 0:
        return fail("配图清单是空的——若确实不再配图，请同时移除本 guard 与 IMAGE_PLACEMENT")

    # ── 2. 产物 -> 清单（孤儿检测）──────────────────────────────────
    orphans = []
    if os.path.isdir(IMAGES_DIR):
        for current, _dirs, files in os.walk(IMAGES_DIR):
            for name in files:
                if not name.endswith(".webp"):
                    continue
                full = os.path.normcase(os.path.abspath(os.path.join(current, name)))
                if full not in referenced:
                    orphans.append(os.path.relpath(full, PUBLIC_DIR).replace(os.sep, "/"))
    if orphans:
        return fail(str(len(orphans)) + " 个产物没有被清单收录（白占体积）: "
                    + ", ".join(sorted(orphans)[:6]))

    # ── 3. sync 脚本读取 WikiContent/illustrations.json，且引用全部对齐 ────
    if not os.path.isfile(SYNC_SCRIPT):
        return fail("sync-content.mjs 不存在，配图注入链已断")
    with open(SYNC_SCRIPT, "r", encoding="utf-8") as handle:
        sync_text = handle.read()
    if not PLACEMENT_RE.search(sync_text):
        return fail("sync-content.mjs 未读取 WikiContent/illustrations.json，配图注入链可能已断开")

    if not os.path.isfile(ILLUSTRATIONS):
        return fail("配图单一事实源缺失：WikiContent/illustrations.json")
    with open(ILLUSTRATIONS, "r", encoding="utf-8") as handle:
        try:
            illustrations = json.load(handle)
        except ValueError as exc:
            return fail("WikiContent/illustrations.json 不是合法 JSON: " + str(exc))

    ill_assets = illustrations.get("assets") if isinstance(illustrations, dict) else None
    ill_entries = illustrations.get("entries") if isinstance(illustrations, dict) else None
    if not isinstance(ill_assets, dict) or not ill_assets:
        return fail("WikiContent/illustrations.json 缺少非空 assets 对象")
    if not isinstance(ill_entries, dict) or not ill_entries:
        return fail("WikiContent/illustrations.json 缺少非空 entries 对象")

    missing_keys = []
    missing_assets = []
    missing_groups = []
    bad_specs = []

    for asset_key, asset_meta in ill_assets.items():
        if asset_key not in known_keys:
            missing_keys.append(asset_key)
        web_src_en = (asset_meta or {}).get("webSrcEn", "") if isinstance(asset_meta, dict) else ""
        if web_src_en:
            en_path = os.path.join(PUBLIC_DIR, web_src_en.lstrip("/").replace("/", os.sep))
            if not os.path.isfile(en_path):
                bad_specs.append(asset_key + "(webSrcEn缺失:" + web_src_en + ")")

    placement_count = 0
    for entry_id, specs in ill_entries.items():
        if not isinstance(specs, list) or not specs:
            bad_specs.append(entry_id + "(空列表)")
            continue
        for spec in specs:
            placement_count += 1
            mode = spec.get("mode", "") if isinstance(spec, dict) else ""
            kind = spec.get("kind", "") if isinstance(spec, dict) else ""
            if mode not in VALID_MODES or kind not in VALID_KINDS or not isinstance(spec.get("at"), int):
                bad_specs.append(entry_id + "(字段无效)")
                continue
            keys = spec.get("keys") or []
            group = spec.get("group") or ""
            if not keys and not group:
                bad_specs.append(entry_id + "(既无 keys 也无 group)")
                continue
            for token in keys:
                if token not in ill_assets:
                    missing_assets.append(token)
                if token not in known_keys:
                    missing_keys.append(token)
            if group and group not in manifest:
                missing_groups.append(group)

    if bad_specs or missing_assets or missing_keys or missing_groups:
        detail = []
        if bad_specs:
            detail.append("规格错误 " + ", ".join(sorted(set(bad_specs))[:6]))
        if missing_assets:
            detail.append("assets 未定义 " + ", ".join(sorted(set(missing_assets))))
        if missing_keys:
            detail.append("manifest 缺失 key " + ", ".join(sorted(set(missing_keys))))
        if missing_groups:
            detail.append("group " + ", ".join(sorted(set(missing_groups))))
        return fail("WikiContent/illustrations.json 与 manifest 未对齐：" + "；".join(detail))

    # ── 5. 游戏内插图都进了 production_icons 压缩包清单 ────────────────
    if not os.path.isfile(ICON_MANIFEST):
        return fail("生产图标包清单缺失：tools/production_icon_manifest.json")
    with open(ICON_MANIFEST, "r", encoding="utf-8") as handle:
        try:
            icon_manifest = json.load(handle)
        except ValueError as exc:
            return fail("production_icon_manifest.json 不是合法 JSON: " + str(exc))
    packed = set()
    for icon in icon_manifest.get("icons") or []:
        path = icon.get("path", "") if isinstance(icon, dict) else ""
        if path:
            packed.add(path.replace("\\", "/").lower())
    unpacked = []
    game_asset_count = 0
    for asset_key, asset_meta in ill_assets.items():
        if not isinstance(asset_meta, dict):
            continue
        for field in ("asset", "assetEn"):
            path = asset_meta.get(field) or ""
            if not path:
                continue
            game_asset_count += 1
            if path.replace("\\", "/").lower() not in packed:
                unpacked.append(asset_key + "." + field + "=" + path)
    if game_asset_count == 0:
        return fail("illustrations.json 没有任何游戏内 asset 路径——冒险家日志插图链已断")
    if unpacked:
        return fail(str(len(unpacked)) + " 张游戏内插图没登记进 production_icons（正式构建取不到图）: "
                    + ", ".join(sorted(unpacked)[:6]))

    print("WikiImageAssetGuard: PASS - " + str(entry_count) + " 条产物、"
          + str(len(manifest)) + " 个分组、"
          + str(len(ill_entries)) + " 篇共 " + str(placement_count) + " 处配图全部对齐")
    return 0


if __name__ == "__main__":
    sys.exit(main())
