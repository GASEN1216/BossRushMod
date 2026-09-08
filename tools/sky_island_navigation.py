"""晴岚群岛的几何事实源：岛、桥、障碍、地面与避障导航。

分类 COMPAT：离线资源生成工具，不改游戏 API、存档或模式入口。
运行 python tools/sky_island_navigation.py；仅生成/验证资源，不启动 Unity。
依赖 shapely 2.1（GEOS 约束三角化），可安装到 Build/sky-island-python-deps。
所有坐标均为 Unity X/Y/Z 米。ground 三角形是实体表面；navigation 由
同一实体域向内收缩后逐片裁切并保留高程，避免独立造两套桥坡。
"""

from __future__ import annotations

import argparse
from collections import defaultdict, deque
import heapq
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
LOCAL_DEPS = ROOT / "Build" / "sky-island-python-deps"
if LOCAL_DEPS.exists():
    sys.path.insert(0, str(LOCAL_DEPS))
try:
    from shapely import constrained_delaunay_triangles, set_precision
    from shapely.geometry import LineString, Point, Polygon, box
    from shapely.ops import unary_union
except ImportError as exc:
    raise SystemExit("需要 shapely 2.1：python -m pip install --target Build/sky-island-python-deps shapely==2.1.2") from exc

UNITY_PROJECT = Path("D:/code/ykf/duckov_modding-main/UnityFiles/BossRush")
CLEARANCE = 0.7
BRIDGE_MAX_TURN_DEGREES = 10.0
BRIDGE_ENTRY_EASE_METERS = 4.0

ISLAND_SPECS = [
    ("A", "登云码头", (0, 0, -300), (130, 100), "dock"),
    ("B", "风铃集", (0, 8, -130), (190, 150), "village"),
    ("C", "青穗梯田", (-230, 16, -110), (190, 170), "farm"),
    ("D", "悬根林", (-250, 28, 110), (190, 180), "forest"),
    ("E", "鸣风栈道", (0, 42, 110), (160, 150), "crossroads"),
    ("F", "镜水寺", (240, 26, -40), (190, 180), "temple"),
    ("G", "残星工坊", (250, 44, 180), (170, 150), "workshop"),
    ("H", "归航钟庭", (0, 62, 300), (180, 150), "bellcourt"),
    ("S1", "蛙鸣池", (-350, 12, -230), (60, 50), "pond"),
    ("S2", "倒挂邮亭", (-370, 24, 220), (55, 55), "post"),
    ("S3", "听雨洞", (355, 18, -170), (65, 65), "cave"),
    ("S4", "残星瞭台", (350, 52, 310), (65, 65), "lookout"),
]

# 首尾垂直进入岛的直边；中间点是避障控制点，实际桥道用相切圆弧连接。
BRIDGE_SPECS = [
    ("AB", "A", "B", 9, [(0, -250), (0, -205)]),
    ("BC", "B", "C", 9, [(-95, -110), (-135, -110)]),
    ("CD", "C", "D", 8, [(-250, -25), (-250, 20)]),
    ("DE", "D", "E", 8, [(-155, 110), (-80, 110)]),
    ("BF", "B", "F", 9, [(95, -120), (120, -120), (120, -40), (145, -40)]),
    ("FG", "F", "G", 8, [(230, 50), (230, 70), (285, 82), (285, 105)]),
    ("GE", "G", "E", 8, [(165, 180), (130, 180), (110, 110), (80, 110)]),
    ("EH", "E", "H", 9, [(40, 185), (40, 200), (115, 215), (115, 270), (90, 270)]),
    ("CS1", "C", "S1", 6, [(-325, -150), (-350, -150), (-350, -205)]),
    ("DS2", "D", "S2", 6, [(-345, 140), (-370, 140), (-370, 192.5)]),
    ("FS3", "F", "S3", 6, [(335, -80), (355, -80), (355, -137.5)]),
    ("GS4", "G", "S4", 6, [(335, 210), (350, 210), (350, 277.5)]),
    ("K1", "D", "B", 7, [(-210, 20), (-210, -5), (-80, -35), (-60, -35), (-60, -55)]),
    ("K2", "G", "B", 7, [(205, 105), (205, 82), (95, 35), (55, 0), (55, -55)]),
    ("K3", "B", "E", 7, [(-25, -55), (-25, -20), (-105, 15), (-105, 80), (-80, 80)]),
]


