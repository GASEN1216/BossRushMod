"""Recompute the existing Last Shift Yard scene bundle's opaque vertex alpha layers.

Run with UnityPy, NumPy and SciPy. The bundle must come from the Last Shift Yard
Unity author project and use the vertex alpha layer shader. For a normal Unity
rebuild, use build_lastshift_blend.py or rebuild_lastshift_fbx_layers.py instead.
Only the named output bundle and its verification report are written.
"""
from collections import defaultdict
from pathlib import Path
import argparse
import hashlib
import json
import types

import numpy as np
from scipy.spatial.transform import Rotation
import UnityPy
from UnityPy.streams import EndianBinaryReader, EndianBinaryWriter
from UnityPy.helpers.MeshHelper import MeshHandler

import build_lastshift_blend as solver


def key(obj):
    return obj.assets_file.name, obj.path_id


def transform(ref, cache):
    if not ref:
        return np.eye(4), ''
    obj = ref.deref()
    obj_key = key(obj)
    if obj_key not in cache:
        t = obj.read()
        parent, name = transform(t.m_Father, cache)
        p, q, s = t.m_LocalPosition, t.m_LocalRotation, t.m_LocalScale
        m = np.eye(4)
        m[:3, :3] = Rotation.from_quat([q.x, q.y, q.z, q.w]).as_matrix() * [s.x, s.y, s.z]
        m[:3, 3] = (p.x, p.y, p.z)
        cache[obj_key] = parent @ m, name + '/' + t.m_GameObject.read().m_Name
    return cache[obj_key]


class Scene:
    def __init__(self, env):
        self.meshes, self.objs, self.nodes, self.parts = {}, [], [], []
        self.materials = defaultdict(set)
        cache = {}
        for obj in env.objects:
            if obj.type.name != 'MeshRenderer':
                continue
            r = obj.read()
            go = r.m_GameObject.read()
            if not r.m_Enabled or not go.m_IsActive:
                continue
            components = {p.component.deref().type.name: p.component for p in go.m_Component}
            if 'Transform' not in components or 'MeshFilter' not in components or not r.m_Materials:
                continue
            m, path = transform(components['Transform'], cache)
            if not path.startswith('/LastShiftYardWorld/Visual/VIS_'):
                continue
            group = path.split('/')[3].removeprefix('VIS_').removesuffix('_instances')
            if group not in solver.ZF_GROUPS:
                continue
            materials = [p.read() for p in r.m_Materials if p]
            modes = {material.m_CustomRenderQueue for material in materials}
            if 3000 in modes:
                if modes != {3000}:
                    raise ValueError(f'Mixed transparent and opaque material on {path}')
                continue
            ref = components['MeshFilter'].read().m_Mesh
            if ref:
                mesh = ref.deref()
                for material in materials:
                    floats = dict(material.m_SavedProperties.m_Floats)
                    self.materials[key(mesh)].add((material.m_Name, floats.get('_UseVertexColor', 0), floats.get('_Decal', 0)))
                self.add(mesh, group, path, m)

    def add(self, obj, group, path, matrix):
        obj_key = key(obj)
        if obj_key not in self.meshes:
            mesh = obj.read()
            h = MeshHandler(mesh)
            h.process()
            local = np.asarray(h.m_Vertices, dtype=float)
            if not len(local):
                return
            indices = [np.asarray(ix) + sub.baseVertex for ix, sub in zip(h.get_triangles(), mesh.m_SubMeshes) if ix]
            if not indices:
                return
            idx = np.concatenate(indices)
            comp = solver._zf_components(len(local), idx)
            roots, inverse = np.unique(comp[idx[:, 0]], return_inverse=True)
            oi = len(self.objs)
            self.objs.append(types.SimpleNamespace(name=path, data=obj_key,
                                                   users_collection=[types.SimpleNamespace(name='LSY_' + group)]))
            n = len(self.nodes)
            self.nodes.extend((oi, int(root)) for root in roots)
            self.meshes[obj_key] = (obj, h, comp, local, idx, inverse + n, oi)
        _, _, _, local, idx, nodes, _ = self.meshes[obj_key]
        world = local @ matrix[:3, :3].T + matrix[:3, 3]
        self.parts.append((world[:, [0, 2, 1]][idx], nodes))

    def solve(self):
        packed = np.concatenate([part for part, _ in self.parts])
        tri_nodes = np.concatenate([node for _, node in self.parts])
        print(f'Bundle: {len(tri_nodes):,} triangles and {len(self.nodes):,} layer nodes', flush=True)
        pairs, sizes = solver._zf_overlap_pairs(packed[:, 0], packed[:, 1], packed[:, 2], tri_nodes)
        layers, capped = solver._zf_assign_layers(self.objs, self.nodes, pairs, sizes)
        bad = [(area, a, b) for (_, a, b), area in pairs.items() if layers[a] == layers[b]]
        if bad or capped:
            for area, a, b in sorted(bad, reverse=True)[:25]:
                print(area, self.objs[self.nodes[a][0]].name, self.objs[self.nodes[b][0]].name, flush=True)
            raise ValueError(f'{len(bad)} remaining overlaps and {len(capped)} capped nodes')
        per_mesh = defaultdict(dict)
        for node, layer in layers.items():
            oi, root = self.nodes[node]
            per_mesh[oi][root] = layer
        report = {'triangles': len(tri_nodes), 'candidates': len(self.nodes), 'pairs': len(pairs), 'nodes': len(layers),
                  'maxLayer': max(layers.values(), default=0), 'sameLayerOverlap_m2': 0, 'capped': capped}
        return per_mesh, report


