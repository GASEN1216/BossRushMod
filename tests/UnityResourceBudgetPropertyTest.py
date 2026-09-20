"""实际交付判据的离线反例：压缩、网格预算、导航、纹理格式与发布清单。"""
from pathlib import Path
import copy
import json
import sys

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
from verify_unity_resource_release import validate


def main():
    row={'compression':[0,2,3],'textures':[{'name':'icon','format':'DXT5','width':256,'height':256,'readable':False}],
         'meshes':[{'name':'EnergyShield_Optimized','triangles':40000,'vertices':20007,'readable':False}]}
    assert not validate(row,'energyshield_totem_model')
    for field,value in [('compression',[1]),('textures',[dict(row['textures'][0],format='DXT5Crunched')]),
                        ('textures',[dict(row['textures'][0],readable=True)]),
                        ('meshes',[dict(row['meshes'][0],triangles=40001)])]:
        bad=copy.deepcopy(row);bad[field]=value
        assert validate(bad,'energyshield_totem_model'),(field,value)
    modeg=copy.deepcopy(row)
    modeg['bytes']=256*1024
    modeg['textures']=[dict(row['textures'][0],name='modeg_echo_emblem',width=256,height=256),
                       dict(row['textures'][0],name='modeg_echo_banner',width=1024,height=576)]
    assert not validate(modeg,'modeg_presentation')
    bad=copy.deepcopy(modeg);bad['bytes']+=1
    assert validate(bad,'modeg_presentation'), 'Mode G disk budget must fail at the boundary'
    bad=copy.deepcopy(modeg);bad['textures'][0].update(width=512,height=512)
    assert validate(bad,'modeg_presentation'), 'Mode G builder dimension contract must not drift'
    sky=copy.deepcopy(row)
    sky['textures']=[dict(row['textures'][0],name=name,format='BC7') for name in
        ['geology_handpainted_native','wood_handpainted_vanilla','sky_mural','stone_handpainted_vanilla',
         'sky_cloth','grass_handpainted_vanilla','roof_handpainted_terracotta']]
    sky['meshes']=[{'name':'SkyIslandNavigation','triangles':1,'vertices':4095,'readable':True}]
    assert not validate(sky,'sky_island_raid')
    for changes in [{'vertices':4096},{'vertices':0},{'readable':False},{'name':'NavigationGone'}]:
        bad=copy.deepcopy(sky);bad['meshes'][0].update(changes)
        assert validate(bad,'sky_island_raid'),changes
    for fmt in ['RGB24','RGBA32','DXT1']:
        bad=copy.deepcopy(sky);bad['textures'][0]['format']=fmt
        assert validate(bad,'sky_island_raid'),fmt
    bad=copy.deepcopy(sky);bad['meshes'].append({'name':'VIS_B_Leaf','vertices':65536,'triangles':1,'readable':True})
    assert validate(bad,'sky_island_raid')
    for name,texture in [('birthday_cake','BirthdayCake'),('bossrush_ticket','BossRushTicket2')]:
        icon=copy.deepcopy(row)
        icon['textures']=[dict(row['textures'][0],name=texture,format='BC7')]
        icon['sprites']=[{'texture':texture,'rectWidth':256,'rectHeight':256,'pixelsPerUnit':25}]
        assert not validate(icon,name)
        bad=copy.deepcopy(icon);bad['sprites'][0]['pixelsPerUnit']=12.5
        assert validate(bad,name), 'Resizing must preserve Sprite display size'
        for field,value in [('width',1024),('height',1024),('format','RGBA32'),('readable',True)]:
            bad=copy.deepcopy(icon);bad['textures'][0][field]=value
            assert validate(bad,name),(name,field,value)
        bad=copy.deepcopy(icon);bad['sprites']=[];assert validate(bad,name)
    manifest=json.loads((ROOT/'tools/resource_release_manifest.json').read_text(encoding='utf8'))
    paths=manifest['bundles']
    assert len(paths)==len(set(paths))
    assert 'Assets/arenas/sky_island_world' not in paths
    for path in paths:
        assert path.startswith('Assets/') and '..' not in path and Path(path).suffix==''
    assert {'Assets/boss/dragonking','Assets/npcs/goblinnpc','Assets/npcs/nursenpc',
        'Assets/ui/bossrush_wiki','Assets/ui/love_heart'} <= set(paths)
    print('UnityResourceBudgetPropertyTest: PASS')


if __name__=='__main__':
    main()