def rounded_outline(center, size):
    x, _, z = center
    w, d = size[0] / 2, size[1] / 2
    c = min(30, w * 0.30, d * 0.30)
    region=next(spec[0] for spec in ISLAND_SPECS if tuple(spec[2])==tuple(center))
    base=[(x-w+c,z-d),(x+w-c,z-d),(x+w,z-d+c),(x+w,z+d-c),
          (x+w-c,z+d),(x-w+c,z+d),(x-w,z+d-c),(x-w,z-d+c)]
    portals=[]
    for _,first,last,width,path in BRIDGE_SPECS:
        if first==region:
            portals.append((path[0],width/2+10))
        if last==region:
            portals.append((path[-1],width/2+10))
    result=[]
    for index,(a,b) in enumerate(zip(base,base[1:]+base[:1])):
        result.append(a)
        if index%2:
            # 二次圆角限定在原尺寸框内；四段不是以八角轮廓硬切大角。
            corner=(b[0],a[1]) if index in (1,5) else (a[0],b[1])
            for t in (0.25,0.5,0.75):
                result.append(((1-t)**2*a[0]+2*t*(1-t)*corner[0]+t*t*b[0],
                               (1-t)**2*a[1]+2*t*(1-t)*corner[1]+t*t*b[1]))
            continue
        length=math.dist(a,b)
        direction=((b[0]-a[0])/length,(b[1]-a[1])/length)
        inward=(-direction[1],direction[0])
        # 桥口连同两侧 10 m 必须保持原直线，起伏只作用于剩余岸段。
        protected=[]
        portal_interiors=[]
        for point,radius in portals:
            if abs(cross(a,b,point))<1e-5:
                station=(point[0]-a[0])*direction[0]+(point[1]-a[1])*direction[1]
                protected.append((max(0,station-radius),min(length,station+radius)))
                portal_interiors.append((station-radius+10,station+radius-10))
        cuts={0.0,length}
        cuts.update(s for pair in protected for s in pair)
        # 自然岸线使用少量 22–36 米的宽缓弯，避免导航在细小锯齿上增面。
        divisions=max(2,int(length/30))
        cuts.update(length*i/divisions for i in range(1,divisions))
        phase=(sum(ord(ch) for ch in region)+index*13)*0.37
        for station in sorted(cuts):
            if station<1e-7 or length-station<1e-7:
                continue
            if any(lo+1e-7<station<hi-1e-7 for lo,hi in portal_interiors):
                continue
            safe_from_portal=all(not lo-1e-7<=station<=hi+1e-7 for lo,hi in protected)
            amplitude=0.0
            if safe_from_portal:
                distance_to_protection=min([station,length-station]+[abs(station-bound) for pair in protected for bound in pair])
                amplitude=min(9.0, min(w,d)*0.12, distance_to_protection*0.5)
                amplitude*=0.55+0.45*math.sin(station*0.075+phase)
            result.append((a[0]+station*direction[0]+amplitude*inward[0],
                           a[1]+station*direction[1]+amplitude*inward[1]))
    return result


def make_obstacles(islands):
    result = []
    def add(region, name, kind, x, z, w, d, h):
        y = islands[region]["height"]
        result.append({"id": region + "_" + name, "island": region, "kind": kind,
                       "center": [x, y+h/2, z], "size": [w, h, d], "yaw": 0,
                       "bounds": [x-w/2, z-d/2, x+w/2, z+d/2]})
    add("A", "CargoHouse", "dock_house", -35, -311, 18, 12, 8)
    for args in [("TeaHouse", "tea_house", -50, -165, 16, 12, 10),
                 ("House02", "house", -22, -170, 12, 10, 8),
                 ("House03", "house", 35, -165, 14, 12, 9),
                 ("House04", "house", -55, -103, 16, 12, 8),
                 ("House05", "house", -23, -93, 10, 8, 7),
                 ("House06", "house", 40, -103, 14, 12, 9)]:
        add("B", *args)
    for index,(x,z) in enumerate([(-68,-148),(61,-153),(-45,-72),(50,-74)]):
        add("B","GardenTree%02d"%(index+1),"garden_tree",x,z,6,6,10)
    for index,x in enumerate((-6,6)):
        add("B","ChimeSupport%02d"%(index+1),"chime_support",x,-125,2.4,2.4,9.2)
    add("B","DuckStatue","duck_statue",18,-112,2.8,2.8,3)
    add("C", "WaterMill", "mill", -280, -90, 16, 12, 13)
    add("C", "FarmBarn", "barn", -180, -155, 20, 12, 10)
    add("D", "GreatRootTree", "great_tree", -285, 133, 22, 22, 36)
    add("D", "RootTree02", "tree", -214, 151, 14, 14, 26)
    add("D", "WindBeacon", "wind_beacon", -277, 84, 10, 10, 15)
    add("E", "WindPillarWest", "wind_pillar", -16, 136, 5, 5, 14)
    add("E", "WindPillarEast", "wind_pillar", 16, 136, 5, 5, 14)
    add("F", "MirrorPool", "pool", 240, -36, 70, 45, 0.5)
    add("F", "TempleHall", "temple", 253, 17, 43, 23, 12)
    add("F", "TemplePavilion", "pavilion", 184, -62, 14, 14, 9)
    for index,(x,z) in enumerate((x,z) for x in (201,211,269,279) for z in (-15,-5)):
        add("F","PavilionSupport%02d"%(index+1),"pavilion_support",x,z,1.7,1.7,7)
    add("G", "StarDome", "dome", 266, 197, 32, 28, 18)
    add("G", "SideShed", "workshop_shed", 214, 220, 20, 14, 8)
    add("G", "BrokenAstrolabe", "astrolabe", 292, 155, 14, 12, 10)
    add("H", "HomecomingBell", "bell", 0, 338, 34, 26, 25)
    add("H", "MemorialWest", "memorial", -51, 310, 8, 8, 9)
    add("H", "MemorialEast", "memorial", 51, 310, 8, 8, 9)
    add("S1", "FrogPond", "pool", -352, -236, 18, 12, 0.4)
    add("S2", "PostOffice", "post_house", -375, 227, 12, 10, 8)
    add("S3", "RainCave", "cave_rock", 358, -180, 22, 18, 14)
    add("S4", "StarLookout", "lookout", 350, 319, 16, 14, 16)
    # 能挡枪的地物也必须进同一障碍表，不能只在美术文件里随手添加。
    for region, entries in {
        "A": [(27,-323,5,4), (36,-281,6,3)],
        "B": [(-32,-137,4,3),(30,-126,4,3)],
        "C": [(-264,-148,5,4),(-220,-62,7,4),(-172,-98,4,5)],
        "D": [(-306,73,7,5),(-221,75,6,4),(-253,165,5,5)],
        "E": [(-43,94,6,4),(45,93,5,4)],
        "F": [(188,-111,6,4),(296,-76,5,5)],
        "G": [(207,162,5,4),(275,231,6,4)],
        "H": [(-32,278,5,4),(33,279,5,4)],
    }.items():
        for i, (x,z,w,d) in enumerate(entries):
            add(region, "Cover%02d" % (i+1), "cover", x,z,w,d,1.2)
    from sky_island_settlement import load_obstacles
    result.extend(load_obstacles())
    return result


