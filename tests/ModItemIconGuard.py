"""ModItemIconGuard：Mod 物品图标只有一个入口。

不变式（规则写在 Integration/ModItemIcons.cs 文件头，repowiki《物品图标统一入口》）：
1. 注册计划（BossRushDynamicItemRegistry.BuildPlans）里的每个物品号，图标名恰好声明一次：
   ModItemIcons.LegacyIconName 的 case、模块定义表（天空岛物品 / 头目装备、潮闸镇、末班编组站、后山）、
   收集品表（collectible_<key>）与收藏册；不在注册计划里的物品号不得声明图标。
2. 声明的图标都登记在 tools/production_icon_manifest.json；清单里的 Assets/Items 图标没有孤儿。
3. ItemAssetsCollection.AddDynamicEntry 只出现在 BossRushDynamicItemRegistry.Publish 里，且先套图标再注册。
4. 生产代码不再按路径读物品图标、不写 Item.Icon、不复制图标贴图、不保留旧注入入口；
   ModItemIcons 的 ForType 与 ApplyBeforePublish 都经 ModSprites.Get 取图（按路径取图的唯一入口见 ModSpritesGuard）。
5. 启动顺序：生产图标包准备 → RefreshPublishedIcons → 加载物品包。
"""

from pathlib import Path
import json
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
sys.path.insert(0, str(ROOT / "tests"))

from compile_list import parse_compile_sources  # noqa: E402
from cs_source_util import clean_source  # noqa: E402

ICONS = "Integration/ModItemIcons.cs"
REGISTRY = "Integration/BossRushDynamicItemRegistry.cs"
LOADING = "Integration/FactoryResourceLoading.cs"
MANIFEST = "tools/production_icon_manifest.json"

# 模块定义表：(文件, 区段起点, 区段终点)。区段里按顺序出现「物品号常量 … 图标名字面量」。
MODULE_TABLES = (
    ("Integration/SkyIsland/SkyIslandItems.cs", "private static Definition[] BuildDefinitions()", "private static Definition GetDefinition("),
    ("SkyIsland/SkyIslandBossRules.cs", "private static readonly SkyIslandBossGearSpec[] gearSpecs =", "internal static SkyIslandBossGearSpec[] GearSpecs"),
    ("Integration/Tidegate/TidegateItems.cs", "private static Definition[] BuildDefinitions()", "private static Definition GetDefinition("),
    ("Integration/LastShift/LastShiftItems.cs", "private static Definition[] Definitions", "private static Definition GetDefinition("),
    ("Integration/BackMountain/BackMountainItems.cs", "private static Definition[] BuildDefinitions()", "internal static Definition GetDefinition("),
)

# ModItemIcons.TryGetIconName 必须问到的每个来源。
PROVIDERS = ("LegacyIconName(typeId)", "SkyIslandItems.IconNameOf(typeId)", "SkyIslandBossGearIconName(typeId)",
             "TidegateItems.IconNameOf(typeId)", "LastShiftItems.IconNameOf(typeId)", "BackMountainIconName(typeId)",
             "CollectibleItems.IconNameOf(typeId)")

# `.Icon =` 写的是界面卡片 / 选项自己的字段，不是 Item.Icon（逐个核对过）。
UI_ICON_FIELD_FILES = {
    "ModeH/ModeHRuntimeModule_LoadoutEditing.cs",
    "PetNest/PetNestUIMuseumPage.cs",
    "PetNest/PetNestUINestPage.cs",
    "PetNest/PetNestUIPages.cs",
    "Integration/Reforge/ReforgeUIManager_AffixForgePanel.cs",
}

# 可以出现 "Assets/Items/" 字面量的文件：只有统一入口本身（ItemFactory.GetSprite 里恒 miss 的按路径兜底已删）。
ITEM_PATH_FILES = {ICONS}

FORBIDDEN_TOKENS = ("TryInjectIcon", "EquipmentHelperIcon", "ApplyBundledItemRedraw", "CloneTexture(")

TYPE_MIN, TYPE_MAX = 500001, 500999


