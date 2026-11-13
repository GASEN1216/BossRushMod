"""Recompute vertex alpha and instance layers in existing Last Shift Yard FBX exports.

Run with NumPy, SciPy and Blender's installed io_scene_fbx Python scripts.
Only the specified output directory is written. The original FBX files, Blender
scene, textures and material manifest stay in place. Copy the changed FBX models
and instance table from the report into the Unity author project before building.
"""
from collections import defaultdict
from pathlib import Path
import argparse
import copy
import hashlib
import json
import sys
import time
import types

import numpy as np
from scipy.spatial.transform import Rotation

import build_lastshift_blend as solver
from lastshift_vegetation import GRASS_MESHES


ROOT = Path(__file__).resolve().parents[1]
FBX_DIR = ROOT / 'ArtSource/LastShiftYard/fbx'


def load_blender_tools(root):
    package = types.ModuleType('blender_fbx')
    package.__path__ = [str(root)]
    sys.modules[package.__name__] = package
    from blender_fbx import encode_bin, parse_fbx
    return encode_bin, parse_fbx


def child(e, name):
    return next(item for item in e.elems if item.id == name)


def properties(obj):
    return {item.props[0]: item.props[4:] for item in child(obj, b'Properties70').elems}


def triangles(geometry):
    poly = np.asarray(child(geometry, b'PolygonVertexIndex').props[0], dtype=np.int64)
    end = poly < 0
    poly[end] = -poly[end] - 1
    if len(poly) % 3 or not np.all(end[2::3]) or np.any(end[::3]) or np.any(end[1::3]):
        return None
    return poly.reshape(-1, 3)


def vertices(geometry):
    return np.asarray(child(geometry, b'Vertices').props[0], dtype=np.float64).reshape(-1, 3)


class Group:
    def __init__(self, path, parser):
        self.path = path
        self.name = path.stem.removeprefix('LastShiftYard_')
        self.root, self.version = parser.parse(path)
        objects = child(self.root, b'Objects').elems
        connections = child(self.root, b'Connections').elems
        self.models = {o.props[0]: o for o in objects if o.id == b'Model'}
        self.geometries = {o.props[0]: o for o in objects if o.id == b'Geometry' and o.props[2] == b'Mesh'}
        self.parents, self.meshes, self.transforms = {}, {}, {}
        for conn in connections:
            mode, a, b, *_ = conn.props
            if mode != b'OO' or b not in self.models:
                continue
            if a in self.models:
                self.parents[a] = b
            if a in self.geometries:
                if b in self.meshes:
                    raise ValueError(f'{path}: multiple geometries on model {b}')
                self.meshes[b] = self.geometries[a]

    def transform(self, model_id):
        if model_id not in self.transforms:
            p = properties(self.models[model_id])
            xyz = p.get(b'Lcl Translation', (0, 0, 0))
            degrees = p.get(b'Lcl Rotation', (0, 0, 0))
            scale = p.get(b'Lcl Scaling', (1, 1, 1))
            if any(k in p for k in (b'PreRotation', b'PostRotation', b'RotationPivot', b'GeometricRotation', b'RotationOrder')):
                raise ValueError(f'{self.path}: unsupported transform on model {model_id}')
            m = np.eye(4)
            m[:3, :3] = Rotation.from_euler('xyz', degrees, degrees=True).as_matrix() * scale
            m[:3, 3] = xyz
            if model_id in self.parents:
                m = self.transform(self.parents[model_id]) @ m
            self.transforms[model_id] = m
        return self.transforms[model_id]

    def positions(self, model_id, coords):
        transform = self.transform(model_id)
        return (coords @ transform[:3, :3].T + transform[:3, 3]) * (0.01, 0.01, -0.01)