def cross(a, b, c):
    return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])


def smooth_bridge_path(path, width):
    """相切圆弧保留桥口直线，按转角采样，避免无约束样条向岛体过冲。"""
    if len(path) < 3:
        return list(path)
    result = [path[0]]
    lengths = [math.dist(a, b) for a, b in zip(path, path[1:])]
    directions = [((b[0]-a[0])/length, (b[1]-a[1])/length)
                  for a, b, length in zip(path, path[1:], lengths)]
    for index in range(1, len(path)-1):
        p = path[index]
        incoming, outgoing = directions[index-1:index+1]
        turn = math.atan2(incoming[0]*outgoing[1]-incoming[1]*outgoing[0],
                          incoming[0]*outgoing[0]+incoming[1]*outgoing[1])
        if abs(turn) < 1e-6:
            result.append(p)
            continue
        if abs(turn) > math.radians(150):
            raise ValueError("桥道急折返")
        # 相邻圆角各占一段最多 46%；桥口至少保留 4 米的正交直线。
        entry_limit = lengths[index-1]-4 if index == 1 else lengths[index-1]*0.46
        exit_limit = lengths[index]-4 if index == len(path)-2 else lengths[index]*0.46
        tangent = math.tan(abs(turn)/2)
        trim = min(width*3.2*tangent, entry_limit, exit_limit)
        radius = trim/tangent
        if radius <= width/2+1:
            raise ValueError("桥道圆角空间不足")
        start = (p[0]-incoming[0]*trim, p[1]-incoming[1]*trim)
        finish = (p[0]+outgoing[0]*trim, p[1]+outgoing[1]*trim)
        sign = 1 if turn > 0 else -1
        center = (start[0]-incoming[1]*radius*sign, start[1]+incoming[0]*radius*sign)
        start_angle = math.atan2(start[1]-center[1], start[0]-center[0])
        count = max(2, math.ceil(abs(math.degrees(turn))/BRIDGE_MAX_TURN_DEGREES))
        result.append(start)
        for step in range(1, count):
            angle = start_angle+turn*step/count
            result.append((center[0]+radius*math.cos(angle), center[1]+radius*math.sin(angle)))
        result.append(finish)
    result.append(path[-1])
    return result


def split_path_at_stations(path, requested):
    result = [path[0]]
    station = 0.0
    for a, b in zip(path, path[1:]):
        length = math.dist(a, b)
        for target in requested:
            if station+1e-6 < target < station+length-1e-6:
                t = (target-station)/length
                result.append((a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t))
        result.append(b)
        station += length
    return result


