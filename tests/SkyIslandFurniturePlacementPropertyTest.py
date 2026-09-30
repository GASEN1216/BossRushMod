"""Execute author-time prop placement and the real bench emitter; no Unity substitute.
Independent contact/orientation witnesses and old-source counterexample are kept in memory.
"""
import ast
import copy
import hashlib
import math
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
from sky_island_prop_placement import PlacementSpace


def space(outline=None):
    island={'id':'B','height':5,'outline':outline or [[-40,-40],[40,-40],[40,40],[-40,40]]}
    layout={'islands':[island],'obstacles':[{'id':'house','island':'B','bounds':[-12,8,-5,15]}],
            'markers':[{'id':'spawn','island':'B','kind':'spawn','position':[25,5,25]}]}
    return PlacementSpace(SimpleNamespace(PAVING_TRACKS=[([(-35,0),(35,0)],6)]),layout,{})


def bench_check(source):
    tree=ast.parse(source)
    method=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='bench')
    calls=[];namespace={'math':math,'box':lambda center,size,*args:calls.append((center,size))}
    exec(compile(ast.Module(body=[method],type_ignores=[]),'production bench','exec'),namespace)
    namespace['bench'](10,5,20,math.pi/2)
    back=calls[-1][0]
    assert abs(back[0]-10.52)<1e-8 and abs(back[2]-20)<1e-8,'BENCH_BACKREST_ROTATION'
    # Seats and legs rotate together; wrong old world-Z backrest cannot form a bench.
    assert all(abs(c[2]-20)<=1.3000001 for c,_ in calls),'BENCH_PARTS_SEPARATED'


def run_landmarks(source,replaced,tops=None):
    import json
    import sky_island_frame
    layout=json.loads((ROOT/'ArtSource/SkyIsland/layout.json').read_text(encoding='utf-8'))
    sky_island_frame.bind_layout(layout)
    node=next(n for n in ast.parse(source).body if isinstance(n,ast.FunctionDef) and n.name=='area_landmarks')
    fitted_names=[]
    def fitted(name,emit,*args,**kwargs):
        fitted_names.append(name);emit();return (0.0,0.0)
    namespace={'math':math,'TAU':math.tau,'sky_island_frame':sky_island_frame,'TRIPO_PROPS':None,
               'REPLACED_OBSTACLES':set(replaced),'DUPLICATE_EMISSIONS_SKIPPED':set(),'CURRENT':'B',
               'fitted':fitted,'LAYOUT_OBSTACLES':layout['obstacles'],'REGISTERED_TOP':dict(tops or {}),
               'MURAL_SHIFT':(0.0,0.0)}
    operations=['paved_disc','cylinder','sphere','beam','bell','textured_quad','ribbon','addmesh','box','torus','tree','lathe','lantern','duck_statue',
                'lamp_light']
    calls=[]
    for name in operations:namespace[name]=lambda *args,_name=name,**kwargs:calls.append((_name,args))
    # Cultivation data stays production-owned while geometry primitives are host stubs.
    farm=next(n for n in ast.parse(source).body if isinstance(n,ast.FunctionDef) and n.name=='farm_beds')
    exec(compile(ast.Module(body=[farm,node],type_ignores=[]),'production landmark emitters','exec'),namespace)
    islands={i['id']:i for i in layout['islands']}
    namespace['area_landmarks'](islands)
    return namespace,calls,fitted_names,islands


def landmark_owner_check(source):
    replaced={'B_ChimeSupport01','B_ChimeSupport02','D_WindBeacon','G_BrokenAstrolabe'}|{'F_PavilionSupport%02d'%i for i in range(1,9)}
    namespace,calls,fitted_names,islands=run_landmarks(source,replaced)
    # 2026-09-30：两根风铃支柱都换成 Tripo 风铃架时，悬在架顶上方的横梁与小钟一并跳过；S1 登记池已画水面，圆水面跳过。
    expected=replaced|{'B_ChimeCrossbeam','S1_PondDisc'}
    assert namespace['DUPLICATE_EMISSIONS_SKIPPED']==expected,'DUPLICATE_LANDMARK_OWNER'
    assert any(name=='paved_disc' for name,_ in calls) and any(name=='bell' for name,_ in calls),'LANDMARK_FEATURES_REMOVED'
    # 按坐标画的小件全部走统一摆放裁决（台账里曾压路的桌凳、石灯、壁画、彩旗、邮亭、洞口灯、池边石）。
    for name in ('village_table','stone_lantern','mural','bunting','post_kiosk','cave_lantern','pond_rock','root_forest_tree'):
        assert name in fitted_names,'LANDMARK_NOT_FITTED:'+name
    namespace,_,_,_=run_landmarks(source,replaced|{'S3_RainCave','S4_StarLookout'})
    assert {'S3_CaveSpheres','S4_Telescope'}<=namespace['DUPLICATE_EMISSIONS_SKIPPED'],'REPLACED_LANDMARK_DUPLICATED'
    # 悬空横梁与亭顶落到实测柱顶：换一组柱顶，梁 / 亭顶必须跟着走。
    y_e=islands['E']['height'];y_f=islands['F']['height']
    tops={'E_WindPillarWest':y_e+5.49,'E_WindPillarEast':y_e+5.49}
    tops.update({'F_PavilionSupport%02d'%i:y_f+4.5 for i in range(1,9)})
    _,calls,_,_=run_landmarks(source,replaced,tops)
    beams=[args for name,args in calls if name=='beam' and abs(args[0][1]-args[1][1])<1e-9 and args[2]==.34]
    assert beams and abs(beams[0][0][1]-(y_e+5.49-.25))<1e-6,'E_BELL_BEAM_NOT_ON_PILLARS'
    bells=[args for name,args in calls if name=='bell' and abs(args[2]-beams[0][0][2])<1e-6]
    # 钟舌垂到钟口下 1.4×scale：最低点也要高过岛面 2.75 m。
    assert len(bells)==9 and all(args[1]-1.4*args[3]>=y_e+2.75-1e-6 for args in bells),'E_BELLS_BLOCK_HEADROOM'
    roofs=[args for name,args in calls if name=='lathe' and args[2]=='Teal' and args[1][0]==(0,8)]
    assert len(roofs)==2 and all(abs(args[0][1]-(y_f+4.45))<1e-6 for args in roofs),'F_ROOF_NOT_ON_PILLARS'


