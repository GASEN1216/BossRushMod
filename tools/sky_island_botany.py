"""晴岚群岛实体植被与石凳：替换原低面数裂片，原导入源保持可恢复。

纯 Python 几何库，所有点使用 Unity XYZ 米制。形状固定，不消费调用方 RNG；
现有 _stamp 仍拥有位置、偏航和缩放。重制件保持原树高并约束在原 XZ 包络内，
石凳在原包络内采用可坐的座高，不参与碰撞或导航。
"""

import math


NAMES = frozenset(("tree_green", "tree_blossom", "tree_gold", "tree_olive", "cherry_tree",
                   "bush_a", "bush_b", "cliff_shrub_cap", "coral_clump", "fern_clump", "stone_bench"))
_CACHE = {}
PATH_REJECTIONS = []
TAU = math.pi * 2.0


def reset():
    _CACHE.clear()
    PATH_REJECTIONS.clear()


def _add(a, b):
    return tuple(a[i] + b[i] for i in range(3))


def _mul(a, value):
    return tuple(component * value for component in a)


def _cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def _unit(value):
    length = math.sqrt(sum(component*component for component in value))
    return _mul(value, 1.0 / max(length, 1e-9))


class Geometry:
    def __init__(self):
        self.groups = {}

    def add(self, material, vertices, faces, smooth=False):
        data = self.groups.setdefault(material, {"v": [], "f": [], "smooth": []})
        offset = len(data["v"])
        data["v"].extend(vertices)
        data["f"].extend(tuple(offset + index for index in face) for face in faces)
        data["smooth"].extend([smooth] * len(faces))

    def tube(self, points, radii, material="Wood", sides=7):
        vertices = []
        previous_u = previous_v = None
        for index, (point, radius) in enumerate(zip(points, radii)):
            direction = _unit(tuple(points[min(index+1, len(points)-1)][i] - points[max(index-1, 0)][i] for i in range(3)))
            if previous_u is None:
                u = _cross(direction, (0, 1, 0))
                if sum(value*value for value in u) < .001:
                    u = _cross(direction, (1, 0, 0))
            else:
                # 平行传递截面：上一圈轴投到新切平面，避免近竖直树干左右转向时翻轴。
                projection = sum(previous_u[i]*direction[i] for i in range(3))
                u = _add(previous_u, _mul(direction, -projection))
                if sum(value*value for value in u) < 1e-10:
                    u = _cross(previous_v, direction)
                if sum(u[i]*previous_u[i] for i in range(3)) < 0:
                    u = _mul(u, -1)
            u = _unit(u)
            v = _unit(_cross(direction, u))
            previous_u, previous_v = u, v
            for step in range(sides):
                angle = TAU * step / sides
                radial = _add(_mul(u, math.cos(angle)*radius), _mul(v, math.sin(angle)*radius))
                vertices.append(_add(point, radial))
        faces = [tuple(reversed(range(sides))), tuple((len(points)-1)*sides+i for i in range(sides))]
        for ring in range(len(points)-1):
            for step in range(sides):
                nxt = (step+1) % sides
                faces.append((ring*sides+step, ring*sides+nxt, (ring+1)*sides+nxt, (ring+1)*sides+step))
        self.add(material, vertices, faces)

    def crown(self, center, size, material, seed=0, sides=9, rings=4):
        """封闭叶团，极点独立；宽圆肩与略偏移的层心给出整体体积。"""
        vertices = [(center[0], center[1]-size[1], center[2])]
        for ring in range(1, rings):
            latitude = -math.pi*.5 + math.pi*ring/rings
            y = math.sin(latitude)
            radius = math.cos(latitude)
            for step in range(sides):
                angle = TAU*step/sides + .11*seed + .085*ring
                scallop = 1 + .11*math.sin(angle*3+seed*1.7) + .055*math.cos(angle*5-seed)
                vertices.append((center[0]+size[0]*(radius*math.cos(angle)*scallop+.06*y),
                                 center[1]+size[1]*(y+.035*math.cos(angle*3+seed)),
                                 center[2]+size[2]*(radius*math.sin(angle)*scallop+.045*y)))
        top = len(vertices)
        vertices.append((center[0]+size[0]*.07, center[1]+size[1], center[2]+size[2]*.03))
        faces = [(0, 1+(step+1)%sides, 1+step) for step in range(sides)]
        for ring in range(rings-2):
            start = 1+ring*sides
            nxt = start+sides
            for step in range(sides):
                other = (step+1)%sides
                faces.append((start+step, start+other, nxt+other, nxt+step))
        start = 1+(rings-2)*sides
        faces.extend((start+step, start+(step+1)%sides, top) for step in range(sides))
        # XZ 环按 +X 到 +Z 排列；由底到顶连接时翻转，源几何即为外向法线。
        faces = [tuple(reversed(face)) for face in faces]
        self.add(material, vertices, faces, smooth=False)

    def leaf(self, origin, tip, width, material, roll=0.0, curl=.10, petal=False):
        """闭合弯曲叶片：连续中脉、三段叶缘及薄腹面，卷转不产生重合面。"""
        direction = tuple(tip[i]-origin[i] for i in range(3))
        axis = _unit(direction)
        side = _cross(axis, (0, 1, 0))
        if sum(value*value for value in side) < .001:
            side = _cross(axis, (1, 0, 0))
        side = _unit(side)
        normal = _unit(_cross(side, axis))
        side, normal = (_add(_mul(side, math.cos(roll)), _mul(normal, math.sin(roll))),
                        _add(_mul(normal, math.cos(roll)), _mul(side, -math.sin(roll))))
        vertices = [origin]
        # 花瓣的远端较宽；叶片收尖，中脉直接由表面折线表现。
        profile = (.55, .94, .82) if petal else (.65, 1.0, .53)
        for t, spread in zip((.23, .54, .82), profile):
            center = _add(_add(origin, _mul(direction, t)),
                          _mul(normal, width*curl*math.sin(math.pi*t)))
            vertices.extend((_add(center, _mul(side, width*spread)),
                             _add(center, _mul(normal, -width*.065*spread)),
                             _add(center, _mul(side, -width*spread)),
                             _add(center, _mul(normal, width*.22*spread))))
        vertices.append(tip)
        faces = [(0, 1+(i+1)%4, 1+i) for i in range(4)]
        for ring in range(2):
            start = 1+ring*4
            faces.extend((start+i, start+(i+1)%4, start+4+(i+1)%4, start+4+i)
                         for i in range(4))
        faces.extend((9+i, 9+(i+1)%4, 13) for i in range(4))
        self.add(material, vertices, faces)

    def foliage_cluster(self, center, size, material, seed=0, sides=7, rings=3):
        """带叶柄的簇生叶，拟合旧球冠实际包络，维持树干定位与道路净空。"""
        envelope = Geometry()
        envelope.crown(center, size, material, seed, sides, rings)
        reference = envelope.groups[material]["v"]
        cluster = Geometry()
        for index in range(5):
            angle = index*TAU/5 + seed*.31
            root = (0, -.82, 0)
            joint = (math.cos(angle)*.23, -.16+index*.025, math.sin(angle)*.23)
            tip = (math.cos(angle), .38+.25*math.cos(angle+.6), math.sin(angle))
            cluster.tube([root, joint], [.035, .014], material, 4)
            cluster.leaf(joint, tip, .32, material, roll=(index-2)*.14, curl=.24)
        cluster.leaf((0, -.30, 0), (.12, 1.0, -.08), .26, material, roll=.3)
        data = cluster.groups[material]
        low = [min(v[i] for v in data["v"]) for i in range(3)]
        high = [max(v[i] for v in data["v"]) for i in range(3)]
        lo = [min(v[i] for v in reference) for i in range(3)]
        hi = [max(v[i] for v in reference) for i in range(3)]
        vertices = [tuple(lo[i]+(v[i]-low[i])*(hi[i]-lo[i])/(high[i]-low[i])
                          for i in range(3)) for v in data["v"]]
        self.add(material, vertices, data["f"])

    def flower(self, center, radius, material, heart, angle=0.0):
        """五瓣浅碟状花冠与封闭六角花盘，只用软植被材质。"""
        for index in range(5):
            theta = angle+index*TAU/5
            direction = (math.cos(theta), 0, math.sin(theta))
            root = _add(center, _mul(direction, radius*.10))
            tip = _add(_add(center, _mul(direction, radius)), (0, radius*.20, 0))
            self.leaf(root, tip, radius*.32, material, roll=.10*math.sin(theta),
                      curl=.45, petal=True)
        self.tube([_add(center, (0, -.025*radius, 0)),
                   _add(center, (0, .12*radius, 0)),
                   _add(center, (0, .23*radius, 0))],
                  [radius*.18, radius*.22, radius*.13], heart, 6)

    def box(self, center, size, material, bevel=.08):
        x, y, z = center
        w, h, d = size
        r = min(bevel, min(size)*.22)
        perimeter = [(-w/2+r,-d/2),(w/2-r,-d/2),(w/2,-d/2+r),(w/2,d/2-r),
                     (w/2-r,d/2),(-w/2+r,d/2),(-w/2,d/2-r),(-w/2,-d/2+r)]
        vertices = [(x+px*factor,y+py,z+pz*factor) for py,factor in ((-h/2,.96),(-h/2+r,1),(h/2-r,1),(h/2,.96)) for px,pz in perimeter]
        faces = [tuple(reversed(range(8))), tuple(range(24,32))]
        for ring in range(3):
            faces.extend((ring*8+i,ring*8+(i+1)%8,(ring+1)*8+(i+1)%8,(ring+1)*8+i) for i in range(8))
        # 与 crown 相同的 XZ 环绕向；独立使用也保持外向，不能依赖 Blender 自动重算法线。
        faces = [tuple(reversed(face)) for face in faces]
        self.add(material, vertices, faces)