def bridge_geometry(spec, island_by_id):
    bid, first, last, width, control_points = spec
    path2 = smooth_bridge_path(control_points, width)
    total = sum(math.dist(a, b) for a, b in zip(path2, path2[1:]))
    ease = min(BRIDGE_ENTRY_EASE_METERS, total*0.1)
    entry_stations = [ease*step/4 for step in range(1,5)]
    path2 = split_path_at_stations(path2, entry_stations+[total-s for s in reversed(entry_stations)])
    y0, y1 = island_by_id[first]["height"], island_by_id[last]["height"]
    lengths = [math.dist(a,b) for a,b in zip(path2,path2[1:])]
    directions = [((b[0]-a[0])/d,(b[1]-a[1])/d) for a,b,d in zip(path2,path2[1:],lengths)]
    total = sum(lengths)
    stations = [0]
    for length in lengths:
        stations.append(stations[-1]+length)
    def rise(s):
        # 坡度在桥口 4 米内线性缓入；所有可见面/碰撞/导航共用这些高程。
        if s < ease:
            return s*s/(2*ease)
        if s > total-ease:
            return total-ease-(total-s)**2/(2*ease)
        return s-ease/2
    path = [[p[0], y0+(y1-y0)*rise(s)/(total-ease), p[1]] for p,s in zip(path2,stations)]
    sections = []
    for i,p in enumerate(path2):
        if i == 0 or i == len(path2)-1:
            dx,dz = directions[0 if i==0 else -1]
            offset = (-dz*width/2, dx*width/2)
        else:
            a,b = directions[i-1],directions[i]
            n1,n2 = (-a[1],a[0]),(-b[1],b[0])
            denom = 1+n1[0]*n2[0]+n1[1]*n2[1]
            if denom <= 0.1:
                raise ValueError("桥道急折返: "+bid)
            offset = ((n1[0]+n2[0])*width/2/denom,(n1[1]+n2[1])*width/2/denom)
        sections.append([[p[0]+sign*offset[0],path[i][1],p[1]+sign*offset[1]] for sign in (1,-1)])
    faces = []
    for a,b in zip(sections,sections[1:]):
        faces.extend([(a[0],a[1],b[1]),(a[0],b[1],b[0])])
    return {"id":bid,"from":first,"to":last,"width":width,"path":path,
            "length":round(total,3),"surfaceTriangles":faces,"crossSections":sections,
            "controlPointsXZ":control_points,"entryEaseMeters":ease,
            "kind":"shortcut" if bid.startswith("K") else ("branch" if "S" in bid else "main")}


def insert_portals(outline, points):
    result=[]
    for a,b in zip(outline,outline[1:]+outline[:1]):
        result.append(a)
        length2=(b[0]-a[0])**2+(b[1]-a[1])**2
        candidates=[]
        for p in points:
            t=((p[0]-a[0])*(b[0]-a[0])+(p[1]-a[1])*(b[1]-a[1]))/length2
            if abs(cross(a,b,p))<1e-6 and 1e-8<t<1-1e-8:
                candidates.append((t,p))
        result.extend(p for _,p in sorted(candidates))
    return result


def polygons(geometry):
    if geometry.is_empty:
        return []
    if geometry.geom_type == "Polygon":
        return [geometry]
    return [p for g in geometry.geoms for p in polygons(g)]


def triangulate(geometry):
    result=[]
    for polygon in polygons(geometry):
        if polygon.area<1e-8:
            continue
        for triangle in constrained_delaunay_triangles(polygon).geoms:
            coords=list(triangle.exterior.coords)[:3]
            if triangle.area>1e-8:
                result.append(coords)
    return result


class MeshBuilder:
    def __init__(self):
        self.vertices=[]
        self.triangles=[]
        self.regions=[]
        self.indices=defaultdict(list)

    def add(self, points, region):
        points=[tuple(round(v,6) for v in p) for p in points]
        signed_area=cross((points[0][0],points[0][2]),(points[1][0],points[1][2]),(points[2][0],points[2][2]))
        if abs(signed_area)<2e-8:
            return
        # Unity XZ 平面顺时针为 +Y 正面。
        if cross((points[0][0],points[0][2]),(points[1][0],points[1][2]),(points[2][0],points[2][2]))>0:
            points[1],points[2]=points[2],points[1]
        face=[]
        for p in points:
            # GEOS 两侧裁切可差 1 微米，且可能刚好落在舍入格的两侧。
            # 搜索相邻 0.1 毫米格，再按真实 3 微米距离焊接，避免圆弯处断图。
            key=(math.floor(p[0]*10000),math.floor(p[2]*10000))
            vertex=next((i for dx in (-1,0,1) for dz in (-1,0,1)
                         for i in self.indices.get((key[0]+dx,key[1]+dz),())
                         if math.hypot(self.vertices[i][0]-p[0],self.vertices[i][2]-p[2])<=0.000003),None)
            if vertex is None:
                vertex=len(self.vertices)
                self.indices[key].append(vertex)
                self.vertices.append(list(p))
            elif abs(self.vertices[vertex][1]-p[1])>0.00002:
                raise ValueError("共用坐标高程冲突: %s" % (key,))
            face.append(vertex)
        if len(set(face))<3:
            return
        actual=[(self.vertices[i][0],self.vertices[i][2]) for i in face]
        if abs(cross(*actual))<2e-8:
            return
        self.triangles.append(face)
        self.regions.append(region)

    def export(self):
        edges=defaultdict(list)
        for i,face in enumerate(self.triangles):
            for a,b in zip(face,face[1:]+face[:1]):
                edges[tuple(sorted((a,b)))].append(i)
        return {"vertices":self.vertices,"triangles":self.triangles,"triangleRegions":self.regions,
                "boundaryEdges":[list(e) for e,uses in edges.items() if len(uses)==1]}


