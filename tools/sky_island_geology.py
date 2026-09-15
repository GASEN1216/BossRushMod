"""天空岛闭合砂岩：参考 Production20260914/References/E_geology_native.png。

COMPAT：只生成地表以下的可见岩体。layout 顶圈逐值保留，ground/nav/collision
不进入本模块。主壳与三块有尺寸差异的封闭扶壁局部咬合（intentional intersection）；
没有复制内部整壳，也不把悬崖图集碎件按高度放大。普通 Python 可执行几何验证。
"""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import zlib


MATERIALS = ('GeologySand', 'GeologyLight', 'GeologyDeep')
FALLBACK_MATERIALS = ('Rock', 'RockLight', 'RockDeep')
GEOLOGY_TEXTURE_METRES = 32.0
REFERENCE = 'ArtSource/SkyIsland/Production20260914/References/E_geology_native.png'

# 剖面只定义大体积与断面，不再按整圈分配深浅条纹。下半部转为块岩的平面断口，
# 清晰的三组岩柱由 build_buttresses 单独封闭成形，与主壳只在岛侧局部相交。
PROFILES = (
    ((0, 1), (.05, .997), (.24, .965), (.43, .83), (.68, .60), (.91, .32), (1.08, .18)),
    ((0, 1), (.065, .994), (.285, .95), (.49, .77), (.76, .55), (.99, .29), (1.11, .17)),
    ((0, 1), (.045, .996), (.20, .96), (.405, .87), (.65, .60), (.88, .35), (1.05, .20)),
)


def parameters(island):
    seed = zlib.crc32(str(island['id']).encode('utf-8'))
    outline = island['outline']
    spans = [max(p[a] for p in outline) - min(p[a] for p in outline) for a in (0, 1)]
    radius = min(spans) * .5
    heading = (seed % 360) * math.pi / 180
    depth = radius * (1.16 + ((seed >> 8) % 13) / 100)
    return {'profile': seed % len(PROFILES), 'heading': heading, 'depth': depth,
            'radius': radius, 'seed': seed,
            'buttressAngles': [heading + a for a in (.13, 2.35, 4.29)],
            'buttressWeights': (1.0, .74 + (seed % 7) / 50, .61 + (seed % 5) / 40)}


def _block_radius(angle, heading, radius):
    """射线与六个切岩平面的交点；连续几段会落在同一平面，保留宽断面。"""
    distances = (.95, .82, .94, .76, .87, 1.02)
    limits = [radius * distance / cosine for i, distance in enumerate(distances)
              if (cosine := math.cos(angle-heading-i*math.tau/6)) > .001]
    return min(limits)


def _sub(a, b):
    return tuple(a[i] - b[i] for i in range(3))


def _cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def _dot(a, b):
    return sum(a[i]*b[i] for i in range(3))


def project_uv(points, repeat):
    """侧面保持同一世界高度相位；只把接近水平的断口改投 XZ。"""
    normal=_cross(_sub(points[1],points[0]),_sub(points[2],points[0]))
    length=math.sqrt(_dot(normal,normal))
    if abs(normal[1])>.94*length:
        return [(p[0]/repeat,p[2]/repeat) for p in points]
    axis=2 if abs(normal[0])>abs(normal[2]) else 0
    return [(p[axis]/repeat,p[1]/repeat) for p in points]