def _tree(geometry, name):
    # 每种树固定自己的枝干姿态、主冠高度和层次；次冠依附主冠，不撒随机碎叶。
    species = {
        "tree_green": ([(0,0,0),(-.12,2.5,.08),(.25,4.7,0),(.15,6.4,.12)],
            [(-1.65,5.05,.2,1.6,.95,1.35), (1.45,5.55,-.35,1.65,1.05,1.4),
             (-.55,6.85,.15,1.85,1.12,1.55),(.95,7.8,.4,1.32,.92,1.25)], "Leaf", "LeafLight"),
        "tree_gold": ([(0,0,0),(.13,2.4,-.05),(-.22,4.5,.07),(.25,6.45,.15)],
            [(-1.65,4.95,-.3,1.5,1.15,1.3),(1.55,5.9,.25,1.6,1.12,1.3),
             (-.65,7.05,.2,1.68,1.15,1.42),(.6,8.05,-.15,1.26,.95,1.15)], "LeafGold", "LeafLight"),
        "tree_olive": ([(0,0,0),(-.32,2.2,.05),(-.25,4.0,.18),(.45,5.65,.02)],
            [(-1.85,4.85,.1,1.55,.9,1.35),(1.65,5.05,.25,1.62,.95,1.42),
             (-.25,6.2,-.6,1.65,.95,1.4),(1.05,7.05,.35,1.4,.85,1.2),(-1.2,6.55,.5,1.25,.8,1.1)], "Leaf", "Forest"),
        "tree_blossom": ([(0,0,0),(.1,2.3,.06),(-.3,4.2,.14),(.2,5.8,.06)],
            [(-1.65,4.6,.15,1.6,1.05,1.4),(1.55,4.95,.3,1.7,1.12,1.4),
             (-.45,6.4,-.5,1.92,1.13,1.5),(.95,7.2,.4,1.5,1.02,1.28)], "Blossom", "Flower"),
        "cherry_tree": ([(0,0,0),(-.22,2.5,.08),(-.05,4.6,.1),(.22,6.45,.18)],
            [(-1.1,4.95,.4,1.15,.95,1.1),(1.05,5.9,-.3,1.3,1.0,1.22),
             (-.65,7.25,.05,1.25,1.12,1.25),(.45,8.25,.3,1.18,1.0,1.03)], "Blossom", "LeafLight"),
    }
    trunk, crowns, primary, accent = species[name]
    geometry.tube(trunk, [.58,.43,.29,.12], "Wood", 8)
    for index in range(5):
        angle = index*TAU/5+.25
        geometry.tube([(math.cos(angle)*.85,.07,math.sin(angle)*.85),
                       (math.cos(angle)*.28,.45,math.sin(angle)*.28),(0,1.1,0)], [.13,.18,.23], "Wood", 6)
    for index,(x,y,z,rx,ry,rz) in enumerate(crowns):
        y -= .35
        z += (.65 if index%2 else -.6)
        start = trunk[1 if index < 2 else 2]
        geometry.tube([start,(x*.57,y-.8,z*.65),(x,y-.12,z)], [.19,.13,.055], "Wood", 6)
        # 深一点的下冠和受光主冠有明确的遮阴关系，色块之间不画黑边。
        lower_material = primary if primary == "Blossom" else "Forest"
        geometry.crown((x,y-.20,z),(rx*.70,ry*.90,rz*.70),lower_material,index+3,8,3)
        geometry.crown((x,y+.11,z),(rx,ry*1.30,rz),primary,index+1,11,5)
        for side in range(4):
            angle=side*TAU/4+index*.6
            sx=x+math.cos(angle)*rx*.68
            sy=y+ry*(.40+.25*math.sin(angle+.7))
            sz=z+math.sin(angle)*rz*.7
            secondary=accent if side==index%4 else primary
            geometry.foliage_cluster((sx,sy,sz),(rx*.43,ry*.60,rz*.43),secondary,index*4+side+12,7,3)
        if primary == "Blossom":
            for bloom in range(2):
                theta = index*.73+bloom*2.1
                flower_center = (x+math.cos(theta)*rx*.60, y+ry*1.20, z+math.sin(theta)*rz*.58)
                geometry.flower(flower_center, min(rx,rz)*.19, primary, accent, theta)
    # 冠缘小叶簇有厚度，每树少量即可在近景显出叶子尺度。
    for index,(x,y,z,rx,ry,rz) in enumerate(crowns[:3]):
        for side in (-1,1):
            leaf_y=y-.35
            leaf_z=z+(.65 if index%2 else -.6)
            origin=(x+side*rx*.62,leaf_y+.15,leaf_z-rz*.58)
            geometry.leaf(origin,(origin[0]+side*.46,origin[1]+.13,origin[2]-.37),.25,accent)


