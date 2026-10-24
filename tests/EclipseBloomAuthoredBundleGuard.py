"""直接读取最终 Unity 资源包，验证 Blender 作者网格/材质/贴图及发布接线；不代替实机观感。"""
import json
import math
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BUNDLE = "Assets/ui/eclipse_bloom_visuals"
SHIELD_BUNDLE = "Assets/Equipment/residual_sun_shield_model"
# Unity 米制/XZ 地面包络；允许作者倒角的微小内缩，不容许 FBX 轴翻转或百倍缩放。
ENVELOPES = {
    "EclipseBloom_Petal": ((-.93, -.19, -1.75), (.93, .43175, 1.65)),
    "EclipseBloom_Core": ((-1.008644, -1.007429, -1.019627), (1.001831, 1.009384, 1.001887)),
    "EclipseBloom_Fragment": ((-.22, -.1, -.34), (.31, .22, .52)),
    "ResidualSunShield_ClosedObsidianPlate": ((-.34, -.036, -.48), (.33, .063, .43)),
    "ResidualSunShield_ChippedGoldBevel": ((-.3366, .012, -.4752), (.3267, .066, .4257)),
    "ResidualSunShield_FracturedSolarCrown": ((-.266259, .072, -.139082), (.191675, .1, .323924)),
    "ResidualSunShield_FacetedBlackSun": ((-.188, .065, -.048), (.098, .094, .238)),
    "ResidualSunShield_MoltenFaultLines": ((-.231893, .062, -.371495), (.191481, .080634, .36229)),
    "ResidualSunShield_BackGripAndRivets": ((-.129, -.09, -.171), (.099, -.0295, .141)),
}


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def inspect_mesh(mesh):
    from UnityPy.helpers.MeshHelper import MeshHandler
    handler = MeshHandler(mesh)
    handler.process()
    vertices = handler.m_Vertices
    require(vertices and len(vertices) >= 4, mesh.m_Name + ": 顶点为空")
    require(all(math.isfinite(v) for point in vertices for v in point), mesh.m_Name + ": 非有限顶点")
    normals, uv = handler.m_Normals, handler.m_UV0
    require(normals and len(normals) == len(vertices)
            and all(all(math.isfinite(v) for v in point) and .9 < sum(v*v for v in point[:3]) < 1.1 for point in normals),
            mesh.m_Name + ": 法线缺失或长度无效")
    require(uv and len(uv) == len(vertices) and all(math.isfinite(v) for point in uv for v in point)
            and len({tuple(point) for point in uv}) >= 4, mesh.m_Name + ": 纹理 UV 缺失或收缩为单点")
    welded = [tuple(round(float(v), 5) for v in point) for point in vertices]
    edges = defaultdict(list)
    adjacent = defaultdict(set)
    face_volumes = []
    volume = 0.0
    faces = 0
    for submesh in handler.get_triangles():
        for triangle in submesh:
            require(len(triangle) == 3 and all(0 <= i < len(vertices) for i in triangle), mesh.m_Name + ": 索引损坏")
            a, b, c = [vertices[i] for i in triangle]
            u, v = [b[i] - a[i] for i in range(3)], [c[i] - a[i] for i in range(3)]
            cross = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
            require(sum(x*x for x in cross) > 1e-15, mesh.m_Name + ": 退化三角面")
            face_volume = (a[0]*(b[1]*c[2]-b[2]*c[1]) + a[1]*(b[2]*c[0]-b[0]*c[2]) + a[2]*(b[0]*c[1]-b[1]*c[0])) / 6
            volume += face_volume
            face_volumes.append(face_volume)
            ids = [welded[i] for i in triangle]
            for index in range(3):
                first, second = ids[index], ids[(index+1) % 3]
                require(first != second, mesh.m_Name + ": 焊接后出现零长边")
                edge = tuple(sorted((first, second)))
                adjacent[edge].add(faces)
                edges[edge].append((first, second))
            faces += 1
    require(faces > 3, mesh.m_Name + ": 三角面不足")
    require(all(len(pairs) == 2 and pairs[0] == pairs[1][::-1] for pairs in edges.values()), mesh.m_Name + ": 网格不封闭或相邻面绕序冲突")
    require(volume > 1e-8, mesh.m_Name + ": 总体积非正，法线朝内")
    neighbours = defaultdict(set)
    for linked in adjacent.values():
        first, second = tuple(linked)
        neighbours[first].add(second)
        neighbours[second].add(first)
    remaining = set(range(faces))
    components = 0
    while remaining:
        pending = [remaining.pop()]
        component_volume = 0.0
        while pending:
            face = pending.pop()
            component_volume += face_volumes[face]
            for neighbour in neighbours[face]:
                if neighbour in remaining:
                    remaining.remove(neighbour)
                    pending.append(neighbour)
        require(component_volume > 1e-10, mesh.m_Name + ": 独立封闭部件法线朝内/零体积")
        components += 1
    minimum = [min(v[axis] for v in vertices) for axis in range(3)]
    maximum = [max(v[axis] for v in vertices) for axis in range(3)]
    require(mesh.m_Name in ENVELOPES, "未登记的正式网格: " + mesh.m_Name)
    expected = ENVELOPES[mesh.m_Name]
    require(all(abs(bounds[axis] - reference[axis]) <= .026 for bounds, reference in zip((minimum, maximum), expected) for axis in range(3)), mesh.m_Name + ": 轴/单位/包络不符")
    return {"name": mesh.m_Name, "vertices": len(vertices), "triangles": faces, "components": components, "volume": volume, "bounds": [minimum, maximum]}


