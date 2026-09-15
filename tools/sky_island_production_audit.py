"""天空岛生产审计与可复现的 11 个相机；只读作者资源，所有输出写独立目录。

blender --factory-startup -b SkyIslandWorld.blend --python-exit-code 1 \
    --python tools/sky_island_production_audit.py -- --project <Unity project> \
    --out Build/sky-production-20260914/audit --render village --skip-audit

默认审计全部 mesh 并创建全部相机；--render all 渲染 11 个验证视角。
开放边仅记录证据：地表、铺路、水、植被片的开放结构不能自动定为缺陷。
"""

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def fingerprint(path):
    stat = path.stat()
    return {"path": str(path), "bytes": stat.st_size,
            "modifiedUtc": datetime.fromtimestamp(stat.st_mtime, timezone.utc).isoformat(),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def category(name):
    lower = name.lower()
    if lower.startswith("nav_"):
        return "navigation"
    if lower.startswith("col_"):
        return "collision"
    if "cloud" in lower:
        return "cloud"
    if "distant" in lower:
        return "distant"
    if "ground" in lower or "paving" in lower:
        return "ground_or_paving"
    if "bridge" in lower:
        return "bridge"
    if "cliff" in lower or "vine" in lower:
        return "cliff_or_vine"
    if "water" in lower:
        return "water"
    if any(token in lower for token in ("leaf", "flower", "fern", "bush", "tree", "grass", "blossom", "coral", "lavender", "lily", "forest")):
        return "vegetation"
    if "life" in lower:
        return "life_props"
    if "tripo" in lower:
        return "imported_prop"
    return "procedural_mixed"


def inspect_mesh(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    positions = [tuple(vertex.co) for vertex in mesh.vertices]
    # 使用位置合并后的边统计排除 UV seam 产生的假开放边；仅量测，不写原网格。
    point_ids = {}
    welded = []
    for point in positions:
        key = tuple(round(value, 6) for value in point)
        if key not in point_ids:
            point_ids[key] = len(point_ids)
        welded.append(point_ids[key])
    edge_use = Counter()
    faces_seen = set()
    duplicate_faces = 0
    for polygon in mesh.polygons:
        ids = [welded[index] for index in polygon.vertices]
        face_key = tuple(sorted(ids))
        duplicate_faces += int(face_key in faces_seen)
        faces_seen.add(face_key)
        for index, first in enumerate(ids):
            second = ids[(index + 1) % len(ids)]
            if first != second:
                edge_use[tuple(sorted((first, second)))] += 1
    zero_triangles = 0
    tiny_triangles = 0
    uv_zero = 0
    uv = mesh.uv_layers.active
    for tri in mesh.loop_triangles:
        a, b, c = (mesh.vertices[index].co for index in tri.vertices)
        area = (b - a).cross(c - a).length * 0.5
        zero_triangles += int(area <= 1e-10)
        tiny_triangles += int(1e-10 < area <= 1e-6)
        if uv:
            a, b, c = (uv.data[index].uv for index in tri.loops)
            uv_zero += int(abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x)) <= 1e-15)
    bounds = [obj.matrix_world @ Vector(point) for point in obj.bound_box]
    materials = [slot.material.name if slot.material else None for slot in obj.material_slots]
    textured = any(slot.material and slot.material.use_nodes and
                   any(node.type == "TEX_IMAGE" and node.image for node in slot.material.node_tree.nodes)
                   for slot in obj.material_slots)
    kind = category(obj.name)
    return {"name": obj.name, "category": kind, "mesh": mesh.name,
            "visibleRender": not obj.hide_render, "hideViewport": obj.hide_viewport,
            "vertices": len(mesh.vertices), "polygons": len(mesh.polygons),
            "triangles": len(mesh.loop_triangles), "looseEdges": sum(1 for edge in mesh.edges if edge.is_loose),
            "materials": materials, "textured": textured,
            "uvLayers": [layer.name for layer in mesh.uv_layers],
            "uvMissingForTexture": textured and not bool(uv),
            "uvZeroAreaTriangles": uv_zero,
            "uvNonfinite": sum(1 for item in uv.data if not all(math.isfinite(value) for value in item.uv)) if uv else 0,
            "positionNonfinite": sum(1 for point in positions if not all(math.isfinite(value) for value in point)),
            "zeroAreaTriangles": zero_triangles, "tinyAreaTriangles": tiny_triangles,
            "duplicateFacesAfterPositionWeld": duplicate_faces,
            "positionWeldPrecisionMetres": 0.000001,
            "boundaryEdgesAfterPositionWeld": sum(count == 1 for count in edge_use.values()),
            "nonmanifoldEdgesAfterPositionWeld": sum(count > 2 for count in edge_use.values()),
            "openSurfaceInterpretation": ("Open surfaces are expected for terrain, paving, water and some plant cards; inspect their silhouette and normals."
                if kind in ("ground_or_paving", "water", "vegetation", "cliff_or_vine") else
                "Merged material batches may contain intersecting or open authored components; edge counts are diagnostics, not automatic defects."),
            "location": list(obj.location), "rotationEuler": list(obj.rotation_euler), "scale": list(obj.scale),
            "matrixWorld": [list(row) for row in obj.matrix_world],
            "boundsBlender": {"min": [min(point[i] for point in bounds) for i in range(3)],
                              "max": [max(point[i] for point in bounds) for i in range(3)]},
            "modifiers": [{"name": item.name, "type": item.type} for item in obj.modifiers]}