def channel_view(data, h, channels):
    channel = channels[3]
    streams = h.get_streams(channels, h.src.m_VertexData.m_VertexCount)
    stream = streams[channel.stream]
    if channel.format == 0:
        dtype = '<f4'
    elif channel.format == 2:
        dtype = 'u1'
    else:
        raise ValueError(f'{h.src.m_Name}: unexpected color format {channel.format}')
    dt = np.dtype(dtype)
    return np.ndarray((h.src.m_VertexData.m_VertexCount, 4), dtype=dt, buffer=data,
                      offset=stream.offset + channel.offset, strides=(stream.stride, dt.itemsize))


def rewrite(obj, h, expected):
    vertex = h.src.m_VertexData
    channels = vertex.m_Channels
    data = bytearray(vertex.m_DataSize)
    obj_raw = obj.get_raw_data()
    channel = channels[3]
    if channel.dimension == 4:
        colors = channel_view(data, h, channels)
        values = expected if colors.dtype.itemsize == 4 else np.rint(expected * 255)
        old_alpha = colors[:, 3].astype(float) / (1 if colors.dtype.itemsize == 4 else 255)
        if np.array_equal(np.minimum(np.rint((1 - old_alpha) * 8), 7), np.rint((1 - expected) * 8)):
            return False
        colors[:, 3] = values
        offset = obj_raw.find(vertex.m_DataSize)
        if offset < 0 or obj_raw.count(vertex.m_DataSize) != 1:
            raise ValueError(f'{h.src.m_Name}: vertex stream does not have a unique byte range')
        obj.set_raw_data(obj_raw[:offset] + data + obj_raw[offset + len(data):])
        return True
    if channel.dimension != 0:
        raise ValueError(f'{h.src.m_Name}: malformed color channel')
    if np.all(expected == 1):
        return False
    tree = obj.read_typetree()
    if obj.save_typetree(tree) != obj_raw:
        raise ValueError(f'{h.src.m_Name}: original mesh cannot round-trip byte-for-byte')
    v = tree['m_VertexData']
    stream = 1 + max(c.stream for c in channels if c.dimension)
    if stream > 3:
        raise ValueError(f'{h.src.m_Name}: no free vertex stream')
    v['m_Channels'][3] = {'stream': stream, 'offset': 0, 'format': 2, 'dimension': 4}
    colors = np.full((len(expected), 4), 255, dtype=np.uint8)
    colors[:, 3] = np.rint(expected * 255)
    padding = bytes(-len(data) % 16)
    v['m_DataSize'] = bytes(data) + padding + colors.tobytes()
    obj.save_typetree(tree)
    return True


