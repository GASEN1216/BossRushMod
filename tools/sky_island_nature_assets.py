"""Kenney CC0 自然网格的缓存读取与天空岛静态合批适配。

原始 OBJ 与生成器都采用 Y-up 的 Unity XYZ；此模块不做 Blender 换轴。
`palette` 是源 MTL 材质名到生成器 PALETTE key 的可选覆盖表。
"""
import math
from functools import lru_cache
from pathlib import Path
from typing import NamedTuple


ASSET_DIRECTORY = (Path(__file__).resolve().parents[1] / 'ArtSource' / 'SkyIsland'
                   / 'ThirdParty' / 'KenneyNatureKit' / 'Selected')

DEFAULT_MATERIALS = {
    'grass': 'Leaf', 'leafsGreen': 'LeafLight', 'leafsDark': 'Forest',
    'colorPurple': 'Lavender', 'colorRed': 'Blossom', 'colorYellow': 'Flower',
    'colorWhite': 'BrassLight', '_defaultMat': 'Ivory', 'colorTan': 'CrystalLavender',
    'woodBark': 'Wood', 'woodInner': 'WoodLight', 'wood': 'WoodLight',
    'woodDark': 'WoodDark', 'stone': 'RockLight',
}


class MeshPart(NamedTuple):
    """一个材质的紧凑顶点表与三角面，均为不可变 tuple。"""
    material: str
    verts: tuple
    faces: tuple


def _material_names(path):
    names = set()
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        fields = line.partition('#')[0].split()
        if fields and fields[0] == 'newmtl':
            if len(fields) != 2:
                raise ValueError('Invalid MTL declaration in ' + str(path))
            names.add(fields[1])
    return names


@lru_cache(maxsize=64)
def load_model(name):
    """读取选中 OBJ；返回按源材质分组、底部居中、总高为 1 的网格。

    保留源三角绕序，凸多边形按扇形三角化。选中 Kenney 模型已核实
    适用此方式；本函数不是任意 OBJ 的通用凹多边形导入器。
    """
    if not isinstance(name, str) or not name or any(c in name for c in '/\\:.'):
        raise ValueError('Expected a selected model stem, got ' + repr(name))
    path = ASSET_DIRECTORY / (name + '.obj')
    declared_materials = _material_names(path.with_suffix('.mtl'))
    vertices = []
    groups = {}
    material = None
    for line_number, line in enumerate(path.read_text(encoding='utf-8-sig').splitlines(), 1):
        fields = line.partition('#')[0].split()
        if not fields:
            continue
        if fields[0] == 'v':
            vertex = tuple(float(v) for v in fields[1:4])
            if len(vertex) != 3 or not all(math.isfinite(v) for v in vertex):
                raise ValueError('Invalid vertex at {}:{}'.format(path, line_number))
            vertices.append(vertex)
        elif fields[0] == 'usemtl':
            if len(fields) != 2 or fields[1] not in declared_materials:
                raise ValueError('Unknown material at {}:{}'.format(path, line_number))
            material = fields[1]
        elif fields[0] == 'f':
            if material is None or len(fields) < 4:
                raise ValueError('Missing material or invalid face at {}:{}'.format(path, line_number))
            indices = []
            for reference in fields[1:]:
                source_index = int(reference.split('/')[0])
                index = source_index - 1 if source_index > 0 else len(vertices) + source_index
                if source_index == 0 or not 0 <= index < len(vertices):
                    raise ValueError('Invalid face index at {}:{}'.format(path, line_number))
                indices.append(index)
            faces = groups.setdefault(material, [])
            faces.extend((indices[0], indices[i], indices[i + 1])
                         for i in range(1, len(indices) - 1))
    if not vertices or not groups:
        raise ValueError('Model has no visible geometry: ' + str(path))
    minimum = tuple(min(v[axis] for v in vertices) for axis in range(3))
    maximum = tuple(max(v[axis] for v in vertices) for axis in range(3))
    height = maximum[1] - minimum[1]
    if not math.isfinite(height) or height <= 0:
        raise ValueError('Model has no positive height: ' + str(path))
    origin = ((minimum[0] + maximum[0]) * .5, minimum[1],
              (minimum[2] + maximum[2]) * .5)
    normalized = tuple(tuple((v[axis] - origin[axis]) / height for axis in range(3))
                       for v in vertices)
    parts = []
    for material, faces in groups.items():
        # 每个材质仅带自己引用的顶点，避免合批时复制整株的顶点表。
        used = sorted({index for face in faces for index in face})
        remap = {source: target for target, source in enumerate(used)}
        parts.append(MeshPart(material, tuple(normalized[i] for i in used),
                              tuple(tuple(remap[i] for i in face) for face in faces)))
    return tuple(parts)


def _palette_for(name, overrides):
    materials = dict(DEFAULT_MATERIALS)
    if name.startswith('mushroom_'):
        materials['colorRed'] = 'Teal' if name.endswith('Tall') else 'Coral'
        materials['_defaultMat'] = 'Chalk' if name.endswith('Group') else 'Ivory'
    elif name == 'flower_redB':
        materials['colorRed'] = 'Coral'
    elif name.startswith('lily_'):
        materials['colorRed'] = 'Blossom'
    if name == 'flower_yellowC':
        materials['colorWhite'] = 'Glow'
    if overrides is not None:
        materials.update(overrides)
    return materials


def geometry(name, position, height, yaw=0, palette=None):
    """返回摆放后的 MeshPart 序列；height 为目标米数，yaw 为弧度。

    position 是底部中心的 Unity XYZ；绕 Y 轴旋转遵循主生成器 box 的
    `(x*cos + z*sin, y, -x*sin + z*cos)` 约定。无碰撞和导航副作用。
    """
    position = tuple(float(v) for v in position)
    height, yaw = float(height), float(yaw)
    if len(position) != 3 or not all(math.isfinite(v) for v in position + (height, yaw)):
        raise ValueError('Expected finite position XYZ, height and yaw')
    if height <= 0:
        raise ValueError('Target height must be positive')
    materials = _palette_for(name, palette)
    cosine, sine = math.cos(yaw), math.sin(yaw)
    result = []
    for part in load_model(name):
        target_material = materials.get(part.material)
        if not isinstance(target_material, str) or not target_material:
            raise ValueError('Missing palette mapping for ' + part.material)
        verts = tuple((position[0] + height * (x * cosine + z * sine),
                       position[1] + height * y,
                       position[2] + height * (-x * sine + z * cosine))
                      for x, y, z in part.verts)
        if not all(math.isfinite(v) for vertex in verts for v in vertex):
            raise ValueError('Placement exceeds finite coordinates for ' + name)
        result.append(MeshPart(target_material, verts, part.faces))
    return tuple(result)


def stamp(g, name, position, height, yaw=0, palette=None):
    """将一株静态装饰写入 g.addmesh，复用 g.CURRENT 与材质合批。

    返回实际写入的 MeshPart，供作者流程统计。若 g 暴露 PALETTE，
    先验证所有目标材质，以免在写入一半时才发现缺少色板 key。
    """
    parts = geometry(name, position, height, yaw, palette)
    if hasattr(g, 'PALETTE'):
        missing = {part.material for part in parts} - set(g.PALETTE)
        if missing:
            raise ValueError('Missing sky island palette keys: ' + ', '.join(sorted(missing)))
    for part in parts:
        g.addmesh(part.material, part.verts, part.faces, smooth=False)
    return parts
