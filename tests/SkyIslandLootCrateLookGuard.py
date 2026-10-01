# -*- coding: utf-8 -*-
u"""天空岛搜刮箱的三种外观（owner 2026-09-30 方案 A：复用岛上 Tripo 木箱，网格打进 skyisland_fx）。

钉住：
1. 搜刮点建好箱子、装完物资后换外观；交互、库存、鉴定仍是官方 InteractableLootbox（SkyIslandRewardCrate.Build）。
2. 材质按名字从地图根下取场景已加载的那份（三个名字与作者工程材质文件一致），实例字段、不做静态缓存；
   资源缺一样就返回 false、保留官方包，不抛。
3. 只藏官方包的渲染器，不碰交互气泡；不加实体碰撞（落点只验过中心胶囊净空）；箱子搜空后不跟着消失。
4. 网格导出脚本：三件名字与作者构建器一致，最长边 ≤ 1.3 m、高 ≤ 1.2 m，带绕序自检。

特效小包的路径、包内容与着色器由 SkyIslandFxBundleGuard 管。反向检查在内存里恢复错误写法，确认每条都抓得住。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(ROOT / "tools"))
from cs_source_util import clean_source  # noqa: E402

LOOK = ROOT / "SkyIsland/SkyIslandLootCrateLook.cs"
SCAV = ROOT / "SkyIsland/SkyIslandScavenging.cs"
EXPORT = ROOT / "tools/sky_island_loot_crates.py"
NAMES = ("Sky_TripoCrateBarrel", "Sky_TripoCoverCrates", "Sky_TripoCrystalCluster")


def squash(text):
    return re.sub(r"\s+", "", text or "")


def check(src):
    errors = []
    look, scav, export = src["look"], src["scav"], src["export"]
    build = squash(scav.split("private void Build(Point point)", 1)[-1].split("private void OnStartLoot(", 1)[0])
    fill = build.find(squash("SkyIslandRewardCrate.FillScavenge(box, point.Anchor, raidSeed"))
    apply = build.find(squash("crateLook.Apply(box, point.Anchor.Tier, point.Anchor.Bearing);"))
    if fill < 0 or apply < fill:
        errors.append("搜刮点必须在装完物资之后换外观")
    if squash("crateLook = new SkyIslandLootCrateLook(sceneRoot.transform);") not in squash(scav):
        errors.append("外观由搜刮 owner 按地图根建一个实例")
    for name, const in zip(NAMES, ("CrateBarrelMaterial", "CoverCratesMaterial", "CrystalMaterial")):
        if 'internal const string %s = "%s";' % (const, name) not in look:
            errors.append("场景材质名漂移：" + name)
    if re.search(r"static\s+(?:readonly\s+)?Material\b", look):
        errors.append("场景材质不得做静态缓存：会话结束后引用失效")
    if squash("if (mesh == null || materials == null || mesh.subMeshCount != materials.Length)") not in squash(look):
        errors.append("网格或材质缺失时必须保留官方包")
    if "GetComponentInParent<InteractMarker>() == null" not in look:
        errors.append("只藏官方包的渲染器，不能把交互气泡一起藏掉")
    if re.search(r"AddComponent<\w*Collider>", look):
        errors.append("外观不得加实体碰撞：搜刮点只按中心胶囊验过净空")
    if squash("if (box.hideIfEmpty == box.transform) box.hideIfEmpty = null;") not in squash(look):
        errors.append("箱子搜空后应留在原地")
    if squash("SkyIslandFxAssets.LootCrate(tier)") not in squash(look):
        errors.append("网格必须从特效小包取")
    for name in ("LootCrate_Supply", "LootCrate_Voyage", "LootCrate_Starworks"):
        if "'%s'" % name not in export:
            errors.append("导出脚本缺件：" + name)
    extent = re.search(r"MAX_EXTENT\s*=\s*([\d.]+)", export)
    height = re.search(r"MAX_HEIGHT\s*=\s*([\d.]+)", export)
    if not extent or float(extent.group(1)) > 1.3 or not height or float(height.group(1)) > 1.2:
        errors.append("箱子最长边 ≤ 1.3 m、高 ≤ 1.2 m")
    if "extend(reversed(corner))" not in export or "绕序与法线不一致" not in export:
        errors.append("导出脚本必须反转绕序并自检叉积与法线同向")
    return errors


def load():
    return {
        "look": clean_source(LOOK.read_text(encoding="utf-8-sig")),
        "scav": clean_source(SCAV.read_text(encoding="utf-8-sig")),
        "export": EXPORT.read_text(encoding="utf-8"),
    }


def author_materials():
    try:
        from unity_project_path import find_unity_project
        project = find_unity_project()
    except Exception:
        return None
    if not project:
        return None
    base = Path(project) / "Assets" / "SkyIsland" / "Materials"
    return [name for name in NAMES if not (base / (name + ".mat")).is_file()]


def main():
    src = load()
    errors = check(src)
    probes = [
        ("scav", "crateLook.Apply(box, point.Anchor.Tier, point.Anchor.Bearing);", ""),
        ("look", "GetComponentInParent<InteractMarker>() == null", "true"),
        ("look", 'internal const string CrystalMaterial = "Sky_TripoCrystalCluster";', 'internal const string CrystalMaterial = "Sky_CrystalTeal";'),
        ("look", "private Material crateBarrel, coverCrates, crystal;", "private static Material crateBarrel, coverCrates, crystal;"),
        ("look", "look.AddComponent<MeshFilter>().sharedMesh = mesh;", "look.AddComponent<MeshFilter>().sharedMesh = mesh; look.AddComponent<BoxCollider>();"),
        ("export", "extend(reversed(corner))", "extend(corner)"),
    ]
    for key, old, new in probes:
        if src[key].count(old) != 1:
            errors.append("反向探针锚点不唯一：" + old)
            continue
        broken = dict(src)
        broken[key] = src[key].replace(old, new)
        if not check(broken):
            errors.append("反向探针没转红：" + old)
    missing = author_materials()
    note = "作者工程不在，未核对材质文件" if missing is None else ""
    if missing:
        errors.append("作者工程缺场景材质：" + ", ".join(missing))
    if errors:
        for e in errors:
            print("FAIL " + e)
        return 1
    print("PASS SkyIslandLootCrateLookGuard: three crate looks, scene materials, fallback, no collider" + ("；" + note if note else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
