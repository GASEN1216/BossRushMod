"""Compare the warehouse entry and yard collision setup with the released visible geometry."""
import json
from pathlib import Path
import re
import sys
from types import SimpleNamespace
from unittest.mock import patch

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtSource/LastShiftYard'
BUNDLES = ROOT / 'Assets/arenas'
sys.path.insert(0, str(ROOT / 'tools'))


def read_code(filename):
    return clean_source((ROOT / 'LastShiftYard' / filename).read_text(encoding='utf-8-sig'))


def check_wiring():
    entry = read_code('LastShiftEntry.cs')
    attach = read_code('LastShiftEntryCollision.cs')
    world = read_code('LastShiftWorld.cs')
    assert re.search(r'CheckShaders\(model\);\s*LastShiftEntryCollision.Attach\(model.transform\);\s*LastShiftTicketPunch.Attach', entry), '入口必须在剪票钳动画接线前装配碰撞'
    assert re.search(r'part.layer = layer;\s*part.transform.SetParent\(root, false\);.*?part.AddComponent<BoxCollider>\(\).size', attach, re.S), '入口实体必须挂模型根且使用游戏墙层'
    assert 'GameplayDataSettings.Layers.wallLayerMask.value' in attach, '入口实体脱离官方墙层'
    assert re.search(r'AttachBuildingColliders\(collision\);.*?foreach \(Collider collider in collision.GetComponentsInChildren', world, re.S), '编组站补碰撞未接入正式墙层分配'


def scene_geometry(filename, entry=False):
    import numpy as np
    import UnityPy
    from UnityPy.helpers.MeshHelper import MeshHandler
    from rebuild_lastshift_bundle_layers import transform

    env = UnityPy.load(str(filename))
    cache, visuals, solids = {}, {}, {}
    for obj in env.objects:
        kind = obj.type.name
        if kind not in ('MeshRenderer', 'BoxCollider') and not (entry and kind.endswith('Collider')):
            continue
        component = obj.read()
        go = component.m_GameObject.read()
        refs = {p.component.deref().type.name: p.component for p in go.m_Component}
        matrix, path = transform(refs['Transform'], cache)
        if not component.m_Enabled or not go.m_IsActive:
            continue
        if entry and kind.endswith('Collider') and not component.m_IsTrigger:
            raise ValueError('入口包已开始自带实体，需要核对它和运行时实体的所有权：' + path)
        if kind == 'BoxCollider' and '/Collision/' in path and not component.m_IsTrigger:
            c, s = component.m_Center, component.m_Size
            solids[path] = (matrix, np.array([c.x, c.y, c.z]), np.array([s.x, s.y, s.z]))
        if kind != 'MeshRenderer' or not (entry or any(piece in path for piece in (
                'walls-and-doorheads', 'platform stock and ink/Node_57', 'Inst_PROTO_yard_00_0_-1'))):
            continue
        mesh = refs['MeshFilter'].read().m_Mesh.read()
        handler = MeshHandler(mesh)
        handler.process()
        vertices = np.asarray(handler.m_Vertices) @ matrix[:3, :3].T + matrix[:3, 3]
        triangles = np.concatenate([np.asarray(indices) + sub.baseVertex
                                    for indices, sub in zip(handler.get_triangles(), mesh.m_SubMeshes) if indices])
        visuals[path] = vertices[triangles]
    return visuals, solids


def ray_hits(triangles, point, axis):
    """Two-sided intersection of a straight line with real exported mesh triangles."""
    import numpy as np

    a, b, c = triangles[:, 0], triangles[:, 1], triangles[:, 2]
    direction = np.eye(3)[axis]
    u, v, w = b - a, c - a, point - a
    h = np.cross(direction, v)
    det = np.einsum('ij,ij->i', u, h)
    good = np.abs(det) > 1e-8
    inverse = np.divide(1, det, where=good, out=np.zeros(len(det)))
    s = inverse * np.einsum('ij,ij->i', w, h)
    q = np.cross(w, u)
    t = inverse * (q @ direction)
    distance = inverse * np.einsum('ij,ij->i', v, q)
    return distance[good & (s >= -1e-5) & (t >= -1e-5) & (s + t <= 1 + 1e-5)]


def fvec(text):
    return tuple(float(x.strip().removesuffix('f')) for x in text.split(','))