def _shrub(geometry, name):
    if name == "cliff_shrub_cap":
        lobes=[(-1.1,.6,0,1.0,.54,.85),(0,.9,.1,1.13,.72,.95),(1.05,.55,-.1,.93,.49,.8),(-.3,.55,-.72,.83,.43,.64)]
    elif name == "bush_b":
        lobes=[(-.62,.44,.05,.7,.42,.66),(.48,.52,-.03,.85,.48,.75),(0,.72,.15,.68,.47,.65)]
    else:
        lobes=[(-.44,.52,.05,.65,.49,.58),(.48,.64,.1,.72,.58,.65),(0,.99,-.06,.67,.57,.63)]
    for index,(x,y,z,rx,ry,rz) in enumerate(lobes):
        geometry.foliage_cluster((x,y,z),(rx,ry,rz),"Leaf" if index%2==0 else "LeafLight",index+7,9,4)
    for index in range(3):
        angle=TAU*index/3+.5
        geometry.leaf((.35*math.cos(angle),.5,.35*math.sin(angle)),
                      (.88*math.cos(angle),.75,.88*math.sin(angle)),.15,"Forest")
    for index,(x,y,z,rx,ry,rz) in enumerate(lobes[:3]):
        for sign in (-1,1):
            start=(x+sign*rx*.52,y+ry*.48,z-rz*.48)
            geometry.leaf(start,(start[0]+sign*.20,start[1]+.12,start[2]-.24),.12,"LeafLight")


