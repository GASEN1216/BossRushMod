"""天空岛输出的独立几何回归；只用标准库，不依赖 Blender、Unity 或 Shapely。

直接检查可发布 JSON 的实际三角面、实体高程、岛桥共边、避障、边界余量和标记。
随后破坏导航和桥道数据，逐项验证检查器确实转红。不能替代游戏内实机行走。
"""

from collections import defaultdict, deque
import copy
import json
import math
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
LAYOUT=ROOT/"ArtSource/SkyIsland/layout.json"
EPS=0.00006


def xz(p):
    return (p[0],p[2])


def cross(a,b,c):
    return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])


def inside(p,tri):
    values=[cross(a,b,p) for a,b in zip(tri,tri[1:]+tri[:1])]
    tolerance=EPS*max(1,max(math.dist(a,b) for a,b in zip(tri,tri[1:]+tri[:1])))
    return min(values)>=-tolerance or max(values)<=tolerance


def height(point,coords):
    a,b,c=[xz(p) for p in coords]
    total=cross(a,b,c)
    wb=cross(a,point,c)/total
    wc=cross(a,b,point)/total
    return coords[0][1]+wb*(coords[1][1]-coords[0][1])+wc*(coords[2][1]-coords[0][1])


def bounds(points):
    return (min(p[0] for p in points),min(p[1] for p in points),
            max(p[0] for p in points),max(p[1] for p in points))


def overlaps(a,b,margin=0):
    return a[0]<=b[2]+margin and b[0]<=a[2]+margin and a[1]<=b[3]+margin and b[1]<=a[3]+margin


def area(poly):
    return abs(sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(poly,poly[1:]+poly[:1])))/2 if len(poly)>2 else 0


def rectangle_intersection(poly,rect):
    for axis,value,sign in [(0,rect[0],1),(0,rect[2],-1),(1,rect[1],1),(1,rect[3],-1)]:
        if not poly:
            return []
        result=[]
        for a,b in zip(poly,poly[1:]+poly[:1]):
            ain=(a[axis]-value)*sign>=0
            bin=(b[axis]-value)*sign>=0
            if ain:
                result.append(a)
            if ain!=bin:
                t=(value-a[axis])/(b[axis]-a[axis])
                result.append((a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])))
        poly=result
    return poly


def point_segment_distance(p,a,b):
    length=(b[0]-a[0])**2+(b[1]-a[1])**2
    if length<1e-15:
        return math.dist(p,a)
    t=max(0,min(1,((p[0]-a[0])*(b[0]-a[0])+(p[1]-a[1])*(b[1]-a[1]))/length))
    return math.dist(p,(a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])))


def segment_distance(a,b,c,d):
    if cross(a,b,c)*cross(a,b,d)<0 and cross(c,d,a)*cross(c,d,b)<0:
        return 0
    return min(point_segment_distance(a,c,d),point_segment_distance(b,c,d),
               point_segment_distance(c,a,b),point_segment_distance(d,a,b))


