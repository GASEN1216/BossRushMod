"""潮闸镇只在脚点入室时隐藏本栋上部建筑，保留低墙/家具；源码接线不证明实机画面。"""
import argparse
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
sys.path.insert(0, str(ROOT / "tools"))
from cs_source_util import clean_source
from unity_project_path import find_unity_project


def body(source, signature):
    start = source.index("{", source.index(signature))
    depth = 0
    for pos in range(start, len(source)):
        depth += (source[pos] == "{") - (source[pos] == "}")
        if depth == 0:
            return source[start + 1:pos]
    raise ValueError("unterminated " + signature)


def check(root, project):
    roofs = clean_source((root / "Common/World/RaidIndoorRoofs.cs").read_text("utf-8-sig"))
    tide = clean_source((root / "Tidegate/TidegateTide.cs").read_text("utf-8-sig"))
    world = clean_source((root / "Tidegate/TidegateWorld.cs").read_text("utf-8-sig"))
    create = body(roofs, "internal RaidIndoorRoofs(")
    update = body(roofs, "internal bool UpdateFeet(")
    errors = []
    expected = (
        'roofs.TryGetValue("BLD_" + volume.name + "_Roof", out roof)',
        "Renderer[] roofRenderers = roof.GetComponentsInChildren<Renderer>(true);",
        "if (roofRenderers.Length == 0) continue;",
        "fade.renderers = roofRenderers;",
        "fade.fadeType = OcclusionFadeTypes.ShadowOnly;",
        "fade.triggers = new OcclusionFadeTrigger[0];",
        "Footprint = box.bounds",
    )
    for token in expected:
        if token not in create:
            errors.append("RaidIndoorRoofs: 作者隐藏组或脚点绑定缺失：" + token)
    if "GetPropertyBlock(" in create or "SetPropertyBlock(" in create:
        errors.append("RaidIndoorRoofs: 不得给整片建筑安装常驻属性块")
    if "roof.parent.GetComponentsInChildren" in roofs or "SetWalls(" in roofs:
        errors.append("RaidIndoorRoofs: 非屋顶节点不得进入室内虚化")
    if not re.search(r"if\s*\(room\.Footprint\.Contains\(feet\)\)\s*\{\s*inside\s*=\s*room;\s*room\.Fade\.OnEnter\(\);", update):
        errors.append("RaidIndoorRoofs: 只有脚点在本屋范围内才可调用 OnEnter")
    if update.count(".OnEnter();") != 1 or "inside.Fade.OnLeave();" not in update:
        errors.append("RaidIndoorRoofs: 入室单次进入与离室还原必须成对")
    if "restoreTransition = pavilions;" not in create or "if (restoreTransition) foreach" not in update:
        errors.append("RaidIndoorRoofs: 潮闸镇室外不能继续屋顶过渡")
    if "UnityEngine.Object.Destroy(room.Fade);" not in body(roofs, "public void Dispose()"):
        errors.append("RaidIndoorRoofs: 清理时必须回收自建官方组件")
    if "IndoorRoofs = new RaidIndoorRoofs(root, false);" not in world:
        errors.append("TidegateWorld: 潮闸镇必须使用室外立即复原路径")
    if "world.IndoorRoofs.UpdateFeet(feet);" not in body(tide, "internal void SetViewFocus("):
        errors.append("TidegateTide: 主角位置必须驱动屋顶 owner")
    for token in ("_TidegateExterior", "_TidegateRuin", "_TidegateViewFocus"):
        if token in roofs or token in tide:
            errors.append("潮闸镇不得恢复视线/墙体裁切参数 " + token)
    if project:
        builder = clean_source((project / "Assets/Editor/TidegateBuilder.cs").read_text("utf-8-sig"))
        build = body(builder, "static void BuildWorldPrefab(")
        split_call = "TidegateRoofGeometry.Build(root, layout, Report);"
        if split_call not in build:
            errors.append("TidegateBuilder: 世界构建必须拆分上部建筑与低墙/家具")
        elif build.index(split_call) < build.index("TidegateTraversalBuilder.Build("):
            errors.append("TidegateBuilder: 必须先由原始外梯面建栏杆碰撞，再切室内视觉面")
        preview = body(builder, "static void RenderPreviews(")
        for token in ('r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;', 'IndoorShot("hw", "shop",', 'IndoorShot("h2", "house",'):
            if token not in preview:
                errors.append("TidegateBuilder: 室内预览必须切实际隐藏组并覆盖民居/五金店：" + token)
        if "_TidegateViewFocus" in preview:
            errors.append("TidegateBuilder: 室内预览不得使用已删除的视线 shader 参数")
        shader = clean_source((project / "Assets/Tidegate/Shaders/TidegateEnvironment.shader").read_text("utf-8-sig"))
        for token in ("ClipExterior", "RestoreIndoorRoof", "_TidegateExterior", "_TidegateRuin", "_TidegateViewFocus", "_TidegateRoofBackAt"):
            if token in shader:
                errors.append("Environment.shader: 不得恢复建筑裁切 " + token)
        if '"LightMode"="UniversalGBuffer"' not in shader or '"LightMode"="DepthOnly"' not in shader:
            errors.append("Environment.shader: 必须保留官方延迟与深度通道")
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--unity-project", type=Path)
    args = parser.parse_args()
    project = args.unity_project or find_unity_project()
    errors = check(args.root, Path(project) if project else None)
    print("TidegateIndoorRoofGuard: " + ("FAIL\n" + "\n".join(errors) if errors else "PASS"))
    if not project:
        print("PARTIAL: 未找到作者工程；未校验屋顶拆分接线与 Environment.shader")
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
