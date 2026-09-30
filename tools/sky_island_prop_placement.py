"""Author-time furniture/hero-prop placement using final roads and actual model bounds.
No Blender or runtime dependency. All translations are baked into the original mesh.

2026-09-30 second pass: every emitter that draws a thing at a fixed coordinate goes
through `PlacementSpace.fit` (landmark furniture, lanterns, scatter trees / rocks,
dock crates, kiosks …). `fit` measures the ACTUAL emitted mesh, clipped to the
player body slab (0.06–2.2 m above the island) and to the overhead slab, as convex
hulls; the hulls are tested against the final road surface (+curb), bridge-mouth
corridors, doorways, gameplay markers, runtime-spawned objects
(`ArtSource/SkyIsland/runtime_placements.json`) and every earlier hard hull. A
blocked thing is translated to the nearest clear spot within its search radius or
removed; nothing is decided from centre points or nominal radii.
"""
import json
import math
from pathlib import Path

from sky_island_dressing import inside, distance_to_segment

ROOT = Path(__file__).resolve().parents[1]
BODY_LOW, BODY_HIGH, HIGH_TOP = 0.06, 2.2, 12.0
ROAD_CURB = 0.2            # hard things keep this clear of the paved edge (road planner CLEARANCE)
FLORA_ROAD_OVERLAP = 0.15  # soft planting may lean this far over the paved edge
HULL_GAP = 0.05
EDGE_MARGIN = 0.65
# 与 tools/sky_island_scene_audit.py 同一张表：玩法标记周围要让出的水平半径。
MARKER_CLEAR = {'search': 1.4, 'point_of_interest': 1.6, 'spawn': 2.0, 'extraction': 3.2,
                'enemy_spawn': 1.6, 'lamp': 1.2, 'relay': 2.0}
# 运行时交互 / 摆放物：花草也要让开这些（站位、开箱、读条的地方）。
# 巡守槽位由 tools/sky_island_patrol_slots.py 从导航三角形中心重取样，而导航又由本次几何烘出。生成器若给它们让位，
# 几何 → 导航 → 槽位 → 几何成环，重跑结果随上一轮槽位漂移（2026-09-30 复测：G 岛一座小屋漂了 0.866 m）。
# 槽位落在导航面上就已经避开了硬物；场景审计照样按最终槽位查净空。其余运行时物件来自固定数据与 layout.json。
DERIVED_RUNTIME_KINDS = {'patrol_slot'}
SOFT_BLOCKING = {'bridge_mouth', 'doorway', 'search', 'lamp', 'runtime_loot_crate', 'runtime_bounty_crate',
                 'runtime_memorial_stake', 'runtime_hearth', 'runtime_gather_node', 'runtime_search_point',
                 'runtime_bridge_sign_post', 'runtime_story_gate'}


def corners(bounds):
    x0,z0,x1,z1=bounds
    return [(x0,z0),(x1,z0),(x1,z1),(x0,z1)]


def segment_distance(a,b,c,d):
    def cross(p,q,r):return (q[0]-p[0])*(r[1]-p[1])-(q[1]-p[1])*(r[0]-p[0])
    ca,cb,cc,cd=cross(a,b,c),cross(a,b,d),cross(c,d,a),cross(c,d,b)
    if ca*cb<=0 and cc*cd<=0 and (max(min(a[0],b[0]),min(c[0],d[0]))<=min(max(a[0],b[0]),max(c[0],d[0]))
            and max(min(a[1],b[1]),min(c[1],d[1]))<=min(max(a[1],b[1]),max(c[1],d[1]))):return 0.0
    return min(distance_to_segment(a,c,d),distance_to_segment(b,c,d),distance_to_segment(c,a,b),distance_to_segment(d,a,b))


def rectangle_segment_distance(bounds,a,b):
    x0,z0,x1,z1=bounds
    if any(x0<=p[0]<=x1 and z0<=p[1]<=z1 for p in (a,b)):return 0.0
    points=corners(bounds)
    return min(segment_distance(c,d,a,b) for c,d in zip(points,points[1:]+points[:1]))