def check_bridge_continuity(layout):
    """检查发布几何的转角、桥口和拼接，独立于生成器的曲线算法。"""
    ground=layout["ground"]
    ground_faces=defaultdict(list)
    for face,region in zip(ground["triangles"],ground["triangleRegions"]):
        ground_faces[region].append([ground["vertices"][i] for i in face])
    islands={island["id"]:island for island in layout["islands"]}
    max_turn=0.0; max_grade_change=0.0; max_mouth_slope=0.0
    for bridge in layout["bridges"]:
        bid=bridge["id"]; path=bridge["path"]; sections=bridge["crossSections"]
        assert len(path)>=2 and len(path)==len(sections),"桥横截面表长度: "+bid
        directions=[]; grades=[]
        for a,b in zip(path,path[1:]):
            distance=math.dist(xz(a),xz(b))
            assert distance>0.05,"桥道重复采样点: "+bid
            directions.append(((b[0]-a[0])/distance,(b[2]-a[2])/distance))
            grades.append(math.degrees(math.atan2(b[1]-a[1],distance)))
        for a,b in zip(directions,directions[1:]):
            turn=abs(math.degrees(math.atan2(a[0]*b[1]-a[1]*b[0],a[0]*b[0]+a[1]*b[1])))
            assert turn<=10.001,"桥道突兀转角: "+bid
            max_turn=max(max_turn,turn)
        for a,b in zip([0]+grades,grades+[0]):
            change=abs(b-a)
            assert change<5,"桥道坡度突变: "+bid
            max_grade_change=max(max_grade_change,change)
        mouth_slope=max(abs(grades[0]),abs(grades[-1]))
        assert mouth_slope<3,"桥口未缓坡: "+bid
        max_mouth_slope=max(max_mouth_slope,mouth_slope)
        for point,section in zip(path,sections):
            assert len(section)==2 and all(abs(p[1]-point[1])<EPS for p in section),"桥横截面高程错位: "+bid
            midpoint=[(section[0][i]+section[1][i])/2 for i in range(3)]
            assert math.dist(midpoint,point)<EPS,"桥横截面偏离中心线: "+bid
            width=math.dist(section[0],section[1])
            assert bridge["width"]-EPS<=width<=bridge["width"]*1.01,"圆弯桥面宽度异常: "+bid
        for endpoint,island_id,direction in [(0,bridge["from"],directions[0]),(-1,bridge["to"],directions[-1])]:
            island=islands[island_id]; section=sections[endpoint]
            assert abs(path[endpoint][1]-island["height"])<EPS,"桥口与岛面高程错位: "+bid
            span=(section[1][0]-section[0][0],section[1][2]-section[0][2])
            assert abs(span[0]*direction[0]+span[1]*direction[1])<EPS,"桥口非正交: "+bid
            outline=island["outline"]
            assert all(min(point_segment_distance(xz(p),a,b) for a,b in zip(outline,outline[1:]+outline[:1]))<EPS
                       for p in section),"桥口没有贴合岛岸: "+bid
        expected=[]
        for a,b in zip(sections,sections[1:]):
            expected.extend([(a[0],a[1],b[1]),(a[0],b[1],b[0])])
        surfaces=bridge["surfaceTriangles"]
        assert len(surfaces)==len(expected)==len(ground_faces[bid]),"桥面拼接缺片: "+bid
        for seam,visual,physical in zip(expected,surfaces,ground_faces[bid]):
            assert all(any(math.dist(p,q)<EPS for q in visual) for p in seam),"桥面接缝错位: "+bid
            assert all(any(math.dist(p,q)<EPS for q in physical) for p in seam),"桥面与实体碰撞错位: "+bid
    return {"maxBridgeTurnDegrees":round(max_turn,4),"maxBridgeGradeChangeDegrees":round(max_grade_change,4),
            "maxBridgeMouthSlopeDegrees":round(max_mouth_slope,4)}


