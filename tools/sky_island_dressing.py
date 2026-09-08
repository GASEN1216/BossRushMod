"""天空岛的离线美术布景（COMPAT）。

借助 Kenney CC0 小型自然网格与原创晶簇、悬藤、风灯丰富区域差异。
所有摆放共用真实 layout / 铺路曲线，保留桥口、玩法标记与建筑的净空。
只向作者生成器的区域/材质网格追加装饰，不创建运行时脚本或碰撞体。
"""
import math
import random
from collections import Counter

from sky_island_nature_assets import load_model, stamp

TAU = math.tau


def distance_to_segment(p, a, b):
    dx, dz = b[0]-a[0], b[1]-a[1]
    t = max(0, min(1, ((p[0]-a[0])*dx+(p[1]-a[1])*dz)/max(1e-9, dx*dx+dz*dz)))
    return math.hypot(p[0]-a[0]-t*dx, p[1]-a[1]-t*dz)


def inside(p, outline):
    result = False
    for a, b in zip(outline, outline[1:]+outline[:1]):
        if (a[1] > p[1]) != (b[1] > p[1]) and p[0] < (b[0]-a[0])*(p[1]-a[1])/(b[1]-a[1])+a[0]:
            result = not result
    return result


class PlantingSpace:
    def __init__(self, g, layout, island):
        self.island = island
        self.outline = island['outline']
        self.paths = [(a, b, width/2+.9) for points, width in g.PAVING_TRACKS
                      for a, b in zip(points, points[1:])
                      if abs(a[0]-island['center'][0]) < island['size'][0] and abs(a[1]-island['center'][2]) < island['size'][1]]
        self.obstacles = [o for o in layout['obstacles'] if o['island'] == island['id']]
        self.markers = [m['position'] for m in layout['markers'] if m.get('island') == island['id']]
        self.portals = [(b['path'][0 if b['from'] == island['id'] else -1], b['width']/2+5)
                        for b in layout['bridges'] if island['id'] in [b['from'], b['to']]]

    def free(self, x, z, radius=.5):
        p = (x, z)
        if not inside(p, self.outline):
            return False
        if any(distance_to_segment(p, a, b) < radius+2 for a, b in zip(self.outline, self.outline[1:]+self.outline[:1])):
            return False
        for o in self.obstacles:
            if abs(x-o['center'][0]) < o['size'][0]/2+radius+1 and abs(z-o['center'][2]) < o['size'][2]/2+radius+1:
                return False
        if any(math.hypot(x-m[0], z-m[2]) < radius+3.8 for m in self.markers):
            return False
        if any(math.hypot(x-p[0], z-p[2]) < r+radius for p, r in self.portals):
            return False
        if any(distance_to_segment(p, a, b) < w+radius for a, b, w in self.paths):
            return False
        # Circular plazas and landmarks are semantic clearings, beyond thin path strips.
        x0, _, z0 = self.island['center']
        clearing = {'B': 21, 'D': 17, 'E': 28, 'G': 19, 'H': 33, 'S1': 11, 'S4': 14}.get(self.island['id'], 9)
        return math.hypot(x-x0, z-z0) > clearing+radius


def crystal(g, x, y, z, height, radius, material, yaw=0, lean=.15):
    """Six broad facets and an irregular point; opaque so intersecting clusters sort reliably."""
    vertices = []
    for ring_y, ring_r in [(0, .74), (height*.68, 1)]:
        for i in range(6):
            angle = yaw+i*TAU/6
            vertices.append((x+radius*ring_r*math.cos(angle)+lean*ring_y,
                             y+ring_y, z+radius*ring_r*math.sin(angle)))
    vertices.append((x+lean*height, y+height, z+radius*.12))
    faces = [tuple(reversed(range(6)))]
    faces += [(i, (i+1)%6, (i+1)%6+6, i+6) for i in range(6)]
    faces += [(6+i, 6+(i+1)%6, 12) for i in range(6)]
    # A narrow pearl facet makes the form readable at night without point lights.
    face = faces.pop(8)
    g.addmesh(material, vertices, faces)
    g.addmesh('PearlGlow', [vertices[i] for i in face], [tuple(range(len(face)))])


