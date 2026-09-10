"""把 sky_island_tripo_import.py 产出的 Tripo3D 模型摆进天空岛世界。

只做装饰：全部避开可行走面与导航网格。导航顶点现为 4037 / 4095，余量只剩 58，
本模块一个顶点都不往导航里加。避让复用 sky_island_dressing.PlantingSpace 的 free()，
它已经覆盖岛缘、障碍物、标记点、桥头、路径与中心广场净空。

几何来自 ArtSource/SkyIsland/tripo/<name>.json，与 sky_island_life_models 同一套
{v, f, uv, smooth} 契约，因此直接并入主 FBX，不新增 AssetBundle、不改 C#。
"""

import json
import math
from pathlib import Path
import random

TAU = math.tau

# name: [(岛 id, 相对岛心的方位角 度, 半径 米, 自转 度), ...]
# 方位角与半径把地标摆在中心广场净空之外、又仍在主视线里；半径不够时按螺旋外扩找空位。
ANCHORS = {
    'bell_arch':        [('H', 205, 44, 25)],
    'great_tree':       [('D',  38, 27, 0), ('D', 226, 31, 140)],
    'crystal_fountain': [('B', 298, 27, 0)],
    'bell_tower':       [('B',  62, 25, 0)],
    'cherry_pavilion':  [('F', 130, 26, 20), ('S1', 40, 14, 0)],
    'cottage_large':    [('B', 150, 26, 90), ('C', 300, 24, 0), ('F', 220, 27, 45)],
    'farm_barn':        [('C',  70, 26, 0), ('C', 200, 28, 90)],
    'cottage_dormer':   [('B', 210, 27, 30), ('B', 340, 26, 200), ('C', 140, 25, 0), ('F', 60, 25, 120)],
    'market_stall':     [('B', 100, 24, 0), ('B', 250, 24, 60), ('A', 45, 18, 0)],
    'stone_platform':   [('H',  20, 40, 0), ('G', 160, 24, 0)],
    'cottage_small':    [('B',  30, 28, 0), ('C', 250, 26, 45), ('F', 300, 26, 0),
                         ('G',  90, 24, 20), ('A', 200, 20, 0)],
    'shop_sign':        [('B', 130, 23, 0), ('B', 300, 23, 90), ('A', 120, 17, 0)],
    'shrine_pavilion':  [('F', 260, 25, 0), ('D', 300, 24, 0), ('S3', 60, 12, 0)],
    'cherry_tree':      [('B', a, 30, 0) for a in (15, 75, 195, 315)]
                        + [('F', a, 29, 0) for a in (40, 150, 260, 340)]
                        + [('S1', a, 15, 0) for a in (90, 270)]
                        + [('C', a, 30, 0) for a in (110, 330)],
    'pergola':          [('C',  30, 27, 0), ('C', 170, 27, 90), ('S1', 200, 13, 0)],
    'ruined_column':    [('H', a, 41, 0) for a in (60, 130, 250, 320)] + [('G', a, 25, 0) for a in (40, 220)],
    'root_rock':        [('D', a, 29, 0) for a in (0, 90, 180, 270)] + [('S2', a, 13, 0) for a in (45, 225)]
                        + [('E', a, 33, 0) for a in (70, 250)],
    'farm_cart':        [('C', 210, 25, 0), ('A', 300, 18, 45), ('G', 250, 23, 0)],
    'crystal_cluster':  [('D', a, 26, 0) for a in (55, 145, 235, 325)] + [('G', a, 23, 0) for a in (70, 190, 310)]
                        + [('S4', a, 13, 0) for a in (0, 120, 240)],
    'rune_stone':       [('D', a, 31, 0) for a in (20, 200)] + [('H', a, 43, 0) for a in (100, 280)]
                        + [('S2', a, 14, 0) for a in (150, 330)],
    'stone_stair':      [('H', a, 38, 0) for a in (160, 340)] + [('F', a, 24, 0) for a in (0, 180)]
                        + [('G', a, 22, 0) for a in (110, 290)],
    'street_lamp':      [('B', a, 24, 0) for a in (0, 45, 90, 135, 180, 225, 270, 315)]
                        + [('A', a, 16, 0) for a in (0, 120, 240)]
                        + [('F', a, 23, 0) for a in (60, 180, 300)],
    'stone_bench':      [('B', a, 26, 0) for a in (20, 200)] + [('F', a, 24, 0) for a in (100, 280)],
    'banner_pole':      [('A', a, 19, 0) for a in (60, 300)] + [('B', a, 29, 0) for a in (110, 290)]
                        + [('E', a, 32, 0) for a in (0, 180)] + [('H', a, 45, 0) for a in (140, 320)],
    'crate_barrel':     [('A', a, 17, 0) for a in (30, 150, 270)] + [('B', a, 25, 0) for a in (70, 250)]
                        + [('C', a, 26, 0) for a in (0, 180)] + [('G', a, 22, 0) for a in (40, 160, 280)]
                        + [('F', a, 25, 0) for a in (140, 320)],
    'planter':          [('B', a, 23, 0) for a in (25, 95, 165, 235, 305)]
                        + [('F', a, 22, 0) for a in (30, 150, 270)]
                        + [('C', a, 24, 0) for a in (60, 240)]
                        + [('A', a, 15, 0) for a in (90, 270)]
                        + [('G', a, 21, 0) for a in (0, 180)],
}