def check_entry():
    import numpy as np

    visual, _ = scene_geometry(BUNDLES / 'lastshift_entry', entry=True)
    assert len(visual) >= 15, '入口实体对照用的实际网格不全'
    mesh = np.concatenate(list(visual.values()))
    attach = read_code('LastShiftEntryCollision.cs')
    literal = re.compile(r'Box\(root, layer, "([A-Za-z]+)", ([^;()]+)\)')
    boxes = {name: np.array(fvec(values)).reshape(2, 3) for name, values in literal.findall(attach)}
    for name, axis in (('DeskBody', 0), ('DeskTop', 1), ('SignPost', 0), ('SignBoard', 2),
                       ('VanChassis', 0), ('VanCab', 0), ('VanCabRoof', 1), ('VanHood', 0),
                       ('VanBed', 1), ('VanBedLeft', 0), ('VanBedRight', 0), ('VanCargo', 0)):
        assert name in boxes, '入口缺实体 ' + name
        center, size = boxes[name]
        distances = ray_hits(mesh, center, axis)
        for edge in (-size[axis] / 2, size[axis] / 2):
            assert np.any(np.abs(distances - edge) < .045), f'入口实体脱离可见面 {name}: {edge}'
    wheels = re.findall(r'Wheel\(root, layer, "VanWheel[A-Za-z]+", ([-.\d]+)f, ([-.\d]+)f\);', attach)
    assert len(wheels) == 4, '接驳车四只轮胎必须各有实体'
    for x, z in wheels:
        tire_faces = ray_hits(visual['/LastShiftEntry/Mesh_1'], np.array([float(x), .49, float(z)]), 0)
        assert set(np.round(tire_faces[np.abs(tire_faces) < .2], 3)) == {-.145, .145}, '车轮脱离可见轴向厚度'

    # 原版在脚点上方 .5 m 和瞄准方向前 .2 m 做半径 .3 m 球形查询。
    entry = read_code('LastShiftEntry.cs')
    interaction = np.array(fvec(re.search(r'InteractLocal = new Vector3\(([^)]+)\)', entry)[1]))
    sensor_radius = float(re.search(r'InteractRadius = ([\d.]+)f;', entry)[1])
    feet = np.array([-.55, 0, .95])
    assert np.linalg.norm(feet + [0, .5, -.2] - interaction) <= sensor_radius + .3, '实体外的站位必须够得着验票点'
    # 查询球只碰到剪票触发体；不能用与余响触发体重叠的站位假证剪票可操作。
    talk = read_code('LastShiftYuXiang.cs')
    talk_point = np.array(fvec(re.search(r'TalkLocal = new Vector3\(([^)]+)\)', talk)[1]))
    talk_radius = float(re.search(r'sensor.radius = ([\d.]+)f;', talk)[1])
    assert np.linalg.norm(feet + [0, .5, -.2] - talk_point) > talk_radius + .3, '剪票站位被余响更近的交互触发体抢占'
    for z in np.linspace(feet[2], 3., 10):
        for center, size in boxes.values():
            overlap = np.abs(np.array([feet[0], .5, z]) - center) < size / 2 + [.3, .5, .3]
            assert not np.all(overlap), '从官方通道靠近检票站位时发生碰撞'