def vine(g, position, length, phase, bloom=False):
    x, y, z = position
    points = [(x+math.sin(t*2.6+phase)*.65, y-length*t, z+.4*math.sin(t*4.5+phase))
              for t in [i/7 for i in range(8)]]
    g.ribbon(points, 'Forest', .065)
    for i in range(1, 10):
        t = i/10
        px, py, pz = x+math.sin(t*2.6+phase)*.65, y-length*t, z+.4*math.sin(t*4.5+phase)
        for sign in [-1, 1]:
            g.addmesh('Fern' if i%3 else 'LeafLight',
                      [(px, py, pz), (px+sign*.55, py+.22, pz+.08),
                       (px+sign*.93, py-.12, pz+.13), (px+sign*.4, py-.35, pz+.08)],
                      [(0, 1, 2, 3)])
        if bloom and i%3 == 0:
            g.sphere((px, py-.25, pz-.13), (.20, .37, .20), 'Lavender' if i%2 else 'PearlGlow', 7, 4)


def moon_mushroom(g,x,y,z,height):
    g.beam((x,y,z),(x+.35,y+height*.73,z),height*.09,'Ivory',8,r_end=height*.055)
    g.lathe((x+.35,y+height*.68,z),[(0,height*.46),(height*.11,height*.49),
              (height*.27,height*.38),(height*.34,height*.12),(height*.36,0)],'Lavender',16)
    g.torus((x+.35,y+height*.73,z),height*.46,.055,'PearlGlow',segments=20,sides=4)
    for i in range(5):
        a=i*TAU/5
        g.sphere((x+.35+height*.3*math.cos(a),y+height*.88,z+height*.3*math.sin(a)),
                 (height*.065,height*.055,height*.065),'LilyWhite',7,4)


def floating_gardens(g, layout, counts):
    rng = random.Random(420731)
    bridges = [(a, b, bridge['width']/2+11) for bridge in layout['bridges'] for a, b in zip(bridge['path'], bridge['path'][1:])]
    for island in layout['islands']:
        sid = island['id']; x, y, z = island['center']; outline = island['outline']
        g.CURRENT = sid
        for i in range(2, len(outline), 8 if len(sid) == 1 else 12):
            edge = outline[i]; dx, dz = edge[0]-x, edge[1]-z; distance = math.hypot(dx, dz)
            cx, cz = edge[0]+dx/distance*13, edge[1]+dz/distance*13
            if any(distance_to_segment((cx, cz), (a[0], a[2]), (b[0], b[2])) < margin for a, b, margin in bridges):
                continue
            if any(other['id'] != sid and inside((cx, cz), other['outline']) for other in layout['islands']):
                continue
            cy = y-7-rng.random()*5; radius = 3.5+rng.random()*2.8
            # Small hanging gardens have a pointed underside, not a full-size island extrusion.
            vertices = [(cx+radius*math.cos(k*TAU/7), cy, cz+radius*.77*math.sin(k*TAU/7)) for k in range(7)]
            vertices.append((cx+1.5, cy-9-rng.random()*6, cz))
            g.addmesh('RockDeep', vertices, [(k, (k+1)%7, 7) for k in range(7)])
            g.addmesh('Fern', vertices[:7], [tuple(range(7))])
            for j in range(4):
                a = j*2.4; h = (6.8 if j == 0 else 2.7+rng.random()*2)*radius/5
                crystal(g, cx+math.cos(a)*j*.55, cy, cz+math.sin(a)*j*.55, h, h*.22,
                        'CrystalTeal' if (i+j)%3 else 'CrystalLavender', a, .12 if j%2 else -.17)
            if sid in ['D','S2','S3']:
                moon_mushroom(g,cx-radius*.48,cy,cz+radius*.27,radius*1.6)
                counts['moonMushrooms']+=1
            for k in range(3):
                a = k*TAU/3
                vine(g, (cx+radius*.8*math.cos(a), cy, cz+radius*.65*math.sin(a)), 6+rng.random()*6, k, True)
            if i%3 == 0:
                g.torus((cx, cy+10, cz), 2.9, .075, 'BrassLight', axis='z', segments=24, sides=4, tilt=.35)
                g.sphere((cx, cy+10, cz), (.25, .25, .25), 'StarGlow', 8, 5)
            counts['floatingCrystalGardens'] += 1
        # Moss curtains are tucked below the cliff rim, never over the bridge entrances.
        for i in range(1, len(outline), 4):
            px, pz = outline[i]
            if any(distance_to_segment((px, pz), (a[0], a[2]), (b[0], b[2])) < margin for a, b, margin in bridges):
                continue
            for k in range(3):
                vine(g, (px+k*.7, y-2, pz), 5+rng.random()*11, k+i, sid in ['D','F','S2','S3'])
                counts['cliffVines'] += 1


