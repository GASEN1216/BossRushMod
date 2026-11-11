#!/usr/bin/env python3
"""校验末班编组站资源包里的自研着色器真的带编译产物（口径同 verify_tidegate_bundle_shaders.py）。

作者工程没指定 URP 资产时，URP 会把 `RenderPipeline=UniversalPipeline` 的变体全部剥掉：包能构建、能读回场景，
玩家一进图 `Shader.isSupported == false`。编译和守卫都证明不了这件事，重打包之后、部署之前必须跑一次。

用法：
    python tools/verify_lastshift_bundle_shaders.py              # 查仓库副本 Assets/arenas/lastshift_raid 与 lastshift_entry
    python tools/verify_lastshift_bundle_shaders.py <包路径>...

全图只有一个不透明环境着色器（含站场地坪与站外地面），必须带 UniversalGBuffer（鸭科夫是 URP Deferred）；仓库区剪票处小包 lastshift_entry 同样只用它。
退出码：0 = 全部可用；1 = 有着色器被剥光、缺 pass、混进未登记的着色器或包读不出来。
"""

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
from verify_sky_island_bundle_shaders import inspect  # noqa: E402

BUNDLE_RELATIVE = "Assets/arenas/lastshift_raid"
ENTRY_RELATIVE = "Assets/arenas/lastshift_entry"
# 运行时接入（阶段 6）的着色器白名单必须与这里一致。
REQUIRED = {
    "BossRush/LastShift/Environment": "UniversalGBuffer",
}


def check(path):
    print("== " + str(path))
    if not Path(path).exists():
        print("   MISSING")
        return False
    try:
        passes, materials, lightmodes = inspect(path)
    except Exception as error:  # UnityPy 版本/包格式问题也算失败
        print("   READ FAILED: " + str(error))
        return False
    ok = True
    for name, mode in REQUIRED.items():
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
    unknown = sorted(n for n in materials if n not in REQUIRED and not n.startswith("Hidden/"))
    if unknown:
        print("   UNREGISTERED SHADERS " + ", ".join(unknown) + "（运行时会拒绝）")
        ok = False
    return ok


def main():
    targets = sys.argv[1:] or [ROOT / BUNDLE_RELATIVE, ROOT / ENTRY_RELATIVE]
    results = [check(target) for target in targets]
    if all(results):
        print("LastShiftBundleShaders: PASS")
        return 0
    print("LastShiftBundleShaders: FAIL")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
