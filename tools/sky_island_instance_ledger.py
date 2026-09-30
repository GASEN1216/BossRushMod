"""天空岛生成器的实例台账（只审计，不改几何）。

整图生成时几何按「岛区 × 材质」合批，出了生成器就分不清哪几个三角形属于哪一件东西。
本模块在 `generate_sky_island.py --audit-ledger <目录>` 下挂到生成器上：

* 每个「一件东西」的发射函数（树、灯、长椅、Tripo 件、登记障碍、生活摆件、花草……）
  最外层调用记成一个实例，记录它在每个合批组里新增的顶点 / 面区间；
* 不属于任何发射函数、由编排函数直接画的零件（地标的柱子、码头木箱、桥与护栏……）
  按调用处记成零件，离线再按空间接触聚成一件；
* 区间在生成结束后读最终顶点（`PROP_SPACE.capture` 之类的事后平移已经生效）。

输出 `instances.json`（元数据）与 `geometry.npz`（三角形、所属实例），由
`tools/sky_island_scene_audit.py` 在普通 Python 里做路面 / 穿模 / 接地 / 重复实例判定。
生产生成不加 `--audit-ledger` 时本模块不被导入，FBX 与元数据逐字不变。
"""
import json
import math
import sys
from pathlib import Path

INSTANCES = []
OWNERS = []
_MAIN = {}
# 调用链里这些函数只是画零件的工具，不代表「一件东西」。
PRIMITIVES = {'addmesh', 'box', 'cylinder', 'beam', 'sphere', 'torus', 'lathe', 'ribbon',
              'textured_quad', 'wrapper', '_record_part', '<lambda>', 'fit', 'fitted', 'capture'}


def _caller(depth):
    frame = sys._getframe(depth)
    while frame is not None and frame.f_code.co_name in PRIMITIVES:
        frame = frame.f_back
    if frame is None:
        return '?', '?'
    return frame.f_globals.get('__name__', '?'), frame.f_code.co_name


def _chain(depth, limit=6):
    """调用链上的函数名（去掉画零件的工具与摆放裁决），闭包也能追到外层编排函数。"""
    frame, names = sys._getframe(depth), []
    while frame is not None and len(names) < limit:
        name = frame.f_code.co_name
        if name not in PRIMITIVES and name != '<module>':
            names.append(name)
        frame = frame.f_back
    return names


def _snapshot(g):
    return {key: (len(data['v']), len(data['f'])) for key, data in g.GROUPS.items()}


def _spans(g, before):
    spans = []
    for key, data in g.GROUPS.items():
        v0, f0 = before.get(key, (0, 0))
        if len(data['f']) > f0:
            spans.append([key, v0, len(data['v']), f0, len(data['f'])])
    return spans


def _number(value):
    return isinstance(value, (int, float)) and math.isfinite(value)


def _describe(name, args, kwargs):
    info = {}
    if name == 'emit_registered_obstacle':
        obs = args[1]
        info.update({'obstacleId': obs['id'], 'obstacleKind': obs['kind'], 'position': list(obs['center']),
                     'size': list(obs['size']), 'model': obs.get('model')})
    elif name == '_stamp':
        entry = args[1]
        payload = entry[0] if isinstance(entry, tuple) else entry
        info.update({'model': payload['meta']['name'], 'position': [args[2], args[3], args[4]],
                     'yaw': args[5], 'scale': args[6] if len(args) > 6 else kwargs.get('scale', 1.0)})
    elif name == 'stamp' and len(args) >= 3 and isinstance(args[1], str):
        info.update({'model': args[1], 'position': list(args[2])})
        if len(args) > 3 and _number(args[3]):
            info['height' if not isinstance(args[2], str) else 'scale'] = args[3]
    elif name in ('place_model',):
        obs = args[1]
        info.update({'obstacleId': obs['id'], 'model': obs.get('model'), 'position': list(obs['position'])})
    elif name in ('vine',):
        info.update({'position': list(args[1]), 'length': args[2]})
    elif name == 'dress_cliff':
        info.update({'island': args[1]['id']})
    else:
        numbers = [a for a in args if _number(a)]
        vector = next((a for a in args if isinstance(a, (tuple, list)) and len(a) == 3 and all(_number(v) for v in a)), None)
        if len(numbers) >= 3 and (vector is None or args.index(numbers[0]) < args.index(vector)):
            info['position'] = numbers[:3]
            if len(numbers) > 3:
                info['extra'] = numbers[3:]
        elif vector is not None:
            info['position'] = list(vector)
    return info


def own(g, module, name, kind):
    """把 module.name 包成实例发射函数：只有最外层调用建实例，嵌套调用归外层。"""
    function = getattr(module, name)
    if getattr(function, '_ledger_kind', None):
        return

    def wrapper(*args, **kwargs):
        if OWNERS or g.GROUPS is not _MAIN['groups']:
            return function(*args, **kwargs)
        module_name, caller = _caller(2)
        record = {'id': len(INSTANCES), 'kind': kind, 'fn': name, 'caller': caller,
                  'callerModule': module_name, 'region': g.CURRENT, 'chain': _chain(2)}
        record.update(_describe(name, args, kwargs))
        before = _snapshot(g)
        OWNERS.append(record)
        try:
            return function(*args, **kwargs)
        finally:
            OWNERS.pop()
            record['spans'] = _spans(g, before)
            if record['spans']:
                INSTANCES.append(record)
                record['id'] = len(INSTANCES) - 1

    wrapper._ledger_kind = kind
    wrapper.__name__ = 'wrapper'
    setattr(module, name, wrapper)


