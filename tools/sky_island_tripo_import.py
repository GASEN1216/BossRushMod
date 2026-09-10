"""把 Tripo3D 生成的 GLB 规范化成天空岛管线可直接消费的网格与贴图。

Tripo3D 的输出有三个不适合直接入场景的地方：尺度是任意的、朝向不固定、面数从几万到十几万，
而且自带一张 PBR 贴图集。本工具在 Blender 里逐件处理：合并 → 摆正 → 按真实米数缩放 →
减面到预算 → 导出贴图。产物是纯数据（网格 JSON + 一张 PNG），主生成器不需要再碰 GLB。

之所以输出 JSON 而不是 FBX：主世界是单个 FBX + geometry.json 的元数据驱动管线，
`SkyIslandBundleBuilder` 按材质名建材质。把 Tripo 件转成同样的 {material: {v,f,uv,smooth}}
契约（与 sky_island_life_models 一致），就能直接并进主 FBX，不新增 C# 分支、不新增包。

贴图文件名不含 `handpainted`，因此构建器会按 Clamp 导入——图集 UV 正需要 Clamp，
平铺才需要 Repeat，这点不能弄反。

用法：
    blender -b --python tools/sky_island_tripo_import.py -- \
        --glb-dir Build/tripo-glb --project <作者工程>
    ... --only bell_arch          # 只处理一件，用于先跑通一件再批量
"""

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix

# name: (目标高度 米, 单件三角面预算, 计划实例数, 中文说明)
#
# 预算按「实例数 × 单件面数」定，不是按单件观感定：主世界烘成单个 FBX，**没有实例化**
# （现有 659 个独立网格），摆 14 盏路灯就是 14 份完整三角面。因此高复用件必须给极小预算，
# 一次性地标才配得上大预算。全部 26 件按此表合计约 21.7 万面，占当前 114.9 万的 +19%。
#
# Tripo3D 侧的 face_limit 建议设成单件预算的 2 倍左右（见 ArtSource/SkyIsland/TRIPO_SETTINGS.md）：
# 留出余量给 Blender 做最后一道精确减面，同时避免从十几万面直接压到一两千面而把轮廓压成一团。
ITEMS = {
    'bell_arch':        (8.5,  12000,  1, '悬钟石拱门'),
    'great_tree':       (18.0, 10000,  2, '巨树带板根'),
    'crystal_fountain': (4.2,   8000,  1, '悬浮水晶喷泉'),
    'bell_tower':       (14.0,  8000,  1, '钟塔'),
    'cherry_pavilion':  (7.0,   5000,  2, '石亭'),
    'cottage_large':    (11.0,   4000,  3, '民居B'),
    'farm_barn':        (9.0,   3500,  2, '谷仓'),
    'cottage_dormer':   (8.0,   3000,  4, '民居A'),
    'market_stall':     (4.2,   2500,  3, '市集摊'),
    'stone_platform':   (1.6,   2500,  2, '圆形石台'),
    'cottage_small':    (6.5,   2000,  5, '民居C'),
    'shop_sign':        (7.0,   2000,  3, '店铺'),
    'shrine_pavilion':  (5.0,   2000,  3, '小亭'),
    'cherry_tree':      (9.0,   1500, 12, '花树'),
    'pergola':          (4.0,   1500,  3, '木藤架'),
    'ruined_column':    (5.0,   1200,  6, '断裂石柱'),
    'root_rock':        (3.4,   1200,  8, '缠根岩'),
    'farm_cart':        (2.0,   1200,  3, '农用推车'),
    'crystal_cluster':  (3.2,    800, 10, '青水晶簇'),
    'rune_stone':       (3.4,    800,  6, '符文石'),
    'stone_stair':      (2.0,    800,  6, '石阶'),
    'street_lamp':      (5.0,    700, 14, '雕花路灯'),
    'stone_bench':      (3.0,   1400,  4, '拱形石桥（素材图为石凳，Tripo 出成了桥）'),
    'banner_pole':      (6.5,    600,  8, '旗帜立柱'),
    'crate_barrel':     (1.6,    600, 12, '桶箱堆'),
    'planter':          (1.1,    400, 14, '花箱'),

    # ── 替换批次：取代程序化低模 ───────────────────────────────────
    'temple':              ( 12.0,   6000,  1, '镜水寺 43×23m 高12'),
    'workshop_dome':       ( 18.0,   6000,  1, '工坊穹顶 32×28m 高18'),
    'mill':                ( 13.0,   4000,  1, '水车坊 16×12m 高13'),
    'barn':                ( 10.0,   3500,  1, '谷仓 20×12m 高10'),
    'workshop_shed':       (  8.0,   3000,  1, '工坊棚 20×14m 高8'),
    'dock_house':          (  8.0,   3000,  1, '码头屋 18×12m 高8'),
    'tea_house':           ( 10.0,   3000,  1, '茶铺 16×12m 高10'),
    'village_house_a':     (  9.0,   3000,  2, '民居A 14×12m 高9'),
    'village_house_b':     (  8.0,   3000,  2, '民居B 16×12m 高8'),
    'village_house_c':     (  7.0,   2500,  1, '民居C 10×8m 高7'),
    'pavilion_hall':       (  9.0,   3000,  1, '亭堂 14×14m 高9'),
    'post_house':          (  8.0,   2500,  1, '邮亭 12×10m 高8'),
    'homecoming_bell':     (  6.0,   4000,  1, '归航钟'),
    'astrolabe':           (  4.5,   4000,  1, '观星镜'),
    'wind_beacon':         (  7.0,   3500,  1, '风标装置'),
    'lookout_tower':       (  6.0,   3500,  1, '瞭望台'),
    'duck_statue':         (  2.6,   2500,  1, '鸭子雕像'),
    'cave_rock':           (  5.0,   3000,  1, '洞窟岩'),
    'wind_pillar':         (  5.5,   2500,  2, '风柱'),
    'memorial_stele':      (  3.0,   2500,  2, '纪念碑'),
    'chime_rack':          (  3.6,   2500,  2, '风铃架'),
    'pavilion_pillar':     (  4.5,   1200,  8, '亭柱'),
    'tree_green':          (  9.0,   1500, 40, '常绿树'),
    'tree_gold':           (  8.5,   1500, 24, '金叶树'),
    'tree_blossom':        (  8.0,   1500, 26, '花树'),
    'tree_olive':          (  8.5,   1500, 16, '橄榄叶树'),
    'cover_crates':        (  1.3,    600,  9, '货箱掩体'),
    'cover_stone':         (  1.2,    600,  9, '石垒掩体'),
}