def transformed_bounds(local,x,z,yaw):
    c,s=math.cos(math.radians(yaw)),math.sin(math.radians(yaw))
    points=[(x+px*c+pz*s,z-px*s+pz*c) for px,pz in corners(local)]
    return [min(p[0] for p in points),min(p[1] for p in points),max(p[0] for p in points),max(p[1] for p in points)]


def model_bounds(payload):
    import sky_island_botany
    parts=sky_island_botany.model(payload['meta']['name'],payload['meta']['bounds'])
    points=[p for data in parts.values() for p in data['v']] if parts is not None else payload['mesh']['v']
    if not points or not all(math.isfinite(v) for p in points for v in p):raise ValueError('Invalid placement geometry')
    return [min(p[0] for p in points),min(p[2] for p in points),max(p[0] for p in points),max(p[2] for p in points)]


# ── convex-hull geometry (pure Python; runs inside Blender) ─────────────────

def hull(points):
    """Monotone-chain convex hull, CCW. Degenerate inputs become a thin sliver so SAT still works."""
    pts=sorted(set((round(p[0],4),round(p[1],4)) for p in points))
    if not pts:
        return []
    if len(pts)<3:
        a,b=pts[0],pts[-1]
        dx,dz=b[0]-a[0],b[1]-a[1];length=math.hypot(dx,dz)
        nx,nz=(-dz/length*.02,dx/length*.02) if length>1e-9 else (.02,0.0)
        if length<=1e-9:
            return [(a[0]-.02,a[1]-.02),(a[0]+.02,a[1]-.02),(a[0]+.02,a[1]+.02),(a[0]-.02,a[1]+.02)]
        return [(a[0]-nx,a[1]-nz),(b[0]-nx,b[1]-nz),(b[0]+nx,b[1]+nz),(a[0]+nx,a[1]+nz)]
    def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
    lower,upper=[],[]
    for p in pts:
        while len(lower)>=2 and cross(lower[-2],lower[-1],p)<=0:lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper)>=2 and cross(upper[-2],upper[-1],p)<=0:upper.pop()
        upper.append(p)
    result=lower[:-1]+upper[:-1]
    return result if len(result)>=3 else hull([pts[0],pts[-1]])


def clip_face(points,low,high):
    """Sutherland–Hodgman against low<=y<=high; returns clipped 3D polygon."""
    for limit,keep_above in ((low,True),(high,False)):
        if not points:
            return []
        out=[]
        for i,p in enumerate(points):
            q=points[(i+1)%len(points)]
            pin=p[1]>=limit if keep_above else p[1]<=limit
            qin=q[1]>=limit if keep_above else q[1]<=limit
            if pin:
                out.append(p)
            if pin!=qin and abs(q[1]-p[1])>1e-12:
                t=(limit-p[1])/(q[1]-p[1])
                out.append((p[0]+(q[0]-p[0])*t,limit,p[2]+(q[2]-p[2])*t))
        points=out
    return points