def check(layout):
    bridge_report=check_bridge_continuity(layout)
    nav=layout["navigation"]; ground=layout["ground"]
    vertices=nav["vertices"]; faces=nav["triangles"]; regions=nav["triangleRegions"]
    assert 0<len(vertices)<4095,"A* 顶点预算"
    assert faces and len(faces)==len(regions)==len(nav["sourceGroundFaces"]),"导航表长度"
    assert all(math.isfinite(v) for p in vertices for v in p),"非有限坐标"
    edges=defaultdict(list); polygons=[]; triangle_bounds=[]; max_slope=0; total_area=0
    for index,face in enumerate(faces):
        assert len(face)==3 and len(set(face))==3 and all(0<=i<len(vertices) for i in face),"退化/越界三角形"
        points=[vertices[i] for i in face]; poly=[xz(p) for p in points]
        signed=cross(*poly)
        assert signed<-1e-8,"绕序或退化"
        polygons.append(poly); triangle_bounds.append(bounds(poly)); total_area-=signed/2
        source=ground["triangles"][nav["sourceGroundFaces"][index]]
        source_points=[ground["vertices"][i] for i in source]
        source_poly=[xz(p) for p in source_points]
        assert all(inside(p,source_poly) for p in poly),"导航超出对应实体面"
        assert all(abs(p[1]-height(xz(p),source_points))<EPS for p in points),"导航与碰撞高程不一致"
        a,b,c=points; u=[b[i]-a[i] for i in range(3)]; v=[c[i]-a[i] for i in range(3)]
        normal=(u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0])
        slope=math.degrees(math.atan2(math.hypot(normal[0],normal[2]),normal[1]))
        assert slope<18,"坡度超过 18 度"
        max_slope=max(max_slope,slope)
        for a,b in zip(face,face[1:]+face[:1]):
            edges[tuple(sorted((a,b)))].append(index)
    assert all(len(uses)<=2 for uses in edges.values()),"非流形/重叠三角形"
    adjacency=defaultdict(set); interfaces=set()
    for uses in edges.values():
        if len(uses)==2:
            a,b=uses; adjacency[a].add(b); adjacency[b].add(a)
            interfaces.add(frozenset((regions[a],regions[b])))
    seen={0}; queue=deque([0])
    while queue:
        for nxt in adjacency[queue.popleft()]:
            if nxt not in seen:
                seen.add(nxt); queue.append(nxt)
    assert len(seen)==len(faces),"导航共边断开"
    assert len(layout["islands"])==12 and len(layout["bridges"])==15,"岛/桥数量"
    for bridge in layout["bridges"]:
        for end in (bridge["from"],bridge["to"]):
            assert frozenset((bridge["id"],end)) in interfaces,"桥口断开: "+bridge["id"]
    clearance=layout["clearance"]
    for obstacle in layout["obstacles"]:
        x0,z0,x1,z1=obstacle["bounds"]
        rect=(x0-clearance+EPS,z0-clearance+EPS,x1+clearance-EPS,z1+clearance-EPS)
        for poly,bbox in zip(polygons,triangle_bounds):
            if overlaps(bbox,rect):
                assert area(rectangle_intersection(poly,rect))<0.0001,"导航侵入障碍: "+obstacle["id"]
    # 对实际碰撞地面的全部边界线测最近距离，不只采几个岛中心。
    boundary=[]
    for a,b in ground["boundaryEdges"]:
        p,q=xz(ground["vertices"][a]),xz(ground["vertices"][b])
        boundary.append((p,q,bounds([p,q])))
    for poly,bbox in zip(polygons,triangle_bounds):
        for a,b,edge_bounds in boundary:
            if not overlaps(bbox,edge_bounds,clearance):
                continue
            minimum=min(segment_distance(a,b,c,d) for c,d in zip(poly,poly[1:]+poly[:1]))
            assert minimum>=clearance-EPS,"岛缘/障碍边界安全余量不足"
    ids={m["id"] for m in layout["markers"]}
    assert len(ids)==len(layout["markers"]),"重复标记"
    for island in layout["islands"]:
        for prefix in ("Region_","EnemySpawn_","Search_","POI_"):
            assert prefix+island["id"] in ids,"区域必要标记缺失"
        if not island["id"].startswith("S"):
            assert "Lamp_"+island["id"] in ids,"主区灯标记缺失"
    for marker in layout["markers"]:
        point=xz(marker["position"])
        matching=[i for i,(poly,bbox) in enumerate(zip(polygons,triangle_bounds))
                  if overlaps(bbox,(*point,*point),EPS) and inside(point,poly)]
        assert matching,"标记落在空地/障碍: "+marker["id"]
        assert any(abs(marker["position"][1]-height(point,[vertices[j] for j in faces[i]]))<EPS for i in matching),"标记未落地"
    assert abs(total_area-layout["validation"]["navigationAreaXZ"])<0.02,"面积报告漂移"
    return {"vertices":len(vertices),"triangles":len(faces),"area":round(total_area,3),
            "slope":round(max_slope,4),"markers":len(ids),**bridge_report}