# 替换批次：按 layout 登记的 **XZ 占地** 缩放，而不是按高度。
# 43×23 米的寺庙若按高度缩放会明显拉变形；而且视觉体量必须落在碰撞盒之内——
# 视觉大于碰撞盒会让玩家看着贴到墙上却还能走进去。
# 值取自 sky_island_layout.json 的 obstacle size，(宽X, 深Z, 高Y)。
FOOTPRINT = {
    'temple': (43.0, 23.0, 12.0), 'workshop_dome': (32.0, 28.0, 18.0),
    'barn': (20.0, 12.0, 10.0), 'workshop_shed': (20.0, 14.0, 8.0),
    'dock_house': (18.0, 12.0, 8.0), 'tea_house': (16.0, 12.0, 10.0),
    'mill': (16.0, 12.0, 13.0), 'village_house_a': (14.0, 12.0, 9.0),
    'village_house_b': (16.0, 12.0, 8.0), 'village_house_c': (10.0, 8.0, 7.0),
    'pavilion_hall': (14.0, 14.0, 9.0), 'post_house': (12.0, 10.0, 8.0),
}

# 白模（Tripo3D 未出贴图时）用的兜底调色板材质。带贴图的正式模型会改用自己的图集，
# 这里只保证缺贴图时也能进场景看轮廓和体量，不至于卡住验证。
FALLBACK_MATERIAL = {
    'bell_arch': 'RockLight', 'ruined_column': 'RockLight', 'stone_platform': 'Limestone',
    'stone_stair': 'Limestone', 'stone_bench': 'Limestone', 'crystal_fountain': 'Ivory',
    'shrine_pavilion': 'Limestone', 'cherry_pavilion': 'Limestone', 'root_rock': 'Rock',
    'rune_stone': 'Rock', 'crystal_cluster': 'CrystalTeal',
    'great_tree': 'Wood', 'cherry_tree': 'Wood', 'pergola': 'WoodLight',
    'farm_cart': 'WoodLight', 'crate_barrel': 'Wood', 'planter': 'Wood',
    'market_stall': 'WoodLight', 'banner_pole': 'Wood', 'street_lamp': 'WoodDark',
    'bell_tower': 'Limestone', 'cottage_large': 'Limestone', 'cottage_dormer': 'Limestone',
    'cottage_small': 'Limestone', 'farm_barn': 'WoodLight', 'shop_sign': 'Limestone',
    # 替换批次
    'temple': 'Limestone', 'workshop_dome': 'Limestone', 'mill': 'WoodLight',
    'barn': 'WoodLight', 'workshop_shed': 'WoodLight', 'dock_house': 'Limestone',
    'tea_house': 'Limestone', 'village_house_a': 'Limestone', 'village_house_b': 'Limestone',
    'village_house_c': 'Limestone', 'pavilion_hall': 'Limestone', 'post_house': 'Limestone',
    'homecoming_bell': 'Brass', 'astrolabe': 'Brass', 'wind_beacon': 'WoodLight',
    'lookout_tower': 'Limestone', 'duck_statue': 'RockLight', 'cave_rock': 'Rock',
    'wind_pillar': 'RockLight', 'memorial_stele': 'RockLight', 'chime_rack': 'Wood',
    'pavilion_pillar': 'RockLight', 'tree_green': 'Leaf', 'tree_gold': 'LeafGold',
    'tree_blossom': 'Blossom', 'tree_olive': 'Fern', 'cover_crates': 'Wood',
    'cover_stone': 'RockLight',
}

