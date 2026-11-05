#!/usr/bin/env python3
"""校验潮闸镇资源包里的自研着色器真的带编译产物（口径同 verify_sky_island_bundle_shaders.py）。

作者工程没指定 URP 资产时，URP 会把 `RenderPipeline=UniversalPipeline` 的变体全部剥掉：包能构建、能读回场景，
玩家一进图 `Shader.isSupported == false`，`TidegateWorld.ApplyLayers` 当场失败。编译和守卫都证明不了这件事，
重打包之后、部署之前必须跑一次。

用法：
    python tools/verify_tidegate_bundle_shaders.py              # 查仓库副本 Assets/arenas/tidegate_raid 与 tidegate_entry
    python tools/verify_tidegate_bundle_shaders.py <包路径>...

不透明的环境 / 地形着色器必须带 UniversalGBuffer（鸭科夫是 URP Deferred）；水面、灯塔光束是透明物体，只要有 UniversalForward。
基地入口竹筏小包 tidegate_entry（按文件名认）只带环境着色器。
退出码：0 = 全部可用；1 = 有着色器被剥光、缺 pass 或包读不出来。
"""

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
from verify_sky_island_bundle_shaders import inspect  # noqa: E402

BUNDLE_RELATIVE = "Assets/arenas/tidegate_raid"
ENTRY_RELATIVE = "Assets/arenas/tidegate_entry"
# 运行时 `TidegateWorld.WorldShaders` 只认这四个名字，必须与生产代码一致。
REQUIRED = {
    "BossRush/Tidegate/Environment": "UniversalGBuffer",
    "BossRush/Tidegate/Terrain": "UniversalGBuffer",
    "BossRush/Tidegate/Water": "UniversalForward",
    "BossRush/Tidegate/Beam": "UniversalForward",  # 灯塔光束 Visual/VIS_Fx/LighthouseBeam：透明加色，只有前向 pass
}
# 竹筏 prefab 与马灯玻璃都用环境着色器。
ENTRY_REQUIRED = {
    "BossRush/Tidegate/Environment": "UniversalGBuffer",
}


def check(path):
    print("== " + str(path))
    required = ENTRY_REQUIRED if Path(path).name == "tidegate_entry" else REQUIRED
    if not Path(path).exists():
        print("   MISSING")
        return False
    try:
        passes, materials, lightmodes = inspect(path)
    except Exception as error:  # UnityPy 版本/包格式问题也算失败
        print("   READ FAILED: " + str(error))
        return False
    ok = True
    for name, mode in required.items():
        count = passes.get(name)
        used = materials.get(name, 0)
        tags = lightmodes.get(name) or set()
        if count is None:
            print("   MISSING SHADER " + name)
            ok = False
        elif count == 0:
            print("   STRIPPED  " + name + "（0 pass，材质 " + str(used) + " 个引用它 → 进图必失败）")
            ok = False
        elif mode not in tags:
            print("   NO-" + mode + " " + name + "（passes=" + str(count) + "）")
            ok = False
        else:
            print("   OK        " + name + "  passes=" + str(count) + " materials=" + str(used) + " lightModes=" + ",".join(sorted(tags)))
    unknown = sorted(n for n in materials if n not in required and not n.startswith("Hidden/"))
    if unknown:
        print("   UNREGISTERED SHADERS " + ", ".join(unknown) + "（运行时会拒绝）")
        ok = False
    return ok


def main():
    targets = sys.argv[1:] or [ROOT / BUNDLE_RELATIVE, ROOT / ENTRY_RELATIVE]
    results = [check(target) for target in targets]
    if all(results):
        print("TidegateBundleShaders: PASS")
        return 0
    print("TidegateBundleShaders: FAIL")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