# ── 替换程序化低模 ──────────────────────────────────────────────────────
#
# 与 ANCHORS 的新增件不同，这里是**取代**已有的程序化几何。关键约束：
#   * `collision_box()` 在生成器里先于本模块调用，读的是 layout 登记的 center/size，
#     因此换外观不影响碰撞，也不影响导航——一个导航顶点都不会变。
#   * 建筑走 footprint 缩放（见 sky_island_tripo_import.FOOTPRINT），模型完全装进碰撞盒内；
#     视觉大于碰撞盒会让玩家看着贴墙却能走进去。
#
# obstacle kind -> Tripo 模型名。列表表示多个变体轮换，按出现顺序取模。
OBSTACLE_REPLACEMENT = {
    'temple': ['temple'], 'dome': ['workshop_dome'], 'mill': ['mill'], 'barn': ['barn'],
    'workshop_shed': ['workshop_shed'], 'dock_house': ['dock_house'], 'tea_house': ['tea_house'],
    'pavilion': ['pavilion_hall'], 'post_house': ['post_house'],
    'house': ['village_house_a', 'village_house_b', 'village_house_c'],
    'cover': ['cover_crates', 'cover_stone'],
    'wind_beacon': ['wind_beacon'], 'astrolabe': ['astrolabe'], 'bell': ['homecoming_bell'],
    'lookout': ['lookout_tower'], 'duck_statue': ['duck_statue'], 'cave_rock': ['cave_rock'],
    'wind_pillar': ['wind_pillar'], 'memorial': ['memorial_stele'],
    'chime_support': ['chime_rack'], 'pavilion_support': ['pavilion_pillar'],
}

# landscape_scatter 的 106 棵程序化树按区域分四类叶色，与 tree() 原本的分支一致。
TREE_VARIANTS = {'gold': 'tree_gold', 'blossom': 'tree_blossom',
                 'moonleaf': 'tree_olive', 'plain': 'tree_green'}

_CACHE = {}
_KIND_SEEN = {}


def _model(g, data_dir, name):
    """读一次缓存起来；同一模型会被摆几十次，不能每次都解析 JSON。"""
    if name not in _CACHE:
        payload = load(data_dir, name)
        if payload is not None:
            key = material_name(name)
            payload = (payload,
                       key if key in getattr(g, 'MODEL_TEXTURES', {})
                       else payload['meta'].get('material', 'RockLight'))
        _CACHE[name] = payload
    return _CACHE[name]


def reset():
    """每次完整生成前清空，避免跨次运行串味。"""
    _CACHE.clear()
    _KIND_SEEN.clear()


