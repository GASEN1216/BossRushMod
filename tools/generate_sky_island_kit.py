"""COMPAT / OPERATIONAL：从晴岚群岛造型库导出可独立复用的模型资产。

Blender 后台运行；导入 generate_sky_island 的造型、材质和 UV 函数，不执行其 main。
仅写作者工程 Assets/SkyIsland/Models/ 与 ArtSource/SkyIsland/SkyIsland_ModelKit.blend，
不修改整图 FBX、整图 blend、运行时代码或现有目录内容。FBX 为纯视觉模块，无推断碰撞。
"""

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

sys.path.insert(0,str(Path(__file__).resolve().parent))
import generate_sky_island as geometry
import sky_island_life_models as life_models


def make_chime():
    for x in (-2.35,2.35):
        geometry.cylinder((x,2.2,0),0.16,4.4,"Wood",10)
        geometry.cylinder((x,0.15,0),0.45,0.3,"Limestone",12)
        geometry.sphere((x,4.53,0),(0.24,0.33,0.24),"Brass",10,5)
    geometry.beam((-2.6,4.3,0),(2.6,4.3,0),0.16,"WoodDark",10)
    for index,x in enumerate((-1.65,-0.82,0,0.82,1.65)):
        bell_y=2.65+0.3*math.cos(index*1.2)
        geometry.beam((x,4.25,0),(x,bell_y+0.75,0),0.035,"Brass",6)
        geometry.bell(x,bell_y,0,0.14)


def make_bell():
    geometry.box((0,0.2,0),(7,0.4,3.1),"Chalk",0.12)
    for x in (-2.65,2.65):
        geometry.cylinder((x,3.6,0),0.28,6.4,"Wood",12)
        geometry.box((x,0.65,0),(1.1,0.5,1.1),"Limestone",0.1)
        geometry.sphere((x,7.03,0),(0.36,0.43,0.36),"Brass",12,6)
    geometry.beam((-3.1,6.75,0),(3.1,6.75,0),0.32,"WoodDark",12)
    geometry.beam((0,6.7,0),(0,5.9,0),0.12,"Brass",8)
    geometry.bell(0,1.65,0,0.85)


def specs():
    base = [
        ("TeaHouse","风铃茶铺","house(obs, 0)",lambda:geometry.house({"center":[0,5,0],"size":[16,10,12]},0)),
        ("House","群岛小屋","house(obs, 1)",lambda:geometry.house({"center":[0,4,0],"size":[12,8,10]},1)),
        ("Lantern","黄铜路灯","lantern(0, 0, 0)",lambda:geometry.lantern(0,0,0)),
        ("Planter","陶盆绿植","pot(0, 0, 0)",lambda:geometry.pot(0,0,0)),
        ("Bench","木制长椅","bench(0, 0, 0)",lambda:geometry.bench(0,0,0)),
        ("Barrel","箍带木桶","barrel(0, 0, 0)",lambda:geometry.barrel(0,0,0)),
        ("WindChime","风铃架","make_chime → cylinder / beam / bell",make_chime),
        ("HomecomingBell","小归航钟","make_bell → box / cylinder / beam / bell",make_bell),
        ("DuckStatue","鸭子雕像","duck_statue(0, 0, 0)",lambda:geometry.duck_statue(0,0,0)),
        ("Tree","群岛阔叶树","tree(0, 0, 0)",lambda:geometry.tree(0,0,0)),
    ]
    return base + [(key, label, 'sky_island_life_models.build_model: '+key,
                    lambda key=key:life_models.build_model(geometry,key))
                   for key,label in life_models.MODEL_SPECS]


def material_metadata():
    materials={}
    for name,color in geometry.PALETTE.items():
        tiled=geometry.TILED_TEXTURES.get(name)
        texture=("Textures/sky_mural.png" if name=="Mural" else "Textures/sky_cloth.png" if name=="Cloth"
                 else "Textures/"+tiled[0] if tiled else None)
        materials["Sky_"+name]={"rgba":tiled[2] if tiled else geometry.rgba(color),"texture":texture,
                                "emission":1.2 if name in ("Glow","StarGlow") else 0}
    return materials