def triangle_height(point, coords):
    a,b,c=[(p[0],p[2]) for p in coords]
    area=cross(a,b,c)
    wb=cross(a,point,c)/area
    wc=cross(a,b,point)/area
    return coords[0][1]+wb*(coords[1][1]-coords[0][1])+wc*(coords[2][1]-coords[0][1])


def build_layout():
    island_by_id={i:{"id":i,"name":name,"center":list(center),"height":center[1],
                     "size":list(size),"theme":theme,"outline":rounded_outline(center,size)}
                  for i,name,center,size,theme in ISLAND_SPECS}
    obstacles=make_obstacles(island_by_id)
    bridges=[bridge_geometry(s,island_by_id) for s in BRIDGE_SPECS]
    for island in island_by_id.values():
        portals=[]
        for bridge in bridges:
            if bridge["from"]==island["id"]:
                portals.extend((p[0],p[2]) for p in bridge["crossSections"][0])
            if bridge["to"]==island["id"]:
                portals.extend((p[0],p[2]) for p in bridge["crossSections"][-1])
        island["outline"]=insert_portals(island["outline"],portals)
    obstacle_polys=[box(*o["bounds"]) for o in obstacles]
    obstacle_union=unary_union(obstacle_polys)
    island_polys={key:Polygon(i["outline"]) for key,i in island_by_id.items()}
    for obstacle,p in zip(obstacles,obstacle_polys):
        if not island_polys[obstacle["island"]].buffer(-1).covers(p):
            raise ValueError("障碍越岛界: "+obstacle["id"])
    ground=MeshBuilder()
    raw_surfaces=[]
    for key,island in island_by_id.items():
        surface=island_polys[key].difference(obstacle_union)
        raw_surfaces.append(surface)
        for tri in triangulate(surface):
            ground.add([(x,island["height"],z) for x,z in tri],key)
    bridge_polys=[]
    for bridge in bridges:
        bp=unary_union([Polygon([(p[0],p[2]) for p in tri]) for tri in bridge["surfaceTriangles"]])
        if not bp.is_valid:
            raise ValueError("桥自交: "+bridge["id"])
        for key,ip in island_polys.items():
            if bp.intersection(ip).area>1e-6:
                raise ValueError("桥穿过岛体: %s / %s"%(bridge["id"],key))
        for other_id,other in bridge_polys:
            if bp.intersection(other).area>1e-6:
                raise ValueError("桥道交叠: %s / %s"%(bridge["id"],other_id))
        bridge_polys.append((bridge["id"],bp))
        raw_surfaces.append(bp)
        for tri in bridge["surfaceTriangles"]:
            ground.add(tri,bridge["id"])
    walkable=unary_union(raw_surfaces)
    if walkable.geom_type!="Polygon":
        raise ValueError("实体岛桥不连通: "+walkable.geom_type)
    safe=walkable.buffer(-CLEARANCE,join_style=2)
    if safe.geom_type!="Polygon":
        raise ValueError("内缩导航出现断桥: "+safe.geom_type)
    navigation=MeshBuilder()
    source_ground_faces=[]
    ground_data=ground.export()
    # 将安全域与每一原地面三角片相交，保留桥的分片线性高程；共边重用顶点。
    for ground_index,(face,region) in enumerate(zip(ground_data["triangles"],ground_data["triangleRegions"])):
        coords=[ground_data["vertices"][i] for i in face]
        flat=Polygon([(p[0],p[2]) for p in coords])
        for tri in triangulate(set_precision(flat.intersection(safe),1e-6)):
            before=len(navigation.triangles)
            navigation.add([(x,triangle_height((x,z),coords),z) for x,z in tri],region)
            if len(navigation.triangles)>before:
                source_ground_faces.append(ground_index)
    markers=[]
    for key,island in island_by_id.items():
        x,y,z=island["center"]
        candidates=[(x,z)]+[(x+dx,z+dz) for dx,dz in [(0,-18),(-18,0),(18,0),(0,18),(-25,-25),(25,-25)]]
        point=next((p for p in candidates if safe.buffer(-2).covers(Point(p))),None)
        if point is None:
            local=safe.intersection(island_polys[key]).representative_point()
            point=(local.x,local.y)
        markers.append({"id":"Region_"+key,"kind":"region","island":key,"position":[point[0],y,point[1]]})
    markers.extend([
        {"id":"PlayerSpawn","kind":"spawn","island":"A","position":[0,0,-326]},
        {"id":"MainExtraction","kind":"extraction","island":"A","position":[0,0,-339]},
        {"id":"BellExtraction","kind":"extraction","island":"H","position":[0,62,309]},
        {"id":"Exit","kind":"extraction_alias","island":"A","position":[0,0,-339],"aliasOf":"MainExtraction"},
    ])
    safe_markers=safe.buffer(-1.5,join_style=2)
    def add_safe_marker(region,marker_id,kind,preferred):
        island=island_by_id[region]
        x,y,z=island["center"]
        w,d=island["size"]
        candidates=[(x+dx,z+dz) for dx,dz in preferred]
        candidates.extend((x+dx,z+dz) for dz in range(-int(d*0.3),int(d*0.3)+1,7)
                          for dx in range(-int(w*0.3),int(w*0.3)+1,7))
        existing=[(m["position"][0],m["position"][2]) for m in markers if m["island"]==region]
        local=island_polys[region].buffer(-3)
        point=next((p for p in candidates if local.covers(Point(p)) and safe_markers.covers(Point(p))
                    and all(math.dist(p,q)>4 for q in existing)),None)
        if point is None:
            raise ValueError("无法放置安全标记: "+marker_id)
        markers.append({"id":marker_id,"kind":kind,"island":region,"position":[point[0],y,point[1]]})
    for region in island_by_id:
        add_safe_marker(region,"EnemySpawn_"+region,"enemy_spawn",[(21,-16),(-21,-16),(10,10)])
        add_safe_marker(region,"POI_"+region,"point_of_interest",[(-12,17),(12,17),(0,-12)])
        add_safe_marker(region,"Search_"+region,"search",[(-30,-20),(30,20),(-12,-9)])
        if not region.startswith("S"):
            add_safe_marker(region,"Search_"+region+"_02","search",[(32,30),(-34,23),(15,31)])
            add_safe_marker(region,"Lamp_"+region,"lamp",[(-25,8),(25,8),(-10,-18)])
            add_safe_marker(region,"Lamp_"+region+"_02","lamp",[(26,-24),(-28,-24),(22,23)])
    layout={"schemaVersion":1,"coordinateSystem":"Unity XYZ metres","name":"晴岚群岛",
            "clearance":CLEARANCE,"islands":list(island_by_id.values()),"bridges":bridges,
            "obstacles":obstacles,"markers":markers,"ground":ground_data,"navigation":navigation.export(),
            "authoringNotes":["所有桥开放，仅场景资源，无剧情门锁。",
                              "ground 供可见地面和 MeshCollider，navigation 仅供 A*。",
                              "桥道保留桥口正交直线，圆弧转向每片最多 10 度；桥口 4 米缓坡按 1 米采样。",
                              "桥板、纵梁与桥墩按 path/crossSections 的累计里程放置，不能逐小片重启布局。",
                              "障碍 bounds 是碰撞事实源；装饰不得额外占用导航域。",
                              "离线几何验证不能替代 Unity/游戏内行走与相机测试。"]}
    layout["navigation"]["sourceGroundFaces"]=source_ground_faces
    layout["validation"]=validate_layout(layout)
    return layout