# 贴图分辨率按**屏幕占比**分档，不是按物件实际尺寸：路灯和旗杆虽高但很细，占不了几个像素。
# 贴图是逐模型一张、所有实例共用，因此实例数只影响面数预算，不影响贴图开销。
# 三档合计约 5.7MB（DXT1 含 mipmap），比一律 1024 的约 13MB 少一半以上。
TEXTURE_SIZE = {
    # 1024：一次性地标，画面里体量最大、离镜头最近
    'bell_arch': 1024, 'great_tree': 1024, 'bell_tower': 1024, 'crystal_fountain': 1024,
    # 512：建筑与中等道具，正常游戏机位下单件约占几百像素
    'cherry_pavilion': 512, 'cottage_large': 512, 'farm_barn': 512, 'cottage_dormer': 512,
    'cottage_small': 512, 'shop_sign': 512, 'shrine_pavilion': 512, 'stone_platform': 512,
    'market_stall': 512, 'cherry_tree': 512, 'ruined_column': 512, 'pergola': 512,
    'root_rock': 512, 'rune_stone': 512, 'crystal_cluster': 512,
    # 256：细长或矮小的高复用件，屏幕占比极低
    'banner_pole': 256, 'street_lamp': 256, 'farm_cart': 256, 'stone_stair': 256,
    'stone_bench': 256, 'crate_barrel': 256, 'planter': 256,
    # 替换批次
    'temple': 1024, 'workshop_dome': 1024, 'mill': 512, 'barn': 512, 'workshop_shed': 512,
    'dock_house': 512, 'tea_house': 512, 'village_house_a': 512, 'village_house_b': 512,
    'village_house_c': 512, 'pavilion_hall': 512, 'post_house': 512, 'homecoming_bell': 1024,
    'astrolabe': 1024, 'wind_beacon': 512, 'lookout_tower': 512, 'duck_statue': 256,
    'cave_rock': 512, 'wind_pillar': 256, 'memorial_stele': 256, 'chime_rack': 256,
    'pavilion_pillar': 256, 'tree_green': 512, 'tree_gold': 512, 'tree_blossom': 512,
    'tree_olive': 512, 'cover_crates': 256, 'cover_stone': 256,
}
DEFAULT_TEXTURE_SIZE = 512


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for item in list(block):
            block.remove(item, do_unlink=True)