def _stamp(g, entry, x, base_y, z, yaw_deg, scale=1.0):
    payload, material = entry
    mesh = payload['mesh']
    yaw = math.radians(yaw_deg)
    cosine, sine = math.cos(yaw), math.sin(yaw)
    vertices = [(x + (vx * cosine + vz * sine) * scale, base_y + vy * scale,
                 z + (-vx * sine + vz * cosine) * scale) for vx, vy, vz in mesh['v']]
    g.addmesh(material, vertices, mesh['f'], mesh['uv'], mesh.get('smooth', True))
    return len(mesh['f'])


def replace_obstacle(g, obs, data_dir, island_centre=None):
    """有对应 Tripo 件就摆上并返回 True，生成器据此跳过原来的程序化绘制。"""
    names = OBSTACLE_REPLACEMENT.get(obs['kind'])
    if not names:
        return False
    index = _KIND_SEEN.get(obs['kind'], 0)
    _KIND_SEEN[obs['kind']] = index + 1
    entry = _model(g, data_dir, names[index % len(names)])
    if entry is None:
        return False
    x, y, z = obs['center']
    centre = island_centre or (x, z)
    yaw = resolve_yaw(names[index % len(names)], x, z, centre[0], centre[1],
                      hash((obs['id'],)) & 0xffffffff)
    _stamp(g, entry, x, y - obs['size'][1] / 2.0, z, yaw)
    return True


def replace_tree(g, data_dir, x, y, z, scale, variant, seed):
    """取代 landscape_scatter 的程序化树；随机偏航与缩放避免同一变体看出重复。"""
    entry = _model(g, data_dir, TREE_VARIANTS.get(variant, 'tree_green'))
    if entry is None:
        return False
    rng = random.Random(seed)
    _stamp(g, entry, x, y, z, rng.uniform(0, 360), scale * rng.uniform(.85, 1.18))
    return True


# ── 第二轮：程序化散件替换（2026-09-10）──────────────────────────────
#
# 与第一轮的「替换障碍物 / 散布树」不同，这一批是高复用散件：灌木、岛缘石块、垂藤、远景岛……
# 一个调用点要摆几十上百份。统一走 stamp_variant()：
#   * 从候选变体里只挑**已经导入**的；一个都没有就返回 False，调用方退回原来的程序化绘制。
#     素材分批回来时每回来一件就少一类程序化几何，中间任何时刻重建都不会出现空洞。
#   * 按目标**高度**缩放（与 replace_tree 同口径）。
#   * 变体与偏航用 stable_rng：内置 hash() 对字符串按进程随机化，Blender 里同一份 layout
#     两次重建会摆出不同的朝向和变体，打包前后对比就失去意义。
ROUND2_MODELS = (
    'bush_a', 'bush_b', 'bush_c', 'cliff_chunk_a', 'cliff_chunk_b', 'cliff_chunk_c',
    'cliff_shrub_cap', 'cliff_vine', 'rock_a', 'rock_b', 'flower_patch', 'lavender_clump',
    'fern_clump', 'coral_clump', 'distant_islet_a', 'distant_islet_b', 'distant_islet_c',
    'mushroom_cluster', 'glow_crystal', 'brass_lamp_post', 'brass_railing_module', 'waterfall',
)


def stable_rng(*key):
    import zlib
    return random.Random(zlib.crc32(repr(key).encode('utf-8')))


def stamp_variant(g, data_dir, names, x, base_y, z, height, key, yaw=None):
    """从 names 里挑一个已导入的变体，底面放在 base_y、按目标高度等比缩放。返回是否摆了。"""
    if data_dir is None:
        return False
    available = [name for name in names if _model(g, data_dir, name) is not None]
    if not available:
        return False
    rng = stable_rng('variant', key)
    entry = _model(g, data_dir, available[rng.randrange(len(available))])
    bounds = entry[0]['meta']['bounds']
    model_height = max(bounds['max'][1] - bounds['min'][1], 1e-3)
    _stamp(g, entry, x, base_y, z, rng.uniform(0, 360) if yaw is None else yaw, height / model_height)
    return True


