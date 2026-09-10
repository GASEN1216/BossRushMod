"""Blender background authoring: original Qinglan archipelago meshes, FBX and previews.

All geometry is built from the checked shared Unity-coordinate layout. This script
only writes its own SkyIsland output directories; it never removes disk content.
"""
import argparse
import json
import math
import random
import sys
from collections import defaultdict
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

TAU = math.tau
RNG = random.Random(20260908)
GROUPS = {}
MATERIALS = {}
CURRENT = 'A'
COLLISIONS = []
ANIMATED = []
PAVING_TRACKS = []
EMISSION = {'Glow': 1.2, 'StarGlow': 1.2, 'CrystalLavender': .18,
            'CrystalTeal': .22, 'PearlGlow': .55}
# 调色板对齐原版鸭科夫：从游戏 `resources.assets` 采样 154 张 `_C` 反照率贴图，色相集中在
# H18-40 的暖琥珀（石/木/墙/屋顶），草地在 H78-89，冷色只出现在工业和实验室材质上。天空岛
# 原来的青蓝岩体(H189)、纯绿草地(H100)与青瓦(H180)是与原版差距最大的三处，这里按实测值归位。
# 奇幻感不再由高饱和底色承担，改由光照、发光件和夜间材质表达。键名是贯穿 FBX 材质槽、
# `Sky_*.mat` 与 geometry.json 的稳定标识，只改颜色不改名；因此 `Teal*` 现在装的是赤陶屋顶色。
PALETTE = {
    'Limestone': '#d9c9a4', 'Ivory': '#ece0bd', 'Chalk': '#c3b394',
    'Rock': '#a98a6f', 'RockLight': '#c0a381', 'RockDeep': '#6d5844',
    'Grass': '#7f9154', 'GrassLight': '#9aa85e', 'Forest': '#4d5c33',
    'Leaf': '#71853f', 'LeafLight': '#a3ad63', 'LeafGold': '#c6a95e',
    'Wood': '#8a6845', 'WoodLight': '#bf9364', 'WoodDark': '#4a3a2b',
    'Teal': '#a4653f', 'TealLight': '#c17f4e', 'TealDeep': '#6d4128',
    'Brass': '#bd914d', 'BrassLight': '#dbb267', 'Copper': '#b47e52',
    'Coral': '#be7460', 'Blue': '#7d94a0', 'Water': '#6fa392',
    'Glow': '#ffe3a0', 'StarGlow': '#a8edf0', 'Cloud': '#e8e4da',
    'CloudShade': '#cdd2d2', 'CloudFar': '#c2d0d8', 'CloudBackdrop': '#a8bcc4', 'Soil': '#7a5c40', 'Flower': '#dfcaa1',
    'Mural': '#ffffff', 'Cloth': '#ffffff',
    'Blossom': '#d9a3a6', 'Lavender': '#b3a6c4', 'Fern': '#6a8a55',
    'LilyWhite': '#f4ece2', 'CrystalLavender': '#a89ad6',
    'CrystalTeal': '#72b7bf', 'PearlGlow': '#f2cfe4',
    'PaintTeal': '#487f78', 'PaintTealLight': '#81aba1', 'PaintTealDeep': '#365d59',
}
# 图集贴图通道：有贴图但**不平铺**，且必须保留调用方传入的 UV。
# 不能塞进 TILED_TEXTURES —— create_object 对那里的材质会丢弃传入 UV 改用世界坐标平面投影，
# 会把 Tripo3D 的图集 UV 彻底打乱。Mural/Cloth 用的就是这条通道，这里沿用同一模式。
# 由 sky_island_tripo_props.register() 在 build_materials 之前填充。
MODEL_TEXTURES = {}
# 由 main() 在注册材质时填入。散布函数是模块级的，看不到 main() 里 import 的局部名，
# 因此把模块与目录都挂到模块全局上。
TRIPO_DIR = None
TRIPO_PROPS = None

TILED_TEXTURES = {
    'Limestone': ('stone_handpainted_vanilla.png', 8, [1.02,1.01,.90,1]),   # #d9c9a4 砖墙暖白
    'Ivory': ('stone_handpainted_vanilla.png', 6, [1.11,1.13,1.04,1]),   # #ece0bd 亮面石材
    'Chalk': ('stone_handpainted_vanilla.png', 8, [.92,.90,.81,1]),   # #c3b394 灰泥
    'Rock': ('stone_handpainted_vanilla.png', 14, [.80,.70,.61,1]),   # #a98a6f 原版 T_Tile_Stones_C
    'RockLight': ('stone_handpainted_vanilla.png', 14, [.91,.82,.71,1]),   # #c0a381 受光岩面
    'RockDeep': ('stone_handpainted_vanilla.png', 16, [.51,.44,.37,1]),   # #6d5844 岛底背光岩
    'Grass': ('grass_handpainted_vanilla.png', 17, [.84,.82,.80,1]),   # #7f9154 原版 T_Tile_Grass_01_C
    'GrassLight': ('grass_handpainted_vanilla.png', 17, [1.02,.95,.89,1]),   # #9aa85e 受光草坡
    'Wood': ('wood_handpainted_vanilla.png', 5, [.67,.70,.78,1]),   # #8a6845 旧木
    'WoodLight': ('wood_handpainted_vanilla.png', 5, [.92,.99,1.13,1]),   # #bf9364 原版 WallWoodBlank01
    'Teal': ('roof_handpainted_terracotta.png', 4.4, [1.01,.87,.76,1]),   # #a4653f 赤陶瓦
    'TealLight': ('roof_handpainted_terracotta.png', 4.4, [1.19,1.09,.94,1]),   # #c17f4e 受光瓦脊
    'TealDeep': ('roof_handpainted_terracotta.png', 4.4, [.67,.56,.48,1]),   # #6d4128 檐下暗瓦
}


def rgba(code):
    return [int(code[i:i + 2], 16) / 255 for i in (1, 3, 5)] + [1.0]


def linear_color(color):
    return [v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in color[:3]]+[color[3]]


def addmesh(mat, verts, faces, uv=None, smooth=False, group=None):
    key = (group or CURRENT, mat)
    data = GROUPS.setdefault(key, {'v': [], 'f': [], 'uv': [], 'smooth': []})
    offset = len(data['v'])
    data['v'].extend(verts)
    data['f'].extend(tuple(offset + i for i in face) for face in faces)
    data['uv'].extend(uv or [(0.0, 0.0)] * len(verts))
    data['smooth'].extend([smooth] * len(faces))


def box(center, size, mat, bevel=0.12, yaw=0, group=None):
    x, y, z = center
    w, h, d = size
    r = min(bevel, w * .18, h * .18, d * .18)
    # Eight-corner footprint and beveled top/bottom retain broad readable planes.
    footprint = [(-w/2+r,-d/2),(w/2-r,-d/2),(w/2,-d/2+r),(w/2,d/2-r),
                 (w/2-r,d/2),(-w/2+r,d/2),(-w/2,d/2-r),(-w/2,-d/2+r)]
    verts=[]
    for yy, factor in [(-h/2,.98),(-h/2+r,1),(h/2-r,1),(h/2,.98)]:
        for xx,zz in footprint:
            xx*=factor; zz*=factor
            verts.append((x+xx*math.cos(yaw)+zz*math.sin(yaw),y+yy,z-xx*math.sin(yaw)+zz*math.cos(yaw)))
    faces=[tuple(range(8)),tuple(reversed(range(24,32)))]
    for ring in range(3):
        for i in range(8):
            j=(i+1)%8
            faces.append((ring*8+i,(ring+1)*8+i,(ring+1)*8+j,ring*8+j))
    addmesh(mat,verts,faces,group=group)


def cylinder(center, radius, height, mat, sides=12, radius_top=None):
    x,y,z=center
    rt=radius if radius_top is None else radius_top
    verts=[(x+r*math.cos(i*TAU/sides),y+dy,z+r*math.sin(i*TAU/sides))
           for dy,r in [(-height/2,radius),(height/2,rt)] for i in range(sides)]
    faces=[tuple(range(sides)),tuple(reversed(range(sides,2*sides)))]
    faces += [(i,sides+i,sides+(i+1)%sides,(i+1)%sides) for i in range(sides)]
    addmesh(mat,verts,faces)