def inspect_material(material):
    images = []
    if material.use_nodes:
        for node in material.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                image = node.image
                resolved = Path(bpy.path.abspath(image.filepath))
                images.append({"image": image.name, "filepath": str(resolved), "fileExists": resolved.is_file(),
                               "packed": bool(image.packed_file), "size": list(image.size),
                               "colorspace": image.colorspace_settings.name, "extension": node.extension})
    return {"name": material.name, "users": material.users, "diffuseColor": list(material.diffuse_color),
            "useNodes": material.use_nodes, "images": images}


def cameras(scene):
    # 坐标均为 Unity XYZ 米制；转换为 Blender (x,z,y)，保持源生成器轴约定。
    specs = [
        ("01_hero", "Hero 3/4", (650, 720, -780), (0, 0, 0), "ORTHO", 780),
        ("02_front", "Front", (0, 180, -1000), (0, 0, 0), "ORTHO", 760),
        ("03_side", "Side", (1050, 190, 0), (0, 0, 0), "ORTHO", 760),
        ("04_back", "Back", (0, 220, 1100), (0, 0, 0), "ORTHO", 760),
        ("05_top", "Top", (0, 1200, 0), (0, 0, 0), "ORTHO", 760),
        ("06_underside", "Island underside", (90, -32, -195), (0, -14, -95), "PERSP", 40),
        ("07_main_path", "Main-path player view", (12, 40, -161), (0, 7, -111), "PERSP", 44),
        ("08_landmark", "Landmark player view", (50, 58, 115), (5, 57, 239), "PERSP", 35),
        ("09_architecture", "Architecture close-up", (-8, 25, -145), (-32, 9, -119), "PERSP", 52),
        ("10_cliff", "Cliff close-up", (-244, 13, -164), (-213, -4, -109), "PERSP", 50),
        ("11_vegetation", "Material/vegetation close-up", (33, 17, -104), (32, 5.8, -98), "PERSP", 55),
    ]
    records = []
    for name, label, position, target, mode, value in specs:
        data = bpy.data.cameras.new("Production_" + name)
        camera = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(camera)
        data.type = mode
        data.clip_start = 0.05
        data.clip_end = 10000
        data.lens = value if mode == "PERSP" else 50
        data.ortho_scale = value if mode == "ORTHO" else 10
        camera.location = (position[0], position[2], position[1])
        target_blender = Vector((target[0], target[2], target[1]))
        camera.rotation_euler = (target_blender - camera.location).to_track_quat('-Z', 'Y').to_euler()
        records.append({"id": name, "label": label, "object": camera.name, "positionUnity": list(position),
                        "targetUnity": list(target), "projection": mode, "lensOrOrthographicWidth": value,
                        "visibilityOverride": "Clouds hidden for underside inspection" if name == "06_underside" else None})
    return records