# 有正面的件要朝向岛心（广场方向），其余用确定性随机偏航打散。
# 约定与 sky_island_life_models 一致：模型的正面朝 -Z。
FACE_CENTRE = {
    'temple', 'workshop_dome', 'mill', 'barn', 'workshop_shed', 'dock_house', 'tea_house',
    'village_house_a', 'village_house_b', 'village_house_c', 'pavilion_hall', 'post_house',
    'lookout_tower', 'cave_rock', 'chime_rack', 'memorial_stele',
    'cottage_large', 'cottage_dormer', 'cottage_small', 'farm_barn', 'shop_sign',
    'cherry_pavilion', 'shrine_pavilion', 'market_stall', 'pergola', 'stone_bench',
    'farm_cart', 'crate_barrel', 'planter', 'stone_stair', 'bell_arch', 'crystal_fountain',
}
# 自然物与柱状物没有正面，固定角度反而看出重复。
FREE_YAW = {'tree_green', 'tree_gold', 'tree_blossom', 'tree_olive', 'cherry_tree',
            'great_tree', 'root_rock', 'crystal_cluster', 'rune_stone', 'ruined_column',
            'wind_pillar', 'pavilion_pillar', 'duck_statue', 'cover_crates', 'cover_stone'}


def facing_yaw(x, z, cx, cz):
    """返回让模型正面(-Z)指向 (cx, cz) 的偏航角（度）。

    _stamp 的旋转下，正面世界方向是 (-sin(yaw), -cos(yaw))，因此
    yaw = atan2(-dx, -dz)。建筑背对广场是最容易被一眼看出的摆放缺陷。
    """
    dx, dz = cx - x, cz - z
    if abs(dx) < 1e-6 and abs(dz) < 1e-6:
        return 0.0
    return math.degrees(math.atan2(-dx, -dz))


def resolve_yaw(name, x, z, cx, cz, seed, fallback=0.0):
    if name in FACE_CENTRE:
        return facing_yaw(x, z, cx, cz)
    if name in FREE_YAW:
        return random.Random(seed).uniform(0, 360)
    return fallback


def material_name(name):
    """Tripo 件的专属材质名。SkyIslandBundleBuilder 按名字子串特判：含 Crystal 会被当水晶
    处理（略高光泽 + 轻微呼吸自发光），对水晶喷泉和水晶簇正好合适，因此不回避这个碰撞。"""
    return 'Tripo' + ''.join(part.capitalize() for part in name.split('_'))


# ── 碰撞（CR-2026-09-10-007）────────────────────────────────────────────
#
# 2026-09-10 实机反馈「有些模型能穿过去，有些则有空气墙」，实测两个成因：
#
# ① **空气墙**：替换件的碰撞盒来自 layout 里登记的**名义尺寸**，而模型被 `FOOTPRINT`
#    归一化后往往远小于那个盒。实测单边空隙中位 1.80 m，最狠的归航钟是 **14.8 m**
#    （盒 34×26，模型只有 4.4×6.0）——玩家离钟十几米就被挡住。
#    修法是 `collision_fit()`：把盒收敛到模型旋转后的真实投影，**只缩不放**。
#    导航是按 `layout['obstacles']` 的名义尺寸挖的，这里不动那份数据，所以导航零影响。
#
# ② **穿模**：ANCHORS 附加件是**新增**几何，layout 里没有对应障碍，因此一个碰撞盒都没有。
#    下面按件给策略补上。**只给锚点分支**（`free()` 严格避让、离路远），语义分支
#    （路缘 / 墙角的路灯、长椅、旗杆、花箱、桶箱、板车）一律不出碰撞——那些正好贴着
#    敌人要走的路，而导航网格已经 4037/4095 顶点、没有余量重新挖洞。
#
# 策略取值：
#   'footprint'   —— 按模型真实投影出盒（建筑、亭子、平台、大石这类实体）
#   ('trunk', r)  —— 只挡树干/柱心，半径 r 米。树冠**不能**挡，否则整片树荫都是空气墙
#   不登记        —— 不出碰撞（可以走上去的石阶、贴地装饰、语义分支的小件）
COLLISION_POLICY = {
    'bell_arch': 'footprint', 'crystal_fountain': 'footprint', 'bell_tower': 'footprint',
    'cherry_pavilion': 'footprint', 'shrine_pavilion': 'footprint', 'pergola': 'footprint',
    'cottage_large': 'footprint', 'cottage_dormer': 'footprint', 'cottage_small': 'footprint',
    'farm_barn': 'footprint', 'market_stall': 'footprint', 'stone_platform': 'footprint',
    'root_rock': 'footprint', 'crystal_cluster': 'footprint',
    'great_tree': ('trunk', 1.8), 'cherry_tree': ('trunk', 1.2),
    'ruined_column': ('trunk', 0.9), 'rune_stone': ('trunk', 0.9), 'shop_sign': ('trunk', 0.35),
    # 'stone_stair' 故意不登记：台阶要能走上去。
}

