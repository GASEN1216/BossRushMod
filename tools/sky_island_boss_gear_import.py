"""天空岛头目 / 岛主的专属装备：把 Tripo3D 回收的 GLB 规范化成作者工程能直接做装备预制体的 FBX + 贴图。

与 tools/sky_island_tripo_import.py 的区别：场景件导出 JSON 网格、并进世界 FBX；装备要单独成预制体、
挂到官方角色的头盔 / 护甲 / 背包挂点上，所以逐件导出独立 FBX。

导入基线参考既有装备与官方挂点（2026-09-20 校准口径见 docs/guides/头盔佩戴与装备尺寸校准.md）：
- 导出为 Y-up、Z-forward 的标准化 FBX；头盔在这里按整体包围盒居中只作为导入基线。
  佩戴时的盔壳中心由 helmet_fit_profiles.json 单独校准，Editor 在模型子节点绝对赋值，不能在 FBX 再烤一次偏移。
- 逐件标准化尺寸取 PIECES；它不是最终佩戴尺寸，也不是所有头盔/护甲通用的规格。
- 背包的数取官方背包，见 PIECES 注释。

导入、减面、展 UV、导出贴图全部复用 sky_island_tripo_import 的函数，不另写一份。
Tripo3D 的朝向不固定：`--yaw 件名=度` 绕竖直轴转到「正面朝 Blender -Y」（FBX 按 axis_forward='-Z' 导出后即 Unity +Z）；
`--box 件名=宽,高,深` 覆盖目标包围盒；`--source 件名=文件名` 对上 Tripo 网页下载时它自己起的文件名（不改 owner 的文件）。
`--back-quantile 件名=q`（只对背包）：贴背面取顶点前后深度的 q 分位点而不是最前沿。Tripo 的背包常自带一圈背带环，
按最前沿贴背会把包身推到身后很远；q 取背带段占的顶点比例（约 0.15–0.2），默认 0 即最前沿，R1 四件不受影响。

用法（Blender 后台模式抛异常默认仍退出 0：必须同时查退出码与最后一行的 PASS 标记）：
    blender -b --factory-startup --python-exit-code 1 \
        --python tools/sky_island_boss_gear_import.py -- \
        --glb-dir ArtSource/SkyIsland/BossGear \
        --source starworks_foreman_helmet=steampunk+diving+helmet+3d+model.glb --yaw starworks_foreman_helmet=-90 [...]
成功时最后一行打印 SKY_ISLAND_BOSS_GEAR_IMPORT_OK。GLB 与清单都在 ArtSource/SkyIsland/BossGear/（local-only，不进 git）。
作者工程默认由 unity_project_path.py 解析，BOSSRUSH_UNITY_PROJECT 优先；也可显式传 --project。
"""

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix

sys.path.insert(0, str(Path(__file__).resolve().parent))
import sky_island_tripo_import as tripo  # noqa: E402
from helmet_fit import load_profiles, validate_staging_bounds  # noqa: E402
from unity_project_path import find_unity_project  # noqa: E402

