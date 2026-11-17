"""潮闸镇只在脚点入室、或站在本栋背对镜头的门前时隐藏本栋上部建筑，保留低墙/家具；源码接线不证明实机画面。"""
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


def compiled_sources(root):
    """正式编译清单里的源码（`compile_official.bat` 的 echo( 行），不遍历仓库目录。"""
    text = (root / "compile_official.bat").read_text("utf-8", errors="ignore")
    return [m.group(1).replace("\\", "/") for m in re.finditer(r"^echo\((\S+\.cs)\s*$", text, re.M)]


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
    # 收起哪一栋只有两个来源：脚点在本屋范围内，或在登记过的门前区（背对镜头的门，2026-11-17）。
    if not re.search(r"if\s*\(room\.Footprint\.Contains\(feet\)\)\s*return\s+room;", body(roofs, "private Room FindRoom(")):
        errors.append("RaidIndoorRoofs: 只有脚点在本屋范围内才算入室")
    if not re.search(r"if\s*\(doorways\[i\]\.Zone\.Contains\(feet\)\)\s*return\s+doorways\[i\]\.Room;", body(roofs, "private Room FindDoorway(")):
        errors.append("RaidIndoorRoofs: 门前区只按脚点落在登记范围内取本栋")
    if not re.search(r"Room\s+show\s*=\s*inside\s*\?\?\s*FindDoorway\(feet\);", update):
        errors.append("RaidIndoorRoofs: 屋里优先于门前区，室外其余地方不收屋顶")
    if update.count(".OnEnter();") != 1 or update.count(".OnLeave();") != 1 or not re.search(
            r"if\s*\(faded\s*!=\s*null\)\s*faded\.Fade\.OnLeave\(\);\s*faded\s*=\s*show;\s*if\s*\(faded\s*!=\s*null\)\s*faded\.Fade\.OnEnter\(\);", update):
        errors.append("RaidIndoorRoofs: 官方 Fade 按计数，进出只经 faded 成对调用")
    if "faded.Fade.OnLeave();" not in body(roofs, "public void Dispose()"):
        errors.append("RaidIndoorRoofs: 清理时必须还原正收起的那一栋")
    doorways = clean_source((root / "Tidegate/TidegateDoorways.cs").read_text("utf-8-sig"))
    register = body(doorways, "internal static int Register(")
    if not re.search(r"if\s*\(!FacesAway\(door\.Side\)\)\s*continue;", register) or "roofs.AddDoorway(" not in register:
        errors.append("TidegateDoorways: 只给背对镜头的门登记门前区")
    for rel in compiled_sources(root):
        if rel == "Tidegate/TidegateDoorways.cs":
            continue
        path = root / rel
        if path.is_file() and ".AddDoorway(" in clean_source(path.read_text("utf-8-sig", errors="ignore")):
            errors.append("门前区只能由 TidegateDoorways 登记：" + rel)
    if "restoreTransition = pavilions;" not in create or "if (restoreTransition) foreach" not in update:
        errors.append("RaidIndoorRoofs: 潮闸镇室外不能继续屋顶过渡")
    if "UnityEngine.Object.Destroy(room.Fade);" not in body(roofs, "public void Dispose()"):
        errors.append("RaidIndoorRoofs: 清理时必须回收自建官方组件")
    if "IndoorRoofs = new RaidIndoorRoofs(root, false);" not in world:
        errors.append("TidegateWorld: 潮闸镇必须使用室外立即复原路径")
    if "world.IndoorRoofs.UpdateFeet(feet);" not in body(tide, "internal void SetViewFocus("):
        errors.append("TidegateTide: 主角位置必须驱动屋顶 owner")
    session = clean_source((root / "Tidegate/TidegateSession.cs").read_text("utf-8-sig"))
    build_session = body(session, "private IEnumerator Build(Action<RaidOfficialLighting> prepareLighting)")
    update_session = body(session, "private void Update()")
    prepare_at, load_at = build_session.find("tide.PrepareIndoorFill();"), build_session.find("lease.BeginLoad();")
    if prepare_at < 0 or load_at < 0 or prepare_at > load_at:
        errors.append("TidegateSession: 必须在官方卸载前准备室内灯模板")
    if not re.search(r"world\.ApplyLayers\(\);.*?tide\.Bind\(world\);", build_session, re.S):
        errors.append("TidegateSession: 官方补光必须在材质图层建立后绑定")
    if not re.search(r"tide\.Bind\(world\);.*?TidegateDoorways\.Register\(world\.IndoorRoofs\).*?tide\.Tick\(0f\);", build_session, re.S):
        errors.append("TidegateSession: 门前区要在世界与图层建好后、首个潮汐 Tick 前登记")
    if not re.search(r"tide\.SetViewFocus\(position\);.*?tide\.Tick\(Time\.deltaTime\);", update_session, re.S):
        errors.append("TidegateSession: 当帧脚点判定必须先于灯数组更新")
    for token in ("indoorFill.ShowRoom(bounds, IndoorLampHeight);", "indoorFill.HideRoom();"):
        if token not in body(tide, "internal void SetViewFocus("):
            errors.append("TidegateTide: 进出屋必须驱动官方角色补光：" + token)
    if "indoorFill.Dispose();" not in body(tide, "public void Dispose()"):
        errors.append("TidegateTide: 释放房间灯与停用模板")
    if "indoorFill.Bind(world.Root);" not in body(tide, "internal void Bind("):
        errors.append("TidegateTide: 必须绑定本图根")
    if "RaidOfficialLighting.TryRead(world.Root.scene," not in body(tide, "internal void Tick("):
        errors.append("TidegateTide: 自研材质必须镜像本图官方太阳")
    if "new SkyIslandLighting(" in session or "lighting.Tick(" in session:
        errors.append("TidegateSession: 不得再用天空岛自定义日照覆盖官方天气")
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
