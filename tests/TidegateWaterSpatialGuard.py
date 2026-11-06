"""潮闸镇真实空间水域、首帧水位与灯塔晚到敌人的接线守卫；行为由 TidegateWater / TidegateDrillJudges 执行回归覆盖。"""
import argparse
from pathlib import Path
import re
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from cs_source_util import clean_source

ROOT = HERE.parent
FILES = ("Tidegate/TidegateWater.cs", "Tidegate/TidegateWorld.cs", "Tidegate/TidegateWaterRegions.cs", "Tidegate/TidegateEnemies.cs")


def body(source, signature):
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 0
    for pos in range(brace, len(source)):
        if source[pos] == "{":
            depth += 1
        elif source[pos] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:pos]
    raise ValueError("unterminated " + signature)


def check(raw):
    water, world, regions, enemies = (clean_source(raw[path]) for path in FILES)
    errors = []
    tick = body(water, "internal void Tick()")
    for field, value, group in (("appliedSea", "sea", "seaVolume"), ("appliedLagoon", "lagoon", "lagoonVolume")):
        gate = r"if\s*\(float\.IsNaN\(" + field + r"\)\s*\|\|\s*Math\.Abs\(" + value + r"\s*-\s*" + field + r"\)\s*>\s*0\.005f\)\s*\{\s*" + field + r"\s*=\s*" + value + r";\s*SetTop\(" + group + r",\s*" + value + r"\);\s*\}"
        if not re.search(gate, tick):
            errors.append("TidegateWater.cs: 水位首帧与升降必须显式处理 " + field + " 的 NaN 并写入对应水域")
    create = body(water, "internal TidegateWater(")
    if "world.CreateWaterRegions(out samples)" not in create or "boxes.Add(Box(region.Lagoon ? lagoonVolume : seaVolume, region));" not in create:
        errors.append("TidegateWater.cs: 水盒必须按真实遮罩分片，同一片只放进对应海 / 湖空间组")
    if "Tick();" not in create:
        errors.append("TidegateWater.cs: 水体创建当帧必须落实首次水位")
    box = body(water, "private static BoxCollider Box(")
    if "volume.gameObject.AddComponent<BoxCollider>()" not in box or "new GameObject" in box or "box.isTrigger = true;" not in box:
        errors.append("TidegateWater.cs: 分片盒必须共用水域对象并保留官方 Water 触发查询")
    if re.search(r"\.enabled\s*=\s*false", water):
        errors.append("TidegateWater.cs: 不能按主角位置关闭另一处的真实水域")
    bind = body(world, "private void BindHeightTexture()")
    if bind.count("heightTexture.GetPixels()") != 1 or "lagoonMask[i] = pixels[i].g;" not in bind:
        errors.append("TidegateWorld.cs: 遮罩必须在绑定时一次批量缓存 G 通道")
    if "TidegateWaterRegions.Sample(lagoonMask, maskWidth, maskHeight, u, v) > 0.5f" not in body(world, "internal bool InLagoon("):
        errors.append("TidegateWorld.cs: 深水判据与空间水域必须共用同一托管遮罩取样")
    if "TidegateWaterRegions.Build(lagoonMask, maskWidth, maskHeight" not in body(world, "internal List<TidegateWaterRegion> CreateWaterRegions("):
        errors.append("TidegateWorld.cs: 真实世界索引必须将缓存遮罩交给空间合并器")
    if not re.search(r"const\s+float\s+CellSize\s*=\s*0\.25f\s*;", regions):
        errors.append("TidegateWaterRegions.cs: 空间水域轮廓格必须保持 0.25 m")
    sample = body(regions, "internal static float Sample(")
    for axis, uv, count in (("x", "u", "width"), ("z", "v", "height")):
        gpu_clamp = (r"float\s+" + axis + r"\s*=\s*Math\.Max\(0f,\s*Math\.Min\(" + count
                     + r"\s*-\s*1,\s*" + uv + r"\s*\*\s*" + count + r"\s*-\s*0\.5f\)\)\s*;")
        if not re.search(gpu_clamp, sample):
            errors.append("TidegateWaterRegions.cs: " + axis + " 必须按可见水面 GPU 纹素中心偏移 -0.5 取样")
    build = body(regions, "internal static List<TidegateWaterRegion> Build(")
    for axis in ("X", "Z"):
        if "(min" + axis + " - 0.5f)" not in build or "(max" + axis + " + 1.5f)" not in build:
            errors.append("TidegateWaterRegions.cs: " + axis + " 细分范围必须覆盖 GPU 过滤的纹素支撑区")
    if "GetPixelBilinear(" in regions or "GetPixelBilinear(" in world:
        errors.append("TidegateWorld.cs / TidegateWaterRegions.cs: 水域细分与每帧判定不能逐点跨原生 Texture 取样")
    spawn = body(enemies, "private async void Spawn(Slot slot)")
    character = spawn.find("slot.Character = created;")
    reveal = spawn.find("if (revealUntil > 0f) MarkOnMap(slot);")
    if character < 0 or reveal < character:
        errors.append("TidegateEnemies.cs: 灯塔期间新敌人必须先登记 slot.Character，再调用 MarkOnMap")
    return errors


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=ROOT)
    args = parser.parse_args()
    errors = check({path: (args.root / path).read_text(encoding="utf-8-sig") for path in FILES})
    print("TidegateWaterSpatialGuard: " + ("FAIL\n" + "\n".join(errors) if errors else "PASS"))
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