def build_shell(island):
    """返回闭合的 Unity XYZ indexed triangles，顶圈是原 outline 的精确拷贝。"""
    x, y, z = island['center']
    outline = island['outline']
    n = len(outline)
    spec = parameters(island)
    profile = PROFILES[spec['profile']]
    heading, depth, radius = spec['heading'], spec['depth'], spec['radius']
    vertices = []
    for level, (drop, factor) in enumerate(profile):
        for px, pz in outline:
            if level == 0:
                vertices.append((px, y, pz))
                continue
            dx, dz = px - x, pz - z
            angle = math.atan2(dz, dx)
            mix = min(1.0, drop/.65)
            ray_length = ((1-mix)*math.hypot(dx,dz)+mix*_block_radius(angle,heading,radius))*factor
            # 偏心、宽断面的基岩沿同一地质倾向下沉；不是均匀缩放成一个尖圆锥。
            shift = radius * .29 * drop ** 1.35
            dip = .044 * (dx * math.cos(heading) + dz * math.sin(heading))
            yy = y - depth * drop + dip * min(1, drop / .13)
            vertices.append((x + math.cos(angle)*ray_length + shift * math.cos(heading), yy,
                             z + math.sin(angle)*ray_length + shift * math.sin(heading)))
    faces, materials = [], []
    for level in range(len(profile) - 1):
        for i in range(n):
            j = (i + 1) % n
            a, b, c, d = level*n+i, level*n+j, (level+1)*n+j, (level+1)*n+i
            # 显式三角化确保 FBX 和运行时采用同一条对角线。
            triangles = [(a, b, c), (a, c, d)] if math.dist(vertices[a], vertices[c]) <= math.dist(vertices[b], vertices[d]) else [(a, b, d), (b, c, d)]
            faces.extend(triangles)
            materials.extend([0, 0])
    # 上盖压入地表，避免与原 ground 的完整平面共面；顶边仍精确相接。
    top = len(vertices)
    vertices.append((x, y - .18, z))
    lower = vertices[(len(profile)-1)*n:len(profile)*n]
    bottom = len(vertices)
    vertices.append((sum(p[0] for p in lower)/n,
                     min(p[1] for p in lower)-depth*.045,
                     sum(p[2] for p in lower)/n))
    for i in range(n):
        j = (i + 1) % n
        faces.append((top, j, i)); materials.append(0)
        faces.append((bottom, (len(profile)-1)*n+i, (len(profile)-1)*n+j)); materials.append(2)
    volume = sum(_dot(vertices[a], _cross(vertices[b], vertices[c])) for a, b, c in faces)/6
    if volume < 0:
        faces = [tuple(reversed(face)) for face in faces]
    mesh = {'vertices': vertices, 'triangles': faces, 'materials': materials,
            'topVertexCount': n, 'parameters': spec}
    validate_shell(island, mesh)
    return mesh


def _closed_block(vertices, rings, count, materials):
    faces, slots = [], []
    for level in range(rings-1):
        for i in range(count):
            j=(i+1)%count
            a,b,c,d=level*count+i,level*count+j,(level+1)*count+j,(level+1)*count+i
            faces.extend(((a,b,c),(a,c,d)))
            slots.extend((materials(level,i),)*2)
    for ring, reverse in ((0,True),(rings-1,False)):
        points=vertices[ring*count:(ring+1)*count]
        centre=len(vertices); vertices.append(tuple(sum(p[k] for p in points)/count for k in range(3)))
        for i in range(count):
            triangle=(centre,ring*count+i,ring*count+(i+1)%count)
            faces.append(tuple(reversed(triangle)) if reverse else triangle)
            slots.append(0 if reverse else 2)
    volume=sum(_dot(vertices[a],_cross(vertices[b],vertices[c])) for a,b,c in faces)/6
    if volume<0: faces=[tuple(reversed(face)) for face in faces]
    return {'vertices':vertices,'triangles':faces,'materials':slots,'topVertexCount':0}