# 盒子往内收一点，宁可留一条能贴着走的缝，也不要在模型外面再造一圈看不见的墙。
COLLISION_INSET = 0.15


def rotated_extent(bounds, yaw_deg):
    """模型 XZ 投影绕 Y 轴转 yaw 之后的轴对齐半宽。"""
    half_x = (bounds['max'][0] - bounds['min'][0]) / 2.0
    half_z = (bounds['max'][2] - bounds['min'][2]) / 2.0
    cosine = abs(math.cos(math.radians(yaw_deg)))
    sine = abs(math.sin(math.radians(yaw_deg)))
    return half_x * cosine + half_z * sine, half_x * sine + half_z * cosine


def collision_fit(obs, data_dir, island_centre=None):
    """替换件的碰撞盒尺寸：收敛到模型真实投影，**只缩不放**。没有替换件返回 None。

    这里的 `_KIND_SEEN` 只**读不写**——真正推进计数的是随后的 `replace_obstacle`，
    两者在生成器的同一次循环里成对调用，索引因此一致。
    """
    names = OBSTACLE_REPLACEMENT.get(obs['kind'])
    if not names:
        return None
    name = names[_KIND_SEEN.get(obs['kind'], 0) % len(names)]
    payload = load(data_dir, name)
    if payload is None:
        return None
    x, y, z = obs['center']
    centre = island_centre or (x, z)
    yaw = resolve_yaw(name, x, z, centre[0], centre[1], hash((obs['id'],)) & 0xffffffff)
    extent_x, extent_z = rotated_extent(payload['meta']['bounds'], yaw)
    width, height, depth = obs['size']
    return [min(width, extent_x * 2), height, min(depth, extent_z * 2)]


def emit_collision(g, name, index, x, base_y, z, yaw_deg, bounds):
    """按 COLLISION_POLICY 给附加件补一个碰撞盒；不登记的件什么都不做。"""
    policy = COLLISION_POLICY.get(name)
    if not policy:
        return False
    low, high = base_y + bounds['min'][1], base_y + bounds['max'][1]
    if isinstance(policy, tuple):
        extent_x = extent_z = policy[1]
    else:
        extent_x, extent_z = rotated_extent(bounds, yaw_deg)
        extent_x = max(extent_x - COLLISION_INSET, 0.25)
        extent_z = max(extent_z - COLLISION_INSET, 0.25)
    g.collision_box('Tripo_%s_%02d' % (name, index),
                    (x, (low + high) / 2.0, z),
                    (extent_x * 2, max(high - low, 0.6), extent_z * 2))
    return True


