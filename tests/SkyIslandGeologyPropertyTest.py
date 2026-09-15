"""执行真实岩体生成：闭合实体、顶圈契约、重建确定性及破坏探针；不依赖 Blender。"""
import copy
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from sky_island_geology import build_shell, build_buttresses, validate_shell, validate_solid, project_uv


def check(island, mesh=None):
    before = copy.deepcopy(island)
    primary = mesh is None
    mesh = build_shell(island) if primary else mesh
    assert island == before, '岩体生成不得改写 layout 输入'
    if primary:
        assert mesh == build_shell(island), '同一岛重建必须产生相同几何与材质分配'
    validate_solid(island, mesh)
    origin = island['center']
    volume = 0.0
    for triangle in mesh['triangles']:
        a, b, c = [tuple(mesh['vertices'][index][axis] - origin[axis]
                          for axis in range(3)) for index in triangle]
        volume += (a[0]*(b[1]*c[2]-b[2]*c[1]) +
                   a[1]*(b[2]*c[0]-b[0]*c[2]) +
                   a[2]*(b[0]*c[1]-b[1]*c[0])) / 6
    assert volume > 0, '闭合岩体应有正体积和向外面向'
    assert len(mesh['materials']) == len(mesh['triangles']), '所有面必须登记材质'
    assert set(mesh['materials']) <= {0, 1, 2}, '不得越过三个专属砂岩材质槽'
    return mesh


def inside(point, mesh):
    """独立射线三角相交，判断侧柱有一部分咬入主体，另一部分确实外露。"""
    sub=lambda a,b:tuple(a[k]-b[k] for k in range(3))
    dot=lambda a,b:sum(a[k]*b[k] for k in range(3))
    cross=lambda a,b:(a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
    direction=(.6723,.2919,.8317)
    intersections=[]
    for face in mesh['triangles']:
        a,b,c=[mesh['vertices'][i] for i in face]
        e1,e2=sub(b,a),sub(c,a); h=cross(direction,e2); determinant=dot(e1,h)
        if abs(determinant)<1e-9: continue
        inv=1/determinant; s=sub(point,a); u=inv*dot(s,h)
        if u<0 or u>1: continue
        q=cross(s,e1); v=inv*dot(direction,q)
        if v<0 or u+v>1: continue
        distance=inv*dot(e2,q)
        if distance>1e-7: intersections.append(round(distance,6))
    return len(set(intersections))%2==1


def must_reject(island, broken, label):
    try:
        validate_shell(island, broken)
    except ValueError:
        return
    raise AssertionError('破坏探针未被拦截：' + label)


def main():
    layout = json.loads((ROOT/'ArtSource/SkyIsland/layout.json').read_text(encoding='utf-8-sig'))
    islands = layout['islands']
    meshes = [check(island) for island in islands]
    for island,primary in zip(islands,meshes):
        blocks=build_buttresses(island)
        assert len(blocks)==3, '每岛只有宽 / 长 / 窄三组主扶壁'
        assert blocks==build_buttresses(island), '扶壁重建必须确定'
        for block in blocks:
            check(island,block)
            overlap=sum(inside(p,primary) for p in block['vertices'])
            assert 0<overlap<len(block['vertices']), island['id']+'/'+block['role']+' 必须局部咬合且外露'
    # 平移与等比缩放后的实际 outline 同样必须满足封闭和地表约束。
    sample = copy.deepcopy(islands[1])
    for scale, dx, dy, dz in ((.5, 0, 0, 0), (1, 937, 148, -721), (1.5, -227, -58, 94)):
        transformed = copy.deepcopy(sample)
        transformed['center'] = [sample['center'][0]*scale+dx, sample['center'][1]*scale+dy,
                                 sample['center'][2]*scale+dz]
        transformed['outline'] = [(x*scale+dx, z*scale+dz) for x,z in sample['outline']]
        check(transformed)
    original = meshes[1]
    # 相邻斜岩面跨过旧主轴投影的 45° 分界时，地质层纹仍应处在相同世界高度。
    # 两面共享 (8,0,0) 与 (0,8,6)，前者旧逻辑选侧投影，后者会突然选顶投影。
    first=project_uv(((0,0,0),(8,0,0),(0,8,6)),32)
    second=project_uv(((8,0,0),(0,8,6),(8,8,20)),32)
    assert first[1][1]==second[0][1]==0, '岩层跨斜面接缝不能漂移'
    assert first[2][1]==second[1][1]==.25, '斜侧面层理必须沿世界高度相接'
    broken = copy.deepcopy(original); broken['triangles'].pop()
    must_reject(sample, broken, '移除底盖三角面')
    broken = copy.deepcopy(original); broken['triangles'][0] = tuple(reversed(broken['triangles'][0]))
    must_reject(sample, broken, '翻转单面')
    broken = copy.deepcopy(original); broken['triangles'].append(broken['triangles'][0])
    must_reject(sample, broken, '重复表面')
    broken = copy.deepcopy(original); x,y,z = broken['vertices'][0]; broken['vertices'][0] = (x+.01,y,z)
    must_reject(sample, broken, '漂移 layout 顶圈')
    broken = copy.deepcopy(original); broken['vertices'][-1] = (0,sample['center'][1]+1,0)
    must_reject(sample, broken, '岩体抬到可玩地表')
    broken = copy.deepcopy(original); broken['triangles'][0] = (0,0,1)
    must_reject(sample, broken, '退化面')
    print('PASS SkyIslandGeologyPropertyTest: %d real islands, 36 closed locally intersecting buttresses, continuous side UV height, 3 transformed domains, 6 rejection probes' % len(islands))


if __name__ == '__main__':
    main()