def _fern(geometry):
    # 外层舒展、内层直立；羽片向尖端收窄，左右错节，不做机械镜像。
    for index in range(7):
        angle = index*TAU/5+.18 if index < 5 else (index-5)*math.pi+.8
        forward = (math.cos(angle), 0, math.sin(angle))
        side = (-math.sin(angle), 0, math.cos(angle))
        young = index >= 5
        length = .62 if young else (.87 if index%2 else 1.05)
        height = .80 if young else .56+index*.018
        def curve(t):
            return _add(_mul(forward, length*t),
                        (0, .025+height*math.sin(t*1.95)-.11*t*t, 0))
        samples = (0, .18, .38, .58, .78, 1)
        geometry.tube([curve(t) for t in samples],
                      [.025, .022, .018, .014, .009, .004], "Forest", 5)
        for pair in range(6):
            t = .17+pair*.125
            width = .31*math.sin(math.pi*(.17+t*.77))*(1-t*.65)
            for sign in (-1, 1):
                middle = curve(t+(.025 if sign > 0 else 0))
                reach = width*(.82 if young else 1)
                endpoint = _add(middle, _add(_mul(side, reach*sign),
                                _add(_mul(forward, .12*(1-t*.4)),
                                     (0, .05-.09*t, 0))))
                geometry.leaf(middle, endpoint, reach*.25,
                              "LeafLight" if (pair+index)%4==0 else "Leaf",
                              roll=sign*(.18+pair*.045), curl=.23)
        geometry.leaf(curve(.87), curve(1), .040, "LeafLight", roll=index*.07)