class Scene:
    def __init__(self):
        self.start = time.monotonic()
        self.objs, self.nodes, self.parts, self.mesh_records, self.inst_records = [], [], [], [], []

    def add(self, name, group, geometry, coords, idx, inst=None):
        obj_id = len(self.objs)
        self.objs.append(types.SimpleNamespace(name=name, data=geometry.props[0],
                                               users_collection=[types.SimpleNamespace(name='LSY_' + group.name)]))
        if inst is None:
            comp = solver._zf_components(len(coords), idx)
            roots, inverse = np.unique(comp[idx[:, 0]], return_inverse=True)
            tri_node = inverse.astype(np.int64) + len(self.nodes)
            self.nodes.extend((obj_id, int(r)) for r in roots)
            self.mesh_records.append((obj_id, group, geometry, comp[idx].ravel()))
        else:
            tri_node = np.full(len(idx), len(self.nodes), dtype=np.int64)
            self.nodes.append((obj_id, -1))
            self.inst_records.append((obj_id, inst))
        # Unity x,y,z -> the Blender solver's x,y,z, whose vertical axis is z.
        self.parts.append((coords[:, [0, 2, 1]][idx], tri_node))

    def solve(self):
        packed = np.concatenate([part for part, _ in self.parts])
        tri_node = np.concatenate([node for _, node in self.parts])
        print(f'Triangles: {len(tri_node):,}; layer nodes: {len(self.nodes):,}', flush=True)
        pairs, sizes = solver._zf_overlap_pairs(packed[:, 0], packed[:, 1], packed[:, 2], tri_node)
        print(f'Overlap graph: {len(pairs):,} plane edges in {time.monotonic() - self.start:.1f}s', flush=True)
        layers, capped = solver._zf_assign_layers(self.objs, self.nodes, pairs, sizes)
        residual = [(v, a, b) for (_, a, b), v in pairs.items() if layers[a] == layers[b]]
        if residual or capped:
            for area, a, b in sorted(residual, reverse=True)[:30]:
                oi, ra = self.nodes[a]; oj, rb = self.nodes[b]
                print(f'Residual {area:.8f} layer {layers[a]}: {self.objs[oi].name} {ra} / {self.objs[oj].name} {rb}')
            raise ValueError(f'{len(residual)} residual layer overlaps, {len(capped)} capped')
        out = defaultdict(dict)
        for node, layer in layers.items():
            oi, root = self.nodes[node]
            out[oi][root] = layer
        self.report = {'triangles': len(tri_node), 'candidateNodes': len(self.nodes), 'pairs': len(pairs),
                       'nodes': len(layers), 'objects': len(out), 'maxLayer': max(layers.values(), default=0),
                       'sameLayerOverlap_m2': 0, 'capped': capped}
        return out


def inspect(scene, parse_fbx, source):
    groups = []
    for group_name in solver.ZF_GROUPS:
        group = Group(source / f'LastShiftYard_{group_name}.fbx', parse_fbx)
        groups.append(group)
        for model_id, geometry in group.meshes.items():
            model = group.models[model_id]
            name = model.props[1].split(b'\0')[0].decode()
            if name in GRASS_MESHES.get(group_name, ()) or b'lsy_decal' in properties(model):
                continue
            ix = triangles(geometry)
            if ix is None:
                if group.name == 'scatter' and geometry.props[1].split(b'\0')[0].endswith(b'_tubes'):
                    continue  # 旧 FBX 导出在分层完成之后才把站外接触网线转成四边形细管
                raise ValueError(f'{group.path}: expected triangulated mesh {name}')
            scene.add(name, group, geometry, group.positions(model_id, vertices(geometry)), ix)
    proto = Group(source / 'LastShiftYard_instance_prototypes.fbx', parse_fbx)
    table = json.loads((source / 'lastshift_instances.json').read_text(encoding='utf-8'))
    model_keys = {o.props[1].split(b'\0')[0].decode(): i for i, o in proto.models.items()}
    for name, rec in table['prototypes'].items():
        group = next(g for g in groups if g.name == rec['group'])
        model_id = model_keys[name]
        geom = proto.meshes[model_id]
        local_coords = proto.positions(model_id, vertices(geom))
        ix = triangles(geom)
        for inst_idx, transform in enumerate(rec['instances']):
            mat = np.asarray(transform).reshape(3, 4)
            coords = local_coords @ mat[:, :3].T + mat[:, 3]
            scene.add(f'{name}/{inst_idx:05d}', group, geom, coords, ix, inst=(name, inst_idx))
    layers = scene.solve()
    print('Per-object layer assignments:', len(layers), flush=True)
    return groups, table, layers


