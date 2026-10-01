#!/usr/bin/env python3
"""校验天空岛场景包里的自研着色器真的带编译产物（CR-2026-09-10-004）。

**为什么需要它**：URP 的 `ShaderScriptableStripper` 在作者工程**没有指定 URP 资产**时，
会把 `RenderPipeline=UniversalPipeline` 的着色器变体**全部剥掉**（构建日志表现为
`After scriptable stripping: 0` 与 `d3d11 (total internal programs: 0, unique: 0)`）。
包照样能构建、能加载、能读回场景路径，作者侧一切"PASS"，但玩家一进岛就是
`Shader.isSupported == false`，`SkyIslandRendering.Apply` 当场抛错、整局作废。

编译、守卫、执行回归**都证明不了这件事**——它只存在于二进制包里。所以在重打包之后、
部署之前必须跑一次本脚本；结果记进 `ArtSource/SkyIsland/Validation/raid_deployment_hashes.json`。

用法：
    python tools/verify_sky_island_bundle_shaders.py            # 默认查仓库副本与游戏副本
    python tools/verify_sky_island_bundle_shaders.py <包路径>...

游戏副本的位置与 `compile_official.bat` 同一口径（CR-2026-09-30-011）：先认环境变量 `GAME_PATH`
（目录下要有 `Duckov_Data/Managed/Assembly-CSharp.dll`，否则警告并忽略），再按编译脚本
`:ensure_game_path` 里 `call :try_game_path` 的候选顺序探测；候选表直接从编译脚本读，不在这里另抄一份。
都找不到时游戏副本记 FAIL（不跳过当绿），设好 `GAME_PATH` 或显式传包路径再跑。

退出码：0 = 全部着色器可用；1 = 有着色器没有 pass，或包读不出来。
"""

from pathlib import Path
import os
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
BUNDLE_RELATIVE = "Assets/arenas/sky_island_raid"
# 部署目标与编译脚本一致：%GAME_PATH%\Duckov_Data\Mods\%MOD_NAME%（MOD_NAME=BossRush）。
MOD_RELATIVE = Path("Duckov_Data") / "Mods" / "BossRush"
MANAGED_MARKER = Path("Duckov_Data") / "Managed" / "Assembly-CSharp.dll"
COMPILE_SCRIPT = ROOT / "compile_official.bat"
# 运行时 `SkyIslandRendering.Apply` 逐材质检查的三个名字，必须与生产代码一致。
REQUIRED = ("BossRush/SkyIsland/Environment", "BossRush/SkyIsland/Water", "BossRush/SkyIsland/Cloud")


def game_path_candidates(compile_script=COMPILE_SCRIPT):
    """按编译脚本 `:ensure_game_path` 里的 `call :try_game_path "..."` 顺序取候选；`%~dp0` 换成仓库根。"""
    text = Path(compile_script).read_text(encoding="utf-8", errors="ignore")
    match = re.search(r"^:ensure_game_path\s*$(.*?)^goto :eof", text, re.M | re.S)
    if not match:
        raise RuntimeError(str(compile_script) + " 里找不到 :ensure_game_path（游戏路径探测口径变了，同步本脚本）")
    root = str(ROOT) + os.sep
    return [Path(raw.replace("%~dp0", root)) for raw in re.findall(r'call :try_game_path "([^"]+)"', match.group(1))]


def resolve_game_path(environ=None, candidates=None):
    """返回 (游戏根目录或 None, 来源说明)。口径同 compile_official.bat：有效的 GAME_PATH 优先，否则按候选顺序探测。"""
    environ = os.environ if environ is None else environ
    configured = environ.get("GAME_PATH")
    if configured:
        path = Path(configured)
        if (path / MANAGED_MARKER).is_file():
            return path.resolve(), "GAME_PATH"
        print("[WARN] Ignoring invalid GAME_PATH: " + configured)
    for candidate in (game_path_candidates() if candidates is None else candidates):
        if (candidate / MANAGED_MARKER).is_file():
            return candidate.resolve(), "compile_official.bat candidate"
    return None, "not found"