def _coral(geometry):
    # 岛上珊瑚状灌枝属于活植物，枝条圆厚并带叶芽，不再使用断裂板片。
    stems=[(-.38,.78,-.12),(.30,1.02,.20),(-.12,1.2,.08),(.42,.72,-.30),(-.40,.65,.38)]
    for index,(x,y,z) in enumerate(stems):
        geometry.tube([(0,.04,0),(x*.4,y*.5,z*.4),(x,y,z)],[.085,.065,.025],"Coral",6)
        geometry.foliage_cluster((x,y-.04,z),(.16,.22,.16),"LeafLight" if index%2 else "Leaf",index+4,6,3)
        for sign in (-1,1):
            start=(x*.55,y*.57,z*.55)
            tip=(x+sign*.18,y*.80,z-.14)
            geometry.leaf(start,tip,.10,"Leaf")


def _bench(geometry):
    geometry.box((0,.94,0),(3.8,.30,1.05),"Limestone",.075)
    geometry.box((0,.77,0),(3.34,.10,.80),"Chalk",.035)
    for x in (-1.18,1.18):
        geometry.box((x,.38,0),(.53,.76,.79),"Chalk",.075)
        geometry.box((x,.095,0),(.75,.19,.94),"Limestone",.05)


def model(name, bounds):
    """返回按旧尺寸合同规范化的多材质几何；原 JSON 不改写。"""
    if name not in NAMES:
        return None
    key=(name,tuple(bounds["min"]),tuple(bounds["max"]))
    if key in _CACHE:
        return _CACHE[key]
    geometry=Geometry()
    if name.startswith("tree_") or name=="cherry_tree":
        _tree(geometry,name)
    elif name in ("bush_a","bush_b","cliff_shrub_cap"):
        _shrub(geometry,name)
    elif name=="fern_clump":
        _fern(geometry)
    elif name=="coral_clump":
        _coral(geometry)
    else:
        _bench(geometry)
    # 单件作者几何预算；实例数由原摆放器决定，不能靠无限堆叶片提质。
    triangle_budget = 5500 if name.startswith("tree_") or name == "cherry_tree" else 2600
    triangles = sum(len(face)-2 for data in geometry.groups.values() for face in data["f"])
    if triangles > triangle_budget:
        raise ValueError("Botany triangle budget exceeded: {} ({}/{})".format(
            name, triangles, triangle_budget))
    points=[point for data in geometry.groups.values() for point in data["v"]]
    lo=[min(p[i] for p in points) for i in range(3)]
    hi=[max(p[i] for p in points) for i in range(3)]
    old_size=[bounds["max"][i]-bounds["min"][i] for i in range(3)]
    scale_x=old_size[0]/(hi[0]-lo[0])
    # 旧 AI 树的破裂叶片有异常长的 Z 包络；它是净空上界，不能把新叶团拉成薄椭圆盘。
    depth=min(old_size[2],old_size[0]*1.20) if name.startswith("tree_") or name=="cherry_tree" else old_size[2]
    scale_z=depth/(hi[2]-lo[2])
    vertical=old_size[1]/(hi[1]-lo[1])
    if name=="stone_bench":
        scale_x=scale_z=min(scale_x,scale_z,1.0)
        vertical=min(vertical,1.0)
    center_x=(hi[0]+lo[0])*.5
    center_z=(hi[2]+lo[2])*.5
    for data in geometry.groups.values():
        data["v"]=[((x-center_x)*scale_x,(y-lo[1])*vertical,(z-center_z)*scale_z) for x,y,z in data["v"]]
    _CACHE[key]=geometry.groups
    return geometry.groups