def mesh_geometry(mesh):
    return [Polygon([(mesh["vertices"][i][0],mesh["vertices"][i][2]) for i in face]) for face in mesh["triangles"]]


def bridge_alignment_metrics(bridges):
    metrics={}
    for bridge in bridges:
        directions=[]
        grades=[]
        for a,b in zip(bridge["path"],bridge["path"][1:]):
            distance=math.hypot(b[0]-a[0],b[2]-a[2])
            if distance<0.05:
                raise ValueError("桥道重复采样点: "+bridge["id"])
            directions.append(((b[0]-a[0])/distance,(b[2]-a[2])/distance))
            grades.append(math.degrees(math.atan2(b[1]-a[1],distance)))
        turns=[abs(math.degrees(math.atan2(a[0]*b[1]-a[1]*b[0],a[0]*b[0]+a[1]*b[1])))
               for a,b in zip(directions,directions[1:])]
        maximum_turn=max(turns,default=0)
        grade_change=max(abs(b-a) for a,b in zip([0]+grades,grades+[0]))
        mouth_slope=max(abs(grades[0]),abs(grades[-1]))
        if maximum_turn>BRIDGE_MAX_TURN_DEGREES+0.001 or grade_change>=5 or mouth_slope>=3:
            raise ValueError("桥道转角/桥口坡度不连续: "+bridge["id"])
        metrics[bridge["id"]]={"samples":len(bridge["path"]),"lengthMeters":bridge["length"],
                              "maximumTurnDegrees":round(maximum_turn,4),
                              "maximumGradeChangeDegrees":round(grade_change,4),
                              "maximumMouthSlopeDegrees":round(mouth_slope,4)}
    return metrics