def meadows(g, layout, counts):
    rng = random.Random(711204)
    palette_by_island = {'B':['flower_purpleA','flower_redB','flower_yellowC'],
                         'C':['flower_yellowA','flower_yellowB','flower_redC'],
                         'D':['mushroom_tan','mushroom_redGroup','flower_purpleB'],
                         'F':['flower_purpleC','flower_redA','flower_purpleB'],
                         'H':['flower_purpleA','flower_yellowB','flower_purpleC']}
    model_usage = Counter(); placements = []
    for island in layout['islands']:
        sid = island['id']; x, y, z = island['center']; space = PlantingSpace(g, layout, island)
        g.CURRENT = sid
        target = {'B':48, 'C':34, 'D':43, 'E':22, 'F':40, 'G':25, 'H':28}.get(sid, 22 if sid=='A' else 8)
        centers = []
        for attempt in range(target*18):
            if len(centers) >= target:
                break
            # Small asymmetric patches leave quiet stretches of lawn between garden pockets.
            cx = x+rng.uniform(-.45,.45)*island['size'][0]
            cz = z+rng.uniform(-.45,.45)*island['size'][1]
            if not space.free(cx, cz, 2.6) or any(math.hypot(cx-a, cz-b)<8 for a,b in centers):
                continue
            centers.append((cx,cz))
            model_set = palette_by_island.get(sid,['flower_purpleA','flower_yellowC','mushroom_tan'])
            for j in range(12):
                angle = j*2.39996; r = 3.6*math.sqrt((j+.5)/12)
                px, pz = cx+r*math.cos(angle), cz+r*.75*math.sin(angle)
                isflower = j%3 != 0
                name = rng.choice(model_set if isflower else ['grass_leafs','plant_flatShort'])
                h = rng.uniform(.65,1.3) if isflower else rng.uniform(.5,.95)
                footprint=max(math.hypot(v[0],v[2]) for part in load_model(name) for v in part.verts)*h
                if not space.free(px,pz,footprint):
                    continue
                # A few low pearl mushrooms distinguish the forest from the village gardens.
                override = None
                if sid in ['D','S3'] and 'mushroom' in name:
                    override = {'colorRed':'CrystalLavender','colorTan':'CrystalTeal'}
                stamp(g,name,(px,y+.025,pz),h,rng.random()*TAU,palette=override)
                model_usage[name] += 1
                placements.append({'island':sid,'model':name,'position':[round(px,3),y+.025,round(pz,3)],'height':round(h,3),'radius':round(footprint,3)})
            counts['meadowPatches'] += 1
        # Scattered petals are tiny, flush triangles; they visually soften borders without colliders.
        if sid in ['B','D','F','H']:
            for cx,cz in centers[::2]:
                for i in range(9):
                    px=cx+rng.uniform(-4.5,4.5); pz=cz+rng.uniform(-4.5,4.5)
                    if not space.free(px,pz,.12): continue
                    g.addmesh('Blossom' if sid in ['B','F'] else 'Lavender',
                              [(px,y+.022,pz),(px+.3,y+.03,pz+.14),(px+.14,y+.025,pz+.37)],[(0,2,1)])
        # Sparse suspended seeds sit above planting patches; shader emission requires no Light objects.
        for cx,cz in centers[::3]:
            for i in range(4):
                px=cx+math.sin(i*3.1)*2; pz=cz+math.cos(i*2.4)*2
                g.sphere((px,y+1.8+i*.7,pz),(.095,.14,.095),'StarGlow' if sid in ['D','F','S3'] else 'Glow',6,3)
                counts['floatingPollen'] += 1
    return dict(model_usage), placements


def lotus(g,x,y,z,scale=1):
    # Sculpted petals sit on a real CC0 lily leaf; no new texture or transparent plane needed.
    g.cylinder((x,y+.02,z),1.25*scale,.04,'Fern',12)
    stamp(g,'lily_large',(x,y,z),.42*scale,0)
    for ring in range(2):
        for i in range(7):
            a=i*TAU/7+ring*.4; r=(.7-ring*.3)*scale
            g.sphere((x+r*math.cos(a),y+(.32+ring*.28)*scale,z+r*math.sin(a)),
                     (.46*scale,.18*scale,.45*scale),'Blossom' if ring==0 else 'LilyWhite',8,4)
    g.sphere((x,y+.59*scale,z),(.26*scale,.2*scale,.26*scale),'Glow',8,4)


