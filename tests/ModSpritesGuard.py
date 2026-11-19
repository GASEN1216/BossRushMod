"""ModSpritesGuard：按路径取图（Sprite / Texture）只有一个入口 Integration/ModSprites.cs。

不变式（规则写在 ModSprites.cs 文件头）：
1. 生产代码里只有 ModSprites.cs 解码图片：`LoadImage(` 只出现在这里；
   其它文件读字节（ReadAllBytes）时不得同时引用图片扩展名（.png / .jpg / .jpeg / .tga / .bmp）。
2. 不得手抄「ProductionIconCache.Get → AllowRawFallback → 解码散图」三段式：
   ProductionIconCache 的 Get / Contains / AllowRawFallback 只有 ModSprites 调；
   专用 bundle 缓存（Mode G / Mode H / 图鉴 / 战役展示包 / 物品包）按资源名 LoadAsset，不经 ProductionIconCache，不在此列。
3. 旧入口 RawImageLoader、ItemFactory.GetSpriteFromFile 不再出现（生产源码原文与夹具源码），
   RawImageLoader.cs 不在编译清单、不在磁盘；IsBorrowed 不再出现（借用关系由 ModSprites 自己记）。
4. ModSprites.Get 的顺序：自身缓存 → ProductionIconCache.Get → AllowRawFallback 为 false 即返回 null → 解码；
   解码 RGBA32 + markNonReadable + DontSave + Clamp；Exists 只读（不解码、不 LoadAsset、不写缓存）；
   GetUnpacked 只给潮闸镇信件插画用。
5. 释放顺序：ItemFactory.Shutdown 调 ModSprites.ResetStaticCaches，
   宿主清理里 ItemFactory.ResetStaticCaches 先于 ProductionIconCache.ResetStaticCaches。

文本守卫防不住「保留 token、杀掉执行路径」；ModSprites 的行为由 AchievementIcons 执行回归覆盖。
"""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.path.insert(0, str(ROOT / "tests"))

from compile_list import parse_compile_sources  # noqa: E402
from cs_source_util import clean_source  # noqa: E402

ENTRY = "Integration/ModSprites.cs"
CACHE = "Integration/ProductionIconCache.cs"
ITEM_FACTORY = "Integration/ItemFactory.cs"
HOST_CLEANUP = "Integration/BossRushIntegration_StartAndScene.cs"
UNPACKED_CALLERS = {"Integration/Tidegate/TidegateLetterUsage.cs"}

IMAGE_EXTENSION = re.compile(r"\.(?:png|jpe?g|tga|bmp)\"", re.IGNORECASE)
LEGACY_TOKENS = ("RawImageLoader", "GetSpriteFromFile", "IsBorrowed")
CACHE_MEMBERS = re.compile(r"\bProductionIconCache\s*\.\s*(Get|Contains|AllowRawFallback)\b")


def compact(text):
    return re.sub(r"\s+", "", text)