def validate(root=ROOT):
    errors = []
    cache = {}

    def source(path):
        if path not in cache:
            cache[path] = clean_source((root / path).read_text(encoding="utf-8-sig"))
        return cache[path]

    def region(path, start, end):
        text = source(path)
        if text.count(start) != 1 or end not in text:
            errors.append(path + ": 找不到区段 " + start)
            return ""
        a = text.index(start)
        return text[a:text.index(end, a)]

    def body(path, signature):
        text = source(path)
        if text.count(signature) != 1:
            errors.append(path + ": 方法签名不唯一或缺失 " + signature)
            return ""
        start = text.index("{", text.index(signature)) + 1
        depth, end = 1, start
        while depth and end < len(text):
            depth += {"{": 1, "}": -1}.get(text[end], 0)
            end += 1
        return text[start:end - 1]

    sources = [s for s in parse_compile_sources((root / "compile_official.bat").read_text(encoding="utf-8", errors="replace"))
               if (root / s).exists()]

    # ---------- 常量解析：Class.NAME → int ----------
    raw_consts = {}
    for path in sources:
        text = source(path)
        classes = [(m.start(), m.group(1)) for m in re.finditer(r"\b(?:class|struct)\s+(\w+)", text)]
        for m in re.finditer(r"\bconst\s+int\s+(\w+)\s*=\s*([^;]+);", text):
            owner = None
            for pos, name in classes:
                if pos < m.start():
                    owner = name
            raw_consts[(owner or "", m.group(1))] = (m.group(2).strip(), owner or "")
        # 同一行声明多个常量：const int A = 1, B = 2;
        for m in re.finditer(r"\bconst\s+int\s+((?:\w+\s*=\s*[^,;]+,\s*)+\w+\s*=\s*[^;]+);", text):
            owner = None
            for pos, name in classes:
                if pos < m.start():
                    owner = name
            for part in re.finditer(r"(\w+)\s*=\s*([^,;]+)", m.group(1)):
                raw_consts[(owner or "", part.group(1))] = (part.group(2).strip(), owner or "")

    resolved = {}

    def resolve(owner, name, depth=0):
        key = (owner, name)
        if key in resolved:
            return resolved[key]
        if key not in raw_consts or depth > 8:
            return None
        expr, cls = raw_consts[key]
        value = None
        if re.fullmatch(r"\d+", expr):
            value = int(expr)
        else:
            m = re.fullmatch(r"(?:(\w+)\.)?(\w+)", expr)
            if m:
                value = resolve(m.group(1) or cls, m.group(2), depth + 1)
        resolved[key] = value
        return value

    def ref_value(token, context_class=""):
        m = re.fullmatch(r"(?:(\w+)\.)?(\w+)", token)
        if not m:
            return None
        return resolve(m.group(1) or context_class, m.group(2))

    def type_refs(text):
        for m in re.finditer(r"\b([A-Z]\w*)\.([A-Za-z_]\w*)\b", text):
            value = resolve(m.group(1), m.group(2))
            if value is not None and TYPE_MIN <= value <= TYPE_MAX:
                yield m.start(), value

    # ---------- 期望集合：注册计划 ----------
    # 计划里三种写法：常量引用、字面量物品号、静态只读 int 数组（营旗、头目装备）。
    arrays = {}
    for path in sources:
        text = source(path)
        for m in re.finditer(r"static\s+readonly\s+int\[\]\s+(\w+)\s*=\s*(?:new\s+int\[\]\s*)?\{([^}]*)\}", text):
            owner = None
            for c in re.finditer(r"\b(?:class|struct)\s+(\w+)", text[:m.start()]):
                owner = c.group(1)
            arrays[(owner or "", m.group(1))] = (m.group(2), owner or "")
    plans = body(REGISTRY, "private static Dictionary<int, RegistrationPlan> BuildPlans()")
    expected = {value for _, value in type_refs(plans)}
    expected |= {int(v) for v in re.findall(r"\b(5\d{5})\b", plans) if TYPE_MIN <= int(v) <= TYPE_MAX}
    for m in re.finditer(r"\b([A-Z]\w*)\.([A-Za-z_]\w*)\b", plans):
        entry = arrays.get((m.group(1), m.group(2)))
        if entry:
            items, cls = entry
            for token in re.findall(r"[\w.]+", items):
                value = int(token) if token.isdigit() else ref_value(token, cls)
                if value is not None and TYPE_MIN <= value <= TYPE_MAX:
                    expected.add(value)
    if "CollectibleTypeIds()" in plans:
        first = ref_value("BossRushItemIds.CollectibleFirst")
        last = ref_value("BossRushItemIds.CollectibleLast")
        if first is None or last is None:
            errors.append("无法解析收集品物品号范围")
        else:
            expected |= set(range(first, last + 1))
    if len(expected) < 200:
        errors.append("注册计划解析到的物品号过少（%d），解析可能失明" % len(expected))

    # ---------- 声明 ----------
    manifest = json.loads((root / MANIFEST).read_text(encoding="utf-8"))
    manifest_items = {row["path"][len("Assets/Items/"):-len(".png")] for row in manifest["icons"]
                      if row["path"].startswith("Assets/Items/") and row["path"].endswith(".png")}
    declared = {}

    def declare(type_id, icon, where):
        declared.setdefault(type_id, []).append((icon, where))

    legacy = body(ICONS, "private static string LegacyIconName(int typeId)")
    for m in re.finditer(r"case\s+([\w.]+)\s*:\s*return\s+\"([a-z0-9_]+)\"\s*;", legacy):
        value = ref_value(m.group(1))
        if value is None:
            errors.append("LegacyIconName 无法解析物品号 " + m.group(1))
            continue
        declare(value, m.group(2), "ModItemIcons.LegacyIconName")

    for path, start, end in MODULE_TABLES:
        text = region(path, start, end)
        events = [(pos, "id", value) for pos, value in type_refs(text)]
        events += [(m.start(), "icon", m.group(1)) for m in re.finditer(r"\"([a-z0-9_]+)\"", text) if m.group(1) in manifest_items]
        current = None
        for _, kind, value in sorted(events):
            if kind == "id":
                current = value
            elif current is None:
                errors.append(path + ": 图标名 " + value + " 前面没有物品号")
            else:
                declare(current, value, path)
                current = None

    catalog = source("Integration/Collectibles/CollectibleCatalog.cs")
    for m in re.finditer(r"new CollectibleDefinition\((\d+),\s*\"([a-z0-9_]+)\"", catalog):
        declare(int(m.group(1)), "collectible_" + m.group(2), "CollectibleCatalog")
    book = source("Integration/Collectibles/CollectionBookItem.cs")
    book_id = re.search(r"const int TypeId = ([\w.]+);", book)
    book_icon = re.search(r"const string IconName = \"([a-z0-9_]+)\";", book)
    if not book_id or not book_icon or ref_value(book_id.group(1)) is None:
        errors.append("收藏册的物品号或图标名解析失败")
    else:
        declare(ref_value(book_id.group(1)), book_icon.group(1), "CollectionBookItem")

    for type_id in sorted(expected):
        rows = declared.get(type_id, [])
        if not rows:
            errors.append("物品号 %d 没有声明图标" % type_id)
        elif len(rows) > 1:
            errors.append("物品号 %d 声明了 %d 次：%r" % (type_id, len(rows), rows))
    for type_id in sorted(set(declared) - expected):
        errors.append("物品号 %d 不在注册计划里却声明了图标：%r" % (type_id, declared[type_id]))
    used = set()
    for rows in declared.values():
        for icon, where in rows:
            used.add(icon)
            if icon not in manifest_items:
                errors.append(where + ": 图标 " + icon + " 没有登记 " + MANIFEST)
            elif (root / "Assets/Items").is_dir() and any((root / "Assets/Items").iterdir()) \
                    and not (root / "Assets/Items" / (icon + ".png")).is_file():
                errors.append(where + ": 图标文件缺失 Assets/Items/" + icon + ".png")
    for orphan in sorted(manifest_items - used):
        errors.append("图标清单里的 Assets/Items/" + orphan + ".png 没有物品使用")

    # ---------- 结构 ----------
    icons_src = source(ICONS)
    try_get = body(ICONS, "internal static bool TryGetIconName(int typeId, out string iconName)")
    for token in PROVIDERS:
        if token not in try_get:
            errors.append("ModItemIcons.TryGetIconName 缺少来源 " + token)
    for path in ("Integration/SkyIsland/SkyIslandItems.cs", "Integration/Tidegate/TidegateItems.cs", "Integration/LastShift/LastShiftItems.cs"):
        if "def.IconName" not in body(path, "internal static string IconNameOf(int typeId)"):
            errors.append(path + ": IconNameOf 必须交出定义表里的 IconName")
    collectible_of = body("Integration/Collectibles/CollectibleItems.cs", "internal static string IconNameOf(int typeId)")
    if "CollectionBookItem.IconName" not in collectible_of or "IconName(def)" not in collectible_of:
        errors.append("CollectibleItems.IconNameOf 必须覆盖收集品与收藏册")
    if 'return "collectible_" + def.Key;' not in source("Integration/Collectibles/CollectibleItems.cs").replace("\r", ""):
        errors.append("收集品图标名必须是 collectible_<key>")
    apply = body(ICONS, "internal static void ApplyBeforePublish(Item prefab)")
    if "prefab.Icon = sprite;" not in apply or "Sprite sprite = ModSprites.Get(IconPath(iconName));" not in apply:
        errors.append("ApplyBeforePublish 必须经 ModSprites.Get 取生产包图标并写 prefab.Icon")
    for_type = body(ICONS, "internal static Sprite ForType(int typeId)")
    if "ModSprites.Get(IconPath(iconName))" not in for_type:
        errors.append("ForType 必须经 ModSprites.Get 取图（与物品格里显示的是同一张）")

    publish = body(REGISTRY, "internal static bool Publish(Item prefab)")
    if "ModItemIcons.ApplyBeforePublish(prefab);" not in publish or "ItemAssetsCollection.AddDynamicEntry(prefab)" not in publish \
            or publish.index("ModItemIcons.ApplyBeforePublish(prefab);") > publish.index("ItemAssetsCollection.AddDynamicEntry(prefab)"):
        errors.append("Publish 必须先套图标、再 AddDynamicEntry")
    refresh = body(REGISTRY, "internal static int RefreshPublishedIcons()")
    for token in ("foreach (int typeId in Plans.Keys)", "GetRegisteredPrefabWithoutEnsuring(typeId)", "prefab.Icon != null", "Publish(prefab)"):
        if token not in refresh:
            errors.append("RefreshPublishedIcons 缺少 " + token)
    init = body(LOADING, "internal static IEnumerator InitializeItems(ModBehaviour owner, Action finish)")
    order = ("ProductionIconCache.Prepare(", "BossRushDynamicItemRegistry.RefreshPublishedIcons();", "ItemFactory.LoadAllItemsAsync(owner);")
    if not all(token in init for token in order) or not (init.index(order[0]) < init.index(order[1]) < init.index(order[2])):
        errors.append("启动顺序必须是 生产包准备 → RefreshPublishedIcons → 加载物品包")

    add_sites = []
    for path in sources:
        text = source(path)
        for token in FORBIDDEN_TOKENS:
            if token in text:
                errors.append(path + ": 不得再出现 " + token)
        count = text.count("ItemAssetsCollection.AddDynamicEntry(")
        if count:
            add_sites.append((path, count))
        if path != ICONS and path not in UI_ICON_FIELD_FILES and re.search(r"\.Icon\s*=(?!=)", text):
            errors.append(path + ": 物品图标只能由 ModItemIcons 写（界面字段请登记 UI_ICON_FIELD_FILES 并写明原因）")
        if path not in ITEM_PATH_FILES and "\"Assets/Items/" in text:
            errors.append(path + ": 不得按路径读物品图标，改调 ModItemIcons.ForType")
        if re.search(r"\bclass\s+\w+Icon\s*:\s*MonoBehaviour", text):
            errors.append(path + ": 不得重新引入物品专属的图标持有组件")
    if add_sites != [(REGISTRY, 1)]:
        errors.append("ItemAssetsCollection.AddDynamicEntry 只能出现在 Publish 里，实际：%r" % add_sites)
    if "LegacyIcons" in icons_src or "Dictionary<" in icons_src:
        errors.append("ModItemIcons 不得持有静态表（用 switch 声明，见文件头）")
    return errors


def main():
    errors = validate()
    if errors:
        print("ModItemIconGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("ModItemIconGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