def slab_hulls(spans,low,high,join=.15):
    """Convex hulls (XZ) of the emitted faces clipped to low<=y<=high, one per touching cluster.

    A table and its stools, or two bunting poles 30 m apart, stay separate footprints
    instead of one hull that would sweep across the road between them.
    """
    pieces=[]
    for verts,faces in spans:
        for face in faces:
            poly=[verts[i] for i in face]
            ys=[p[1] for p in poly]
            if max(ys)<low or min(ys)>high:
                continue
            pts=[(p[0],p[2]) for p in (poly if min(ys)>=low and max(ys)<=high else clip_face(poly,low,high))]
            if pts:
                pieces.append((pts,(min(p[0] for p in pts),min(p[1] for p in pts),max(p[0] for p in pts),max(p[1] for p in pts))))
    if not pieces:
        return []
    # 相接判定按占用格：每块截面把包围盒（外扩 join）压进 0.25 m 格，八邻接连通的格子是一簇。
    # 线性于占用格数，稠密的 Tripo 房子（上万面）也不会退化成两两比较。
    size=max(join,.25) if join<1e8 else 1e8
    owner={}
    for index,(_,box) in enumerate(pieces):
        if size>=1e8:
            owner.setdefault((0,0),[]).append(index);continue
        for i in range(int(math.floor((box[0]-join)/size)),int(math.floor((box[2]+join)/size))+1):
            for j in range(int(math.floor((box[1]-join)/size)),int(math.floor((box[3]+join)/size))+1):
                owner.setdefault((i,j),[]).append(index)
    label={}
    component=0
    for start in owner:
        if start in label:
            continue
        stack=[start];label[start]=component
        while stack:
            ci,cj=stack.pop()
            for di in (-1,0,1):
                for dj in (-1,0,1):
                    cell=(ci+di,cj+dj)
                    if cell in owner and cell not in label:
                        label[cell]=component;stack.append(cell)
        component+=1
    clusters={}
    for cell,members in owner.items():
        clusters.setdefault(label[cell],set()).update(members)
    clusters={key:[point for index in members for point in pieces[index][0]] for key,members in clusters.items()}
    return [hull(points) for points in clusters.values()]


def slab_hull(spans,low,high):
    """Single convex hull of the clipped faces (legacy callers)."""
    pts=[point for poly in slab_hulls(spans,low,high,join=1e9) for point in poly]
    return hull(pts) if pts else []


def _axes(poly):
    for a,b in zip(poly,poly[1:]+poly[:1]):
        dx,dz=b[0]-a[0],b[1]-a[1];length=math.hypot(dx,dz)
        if length>1e-9:
            yield (-dz/length,dx/length)


def separation(a,b):
    """SAT gap (lower bound of the true distance); <=0 means the convex polygons overlap."""
    best=-math.inf
    for axis in list(_axes(a))+list(_axes(b)):
        pa=[p[0]*axis[0]+p[1]*axis[1] for p in a]
        pb=[p[0]*axis[0]+p[1]*axis[1] for p in b]
        best=max(best,min(pb)-max(pa),min(pa)-max(pb))
    return best


def point_in_convex(poly,p):
    sign=0
    for a,b in zip(poly,poly[1:]+poly[:1]):
        c=(b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])
        if abs(c)<1e-12:
            continue
        s=1 if c>0 else -1
        if sign==0:
            sign=s
        elif s!=sign:
            return False
    return True


def poly_segment_distance(poly,a,b):
    if point_in_convex(poly,a) or point_in_convex(poly,b):
        return 0.0
    return min(segment_distance(c,d,a,b) for c,d in zip(poly,poly[1:]+poly[:1]))


def poly_point_distance(poly,p):
    if point_in_convex(poly,p):
        return 0.0
    return min(distance_to_segment(p,a,b) for a,b in zip(poly,poly[1:]+poly[:1]))


def shift(poly,dx,dz):
    return [(x+dx,z+dz) for x,z in poly]


def bbox(poly):
    return (min(p[0] for p in poly),min(p[1] for p in poly),max(p[0] for p in poly),max(p[1] for p in poly))


def circle(x,z,r,n=16):
    """外切正多边形：比真圆大一圈，净空只会更严（内接多边形会让物件钻进圆里 r·(1-cos π/n)）。"""
    r=r/math.cos(math.pi/n)
    return [(x+r*math.cos(i*math.tau/n),z+r*math.sin(i*math.tau/n)) for i in range(n)]


GRID=8.0


def _cells(box,pad):
    x0,z0,x1,z1=box
    for i in range(int(math.floor((x0-pad)/GRID)),int(math.floor((x1+pad)/GRID))+1):
        for j in range(int(math.floor((z0-pad)/GRID)),int(math.floor((z1+pad)/GRID))+1):
            yield (i,j)


LAMPS_KEY='__lamp_lights__'