def main():
    watched=[ROOT/'tools/generate_sky_island.py',ROOT/'tools/sky_island_prop_placement.py',Path(__file__)]
    before={p:hashlib.sha256(p.read_bytes()).hexdigest() for p in watched}
    s=space()
    # The center is outside the road but a deep seat corner overlaps its 3m edge.
    assert not s.free('B',[7,2.5,11,5]),'CORNER_INTRUSION_ACCEPTED'
    x,z,yaw=s.place('B','seat',[-2,-1.25,2,1.25],9,3,0,'roadside')
    row=s.records[-1];assert row['bounds'][1]>=3.35-1e-6 or row['bounds'][3]<=-3.35+1e-6,'ROAD_NOT_CLEAR'
    near=s.nearest_road('B',x,z)[1];dx,dz=near[0]-x,near[1]-z
    facing=(-math.sin(math.radians(yaw)),-math.cos(math.radians(yaw)))
    assert (facing[0]*dx+facing[1]*dz)/math.hypot(dx,dz)>.99999,'SEAT_NOT_FACING_ROAD'
    s.place('B','second seat',[-2,-1.25,2,1.25],x,z,yaw,'roadside')
    a,b=(r['bounds'] for r in s.records)
    assert a[2]+.45<=b[0]+1e-6 or b[2]+.45<=a[0]+1e-6 or a[3]+.45<=b[1]+1e-6 or b[3]+.45<=a[1]+1e-6,'PROPS_INTERSECT'
    assert not s.free('B',[-13,9,-8,12]),'HOUSE_NOT_PROTECTED'
    assert not s.free('B',[24,24,26,26]),'SPAWN_NOT_PROTECTED'
    concave=space([[-40,-40],[40,-40],[40,40],[5,40],[5,5],[-5,5],[-5,40],[-40,40]])
    assert not concave.free('B',[-8,9,8,18]),'CONCAVE_BOUNDARY_CROSSED'
    s=space();g=SimpleNamespace(GROUPS={},PAVING_TRACKS=list(s.tracks))
    def emit():g.GROUPS['wood']={'v':[(7,5,2.5),(11,5,2.5),(11,6,5),(7,6,5)],'f':[(0,1,2,3)],'uv':[(0,0)]*4}
    tracks=copy.deepcopy(g.PAVING_TRACKS);s.capture(g,'B','table',9,3.75,emit)
    vertices=g.GROUPS['wood']['v'];record=s.records[0]
    actual=[min(v[0] for v in vertices),min(v[2] for v in vertices),max(v[0] for v in vertices),max(v[2] for v in vertices)]
    assert all(abs(a-b)<1e-8 for a,b in zip(actual,record['bounds'])),'CAPTURE_TRANSFORM_MISMATCH'
    assert g.GROUPS['wood']['f']==[(0,1,2,3)] and g.GROUPS['wood']['uv']==[(0,0)]*4 and g.PAVING_TRACKS==tracks,'CAPTURE_CHANGED_TOPOLOGY_OR_ROADS'
    source=(ROOT/'tools/generate_sky_island.py').read_text(encoding='utf-8');bench_check(source);landmark_owner_check(source)
    broken=source.replace('x+.52*math.sin(yaw),y+1.25,z+.52*math.cos(yaw)','x,y+1.25,z+.52',1)
    assert broken!=source
    try:bench_check(broken)
    except AssertionError as error:assert str(error)=='BENCH_BACKREST_ROTATION'
    else:raise AssertionError('OLD_BACKREST_MUTATION_ACCEPTED')
    # Force the production placement decision to ignore clearance; the independently
    # measured seat-road contact must reject that candidate.
    s=space();s.free=lambda *args:True
    s.place('B','bad seat',[-2,-1.25,2,1.25],9,3,0,'roadside')
    b=s.records[0]['bounds']
    try:assert b[1]>=3.35-1e-6 or b[3]<=-3.35+1e-6,'ROAD_NOT_CLEAR'
    except AssertionError as error:assert str(error)=='ROAD_NOT_CLEAR'
    else:raise AssertionError('BYPASSED_CLEARANCE_MUTATION_ACCEPTED')
    for p,digest in before.items():assert hashlib.sha256(p.read_bytes()).hexdigest()==digest,'SOURCE_BYTES_CHANGED'
    print('PASS furniture: rotated bench, corner/road clearance, facing, inter-prop separation, house/spawn, concave boundary, exact emitted translation; 2 negative witnesses; source bytes unchanged')


if __name__=='__main__':main()
