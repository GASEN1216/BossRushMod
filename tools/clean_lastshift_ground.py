"""Update the Last Shift Yard worn-ground alpha and yard atlas in existing assets.

Uses the production atlas draw list, Node, Pillow, NumPy, UnityPy, and Blender's
pure-Python FBX codec. Give a fresh output directory. The Unity and Blender
projects are not opened, and the input files are retained.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

from clean_lastshift_ground_atlas import repair_atlas
from lastshift_ground_finish import WORN_GROUND_MESHES


ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'ArtSource/LastShiftYard'
ATLAS = 'yard_ground_atlas.png'
PROPS = 'LastShiftYard_props.fbx'
BUNDLE = 'lastshift_raid'
# The fallback delta is calibrated to this original Chromium raster. Future
# source exports already omit the paint and do not need this offline replay.
ORIGINAL_ATLAS_SHA256 = 'a563bb74f0e45c2982640b125fb36574d77616b4ca8eeb6992e4af94b787b0d1'


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def pixels(path):
    with Image.open(path) as img:
        return np.array(img.convert('RGBA'))


def bundle_assets(env):
    from rebuild_lastshift_bundle_layers import key, transform

    meshes, consumers, texture, cache = {}, {}, [], {}
    for obj in env.objects:
        if obj.type.name == 'Texture2D' and obj.read().m_Name == 'yard_ground_atlas':
            texture.append(obj)
        if obj.type.name != 'MeshRenderer':
            continue
        renderer = obj.read()
        go = renderer.m_GameObject.read()
        material = [r.read() for r in renderer.m_Materials if r]
        if go.m_Name not in WORN_GROUND_MESHES:
            continue
        components = {p.component.deref().type.name: p.component for p in go.m_Component}
        _, path = transform(components['Transform'], cache)
        if (not path.startswith('/LastShiftYardWorld/Visual/VIS_props/LastShift / eight yard pockets/')
                or set(components) != {'MeshRenderer', 'MeshFilter', 'Transform'}
                or not renderer.m_Enabled or len(material) != 1
                or material[0].m_CustomRenderQueue != 3000):
            raise ValueError('Unexpected worn-ground renderer ' + path)
        props = dict(material[0].m_SavedProperties.m_Floats)
        if tuple(props.get(k) for k in ('_Decal', '_ZWrite', '_SrcBlend', '_DstBlend')) != (1, 0, 5, 10):
            raise ValueError('Worn-ground transparency contract changed: ' + path)
        mesh = components['MeshFilter'].read().m_Mesh.deref()
        if mesh.read().m_Name != go.m_Name or go.m_Name in meshes:
            raise ValueError('Unexpected worn-ground mesh ' + path)
        meshes[go.m_Name] = mesh
        consumers[key(mesh)] = 0
    if set(meshes) != WORN_GROUND_MESHES or len(texture) != 1:
        raise ValueError('Worn-ground meshes or yard atlas missing')
    for obj in env.objects:
        if obj.type.name not in ('MeshFilter', 'MeshCollider', 'SkinnedMeshRenderer'):
            continue
        ref = obj.read().m_Mesh
        if ref:
            k = key(ref.deref())
            if k in consumers:
                consumers[k] += 1
    if any(count != 1 for count in consumers.values()):
        raise ValueError('A worn-ground mesh is shared by another renderer or collider')
    return meshes, texture[0]


def mesh_alpha(obj):
    from UnityPy.helpers.MeshHelper import MeshHandler
    from rebuild_lastshift_bundle_layers import channel_view

    handler = MeshHandler(obj.read())
    vertex = handler.src.m_VertexData
    data = bytearray(vertex.m_DataSize)
    if vertex.m_Channels[3].dimension != 4 or handler.src.m_StreamData.size:
        raise ValueError('Expected embedded RGBA vertex stream: ' + handler.src.m_Name)
    alpha = channel_view(data, handler, vertex.m_Channels)[:, 3]
    return data, alpha


def replace_bytes(obj, old, new):
    original = obj.get_raw_data()
    offset = original.find(old)
    if len(old) != len(new) or offset < 0 or original.find(old, offset+1) != -1:
        raise ValueError('Cannot find a unique same-length payload in ' + str(obj.path_id))
    return original[:offset] + new + original[offset+len(old):]


def atlas_payload(tex, old, new):
    if (tex.m_TextureFormat != 4 or tex.m_StreamData.size != 0 or tex.m_ImageCount != 1
            or old.shape != new.shape or old.shape != (tex.m_Height, tex.m_Width, 4)):
        raise ValueError('Yard atlas must be a single embedded RGBA32 texture at its authored size')
    if not np.array_equal(old[:, :, 3], new[:, :, 3]):
        raise ValueError('Yard atlas alpha changed')
    changed, result, offset = [], bytearray(tex.image_data), 0
    width, height = tex.m_Width, tex.m_Height
    old_image, new_image = (Image.fromarray(a) for a in (old, new))
    for mip in range(tex.m_MipCount):
        size = width*height*4
        current = np.frombuffer(tex.image_data[offset:offset+size], dtype='u1').reshape(height, width, 4)[::-1]
        if mip == 0 and not np.array_equal(current, old):
            raise ValueError('Bundle yard atlas does not match its original source PNG')
        if mip == 0:
            edited = new
        else:
            a, b = (np.array(image.resize((width, height), Image.Resampling.BOX), dtype=np.int16)
                    for image in (old_image, new_image))
            # Apply the new contribution to Unity's existing mipmap. Preserve
            # all original texels where the downsampled paint did not change.
            edited = np.clip(current.astype(np.int16) + b - a, 0, 255).astype(np.uint8)
        changed.append(int(np.count_nonzero(np.any(current != edited, axis=2))))
        result[offset:offset+size] = edited[::-1].tobytes()
        offset += size
        width, height = max(1, width//2), max(1, height//2)
    if offset != len(tex.image_data) or offset != tex.m_CompleteImageSize:
        raise ValueError('Unexpected mip chain length')
    return result, changed


def check_bundle(env, atlas):
    meshes, texture = bundle_assets(env)
    for name, obj in meshes.items():
        if np.any(mesh_alpha(obj)[1]):
            raise ValueError('Worn-ground tint is still visible: ' + name)
    if not np.array_equal(np.asarray(texture.read().image), atlas):
        raise ValueError('Bundle yard atlas differs from the cleaned source PNG')


def patch_bundle(source, output, old_atlas, new_atlas):
    import UnityPy
    from rebuild_lastshift_bundle_layers import key, save_bundle, verified_changed_objects

    raw = source.read_bytes()
    before, edited = UnityPy.load(raw), UnityPy.load(raw)
    meshes, texture = bundle_assets(edited)
    expected, triangles = {}, {}
    for name, obj in meshes.items():
        data, alpha = mesh_alpha(obj)
        if np.any(alpha):
            old = bytes(data)
            alpha[:] = 0
            expected[key(obj)] = replace_bytes(obj, old, data)
            obj.set_raw_data(expected[key(obj)])
        triangles[name] = sum(sub.indexCount//3 for sub in obj.read().m_SubMeshes)
    tex = texture.read()
    mip_changes = [0]*tex.m_MipCount
    if not np.array_equal(np.asarray(tex.image), new_atlas):
        payload, mip_changes = atlas_payload(tex, old_atlas, new_atlas)
        expected[key(texture)] = replace_bytes(texture, tex.image_data, payload)
        texture.set_raw_data(expected[key(texture)])
    result = (save_bundle(next(iter(edited.files.values())), {name for name, _ in expected})
              if expected else raw)
    reopened = UnityPy.load(result)
    for obj_key, _, obj in verified_changed_objects(before, reopened, expected):
        if obj.get_raw_data() != expected[obj_key]:
            raise ValueError('Unexpected serialized object change: ' + str(obj_key))
    check_bundle(reopened, new_atlas)
    with output.open('xb') as file:
        file.write(result)
    return {'sourceBundleSHA256': sha(raw), 'bundleSHA256': sha(result),
            'clearedTrianglesByMesh': triangles, 'changedTextureTexelsByMip': mip_changes,
            'changedObjects': len(expected), 'unchangedObjects': len(list(before.objects))-len(expected)}


def fbx_colors(group):
    from rebuild_lastshift_fbx_layers import child, properties

    found = {}
    for model_id, mesh in group.meshes.items():
        model = group.models[model_id]
        name = model.props[1].split(b'\0')[0].decode()
        if name not in WORN_GROUND_MESHES:
            continue
        if name in found or b'lsy_decal' not in properties(model) or sum(x is mesh for x in group.meshes.values()) != 1:
            raise ValueError('Unexpected FBX worn-ground mesh: ' + name)
        found[name] = child(child(mesh, b'LayerElementColor'), b'Colors').props[0]
    if found.keys() != WORN_GROUND_MESHES:
        raise ValueError('FBX worn-ground mesh missing')
    return found


def patch_fbx(source, output, addon):
    from rebuild_lastshift_fbx_layers import Group, encode, equal, load_blender_tools

    encoder, parser = load_blender_tools(addon)
    group = Group(source, parser)
    colors = fbx_colors(group)
    if not any(np.any(np.asarray(data)[3::4]) for data in colors.values()):
        with output.open('xb') as file:
            file.write(source.read_bytes())
        return
    for data in colors.values():
        np.asarray(data)[3::4] = 0
    with encoder.FBXElem.enable_multithreading_cm():
        tree = encode(group.root, encoder.FBXElem)
    encoder.write(output, tree, group.version)
    readback = Group(output, parser)
    if not equal(group.root, readback.root, ignore_time=True):
        raise ValueError('FBX write changed data other than the requested alpha')
    for data in fbx_colors(readback).values():
        if np.any(np.asarray(data)[3::4]):
            raise ValueError('FBX worn-ground mesh still has visible alpha')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--source-atlas', type=Path, default=ART / 'export/tex' / ATLAS)
    parser.add_argument('--source-bundle', type=Path, default=ROOT / 'Assets/arenas' / BUNDLE)
    parser.add_argument('--source-fbx', type=Path, default=ART / 'fbx' / PROPS)
    parser.add_argument('--fbx-addon', type=Path, default=Path('D:/blender/5.2/scripts/addons_core/io_scene_fbx'))
    parser.add_argument('--clean-atlas', type=Path, help='Use an already cleaned production PNG; skip the legacy paint delta')
    args = parser.parse_args()
    if not args.clean_atlas and sha(args.source_atlas.read_bytes()) != ORIGINAL_ATLAS_SHA256:
        parser.error('Legacy atlas replay requires the original PNG; use --clean-atlas for a clean export')
    args.output.mkdir(parents=True, exist_ok=False)
    if args.clean_atlas:
        (args.output / ATLAS).write_bytes(args.clean_atlas.read_bytes())
        report = {'cleanAtlasReplayed': str(args.clean_atlas)}
    else:
        report = repair_atlas(args.source_atlas, args.output / ATLAS, args.output)
    report.update(patch_bundle(args.source_bundle, args.output / BUNDLE,
                               pixels(args.source_atlas), pixels(args.output / ATLAS)))
    patch_fbx(args.source_fbx, args.output / PROPS, args.fbx_addon)
    report.update({'method': 'offline object payload patch; no Unity rebuild or Blender scene regeneration',
                   'sourceAtlasSHA256': sha(args.source_atlas.read_bytes()),
                   'atlasSHA256': sha((args.output / ATLAS).read_bytes()),
                   'sourceFbxSHA256': sha(args.source_fbx.read_bytes()),
                   'fbxSHA256': sha((args.output / PROPS).read_bytes())})
    (args.output / 'ground_finish_report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('LASTSHIFT_GROUND_CLEAN_OK', report)


if __name__ == '__main__':
    main()