def register(g, data_dir):
    """把已备好的 Tripo 件登记成非平铺贴图材质，必须在 build_materials 之前调用。

    带贴图的走 MODEL_TEXTURES（保留模型自带的图集 UV）；没贴图的白模不登记，
    build() 会退回 FALLBACK_MATERIAL 的调色板色，只保证能看体量。
    """
    registered = {}
    directory = Path(data_dir)
    if not directory.is_dir():
        return registered
    # 必须同时覆盖新增件(ANCHORS)与替换件(OBSTACLE_REPLACEMENT / TREE_VARIANTS)。
    # 只遍历 ANCHORS 会让替换件静默退回调色板兜底色——几何进得去、贴图却丢了，
    # 表现为「面数涨了但 Tripo 材质数没变」，很难一眼看出。
    names = list(ANCHORS)
    for variants in OBSTACLE_REPLACEMENT.values():
        names.extend(variants)
    names.extend(TREE_VARIANTS.values())
    # 第二轮散件同理：漏登记就是「几何进了、贴图丢了」，几百份灌木会整片退回调色板纯色。
    names.extend(ROUND2_MODELS)
    for name in dict.fromkeys(names):
        payload = load(directory, name)
        if payload is None:
            continue
        texture = payload['meta'].get('texture')
        if not texture:
            continue
        key = material_name(name)
        g.PALETTE[key] = '#ffffff'          # 白底，颜色全部来自贴图
        g.MODEL_TEXTURES[key] = 'Textures/' + texture['file']
        registered[name] = key
    return registered


def load(data_dir, name):
    path = Path(data_dir) / (name + '.json')
    if not path.is_file():
        return None
    return json.loads(path.read_text(encoding='utf-8'))


def find_spot(space, cx, cz, angle_deg, radius, clearance):
    """从首选点开始按螺旋外扩找一个 free() 通过的位置，找不到就放弃这一处。"""
    for step in range(44):
        r = radius + step * 2.4
        a = math.radians(angle_deg + step * 11)
        x, z = cx + r * math.cos(a), cz + r * math.sin(a)
        if space.free(x, z, clearance):
            return x, z
    return None