def import_model(path):
    # Tripo3D 在 quad=true（Clean Topology）时强制输出 FBX，其余情况输出 GLB，两种都要能收。
    if path.suffix.lower() == '.fbx':
        bpy.ops.import_scene.fbx(filepath=str(path))
    else:
        bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        raise RuntimeError('模型文件内没有网格：' + str(path))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def normalise(obj, target_height, footprint=None):
    """把 Blender 的 Z-up 换成本管线的 Y-up，底面贴地、水平居中、按目标高度等比缩放。

    两个教训都写在这里：

    1. **不靠包围盒比例猜朝向。** 原先用 `if size[2] > size[1]` 判断竖直轴，对「宽大于高」的
       物件必然猜错。Blender 的 glTF / FBX 导入器一律把模型转成 Z-up，所以这里无条件换轴。
    footprint 给定时改按 XZ 占地等比装箱，用于替换已登记 footprint 的建筑。

    2. **不用 obj.bound_box，也不用 bpy.ops。** `bound_box` 在 transform_apply 之后不会立刻
       刷新（依赖 depsgraph），后台模式下读到的是过期值；算子又依赖选中/活动对象上下文。
       悬钟拱门第一次进场景整个躺倒、且按跨度而非高度缩放，就是这两件事叠加的结果。
       改用 mesh.transform() 和直接遍历顶点，完全确定、与上下文无关。
    """
    mesh = obj.data
    mesh.transform(obj.matrix_world)                  # 把物体变换烘进网格
    obj.matrix_world = Matrix.Identity(4)
    # Z-up -> Y-up: (x, y, z) -> (x, z, -y)
    mesh.transform(Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1))))

    coords = [v.co for v in mesh.vertices]
    if not coords:
        raise RuntimeError('模型没有顶点')
    lo = [min(c[i] for c in coords) for i in range(3)]
    hi = [max(c[i] for c in coords) for i in range(3)]
    height = hi[1] - lo[1]
    if height < 1e-6:
        raise RuntimeError('模型高度为零，无法归一化')
    if footprint is None:
        scale = target_height / height
        mesh.transform(Matrix.Diagonal((scale, scale, scale, 1.0)))
    else:
        span_x = max(hi[0] - lo[0], 1e-6)
        span_z = max(hi[2] - lo[2], 1e-6)
        # 先对齐长轴。模型的长边未必和 footprint 的长边同向：镜水寺登记 43×23（长边在 X），
        # 而生成的模型长边在 Z，直接装箱会被短边卡死，X 方向只填到 29%。差 90 度就转过来。
        if (span_x < span_z) != (footprint[0] < footprint[1]):
            mesh.transform(Matrix(((0, 0, 1, 0), (0, 1, 0, 0), (-1, 0, 0, 0), (0, 0, 0, 1))))
            span_x, span_z = span_z, span_x
        # 等比装箱保证不超出碰撞盒，再允许 X/Z 各自小幅拉伸把剩余空隙填掉。
        # 视觉小于碰撞盒会留下一圈看不见的墙，比轻微各向异性更难受；上限 1.35 倍，
        # 对这种风格化建筑几乎看不出变形。
        scale = min(footprint[0] / span_x, footprint[1] / span_z, footprint[2] / height)
        stretch_x = min(max(footprint[0] / (span_x * scale), 1.0), 1.35)
        stretch_z = min(max(footprint[1] / (span_z * scale), 1.0), 1.35)
        mesh.transform(Matrix.Diagonal((scale * stretch_x, scale, scale * stretch_z, 1.0)))

    coords = [v.co for v in mesh.vertices]
    lo = [min(c[i] for c in coords) for i in range(3)]
    hi = [max(c[i] for c in coords) for i in range(3)]
    mesh.transform(Matrix.Translation((-(hi[0] + lo[0]) / 2, -lo[1], -(hi[2] + lo[2]) / 2)))
    mesh.update()
    return obj


def ensure_uv(obj):
    """Tripo3D 的白模只有 POSITION/NORMAL，没有 UV。没有 UV 就既贴不了图集也贴不了平铺图，
    因此缺失时用 Smart UV Project 现场展开一套。已有 UV 的（带贴图的正式模型）原样保留。"""
    if obj.data.uv_layers:
        return False
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    return True


def decimate(obj, budget):
    triangles = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if triangles <= budget:
        return triangles
    modifier = obj.modifiers.new('TripoDecimate', 'DECIMATE')
    modifier.decimate_type = 'COLLAPSE'
    modifier.ratio = max(0.02, budget / float(triangles))
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def save_texture(obj, target, limit):
    """导出材质里的第一张 base color 贴图，缩到该件的分档上限以内。"""
    for slot in obj.material_slots:
        material = slot.material
        if material is None or not material.use_nodes:
            continue
        for node in material.node_tree.nodes:
            if node.type != 'TEX_IMAGE' or node.image is None:
                continue
            image = node.image
            width, height = image.size
            if max(width, height) > limit:
                scale = limit / float(max(width, height))
                image.scale(max(1, int(width * scale)), max(1, int(height * scale)))
            image.filepath_raw = str(target)
            image.file_format = 'PNG'
            image.save()
            return {'file': target.name, 'size': list(image.size)}
    return None