def write_outputs(scene, groups, table, layers, source, output, encode_bin, parse_fbx):
    verify_inputs = {group.name: {geo.props[0]: mesh_fingerprint(geo) for geo in group.geometries.values()} for group in groups}
    original_table = copy.deepcopy(table)
    example = next(geo for group in groups if group.name == 'props' for geo in group.geometries.values()
                   if any(e.id == b'LayerElementColor' for e in geo.elems))
    color_template = child(example, b'LayerElementColor')
    layer_template = next(e for e in child(example, b'Layer').elems if e.id == b'LayerElement'
                          and child(e, b'Type').props[0] == b'LayerElementColor')
    by_group = defaultdict(list)
    for obj_id, group, geometry, loop_roots in scene.mesh_records:
        if obj_id not in layers:
            continue
        assignment = layers[obj_id]
        alpha = np.fromiter((1.0 - assignment.get(r, 0) / 8.0 for r in loop_roots), dtype=np.float64, count=len(loop_roots))
        if set_alpha(geometry, alpha, color_template, layer_template):
            by_group[group.name].append(scene.objs[obj_id].name)
    for name, rec in table['prototypes'].items():
        rec['layers'] = [0] * len(rec['instances'])
    for obj_id, (name, index) in scene.inst_records:
        table['prototypes'][name]['layers'][index] = layers.get(obj_id, {}).get(-1, 0)
    for rec in table['prototypes'].values():
        if not any(rec['layers']):
            rec['layers'] = None
    output.mkdir(parents=True, exist_ok=True)
    outputs = []
    for group in groups:
        if group.name in by_group:
            out = output / group.path.name
            with encode_bin.FBXElem.enable_multithreading_cm():
                encoded = encode(group.root, encode_bin.FBXElem)
            encode_bin.write(out, encoded, group.version)
            reopened, _ = parse_fbx.parse(out)
            if not equal(group.root, reopened, ignore_time=True):
                raise ValueError(f'{out}: FBX round trip changed the encoded model')
            original, _ = parse_fbx.parse(group.path)
            if not equal(strip_color(original), strip_color(reopened), ignore_time=True):
                raise ValueError(f'{out}: changed FBX element other than colors')
            verify_outputs = {geo.props[0]: mesh_fingerprint(geo) for geo in child(reopened, b'Objects').elems
                              if geo.id == b'Geometry' and geo.props[2] == b'Mesh'}
            if verify_inputs[group.name] != verify_outputs:
                raise ValueError(f'{out}: geometry or RGB changed')
            if not all(valid_alpha(geo) for geo in child(reopened, b'Objects').elems if geo.id == b'Geometry'):
                raise ValueError(f'{out}: vertex alpha is outside [0, 1]')
            outputs.append(out)
            print(f'{out.name}: patched {len(by_group[group.name])} alpha meshes', flush=True)
    output_table = output / 'lastshift_instances.json'
    output_table.write_text(json.dumps(table, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    loaded_table = json.loads(output_table.read_text(encoding='utf-8'))
    if loaded_table != table:
        raise ValueError('Instance table round trip changed the encoded data')
    for proto in loaded_table['prototypes'].values():
        values = proto.pop('layers')
        if values is not None and (len(values) != len(proto['instances']) or any(not 0 <= layer <= solver.ZF_MAX_LAYER for layer in values)):
            raise ValueError('Instance layer count or range does not match the instances')
    for proto in original_table['prototypes'].values():
        del proto['layers']
    if loaded_table != original_table:
        raise ValueError('Instance table fields other than layers changed')
    outputs.append(output_table)
    report = {'method': 'recomputed layers on existing FBX exports, Blender scene not regenerated',
              'source': str(source.resolve()), 'coplanar': scene.report,
              'files': {f.name: hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted(outputs)},
              'updatedMeshes': {group: sorted(meshes) for group, meshes in by_group.items()}}
    (output / 'lastshift_fbx_layer_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')


def strip_color(element):
    def is_color(item):
        return item.id == b'LayerElementColor' or (item.id == b'LayerElement' and child(item, b'Type').props[0] == b'LayerElementColor')

    return element._replace(elems=[strip_color(child) for child in element.elems if not is_color(child)])


def valid_alpha(geometry):
    color = next((e for e in geometry.elems if e.id == b'LayerElementColor'), None)
    if color is None:
        return True
    alpha = np.asarray(child(color, b'Colors').props[0])[3::4]
    return np.all(np.isfinite(alpha)) and np.all((0 <= alpha) & (alpha <= 1))


def equal(a, b, ignore_time=False):
    if ignore_time and a.id in (b'CreationTime', b'FileId'):
        return a.id == b.id
    return (a.id == b.id and a.props_type == b.props_type and len(a.props) == len(b.props)
            and all(np.array_equal(x, y) if isinstance(x, np.ndarray) else x == y for x, y in zip(a.props, b.props))
            and len(a.elems) == len(b.elems)
            and all(equal(x, y, ignore_time) for x, y in zip(a.elems, b.elems)))


def mesh_fingerprint(geometry):
    digest = hashlib.sha256()
    for field in (b'Vertices', b'PolygonVertexIndex'):
        digest.update(child(geometry, field).props[0].tobytes())
    color = next((element for element in geometry.elems if element.id == b'LayerElementColor'), None)
    if color is not None:
        values = np.asarray(child(color, b'Colors').props[0]).reshape(-1, 4)
        rgb = values[np.asarray(child(color, b'ColorIndex').props[0]), :3]
        if not np.all(rgb == 1.0):
            digest.update(np.ascontiguousarray(rgb).tobytes())
    return digest.digest()


def set_alpha(geometry, alpha, color_template, layer_template):
    channel = next((child for child in geometry.elems if child.id == b'LayerElementColor'), None)
    if channel is None:
        if np.all(alpha == 1.0):
            return False
        channel = copy.deepcopy(color_template)
        layer = child(geometry, b'Layer')
        geometry.elems.insert(geometry.elems.index(layer), channel)
        layer.elems.append(copy.deepcopy(layer_template))
        colors = np.ones((len(alpha), 4))
    else:
        if child(channel, b'MappingInformationType').props[0] != b'ByPolygonVertex':
            raise ValueError(f'Unexpected color mapping {geometry.props[1]}')
        if child(channel, b'ReferenceInformationType').props[0] != b'IndexToDirect':
            raise ValueError(f'Unexpected color reference {geometry.props[1]}')
        colors = np.asarray(child(channel, b'Colors').props[0], dtype=np.float64).reshape(-1, 4)
        indices = np.asarray(child(channel, b'ColorIndex').props[0], dtype=np.int64)
        colors = colors[indices]
        if len(alpha) != len(colors):
            raise ValueError(f'Unexpected color length {geometry.props[1]}')
        if np.all(np.minimum(np.rint((1.0 - colors[:, 3]) * 8.0), 7) == np.rint((1.0 - alpha) * 8.0)):
            return False
    colors[:, 3] = alpha
    direct, indices = np.unique(colors, axis=0, return_inverse=True)
    child(channel, b'Colors').props[0] = direct.ravel()
    child(channel, b'ColorIndex').props[0] = indices.astype(np.int32)
    return True


def encode(parsed, element_type):
    out = element_type(parsed.id)
    add = {b'B': out.add_bool, b'C': out.add_char, b'Z': out.add_int8, b'Y': out.add_int16,
           b'I': out.add_int32, b'L': out.add_int64, b'F': out.add_float32, b'D': out.add_float64,
           b'R': out.add_bytes, b'S': out.add_string, b'i': out.add_int32_array, b'l': out.add_int64_array,
           b'f': out.add_float32_array, b'd': out.add_float64_array, b'b': out.add_bool_array, b'c': out.add_byte_array}
    for typ, value in zip(parsed.props_type, parsed.props):
        add[bytes((typ,))](value)
    out.elems = [encode(sub, element_type) for sub in parsed.elems]
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--source', type=Path, default=FBX_DIR)
    p.add_argument('--output', type=Path)
    p.add_argument('--fbx-addon', type=Path, default=Path('D:/blender/5.2/scripts/addons_core/io_scene_fbx'))
    args = p.parse_args()
    if args.output and (args.output.resolve().is_relative_to(args.source.resolve())
                        or args.source.resolve().is_relative_to(args.output.resolve())):
        p.error('--output and --source must be separate directories')
    encode_bin, parse_fbx = load_blender_tools(args.fbx_addon)
    scene = Scene()
    groups, table, layers = inspect(scene, parse_fbx, args.source)
    if args.output:
        write_outputs(scene, groups, table, layers, args.source, args.output, encode_bin, parse_fbx)


if __name__ == '__main__':
    main()