# 件名 -> (装备 bundle 基名, 槽位, 目标包围盒 (宽, 高, 深) 米, 是否按轴拉伸, 三角面预算, 贴图上限)
# 不拉伸时只按宽度等比缩放（头盔先保证套得住头），高、深跟着模型走，清单里记实际值。
# 基名与 DebugAndTools/SkyIsland/SkyIslandBossRules.cs 的 GearSpecs.ModelBaseName 一一对应，发布后不改。
# 三角面预算按 Tripo 网页「面数上限约 6000」给：回收件本来就在预算内时不减面（减面会把贴图接缝拉花）。
# 装备材质贴图上限 512；立绘的 1024 例外不适用于随身装备贴图。
PIECES = {
    'starworks_foreman_helmet':   ('StarbrassVisor_Helmet',    'Helmat',   (0.88, 1.06, 1.04), False, 6500, 512),
    'starworks_foreman_armor':    ('StarfurnaceHarness_Armor', 'Armor',    (1.02, 0.62, 0.66), True,  6500, 512),
    # 官方背包（IG_Backpack_LV1 / LV4）：根节点单位变换，网格子节点偏到挂点身后——
    # 包体中心 (0, +0.01~+0.04, -0.22~-0.28)，宽 0.44、高 0.38~0.68、深 0.30~0.39，贴背那一面在 z≈-0.08。
    # 星炉背囊是 Boss 的大件，宽给到 0.50；贴背一面放在 z=-0.08、包体中心抬到 +0.03（见 BEHIND_SOCKET）。
    'starworks_foreman_backpack': ('StarfurnacePack_Backpack', 'Backpack', (0.50, 0.50, 0.35), False, 6500, 512),
    'lookout_stargazer_helmet':   ('StargazerLens_Helmet',     'Helmat',   (0.92, 0.76, 0.96), False, 6500, 512),
    # ---- R2–R4（2026-09-15）：头盔 / 护甲 / 背包沿用 R1 的口径；面罩与耳机的目标盒按官方面罩 / 耳机实测（见 SOCKET_CENTER 注释）----
    'roothunter_facemask':        ('RootweaveMask_FaceMask',   'FaceMask', (0.61, 0.23, 0.31), False, 6500, 512),
    'roothunter_armor':           ('VinewovenCuirass_Armor',   'Armor',    (1.02, 0.60, 0.66), True,  6500, 512),
    'roothunter_backpack':        ('HangrootQuiver_Backpack',  'Backpack', (0.40, 0.66, 0.32), False, 6500, 512),
    'waylayer_backpack':          ('OldMailbag_Backpack',      'Backpack', (0.46, 0.44, 0.30), False, 6500, 512),
    'sickle_helmet':              ('GreenearStrawHat_Helmet',  'Helmat',   (1.05, 0.62, 1.05), False, 6500, 512),
    'sickle_armor':               ('StrawRaincoat_Armor',      'Armor',    (1.06, 0.60, 0.72), True,  6500, 512),
    'sickle_backpack':            ('GrainSack_Backpack',       'Backpack', (0.48, 0.56, 0.36), False, 6500, 512),
    'listener_headset':           ('RainhushEarmuffs_Headset', 'Headset',  (0.94, 0.64, 0.20), False, 6500, 512),
    'piper_facemask':             ('MossgauzeMask_FaceMask',   'FaceMask', (0.63, 0.30, 0.34), False, 6500, 512),
    'mirror_armor':               ('MirrorgrainPlate_Armor',   'Armor',    (1.00, 0.56, 0.62), True,  6500, 512),
    'windhunter_helmet':          ('WindbreakHood_Helmet',     'Helmat',   (0.90, 0.86, 1.04), False, 6500, 512),
    'windhunter_armor':           ('WindbreakMantle_Armor',    'Armor',    (1.00, 0.56, 0.70), True,  6500, 512),
    'windhunter_backpack':        ('WindbreakPack_Backpack',   'Backpack', (0.40, 0.58, 0.28), False, 6500, 512),
}

# 槽位 -> (贴背面在挂点空间的 z, 包体中心高度 y)；其余槽位导入基线居中，头盔佩戴偏移另由 Editor 应用。
BEHIND_SOCKET = {'Backpack': (-0.08, 0.03)}

# 槽位 -> 包围盒中心在挂点空间的 (x, y, z)（Unity 轴：y 上、z 前），原点留在挂点上（2026-09-15 用 UnityPy 读官方 IG_FackMask_* / IG_Headset_* 实测）：
# - 面罩挂在 FaceMaskSocket（头部中轴、眼睛高度），官方 Glass / Blindfold / GasMask 的网格都往前偏在顶点里，根节点生成时会被清零。
#   盖住眼睛与扁嘴上半的推荐包围盒 x ±0.305、y -0.09…+0.14、z +0.06…+0.37：中心 (0, +0.025, +0.215)。
# - 耳机挂在 HelmatSocket（与头盔同一个挂点）；官方耳罩贴头、与所有标准头盔互穿。不穿插 19 顶标准头盔的推荐盒
#   x ±0.47、y -0.18…+0.46、z -0.10…+0.10：中心 (0, +0.14, 0)。
SOCKET_CENTER = {'FaceMask': (0.0, 0.025, 0.215), 'Headset': (0.0, 0.14, 0.0)}

PASS_MARKER = 'SKY_ISLAND_BOSS_GEAR_IMPORT_OK'