def snapshot(g):
    state={key:(len(data['v']),len(data['f'])) for key,data in g.GROUPS.items()}
    # 夜灯灯位跟着它所在的件走：平移时一起平移、撤下时一起撤掉（生成器 LAMP_LIGHTS）。
    state[LAMPS_KEY]=len(getattr(g,'LAMP_LIGHTS',()))
    return state


def translate_lamps(g,before,dx,dz):
    lamps=getattr(g,'LAMP_LIGHTS',None)
    if lamps is None:
        return
    for lamp in lamps[before.get(LAMPS_KEY,len(lamps)):]:
        x,y,z=lamp['position']
        lamp['position']=[round(x+dx,3),y,round(z+dz,3)]


def emitted(g,before):
    """[(key, v0, v1, f0, f1)] appended to every group since `before`."""
    rows=[]
    for key,data in g.GROUPS.items():
        v0,f0=before.get(key,(0,0))
        if len(data['f'])>f0 or len(data['v'])>v0:
            rows.append((key,v0,len(data['v']),f0,len(data['f'])))
    return rows


def spans_of(g,rows):
    """Faces re-indexed into their local vertex slice (vertex positions are live)."""
    out=[]
    for key,v0,v1,f0,f1 in rows:
        data=g.GROUPS[key]
        verts=data['v'][v0:v1]
        faces=[tuple(i-v0 for i in face) for face in data['f'][f0:f1]]
        out.append((verts,faces))
    return out


def translate(g,rows,dx,dz):
    for key,v0,v1,_,_ in rows:
        data=g.GROUPS[key]
        data['v'][v0:v1]=[(x+dx,y,z+dz) for x,y,z in data['v'][v0:v1]]


def rollback(g,before):
    hook=getattr(g,'ledger_rollback',None)
    if hook is not None:
        hook(before)
    lamps=getattr(g,'LAMP_LIGHTS',None)
    if lamps is not None and LAMPS_KEY in before:
        del lamps[before[LAMPS_KEY]:]
    for key in list(g.GROUPS):
        v0,f0=before.get(key,(0,0))
        data=g.GROUPS[key]
        data['v']=data['v'][:v0];data['uv']=data['uv'][:v0]
        data['f']=data['f'][:f0];data['smooth']=data['smooth'][:f0]
        if 'corner_normals' in data:
            data['corner_normals']=data['corner_normals'][:f0]
        if not data['v'] and key not in before:
            del g.GROUPS[key]


