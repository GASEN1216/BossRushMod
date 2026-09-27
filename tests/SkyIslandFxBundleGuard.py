"""天空岛运行时特效小包 skyisland_fx（热浪折射 + 泥面流动，owner 2026-09-27 批准重打包）。

不变式：
1. 运行时 SkyIslandFxAssets 的包路径与材质路径 == 作者工程 SkyIslandFxBundleBuilder 的常量（作者工程不在时只查仓库侧）；
2. 正式编译脚本部署 Assets\\ui\\skyisland_fx，缺包只警告；
3. 缺包 / 材质缺失 / 着色器不受支持时退回粒子版：LoadFromFile 一次性闸门、Usable 查 isSupported、
   热浪另要求管线提供不透明场景色（SceneColorAvailable），ResetStaticCaches 卸包；
4. 调用点：匠首过热叠折射热浪、穗镰泥块换流动材质（换上后不再推涟漪圈）；
5. 包文件在时（local-only 二进制）：UnityPy 读出恰好两个材质，着色器名对、都带 d3d11 编译产物、< 256 KiB；
6. 作者着色器是透明队列、只走 UniversalForward（Deferred 下透明物体走前向；不写 UniversalForwardOnly、不留无名 pass）。

包文件缺失时退出码 2（外部制品缺失，source-only 模式记 PARTIAL）。脚本末尾带内存反向检查。
"""
import re
import sys
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
FX = ROOT / "DebugAndTools" / "SkyIsland" / "SkyIslandImpactFx.cs"
FX_ASSETS = ROOT / "DebugAndTools" / "SkyIsland" / "SkyIslandFxAssets.cs"
FOREMAN = ROOT / "DebugAndTools" / "SkyIsland" / "SkyIslandForemanBoss.cs"
BAT = ROOT / "compile_official.bat"
BUNDLE = ROOT / "Assets" / "ui" / "skyisland_fx"
HAZE_SHADER = "BossRush/SkyIsland/HeatHaze"
MUD_SHADER = "BossRush/SkyIsland/MudFlow"


def squash(text):
    return re.sub(r"\s+", "", text or "")


def body_of(text, signature):
    at = text.find(signature) if text else -1
    if at < 0:
        return None
    start = text.find("{", at)
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return None


def need(errors, hay, needle, message):
    if hay is None or squash(needle) not in squash(hay):
        errors.append(message)


def check(code):
    errors = []
    fx, foreman, bat = code["fx"], code["foreman"], code["bat"]
    need(errors, fx, 'internal const string BundleRelativePath = "Assets/ui/skyisland_fx";', "运行时包路径不是 Assets/ui/skyisland_fx")
    need(errors, fx, 'internal const string HazeMaterialPath = "assets/skyisland/fx/skyislandheathaze.mat";', "热浪材质路径漂移")
    need(errors, fx, 'internal const string MudMaterialPath = "assets/skyisland/fx/skyislandmudflow.mat";', "泥面材质路径漂移")
    load = body_of(fx, "private static void Load()")
    need(errors, load, "if (attempted) return; attempted = true;", "LoadFromFile 必须是一次性闸门")
    need(errors, load, "if (!File.Exists(path))", "缺包要早退到粒子版")
    need(errors, fx, "return material != null && material.shader != null && material.shader.isSupported ? material : null;", "材质要查着色器受支持")
    need(errors, fx, "get { Load(); return haze != null && SceneColorAvailable() ? haze : null; }", "热浪只在管线提供不透明场景色时启用")
    need(errors, body_of(fx, "internal static bool SceneColorAvailable()"), 'GetProperty("supportsCameraOpaqueTexture")', "要读 URP 资产的 Opaque Texture")
    need(errors, body_of(fx, "internal static void ResetStaticCaches()\n        {\n            if (bundle"), "bundle.Unload(true);", "ResetStaticCaches 要卸包")
    need(errors, body_of(fx, "internal static void ResetStaticCaches()"), "SkyIslandFxAssets.ResetStaticCaches();", "ImpactFx 收尾要连带卸特效小包")
    need(errors, foreman, "overheatHaze = SkyIslandImpactFx.CreateHeatHaze(overheatGlow.transform);", "匠首过热没接折射热浪")
    need(errors, fx, "flow.shaderFlow = TryUseFlowMaterial(patch);", "泥块没接流动材质")
    need(errors, fx, "if (!shaderFlow)", "换上流动材质后不应再推涟漪圈")
    if 'copy /Y "Assets\\ui\\skyisland_fx" "%GAME_PATH%\\Duckov_Data\\Mods\\%MOD_NAME%\\Assets\\ui\\skyisland_fx"' not in bat:
        errors.append("compile_official.bat 没部署 Assets\\ui\\skyisland_fx")
    builder = code.get("builder")
    if builder:
        need(errors, builder, 'private const string BundleName = "skyisland_fx";', "作者构建器包名漂移")
        need(errors, builder, 'private const string HazeMaterialPath = Dir + "/SkyIslandHeatHaze.mat";', "作者构建器热浪材质路径漂移")
        need(errors, builder, 'private const string MudMaterialPath = Dir + "/SkyIslandMudFlow.mat";', "作者构建器泥面材质路径漂移")
        need(errors, builder, 'private const string Dir = "Assets/SkyIsland/Fx";', "作者构建器目录漂移")
    for key in ("haze_shader", "mud_shader"):
        shader = code.get(key)
        if shader is None:
            continue
        if '"Queue"="Transparent"' not in shader:
            errors.append(key + " 必须在透明队列")
        if shader.count('"LightMode"="UniversalForward"') != 1 or "UniversalForwardOnly" in shader:
            errors.append(key + " 必须恰好一个 UniversalForward pass（不写 UniversalForwardOnly）")
        if shader.count("Pass") != shader.count('"LightMode"='):
            errors.append(key + " 不许留无名 pass（会画两遍）")
    return errors