def expect_rejected(layout,mutation,label,reason=None):
    changed=copy.deepcopy(layout)
    mutation(changed)
    try:
        check(changed)
    except AssertionError as error:
        assert reason is None or reason in str(error),"破坏触发了非预期检查: "+label+" / "+str(error)
        return
    raise AssertionError("人为破坏未被拒绝: "+label)


def main():
    assert LAYOUT.exists(),"缺少随资源维护的 ArtSource/SkyIsland/layout.json"
    layout=json.loads(LAYOUT.read_text(encoding="utf-8"))
    report=check(layout)
    def remove_bridge(data):
        keep=[i for i,r in enumerate(data["navigation"]["triangleRegions"]) if r!="AB"]
        for key in ("triangles","triangleRegions","sourceGroundFaces"):
            data["navigation"][key]=[data["navigation"][key][i] for i in keep]
    def duplicate_face(data):
        for key in ("triangles","triangleRegions","sourceGroundFaces"):
            data["navigation"][key].append(data["navigation"][key][0])
    def move_spawn(data):
        next(m for m in data["markers"] if m["id"]=="PlayerSpawn")["position"]=[0,0,-380]
    def add_blocker(data):
        data["obstacles"].append({"id":"injected_blocker","bounds":[-5,-255,5,-200]})
    cases=[(remove_bridge,"删除AB桥"),(duplicate_face,"重复三角面"),(move_spawn,"出生点移到虚空"),
           (add_blocker,"障碍未从导航剔除"),
           (lambda d:d["navigation"]["vertices"][0].__setitem__(1,100),"抬高导航顶点"),
           (lambda d:d["navigation"]["vertices"][0].__setitem__(0,900),"导航越岛缘"),
           (lambda d:d["navigation"]["vertices"].extend([[0,0,0]]*4095),"A*超预算")]
    for mutation,label in cases:
        expect_rejected(layout,mutation,label)
    def hard_turn(data):
        bridge=next(b for b in data["bridges"] if b["id"]=="BF")
        bridge["path"][8][0]+=8
    def unweld_bridge_mouth(data):
        mesh=data["navigation"]
        shores={v for face,region in zip(mesh["triangles"],mesh["triangleRegions"]) if region=="A" for v in face}
        duplicates={}
        for face,region in zip(mesh["triangles"],mesh["triangleRegions"]):
            if region!="AB":
                continue
            for index,vertex in enumerate(face):
                if vertex not in shores:
                    continue
                if vertex not in duplicates:
                    duplicates[vertex]=len(mesh["vertices"])
                    mesh["vertices"].append(list(mesh["vertices"][vertex]))
                face[index]=duplicates[vertex]
    bridge_cases=[
        (hard_turn,"重新引入急转角","桥道突兀转角"),
        (lambda d:d["bridges"][0]["path"][1].__setitem__(1,2),"桥口突然起坡","桥道坡度突变"),
        (lambda d:d["bridges"][0]["crossSections"][3][0].__setitem__(0,-7),"横截面错开","桥横截面偏离中心线"),
        (lambda d:d["bridges"][0]["surfaceTriangles"][0][0].__setitem__(1,.5),"桥面片接缝抬高","桥面接缝错位"),
        (unweld_bridge_mouth,"桥口重合坐标未焊接","导航共边断开"),
    ]
    for mutation,label,reason in bridge_cases:
        expect_rejected(layout,mutation,label,reason)
    print("SkyIslandNavigationPropertyTest: PASS",json.dumps(report,ensure_ascii=False),
          str(len(cases)+len(bridge_cases))+" mutations rejected")


if __name__=="__main__":
    main()