class PlacementSpace:
    def __init__(self,g,layout,actual_bounds,roads=None,runtime=None,registered_hulls=None):
        self.islands={i['id']:i for i in layout['islands']}
        self.tracks=list(g.PAVING_TRACKS)
        self.markers=layout['markers']
        self.reserved=[{'id':o['id'],'island':o['island'],'bounds':actual_bounds.get(o['id'],o['bounds'])} for o in layout['obstacles']]
        self.records=[]
        self.omitted=[]
        # Hull reservations: {'id','island','layer':'body'|'high','poly','category'}
        self.hulls=[]
        for identifier,row in (registered_hulls or {}).items():
            for layer in ('body','high'):
                for poly in row.get(layer) or []:
                    if poly:
                        self.hulls.append({'id':identifier,'island':row['island'],'layer':layer,
                                           'poly':poly,'category':row.get('category','hard')})
        self.zones=[]
        self._zones(layout,roads,runtime)
        # Uniform 8 m grids keep the translate-search linear in local neighbours.
        self._segments={}
        for samples,width in self.tracks:
            for a,b in zip(samples,samples[1:]):
                box=(min(a[0],b[0]),min(a[1],b[1]),max(a[0],b[0]),max(a[1],b[1]))
                for cell in _cells(box,width/2+ROAD_CURB+.1):
                    self._segments.setdefault(cell,[]).append((a,b,width))
        self._zone_grid={}
        for zone in self.zones:
            for cell in _cells(zone['bbox'],0):
                self._zone_grid.setdefault(cell,[]).append(zone)
        self._hull_grid={}
        for row in self.hulls:
            self._index(row)

    # ── protected zones ──────────────────────────────────────────────────
    def _zones(self,layout,roads,runtime):
        def island_of(x,z):
            return next((i['id'] for i in self.islands.values() if inside((x,z),i['outline'])),None)
        for marker in self.markers:
            radius=MARKER_CLEAR.get(marker['kind'])
            if radius:
                x,_,z=marker['position']
                self.zones.append({'id':marker['id'],'kind':marker['kind'],'island':marker.get('island'),
                                   'poly':circle(x,z,radius)})
        if roads:
            for portal in roads.get('portals',[]):
                p,d=portal['point'],portal['inward'];length=max(3.0,min(8.0,portal['straightLength']))
                half=portal['bridgeWidth']/2;nx,nz=-d[1],d[0]
                q=(p[0]+d[0]*length,p[1]+d[1]*length)
                self.zones.append({'id':portal['island']+'@'+portal['bridge'],'kind':'bridge_mouth','island':portal['island'],
                                   'poly':[(p[0]+nx*half,p[1]+nz*half),(q[0]+nx*half,q[1]+nz*half),
                                           (q[0]-nx*half,q[1]-nz*half),(p[0]-nx*half,p[1]-nz*half)]})
            for front in roads.get('frontages',[]):
                x,z=front['point']
                self.zones.append({'id':front['obstacle'],'kind':'doorway','island':front['island'],
                                   'poly':circle(x,z,1.6)})
        for row in (runtime or {}).get('points',[]):
            if row['kind'] in DERIVED_RUNTIME_KINDS:
                continue
            if row['kind']=='story_gate':
                yaw=math.radians(row['yaw']);hw,hd=(row['width']+1)/2,.5
                poly=[(row['x']+cx*math.cos(yaw)+cz*math.sin(yaw),row['z']-cx*math.sin(yaw)+cz*math.cos(yaw))
                      for cx,cz in ((-hw,-hd),(hw,-hd),(hw,hd),(-hw,hd))]
            elif row.get('clearRadius',0)>0:
                poly=circle(row['x'],row['z'],row['clearRadius'])
            else:
                continue
            self.zones.append({'id':row['id'],'kind':'runtime_'+row['kind'],'island':island_of(row['x'],row['z']),
                               'poly':poly})
        for zone in self.zones:
            zone['bbox']=bbox(zone['poly'])

    # ── admission ────────────────────────────────────────────────────────
    def admissible(self,sid,bodies,highs,category='hard',ignore=()):
        """None when clear, else a short reason (kept in records for evidence)."""
        for body in bodies:
            why=self._admit_body(sid,body,category,ignore)
            if why:
                return why
        if category!='flora':
            for high in highs:
                why=self._admit_high(sid,high,category,ignore)
                if why:
                    return why
        return None

    def _admit_body(self,sid,body,category,ignore):
        island=self.islands[sid];outline=island['outline']
        if body:
            margin=EDGE_MARGIN if category!='flora' else .3
            if not all(inside(p,outline) for p in body):
                return 'outside_island'
            box=bbox(body)
            for a,b in zip(outline,outline[1:]+outline[:1]):
                if min(a[0],b[0])>box[2]+margin or max(a[0],b[0])<box[0]-margin or \
                        min(a[1],b[1])>box[3]+margin or max(a[1],b[1])<box[1]-margin:
                    continue
                if poly_segment_distance(body,a,b)<margin:
                    return 'island_edge'
            curb=ROAD_CURB if category!='flora' else -FLORA_ROAD_OVERLAP
            seen=set()
            for cell in _cells(box,0):
                for a,b,width in self._segments.get(cell,()):
                    if (a,b) in seen:
                        continue
                    seen.add((a,b))
                    limit=width/2+curb
                    if (min(a[0],b[0])>box[2]+limit or max(a[0],b[0])<box[0]-limit
                            or min(a[1],b[1])>box[3]+limit or max(a[1],b[1])<box[1]-limit):
                        continue
                    if poly_segment_distance(body,a,b)<limit-1e-6:
                        return 'road'
            for zone in self._near(self._zone_grid,box):
                if zone['id'] in ignore or (zone['island'] not in (None,sid)):
                    continue
                if category=='flora' and zone['kind'] not in SOFT_BLOCKING:
                    continue
                zb=zone['bbox']
                if zb[0]>box[2] or zb[2]<box[0] or zb[1]>box[3] or zb[3]<box[1]:
                    continue
                if separation(body,zone['poly'])<=0:
                    return 'zone:'+zone['id']
            for row in self._near(self._hull_grid,box):
                if row['island']!=sid or row['layer']!='body' or row['id'] in ignore:
                    continue
                if category=='flora' and row['category']=='flora':
                    continue
                if separation(body,row['poly'])<(HULL_GAP if category!='flora' else -.25):
                    return 'hull:'+row['id']
        return None

    def _admit_high(self,sid,high,category,ignore):
        if high:
            for row in self._near(self._hull_grid,bbox(high)):
                if row['island']!=sid or row['layer']!='high' or row['id'] in ignore:
                    continue
                # Crowns may interlace with crowns; structures may not stab into either.
                if category=='tree' and row['category']=='tree':
                    continue
                if separation(high,row['poly'])<HULL_GAP:
                    return 'high:'+row['id']
        return None

    def _index(self,row):
        row['bbox']=bbox(row['poly'])
        for cell in _cells(row['bbox'],0):
            self._hull_grid.setdefault(cell,[]).append(row)

    @staticmethod
    def _near(grid,box):
        seen=set()
        for cell in _cells(box,0):
            for row in grid.get(cell,()):
                if id(row) not in seen:
                    seen.add(id(row))
                    yield row

    def reserve(self,identifier,sid,bodies,highs,category):
        for layer,polys in (('body',bodies),('high',highs)):
            for poly in polys:
                if poly:
                    row={'id':identifier,'island':sid,'layer':layer,'poly':poly,'category':category}
                    self.hulls.append(row);self._index(row)

    def reserve_emitted(self,g,name,before,low=.13):
        """Reserve hulls of everything emitted since `before` (ground detail excluded), split by island."""
        rows=[row for row in emitted(g,before) if not str(row[0][0]).endswith('_GroundDetail')]
        by_island={}
        for key,v0,v1,f0,f1 in rows:
            sid=str(key[0]).split('_')[0]
            if sid in self.islands:
                by_island.setdefault(sid,[]).append((key,v0,v1,f0,f1))
        for sid,island_rows in by_island.items():
            gy=self.islands[sid]['height'];spans=spans_of(g,island_rows)
            self.reserve(sid+'_'+name,sid,slab_hulls(spans,gy+low,gy+BODY_HIGH),slab_hulls(spans,gy+BODY_HIGH,gy+HIGH_TOP),'hard')

    def fit(self,g,sid,name,emit,category='hard',search=6.0,ground=None,reserve=True,ignore=(),step=.5,
            high_top=HIGH_TOP):
        """Emit, measure the real mesh, then keep / translate / remove it. Returns (dx, dz) or None."""
        before=snapshot(g)
        emit()
        rows=emitted(g,before)
        if not rows:
            return (0.0,0.0)
        gy=self.islands[sid]['height'] if ground is None else ground
        spans=spans_of(g,rows)
        body=slab_hulls(spans,gy+BODY_LOW,gy+BODY_HIGH)
        high=slab_hulls(spans,gy+BODY_HIGH,gy+high_top) if category!='flora' else []
        chosen=None;reason=None
        candidates=[(0.0,0.0)]
        radius=step
        while radius<=search+1e-9:
            count=max(8,int(math.tau*radius/step))
            candidates.extend((radius*math.cos(k*math.tau/count),radius*math.sin(k*math.tau/count)) for k in range(count))
            radius+=step
        for dx,dz in candidates:
            why=self.admissible(sid,[shift(poly,dx,dz) for poly in body],[shift(poly,dx,dz) for poly in high],category,ignore)
            if why is None:
                chosen=(dx,dz);break
            if reason is None:
                reason=why
        identifier=sid+'_'+name+'_'+str(len(self.records)+len(self.omitted))
        if chosen is None:
            rollback(g,before)
            self.omitted.append({'id':identifier,'island':sid,'model':name,'category':category,'reason':reason})
            return None
        dx,dz=chosen
        if dx or dz:
            translate(g,rows,dx,dz)
            translate_lamps(g,before,dx,dz)
        if reserve:
            self.reserve(identifier,sid,[shift(poly,dx,dz) for poly in body],[shift(poly,dx,dz) for poly in high],category)
        points=[point for poly in body for point in shift(poly,dx,dz)]
        box=bbox(points) if points else None
        self.records.append({'id':identifier,'island':sid,'model':name,'role':'fit','category':category,
                             'movedMeters':round(math.hypot(dx,dz),3),'firstBlock':reason if (dx or dz) else None,
                             'bounds':list(box) if box else None,'solidReservation':reserve})
        return chosen

    def blocked_circle(self,sid,x,z,radius,category='flora'):
        """PlantingSpace hook: circle footprint against hard hulls and interaction zones."""
        point=(x,z);box=(x-radius,z-radius,x+radius,z+radius)
        for row in self._near(self._hull_grid,box):
            if row['island']!=sid or row['layer']!='body' or row['category']=='flora':
                continue
            if poly_point_distance(row['poly'],point)<radius*.6:
                return True
        for zone in self._near(self._zone_grid,box):
            if zone['island'] not in (None,sid) or zone['kind'] not in SOFT_BLOCKING:
                continue
            zb=zone['bbox']
            if zb[0]>x+radius or zb[2]<x-radius or zb[1]>z+radius or zb[3]<z-radius:
                continue
            if poly_point_distance(zone['poly'],point)<radius*.6:
                return True
        return False

    # ── legacy AABB API (village porch furniture, Tripo anchors) ─────────
    def capture(self,g,sid,name,x,z,emit):
        before={key:len(data['v']) for key,data in g.GROUPS.items()}
        tracks=g.PAVING_TRACKS
        try:
            g.PAVING_TRACKS=[]
            emit()
        finally:g.PAVING_TRACKS=tracks
        points=[v for key,data in g.GROUPS.items() for v in data['v'][before.get(key,0):]]
        if not points:raise ValueError('Empty attached prop: '+name)
        local=[min(p[0] for p in points)-x,min(p[2] for p in points)-z,
               max(p[0] for p in points)-x,max(p[2] for p in points)-z]
        px,pz,_=self.place(sid,name,local,x,z,0,'building',reserve=name not in ('porch_garden','porch_hedge'))
        for key,data in g.GROUPS.items():
            start=before.get(key,0)
            data['v'][start:]=[(vx+px-x,vy,vz+pz-z) for vx,vy,vz in data['v'][start:]]
        return px,pz

    def nearest_road(self,sid,x,z):
        outline=self.islands[sid]['outline'];best=None
        for points,width in self.tracks:
            for a,b in zip(points,points[1:]):
                if not inside(((a[0]+b[0])/2,(a[1]+b[1])/2),outline):continue
                dx,dz=b[0]-a[0],b[1]-a[1];t=max(0,min(1,((x-a[0])*dx+(z-a[1])*dz)/max(dx*dx+dz*dz,1e-12)))
                point=(a[0]+t*dx,a[1]+t*dz);distance=math.dist((x,z),point)
                if best is None or distance<best[0]:best=(distance,point,width)
        return best

    def free(self,sid,bounds):
        island=self.islands[sid];points=corners(bounds);outline=island['outline']
        if not all(inside(p,outline) for p in points):return False
        for a,b in zip(outline,outline[1:]+outline[:1]):
            if rectangle_segment_distance(bounds,a,b)<.65:return False
        x0,z0,x1,z1=bounds
        for row in self.reserved:
            if row['island']!=sid:continue
            a,b,c,d=row['bounds']
            if x0<c+.45 and x1>a-.45 and z0<d+.45 and z1>b-.45:return False
        for marker in self.markers:
            if marker.get('island')!=sid or marker['kind']=='lamp':continue
            x,_,z=marker['position'];distance=math.hypot(max(x0-x,0,x-x1),max(z0-z,0,z-z1))
            if distance<1.1:return False
        for samples,width in self.tracks:
            half=width/2+.35
            for a,b in zip(samples,samples[1:]):
                if (min(a[0],b[0])>x1+half or max(a[0],b[0])<x0-half
                        or min(a[1],b[1])>z1+half or max(a[1],b[1])<z0-half):continue
                if rectangle_segment_distance(bounds,a,b)<half-1e-6:return False
        # Second pass: rectangle against hulls of everything already emitted and
        # against runtime / doorway / bridge-mouth zones (lamp markers stay exempt).
        rect=points
        for row in self._near(self._hull_grid,(x0,z0,x1,z1)):
            if row['island']!=sid or row['layer']!='body':continue
            if separation(rect,row['poly'])<HULL_GAP:return False
        for zone in self._near(self._zone_grid,(x0,z0,x1,z1)):
            if zone['island'] not in (None,sid):continue
            zb=zone['bbox']
            if zb[0]>x1 or zb[2]<x0 or zb[1]>z1 or zb[3]<z0:continue
            if separation(rect,zone['poly'])<=0:return False
        return True

    def place(self,sid,name,local,x,z,yaw=0,role='free',reserve=True,optional=False):
        original=[x,self.islands[sid]['height'],z];chosen=None
        # Keep valid authored placements. Search only when real geometry blocks a
        # road/prop, facing the same nearest road for seating and shop fronts.
        candidates=[(x,z)]
        for radius in [i*.75 for i in range(1,41)]:
            for step in range(24):
                angle=step*math.tau/24;candidates.append((x+radius*math.cos(angle),z+radius*math.sin(angle)))
        for px,pz in candidates:
            heading=yaw
            if role=='roadside':
                nearest=self.nearest_road(sid,px,pz)
                if nearest:
                    ax,az=nearest[1];heading=math.degrees(math.atan2(-(ax-px),-(az-pz)))
            bounds=transformed_bounds(local,px,pz,heading)
            if self.free(sid,bounds):chosen=(px,pz,heading,bounds);break
        if chosen is None:
            if optional:
                self.omitted.append({'id':sid+'_'+name+'_'+str(len(self.records)+len(self.omitted)),'island':sid,
                                     'model':name,'category':role,'reason':'no_clear_spot_30m'})
                return None
            raise ValueError('No clear authored placement: '+sid+'/'+name)
        px,pz,heading,bounds=chosen;identifier=sid+'_'+name+'_'+str(len(self.records))
        if reserve:
            self.reserved.append({'id':identifier,'island':sid,'bounds':bounds})
            # 同一块也登记成截面，花草与按实际网格裁决的散件才看得见它。
            self.reserve(identifier,sid,[corners(bounds)],[],'hard')
        self.records.append({'id':identifier,'island':sid,'model':name,'role':role,'sourcePosition':original,
                             'position':[px,self.islands[sid]['height'],pz],'yaw':heading,'bounds':bounds,
                             'movedMeters':math.dist((x,z),(px,pz)),'solidReservation':reserve})
        return px,pz,heading


def load_inputs():
    """road_layout.json + runtime_placements.json for the generator (missing runtime table = no runtime zones)."""
    roads=json.loads((ROOT/'ArtSource/SkyIsland/road_layout.json').read_text(encoding='utf-8'))
    runtime_path=ROOT/'ArtSource/SkyIsland/runtime_placements.json'
    runtime=json.loads(runtime_path.read_text(encoding='utf-8')) if runtime_path.is_file() else None
    return roads,runtime