def inspect_bundle():
    errors = []
    if BUNDLE.stat().st_size >= 256 * 1024:
        errors.append("skyisland_fx 超过 256 KiB")
    try:
        import UnityPy
    except ImportError:
        return errors, "UnityPy 不可用，跳过二进制内容检查"
    env = UnityPy.load(str(BUNDLE))
    materials, shaders = [], {}
    for obj in env.objects:
        if obj.type.name == "Material":
            materials.append(obj.read_typetree().get("m_Name"))
        elif obj.type.name == "Shader":
            tree = obj.read_typetree()
            shaders[tree.get("m_ParsedForm", {}).get("m_Name")] = (tree.get("platforms") or [], len(tree.get("compressedBlob") or []))
    if sorted(materials) != ["SkyIslandHeatHaze", "SkyIslandMudFlow"]:
        errors.append("包内材质不是恰好两个：%s" % materials)
    for name in (HAZE_SHADER, MUD_SHADER):
        platforms, blob = shaders.get(name, ([], 0))
        if 4 not in platforms or blob <= 0:
            errors.append("包内 %s 没有 d3d11 编译产物（platforms=%s blob=%d）" % (name, platforms, blob))
    return errors, None


def load():
    code = {
        # 加载器在 2026-09-27 拆到 SkyIslandFxAssets.cs：两份拼起来查。
        "fx": clean_source(FX.read_text(encoding="utf-8")) + "\n" + clean_source(FX_ASSETS.read_text(encoding="utf-8")),
        "foreman": clean_source(FOREMAN.read_text(encoding="utf-8")),
        "bat": BAT.read_text(encoding="utf-8", errors="replace"),
    }
    try:
        import unity_project_path
        project = unity_project_path.find_unity_project()
    except Exception:
        project = None
    if project:
        base = Path(project)
        for key, rel in (("builder", "Assets/Editor/SkyIslandFxBundleBuilder.cs"),
                         ("haze_shader", "Assets/SkyIsland/Fx/SkyIslandHeatHaze.shader"),
                         ("mud_shader", "Assets/SkyIsland/Fx/SkyIslandMudFlow.shader")):
            path = base / rel
            if not path.is_file():
                code[key] = ""  # 作者工程在、文件却不在：让对应断言红
            else:
                code[key] = path.read_text(encoding="utf-8")
    return code, project


def reverse_checks(code):
    probes = [
        ("fx", "haze != null && SceneColorAvailable() ? haze : null", "haze"),
        ("fx", "if (!shaderFlow)", "if (true)"),
        ("bat", 'copy /Y "Assets\\ui\\skyisland_fx"', 'rem "Assets\\ui\\skyisland_fx"'),
        ("foreman", "overheatHaze = SkyIslandImpactFx.CreateHeatHaze(overheatGlow.transform);", ""),
    ]
    if code.get("haze_shader"):
        probes.append(("haze_shader", '"LightMode"="UniversalForward"', '"LightMode"="UniversalForwardOnly"'))
    failures = []
    for key, old, new in probes:
        if code[key].count(old) != 1:
            failures.append("反向检查锚点不唯一：%s %s" % (key, old))
            continue
        broken = dict(code)
        broken[key] = code[key].replace(old, new)
        if not check(broken):
            failures.append("反向检查没有转红：%s %s" % (key, old))
    return failures


def main():
    code, project = load()
    errors = check(code) or reverse_checks(code)
    note = []
    if not project:
        note.append("作者工程不在，只查仓库侧")
    if not errors and BUNDLE.is_file():
        bundle_errors, skipped = inspect_bundle()
        errors += bundle_errors
        if skipped:
            note.append(skipped)
    if errors:
        for e in errors:
            print("FAIL:", e)
        sys.exit(1)
    if not BUNDLE.is_file():
        print("PARTIAL: SkyIslandFxBundleGuard 源码侧通过，但 Assets/ui/skyisland_fx 不在（local-only 二进制）")
        sys.exit(2)
    print("PASS: SkyIslandFxBundleGuard（路径对齐、部署、回退、调用点、包内容与着色器 pass）" + ("；" + "；".join(note) if note else ""))


if __name__ == "__main__":
    main()