def stamp(g, payload, x, base_y, z, yaw_deg, scale):
    groups=model(payload["meta"]["name"],payload["meta"]["bounds"])
    if groups is None:
        return None
    if not path_clear(g, payload["meta"]["name"], groups, x, base_y, z, scale):
        return 0
    cosine=math.cos(math.radians(yaw_deg))
    sine=math.sin(math.radians(yaw_deg))
    triangles=0
    for material,data in groups.items():
        vertices=[(x+(vx*cosine+vz*sine)*scale,base_y+vy*scale,z+(-vx*sine+vz*cosine)*scale) for vx,vy,vz in data["v"]]
        # 所有新面均平面着色，合批接口不依赖 smooth-list 的扩展。
        if payload["meta"]["name"] in ("bush_a","bush_b","cliff_shrub_cap","coral_clump","fern_clump"):
            g.addmesh(material,vertices,data["f"],smooth=False,group=g.CURRENT+'_Flora')
        else:
            g.addmesh(material,vertices,data["f"],smooth=False)
        triangles+=sum(len(face)-2 for face in data["f"])
    return triangles


def path_clear(g, name, groups, x, base_y, z, scale):
    """实体植被的可视净空：复用 PlantingSpace 的道路半宽与 0.9 m 边距。"""
    tracks = getattr(g, "PAVING_TRACKS", ())
    if name == "stone_bench" or not tracks:
        return True
    from sky_island_dressing import distance_to_segment
    tree = name.startswith("tree_") or name == "cherry_tree"
    points = [p for data in groups.values() for p in data["v"]
              if not tree or p[1]*scale <= 2.2]
    radius = max((math.hypot(p[0],p[2])*scale for p in points), default=0)
    for samples, width in tracks:
        for a, b in zip(samples, samples[1:]):
            distance = distance_to_segment((x,z),a,b)
            if distance >= width*.5+.9+radius:
                continue
            # 登记障碍物的树不能只隐藏外观而留下无形碰撞，留证据给布局修正。
            registered = tree and any(abs(item['center'][0]-x)<.05 and abs(item['center'][2]-z)<.05
                                     for item in getattr(g, 'COLLISIONS', ()))
            PATH_REJECTIONS.append({'name':name,'positionUnity':[x,base_y,z],'radius':radius,
                'distanceToPath':distance,'pathHalfWidth':width*.5,'margin':.9,
                'action':'review_registered_collision' if registered else 'omit_decorative_plant'})
            return bool(registered)
    return True