def build(g, layout, data_dir, dressing):
    """把 tripo 目录里所有已备好的模型摆进世界，返回台账。"""
    islands = {i['id']: i for i in layout['islands']}
    spaces = {i['id']: dressing.PlantingSpace(g, layout, i) for i in layout['islands']}
    placements, counts, triangles = [], {}, 0

    for name, anchors in ANCHORS.items():
        payload = load(data_dir, name)
        if payload is None:
            continue
        meta, mesh = payload['meta'], payload['mesh']
        # 有贴图就用专属图集材质，没有则退回调色板兜底色。
        key = material_name(name)
        material = key if key in getattr(g, 'MODEL_TEXTURES', {}) else meta.get('material', 'RockLight')
        bounds = meta['bounds']
        # 用真实包围盒半径当避让半径，别用目标高度——横向铺开的件（拱门、巨树）比高度宽得多。
        clearance = max(bounds['max'][0] - bounds['min'][0], bounds['max'][2] - bounds['min'][2]) / 2 + 1.5
        placed = 0
        strategy = PLACEMENT.get(name)
        if strategy is not None:
            # 语义摆放：名额沿用 ANCHORS 的条目数，位置改由路缘/墙角产出。
            # 按岛轮转取点，避免全挤在第一个岛上。
            wanted = len(anchors)
            island_ids = list(dict.fromkeys(a[0] for a in anchors if a[0] in islands))
            pools = [(iid, semantic_spots(g, layout, islands[iid], name, clearance) or []) for iid in island_ids]
            cursor = {iid: 0 for iid, _ in pools}
            while placed < wanted:
                progressed = False
                for island_id, spots in pools:
                    if placed >= wanted:
                        break
                    space = spaces[island_id]
                    island = islands[island_id]
                    while cursor[island_id] < len(spots):
                        x, z, ax, az = spots[cursor[island_id]]
                        cursor[island_id] += 1
                        if not free_relaxed(dressing, space, x, z, clearance,
                                            near_path=strategy[0] == 'roadside',
                                            near_building=strategy[0] == 'building'):
                            continue
                        # 路缘件正面朝路，墙角件正面背向建筑。
                        if strategy[0] == 'roadside':
                            yaw_deg = facing_yaw(x, z, ax, az)
                        else:
                            yaw_deg = facing_yaw(x, z, 2 * x - ax, 2 * z - az)
                        yaw = math.radians(yaw_deg)
                        cosine, sine = math.cos(yaw), math.sin(yaw)
                        g.CURRENT = island_id
                        cy = island['center'][1]
                        vertices = [(x + vx * cosine + vz * sine, cy + vy, z - vx * sine + vz * cosine)
                                    for vx, vy, vz in mesh['v']]
                        g.addmesh(material, vertices, mesh['f'], mesh['uv'], mesh.get('smooth', True))
                        triangles += len(mesh['f'])
                        placed += 1
                        progressed = True
                        placements.append({'model': name, 'island': island_id,
                                           'position': [round(x, 2), round(cy, 2), round(z, 2)],
                                           'yaw': round(yaw_deg, 1), 'material': material,
                                           'placement': strategy[0]})
                        break
                if not progressed:
                    break
            if placed:
                counts[name] = placed
            continue
        for island_id, angle_deg, radius, yaw_deg in anchors:
            island = islands.get(island_id)
            if island is None:
                continue
            space = spaces[island_id]
            cx, cy, cz = island['center']
            spot = find_spot(space, cx, cz, angle_deg, radius, clearance)
            if spot is None:
                continue
            x, z = spot
            yaw_deg = resolve_yaw(name, x, z, cx, cz, hash((name, island_id, angle_deg)) & 0xffffffff, yaw_deg)
            yaw = math.radians(yaw_deg)
            cosine, sine = math.cos(yaw), math.sin(yaw)
            g.CURRENT = island_id
            vertices = [(x + vx * cosine + vz * sine, cy + vy, z - vx * sine + vz * cosine)
                        for vx, vy, vz in mesh['v']]
            g.addmesh(material, vertices, mesh['f'], mesh['uv'], mesh.get('smooth', True))
            triangles += len(mesh['f'])
            # 只有锚点分支补碰撞：这一支走 `free()` 严格避让，离敌人要走的路最远。
            # 语义分支（路缘 / 墙角）不补，理由见 COLLISION_POLICY 上方的注释。
            solid = emit_collision(g, name, placed, x, cy, z, yaw_deg, bounds)
            placed += 1
            placements.append({'model': name, 'island': island_id,
                               'position': [round(x, 2), round(cy, 2), round(z, 2)],
                               'yaw': round(yaw_deg, 1), 'material': material,
                               'collision': bool(solid)})
        if placed:
            counts[name] = placed

    return {'version': 1, 'classification': 'COMPAT',
            'description': 'Tripo3D image-to-3D hero props, decorative only, outside navigation',
            'models': counts, 'instances': sum(counts.values()), 'triangles': triangles,
            'placementPolicy': {'navigation': 'untouched; decorative geometry only',
                                'avoidance': 'sky_island_dressing.PlantingSpace.free()',
                                'clearance': 'model bounding radius + 1.5m'},
            'placements': placements}

# ── 语义化摆放 ─────────────────────────────────────────────────────────
#
# 螺旋搜索只保证「不撞东西」，结果是道具散落在空草坪中间，读起来像随机撒的。
# 生产级摆放要让道具附着到它本该附着的东西上：长椅和路灯贴路缘、花箱和桶箱堆在
# 建筑墙角。这里按模型给策略，找不到语义位就回落到原来的 ANCHORS 螺旋搜索。
#
# name -> (策略, 参数)
PLACEMENT = {
    'street_lamp':  ('roadside', {'spacing': 30.0, 'margin': 1.4}),
    'stone_bench':  ('roadside', {'spacing': 46.0, 'margin': 2.2}),
    'banner_pole':  ('roadside', {'spacing': 62.0, 'margin': 2.6}),
    'planter':      ('building', {'margin': 1.5}),
    'crate_barrel': ('building', {'margin': 2.0}),
    'farm_cart':    ('building', {'margin': 3.0}),
}