def inspect_material(material):
    shader = material.m_Shader.read_typetree()
    parsed = shader.get("m_ParsedForm", {})
    require(parsed.get("m_Name") == "BossRush/EclipseBloom/Metal", material.m_Name + ": 会被工厂替换的非专属材质")
    require(4 in shader.get("platforms", []) and len(shader.get("compressedBlob", [])) > 0, material.m_Name + ": 没有 Windows d3d11 着色器")
    require("UniversalGBuffer" in json.dumps(parsed), material.m_Name + ": 缺少 URP Deferred pass")


def validate(root=ROOT):
    import UnityPy
    from PIL import Image
    manifest = json.loads((root / "tools/resource_release_manifest.json").read_text(encoding="utf-8-sig"))
    report = []
    for bundle_name in (BUNDLE, SHIELD_BUNDLE):
        require(manifest["bundles"].count(bundle_name) == 1, "发布清单未唯一登记资源包: " + bundle_name)
        bundle = root / bundle_name
        require(bundle.stat().st_size > 1024, "资源包为空: " + bundle_name)
        env = UnityPy.load(str(bundle))
        paths = {str(path).lower(): reader for path, reader in env.container.items()}
        if bundle_name == BUNDLE:
            for name in ("petal", "core", "fragment"):
                path = "assets/eclipsebloom/meshes/eclipsebloom_" + name + ".asset"
                require(path in paths, "最终包缺少运行时网格路径: " + path)
                report.append(inspect_mesh(paths[path].read()))
            for name in ("obsidian", "gold"):
                path = "assets/eclipsebloom/materials/eclipsebloom_" + name + ".mat"
                require(path in paths, "最终包缺少运行时材质路径: " + path)
                inspect_material(paths[path].read())
        else:
            prefab = "assets/eclipsebloom/prefabs/residualsunshield_totem_model.prefab"
            require(prefab in paths, "最终包缺少工厂命名的盾片 prefab")
            root_object = paths[prefab].read()
            require(root_object.m_Name == "ResidualSunShield_Totem_Model", "工厂模型 BaseName 不匹配")
            meshes = [obj.read() for obj in env.objects if obj.type.name == "Mesh"]
            require(set(mesh.m_Name for mesh in meshes) == {name for name in ENVELOPES if name.startswith("ResidualSunShield_")}, "盾片缺少独立作者部件")
            report.extend(inspect_mesh(mesh) for mesh in meshes)
            materials = [obj.read() for obj in env.objects if obj.type.name == "Material"]
            require(len(materials) == 4, "盾片四类材质不完整")
            for material in materials:
                inspect_material(material)
            transforms = [obj.read() for obj in env.objects if obj.type.name == "Transform"]
            grounds = [obj for obj in transforms if obj.m_GameObject.read().m_Name == "GroundPoint"]
            require(len(grounds) == 1, "盾片 GroundPoint 缺失/重复")
            lowest = min(mesh.m_LocalAABB.m_Center.y - mesh.m_LocalAABB.m_Extent.y for mesh in meshes)
            require(abs(grounds[0].m_LocalPosition.y - lowest) < 1e-5, "盾片地面锚点未贴合真实底部")
            for transform in transforms:
                scale = transform.m_LocalScale
                require(all(abs(value - 1) < 1e-6 for value in (scale.x, scale.y, scale.z)), "盾片根/网格节点缩放非单位")
            require(not any(obj.type.name.endswith("Collider") for obj in env.objects), "盾片有多余碰撞器")
        textures = [obj.read() for obj in env.objects if obj.type.name == "Texture2D"]
        for texture in textures:
            require(0 < texture.m_Width <= 512 and 0 < texture.m_Height <= 512, texture.m_Name + ": 贴图超过装备资源上限")
        if bundle_name == SHIELD_BUNDLE:
            icons = [texture for texture in textures if texture.m_Name == "residual_sun_shield_icon"]
            require(len(icons) == 1 and icons[0].m_Width == 512 and icons[0].m_Height == 512, "盾片包图标不是 512 成品")
    icon = Image.open(root / "Assets/Items/residual_sun_shield_icon.png").convert("RGBA")
    require(icon.size == (512, 512), "实际 UI PNG 不是 512")
    alpha = icon.getchannel("A")
    require(alpha.getextrema() == (0, 255), "实际 UI PNG 缺少透明背景或实心主体")
    require(sum(a > 0 for a in alpha.getdata()) > 512 * 512 * .15, "实际 UI PNG 主体过小/空白")
    return report


if __name__ == "__main__":
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT
    if not all((root / name).is_file() for name in (BUNDLE, SHIELD_BUNDLE)):
        print("EclipseBloomAuthoredBundleGuard: 缺少正式资源包，不能确认完整性")
        raise SystemExit(2)
    try:
        result = validate(root)
        print("EclipseBloomAuthoredBundleGuard: PASS " + json.dumps(result, ensure_ascii=False))
    except (AssertionError, ImportError, ValueError, OSError) as error:
        print("EclipseBloomAuthoredBundleGuard: FAIL " + str(error))
        raise SystemExit(1)