def extract(obj):
    """转成与 sky_island_life_models 一致的 {v, f, uv, smooth} 契约。

    注意 generate_sky_island.addmesh 的 UV 是**逐顶点**的，不是逐面角：
    `data['uv'].extend(uv or [(0,0)] * len(verts))`。而 Smart UV Project 会在缝处让同一个
    顶点带多组 UV，直接给逐面角 UV 会整体错位。因此这里按 (顶点索引, UV) 去重重建顶点表——
    缝上的顶点被拆开，缝内的顶点仍然共享，比无脑展开成散三角省下大量顶点。
    """
    mesh = obj.data
    mesh.calc_loop_triangles()
    uv_layer = mesh.uv_layers.active
    vertices, uvs, faces = [], [], []
    lookup = {}
    for tri in mesh.loop_triangles:
        face = []
        for index, loop in zip(tri.vertices, tri.loops):
            if uv_layer is None:
                uv = (0.0, 0.0)
            else:
                raw = uv_layer.data[loop].uv
                uv = (round(raw[0], 5), round(raw[1], 5))
            key = (index, uv)
            slot = lookup.get(key)
            if slot is None:
                slot = len(vertices)
                lookup[key] = slot
                co = mesh.vertices[index].co
                vertices.append((round(co.x, 4), round(co.y, 4), round(co.z, 4)))
                uvs.append(uv)
            face.append(slot)
        faces.append(face)
    return {'v': vertices, 'f': faces, 'uv': uvs, 'smooth': True}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--glb-dir', required=True)
    parser.add_argument('--project', required=True)
    parser.add_argument('--only')
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    args = parser.parse_args(argv)

    glb_dir = Path(args.glb_dir).resolve()
    project = Path(args.project).resolve()
    textures = project / 'Assets' / 'SkyIsland' / 'Textures'
    data_dir = project / 'ArtSource' / 'SkyIsland' / 'tripo'
    textures.mkdir(parents=True, exist_ok=True)
    data_dir.mkdir(parents=True, exist_ok=True)

    wanted = set(args.only.split(',')) if args.only else None
    manifest = []
    for name, (height, budget, instances, label) in ITEMS.items():
        if wanted is not None and name not in wanted:
            continue
        candidates = [glb_dir / (name + ext) for ext in ('.glb', '.fbx', '.GLB', '.FBX')]
        source = next((c for c in candidates if c.is_file()), None)
        if source is None:
            print('%-18s 缺少 GLB/FBX，跳过' % name)
            continue
        clear_scene()
        obj = import_model(source)
        before = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        normalise(obj, height, FOOTPRINT.get(name))
        after = decimate(obj, budget)
        unwrapped = ensure_uv(obj)
        limit = TEXTURE_SIZE.get(name, DEFAULT_TEXTURE_SIZE)
        texture = save_texture(obj, textures / ('tripo_' + name + '.png'), limit)
        payload = extract(obj)
        record_vertices = len(payload['v'])
        coords = [v.co for v in obj.data.vertices]
        xs = [c[0] for c in coords]; ys = [c[1] for c in coords]; zs = [c[2] for c in coords]
        record = {'name': name, 'label': label, 'height': height, 'instances': instances,
                  'trianglesBefore': before, 'triangles': after,
                  'sceneTriangles': after * instances, 'source': source.name,
                  'uvGenerated': unwrapped, 'material': FALLBACK_MATERIAL.get(name, 'RockLight'),
                  'vertices': record_vertices, 'textureLimit': limit,
                  'footprint': FOOTPRINT.get(name),
                  'bounds': {'min': [min(xs), min(ys), min(zs)], 'max': [max(xs), max(ys), max(zs)]},
                  'texture': texture}
        (data_dir / (name + '.json')).write_text(
            json.dumps({'meta': record, 'mesh': payload}, ensure_ascii=False), encoding='utf-8')
        manifest.append(record)
        print('%-18s %-12s %6d -> %5d 面 x%2d 例 = %6d  高 %.1fm  贴图 %s%s'
              % (name, label, before, after, instances, after * instances, height,
                 texture['size'] if texture else '无(用 ' + record['material'] + ' 兜底)',
                 '  UV已展开' if unwrapped else ''))

    (data_dir / 'manifest.json').write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SKY_ISLAND_TRIPO_IMPORT_OK %d 件  单件合计 %d 面  按实例数入场景合计 %d 面'
          % (len(manifest), sum(m['triangles'] for m in manifest),
             sum(m['sceneTriangles'] for m in manifest)))


if __name__ == '__main__':
    main()
