"""复算当前潮闸镇包的补碰撞数据。坐标使用原型口径，发布时 z 与 yaw 取反。"""
import json
import math
import struct

from tidegate_collision_boxes import LOW_SOLID_SCATTER, building_obbs, scatter_parts


def scatter_bounds(path):
    raw = path.read_bytes()
    length, chunk_type = struct.unpack_from('<I4s', raw, 12)
    if raw[:4] != b'glTF' or chunk_type != b'JSON':
        raise ValueError(f'{path} 不是 glTF 二进制')
    doc = json.loads(raw[20:20 + length])
    bounds = {}
    for node in doc['nodes']:
        kind = node['name'].split('__')[0]
        if kind not in LOW_SOLID_SCATTER:
            continue
        for primitive in doc['meshes'][node['mesh']]['primitives']:
            accessor = doc['accessors'][primitive['attributes']['POSITION']]
            lo, hi = accessor['min'], accessor['max']
            if kind in bounds:
                prev_lo, prev_hi = bounds[kind]
                lo = [min(a, b) for a, b in zip(lo, prev_lo)]
                hi = [max(a, b) for a, b in zip(hi, prev_hi)]
            bounds[kind] = lo, hi
    return bounds


def legacy_parts(kit, ledger, layout, bounds):
    """只发给当前发布包缺失的结构；其它已发布家具的旧盒由资源重建逐步替换。"""
    dropped = tuple(name + '_' for name, _ in legacy_interactions(layout))
    out = []
    for row in building_obbs(kit):
        name = row['id']
        if name.startswith(dropped) or 'Details_' in name:
            out.append(row)
        elif row['kind'] == 'pipe':
            # 旧生成器以三位小数边界直接相减再比较，高度恰好 0.6 m 时复现旧浮点判据。
            _, building, _, index = name.split('_')
            box = kit['buildings'][building]['pipes'][int(index)]
            if box[4] - box[1] < .6:
                out.append(row)
    for kind in sorted(LOW_SOLID_SCATTER):
        lo, hi = bounds[kind]
        skipped = set(ledger['scatterThinned'].get(kind, []))
        shrink = LOW_SOLID_SCATTER[kind]
        for i, row in enumerate(layout['instances'][kind]):
            x, _, z, *_ = row
            if i not in skipped and -90 < x < 90 and -70 < z < 70 and (kind != 'rock2' or hi[1] * row[5] < .6):
                out.extend(scatter_parts(kind, i, row, lo, hi, shrink))
    return out


def legacy_interactions(layout):
    """旧包按标记点剔掉的 11 件家具，绑定到同一个布局中的既有交互 ID。"""
    pairs = (
        ('B_hall_furniture_8', 'LOOT_safe_11'),
        ('B_clinic_furniture_12', 'LOOT_med_18'),
        ('B_clinic_furniture_17', 'LOOT_med_19'),
        ('B_ctrl_furniture_0', 'LOOT_safe_68'),
        ('B_tide_furniture_1', 'LOOT_house_70'),
        ('B_tide_furniture_4', 'LOOT_tool_61'),
        ('B_tide_furniture_5', 'GP_valve_2'),
        ('B_vault_furniture_0', 'LOOT_vault_65'),
        ('B_vault_furniture_3', 'LOOT_vault_66'),
        ('B_vault_furniture_5', 'LOOT_vault_67'),
        ('B_vault_furniture_7', 'LOOT_vault_71'),
    )
    markers = {p['id'] for kind in ('containers', 'valves') for p in layout[kind]}
    if any(marker not in markers for _, marker in pairs):
        raise ValueError('原潮闸镇交互 ID 已变化，需复核旧包补实体')
    return pairs


def runtime_rows(parts):
    for row in parts:
        x, y, z = row['center']
        yaw = round(-math.degrees(row['yaw']), 3)
        prefix = 'COL_Wall_O_' if row['kind'] == 'wall' else 'COL_Prop_O_'
        values = ', '.join(f'{v:g}f' if v else '0f' for v in (x, y, -z, *row['size'], yaw))
        yield f'            new Part("{prefix}{row["id"]}", {values}),'