def validate(root=ROOT):
    errors = []

    def raw(path):
        return (root / path).read_text(encoding="utf-8-sig")

    def body(text, signature, label):
        if text.count(signature) != 1:
            errors.append(label + ": 方法签名不唯一或缺失 " + signature)
            return ""
        start = text.index("{", text.index(signature)) + 1
        depth, end = 1, start
        while depth and end < len(text):
            depth += {"{": 1, "}": -1}.get(text[end], 0)
            end += 1
        return text[start:end - 1]

    bat = (root / "compile_official.bat").read_text(encoding="utf-8", errors="replace")
    sources = [s for s in parse_compile_sources(bat) if (root / s).exists()]
    if ENTRY not in sources:
        errors.append(ENTRY + " 没有登记进 compile_official.bat")
    if any(s.endswith("RawImageLoader.cs") for s in parse_compile_sources(bat)) or (root / "Integration/RawImageLoader.cs").exists():
        errors.append("Integration/RawImageLoader.cs 已并入 ModSprites，不得留在编译清单或磁盘上")

    # ---------- 1~3：全量生产源码 ----------
    for path in sources:
        text = raw(path)
        code = clean_source(text)
        for token in LEGACY_TOKENS:
            if token in text:
                errors.append(path + ": 不得再出现旧入口 " + token + "（含注释），按路径取图改调 ModSprites")
        if path == ENTRY:
            continue
        if "LoadImage(" in code:
            errors.append(path + ": 只有 ModSprites 可以 LoadImage 解码图片，改调 ModSprites.Get(...)（要 Texture2D 取 .texture）")
        if "ReadAllBytes(" in code and IMAGE_EXTENSION.search(code):
            errors.append(path + ": 读图片字节只能在 ModSprites 里，改调 ModSprites.Get(...)")
        if path != CACHE:
            for m in CACHE_MEMBERS.finditer(code):
                errors.append(path + ": 不得手抄 ProductionIconCache." + m.group(1) + " 取图三段式，改调 ModSprites")
        if "ModSprites.GetUnpacked(" in code and path not in UNPACKED_CALLERS:
            errors.append(path + ": GetUnpacked 只给不进压缩包的潮闸镇信件插画用；其它图登记 production_icons 后走 ModSprites.Get")

    if not any("ModSprites.GetUnpacked(" in clean_source(raw(p)) for p in UNPACKED_CALLERS if (root / p).exists()):
        errors.append("潮闸镇信件插画必须经 ModSprites.GetUnpacked 读散图（正式构建也要读）")

    fixtures = root / "tests" / "fixtures"
    for path in sorted(fixtures.rglob("*")):
        if not path.is_file() or path.suffix not in (".cs", ".py") or {"bin", "obj"} & set(path.parts):
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        for token in LEGACY_TOKENS:
            if token in text:
                errors.append(path.relative_to(root).as_posix() + ": 夹具仍引用已删除的 " + token)

    # ---------- 4：入口自身 ----------
    entry = clean_source(raw(ENTRY))
    load = compact(body(entry, "private static Sprite Load(string relativePath, bool packed)", ENTRY))
    order = ("if(sprites.TryGetValue(key,outcached)&&cached!=null)returncached;",
             "sprite=ProductionIconCache.Get(key);",
             "if(sprite==null&&!ProductionIconCache.AllowRawFallback)returnnull;",
             "if(sprite==null)sprite=Decode(key);",
             "if(sprite!=null)sprites[key]=sprite;")
    positions = [load.find(token) for token in order]
    if -1 in positions or positions != sorted(positions):
        errors.append("ModSprites.Load 必须是 缓存 → 压缩包 → 不许散图即返回 → 解码 → 记缓存：%r" % positions)
    if compact(body(entry, "internal static Sprite Get(string relativePath)", ENTRY)) != "returnLoad(relativePath,true);":
        errors.append("ModSprites.Get 必须走压缩包优先的 Load(relativePath, true)")
    if compact(body(entry, "internal static Sprite GetUnpacked(string relativePath)", ENTRY)) != "returnLoad(relativePath,false);":
        errors.append("ModSprites.GetUnpacked 必须是 Load(relativePath, false)")
    decode = compact(body(entry, "private static Sprite Decode(string key)", ENTRY))
    for token in ("newTexture2D(2,2,TextureFormat.RGBA32,false)", "if(!texture.LoadImage(bytes,true))returnnull;",
                  "texture.hideFlags=HideFlags.DontSave;", "texture.wrapMode=TextureWrapMode.Clamp;",
                  "sprite.hideFlags=HideFlags.DontSave;", "ownedTextures.Add(texture);", "ownedSprites.Add(sprite);",
                  "retained=true;", "if(!retained)"):
        if token not in decode:
            errors.append("ModSprites.Decode 缺少 " + token)
    exists = compact(body(entry, "internal static bool Exists(string relativePath)", ENTRY))
    if "ProductionIconCache.Contains(key)" not in exists:
        errors.append("ModSprites.Exists 必须只读探测压缩包目录（ProductionIconCache.Contains）")
    for token in ("Decode(", "Load(", "LoadImage", "LoadAsset", "ProductionIconCache.Get(", "sprites["):
        if token in exists:
            errors.append("ModSprites.Exists 不得解码、加载资源或写缓存：" + token)
    contains = compact(body(clean_source(raw(CACHE)), "internal static bool Contains(string relativePath)", CACHE))
    if "bundle.Contains(key)" not in contains or "LoadAsset" in contains or "sprites[" in contains:
        errors.append("ProductionIconCache.Contains 只能查包目录，不加载资源、不写缓存")
    reset = compact(body(entry, "internal static void ResetStaticCaches()", ENTRY))
    for token in ("sprites.Clear();", "UnityEngine.Object.Destroy(sprite);", "UnityEngine.Object.Destroy(texture);"):
        if token not in reset:
            errors.append("ModSprites.ResetStaticCaches 缺少 " + token)

    # ---------- 5：释放顺序 ----------
    shutdown = compact(body(clean_source(raw(ITEM_FACTORY)), "public static void Shutdown()", ITEM_FACTORY))
    if "ModSprites.ResetStaticCaches();" not in shutdown:
        errors.append("ItemFactory.Shutdown 必须调 ModSprites.ResetStaticCaches()")
    host = compact(clean_source(raw(HOST_CLEANUP)))
    a, b = host.find("ItemFactory.ResetStaticCaches();"), host.find("ProductionIconCache.ResetStaticCaches();")
    if a < 0 or b < 0 or a > b:
        errors.append("宿主清理必须先 ItemFactory.ResetStaticCaches（放下对压缩图的借用）再 ProductionIconCache.ResetStaticCaches")
    return errors


def main():
    errors = validate()
    if errors:
        print("ModSpritesGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("ModSpritesGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
