"""潮闸镇建筑部件与角色脚点的离线碰撞判据，供 Blender 组装和实包验证共用。"""
import math


PLAYER_RADIUS = 0.3
PLAYER_HEIGHT = 1.0
PLAYER_STEP = 0.25

# 系缆桩的底座、柱身和帽沿宽度不同。其余近地道具用可见模型的低位包络。
BOLLARD_PARTS = (
    ((0, 0.025, 0), (0.54, 0.05, 0.54)),
    ((0, 0.24, 0), (0.368, 0.38, 0.368)),
    ((0, 0.505, 0), (0.42, 0.15, 0.42)),
)
LOW_SOLID_SCATTER = {'bollard': 1.0, 'driftwood': 0.85, 'lobsterTrap': 1.0,
                     'pallet': 1.0, 'tire': 0.9, 'rock2': 0.8}


def scatter_parts(kind, index, row, lo, hi, shrink):
    """按缩放与朝向放置逐件盒子。可走的薄物也保留其实际厚度。"""
    x, y, z, yaw, sx, sy, sz = row
    if kind == 'bollard':
        shapes = BOLLARD_PARTS
    else:
        bottom = max(lo[1], 0.0)
        shapes = [(((lo[0] + hi[0]) * 0.5, (bottom + hi[1]) * 0.5, (lo[2] + hi[2]) * 0.5),
                   ((hi[0] - lo[0]) * shrink, hi[1] - bottom, (hi[2] - lo[2]) * shrink))]
    c, s = math.cos(yaw), math.sin(yaw)
    out = []
    for part, (center, size) in enumerate(shapes):
        lx, ly, lz = center[0] * sx, center[1] * sy, center[2] * sz
        width, height, depth = size[0] * sx, size[1] * sy, size[2] * sz
        name = f'{kind}_{index}' if len(shapes) == 1 else f'{kind}_{index}_{part}'
        out.append(dict(id=name, kind=kind, source='instance',
                        center=[round(v, 3) for v in (x + lx*c + lz*s, y + ly, z - lx*s + lz*c)],
                        size=[round(v, 3) for v in (width, height, depth)],
                        yaw=round(yaw, 4), visibleHeight=round(hi[1] * sy, 3)))
    return out


def building_obbs(kit):
    if kit['schema'] != 2:
        raise ValueError('建筑部件清单需重建：node tools/build_tidegate_building_colliders.mjs')
    out = []
    for building, entry in kit['buildings'].items():
        categories = {'walls': 'wall', 'stairs': 'stair', 'pipes': 'pipe', 'furniture': 'furniture',
                      'wallDetails': 'wall', 'pipeDetails': 'pipe'}
        for group, kind in categories.items():
            for index, item in enumerate(entry[group]):
                name = f'B_{building}_{group if group.endswith("Details") else kind}_{index}'
                category = f'furniture:{item["name"]}' if kind == 'furniture' else kind
                if kind == 'furniture' and 'parts' in item:
                    for number, part in enumerate(item['parts']):
                        out.append(dict(part, id=f'{name}_{number}', kind=category, source='building',
                                        visibleHeight=part['size'][1]))
                    continue
                limits = item['box'] if kind == 'furniture' else item
                size = [round(limits[i + 3] - limits[i], 3) for i in range(3)]
                center = [round((limits[i + 3] + limits[i]) / 2, 3) for i in range(3)]
                out.append(dict(id=name, kind=category, source='building', center=center, size=size,
                                yaw=0.0, visibleHeight=size[1]))
    return out


def box_distance_sq(box, position, vertical_segment=0.0):
    """从竖直线段底端到其上 vertical_segment 的距离平方，单位为米。"""
    dx, dy, dz = (p - c for p, c in zip(position, box['center']))
    yaw = box['yaw']
    c, s = math.cos(yaw), math.sin(yaw)
    x, z = dx * c - dz * s, dx * s + dz * c
    sx, sy, sz = (v * 0.5 for v in box['size'])
    return max(0.0, abs(x) - sx)**2 + max(0.0, abs(z) - sz)**2 + max(0.0, dy - sy, -dy - sy - vertical_segment)**2


def blocks_player(box, foot):
    bottom = (foot[0], foot[1] + PLAYER_RADIUS, foot[2])
    return box_distance_sq(box, bottom, PLAYER_HEIGHT - 2 * PLAYER_RADIUS) < (PLAYER_RADIUS - 0.005)**2


def verify_standing_markers(layout, boxes, spawn_height=None):
    # 搜刮点和阀门标的是物体。敌人出生与路径节点吸附导航，不能以未经吸附的坐标删物件。
    spawn = layout['spawn']
    feet = [('PlayerSpawn', (spawn['x'], 0.8 if spawn_height is None else spawn_height, spawn['z']))]
    feet += [(exit['id'], exit['position']) for exit in layout['exits']]
    for name, foot in feet:
        hit = next((box for box in boxes if blocks_player(box, foot)), None)
        if hit:
            raise ValueError(f'{name} 角色站位被 {hit["id"]} 占用，请调整通行或点位')