def beam(a,b,r,mat,sides=8,r_end=None):
    va,vb=Vector(a),Vector(b)
    direction=(vb-va).normalized()
    u=direction.cross(Vector((0,1,0)))
    if u.length < .01: u=direction.cross(Vector((1,0,0)))
    u.normalize(); v=direction.cross(u).normalized()
    r2=r if r_end is None else r_end
    verts=[tuple(p+rr*(math.cos(i*TAU/sides)*u+math.sin(i*TAU/sides)*v))
           for p,rr in [(va,r),(vb,r2)] for i in range(sides)]
    faces=[tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
    faces += [(i,(i+1)%sides,sides+(i+1)%sides,sides+i) for i in range(sides)]
    addmesh(mat,verts,faces)


def sphere(center,scale,mat,segments=12,rings=7,smooth=True):
    x,y,z=center; sx,sy,sz=scale
    verts=[]
    for j in range(rings+1):
        ph=math.pi*j/rings
        for i in range(segments):
            th=TAU*i/segments
            verts.append((x+sx*math.sin(ph)*math.cos(th),y+sy*math.cos(ph),z+sz*math.sin(ph)*math.sin(th)))
    faces=[(j*segments+i,j*segments+(i+1)%segments,(j+1)*segments+(i+1)%segments,(j+1)*segments+i)
           for j in range(rings) for i in range(segments)]
    addmesh(mat,verts,faces,smooth=smooth)


def torus(center,major,minor,mat,axis='y',segments=40,sides=7,tilt=0):
    x,y,z=center; verts=[]
    for i in range(segments):
        a=TAU*i/segments
        for j in range(sides):
            b=TAU*j/sides
            xx=(major+minor*math.cos(b))*math.cos(a)
            yy=minor*math.sin(b)
            zz=(major+minor*math.cos(b))*math.sin(a)
            if axis=='z': yy,zz=zz,yy
            if axis=='x': xx,yy=yy,xx
            yy,xx=yy*math.cos(tilt)-xx*math.sin(tilt),yy*math.sin(tilt)+xx*math.cos(tilt)
            verts.append((x+xx,y+yy,z+zz))
    faces=[(i*sides+j,((i+1)%segments)*sides+j,((i+1)%segments)*sides+(j+1)%sides,i*sides+(j+1)%sides)
           for i in range(segments) for j in range(sides)]
    addmesh(mat,verts,faces,smooth=True)


def lathe(center,profile,mat,sides=24):
    x,y,z=center
    verts=[(x+r*math.cos(i*TAU/sides),y+yy,z+r*math.sin(i*TAU/sides))
           for yy,r in profile for i in range(sides)]
    faces=[(j*sides+i,(j+1)*sides+i,(j+1)*sides+(i+1)%sides,j*sides+(i+1)%sides)
           for j in range(len(profile)-1) for i in range(sides)]
    addmesh(mat,verts,faces,smooth=True)


def ribbon(points,mat,r=.14):
    for a,b in zip(points,points[1:]): beam(a,b,r,mat,sides=6)


def textured_quad(corners,mat):
    addmesh(mat,corners,[(0,1,2,3)],[(0,0),(1,0),(1,1),(0,1)])


def marker(name,pos):
    obj=bpy.data.objects.new('M_'+name,None)
    bpy.context.scene.collection.objects.link(obj)
    obj.location=(pos[0],pos[2],pos[1])
    obj.empty_display_type='PLAIN_AXES'; obj.empty_display_size=1.2


def create_object(name,verts,faces,mat=None,uv=None,smooth=None,hidden=False):
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata([(p[0],p[2],p[1]) for p in verts],[],[tuple(reversed(f)) for f in faces])
    mesh.update()
    if mat: mesh.materials.append(MATERIALS[mat])
    if mat in TILED_TEXTURES:
        layer=mesh.uv_layers.new(name='UVMap')
        repeat=TILED_TEXTURES[mat][1]
        for poly in mesh.polygons:
            points=[Vector(verts[mesh.loops[li].vertex_index]) for li in poly.loop_indices]
            normal=(points[1]-points[0]).cross(points[2]-points[0])
            axis=max(range(3),key=lambda axis:abs(normal[axis]))
            for li in poly.loop_indices:
                p=verts[mesh.loops[li].vertex_index]
                layer.data[li].uv=((p[0],p[2]) if axis==1 else (p[2],p[1]) if axis==0 else (p[0],p[1]))
                layer.data[li].uv/=repeat
    elif uv:
        layer=mesh.uv_layers.new(name='UVMap')
        for poly in mesh.polygons:
            for li in poly.loop_indices: layer.data[li].uv=uv[mesh.loops[li].vertex_index]
    # Recompute closed-shell normals; open terrain keeps the supplied upward faces.
    if not name.startswith(('NAV_','COL_Ground','VIS_Ground')):
        bm=bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(mesh); bm.free()
    if smooth:
        for p,s in zip(mesh.polygons,smooth): p.use_smooth=s
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.hide_render=hidden
    return obj


def collision_box(name,center,size):
    x,y,z=center; w,h,d=size
    v=[(x+sx*w/2,y+sy*h/2,z+sz*d/2) for sy in (-1,1) for sz in (-1,1) for sx in (-1,1)]
    f=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
    create_object('COL_Wall_'+name,v,f,hidden=True)
    COLLISIONS.append({'name':name,'center':list(center),'size':list(size)})


def tree(x,y,z,scale=1,gold=False):
    h=7.5*scale
    # Distinct regional foliage, with asymmetrical crowns instead of identical green balls.
    blossom = not gold and CURRENT.split('_')[0] in ['B','F','S2'] and math.sin(x*.137+z*.073) > -.2
    moonleaf = not gold and CURRENT.split('_')[0] in ['D','S3'] and math.cos(x*.07-z*.09) > .2
    leaf = 'LeafGold' if gold else 'Blossom' if blossom else 'Fern' if moonleaf else 'Leaf'
    highlight = 'LilyWhite' if blossom else 'LeafLight'
    beam((x,y,z),(x+.5*scale,y+h,z),.7*scale,'Wood',r_end=.4*scale)
    for a in [0,2.1,4.1]:
        tip=(x+2.8*scale*math.cos(a),y+h*.82,z+2.8*scale*math.sin(a))
        beam((x,y+h*.4,z),tip,.3*scale,'Wood',r_end=.13*scale)
        sphere((tip[0],tip[1]+1.8*scale,tip[2]),(3.9*scale,2.65*scale,3.3*scale),
               leaf if a else highlight,segments=10,rings=5,smooth=False)
    sphere((x-.8*scale,y+h+2*scale,z+.4*scale),(3.6*scale,3.1*scale,3.4*scale),leaf,10,5,False)
    if blossom or moonleaf:
        for i in range(5):
            a=i*TAU/5+.4; px=x+3.4*scale*math.cos(a); pz=z+3.4*scale*math.sin(a)
            for j in range(3):
                sphere((px+.3*math.sin(j),y+h-(.3+j*.65)*scale,pz),
                       (.65*scale,.65*scale,.55*scale),leaf if j<2 else highlight,7,4,False)
    # Large roots, kept within the tree's island-edge planting zone.
    for a in [0,2,4]:
        beam((x,y+.5,z),(x+2*scale*math.cos(a),y+.12,z+2*scale*math.sin(a)),.35*scale,'Wood',r_end=.08)


def lantern(x,y,z,scale=1):
    cylinder((x,y+1.7*scale,z),.1*scale,3.4*scale,'WoodDark',8)
    beam((x,y+3.3*scale,z),(x+.7*scale,y+3.3*scale,z),.09*scale,'Brass')
    lathe((x+.65*scale,y+2.4*scale,z),[(0,.22*scale),(.1*scale,.32*scale),(.62*scale,.32*scale),(.76*scale,.12*scale)],'Glow',10)
    for yy in [2.4,3.05]: torus((x+.65*scale,y+yy*scale,z),.33*scale,.065*scale,'Brass',segments=12,sides=4)


def pot(x,y,z,scale=1,plant=True):
    lathe((x,y,z),[(0,.3*scale),(.12*scale,.48*scale),(.9*scale,.6*scale),(1.05*scale,.65*scale),(1.12*scale,.52*scale)],'Coral',10)
    if plant:
        for a in [0,1.6,3.2,4.8]:
            sphere((x+.32*scale*math.cos(a),y+1.28*scale,z+.32*scale*math.sin(a)),(.5*scale,.28*scale,.36*scale),'Leaf',8,4,False)


def house(obs,index):
    x,cy,z=obs['center']; w,h,d=obs['size']; base=cy-h/2
    wallh=min(8.5,max(5.3,h))
    box((x,base+wallh/2,z),(w,wallh,d),'Ivory' if index%3 else 'Limestone',.35)
    box((x,base+.4,z),(w+.5,.8,d+.5),'Chalk',.15)
    # Alternating roof silhouettes make the cottages readable from the game camera.
    roofmat=['Teal','TealDeep','TealLight'][index%3]
    verts=[]
    levels=[(wallh+.12,1.16),(wallh+.7,1.03),(wallh+2.4,.76),(wallh+3.6,.32),(wallh+3.9,.07)]
    for yy,s in levels:
        verts += [(x+dx*w*s/2,base+yy,z+dz*d*s/2) for dx,dz in [(-1,-1),(1,-1),(1,1),(-1,1)]]
    faces=[(j*4+i,j*4+(i+1)%4,(j+1)*4+(i+1)%4,(j+1)*4+i) for j in range(4) for i in range(4)]
    if index % 3 == 1:
        eave=base+wallh+.25; ridge=eave+3.9
        for sign in [-1,1]:
            panel=[(x+sign*w*.59,eave,z-d*.6),(x,ridge,z-d*.6),
                   (x,ridge,z+d*.6),(x+sign*w*.59,eave,z+d*.6)]
            addmesh(roofmat,panel,[(0,1,2,3)],group=CURRENT+'_Roof')
            beam(panel[0],panel[3],.18,'WoodDark')
        beam((x,ridge,z-d*.63),(x,ridge,z+d*.63),.22,'WoodLight')
        for sign in [-1,1]:
            zz=z+sign*d*.5
            addmesh('Ivory',[(x-w*.5,eave,zz),(x+w*.5,eave,zz),(x,ridge-.18,zz)],[(0,1,2)])
            for side in [-1,1]:
                beam((x+side*w*.57,eave,z+sign*d*.605),(x,ridge,z+sign*d*.605),.16,'WoodLight')
            box((x,eave+1.25,zz+sign*.06),(1.1,1.4,.14),'TealDeep',.12)
            box((x,eave+1.25,zz+sign*.15),(.8,1.05,.08),'Glow',.1)
    else:
        addmesh(roofmat,verts,faces,group=CURRENT+'_Roof')
        for i in range(4):
            ribbon([verts[j*4+i] for j in range(5)],'Brass',.11)
            beam(verts[i],verts[(i+1)%4],.16,'WoodDark')
        for side in [-1,1]:
            for t in [-.28,0,.28]:
                beam((x+w*t,base+wallh+.22,z+side*d*.575),(x+w*t*.3,base+wallh+3.52,z+side*d*.17),.055,'TealLight')
    for side in [-1,1]:
        for frontside in [-1,1]:
            box((x+side*(w/2-.13),base+wallh*.5,z+frontside*(d/2+.03)),
                (.32,wallh,.26),'WoodLight',.04)
        box((x,base+1.05,z+side*(d/2+.045)),(w,.28,.16),'Chalk',.04)
    # Doors and windows are modelled trim inset against solid collision footprints.
    front=z-d/2-.035
    box((x,base+1.8,front),(2.6,3.6,.16),'WoodDark',.16)
    for dx in [-1.52,1.52]: box((x+dx,base+1.95,front-.14),(.3,4.1,.35),'WoodLight',.06)
    box((x,base+4.04,front-.14),(3.35,.32,.4),'WoodLight')
    sphere((x+.85,base+1.7,front-.16),(.13,.13,.12),'Brass',8,4)
    for dx in [-w*.32,w*.32]:
        if abs(dx)<2.3: continue
        box((x+dx,base+3.4,front-.05),(1.85,2.2,.25),'TealDeep',.15)
        box((x+dx,base+3.4,front-.2),(1.5,1.8,.12),'Glow',.1)
        box((x+dx,base+3.4,front-.29),(.12,2,.13),'Wood')
        box((x+dx,base+3.4,front-.29),(1.7,.12,.13),'Wood')
        box((x+dx,base+2.13,front-.36),(2.2,.38,.65),'WoodLight')
        for sign in [-1,1]:
            box((x+dx+sign*1.15,base+3.38,front-.12),(.46,2.12,.14),
                'Coral' if index%3==1 else 'Teal',.06)
            for yy in [2.8,3.3,3.8]:
                box((x+dx+sign*1.15,base+yy,front-.21),(.4,.075,.05),'WoodLight',.015)
        for t in [-.65,0,.65]: sphere((x+dx+t,base+2.53,front-.38),(.43,.45,.42),'LeafLight',8,4,False)
    chimney=(x+w*.26,base+wallh+2.9,z+d*.2)
    box(chimney,(1.3,3.1,1.3),'Limestone')
    box((chimney[0],chimney[1]+1.5,chimney[2]),(1.7,.35,1.7),'Copper')
    if index%2==0:
        for sign in [-1,1]: beam((x+sign*2.9,base,z-d/2-2.8),(x+sign*2.9,base+3.8,z-d/2-2.8),.12,'Wood')
        textured_quad([(x-3.1,base+3.8,z-d/2-3),(x+3.1,base+3.8,z-d/2-3),
                       (x+3.1,base+4.4,z-d/2),(x-3.1,base+4.4,z-d/2)],'Cloth')
    pot(x-w*.4,base,z-d/2-.85,1.05)


def island_shell(island):
    x,y,z=island['center']; outline=island['outline']; n=len(outline)
    verts=[]
    depth=38 if len(island['id'])==1 else 24
    for level,(drop,factor) in enumerate([(0,1),(-3,1.02),(-13,.95),(-depth,.57),(-depth-15,.13)]):
        for i,(px,pz) in enumerate(outline):
            jitter=0 if level==0 else (math.sin(i*13.7+level)*1.8)
            verts.append((x+(px-x)*factor,y+drop+jitter,z+(pz-z)*factor))
    for level in range(4):
        for i in range(n):
            j=(i+1)%n
            mat= ['Limestone','RockLight','Rock','RockDeep'][level]
            if i%4==0 and level==2: mat='RockDeep'
            addmesh(mat,[verts[level*n+i],verts[level*n+j],verts[(level+1)*n+j],verts[(level+1)*n+i]],[(0,1,2,3)])
    # Thin warm limestone rim, broad geological strata and dangling roots.
    for i,(px,pz) in enumerate(outline):
        qx,qz=outline[(i+1)%n]
        beam((px,y-.55,pz),(qx,y-.55,qz),.65,'Limestone',6)
        if i%2==0:
            root=[(px,y-1,pz),(x+(px-x)*.94,y-12,z+(pz-z)*.94),
                  (x+(px-x)*.74+3,y-25,z+(pz-z)*.74),(x+(px-x)*.64,y-32,z+(pz-z)*.64)]
            ribbon(root,'Forest',.6 if len(island['id'])==1 else .3)
    # Steep hanging chalk columns visually break the simple ring silhouette.
    for i in range(0,n,3):
        px,pz=outline[i]
        cylinder((x+(px-x)*.89,y-13,z+(pz-z)*.89),3.6,21,'RockLight',5,radius_top=5)


def paved_disc(x,y,z,r,mat='Limestone'):
    cylinder((x,y-.10,z),r,.24,mat,40)
    torus((x,y+.04,z),r-.35,.12,'Chalk',segments=48,sides=5)
    # Radial decorative seams, physically flush.
    for i in range(12):
        a=i*TAU/12
        beam((x+2*math.cos(a),y+.03,z+2*math.sin(a)),(x+(r-.5)*math.cos(a),y+.03,z+(r-.5)*math.sin(a)),.055,'Chalk',4)


def bell(x,y,z,scale=1):
    lathe((x,y,z),[(0,2.4*scale),(.3*scale,2.75*scale),(.75*scale,2.45*scale),
           (1.2*scale,1.98*scale),(3.9*scale,1.35*scale),(4.8*scale,.7*scale),(5.1*scale,0)],'Brass',32)
    for yy,rr in [(.4,2.65),(.9,2.25),(3.7,1.45)]: torus((x,y+yy*scale,z),rr*scale,.10*scale,'BrassLight',segments=32)
    beam((x,y-.1*scale,z),(x+.25*scale,y-1.1*scale,z),.10*scale,'WoodDark')
    sphere((x+.25*scale,y-1.1*scale,z),(.28*scale,.3*scale,.28*scale),'BrassLight')
    for a in range(8):
        an=a*TAU/8
        sphere((x+1.73*scale*math.cos(an),y+2.4*scale,z+1.73*scale*math.sin(an)),(.17*scale,.45*scale,.17*scale),'BrassLight',8,4)


def area_landmarks(islands):
    global CURRENT
    # Village: paved circle, wind chime canopy and framed story mural.
    CURRENT='B'; x,y,z=islands['B']['center']
    paved_disc(x,y+.05,z,17)
    for dx in [-6,6]:
        cylinder((x+dx,y+4.6,z+5),.52,9.2,'Wood',12)
        cylinder((x+dx,y+.3,z+5),1.15,.6,'Limestone',12)
        sphere((x+dx,y+9.6,z+5),(.65,.85,.65),'Brass',12,6)
    beam((x-7,y+8.7,z+5),(x+7,y+8.7,z+5),.28,'WoodDark')
    for i in range(7):
        bx=x-5.4+i*1.8; drop=.7+.4*math.sin(i)
        beam((bx,y+8.6,z+5),(bx,y+7.2-drop,z+5),.055,'Brass')
        bell(bx,y+6.4-drop,z+5,.28)
    # Freestanding mural next to the north edge, accessible on its front side.
    textured_quad([(x-26.8,y+.35,z+32.92),(x-19.2,y+.35,z+32.92),(x-19.2,y+5.95,z+32.92),(x-26.8,y+5.95,z+32.92)],'Mural')
    for xx in [-16,17]:
        cylinder((x+xx,y+.8,z-12),1.6,.16,'WoodLight',20)
        cylinder((x+xx,y+.38,z-12),.16,.8,'WoodDark',8)
        for a in [0,2.1,4.2]: cylinder((x+xx+2.2*math.cos(a),y+.5,z-12+2.2*math.sin(a)),.6,.9,'Wood',10)
    # Festival bunting strung high above the two village approach paths.
    for zoff in [-24,23]:
        pts=[]
        for i in range(15):
            t=i/14; pts.append((x-22+44*t,y+6.2-2*math.sin(math.pi*t),z+zoff))
        ribbon(pts,'Wood',.04)
        for i,p in enumerate(pts[1:-1]):
            addmesh(['Coral','Teal','BrassLight'][i%3],[(p[0]-.5,p[1],p[2]),(p[0]+.5,p[1],p[2]),(p[0],p[1]-1.1,p[2])],[(0,1,2)])
    # Farm fields are arranged around a clear principal walkway.
    CURRENT='C'; x,y,z=islands['C']['center']
    for side in [-1,1]:
        for row in range(3):
            cx=x+side*32; cz=z-32+row*24
            box((cx,y+.045,cz),(24,.08,16),'Soil',.01)
            for dx in [-12,12]: box((cx+dx,y+.08,cz),(.35,.12,17.3),'Limestone',.01)
            for zz in [-8,8]: box((cx,y+.08,cz+zz),(24,.12,.35),'Limestone',.01)
            for col in range(6):
                for rr in range(3):
                    px=cx-9.6+col*3.7; pz=cz-5+rr*4.5
                    if row == 0:
                        sphere((px,y+.45,pz),(.69,.46,.64),'LeafLight',8,4,False)
                        for k in range(4):
                            a=k*TAU/4
                            sphere((px+math.cos(a)*.43,y+.29,pz+math.sin(a)*.43),
                                   (.47,.17,.4),'Leaf',7,4,False)
                    elif row == 1:
                        sphere((px,y+.33,pz),(.36,.34,.32),'Coral',8,4,False)
                        for k in range(4):
                            a=k*2.4
                            addmesh('Forest' if k%2 else 'LeafLight',
                                    [(px,y+.4,pz),(px+math.cos(a)*.6,y+.73,pz+math.sin(a)*.6),
                                     (px+math.cos(a)*.8,y+.5,pz+math.sin(a)*.8),
                                     (px+.14,y+.42,pz+.1)],[(0,1,2,3)])
                    else:
                        for k in range(5):
                            dx=.25*math.cos(k*2.4); dz=.25*math.sin(k*2.4)
                            top=y+1.3+(k%3)*.12
                            beam((px+dx,y,pz+dz),(px+dx+.15,top,pz+dz),.026,'LeafGold',5)
                            sphere((px+dx+.15,top,pz+dz),(.12,.27,.1),'Flower',6,3,False)
    wx,wz=x-50,z+13.8
    torus((wx,y+5,wz),4.8,.5,'Wood',axis='z',segments=24)
    torus((wx,y+5,wz-1.7),4.8,.4,'Wood',axis='z',segments=24)
    for i in range(12):
        a=i*TAU/12
        beam((wx,y+5,wz),(wx+4.7*math.cos(a),y+5+4.7*math.sin(a),wz),.16,'WoodLight')
        beam((wx+4.8*math.cos(a),y+5+4.8*math.sin(a),wz-2),(wx+4.8*math.cos(a),y+5+4.8*math.sin(a),wz+.3),.55,'Wood',4)
    # Suspended root forest and brass directional vane.
    CURRENT='D'; x,y,z=islands['D']['center']
    for angle in [-.5,.6,1.8,2.8,3.8]:
        tx=x+53*math.cos(angle); tz=z+57*math.sin(angle)
        tree(tx,y,tz,2.2)
    points=[(-285,y,133),(-281,y+16,136),(-270,y+27,140),(-253,y+32,143),
            (-236,y+29,146),(-222,y+19,148),(-214,y,151)]
    for i,(a,b) in enumerate(zip(points,points[1:])):
        beam(a,b,2.0+.7*abs(i-2.5)/2.5,'Wood',10)
    paved_disc(x,y+.04,z,13)
    x,z=x-27,z-26
    cylinder((x,y+3,z),1.4,6,'Limestone',16)
    torus((x,y+7,z),3.4,.19,'Brass',axis='z')
    beam((x-5,y+7,z),(x+5,y+7,z),.14,'BrassLight')
    addmesh('Brass',[(x+6,y+7,z),(x+3.3,y+8.4,z),(x+3.3,y+5.6,z)],[(0,1,2)])
    sphere((x,y+7,z),(.7,.7,.7),'StarGlow')
    # Wind causeway reads as its own ritual crossing, with clear space underneath.
    CURRENT='E'; x,y,z=islands['E']['center']
    paved_disc(x,y+.05,z,24)
    beam((x-17,y+14,z+26),(x+17,y+14,z+26),.34,'Brass')
    for i in range(9):
        px=x-13+i*3.25; drop=1.4+1.1*math.sin(i*.8)
        beam((px,y+14,z+26),(px,y+12-drop,z+26),.05,'Brass')
        bell(px,y+10.9-drop,z+26,.34)
    # Mirror temple: shrine pavilions, thin water reflections and stone lanterns.
    CURRENT='F'; x,y,z=islands['F']['center']
    for sign in [-1,1]:
        px=x+sign*34
        for dx in [-5,5]:
            for dz in [-5,5]:
                cylinder((px+dx,y+3.5,z+30+dz),.42,7,'Limestone',12)
                cylinder((px+dx,y+.25,z+30+dz),.8,.5,'Chalk',12)
        lathe((px,y+6.9,z+30),[(0,8),(.7,6.8),(3,3.8),(4,.6),(4.5,0)],'Teal',4)
        torus((px,y+9.8,z+30),1,.13,'Brass')
    # Wide rings create graphical ripples on the solid visual pool surface.
    for dx,dz,rad in [(-6,-4,4.5),(8,5,3),(-12,6,1.8)]:
        torus((x+dx,y+.19,z+dz),rad,.065,'Ivory',segments=36,sides=4)
    for sign in [-1,1]:
        for dz in [-22,22]:
            cylinder((x+sign*25,y+.6,z+dz),.8,1.2,'Chalk',8)
            box((x+sign*25,y+1.65,z+dz),(1.3,1.1,1.3),'Glow')
            lathe((x+sign*25,y+2.15,z+dz),[(0,1.15),(.65,.1)],'Teal',4)
    # Workshop armillary sphere, large brass star machine visible in game view.
    CURRENT='G'; x,y,z=islands['G']['center']
    paved_disc(x,y+.03,z,15)
    x,z=x+42,z-25
    cylinder((x,y+1.3,z),4.5,2.6,'Limestone',24)
    for ang in [0,.65,-.7]: torus((x,y+8,z),6.1,.22,'Brass',axis='z',segments=48,tilt=ang)
    torus((x,y+8,z),6.1,.2,'Copper',axis='y',segments=48)
    sphere((x,y+8,z),(1.2,1.2,1.2),'StarGlow',20,10)
    for i in range(8):
        a=i*TAU/8
        sphere((x+6.1*math.cos(a),y+8+6.1*math.sin(a),z),(.35,.35,.35),'BrassLight')
    # Bell court: rear monumental arcade and hovering fractured orbital halo.
    CURRENT='H'; x,y,z=islands['H']['center']
    paved_disc(x,y+.04,z,29)
    bz=z+38
    for dx in [-12,12]:
        box((x+dx,y+14,bz),(4.8,28,5.5),'Limestone',.65)
        box((x+dx,y+1.1,bz),(8,2.2,8),'Chalk',.4)
        box((x+dx,y+25,bz),(6.2,1.4,7),'Ivory',.3)
        torus((x+dx,y+29,bz),2,.2,'Brass',axis='z')
    # Segmented semicircular arch with alternating warm stone blocks.
    for i in range(20):
        a0=math.pi*i/20; a1=math.pi*(i+1)/20
        verts=[]
        for zz in [-3,3]:
            for rr,aa in [(12,a0),(16,a0),(16,a1),(12,a1)]: verts.append((x+rr*math.cos(aa),y+26+rr*math.sin(aa),bz+zz))
        addmesh('Ivory' if i%3 else 'Chalk',verts,[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)])
    beam((x,y+40,bz),(x,y+30,bz),.28,'WoodDark')
    bell(x,y+16,bz,2.65)
    torus((x,y+51,bz),20,.48,'Brass',axis='z',segments=64,tilt=.23)
    # An interrupted stone halo gives the final landmark a strong ancient silhouette.
    for i in range(18):
        a0=i*TAU/18+.035; a1=(i+1)*TAU/18-.035
        verts=[]
        for zz in [-1.2,1.2]:
            for rr,a in [(21,a0),(23,a0),(23,a1),(21,a1)]:
                xx=rr*math.cos(a); yy=rr*math.sin(a)
                verts.append((x+xx*math.cos(.23)+yy*math.sin(.23),y+51+yy*math.cos(.23)-xx*math.sin(.23),bz+zz))
        addmesh('Limestone' if i%3 else 'Ivory',verts,[(0,1,2,3),(4,7,6,5),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)])
    star=[]
    for i in range(16):
        a=i*TAU/16; r=17 if i%2==0 else 4.8
        star.append((x+r*math.sin(a),y+.085,z+r*math.cos(a)))
    addmesh('BrassLight',star,[tuple(reversed(range(16)))])
    for i in range(9):
        a=i*TAU/9
        sphere((x+20*math.cos(a),y+51+20*math.sin(a),bz),(.8,.8,.8),'StarGlow',10,5)
    # Side island landmarks: pond, suspended postal kiosk, cave and telescope.
    for sid in ['S1','S2','S3','S4']:
        CURRENT=sid; x,y,z=islands[sid]['center']
        if sid=='S1':
            cylinder((x,y+.06,z),8,.1,'Water',32)
            for a in [0,1,2.7,4]: sphere((x+10*math.cos(a),y+.5,z+10*math.sin(a)),(2.5,.9,1.6),'RockLight',10,5,False)
        elif sid=='S2':
            x+=10
            box((x,y+1.6,z),(1.4,3.2,1.2),'Teal',.2)
            box((x,y+2.2,z-.62),(1,.25,.15),'WoodDark')
            lathe((x,y+3.2,z),[(0,1.5),(.75,0)],'Copper',4)
            for sign in [-1,1]: ribbon([(x+sign*5,y,z+3),(x+sign*7,y+10,z+4),(x+sign*12,y+16,z+10)],'Wood',.2)
        elif sid=='S3':
            z-=10; x+=3
            for a in [0,.55,1.1,1.65,2.2,2.8,3.3]:
                sphere((x+8*math.cos(a),y+4+6*math.sin(a),z),(4,5,7),'RockLight',8,5,False)
            for dx in [-7,0,7]: lantern(x+dx,y,z+4,.8)
        else:
            paved_disc(x,y+.04,z,12)
            z+=9
            for dx,dz in [(-3,-2),(3,-2),(0,3)]: beam((x+dx,y,z+dz),(x,y+4,z),.22,'Brass')
            beam((x,y+4,z),(x+6,y+8,z+4),1.2,'Teal',16,r_end=1.6)
            sphere((x+6,y+8,z+4),(1.5,1.5,1.5),'StarGlow',12,6)


