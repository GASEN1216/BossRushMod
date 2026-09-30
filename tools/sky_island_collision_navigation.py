"""Shared authoring contract for the collision-aware navigation sidecar."""
from collections.abc import Mapping
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read_collision_policy(project):
    """Read the author's current material and group exclusions; keep no policy table here."""
    path = Path(project) / 'Assets/Editor/SkyIslandCollisionBuilder.cs'
    source = path.read_text(encoding='utf-8-sig')
    table = re.search(
        r'SoftMaterials\s*=\s*new\s+HashSet<string>\s*\(StringComparer\.Ordinal\)\s*\{([^{}]*)\}\s*;',
        source)
    try:
        # The author initializer is a list of plain string literals. Reject unsupported
        # syntax rather than silently classifying solids with an incomplete table.
        soft = json.loads('[' + table.group(1).strip().rstrip(',') + ']') if table else None
        start = source.index('public static bool NeedsCollision')
        guard = source[start:source.index('MeshRenderer renderer', start)]
    except (ValueError, AttributeError) as error:
        raise ValueError('Cannot read collision classification: ' + str(path)) from error
    required = re.findall(r'!source\.name\.StartsWith\("([^"\\]+)",\s*StringComparison\.Ordinal\)', guard)
    prefixes = re.findall(r'(?<!!)source\.name\.StartsWith\("([^"\\]+)",\s*StringComparison\.Ordinal\)', guard)
    tags = re.findall(r'source\.name\.IndexOf\("([^"\\]+)",\s*StringComparison\.Ordinal\)\s*>=\s*0', guard)
    suffixes = re.findall(r'source\.name\.EndsWith\("([^"\\]+)",\s*StringComparison\.Ordinal\)', guard)
    if (not isinstance(soft, list) or not soft or not all(isinstance(name, str) for name in soft)
            or len(set(soft)) != len(soft) or len(required) != 1 or not prefixes or not tags or not suffixes):
        raise ValueError('Cannot read collision classification: ' + str(path))
    return {'soft_materials': set(soft), 'required_prefix': required[0],
            'excluded_prefixes': tuple(prefixes), 'excluded_tags': tuple(tags),
            'excluded_suffixes': tuple(suffixes)}


def production_solid_names(objects, policy):
    """Mirror NeedsCollision for Blender meshes, including mixed/missing errors."""
    rows = objects.values() if isinstance(objects, Mapping) else objects
    names = []
    for obj in rows:
        name = obj.name
        if (obj.type != 'MESH' or obj.data is None or not name.startswith(policy['required_prefix'])
                or name.startswith(policy['excluded_prefixes']) or name.endswith(policy['excluded_suffixes'])
                or any(tag in name for tag in policy['excluded_tags'])):
            continue
        solid = None
        for material in obj.data.materials:
            if material is None:
                raise ValueError('Missing collision material: ' + name)
            material_name = material.name.replace(' (Instance)', '').removeprefix('Sky_')
            current = material_name not in policy['soft_materials']
            if solid is not None and solid != current:
                raise ValueError('Mixed solid/soft collision group: ' + name)
            solid = current
        if solid is None:
            raise ValueError('Visual has no collision material classification: ' + name)
        if solid:
            names.append(name)
    return sorted(names)


def collision_signature(objects, names):
    digest = hashlib.sha256()
    for name in sorted(names):
        obj = objects.get(name)
        if obj is None:
            raise ValueError('Collision source changed; rebuild navigation: ' + name)
        mesh = obj.data
        mesh.calc_loop_triangles()
        digest.update(name.encode())
        for vertex in mesh.vertices:
            point = obj.matrix_world @ vertex.co
            digest.update(str(tuple(round(c, 4) for c in (float(point.x), float(point.z), float(point.y)))).encode())
        for triangle in mesh.loop_triangles:
            digest.update(str(tuple(triangle.vertices)).encode())
    return digest.hexdigest()


def validate_collision_sources(data, objects, project):
    # Re-enumerate with the current author policy. Hashing the old list alone misses
    # new solids, including previously soft groups; bridge solids belong here too.
    names = production_solid_names(objects, read_collision_policy(project))
    recorded = data.get('sourceSolids')
    if (not isinstance(recorded, list) or not all(isinstance(name, str) for name in recorded)
            or sorted(recorded) != names):
        raise ValueError('Solid source set changed; rebuild collision navigation')
    if collision_signature(objects, names) != data['sourceCollisionSha256']:
        raise ValueError('Solid geometry changed; rebuild collision navigation')


def apply_navigation(generator, layout, assets):
    import bpy
    path = ROOT / 'ArtSource/SkyIsland/collision_navigation.json'
    if not path.is_file():
        raise FileNotFoundError('Collision-aware navigation must be rebuilt before producing this map: ' + str(path))
    data = json.loads(path.read_text(encoding='utf-8'))
    layout_path = Path(assets) / 'sky_island_layout.json'
    if hashlib.sha256(layout_path.read_bytes()).hexdigest() != data['sourceLayoutSha256']:
        raise ValueError('Layout changed; rebuild collision navigation')
    validate_collision_sources(data, bpy.data.objects, Path(assets).resolve().parent.parent)
    nav = data['navigation']
    if not 0 < len(nav['vertices']) < 4095 or data['connectedTriangles'] != len(nav['triangles']):
        raise ValueError('Collision navigation exceeds budget or has disconnected triangles')
    old = bpy.data.objects.get('NAV_SkyIsland')
    if old is not None:
        mesh = old.data
        bpy.data.objects.remove(old, do_unlink=True)
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    generator.create_object('NAV_SkyIsland', nav['vertices'], nav['triangles'], hidden=True)
    (Path(assets) / 'sky_island_collision_navigation.json').write_bytes(path.read_bytes())
    return nav