def inspect(path):
    """返回 {shader 名: pass 数} 与 {shader 名: 使用它的材质数}。"""
    import UnityPy

    env = UnityPy.load(str(path))
    # 包里有多个序列化文件，path_id 只在文件内唯一：跨文件用同一个字典会张冠李戴。
    objects = {(id(obj.assets_file), obj.path_id): obj for obj in env.objects}

    def typetree(obj, path_id):
        target = objects.get((id(obj.assets_file), path_id))
        if target is None:
            return None
        try:
            return target.read_typetree()
        except Exception:
            return None

    passes, shader_names, materials, lightmodes = {}, {}, {}, {}
    for obj in env.objects:
        if obj.type.name != "Shader":
            continue
        data = obj.read_typetree()
        parsed = data.get("m_ParsedForm", {})
        name = parsed.get("m_Name") or data.get("m_Name")
        # `UsePass` 型着色器（水面）自身没有编译产物，pass 是对被引用着色器的引用，同样计数。
        passes[name] = sum(len(sub.get("m_Passes", [])) for sub in parsed.get("m_SubShaders", []))
        shader_names[(id(obj.assets_file), obj.path_id)] = name
        # LIGHTMODE 存在 `m_State.m_Tags` 里而不是 pass 自己的 m_Tags；UsePass 型没有自己的
        # 状态块，改看它引用的 pass 名（`BossRush/SkyIsland/Environment/GBuffer`）。
        tags = set()
        for sub in parsed.get("m_SubShaders", []):
            for entry in sub.get("m_Passes", []):
                state = entry.get("m_State") or {}
                for key, value in (state.get("m_Tags", {}) or {}).get("tags", []) or []:
                    if key.upper() == "LIGHTMODE":
                        tags.add(value)
                use = entry.get("m_UseName") or ""
                if use:
                    tags.add("UsePass:" + use.rsplit("/", 1)[-1])
        lightmodes[name] = tags

    for obj in env.objects:
        if obj.type.name != "Material":
            continue
        data = obj.read_typetree()
        reference = data.get("m_Shader", {})
        name = shader_names.get((id(obj.assets_file), reference.get("m_PathID")))
        if name is not None:
            materials[name] = materials.get(name, 0) + 1
    return passes, materials, lightmodes


def check(path):
    print("== " + str(path))
    if not Path(path).exists():
        print("   MISSING")
        return False
    try:
        passes, materials, lightmodes = inspect(path)
    except Exception as error:  # UnityPy 版本/包格式问题也算失败，不能当作通过
        print("   READ FAILED: " + str(error))
        return False
    ok = True
    for name in REQUIRED:
        count = passes.get(name)
        used = materials.get(name, 0)
        tags = lightmodes.get(name) or set()
        deferred = "UniversalGBuffer" in tags or "UsePass:GBuffer" in tags
        if count is None:
            print("   MISSING SHADER " + name)
            ok = False
        elif count == 0:
            print("   STRIPPED  " + name + "（0 pass，材质 " + str(used) + " 个引用它 → 进岛必失败）")
            ok = False
        elif not deferred:
            # CR-2026-09-10-006：鸭科夫跑在 URP Deferred 下（官方 SodaCraft/SodaLit* 只有 GBuffer
            # 没有前向 pass，always-included 里有 StencilDeferred）。少了 UniversalGBuffer，
            # 玩家进得去、走得动，但整张地形一片漆黑。
            print("   NO-GBUFFER " + name + "（passes=" + str(count) + " 但没有 UniversalGBuffer → 延迟管线下画不出来）")
            ok = False
        else:
            print("   OK        " + name + "  passes=" + str(count) + " materials=" + str(used)
                  + " lightModes=" + ",".join(sorted(tags)))
    return ok


def main():
    targets = sys.argv[1:]
    results = []
    if not targets:
        targets = [ROOT / BUNDLE_RELATIVE]
        game, source = resolve_game_path()
        if game is None:
            print("== <游戏副本>")
            print("   GAME NOT FOUND：设环境变量 GAME_PATH 指向 Escape from Duckov 安装根目录，或显式传包路径")
            results.append(False)
        else:
            print("GAME_PATH=" + str(game) + "（" + source + "）")
            targets.append(game / MOD_RELATIVE / BUNDLE_RELATIVE)
    results += [check(target) for target in targets]
    if all(results):
        print("SkyIslandBundleShaders: PASS")
        return 0
    print("SkyIslandBundleShaders: FAIL —— 作者工程需指定 URP 资产后重打包，见 ArtSource/SkyIsland/README.md")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
