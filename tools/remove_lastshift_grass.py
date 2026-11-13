"""Remove the retired Last Shift Yard grass from an existing bundle or FBX export.

The bundle mode disables the five serialized mesh renderers and leaves the source
bundle intact. The FBX mode produces two replacement models for the author project.
Neither mode starts the game or editor. Use a new output path for each build.
"""
import argparse
import hashlib
import json
from pathlib import Path

from lastshift_vegetation import GRASS_MESHES


def require_targets(actual, expected):
    if actual != expected:
        raise ValueError(f'Unexpected decorative grass objects: missing {sorted(expected - actual)}, extra {sorted(actual - expected)}')


def grass_renderers(env):
    from rebuild_lastshift_bundle_layers import key, transform

    targets = {}
    seen = set()
    cache = {}
    for obj in env.objects:
        if obj.type.name != 'MeshRenderer':
            continue
        renderer = obj.read()
        go = renderer.m_GameObject.read()
        if not any(go.m_Name in names for names in GRASS_MESHES.values()):
            continue
        components = {p.component.deref().type.name: p.component for p in go.m_Component}
        _, path = transform(components['Transform'], cache)
        group = path.split('/')[3].removeprefix('VIS_')
        if not path.startswith('/LastShiftYardWorld/Visual/VIS_') or go.m_Name not in GRASS_MESHES.get(group, ()):
            raise ValueError(f'Unexpected decorative grass path: {path}')
        if go.m_Name in seen or set(components) != {'MeshRenderer', 'MeshFilter', 'Transform'}:
            raise ValueError(f'Unexpected decorative grass components: {path}')
        mesh = components['MeshFilter'].read().m_Mesh.read()
        if mesh.m_Name != go.m_Name:
            raise ValueError(f'Unexpected decorative grass mesh: {path}')
        seen.add(go.m_Name)
        targets[key(obj)] = obj, path, sum(sub.indexCount // 3 for sub in mesh.m_SubMeshes)
    require_targets(seen, set().union(*GRASS_MESHES.values()))
    return targets


def verify_disabled_renderers(before, after, targets):
    from rebuild_lastshift_bundle_layers import verified_changed_objects

    disabled = grass_renderers(after)
    if targets.keys() != disabled.keys():
        raise ValueError('Decorative grass renderer identity changed')
    for obj_key, old, new in verified_changed_objects(before, after, targets):
        path = targets[obj_key][1]
        old_tree, new_tree = old.read_typetree(), new.read_typetree()
        if old_tree.pop('m_Enabled') != 1 or new_tree.pop('m_Enabled') != 0 or old_tree != new_tree:
            raise ValueError(f'Unexpected decorative grass renderer change: {path}')


def patch_bundle(source, output):
    import UnityPy
    from rebuild_lastshift_bundle_layers import save_bundle

    original = source.read_bytes()
    before = UnityPy.load(original)
    patch = UnityPy.load(original)
    targets = grass_renderers(patch)
    for obj, path, _ in targets.values():
        tree = obj.read_typetree()
        if tree['m_Enabled'] != 1:
            raise ValueError(f'Expected an enabled source renderer: {path}')
        raw = obj.get_raw_data()
        if obj.save_typetree(tree) != raw:
            raise ValueError(f'Renderer does not round-trip byte-for-byte: {path}')
        tree['m_Enabled'] = 0
        if sum(a != b for a, b in zip(raw, obj.save_typetree(tree), strict=True)) != 1:
            raise ValueError(f'Expected one byte to change: {path}')

    result = save_bundle(next(iter(patch.files.values())), {name for name, _ in targets})
    verify_disabled_renderers(before, UnityPy.load(result), targets)

    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open('xb') as file:
        file.write(result)
    report = {'method': 'disabled five serialized MeshRenderers; did not rebuild the Unity project',
              'sourceSHA256': hashlib.sha256(original).hexdigest(), 'bundleSHA256': hashlib.sha256(result).hexdigest(),
              'disabledRenderers': {path: triangles for _, path, triangles in targets.values()},
              'unchangedObjects': len(list(before.objects)) - len(targets)}
    output.with_name(output.name + '_grass_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('LASTSHIFT_GRASS_BUNDLE_OK', report)


def strip_fbx(group, names):
    from rebuild_lastshift_fbx_layers import child

    models = {model_id: model.props[1].split(b'\0')[0].decode() for model_id, model in group.models.items()}
    targets = {model_id for model_id, name in models.items() if name in names}
    require_targets({models[i] for i in targets}, set(names))
    if len(targets) != len(names):
        raise ValueError(f'{group.path}: duplicate grass model name')
    if targets & set(group.parents.values()):
        raise ValueError(f'{group.path}: a grass model owns child models')
    geos = {group.meshes[i].props[0] for i in targets}
    if len(geos) != len(targets) or any(m.props[0] in geos for i, m in group.meshes.items() if i not in targets):
        raise ValueError(f'{group.path}: grass geometries are shared')
    removed = targets | geos
    objects = child(group.root, b'Objects').elems
    connections = child(group.root, b'Connections').elems
    objects[:] = [obj for obj in objects if obj.props[0] not in removed]
    connections[:] = [conn for conn in connections if not removed.intersection(conn.props[1:3])]
    definitions = child(group.root, b'Definitions')
    child(definitions, b'Count').props[0] -= len(removed)
    for entry in definitions.elems:
        if entry.id == b'ObjectType' and entry.props[0] in (b'Model', b'Geometry'):
            child(entry, b'Count').props[0] -= len(targets)


def patch_fbx(source, output, addon):
    from rebuild_lastshift_fbx_layers import Group, encode, equal, load_blender_tools

    encode_bin, parse_fbx = load_blender_tools(addon)
    output.mkdir(parents=True, exist_ok=False)
    for group, names in GRASS_MESHES.items():
        filename = f'LastShiftYard_{group}.fbx'
        model = Group(source / filename, parse_fbx)
        strip_fbx(model, names)
        with encode_bin.FBXElem.enable_multithreading_cm():
            encoded = encode(model.root, encode_bin.FBXElem)
        result = output / filename
        encode_bin.write(result, encoded, model.version)
        if not equal(model.root, parse_fbx.parse(result)[0], ignore_time=True):
            raise ValueError(f'{result}: non-grass FBX data changed on serialization')
        print(f'LASTSHIFT_GRASS_FBX_OK {filename}: {len(names)} objects removed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('bundle', 'fbx'))
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--fbx-addon', type=Path, default=Path('D:/blender/5.2/scripts/addons_core/io_scene_fbx'))
    args = parser.parse_args()
    if args.source.resolve() == args.output.resolve():
        parser.error('source and output must differ')
    if args.mode == 'bundle':
        patch_bundle(args.source, args.output)
    else:
        patch_fbx(args.source, args.output, args.fbx_addon)


if __name__ == '__main__':
    main()