def water_gardens(g,layout,counts):
    islands={island['id']:island for island in layout['islands']}
    for sid, positions in [('F',[(220,-49),(247,-35),(249,-48),(226,-33),(241,-54)]),
                           ('S1',[(-353,-232),(-348,-234),(-346,-228)])]:
        g.CURRENT=sid; y=islands[sid]['height']+.22
        for i,(x,z) in enumerate(positions):
            lotus(g,x,y,z,1.2 if sid=='F' else .8)
            counts['lotusFlowers']+=1
    for sid in ['F','S1']:
        g.CURRENT=sid; x,y,z=islands[sid]['center']
        for i in range(9):
            a=i*TAU/9; r=23 if sid=='F' else 9
            stamp(g,'plant_flatTall',(x+math.cos(a)*r,y+.02,z+math.sin(a)*r),1.1,a)
            counts['watersidePlants']+=1


def hanging_lantern(g,x,y,z,scale=1):
    g.lathe((x,y,z),[(0,.17*scale),(.2*scale,.6*scale),(1.2*scale,.65*scale),(1.6*scale,.32*scale)],'Glow',10)
    for yy,rr in [(.18,.58),(1.45,.43)]:
        g.torus((x,y+yy*scale,z),rr*scale,.055*scale,'Brass',segments=12,sides=4)
    g.ribbon([(x,y,z),(x+.2*scale,y-1.1*scale,z),(x-.15*scale,y-2*scale,z+.2*scale)],'Coral',.045*scale)


def sky_festival(g,layout,counts):
    islands={island['id']:island for island in layout['islands']}
    for sid in ['B','D','F','H']:
        g.CURRENT=sid; x,y,z=islands[sid]['center']
        for i in range(7):
            a=i*2.39996+.7; r=38+i*2.2
            px,pz=x+math.cos(a)*r,z+math.sin(a)*r
            # Floating lanterns stay high enough to preserve the player camera and traversal.
            hanging_lantern(g,px,y+17+math.sin(i*4)*4,pz,.65+(i%3)*.15)
            counts['wishLanterns']+=1
    # Gentle spiral constellations above the forest root arch, well clear of the player's plane.
    g.CURRENT='D'; y=islands['D']['height']
    for i in range(9):
        t=(i+1)/10
        vine(g,(-281+t*59,y+17+math.sin(t*math.pi)*15,140+t*8),5+math.sin(i*2)*1.5,i,True)
        counts['archFlowerVines']+=1
    for i in range(16):
        a=i*TAU/16; x=-251+math.cos(a)*23; z=141+math.sin(a)*8
        g.sphere((x,y+37+math.sin(a)*6,z),(.20,.28,.20),'PearlGlow',8,4)
    counts['constellationSeeds']=16
    # A few small bird silhouettes help read the scale of the open sky.
    g.CURRENT='Distant_Birds'
    for i in range(15):
        x=-35+i*5; y=105+math.sin(i*.8)*6; z=380+i%4*5
        g.addmesh('Ivory',[(x,y,z),(x-2,y+1,z+.5),(x-3,y+.7,z+1.5),
                          (x+2,y+1,z+.5),(x+3,y+.7,z+1.5),(x,y-.3,z+1)],
                  [(0,1,2,5),(0,5,4,3)])
    counts['distantBirds']=15


def build(g,layout):
    counts=Counter()
    model_usage, placements=meadows(g,layout,counts)
    floating_gardens(g,layout,counts)
    water_gardens(g,layout,counts)
    sky_festival(g,layout,counts)
    return {'version':1,'classification':'COMPAT','seed':711204,
            'description':'CC0 nature meshes plus original floating crystal gardens, cliff vines, lotus and wishing lanterns',
            'source':'https://kenney.nl/assets/nature-kit','license':'CC0-1.0',
            'counts':dict(counts),'natureModels':model_usage,'natureInstances':sum(model_usage.values()),
            'placementPolicy':{'pathMargin':.9,'markerMargin':3.8,'bridgePortalMargin':5,'maxMeadowHeight':1.3,
                               'collision':'decorative only; geometry merged per island/material; no Update scripts or point lights'},
            'meadowPlacements':placements}