def parse_overrides(values, cast):
    result = {}
    for value in values or []:
        if '=' not in value:
            raise SystemExit('覆盖参数要写成 件名=值：' + value)
        name, raw = value.split('=', 1)
        if name not in PIECES:
            raise SystemExit('未知装备件：' + name)
        result[name] = cast(raw)
    return result


def parse_box(raw):
    parts = [float(t) for t in raw.split(',')]
    if len(parts) != 3 or min(parts) <= 0:
        raise SystemExit('--box 要写成 件名=宽,高,深（米，均为正）：' + raw)
    return tuple(parts)


def mesh_bounds(mesh):
    coords = [v.co for v in mesh.vertices]
    if not coords:
        raise RuntimeError('模型没有顶点')
    return ([min(c[i] for c in coords) for i in range(3)], [max(c[i] for c in coords) for i in range(3)])


def depth_quantile(mesh, q):
    ys = sorted(v.co[1] for v in mesh.vertices)
    return ys[min(len(ys) - 1, int(q * (len(ys) - 1)))]


def normalise_equipment(obj, box, stretch, yaw_degrees, slot, back_quantile=0.0):
    """在 Blender 的 Z-up 空间里标准化：绕 Z 转 yaw，缩到目标盒并居中（背包再挪到挂点身后）。

    头盔的几何中心只是导入基线，盔壳与角色贴合由 Editor 的 HelmetFitUtility 完成。
    Blender 轴与挂点轴：X = 宽、Z = 高（Unity +Y）、-Y = 正前（Unity +Z）。
    不在这里换成 Y-up：FBX 导出器按 axis_up='Y' 统一换轴，手工换一次再让导出器换一次会躺倒。
    与场景件同一条教训：不用 obj.bound_box（后台模式下读到过期值），直接遍历顶点。
    """
    mesh = obj.data
    mesh.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    if yaw_degrees:
        mesh.transform(Matrix.Rotation(math.radians(yaw_degrees), 4, 'Z'))
    lo, hi = mesh_bounds(mesh)
    size = [hi[i] - lo[i] for i in range(3)]
    if min(size) < 1e-6:
        raise RuntimeError('模型某一轴尺寸为零，无法归一化')
    width, height, depth = box
    if stretch:
        factors = (width / size[0], depth / size[1], height / size[2])
    else:
        uniform = width / size[0]
        factors = (uniform, uniform, uniform)
    mesh.transform(Matrix.Diagonal((factors[0], factors[1], factors[2], 1.0)))
    if stretch and mesh.has_custom_normals:
        # 不等比缩放后自定义法线不再垂直于面，清掉让 Blender 按面重算，否则光照会歪
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    lo, hi = mesh_bounds(mesh)
    mesh.transform(Matrix.Translation((-(hi[0] + lo[0]) / 2, -(hi[1] + lo[1]) / 2, -(hi[2] + lo[2]) / 2)))
    placement = BEHIND_SOCKET.get(slot)
    if placement:
        front_z, center_y = placement
        lo, hi = mesh_bounds(mesh)
        # Unity z = -Blender y：贴背面（Unity z 最大）就是 Blender y 最小，挪到 y = -front_z
        # 给了 back_quantile 时贴背面取深度分位点：自带的背带环穿过躯干，包身贴背（见文件头 --back-quantile）
        back = depth_quantile(mesh, back_quantile) if back_quantile > 0 else lo[1]
        mesh.transform(Matrix.Translation((0.0, -front_z - back, center_y)))
    center = SOCKET_CENTER.get(slot)
    if center:
        # 已经居中在原点：Unity (x, y, z) = Blender (x, z, -y)，把包围盒中心挪到挂点空间里的指定位置
        cx, cy, cz = center
        mesh.transform(Matrix.Translation((cx, -cz, cy)))
    mesh.update()
    lo, hi = mesh_bounds(mesh)
    return {
        'min': [round(v, 4) for v in lo],
        'max': [round(v, 4) for v in hi],
        'widthHeightDepth': [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)],
        'scale': [round(f, 4) for f in factors],
    }