def dock_landmark(islands):
    global CURRENT
    CURRENT='A'; x,y,z=islands['A']['center']
    # A moored sky skiff is an actual sculpted hull, with a full patterned sail.
    bx,bz=x-72,z-15
    verts=[]
    for yy,w,l in [(0,2.4,8),(2.2,5.5,12),(3,5.9,12.3)]:
        for i in range(20):
            a=i*TAU/20
            verts.append((bx+w*math.sin(a),y+yy,bz+l*math.cos(a)))
    addmesh('Wood',verts,[(j*20+i,j*20+(i+1)%20,(j+1)*20+(i+1)%20,(j+1)*20+i) for j in range(2) for i in range(20)])
    for i in range(20): beam(verts[40+i],verts[40+(i+1)%20],.18,'Brass')
    box((bx,y+2.6,bz),(8.5,.3,15),'WoodLight')
    beam((bx,y+2,bz),(bx+2,y+25,bz),.3,'WoodDark')
    textured_quad([(bx+.8,y+10,bz-.2),(bx+10,y+10,bz-.2),(bx+7,y+23,bz-.2),(bx+1.8,y+23,bz-.2)],'Cloth')
    for sign in [-1,1]:
        sphere((bx+sign*4,y+16,bz+7),(4,6,4),'Ivory',16,8)
        ribbon([(bx+sign*4,y+11,bz+7),(bx+sign*4.5,y+3,bz+6)],'Wood',.07)
        torus((bx+sign*4,y+16,bz+7),4.02,.12,'Brass',axis='z')
    for i in range(7):
        px=x+26+(i%3)*3.1; pz=z-25+(i//3)*4
        box((px,y+1.1,pz),(2.8,2.2,2.7),'WoodLight')
        for dx in [-1,1]: box((px+dx,y+1.1,pz-1.36),(.12,2.15,.12),'WoodDark')
    # Tall slanted entry arch with tiny welcoming lanterns.
    for dx in [-7,7]: cylinder((x+dx,y+4,z+15),.55,8,'Limestone',12)
    beam((x-8,y+8.5,z+15),(x+8,y+8.5,z+15),.32,'Teal')
    lantern(x-6,y,z+14); lantern(x+6,y,z+14)


def bridge_details(bridges):
    global CURRENT
    for bridge in bridges:
        CURRENT='Bridge_'+bridge['id']; width=bridge['width']
        sections=[tuple(Vector(p) for p in section) for section in bridge['crossSections']]
        centers=[(left+right)*.5 for left,right in sections]
        distances=[0.0]
        for a,b in zip(centers,centers[1:]): distances.append(distances[-1]+(b-a).length)
        length=distances[-1]
        # Both longitudinal beams consume the SAME section endpoints as the surface.
        # Per-segment perpendicular offsets leave gaps/overlaps when a bridge bends.
        for first,last in zip(sections,sections[1:]):
            for side in range(2):
                beam(tuple(first[side]-Vector((0,.40,0))),tuple(last[side]-Vector((0,.40,0))),.31,'WoodDark')

        def at_distance(distance):
            index=next((i for i in range(len(distances)-1) if distances[i+1]>=distance),len(distances)-2)
            t=(distance-distances[index])/max(1e-8,distances[index+1]-distances[index])
            return tuple(sections[index][side].lerp(sections[index+1][side],t) for side in range(2))

        # Arc-length spacing runs once over the complete bridge, including short corner samples.
        count=max(1,math.ceil(length/1.55))
        for i in range(count):
            left,right=at_distance((i+.5)*length/count)
            beam(tuple(left-Vector((0,.11,0))),tuple(right-Vector((0,.11,0))),.18,'Wood' if i%5==0 else 'WoodLight',4)
        for distance in range(24,int(length)-10,32):
            left,right=at_distance(distance); p=(left+right)*.5
            cylinder(tuple(p-Vector((0,9,0))),width*.47,16,'Limestone',8,radius_top=width*.64)
            cylinder(tuple(p-Vector((0,20,0))),width*.18,9,'Rock',6,radius_top=width*.47)
        # Tiny continuous inlaid seams and warm studs lead the eye through the curve.
        for distance in range(6,int(length)-3,12):
            left,right=at_distance(distance)
            for p in [left,right]:
                sphere(tuple(p+Vector((0,1.45,0))),(.18,.23,.18),'Glow',8,4)


def landscape_scatter(islands,obstacles):
    global CURRENT
    for sid,island in islands.items():
        CURRENT=sid; x,y,z=island['center']; outline=island['outline']
        for i,(px,pz) in enumerate(outline):
            # Plant only at periphery, away from all connection center corridors.
            xx=x+(px-x)*.89; zz=z+(pz-z)*.89
            if any(abs(xx-o['center'][0])<o['size'][0]/2+7 and abs(zz-o['center'][2])<o['size'][2]/2+7 for o in obstacles if o.get('island')==sid): continue
            if i%3==0 and sid not in ['E','H','D']:
                tscale=.8+RNG.random()*.45
                # 变体判定与 tree() 内部的分支保持一致，换模型不换区域叶色规律。
                base=sid.split('_')[0]
                variant=('gold' if base in ['C','S1'] else
                         'blossom' if base in ['B','F','S2'] and math.sin(xx*.137+zz*.073)>-.2 else
                         'moonleaf' if base in ['D','S3'] and math.cos(xx*.07-zz*.09)>.2 else 'plain')
                if TRIPO_PROPS is None or not TRIPO_PROPS.replace_tree(sys.modules[__name__],TRIPO_DIR,
                                                           xx,y,zz,tscale,variant,hash((sid,i))&0xffffffff):
                    tree(xx,y,zz,tscale,gold=sid in ['C','S1'])
            else:
                # 灌木原为 8 段 4 环不平滑的正椭球，读成硬边低模球。改平滑并提高分段，
                # 同时按实例扰动三轴比例——完美椭球本身就假，只改着色不够。
                # 用独立 RNG 取扰动，避免改动全局 RNG 序列而扰乱后续所有摆放。
                jitter=random.Random(hash((sid,i,'bush'))&0xffffffff)
                sphere((xx,y+.7,zz),(2.2*jitter.uniform(.78,1.28),1.2*jitter.uniform(.82,1.35),
                                     2.0*jitter.uniform(.78,1.28)),
                       'Leaf' if i%2 else 'LeafLight',12,6,True)
            if i%2==0:
                # 石块保留硬边（岩石本就有棱），但提高分段并打散比例，不再是一排同样的圆球。
                jitter=random.Random(hash((sid,i,'rock'))&0xffffffff)
                sphere((xx+2,y+.6,zz-1),(1.7*jitter.uniform(.65,1.4),1.0*jitter.uniform(.7,1.5),
                                         1.4*jitter.uniform(.65,1.4)),
                       'RockLight' if i%3 else 'Rock',10,5,False)
        for i in range(8 if len(sid)==1 else 3):
            a=i*TAU/8+.4; rad=min(island['size'])*.3
            xx=x+rad*math.cos(a); zz=z+rad*math.sin(a)
            for k in range(3): sphere((xx+k*.43,y+.23,zz+.2*math.sin(k)),(.25,.38,.25),'Flower',8,4,True)


def world_clouds():
    global CURRENT
    CURRENT='CloudSea'
    # Broad closed horizon; backdrop shader fades this floor into layered mist, not a visible edge.
    addmesh('CloudBackdrop',[(-6000,-160,-6000),(-6000,-160,6000),(6000,-160,6000),(6000,-160,-6000)],[(0,1,2,3)])
    cloud_rng=random.Random(930108)
    for i in range(56):
        a=i*2.39996
        if i<34:
            r=125+76*math.sqrt(i); y=-75-cloud_rng.random()*14; base=30+cloud_rng.random()*29
        elif i<46:
            r=600+(i-34)*17; y=-50+cloud_rng.random()*26; base=47+cloud_rng.random()*34
        else:
            r=1350+(i-46)*85; y=-40+cloud_rng.random()*80; base=105+cloud_rng.random()*90
        CURRENT='CloudBank_'+str(i//4)
        cloud_cluster((r*math.cos(a),y,r*math.sin(a)),base,i,'near' if i<34 else 'mid' if i<46 else 'far')
    # Very distant fragment silhouettes are visual geometry without navigation/collision.
    for i in range(12):
        CURRENT='Distant_'+str(i)
        a=i*TAU/12; x=710*math.cos(a); z=650*math.sin(a); y=60+math.sin(i*2)*55
        outline=[(x+(18+4*math.sin(k*5+i))*math.cos(k*TAU/9),z+(15+4*math.sin(k*6+i))*math.sin(k*TAU/9)) for k in range(9)]
        island_shell({'id':'far','center':[x,y,z],'outline':outline})
        cylinder((x,y,z),13,.5,'Grass',9)


# 近景塔状积云最出体积，中远景用云筏铺底，碎云打散节奏；权重按距离环切换。
CLOUD_SHAPE_WEIGHTS={'near':(('tower',.60),('raft',.25),('wisp',.15)),
                     'mid':(('tower',.35),('raft',.45),('wisp',.20)),
                     'far':(('tower',.15),('raft',.55),('wisp',.30))}


def cloud_shape(rng,tier):
    roll=rng.random(); total=0
    for name,weight in CLOUD_SHAPE_WEIGHTS[tier]:
        total+=weight
        if roll<total: return name
    return 'raft'


def cloud_lobes(rng,base,shape):
    """云的融球布瓣。返回 (x, 高度, z, 半径)，高度以云底为 0。

    真实积云是平底加花椰菜状的层叠冠部。旧版把主瓣摆成一圈再整体压扁，上下都成椭球，
    56 朵云因此读成同一颗棉花球。这里按云型分别布瓣、主瓣坐在同一高度，配合调用处的
    平底裁剪形成凝结高度那条平边，冠部则用逐层递减的小瓣堆出起伏。
    """
    lobes=[]
    if shape=='tower':
        for i in range(3):                                   # 底盘三大瓣
            a=i*TAU/3+rng.uniform(0,.6)
            lobes.append((math.cos(a)*base*.52,base*.60,math.sin(a)*base*.40,base*.80))
        for i in range(4):                                   # 中层冠
            a=i*TAU/4+rng.uniform(0,.8)
            lobes.append((math.cos(a)*base*.36,base*1.12,math.sin(a)*base*.28,base*.56))
        for i in range(3):                                   # 顶冠，最小
            a=i*TAU/3+rng.uniform(0,1.0)
            lobes.append((math.cos(a)*base*.20,base*1.55,math.sin(a)*base*.15,base*.40))
    elif shape=='raft':
        for i in range(5):                                   # 横向铺开的筏体
            a=i*TAU/5+rng.uniform(0,.5)
            lobes.append((math.cos(a)*base*.95,base*.55,math.sin(a)*base*.62,base*.72))
        lobes.append((0,base*.62,0,base*.95))
        for i in range(4):                                   # 低缓起伏，不起塔
            a=i*TAU/4+rng.uniform(0,.9)
            lobes.append((math.cos(a)*base*.52,base*.98,math.sin(a)*base*.34,base*.44))
    else:
        for i in range(4):                                   # 碎云：瓣少半径杂，无冠部
            a=i*TAU/4+rng.uniform(0,1.2)
            lobes.append((math.cos(a)*base*rng.uniform(.5,1.0),base*rng.uniform(.5,.8),
                          math.sin(a)*base*rng.uniform(.35,.75),base*rng.uniform(.42,.70)))
    return lobes


def cloud_cluster(center,base,seed,tier='near'):
    """Offline metaball fusion removes the intersecting-egg seams of primitive clouds.

    Only the resulting smooth mesh is exported. There is no metaball evaluation in Unity.
    """
    rng=random.Random(seed+9120)
    shape=cloud_shape(rng,tier)
    # 近景给最细的体素，远景放粗省三角面；旧版三档同精度，近处不够细远处又浪费。
    detail=.112 if tier=='near' else .135 if tier=='mid' else .165
    data=bpy.data.metaballs.new('AuthorCloudFusion'); data.resolution=base*detail
    data.render_resolution=data.resolution
    # 阈值从 .88 提到 .96：旧值把瓣融成一坨土豆，提高后各瓣仍相连但保留花椰菜起伏。
    data.threshold=.96
    obj=bpy.data.objects.new('AuthorCloudFusion',data); bpy.context.scene.collection.objects.link(obj)
    for x,y,z,r in cloud_lobes(rng,base,shape):
        element=data.elements.new(); element.co=(x,z,y)
        element.radius=r*rng.uniform(.93,1.10); element.stiffness=2
    bpy.context.view_layer.update()
    mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    try:
        x,y,z=center; yaw=rng.random()*TAU
        # 纵向比例按云型给：塔状要蓬起来，云筏要压扁，旧版一律 .63-.87 全成了扁豆。
        sy=rng.uniform(.92,1.15) if shape=='tower' else rng.uniform(.52,.70) if shape=='raft' else rng.uniform(.60,.85)
        sx,sz=rng.uniform(.88,1.2),rng.uniform(.73,1.04)
        floor=base*.42                                       # 凝结高度：以下削平成云底
        cosine,sine=math.cos(yaw),math.sin(yaw)
        vertices=[(x+v.co.x*sx*cosine+v.co.y*sz*sine,y+max(v.co.z,floor)*sy,
                   z-v.co.x*sx*sine+v.co.y*sz*cosine) for v in mesh.vertices]
        faces=[tuple(reversed(p.vertices)) for p in mesh.polygons]
        if not faces: raise RuntimeError('Empty fused cloud mesh')
        # 空气透视：近景云偏暖白，中景转中性灰，远景压向天色，靠材质分层拉开纵深。
        # 旧写法 7 朵里只有 1 朵换色，整片云海因此读成均匀的白色圆点。
        material='Cloud' if tier=='near' else 'CloudShade' if tier=='mid' else 'CloudFar'
        if tier=='near' and seed%3==0: material='CloudShade'
        addmesh(material,vertices,faces,smooth=True)
    finally:
        bpy.data.meshes.remove(mesh)
        bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.metaballs.remove(data)


def smooth_path(points,width,mat='Limestone'):
    """Flat decorative paving follows walkable terrain; it adds no separate collision."""
    if len(points)<2: return
    expanded=[Vector(points[0])]+[Vector(p) for p in points]+[Vector(points[-1])]
    samples=[]
    for i in range(1,len(expanded)-2):
        p0,p1,p2,p3=expanded[i-1:i+3]
        count=max(4,int((p2-p1).length/2.8))
        for k in range(count):
            t=k/count
            samples.append(.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t))
    samples.append(Vector(points[-1])); verts=[]
    PAVING_TRACKS.append(([(p.x,p.z) for p in samples],width))
    for i,p in enumerate(samples):
        direction=samples[min(i+1,len(samples)-1)]-samples[max(0,i-1)]
        right=Vector((direction.z,0,-direction.x)).normalized()
        w=width*(1+.055*math.sin(i*.83))
        verts.extend([tuple(p-right*w/2),tuple(p+right*w/2)])
    faces=[(i*2,i*2+2,i*2+3,i*2+1) for i in range(len(samples)-1)]
    addmesh(mat,verts,faces)


def make_paths(islands,bridges):
    global CURRENT
    routes={
      'A': [[(0,-350),(0,-323),(5,-302),(0,-278),(0,-250)],[(-46,-323),(-25,-326),(0,-326)],[(3,-300),(23,-286),(36,-281)]],
      'B': [[(0,-205),(0,-184),(12,-158),(0,-130),(3,-103),(-3,-76),(-25,-55)],
            [(-95,-110),(-71,-122),(-48,-120),(-20,-118),(0,-130),(29,-147),(66,-136),(95,-120)],
            [(65,-135),(65,-113),(66,-90),(55,-55)],
            [(-57,-55),(-71,-80),(-77,-107),(-70,-122)],
            [(-66,-176),(-42,-179),(-20,-179),(10,-182),(44,-180),(63,-166),(66,-136)],
            [(-49,-172),(-48,-145),(-48,-120)],
            [(39,-171),(57,-159),(66,-136)]],
      'C': [[(-135,-110),(-164,-120),(-211,-115),(-232,-88),(-247,-56),(-250,-25)],
            [(-232,-113),(-277,-124),(-304,-131),(-325,-150)]],
      'D': [[(-250,20),(-241,52),(-259,85),(-250,110),(-220,113),(-192,121),(-155,110)],
            [(-250,110),(-271,109),(-310,121),(-345,140)],
            [(-242,57),(-226,46),(-210,20)]],
      'E': [[(-80,110),(-55,119),(-27,110),(0,110),(39,112),(80,110)],
            [(0,110),(21,125),(39,151),(40,185)],[(-80,80),(-55,79),(-25,96),(0,110)]],
      'F': [[(145,-40),(170,-37),(186,-8),(196,25),(216,37),(230,50)],
            [(171,-38),(174,-79),(204,-96),(247,-101),(287,-91),(309,-79),(335,-80)],
            [(198,29),(231,35),(274,36),(296,17),(295,-28),(287,-90)]],
      'G': [[(285,105),(272,126),(251,149),(242,176),(219,187),(194,189),(165,180)],
            [(242,176),(230,153),(211,133),(205,105)],
            [(298,148),(314,168),(310,193),(320,210),(335,210)]],
      'H': [[(90,270),(67,260),(37,269),(15,284),(0,300),(0,314)]],
      'S1': [[(-350,-205),(-340,-216),(-334,-236),(-342,-248),(-350,-248)]],
      'S2': [[(-370,192.5),(-367,208),(-365,218)]],
      'S3': [[(355,-137.5),(343,-151),(338,-167),(342,-188)]],
      'S4': [[(350,277.5),(342,291),(338,305),(350,307)]],
    }
    portals=defaultdict(list)
    for bridge in bridges:
        for sid,index,neighbor in [(bridge['from'],0,1),(bridge['to'],-1,-2)]:
            mouth=bridge['path'][index]; approach=bridge['path'][neighbor]
            dx,dz=mouth[0]-approach[0],mouth[2]-approach[2]
            length=math.hypot(dx,dz)
            portals[sid].append(((mouth[0],mouth[2]),(dx/length,dz/length)))

    def align_mouths(sid,points):
        points=list(points)
        for end in (0,-1):
            ordered=points if end==0 else list(reversed(points))
            mouth,inward=min(portals[sid],key=lambda portal:math.dist(ordered[0],portal[0]))
            if math.dist(ordered[0],mouth)>4:
                continue
            # Two collinear controls keep the first 8 m exactly tangent even under
            # Catmull-Rom interpolation; the curve begins further inside the island.
            interior=list(ordered[1:])
            while len(interior)>1 and sum((interior[0][axis]-mouth[axis])*inward[axis] for axis in (0,1))<16:
                interior.pop(0)
            lead=[mouth]+[(mouth[0]+inward[0]*distance,mouth[1]+inward[1]*distance) for distance in (8,16)]
            aligned=lead+interior
            points=aligned if end==0 else list(reversed(aligned))
        return points

    for sid,paths in routes.items():
        CURRENT=sid+'_Paving'
        for index,pts in enumerate(paths):
            # Crossing decorative strips need distinct elevations to avoid coplanar artifacts.
            y=islands[sid]['height']+.028+index*.011
            smooth_path([(x,y,z) for x,z in align_mouths(sid,pts)],6.4 if len(sid)==1 else 4.2)
    # A continuous, flush stone promenade surrounds the mirror pool.
    CURRENT='F_Paving'; y=islands['F']['height']+.05
    smooth_path([(198,y,-66),(198,y,-6),(212,y,-4),(280,y,-4),(283,y,-18),(283,y,-62),(266,y,-66),(198,y,-66)],4.4)


def bench(x,y,z,yaw=0):
    box((x,y+.65,z),(3.7,.23,1.15),'WoodLight',.06,yaw)
    for dx in [-1.3,1.3]:
        box((x+dx*math.cos(yaw),y+.3,z-dx*math.sin(yaw)),(.22,.6,.85),'WoodDark',.04,yaw)
    box((x,y+1.25,z+.52),(3.7,.75,.18),'Wood',.06,yaw)


def barrel(x,y,z,scale=1):
    lathe((x,y,z),[(0,.55*scale),(.2*scale,.62*scale),(1*scale,.72*scale),(1.6*scale,.62*scale),(1.7*scale,.55*scale)],'WoodLight',12)
    for yy in [.25,1.45]: torus((x,y+yy*scale,z),.65*scale,.075*scale,'WoodDark',segments=12,sides=4)
    cylinder((x,y+1.67*scale,z),.56*scale,.1*scale,'Wood',12)


def duck_statue(x,y,z,scale=1):
    cylinder((x,y+.18,z),1.4*scale,.35,'Limestone',16)
    sphere((x,y+1.2*scale,z),(.85*scale,1.08*scale,.8*scale),'Ivory',14,8)
    sphere((x,y+2.38*scale,z-.08*scale),(.82*scale,.85*scale,.78*scale),'Ivory',14,8)
    sphere((x,y+2.18*scale,z-.86*scale),(.53*scale,.19*scale,.43*scale),'Brass',12,6)
    for sign in [-1,1]: sphere((x+sign*.37*scale,y+2.53*scale,z-.72*scale),(.08*scale,.11*scale,.07*scale),'WoodDark',8,4)
    torus((x,y+1.9*scale,z),.65*scale,.13*scale,'Teal',segments=18,sides=5)
    for sign in [-1,1]: sphere((x+sign*.82*scale,y+1.28*scale,z),(.23*scale,.58*scale,.42*scale),'Limestone',10,6)


def garden_clump(x,y,z,radius=4):
    # Ground-level flowers and foliage have no collision, like official cosmetic grass.
    count=max(6,int(radius*3))
    for i in range(count):
        a=i*2.39996; r=radius*math.sqrt((i+.5)/count)
        px=x+r*math.cos(a); pz=z+r*math.sin(a)
        leaf='LeafLight' if i%3 else 'Leaf'
        sphere((px,y+.28,pz),(.6,.38,.55),leaf,7,4,False)
        if i%2==0:
            for j in range(3):
                sphere((px+.16*j,y+.65+.09*j,pz+.2*math.sin(j)),(.20,.12,.20),'Flower' if i%3 else 'Coral',6,3,False)


def village_life(layout,islands):
    global CURRENT
    CURRENT='B_Life'; y=islands['B']['height']
    # Market dressing hugs existing buildings. Clear paths remain about six metres wide.
    for index,o in enumerate(x for x in layout['obstacles'] if x['island']=='B' and 'house' in x['kind']):
        x,_,z=o['center']; w,h,d=o['size']; front=z-d/2
        # Low porch paving and side flower borders pull each cottage into a little garden.
        box((x,y+.07,front-1.9),(w*.92,.12,3.8),'Limestone',.02)
        garden_clump(x-w*.53,y,z+d*.28,2.5)
        garden_clump(x+w*.53,y,z+d*.28,2.2)
        for side in [-1,1]:
            for i in range(4):
                px=x+side*(w*.5+.5); pz=front+1.5+i*2.1
                sphere((px,y+.45,pz),(.64,.5,.65),'LeafLight',8,4,False)
        barrel(x+w*.45,y,front-.7,.75)
        if index%2:
            bench(x-w*.2,y,front-2.7)
        else:
            # Stall counter plus three shallow trays of painted produce.
            box((x,y+.85,front-2.2),(4.8,1.7,1.25),'Wood',.08)
            for tray in range(3):
                tx=x-1.5+tray*1.5
                box((tx,y+1.76,front-2.2),(1.3,.18,1),'WoodLight',.04)
                for j in range(5):
                    sphere((tx+.34*math.cos(j*2.4),y+2,front-2.2+.28*math.sin(j*2.4)),(.23,.23,.23),['Coral','BrassLight','LeafLight'][tray],8,4)
        # A projected hanging shop sign gives each facade a unique silhouette.
        beam((x-w*.4,y+4.5,front),(x-w*.4,y+4.5,front-1.8),.08,'WoodDark')
        torus((x-w*.4,y+3.8,front-1.9),.62,.08,'Brass',axis='z',segments=20)
        sphere((x-w*.4,y+3.8,front-1.9),(.42,.46,.09),['Coral','BrassLight','Ivory'][index%3],10,5)
    duck_statue(18,y,-112,.9)
    for x,z in [(-16,-149),(20,-151),(-17,-112),(26,-115),(-63,-134),(66,-124),(-40,-69),(45,-65)]:
        lantern(x,y,z,1.15)
        garden_clump(x+1.9,y,z+1.3,1.7)
    for x,z in [(-76,-171),(75,-168),(-79,-80),(78,-81),(-46,-68),(34,-76)]:
        garden_clump(x,y,z,6)
    # A village banner hangs well above the northern passage, fully textured.
    for x in [-6,6]: beam((x,y,-75),(x,y+7.5,-75),.17,'WoodDark')
    textured_quad([(-6,y+5.2,-75),(6,y+5.2,-75),(6,y+7.2,-75),(-6,y+7.2,-75)],'Cloth')


def cliff_dressing(islands):
    global CURRENT
    for sid,island in islands.items():
        CURRENT=sid+'_Cliff'; x,y,z=island['center']; outline=island['outline']
        # Broad limestone clumps break the extrusion silhouette below the walk surface.
        for i,(a,b) in enumerate(zip(outline,outline[1:]+outline[:1])):
            length=math.dist(a,b)
            for j in range(max(1,int(length/17))):
                t=(j+.45)/max(1,int(length/17)); xx=a[0]+(b[0]-a[0])*t; zz=a[1]+(b[1]-a[1])*t
                rx=4+RNG.random()*4; ry=5+RNG.random()*9
                sphere((xx,y-ry-1,zz),(rx,ry,rx*.8),'RockLight' if (i+j)%3 else 'Rock',8,5,False)
                if (i+j)%3==0:
                    sphere((xx,y-2,zz),(rx*.8,2,rx*.65),'Forest',8,4,False)
                    for k in range(3):
                        ribbon([(xx+k*.8,y-2,zz),(xx+k*.8-1,y-7,zz-.6),(xx+k*.5+1,y-13-k*2,zz-1)],'Leaf',.13)
    # Decorative cloud-fed waterfalls stay beyond the collision boundary.
    for sid,xx,zz,w in [('F',336,-23,7),('D',-345,102,5),('C',-293,-194,4)]:
        CURRENT=sid+'_Waterfall'; y=islands[sid]['height']
        verts=[]
        for row in range(9):
            t=row/8
            for side in [-1,1]: verts.append((xx+side*w*(.5+.2*t),y-2-45*t,zz+math.sin(t*3)*3))
        addmesh('Water',verts,[(i*2,i*2+1,i*2+3,i*2+2) for i in range(8)])
        for i in range(4): sphere((xx+(i-1.5)*w*.4,y-47,zz),(w*.65,2.2,3.8),'Cloud',10,5)


def terrain_and_boundary(layout):
    global CURRENT
    ground=layout['ground']; verts=ground['vertices']; faces=ground['triangles']
    regions=ground.get('triangleRegions',['Ground']*len(faces)); byregion=defaultdict(list)
    for f,r in zip(faces,regions): byregion[r].append(f)
    for region,tri in byregion.items():
        mat=('GrassLight' if region in ['A','C','S1'] else 'Grass') if region in {s['id'] for s in layout['islands']} else 'WoodLight'
        # Compact indices preserve per-region frustum culling.
        used=sorted(set(i for f in tri for i in f)); remap={old:new for new,old in enumerate(used)}
        vv=[verts[i] for i in used]; ff=[tuple(remap[i] for i in f) for f in tri]
        create_object('VIS_Ground_'+str(region),vv,ff,mat)
        create_object('COL_Ground_'+str(region),vv,ff,hidden=True)
    nav=layout['navigation']
    create_object('NAV_SkyIsland',nav['vertices'],nav['triangles'],hidden=True)
    # Continuous physical safety rail on the outside of the exact ground domain.
    CURRENT='SafetyRail'; railv=[]; railf=[]
    edge_regions={tuple(sorted((f[i],f[(i+1)%3]))):r for f,r in zip(faces,regions) for i in range(3)}
    island_ids={s['id'] for s in layout['islands']}
    post_cells=defaultdict(list)
    edges=ground.get('boundaryEdges',[])
    for e in edges:
        a,b=Vector(verts[e[0]]),Vector(verts[e[1]])
        if (a-b).length<.12: continue
        mid=(a+b)*.5
        if any(abs(mid.x-o['center'][0])<o['size'][0]/2+.15 and abs(mid.z-o['center'][2])<o['size'][2]/2+.15 for o in layout['obstacles']): continue
        region=edge_regions[tuple(sorted(e))]
        CURRENT=region if region in island_ids else 'Bridge_'+region
        # Two-sided vertical mesh (runtime walls) at real walk edge, lower timber in view.
        idx=len(railv); railv.extend([tuple(a-Vector((0,.25,0))),tuple(b-Vector((0,.25,0))),tuple(b+Vector((0,1.7,0))),tuple(a+Vector((0,1.7,0)))])
        railf.extend([(idx,idx+1,idx+2,idx+3),(idx+3,idx+2,idx+1,idx)])
        for height in [.55,1.05]: beam(tuple(a+Vector((0,height,0))),tuple(b+Vector((0,height,0))),.12,'Wood',6)
        count=max(1,math.ceil((b-a).length/4.5))
        for i in range(count):
            p=a+(b-a)*i/count
            cell=tuple(math.floor(c/3) for c in p)
            # Dense curve/triangulation vertices must not each become a decorative post.
            if any((p-q).length_squared<6.25 for dx in [-1,0,1] for dy in [-1,0,1] for dz in [-1,0,1]
                   for q in post_cells.get((cell[0]+dx,cell[1]+dy,cell[2]+dz),[])): continue
            post_cells[cell].append(p.copy())
            beam(tuple(p-Vector((0,.12,0))),tuple(p+Vector((0,1.35,0))),.19,'WoodDark',6)
            cylinder(tuple(p+Vector((0,1.40,0))),.24,.16,'Brass',6,radius_top=.11)
    if railv: create_object('COL_Rail_Perimeter',railv,railf,hidden=True)


def build_materials(assets):
    for name,color in PALETTE.items():
        mat=bpy.data.materials.new('Sky_'+name); mat.diffuse_color=linear_color(rgba(color)); mat.use_nodes=True
        bsdf=mat.node_tree.nodes.get('Principled BSDF'); bsdf.inputs['Base Color'].default_value=linear_color(rgba(color))
        bsdf.inputs['Roughness'].default_value=.77
        if name in ['Brass','BrassLight','Copper']: bsdf.inputs['Metallic'].default_value=.32; bsdf.inputs['Roughness'].default_value=.4
        if name=='Water': bsdf.inputs['Roughness'].default_value=.21; bsdf.inputs['Metallic'].default_value=.25
        if name in EMISSION:
            bsdf.inputs['Emission Color'].default_value=rgba(color); bsdf.inputs['Emission Strength'].default_value=EMISSION[name]
        if name in MODEL_TEXTURES:
            img=bpy.data.images.load(str(assets/MODEL_TEXTURES[name]),check_existing=True); img.pack()
            node=mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image=img
            mat.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
        elif name in ['Mural','Cloth']:
            img=bpy.data.images.load(str(assets/'Textures'/('sky_mural.png' if name=='Mural' else 'sky_cloth.png')))
            img.pack(); node=mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image=img
            mat.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
        elif name in TILED_TEXTURES:
            filename,_,tint=TILED_TEXTURES[name]
            img=bpy.data.images.load(str(assets/'Textures'/filename),check_existing=True); img.pack()
            tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=img; tex.extension='REPEAT'
            mix=mat.node_tree.nodes.new('ShaderNodeMixRGB'); mix.blend_type='MULTIPLY'; mix.inputs[0].default_value=1
            mix.inputs[2].default_value=linear_color(tint)
            mat.node_tree.links.new(tex.outputs['Color'],mix.inputs[1]); mat.node_tree.links.new(mix.outputs[0],bsdf.inputs['Base Color'])
        MATERIALS[name]=mat


def main():
    global CURRENT
    parser=argparse.ArgumentParser(); parser.add_argument('--project',required=True); parser.add_argument('--skip-render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]); project=Path(args.project).resolve()
    if not (project/'ProjectSettings'/'ProjectVersion.txt').is_file(): raise ValueError('Existing Unity project required')
    assets=project/'Assets'/'SkyIsland'; source=project/'ArtSource'/'SkyIsland'
    assets.mkdir(parents=True,exist_ok=True); source.mkdir(parents=True,exist_ok=True)
    layout=json.loads((assets/'sky_island_layout.json').read_text(encoding='utf-8-sig'))
    for obj in list(bpy.data.objects): bpy.data.objects.remove(obj,do_unlink=True)
    # Tripo3D 件的材质必须在 build_materials 之前登记，否则 MATERIALS 里没有对应条目。
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    import sky_island_tripo_props
    global TRIPO_DIR,TRIPO_PROPS
    TRIPO_DIR=source/'tripo'; TRIPO_PROPS=sky_island_tripo_props
    sky_island_tripo_props.reset()
    sky_island_tripo_props.register(sys.modules[__name__],TRIPO_DIR)
    build_materials(assets)
    islands={s['id']:s for s in layout['islands']}
    for sid,isl in islands.items(): CURRENT=sid; island_shell(isl)
    terrain_and_boundary(layout)
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    import sky_island_settlement
    settlement_records=[]
    for index,obs in enumerate(layout['obstacles']):
        CURRENT=obs.get('island','Props'); x,y,z=obs['center']; w,h,d=obs['size']; kind=obs['kind'].lower()
        collision_box(obs['id'],obs['center'],obs['size'])
        # The navigation/collision surface excludes obstacles; the visible soil must still
        # continue beneath narrow trunks, posts and round pedestals instead of exposing void.
        floor=y-h/2-.001
        addmesh('GrassLight' if CURRENT in ['A','C','S1'] else 'Grass',
                [(x-w/2,floor,z-d/2),(x-w/2,floor,z+d/2),(x+w/2,floor,z+d/2),(x+w/2,floor,z-d/2)],[(0,1,2,3)])
        if kind == 'life_prop':
            settlement_records.append(sky_island_settlement.place_model(sys.modules[__name__],obs))
            continue
        # 有 Tripo 替换件就用它取代下面的程序化外观。碰撞盒已在上面登记，不受影响。
        # 传入所属岛的中心，替换件据此把正面转向广场方向。
        _isl=next((i for i in layout['islands'] if i['id']==obs.get('island')),None)
        if sky_island_tripo_props.replace_obstacle(sys.modules[__name__],obs,source/'tripo',
                                                   (_isl['center'][0],_isl['center'][2]) if _isl else None):
            continue
        if kind in ['wind_beacon','astrolabe','bell','lookout','cave_rock','chime_support','duck_statue','pavilion_support']:
            # Bespoke landmark geometry is built below at these exact registered footprints.
            continue
        if any(k in kind for k in ['house','building','tea','shed','hut','workshop','barn','mill','temple','pavilion']): house(obs,index)
        elif 'pool' in kind or 'water' in kind:
            box((x,y-h/2+.04,z),(w,.15,d),'Water',.02)
            for sign in [-1,1]:
                box((x+sign*w/2,y-h/2+.12,z),(.55,.35,d+.55),'Ivory')
                box((x,y-h/2+.12,z+sign*d/2),(w+.55,.35,.55),'Ivory')
        elif 'tree' in kind: tree(x,y-h/2,z,max(1,w/6))
        elif 'dome' in kind:
            cylinder((x,y-h/2+h*.35,z),w*.45,h*.7,'Limestone',24)
            lathe((x,y-h/2+h*.7,z),[(0,w*.52),(h*.18,w*.48),(h*.36,w*.36),(h*.46,w*.12),(h*.49,0)],'Copper',24)
            for i in range(8):
                a=i*TAU/8; beam((x+w*.51*math.cos(a),y-h/2+h*.7,z+w*.51*math.sin(a)),(x+w*.12*math.cos(a),y-h/2+h*1.16,z+w*.12*math.sin(a)),.12,'BrassLight')
        elif kind in ['wind_pillar','memorial']:
            cylinder((x,y,z),w*.44,h,'Limestone',8)
            cylinder((x,y+h/2-.2,z),w*.6,.7,'Ivory',8)
            torus((x,y+h/2+2,z),1.45,.16,'Brass',axis='z')
            sphere((x,y+h/2+2,z),(.5,.5,.5),'StarGlow')
        elif kind == 'cover':
            box(obs['center'],obs['size'],'Wood' if index%2 else 'TealDeep',.14)
            for fraction in [-.31,.31]:
                box((x+w*fraction,y,z),(w*.055,h*.99,d*.99),'WoodDark',.045)
            for sign in [-1,1]:
                box((x,y,z+sign*(d/2-.018)),(w*.5,h*.12,.045),'WoodLight',.01)
                for dx in [-.39,.39]:
                    box((x+w*dx,y+h*.2,z+sign*(d/2-.04)),(w*.065,h*.46,.06),'Brass',.02)
            box((x,y+h/2-.01,z),(w*.9,.02,d*.8),'WoodLight',.01)
            for i in range(3):
                box((x,y+h/2+.005,z+d*(i-1)*.18),(w*.85,.012,.028),'WoodDark',.004)
        else:
            box(obs['center'],obs['size'],'Limestone' if index%3 else 'WoodLight',.22)
            if index%3==0: box((x,y,z-d/2-.04),(w*.8,h*.1,.12),'WoodDark')
    dock_landmark(islands); area_landmarks(islands); bridge_details(layout['bridges'])
    make_paths(islands,layout['bridges']); village_life(layout,islands); cliff_dressing(islands)
    landscape_scatter(islands,layout['obstacles']); world_clouds()
    # Offline decorative geometry shares the same palette, regional mesh groups and layout.
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    import sky_island_dressing
    dressing=sky_island_dressing.build(sys.modules[__name__],layout)
    dressing['settlement']=sky_island_settlement.finish_gardens(sys.modules[__name__],layout,settlement_records)
    (source/'sky_island_dressing.json').write_text(json.dumps(dressing,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    # Tripo3D 英雄道具：纯装饰，避让复用 dressing 的 PlantingSpace，不加导航顶点。
    # tripo 目录为空时整段静默跳过，因此没有模型也不影响世界生成。
    import sky_island_tripo_props
    tripo=sky_island_tripo_props.build(sys.modules[__name__],layout,source/'tripo',sky_island_dressing)
    (source/'sky_island_tripo.json').write_text(json.dumps(tripo,indent=2,ensure_ascii=False),encoding='utf-8')
    for m in layout['markers']:
        marker(m['id'],m['position'])
        if m['kind'].lower()=='lamp':
            CURRENT=m.get('island','Lamps'); lantern(*m['position'])
    marker('POI_B_Mural',[-23,8.25,-100])
    stats={}
    for (region,mat),data in GROUPS.items():
        obj=create_object('VIS_'+region+'_'+mat,data['v'],data['f'],mat,data['uv'],data['smooth'])
        stats[obj.name]={'vertices':len(data['v']),'triangles':sum(len(f)-2 for f in data['f'])}
    scene=bpy.context.scene; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    bpy.ops.object.select_all(action='DESELECT')
    for obj in scene.objects: obj.select_set(obj.type in {'MESH','EMPTY'})
    fbx=assets/'SkyIslandWorld.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,path_mode='RELATIVE')
    metadata={'coordinateSystem':'Unity XYZ metres','materials':{'Sky_'+name:{'rgba':TILED_TEXTURES[name][2] if name in TILED_TEXTURES else rgba(color),'texture':(MODEL_TEXTURES[name].replace(chr(92),'/') if name in MODEL_TEXTURES else 'Textures/sky_mural.png' if name=='Mural' else 'Textures/sky_cloth.png' if name=='Cloth' else 'Textures/'+TILED_TEXTURES[name][0] if name in TILED_TEXTURES else None),'emission':EMISSION.get(name,0)} for name,color in PALETTE.items()},
              'markers':[{'name':m['id'],'position':m['position']} for m in layout['markers']]+[{'name':'POI_B_Mural','position':[-23,8.25,-100]}],
              'visualMeshes':stats,'totalVisualTriangles':sum(s['triangles'] for s in stats.values()),'collisionBoxes':COLLISIONS,
              'navVertices':len(layout['navigation']['vertices']),'navTriangles':len(layout['navigation']['triangles']),
              'sourceLayout':str(assets/'sky_island_layout.json'),'textures':['Textures/sky_mural.png','Textures/sky_cloth.png']+['Textures/'+n for n in sorted(set(v[0] for v in TILED_TEXTURES.values()))]}
    (source/'sky_island_geometry.json').write_text(json.dumps(metadata,indent=2,ensure_ascii=False),encoding='utf-8')
    # Authoring light/cameras exist only in the .blend, not the exported FBX.
    scene.world.use_nodes=True; world=scene.world.node_tree.nodes.get('Background'); world.inputs['Color'].default_value=(.30,.53,.69,1); world.inputs['Strength'].default_value=.62
    bpy.ops.object.light_add(type='SUN',location=(-400,-500,700)); sun=bpy.context.object; sun.name='Author_Sun'
    sun.rotation_euler=(math.radians(28),math.radians(-25),math.radians(-28)); sun.data.energy=2.5; sun.data.angle=math.radians(12); sun.data.color=(1,.82,.63)
    bpy.ops.object.camera_add(); camera=bpy.context.object; camera.name='Author_Camera'; scene.camera=camera
    camera.data.type='ORTHO'; camera.data.clip_end=6000
    scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.use_denoising=True
    scene.render.resolution_percentage=100; scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='AgX'; scene.render.film_transparent=False
    views=[('sky_island_panorama',(870,1050,-1260),(0,3,35),1330,2160,1728),
           ('sky_island_village',(126,150,-306),(0,16,-134),198,2000,1500),
           ('sky_island_bell',(108,175,146),(0,91,327),152,1800,1600),
           ('sky_island_temple',(360,132,-202),(237,32,-40),190,1800,1400)]
    for name,pos,target,scale,w,h in views:
        camera.location=(pos[0],pos[2],pos[1]); target=Vector((target[0],target[2],target[1])); camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=scale; scene.render.resolution_x=w; scene.render.resolution_y=h; scene.render.filepath=str(source/(name+'.png'))
        if name=='sky_island_village': bpy.ops.wm.save_as_mainfile(filepath=str(source/'SkyIslandWorld.blend'))
        if not args.skip_render: bpy.ops.render.render(write_still=True)
    print('SKY_ISLAND_MODEL_OK '+json.dumps({'fbx':str(fbx),'triangles':metadata['totalVisualTriangles'],'visualMeshes':len(stats),'navVertices':metadata['navVertices']}))


if __name__=='__main__': main()