def build_buttresses(island):
    """宽 / 长 / 窄三块封闭柱状岩，只在岛侧咬入主体，不形成第二层完整壳。"""
    x,y,z=island['center']; spec=parameters(island); radius=spec['radius']; depth=spec['depth']
    blocks=[]
    # 八边切角的横断面，外侧是两块大面；各角不等宽，杜绝低模圆柱外观。
    footprint=((-1,-.57),(-.73,-1),(.60,-1),(1,-.64),(1,.57),(.53,1),(-.69,1),(-1,.38))
    # 每组由宽浅的三段岩层构成，宽度与总高度接近，避免垂挂家具腿的比例。
    dimensions=((.90,.50,.68),(.68,.47,.77),(.49,.39,.54))
    for index,(angle,(width,thickness,height)) in enumerate(zip(spec['buttressAngles'],dimensions)):
        px,pz=min(island['outline'],key=lambda p:abs((math.atan2(p[1]-z,p[0]-x)-angle+math.pi)%math.tau-math.pi))
        # 内侧约半个厚度与主壳相交，外侧从冠部以下才露出。
        cx=x+(px-x)*.86; cz=z+(pz-z)*.86
        ux,uz=-math.sin(angle),math.cos(angle); nx,nz=math.cos(angle),math.sin(angle)
        width*=radius; thickness*=radius; height*=depth
        base_y=y-depth*(.055,.11,.065)[index]
        # 三个宽浅岩段，各保留长主面、窄倒角和一处不等宽的断肩。微小退台是
        # 几何形成的阴影，不使用贯穿全岛的黑色色带，也不向顶点撒随机噪声。
        profile=((0,.78,.76),(.045,1,1),
                 (.265,.985,.98),(.30,.89,.86),(.322,.87,.84),
                 (.353,.96,.95),(.575,.91,.91),(.615,.79,.77),(.637,.77,.75),
                 (.675,.86,.82),(.89,.70,.71),(.935,.56,.58),(1,.41,.47))
        vertices=[]
        for level,(drop,wfactor,rfactor) in enumerate(profile):
            # 三块岩体的主断肩不在等分高度上；宽肩、长断面、浅断口各自成形。
            # 只重映射高度分段，保留封闭拓扑和上缘/侧向尺寸。
            first,second=((.235,.68),(.41,.74),(.29,.56))[index]
            if drop<=.322:
                drop=drop/.322*first
            elif drop<=.637:
                drop=first+(drop-.322)/(.637-.322)*(second-first)
            else:
                drop=second+(drop-.637)/(1-.637)*(1-second)
            # 下端向主基岩收进，三个断面彼此搭接，不留三条细长的独立支脚。
            lean=drop*radius*(.36,.43,.27)[index]
            stratum=0 if drop<.32 else 1 if drop<.637 else 2
            side_shift=width*(drop*(.075,-.09,.08)[index]+(0,.025,-.025)[stratum])
            for u,v in footprint:
                lx=u*width*.5*wfactor+side_shift; lz=v*thickness*.5*rfactor
                yy=base_y-height*drop+.038*(lx*ux+lz*nx)*math.cos(spec['heading'])+.038*(lx*uz+lz*nz)*math.sin(spec['heading'])
                vertices.append((cx+ux*lx+nx*lz-nx*lean,yy,cz+uz*lx+nz*lz-nz*lean))
        block=_closed_block(vertices,len(profile),len(footprint),
                            lambda level,face:1 if level in (0,4,8) and face in (1,2,3) else 0)
        block['parameters']=spec; block['role']=('Wide','Long','Narrow')[index]
        block['intentionalIntersection']='Side buttress locally keyed into the primary solid; no inner whole-island shell.'
        validate_solid(island,block)
        blocks.append(block)
    return blocks


def validate_shell(island, mesh):
    vertices, triangles = mesh['vertices'], mesh['triangles']
    n = len(island['outline'])
    expected = [(px, island['center'][1], pz) for px, pz in island['outline']]
    if vertices[:n] != expected:
        raise ValueError('Geology changed the authoritative surface outline')
    result=validate_solid(island,mesh)
    result['exactTopOutline']=True
    return result


def validate_solid(island, mesh):
    vertices, triangles=mesh['vertices'],mesh['triangles']
    n=mesh['topVertexCount']
    edge_uses, directed = Counter(), Counter()
    minimum_area = float('inf')
    for a, b, c in triangles:
        cross = _cross(_sub(vertices[b], vertices[a]), _sub(vertices[c], vertices[a]))
        area = math.sqrt(_dot(cross, cross))*.5
        minimum_area = min(minimum_area, area)
        if area <= 1e-8:
            raise ValueError('Degenerate geology triangle')
        for u, v in ((a, b), (b, c), (c, a)):
            edge_uses[tuple(sorted((u, v)))] += 1
            directed[(u, v)] += 1
    if any(count != 2 for count in edge_uses.values()):
        raise ValueError('Geology must be a closed manifold')
    if any(directed[(a,b)] != directed[(b,a)] for a,b in directed):
        raise ValueError('Geology normals have inconsistent edge winding')
    if len(vertices)-len(edge_uses)+len(triangles) != 2:
        raise ValueError('Geology must be one closed genus-zero solid')
    if any(not math.isfinite(v) for p in vertices for v in p):
        raise ValueError('Nonfinite geology position')
    if any(p[1] >= island['center'][1] for p in vertices[n:]):
        raise ValueError('Geology may not rise into the gameplay surface')
    return {'id': island['id'], 'vertices': len(vertices), 'triangles': len(triangles),
            'boundaryEdges': 0, 'nonmanifoldEdges': 0, 'degenerateTriangles': 0,
            'minimumTriangleArea': round(minimum_area, 6),
            'profile': mesh['parameters']['profile'], 'depth': mesh['parameters']['depth']}