# 墙角堆放时要跳过的障碍类型：掩体本身是道具，生活道具已由 settlement 摆过。
_SKIP_KINDS = {'cover', 'life_prop', 'pool', 'water'}


def roadside_spots(g, island, spacing, margin):
    """沿本岛的铺装路缘等距取点，左右交替。返回 (x, z, 路心x, 路心z)。"""
    cx, _, cz = island['center']
    half_x, half_z = island['size'][0] / 2.0, island['size'][1] / 2.0
    spots = []
    for points, width in getattr(g, 'PAVING_TRACKS', []):
        travelled = 0.0
        side = 1
        for a, b in zip(points, points[1:]):
            if abs(a[0] - cx) > half_x or abs(a[1] - cz) > half_z:
                continue
            dx, dz = b[0] - a[0], b[1] - a[1]
            length = math.hypot(dx, dz)
            if length < 1e-6:
                continue
            travelled += length
            if travelled < spacing:
                continue
            travelled = 0.0
            # 路的法线方向偏移出去，交替换边，避免全挤在一侧。
            nx, nz = -dz / length, dx / length
            offset = width / 2.0 + margin
            mx, mz = (a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0
            spots.append((mx + nx * offset * side, mz + nz * offset * side, mx, mz))
            side = -side
    return spots


def building_spots(layout, island, margin):
    # margin 由调用方加上道具自身的避让半径后传入，否则角点两轴都会落进拒绝区。
    """建筑四角外侧的堆放位。返回 (x, z, 建筑x, 建筑z)。"""
    spots = []
    for obs in layout['obstacles']:
        if obs.get('island') != island['id'] or obs['kind'] in _SKIP_KINDS:
            continue
        x, _, z = obs['center']
        w, _, d = obs['size']
        if max(w, d) < 6.0:            # 太小的不算建筑，墙角贴不出效果
            continue
        for sx, sz in ((1, 1), (-1, 1), (-1, -1), (1, -1)):
            spots.append((x + sx * (w / 2.0 + margin), z + sz * (d / 2.0 + margin), x, z))
    return spots


def free_relaxed(dressing, space, x, z, radius, near_path=False, near_building=False):
    """语义摆放专用的放宽校验。

    PlantingSpace.free() 是为「远离路径撒植被」设计的：它把路缘和建筑周围整片排除掉，
    而贴路、贴墙正是这里想要的效果，直接用会 100% 拒绝。所以这里保留真正的硬约束
    （岛缘、标记点、桥头净空、不与建筑实体重叠），只放开被刻意排除的那两项。
    """
    point = (x, z)
    if not dressing.inside(point, space.outline):
        return False
    if any(dressing.distance_to_segment(point, a, b) < radius + 2
           for a, b in zip(space.outline, space.outline[1:] + space.outline[:1])):
        return False
    if any(math.hypot(x - m[0], z - m[2]) < radius + 3.8 for m in space.markers):
        return False
    if any(math.hypot(x - q[0], z - q[2]) < r + radius for q, r in space.portals):
        return False
    for o in space.obstacles:
        # 贴墙时只拒绝真正的实体重叠，不再加 1 米余量。
        pad = 0.0 if near_building else 1.0
        if (abs(x - o['center'][0]) < o['size'][0] / 2 + radius + pad
                and abs(z - o['center'][2]) < o['size'][2] / 2 + radius + pad):
            return False
    if not near_path and any(dressing.distance_to_segment(point, a, b) < w + radius
                             for a, b, w in space.paths):
        return False
    return True


def semantic_spots(g, layout, island, name, clearance=0.0):
    """按策略产出候选位；没有策略的返回 None，交回螺旋搜索。"""
    entry = PLACEMENT.get(name)
    if entry is None:
        return None
    kind, opts = entry
    if kind == 'roadside':
        return roadside_spots(g, island, opts['spacing'], opts['margin'] + clearance)
    return building_spots(layout, island, opts['margin'] + clearance)