def extract_module(name,label,function,build,folder,collection_index):
    geometry.GROUPS.clear()
    geometry.CURRENT=name
    build()
    if not geometry.GROUPS:
        raise ValueError("空模型: "+name)
    minimum_y=min(p[1] for data in geometry.GROUPS.values() for p in data["v"])
    for data in geometry.GROUPS.values():
        data["v"]=[(p[0],p[1]-minimum_y,p[2]) for p in data["v"]]
    points=[p for data in geometry.GROUPS.values() for p in data["v"]]
    if not all(math.isfinite(value) for p in points for value in p):
        raise ValueError("非有限模型顶点: "+name)
    minimum=[min(p[i] for p in points) for i in range(3)]
    maximum=[max(p[i] for p in points) for i in range(3)]
    size=[maximum[i]-minimum[i] for i in range(3)]
    if min(size)<=0:
        raise ValueError("无体积模型: "+name)
    collection=bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    root=bpy.data.objects.new(name,None)
    collection.objects.link(root)
    root.empty_display_type='PLAIN_AXES'
    root.empty_display_size=1
    root["module_label"]=label
    root["source_function"]=function
    root["coordinate_system"]="Unity XYZ metres; Blender uses Z up"
    objects=[root]
    triangle_count=0
    materials=set()
    mesh_records=[]
    for (group,material),data in geometry.GROUPS.items():
        obj=geometry.create_object("VIS_"+group+"_"+material,data["v"],data["f"],material,data["uv"],data["smooth"])
        for old_collection in list(obj.users_collection):
            old_collection.objects.unlink(obj)
        collection.objects.link(obj)
        obj.parent=root
        obj.data.calc_loop_triangles()
        count=len(obj.data.loop_triangles)
        triangle_count+=count
        objects.append(obj)
        materials.add("Sky_"+material)
        if material in geometry.TILED_TEXTURES and not obj.data.uv_layers:
            raise ValueError("缺失贴图 UV: "+obj.name)
        mesh_records.append({"name":obj.name,"vertices":len(obj.data.vertices),"triangles":count,"material":"Sky_"+material})
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    path=folder/(name+".fbx")
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH','EMPTY'},
                           axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,path_mode='RELATIVE')
    if not path.is_file() or path.stat().st_size<1000:
        raise ValueError("FBX 导出为空: "+str(path))
    # 每份 FBX 已在局部原点导出；组合源只平移根，网格仍保持可独立导出的局部坐标。
    grid=((collection_index%5)*30,(collection_index//5)*30,0)
    root.location=grid
    return {"name":name,"label":label,"fbx":"Assets/SkyIsland/Models/"+path.name,
            "bounds":{"min":minimum,"max":maximum,"size":size},"triangles":triangle_count,
            "vertices":sum(m["vertices"] for m in mesh_records),"meshCount":len(mesh_records),
            "materials":sorted(materials),"sourceFunction":function,"groundShiftApplied":-minimum_y,
            "origin":"Construction origin on ground, local minimum Y = 0",
            "kitLocationBlenderXYZ":list(grid),"colliders":[],"meshes":mesh_records,"bytes":path.stat().st_size}


def preview_and_save(project,folder,blend_path,render):
    scene=bpy.context.scene
    scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    scene.world.use_nodes=True
    background=scene.world.node_tree.nodes.get('Background')
    background.inputs['Color'].default_value=(0.56,0.66,0.68,1)
    background.inputs['Strength'].default_value=0.55
    bpy.ops.object.light_add(type='SUN',location=(-40,-70,100))
    sun=bpy.context.object; sun.name='KitPreview_Sun'
    sun.rotation_euler=(math.radians(27),math.radians(-25),math.radians(-28))
    sun.data.energy=2.7; sun.data.angle=math.radians(16)
    bpy.ops.object.camera_add(location=(105,-108,110))
    camera=bpy.context.object; camera.name='KitPreview_Camera'; scene.camera=camera
    rows=math.ceil(len(specs())/5)
    target=Vector((59,(rows-1)*15,3))
    camera.location=target+Vector((65,-115,115))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO'; camera.data.ortho_scale=max(158,rows*33); camera.data.clip_end=1000
    scene.render.engine='CYCLES'; scene.cycles.samples=24; scene.cycles.use_denoising=True
    scene.render.resolution_x=2300; scene.render.resolution_y=1400; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
    scene.view_settings.view_transform='AgX'
    scene.render.filepath=str(folder/'SkyIsland_ModelKit_Preview.png')
    # 材质加载器已逐张 pack；此处核验使用中的图片均带独立可编辑源所需数据。
    packed=[]
    for image in bpy.data.images:
        if image.packed_file is not None:
            packed.append(image.name)
        elif image.source=='FILE':
            image.reload()
            image.pack()
            if image.packed_file is not None:
                packed.append(image.name)
    if not packed:
        raise ValueError("模型组合源没有打包真实贴图")
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    if render:
        bpy.ops.render.render(write_still=True)
        render_life_sheet(folder)
    return packed


def render_life_sheet(folder):
    """A closer inspection sheet for the newly authored props, using real model meshes."""
    scene=bpy.context.scene
    keys={'SkyIsland_'+key for key,label in life_models.MODEL_SPECS}
    roots=[bpy.data.objects[key] for key in keys]
    old_positions={root.name:root.location.copy() for root in roots}
    visibility={obj.name:obj.hide_render for obj in bpy.data.objects}
    for obj in bpy.data.objects:
        if obj.type=='MESH':
            obj.hide_render=obj.parent is None or obj.parent.name not in keys
    ordered=[bpy.data.objects['SkyIsland_'+key] for key,label in life_models.MODEL_SPECS]
    temporary=[]
    try:
        for index,root in enumerate(ordered):
            x=(index%4)*13; y=(index//4)*13
            root.location=(x,y,0)
            bpy.ops.object.text_add(location=(x-3.7,y-4.6,.035))
            label=bpy.context.object
            label.data.body=root.name.replace('SkyIsland_','')
            label.data.size=.52
            label.data.materials.append(geometry.MATERIALS['WoodDark'])
            temporary.append(label)
        bpy.ops.mesh.primitive_plane_add(size=200,location=(20,20,-.025))
        ground=bpy.context.object
        ground.data.materials.append(geometry.MATERIALS['Cloud'])
        temporary.append(ground)
        rows=math.ceil(len(ordered)/4)
        target=Vector((19.5,(rows-1)*6.5,0))
        camera=scene.camera
        camera.location=target+Vector((21,-49,68))
        camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.ortho_scale=max(68,rows*17)
        scene.render.resolution_x=2200; scene.render.resolution_y=1900
        scene.render.filepath=str(folder/'SkyIsland_LifeKit_Preview.png')
        bpy.ops.render.render(write_still=True)
    finally:
        for obj in temporary:
            bpy.data.objects.remove(obj,do_unlink=True)
        for root in roots:
            root.location=old_positions[root.name]
        for name,hidden in visibility.items():
            bpy.data.objects[name].hide_render=hidden


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project',required=True)
    parser.add_argument('--skip-render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    project=Path(args.project).resolve()
    if not (project/'ProjectSettings/ProjectVersion.txt').is_file():
        raise ValueError("必须指定现有 Unity 作者工程")
    assets=project/'Assets/SkyIsland'
    folder=assets/'Models'
    blend_path=project/'ArtSource/SkyIsland/SkyIsland_ModelKit.blend'
    folder.mkdir(parents=True,exist_ok=True)
    blend_path.parent.mkdir(parents=True,exist_ok=True)
    # 只清空本次后台进程的内存场景，不清文件或目录。
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj,do_unlink=True)
    geometry.MATERIALS.clear()
    geometry.build_materials(assets)
    models=[]
    for index,(key,label,function,build) in enumerate(specs()):
        models.append(extract_module('SkyIsland_'+key,label,function,build,folder,index))
    packed=preview_and_save(project,folder,blend_path,not args.skip_render)
    manifest={"schemaVersion":1,"classification":["COMPAT","OPERATIONAL"],
              "coordinateSystem":"Unity XYZ metres; FBX axis_forward=-Z, axis_up=Y",
              "sourceScript":str(Path(__file__).resolve()),
              "geometryLibrary":"tools/generate_sky_island.py",
              "materialRoot":"Assets/SkyIsland","materials":material_metadata(),
              "blend":"ArtSource/SkyIsland/SkyIsland_ModelKit.blend",
              "preview":"Assets/SkyIsland/Models/SkyIsland_ModelKit_Preview.png",
              "models":models,"modelCount":len(models),
              "totalTriangles":sum(m['triangles'] for m in models),"packedImages":packed,
              "usage":"纯视觉模块。Unity材质复用同名Sky_*与此manifest真实贴图；不从树冠/风铃自动推断整盒碰撞。",
              "validation":{"finiteGeometry":True,"groundOrigins":True,"texturedUVs":True,
                            "nonEmptyIndependentFbx":True,"packedEditableTextures":True,
                            "unityImport":"Pending independent importer verification"}}
    (folder/'sky_island_model_kit.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    (folder/'README.md').write_text('# 晴岚群岛可复用模型库\n\n分类：COMPAT / OPERATIONAL。\n\n'
        +str(len(models))+' 个纯视觉 FBX 均以米为单位、底部原点导出，完整模型清单、尺寸、三角数与材质见 `sky_island_model_kit.json`。'
        '作者工程 `ArtSource/SkyIsland/SkyIsland_ModelKit.blend` 按 30 米网格排布，各模型独立 Collection/根节点，真实贴图已打包。'
        '组合源含预览相机/日光，单体 FBX 不含相机、灯、碰撞或运行时脚本。\n\n'
        '模型使用与整图一致的 `Sky_*` 材质及平铺 UV。Unity 导入应按 manifest 复用 `Assets/SkyIsland/Materials/` 中同名材质；'
        '不要依赖 FBX 对 Blender 混色节点的自动翻译来恢复真实贴图。\n',encoding='utf-8')
    print('SKY_ISLAND_KIT_PASS '+json.dumps({"models":len(models),"triangles":manifest['totalTriangles'],
                                         "blend":str(blend_path),"manifest":str(folder/'sky_island_model_kit.json')},ensure_ascii=False))


if __name__=='__main__':
    main()