def install(g):
    import sky_island_dressing
    import sky_island_geology
    import sky_island_nature_assets
    import sky_island_road_layout
    import sky_island_settlement
    import sky_island_tripo_props
    _MAIN['groups'] = g.GROUPS
    original = g.addmesh

    def addmesh(mat, verts, faces, uv=None, smooth=False, group=None, corner_normals=None):
        if OWNERS or g.GROUPS is not _MAIN['groups']:
            return original(mat, verts, faces, uv, smooth, group, corner_normals)
        key = (group or g.CURRENT, mat)
        data = g.GROUPS.get(key)
        v0, f0 = (len(data['v']), len(data['f'])) if data else (0, 0)
        result = original(mat, verts, faces, uv, smooth, group, corner_normals)
        data = g.GROUPS.get(key)
        if data is not None and len(data['f']) > f0:
            module_name, caller = _caller(2)
            INSTANCES.append({'id': len(INSTANCES), 'kind': 'part', 'fn': caller, 'caller': caller,
                              'callerModule': module_name, 'region': g.CURRENT, 'material': mat, 'chain': _chain(2),
                              'spans': [[key, v0, len(data['v']), f0, len(data['f'])]]})
        return result

    addmesh.__name__ = 'addmesh'
    g.addmesh = addmesh

    def ledger_rollback(before):
        # 摆放裁决找不到净空时整件撤掉（sky_island_prop_placement.rollback）：台账里同一段区间也要撤。
        for record in INSTANCES:
            kept = []
            for span in record.get('spans', []):
                key, v0 = span[0], span[1]
                if v0 < before.get(key, (0, 0))[0]:
                    kept.append(span)
            record['spans'] = kept
        INSTANCES[:] = [r for r in INSTANCES if r.get('spans') or r in OWNERS]
        for index, record in enumerate(INSTANCES):
            record['id'] = index

    g.ledger_rollback = ledger_rollback
    for name, kind in [('emit_registered_obstacle', 'registered'), ('tree', 'tree'), ('lantern', 'lantern'),
                       ('pot', 'pot'), ('house', 'house'), ('bell', 'bell'), ('bench', 'bench'),
                       ('barrel', 'barrel'), ('duck_statue', 'statue'), ('garden_clump', 'garden'),
                       ('paved_disc', 'paving'), ('world_clouds', 'cloud'), ('rail_lamp', 'rail_lamp')]:
        own(g, g, name, kind)
    own(g, sky_island_road_layout, 'emit', 'road')
    own(g, sky_island_tripo_props, '_stamp', 'model')
    own(g, sky_island_settlement, 'place_model', 'life_prop')
    own(g, sky_island_geology, 'dress_cliff', 'cliff')
    for name, kind in [('crystal', 'crystal'), ('vine', 'vine'), ('moon_mushroom', 'mushroom'),
                       ('lotus', 'lotus'), ('hanging_lantern', 'sky_lantern')]:
        own(g, sky_island_dressing, name, kind)
    own(g, sky_island_nature_assets, 'stamp', 'nature')
    # dressing 在导入时把 stamp 绑成了自己的全局名，要一起换。
    sky_island_dressing.stamp = sky_island_nature_assets.stamp


def dump(g, out_dir, layout, extra):
    """读最终顶点，写 instances.json 与 geometry.npz。"""
    import numpy
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    group_names = sorted(g.GROUPS, key=lambda k: (str(k[0]), str(k[1])))
    group_index = {key: i for i, key in enumerate(group_names)}
    covered = {key: 0 for key in group_names}
    verts, tris, owner, group_of = [], [], [], []
    base = 0
    for record in INSTANCES:
        lo, hi = [math.inf] * 3, [-math.inf] * 3
        tri_count = 0
        for key, v0, v1, f0, f1 in record['spans']:
            data = g.GROUPS[key]
            local = data['v'][v0:v1]
            verts.extend(local)
            for p in local:
                for k in range(3):
                    lo[k] = min(lo[k], p[k])
                    hi[k] = max(hi[k], p[k])
            for face in data['f'][f0:f1]:
                ids = [index - v0 + base for index in face]
                for j in range(1, len(ids) - 1):
                    tris.append((ids[0], ids[j], ids[j + 1]))
                    owner.append(record['id'])
                    group_of.append(group_index[key])
                    tri_count += 1
            covered[key] += f1 - f0
            base += v1 - v0
        record['bounds'] = [lo, hi]
        record['triangles'] = tri_count
        record['spans'] = [[str(key[0]), str(key[1]), v0, v1, f0, f1] for key, v0, v1, f0, f1 in record['spans']]
    unattributed = {'%s|%s' % key: len(g.GROUPS[key]['f']) - covered[key]
                    for key in group_names if len(g.GROUPS[key]['f']) != covered[key]}
    numpy.savez_compressed(out / 'geometry.npz', vertices=numpy.asarray(verts, dtype=numpy.float32),
                           triangles=numpy.asarray(tris, dtype=numpy.int32),
                           owner=numpy.asarray(owner, dtype=numpy.int32),
                           group=numpy.asarray(group_of, dtype=numpy.int32))
    payload = {'schemaVersion': 1, 'coordinateSystem': 'Unity XYZ metres',
               'groups': ['%s|%s' % key for key in group_names], 'instances': INSTANCES,
               'unattributedFaces': unattributed, **extra}
    (out / 'instances.json').write_text(json.dumps(payload, ensure_ascii=False), encoding='utf-8')
    return {'instances': len(INSTANCES), 'triangles': len(tris), 'vertices': len(verts),
            'unattributedGroups': len(unattributed)}