def check_yard(layout):
    import numpy as np
    import build_lastshift_blend as builder

    visuals, solids = scene_geometry(BUNDLES / 'lastshift_raid')
    roots = {k.rsplit('/', 1)[-1]: v for k, v in solids.items()}
    walls = [v for k, v in solids.items() if '/COL_wall/' in k]
    world = read_code('LastShiftWorld.cs')
    preserved = {s['id']: s for s in layout['solids']}
    check_author_collision(builder, layout)
    assert re.search(r'new Vector3\(\.49f, \.29f, width \+ \.75f\) : new Vector3\(width \+ \.75f, \.29f, \.49f\)', world), '门楣实际碰撞厚度或宽度改变'
    for authored in layout['solids']:
        if authored['type'] not in ('wall', 'furniture'):
            continue
        name = 'COL_' + authored['id']
        assert name in roots, '发布场景缺墙或家具 ' + name
        matrix, offset, size = roots[name]
        x, z = authored['center']
        assert np.allclose(matrix[:3, 3] + offset, [x, authored['baseY'] + size[1] / 2, -z], atol=.002), name
        assert np.allclose(size, authored['size'], atol=.002), name

    # 横梁应拦住高处的探针，五个门洞在身体高度必须给半径 .3 m 的角色留空。
    calls = re.findall(r'AddDoorFrame\(details, "([A-Za-z]+)", ([-f., \d]+), (true|false)\);', world)
    doors = {(tuple(fvec(values)), east == 'true'): name for name, values, east in calls}
    for building in layout['buildings']:
        room = building['id']
        mesh, = (v for k, v in visuals.items() if '/BLD_' + room + '/' in k and k.endswith('walls-and-doorheads'))
        for door in building['doors']:
            east = door['side'] == 'E'
            x, floor, z = door['position']
            lintel = 2.55 if room == 'signal' else 3.45
            assert ((x, floor, -z, door['width'], lintel), east) in doors, '漏掉门框 ' + door['id']
            faces = ray_hits(mesh, np.array([x, floor + lintel, -z]), 0 if east else 2)
            assert all(np.any(np.abs(faces - face) < .002) for face in (-.245, .245)), '门楣与可见面不齐 ' + door['id']
            # 门框尺寸来自生产方法，带角色半径的中心通路与最近门柱仍有净距。
            assert door['width'] / 2 + .12 - .33 / 2 > .3 and lintel - .29 / 2 > 1., '门框封堵 ' + door['id']
            for normal in np.linspace(-.75, .75, 9):
                position = np.array([x + normal * east, floor + .5, -z + normal * (not east)])
                for matrix, offset, size in walls:
                    local = np.linalg.solve(matrix[:3, :3], position - matrix[:3, 3]) - offset
                    assert not np.all(np.abs(local) < size / 2 + [.3, .5, .3]), '已烘焙的墙封堵 ' + door['id']

    old_layout = json.loads((ART / 'collision_boxes.json').read_text(encoding='utf-8'))
    notes = old_layout.get('readability', {})
    formerly_filtered = set(notes.get('dropped', [])) | {foot for foot, _ in notes.get('merged', [])}
    expected = {'gantryNE', 'gantrySE', 'crate:signalMed', 'crate:repairMed', 'crate:eastCab', 'crate:bellMed'}
    assert formerly_filtered <= expected, f'旧台账还有未覆盖的硬物 {formerly_filtered - expected}'
    found = {}
    for group, name, center, size in re.findall(r'AddMissingBox\(collision, details, "(\w+)", "([:\w]+)", new Vector3\(([^)]+)\), (\w+)\);', world):
        values = re.search(r'Vector3 ' + size + r' = new Vector3\(([^)]+)\);', world)
        found[name] = (group, np.array(fvec(center)), np.array(fvec(values[1])))
    assert expected <= set(found), f'缺旧包运行时补齐实体 {expected - set(found)}'
    for name in sorted(expected):
        group, center, size = found[name]
        original = preserved[name]
        x, z = original['center']
        assert original['type'] == group and np.allclose(center[[0, 2]], [x, -z]), '实心体脱离原位置 ' + name
        if 'COL_' + name in roots:
            assert np.allclose(roots['COL_' + name][2], original['size'], atol=.002), '重建包尺寸错误 ' + name
        key = 'Inst_PROTO_yard_00_0_-1' if group == 'block' else 'platform stock and ink'
        selected = np.concatenate([v for k, v in visuals.items() if key in k]).reshape(-1, 3)
        near = selected[np.all(np.abs(selected - center) < size / 2 + [.02, .03, .02], axis=1)]
        assert len(near) and np.allclose(np.ptp(near, axis=0), size, atol=.035), '恢复的盒体脱离可见模型 ' + name


def check_author_collision(builder, layout):
    # 跑生产碰撞导出，只替代 Blender 写入对象的边界。
    mesh = SimpleNamespace(from_pydata=lambda vertices, edges, faces: None)
    bpy = SimpleNamespace(data=SimpleNamespace(meshes=SimpleNamespace(new=lambda name: mesh),
                                                objects=SimpleNamespace(new=lambda name, mesh: SimpleNamespace())))
    coll = SimpleNamespace(objects=SimpleNamespace(link=lambda obj: None))
    with patch.object(builder, 'bpy', bpy), patch.object(builder, 'T', lambda *point: point):
        emitted = builder.build_collision(layout, coll, ART / 'export')
    for solid in layout['solids']:
        name = solid['id']
        if solid['type'] == 'rail' and name.startswith(builder.SLOPED_RAILS):
            assert any(seg['id'].startswith(name + '_') for seg in emitted['railSegments']), '重建丢掉坡道栏杆 ' + name
            continue
        boxes = {b['id']: b for b in emitted[solid['type']]}
        assert name in boxes and boxes[name]['size'] == solid['size'], '重建丢掉布局硬物或缩小其尺寸 ' + name


def main():
    try:
        check_wiring()
        layout_path = ART / 'export/lastshift_layout.json'
        if not all(p.is_file() for p in (layout_path, ART / 'export/grid_height.f32', ART / 'collision_boxes.json',
                                          BUNDLES / 'lastshift_entry', BUNDLES / 'lastshift_raid')):
            print('LastShiftCollisionPropertyTest: PARTIAL 缺作者布局或发布包')
            return 2
        check_entry()
        check_yard(json.loads(layout_path.read_text(encoding='utf-8')))
    except (AssertionError, ValueError) as error:
        print('LastShiftCollisionPropertyTest: FAIL ' + str(error))
        return 1
    print('LastShiftCollisionPropertyTest: PASS 入口实体、三栋楼的墙与家具、五处门洞、六件旧包漏项')
    return 0


if __name__ == '__main__':
    sys.exit(main())