def emit_shell(g, island):
    """输出主壳和三个独立封闭扶壁；优先专属砂岩槽，未注册时回落旧岩石。"""
    main=build_shell(island)
    obj=_emit_mesh(g,'VIS_'+g.CURRENT+'_Geology',main)
    for block in build_buttresses(island):
        child=_emit_mesh(g,'VIS_'+g.CURRENT+'_Buttress'+block['role']+'_Geology',block)
        child['intentional_intersection']=block['intentionalIntersection']
    return obj


def _emit_mesh(g,name,mesh):
    names=tuple(preferred if preferred in g.MATERIALS else fallback
                for preferred,fallback in zip(MATERIALS,FALLBACK_MATERIALS))
    obj = g.create_object(name, mesh['vertices'], mesh['triangles'], names[0])
    for name in names[1:]:
        obj.data.materials.append(g.MATERIALS[name])
    layer = obj.data.uv_layers.active
    for polygon, slot in zip(obj.data.polygons, mesh['materials']):
        polygon.material_index = slot
        # 原生砂岩图包含多层浅断纹，32 米周期保持大层理可读；旧 fallback
        # 仍采用自己的世界尺度。斜侧面不因法线刚跨过 45° 就把纹理旋成顶投影。
        repeat = GEOLOGY_TEXTURE_METRES if names[slot] in MATERIALS else g.TILED_TEXTURES[names[slot]][1]
        points = [mesh['vertices'][obj.data.loops[li].vertex_index] for li in polygon.loop_indices]
        for li,uv in zip(polygon.loop_indices,project_uv(points,repeat)):
            layer.data[li].uv = uv
    obj['geology_reference'] = REFERENCE
    obj['geology_profile'] = mesh['parameters']['profile']
    obj['geology_closed'] = True
    return obj


def dress_cliff(g, island):
    """少量贴岩根藤沿扶壁凹口落下；尺度来自本岛剖面而非图集模型高度。"""
    x, y, z = island['center']
    spec = parameters(island)
    shell=build_shell(island)
    n=len(island['outline'])
    for index, target in enumerate(spec['buttressAngles']):
        # 根藤取真正的岩面截线，不能沿旧壳比例悬空垂下。
        angle = target + .5
        nearest=min(range(n),key=lambda i:abs((math.atan2(island['outline'][i][1]-z,
                                                         island['outline'][i][0]-x)-angle+math.pi)%math.tau-math.pi))
        root_line=[shell['vertices'][level*n+nearest] for level in (1,2,3)]
        nx,nz=math.cos(angle),math.sin(angle)
        px,py,pz=root_line[0]
        size = 1.7 if len(island['id']) == 1 else 1.05
        g.sphere((px, py+.3, pz), (size*1.7, .8, size*1.25), 'Forest', 9, 4, False)
        for root in range(2):
            offset = (root-.5)*size*.8
            points = [(p[0]+nx*.20-nz*offset,p[1],p[2]+nz*.20+nx*offset) for p in root_line]
            g.ribbon(points, 'WoodDark' if root == 0 else 'Forest', .13 if root else .22)
            for leaf in range(4):
                t = (leaf%2+.35)/2
                a,b=points[leaf//2],points[leaf//2+1]
                centre = tuple(a[k]+(b[k]-a[k])*t for k in range(3))
                g.sphere(centre, (.42*size, .32*size, .3*size), 'Leaf', 7, 3, False)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--layout', default='ArtSource/SkyIsland/layout.json')
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    path = Path(args.layout)
    raw = path.read_bytes()
    layout = json.loads(raw.decode('utf-8-sig'))
    records = [validate_shell(island, build_shell(island)) for island in layout['islands']]
    buttresses=[]
    for island in layout['islands']:
        for block in build_buttresses(island):
            record=validate_solid(island,block)
            record.update({'role':block['role'],'intentionalIntersection':block['intentionalIntersection']})
            buttresses.append(record)
    if path.read_bytes() != raw:
        raise ValueError('Source layout changed during validation')
    report = {'classification': 'COMPAT', 'layoutSHA256': hashlib.sha256(raw).hexdigest(),
              'reference': REFERENCE, 'islands': records, 'buttresses':buttresses,
              'triangles': sum(r['triangles'] for r in records+buttresses), 'sourceLayoutUnchanged': True,
              'limits': 'Geometry verification does not establish Unity runtime appearance.'}
    output = Path(args.out)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({'islands': len(records), 'triangles': report['triangles'], 'result': 'PASS'}))


if __name__ == '__main__':
    main()