def validate_layout(layout):
    nav=layout["navigation"]
    ground=layout["ground"]
    bridge_metrics=bridge_alignment_metrics(layout["bridges"])
    if not nav["triangles"] or len(nav["vertices"])>=4095:
        raise ValueError("导航为空或顶点超游戏 A* 上限")
    nav_polys=mesh_geometry(nav)
    ground_polys=mesh_geometry(ground)
    physical=unary_union(ground_polys)
    nav_union=unary_union(nav_polys)
    edges=defaultdict(list)
    maximum_slope=0
    area3d=0
    for idx,(face,poly) in enumerate(zip(nav["triangles"],nav_polys)):
        if len(set(face))!=3 or poly.area<=1e-8:
            raise ValueError("退化导航三角形: %d"%idx)
        a,b,c=[nav["vertices"][i] for i in face]
        source_index=nav["sourceGroundFaces"][idx]
        source_coords=[ground["vertices"][i] for i in ground["triangles"][source_index]]
        if poly.difference(ground_polys[source_index].buffer(0.00003)).area>0.0002:
            raise ValueError("导航跨出对应实体三角面: %d" % idx)
        if any(abs(p[1]-triangle_height((p[0],p[2]),source_coords))>0.00005 for p in (a,b,c)):
            raise ValueError("导航与碰撞高程不一致: %d" % idx)
        u=[b[k]-a[k] for k in range(3)]
        v=[c[k]-a[k] for k in range(3)]
        normal=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
        if normal[1]<=0:
            raise ValueError("导航绕序反向: %d"%idx)
        slope=math.degrees(math.atan2(math.hypot(normal[0],normal[2]),normal[1]))
        maximum_slope=max(maximum_slope,slope)
        area3d+=math.sqrt(sum(n*n for n in normal))/2
        for a,b in zip(face,face[1:]+face[:1]):
            edges[tuple(sorted((a,b)))].append(idx)
    if maximum_slope>=18:
        raise ValueError("坡度超 18 度: %.3f"%maximum_slope)
    if any(len(uses)>2 for uses in edges.values()):
        raise ValueError("非流形导航边")
    adjacency=defaultdict(set)
    region_interfaces=set()
    for uses in edges.values():
        if len(uses)==2:
            a,b=uses
            adjacency[a].add(b)
            adjacency[b].add(a)
            if nav["triangleRegions"][a]!=nav["triangleRegions"][b]:
                region_interfaces.add(frozenset((nav["triangleRegions"][a],nav["triangleRegions"][b])))
    visited={0}
    pending=deque([0])
    while pending:
        for node in adjacency[pending.popleft()]:
            if node not in visited:
                visited.add(node)
                pending.append(node)
    if len(visited)!=len(nav["triangles"]):
        missing=[nav["triangleRegions"][i] for i in range(len(nav["triangles"])) if i not in visited]
        raise ValueError("导航共边拓扑断开: %d / %d; %s"%(len(visited),len(nav["triangles"]),sorted(set(missing))))
    if sum(p.area for p in nav_polys)-nav_union.area>0.005:
        raise ValueError("导航三角形重叠")
    clearance=layout["clearance"]
    if nav_union.difference(physical.buffer(-clearance+0.00002,join_style=2)).area>0.002:
        raise ValueError("导航超出实体地面安全内缩域")
    expected=physical.buffer(-clearance,join_style=2)
    if expected.symmetric_difference(nav_union).area>0.005:
        raise ValueError("导航漏面或跨越空洞")
    for obstacle in layout["obstacles"]:
        if nav_union.intersection(box(*obstacle["bounds"]).buffer(clearance-0.00002,join_style=2)).area>0.0002:
            raise ValueError("导航侵入障碍安全域: "+obstacle["id"])
    route_points=list(nav["vertices"])
    route_graph=defaultdict(dict)
    def connect(a,b):
        length=math.dist(route_points[a],route_points[b])
        route_graph[a][b]=length
        route_graph[b][a]=length
    for a,b in edges:
        connect(a,b)
    marker_faces={}
    marker_nodes={}
    for marker in layout["markers"]:
        x,y,z=marker["position"]
        matches=[i for i,p in enumerate(nav_polys) if p.buffer(1e-7).covers(Point(x,z))]
        if not matches:
            raise ValueError("标记不在导航面: "+marker["id"])
        index=min(matches,key=lambda i:abs(triangle_height((x,z),[nav["vertices"][v] for v in nav["triangles"][i]])-y))
        height=triangle_height((x,z),[nav["vertices"][v] for v in nav["triangles"][index]])
        if abs(y-height)>0.001:
            raise ValueError("标记高程不落地: "+marker["id"])
        marker_faces[marker["id"]]=index
        node=len(route_points)
        route_points.append(marker["position"])
        marker_nodes[marker["id"]]=node
        for matching in matches:
            for vertex in nav["triangles"][matching]:
                connect(node,vertex)
        for other_id,other_face in marker_faces.items():
            if other_id!=marker["id"] and other_face in matches:
                connect(node,marker_nodes[other_id])
    def distances(source):
        result={source:0.0}
        previous={}
        heap=[(0.0,source)]
        while heap:
            dist,current=heapq.heappop(heap)
            if dist>result[current]+1e-9:
                continue
            for nxt,length in route_graph[current].items():
                new=dist+length
                if new<result.get(nxt,float("inf")):
                    result[nxt]=new
                    previous[nxt]=current
                    heapq.heappush(heap,(new,nxt))
        return result,previous
    from_spawn,previous=distances(marker_nodes["PlayerSpawn"])
    routes={key:round(from_spawn[node],2) for key,node in marker_nodes.items()}
    polylines={}
    for marker,node in marker_nodes.items():
        path=[node]
        while path[-1] in previous:
            path.append(previous[path[-1]])
        polylines[marker]=[route_points[n] for n in reversed(path)]
    layout["verifiedPathsFromSpawn"]=polylines
    exploration_sequence=["PlayerSpawn","Region_B","Region_C","Region_D","Region_E",
                          "Region_G","Region_F","Region_B","Region_E","Region_H","BellExtraction"]
    exploration_legs=[]
    for first,last in zip(exploration_sequence,exploration_sequence[1:]):
        leg_distances,_=distances(marker_nodes[first])
        exploration_legs.append({"from":first,"to":last,"meters":round(leg_distances[marker_nodes[last]],2)})
    region_connections={i["id"]:set() for i in layout["islands"]}
    for bridge in layout["bridges"]:
        # 桥身所有三角面都要有可达代表；只测岛中心会漏掉断开的装饰桥。
        route_faces=[i for i,r in enumerate(nav["triangleRegions"]) if r==bridge["id"]]
        if not route_faces or any(i not in visited for i in route_faces):
            raise ValueError("断桥: "+bridge["id"])
        for end in (bridge["from"],bridge["to"]):
            if frozenset((bridge["id"],end)) not in region_interfaces:
                raise ValueError("桥口没有与声明岛屿共边连接: %s / %s" % (bridge["id"],end))
        region_connections[bridge["from"]].add(bridge["to"])
        region_connections[bridge["to"]].add(bridge["from"])
    bounds=[[min(v[k] for v in ground["vertices"]),max(v[k] for v in ground["vertices"])] for k in range(3)]
    return {"status":"PASS","navigationVertices":len(nav["vertices"]),"navigationTriangles":len(nav["triangles"]),
            "groundVertices":len(ground["vertices"]),"groundTriangles":len(ground["triangles"]),
            "navigationConnectedComponents":1,"navigationAreaXZ":round(nav_union.area,3),
            "navigationSurfaceArea":round(area3d,3),"physicalWalkableAreaXZ":round(physical.area,3),
            "maximumSlopeDegrees":round(maximum_slope,4),"clearanceMeters":clearance,"boundsXYZ":bounds,
            "islandCount":len(layout["islands"]),"bridgeCount":len(layout["bridges"]),"obstacleCount":len(layout["obstacles"]),
            "markerCount":len(layout["markers"]),
            "bridgeGeometry":bridge_metrics,
            "fromSpawnWalkablePathDistancesMeters":routes,
            "explorationRoute":{"sequence":exploration_sequence,"legs":exploration_legs,
                                "meters":round(sum(leg["meters"] for leg in exploration_legs),2)},
            "distanceMethod":"实体三角面边/面内连接图 Dijkstra；每条线段实际在面内，长度为可行路线的上界，非 A* funnel 最短距离",
            "verified":["同源地面覆盖与逐面高程匹配","全部导航面共边连通","15 条道路无交叠且两端桥口共边连接","全部标记落地可达",
                        "桥道单片转向最多10度、坡度变化小于5度、桥口坡度小于3度",
                        "非退化/绕序/流形","坡度小于18度","障碍和边界0.7米余量","A*顶点预算"],
            "runtimeVerification":"未运行 Unity / 游戏内行走验证"}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output",type=Path,default=UNITY_PROJECT/"Assets/SkyIsland/sky_island_layout.json")
    parser.add_argument("--source-copy",type=Path,default=ROOT/"ArtSource/SkyIsland/layout.json")
    parser.add_argument("--validate",type=Path,help="只验证已有 JSON，不重新生成")
    args=parser.parse_args()
    if args.validate:
        report=validate_layout(json.loads(args.validate.read_text(encoding="utf-8")))
    else:
        layout=build_layout()
        content=json.dumps(layout,ensure_ascii=False,indent=2)+"\n"
        for path in (args.output,args.source_copy):
            path.parent.mkdir(parents=True,exist_ok=True)
            path.write_text(content,encoding="utf-8")
        report=layout["validation"]
        report_path=args.source_copy.parent/"navigation_validation.json"
        report_path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(report,ensure_ascii=False,indent=2))


if __name__=="__main__":
    main()