def configure_render(scene, size, samples, engine_mode="auto"):
    # Blender 4 与 5.2 的引擎名称和采样属性不同，依实际注册的 RNA 设置。
    engine = None
    for candidate in (() if engine_mode == "cycles-cpu" else ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT")):
        try:
            scene.render.engine = candidate
            engine = candidate
            break
        except TypeError:
            continue
    if engine is None:
        scene.render.engine = "CYCLES"
        if engine_mode == "cycles-cpu":
            scene.cycles.device = "CPU"
        scene.cycles.samples = samples
        scene.cycles.use_denoising = True
    eevee = getattr(scene, "eevee", None)
    if eevee is not None and hasattr(eevee, "taa_render_samples"):
        eevee.taa_render_samples = samples
    scene.render.resolution_x = size
    scene.render.resolution_y = int(size * 0.75)
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    return {"engine": scene.render.engine, "device": scene.cycles.device if scene.render.engine == "CYCLES" else None,
            "samples": samples, "resolution": [size, int(size * .75)],
            "viewTransform": scene.view_settings.view_transform, "blender": bpy.app.version_string}


def technical_material(wireframe):
    """渲染层材质替换，仅用于技术图；不增加、删减或修改任何场景几何。"""
    material = bpy.data.materials.new("Audit_Technical_" + ("Wireframe" if wireframe else "Solid"))
    material.use_nodes = True
    nodes = material.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (.62, .64, .61, 1)
    bsdf.inputs["Roughness"].default_value = .9
    if wireframe:
        edge = nodes.new("ShaderNodeWireframe")
        edge.use_pixel_size = True
        edge.inputs["Size"].default_value = .8
        mix = nodes.new("ShaderNodeMixRGB")
        mix.inputs[1].default_value = (.62, .64, .61, 1)
        mix.inputs[2].default_value = (.018, .024, .018, 1)
        material.node_tree.links.new(edge.outputs["Fac"], mix.inputs[0])
        material.node_tree.links.new(mix.outputs[0], bsdf.inputs["Base Color"])
    return material


def render_normalized_assets(scene, project, output, size):
    """逐件展示规范化后的真实网格与现有图集，不调用原始 GLB 导入/减面器。"""
    folder = output / "asset_thumbnails"
    folder.mkdir(exist_ok=True)
    for obj in scene.objects:
        obj.hide_render = True
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.55, 0.55, 0.55, 1)
    background.inputs["Strength"].default_value = 0.8
    light_data = bpy.data.lights.new("AuditNeutralSun", "SUN")
    light_data.energy = 2.4
    light_data.angle = math.radians(18)
    light = bpy.data.objects.new(light_data.name, light_data)
    scene.collection.objects.link(light)
    light.rotation_euler = (math.radians(30), math.radians(-22), math.radians(-28))
    camera_data = bpy.data.cameras.new("AuditAssetCamera")
    camera_data.type = "ORTHO"
    camera_data.clip_end = 10000
    camera = bpy.data.objects.new(camera_data.name, camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    records = []
    for path in sorted((project / "ArtSource/SkyIsland/tripo").glob("*.json")):
        payload = json.loads(path.read_text(encoding="utf-8-sig"))
        if "mesh" not in payload:
            continue
        name = path.stem
        data = payload["mesh"]
        mesh = bpy.data.meshes.new("AuditAsset_" + name)
        mesh.from_pydata([(p[0], p[2], p[1]) for p in data["v"]], [], [tuple(reversed(face)) for face in data["f"]])
        mesh.update()
        layer = mesh.uv_layers.new(name="UVMap")
        for poly in mesh.polygons:
            poly.use_smooth = bool(data.get("smooth", True))
            for loop in poly.loop_indices:
                layer.data[loop].uv = data["uv"][mesh.loops[loop].vertex_index]
        material_name = "Sky_Tripo" + "".join(part.title() for part in name.split("_"))
        material = bpy.data.materials.get(material_name)
        source_material_present = material is not None
        if material is None:
            # 某些已导入但未摆放的资产没有进入 World.blend，仍必须纳入逐件覆盖。
            material = bpy.data.materials.new(material_name)
            material.use_nodes = True
            bsdf = material.node_tree.nodes.get("Principled BSDF")
            bsdf.inputs["Roughness"].default_value = .77
            node = material.node_tree.nodes.new("ShaderNodeTexImage")
            node.image = bpy.data.images.load(str(project / "Assets/SkyIsland/Textures" / payload["meta"]["texture"]["file"]), check_existing=True)
            material.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name, mesh)
        scene.collection.objects.link(obj)
        points = [Vector(point) for point in obj.bound_box]
        lo = Vector(tuple(min(point[i] for point in points) for i in range(3)))
        hi = Vector(tuple(max(point[i] for point in points) for i in range(3)))
        center = (lo + hi) * .5
        extent = max(hi - lo)
        camera.location = center + Vector((extent * 1.4, -extent * 1.9, extent * 1.05))
        camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera_data.ortho_scale = extent * 1.35
        scene.render.filepath = str(folder / (name + ".png"))
        bpy.ops.render.render(write_still=True)
        record = inspect_mesh(obj)
        record.update({"assetKind": "normalized_imported_model", "source": fingerprint(path),
                       "sourceWorldMaterialPresent": source_material_present,
                       "sourceMeta": payload["meta"], "preview": fingerprint(Path(scene.render.filepath)),
                       "texture": fingerprint(project / "Assets/SkyIsland/Textures" / payload["meta"]["texture"]["file"])})
        records.append(record)
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)
        print("ASSET_PREVIEW_COMPLETE", name, len(records), flush=True)
    write_json(output / "normalized_assets.json", {"count": len(records), "lighting": "Neutral white sun and gray world; current texture/material unchanged",
                                                   "assets": records})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--render", default="none", help="none, village, all, or comma-separated camera ids")
    parser.add_argument("--skip-audit", action="store_true")
    parser.add_argument("--size", type=int, default=1280)
    parser.add_argument("--samples", type=int, default=24)
    parser.add_argument("--engine", choices=("auto", "cycles-cpu"), default="auto",
                        help="auto preserves EEVEE-first behavior; cycles-cpu avoids graphics-driver dependency")
    parser.add_argument("--technical", default="none",
                        help="comma-separated fixed camera ids for extra solid and triangle-wireframe images")
    parser.add_argument("--cloud-shadow-off", action="store_true",
                        help="preview-only: mirror Unity cloud ShadowCastingMode.Off with Cycles shadow-ray visibility")
    parser.add_argument("--save-scene", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    project = Path(args.project).resolve()
    output = Path(args.out).resolve()
    source = Path(bpy.data.filepath).resolve()
    if output == project or project in output.parents:
        raise ValueError("Audit output must be outside the author project")
    output.mkdir(parents=True, exist_ok=True)
    source_before = fingerprint(source)
    fbx_before = fingerprint(project / "Assets/SkyIsland/SkyIslandWorld.fbx")
    geometry = json.loads((project / "ArtSource/SkyIsland/sky_island_geometry.json").read_text(encoding="utf-8-sig"))
    report_path = project / "SkyIslandExport/sky_island_validation.json"
    unity_report = json.loads(report_path.read_text(encoding="utf-8-sig"))
    scene = bpy.context.scene
    baseline_camera = scene.camera
    render_settings = configure_render(scene, args.size, args.samples, args.engine)
    shadow_overrides = []
    if args.cloud_shadow_off:
        for obj in scene.objects:
            if category(obj.name) == "cloud" and hasattr(obj, "visible_shadow"):
                obj.visible_shadow = False
                shadow_overrides.append(obj.name)
    render_settings["cloudShadowOverride"] = {"requested": args.cloud_shadow_off, "objects": shadow_overrides,
                                               "reason": "Match Unity cloud ShadowCastingMode.Off; source file remains unchanged"}
    if args.render == "assets":
        render_normalized_assets(scene, project, output, args.size)
        if source_before != fingerprint(source) or fbx_before != fingerprint(project / "Assets/SkyIsland/SkyIslandWorld.fbx"):
            raise RuntimeError("Author source changed while asset audit ran")
        return
    camera_records = cameras(scene)
    write_json(output / "cameras.json", {"coordinateSystem": "Unity XYZ metres", "cameras": camera_records, "render": render_settings})
    if not args.skip_audit:
        # 将本轮契约与原件指纹随审计固定下来，后续生成不能改写本轮证据。
        layout_path = project / "Assets/SkyIsland/sky_island_layout.json"
        placement_path = project / "ArtSource/SkyIsland/sky_island_tripo.json"
        placements = json.loads(placement_path.read_text(encoding="utf-8-sig"))
        source_models = []
        for model_path in sorted((project / "ArtSource/SkyIsland/tripo").glob("*.json")):
            payload = json.loads(model_path.read_text(encoding="utf-8-sig"))
            if "mesh" not in payload or "meta" not in payload:
                continue
            mesh = payload["mesh"]
            source_models.append({"name": payload["meta"]["name"],
                                  "fingerprint": fingerprint(model_path), "metadata": payload["meta"],
                                  "vertices": len(mesh["v"]), "faces": len(mesh["f"]),
                                  "positionNonfinite": sum(not math.isfinite(v) for p in mesh["v"] for v in p),
                                  "hasUv": bool(mesh.get("uv"))})
        write_json(output / "source_contracts.json", {
            "layout": fingerprint(layout_path),
            "geometryMetadata": fingerprint(project / "ArtSource/SkyIsland/sky_island_geometry.json"),
            "collisionBoxes": geometry["collisionBoxes"], "markers": geometry["markers"],
            "navVertices": geometry["navVertices"], "navTriangles": geometry["navTriangles"],
            "placementSource": fingerprint(placement_path), "placements": placements["placements"],
            "vegetationPathClearance": placements.get("vegetationPathClearance", []),
            "normalizedModels": source_models})
        records = []
        for index, obj in enumerate(sorted((obj for obj in scene.objects if obj.type == "MESH"), key=lambda obj: obj.name)):
            records.append(inspect_mesh(obj))
            if index % 50 == 0:
                print("AUDIT_PROGRESS", index, obj.name, flush=True)
        visible = [row for row in records if row["visibleRender"]]
        expected = geometry["visualMeshes"]
        actual = {row["name"]: row["triangles"] for row in visible}
        count_mismatches = [{"name": name, "blend": actual.get(name), "metadata": item["triangles"]}
                            for name, item in expected.items() if actual.get(name) != item["triangles"]]
        report = {"classification": "SAFE", "generatedUtc": datetime.now(timezone.utc).isoformat(),
                  "sourceBlend": source_before, "sourceFbx": fbx_before,
                  "sourceValidation": {"fbxMatchesUnityReport": fbx_before["sha256"] == unity_report.get("sourceFbxSHA256"),
                                       "blendAndFbxModificationGapSeconds": abs(source.stat().st_mtime - (project / "Assets/SkyIsland/SkyIslandWorld.fbx").stat().st_mtime),
                                       "metadataExpectedVisibleMeshes": len(expected), "missingVisibleNames": sorted(set(expected) - set(actual)),
                                       "additionalGroundMeshes": sorted(name for name in set(actual) - set(expected) if name.startswith("VIS_Ground_")),
                                       "unexpectedVisibleNames": sorted(name for name in set(actual) - set(expected) if not name.startswith("VIS_Ground_")),
                                       "triangleCountMismatches": count_mismatches,
                                       "note": "Matching names/counts/time and FBX hash establish generation provenance; position-level FBX parity is separate."},
                  "summary": {"allMeshes": len(records), "visibleMeshes": len(visible), "visibleTriangles": sum(row["triangles"] for row in visible),
                              "categories": dict(Counter(row["category"] for row in visible)),
                              "positionNonfinite": sum(row["positionNonfinite"] for row in records),
                              "uvMissingForTexture": sum(row["uvMissingForTexture"] for row in visible),
                              "visibleZeroAreaTriangles": sum(row["zeroAreaTriangles"] for row in visible),
                              "visibleDuplicateFacesAfterPositionWeld": sum(row["duplicateFacesAfterPositionWeld"] for row in visible)},
                  "objects": records, "materials": [inspect_material(mat) for mat in bpy.data.materials],
                  "limits": ["Blender preview does not verify the Unity Deferred shaders or gameplay visibility.",
                             "Topology boundaries and overlapping shells require visual interpretation.",
                             "Material batches contain multiple source instances; this report covers every actual scene mesh, not an inferred prop list."]}
        write_json(output / "scene_audit.json", report)
        print("AUDIT_COMPLETE", json.dumps(report["summary"]), flush=True)
    wanted = [row["id"] for row in camera_records] if args.render == "all" else args.render.split(",")
    rendered = []
    for identifier in wanted:
        if identifier == "none":
            continue
        scene.camera = baseline_camera if identifier == "village" else bpy.data.objects["Production_" + identifier]
        scene.render.filepath = str(output / (identifier + ".png"))
        hidden = []
        if identifier == "06_underside":
            for obj in scene.objects:
                if category(obj.name) == "cloud" and not obj.hide_render:
                    obj.hide_render = True
                    hidden.append(obj)
        try:
            bpy.ops.render.render(write_still=True)
        finally:
            for obj in hidden:
                obj.hide_render = False
        rendered.append(fingerprint(Path(scene.render.filepath)))
        print("RENDER_COMPLETE", scene.render.filepath, flush=True)
    technical_ids = [] if args.technical == "none" else args.technical.split(",")
    if technical_ids:
        previous_override = scene.view_layers[0].material_override
        try:
            for mode in ("solid", "wireframe"):
                scene.view_layers[0].material_override = technical_material(mode == "wireframe")
                for identifier in technical_ids:
                    scene.camera = baseline_camera if identifier == "village" else bpy.data.objects["Production_" + identifier]
                    scene.render.filepath = str(output / (identifier + "_" + mode + ".png"))
                    hidden = [obj for obj in scene.objects if not obj.hide_render and category(obj.name) == "cloud"]
                    for obj in hidden:
                        obj.hide_render = True
                    try:
                        bpy.ops.render.render(write_still=True)
                    finally:
                        for obj in hidden:
                            obj.hide_render = False
                    rendered.append(fingerprint(Path(scene.render.filepath)))
                    print("TECHNICAL_COMPLETE", scene.render.filepath, flush=True)
        finally:
            scene.view_layers[0].material_override = previous_override
    if rendered:
        write_json(output / ("render_manifest_" + args.render.replace(",", "_") + ".json"),
                   {"sourceBlend": source_before, "sourceFbx": fbx_before, "settings": render_settings,
                    "technical": {"cameraIds": technical_ids, "cloudsHidden": True,
                                  "wireframe": "Shader triangle edges; no mesh modifier; material override only"}, "renders": rendered})
    if args.save_scene:
        bpy.ops.wm.save_as_mainfile(filepath=str(output / "SkyIsland_ProductionAudit.blend"))
    if source_before != fingerprint(source) or fbx_before != fingerprint(project / "Assets/SkyIsland/SkyIslandWorld.fbx"):
        raise RuntimeError("Author source changed while audit ran")


if __name__ == "__main__":
    main()