def main():
    p = argparse.ArgumentParser()
    p.add_argument('source', type=Path)
    p.add_argument('output', type=Path, nargs='?')
    args = p.parse_args()
    e = UnityPy.load(str(args.source))
    scene = Scene(e)
    layers, report = scene.solve()
    print(json.dumps(report, ensure_ascii=False), flush=True)
    if not args.output:
        return
    if args.output.resolve() == args.source.resolve():
        p.error('source and output must differ')
    changed = {}
    added = {}
    for obj_key, (obj, h, comp, _, _, _, oi) in scene.meshes.items():
        expected = np.ones(len(comp), dtype=float)
        for root, layer in layers[oi].items():
            expected[comp == root] = 1 - layer / 8
        if rewrite(obj, h, expected):
            changed[obj_key] = (h.src.m_Name, expected)
            if not h.src.m_VertexData.m_Channels[3].dimension:
                consumers = sorted(scene.materials[obj_key])
                if any(vertex_rgb or decal for _, vertex_rgb, decal in consumers):
                    raise ValueError(f'{h.src.m_Name}: would add a color channel to a material using vertex RGB or decal alpha')
                added[h.src.m_Name] = consumers
    report['changedMeshes'] = len(changed)
    report['addedColorChannels'] = added
    args.output.parent.mkdir(parents=True, exist_ok=True)
    bundle = next(iter(e.files.values()))
    changed_files = {name for name, _ in changed}
    result = save_bundle(bundle, changed_files)
    staged = args.output.with_name(args.output.name + '.candidate')
    with staged.open('xb') as file:
        file.write(result)
    verify(args.source, result, changed)
    staged.replace(args.output)
    report['method'] = 'recomputed opaque layers on serialized scene meshes with UnityPy, without rebuilding the Unity project or the Blender scene'
    report['nextBuild'] = 'Regenerate source FBX layers and build with the Unity author project. The serialized mesh topology and layer statistics differ from the FBX inputs.'
    report['sourceSHA256'] = hashlib.sha256(args.source.read_bytes()).hexdigest()
    report['bundleSHA256'] = hashlib.sha256(result).hexdigest()
    args.output.with_name(args.output.name + '_layer_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('LASTSHIFT_BUNDLE_LAYERS_OK', report)


def save_bundle(bundle, changed_files):
    for name, serialized in bundle.files.items():
        if name not in changed_files:
            # UnityPy normally reserializes every archive member. The untouched scene
            # must retain even its object order, padding and metadata bytes.
            raw = EndianBinaryReader(serialized.reader.bytes)
            raw.flags = serialized.flags
            bundle.files[name] = raw
    return bundle.save(packer='original')


def verify(source, output, changed):
    a = UnityPy.load(str(source))
    b = UnityPy.load(output if isinstance(output, bytes) else str(output))
    for obj_key, old_obj, new_obj in verified_changed_objects(a, b, changed):
        name, expected = changed[obj_key]
        verify_mesh_layers(old_obj, new_obj, name, expected)


def verified_changed_objects(a, b, changed):
    old_bundle, new_bundle = (next(iter(e.files.values())) for e in (a, b))
    settings = ('signature', 'version', 'version_player', 'version_engine', 'dataflags', '_block_info_flags')
    if any(getattr(old_bundle, attr) != getattr(new_bundle, attr) for attr in settings) or old_bundle.files.keys() != new_bundle.files.keys():
        raise ValueError('UnityFS archive settings or file inventory changed')
    for name, f in old_bundle.files.items():
        g = new_bundle.files[name]
        if f.flags != g.flags or f.unity_version != g.unity_version or f.target_platform != g.target_platform or f.header.version != g.header.version:
            raise ValueError(f'Serialized file metadata changed: {name}')
        if [(x.path, x.guid, x.type) for x in f.externals] != [(x.path, x.guid, x.type) for x in g.externals]:
            raise ValueError(f'Serialized file dependencies changed: {name}')
        if serialized_types(f) != serialized_types(g):
            raise ValueError(f'Serialized file type definitions changed: {name}')
        if not any(obj_key[0] == name for obj_key in changed) and f.reader.bytes != g.reader.bytes:
            raise ValueError(f'Untouched serialized scene file changed: {name}')
    old, new = ({key(o): o for o in e.objects} for e in (a, b))
    if old.keys() != new.keys():
        raise ValueError('Bundle object inventory changed')
    for obj_key, old_obj in old.items():
        new_obj = new[obj_key]
        if old_obj.type != new_obj.type:
            raise ValueError(f'Bundle object type changed: {obj_key}')
        if obj_key not in changed:
            if old_obj.get_raw_data() != new_obj.get_raw_data():
                raise ValueError(f'Unrelated bundle object changed: {obj_key} {old_obj.type}')
            continue
        yield obj_key, old_obj, new_obj


def verify_mesh_layers(old_obj, new_obj, name, expected):
    old_tree, new_tree = (o.read_typetree() for o in (old_obj, new_obj))
    old_vertex, new_vertex = (t.pop('m_VertexData') for t in (old_tree, new_tree))
    if old_tree != new_tree or old_vertex['m_VertexCount'] != new_vertex['m_VertexCount']:
        raise ValueError(f'{name}: non-vertex mesh data changed')
    oc, nc = (v['m_Channels'] for v in (old_vertex, new_vertex))
    if len(oc) != len(nc) or any(x != y for i, (x, y) in enumerate(zip(oc, nc)) if i != 3):
        raise ValueError(f'{name}: a non-color channel changed')
    handler = MeshHandler(new_obj.read())
    old_data, new_data = (bytearray(v['m_DataSize']) for v in (old_vertex, new_vertex))
    new_rgba = channel_view(new_data, handler, handler.src.m_VertexData.m_Channels)
    new_alpha = new_rgba[:, 3].astype(float) / (1 if new_rgba.dtype.itemsize == 4 else 255)
    if not np.array_equal(np.rint((1 - new_alpha) * 8), np.rint((1 - expected) * 8)):
        raise ValueError(f'{name}: emitted alpha does not match the layer assignment')
    if oc[3]['dimension']:
        if oc != nc:
            raise ValueError(f'{name}: an existing color channel was moved')
        old_rgba = channel_view(old_data, handler, handler.src.m_VertexData.m_Channels)
        new_rgba[:, 3] = old_rgba[:, 3]
        if old_data != new_data:
            raise ValueError(f'{name}: bytes other than vertex alpha changed')
    elif not np.all(new_rgba[:, :3] == 255) or new_data[:len(old_data)] != old_data:
        raise ValueError(f'{name}: added channel modified RGB or old vertex streams')


def serialized_types(file):
    out = EndianBinaryWriter(endian=file.header.endian)
    for typ in file.types:
        typ.write(file, out, False)
    for typ in file.ref_types:
        typ.write(file, out, True)
    for typ in file.script_types:
        typ.write(file.header, out)
    return out.bytes


if __name__ == '__main__':
    main()