def export_fbx(obj, target):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    obj.name = target.stem
    bpy.ops.export_scene.fbx(
        filepath=str(target), use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
        path_mode='RELATIVE', embed_textures=False)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--glb-dir', required=True)
    parser.add_argument('--project', help='默认使用 BOSSRUSH_UNITY_PROJECT / unity_project_path.py')
    parser.add_argument('--only')
    parser.add_argument('--yaw', action='append')
    parser.add_argument('--box', action='append')
    parser.add_argument('--source', action='append')
    parser.add_argument('--back-quantile', action='append')
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    args = parser.parse_args(argv)

    args.project = args.project or find_unity_project()
    if not args.project:
        raise SystemExit('找不到 Unity 作者工程，请设置 BOSSRUSH_UNITY_PROJECT')
    fit_profiles = load_profiles()
    glb_dir = Path(args.glb_dir).resolve()
    out_dir = Path(args.project).resolve() / 'Assets' / 'SkyIslandBossGear' / 'Models'
    out_dir.mkdir(parents=True, exist_ok=True)
    yaws = parse_overrides(args.yaw, float)
    boxes = parse_overrides(args.box, parse_box)
    sources = parse_overrides(args.source, str)
    back_quantiles = parse_overrides(args.back_quantile, float)
    for name, q in back_quantiles.items():
        if PIECES[name][1] != 'Backpack' or not 0.0 <= q < 0.5:
            raise SystemExit('--back-quantile 只给背包、取值 [0, 0.5)：%s=%s' % (name, q))
    wanted = set(args.only.split(',')) if args.only else set(PIECES)
    unknown = wanted - set(PIECES)
    if unknown:
        raise SystemExit('未知装备件：' + ', '.join(sorted(unknown)))

    manifest = []
    for name in sorted(wanted):
        base, slot, default_box, stretch, budget, texture_limit = PIECES[name]
        if name in sources:
            source = glb_dir / sources[name]
            if not source.is_file():
                raise RuntimeError('--source 指的文件不存在：' + str(source))
        else:
            source = next((glb_dir / (name + ext) for ext in ('.glb', '.GLB', '.fbx', '.FBX') if (glb_dir / (name + ext)).is_file()), None)
        if source is None:
            # 缺件直接失败：owner 否决「资源不对就降级」，缺一件就不该继续打包。
            raise RuntimeError('缺少 Tripo 模型：' + str(glb_dir / (name + '.glb')))
        tripo.clear_scene()
        obj = tripo.import_model(source)
        before = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        box = boxes.get(name, default_box)
        yaw = yaws.get(name, 0.0)
        back_quantile = back_quantiles.get(name, 0.0)
        bounds = normalise_equipment(obj, box, stretch, yaw, slot, back_quantile)
        validate_staging_bounds(base, slot, bounds, fit_profiles)
        after = tripo.decimate(obj, budget)
        if after > budget:
            raise RuntimeError('%s 减面后仍超预算：%d > %d' % (name, after, budget))
        unwrapped = tripo.ensure_uv(obj)
        texture = tripo.save_texture(obj, out_dir / (base + '_Albedo.png'), texture_limit)
        if texture is None:
            raise RuntimeError(name + ' 没有 base color 贴图：Tripo 要选带贴图的输出，白模不打包')
        export_fbx(obj, out_dir / (base + '.fbx'))
        record = {'name': name, 'base': base, 'slot': slot, 'box': list(box), 'stretch': stretch, 'yaw': yaw,
                  'backQuantile': back_quantile, 'trianglesBefore': before, 'triangles': after, 'uvGenerated': unwrapped, 'texture': texture,
                  'bounds': bounds, 'source': source.name}
        manifest.append(record)
        print('%-28s -> %-26s %6d -> %5d 面  宽高深 %s  yaw %+.0f  贴图 %s'
              % (name, base, before, after, bounds['widthHeightDepth'], yaw, texture['size']))

    manifest_path = glb_dir / 'manifest.json'
    merged = {}
    if manifest_path.is_file():
        try:
            merged = {m['name']: m for m in json.loads(manifest_path.read_text(encoding='utf-8'))}
        except (ValueError, KeyError, TypeError):
            merged = {}
    merged.update({m['name']: m for m in manifest})
    manifest_path.write_text(json.dumps(list(merged.values()), indent=2, ensure_ascii=False), encoding='utf-8')
    print('%s 本次 %d 件  合计 %d 面  清单共 %d 件' % (PASS_MARKER, len(manifest), sum(m['triangles'] for m in manifest), len(merged)))


if __name__ == '__main__':
    main()
