"""按原图集中的表面分配水材质，不让瀑布的石壁也出现流动高光。

只拆分面组，位置、三角形、面角 UV 和原图集均不改。水色边界以原画为准；
泡沫与无明确青蓝色的混合边缘留在静态表面，避免把苔藓识别成流水。
"""

from array import array

# 喷泉顶端的实体晶体与流水同为青蓝色，不能依靠颜色区分，保留原静态材质。
SURFACES = {
    'waterfall': ('TripoCascadeStone', 'TripoCascadeWater'),
}
_CACHE = {}


def register(g, name, texture_path):
    for material in SURFACES.get(name, ()):
        g.PALETTE[material] = '#ffffff'
        g.MODEL_TEXTURES[material] = texture_path


def clear():
    _CACHE.clear()


def _faces(g, payload, original_material):
    name = payload['meta']['name']
    if name in _CACHE:
        return _CACHE[name]
    material = g.MATERIALS[original_material]
    images = [node.image for node in material.node_tree.nodes if node.type == 'TEX_IMAGE' and node.image]
    if len(images) != 1:
        raise ValueError('表面分组必须使用该模型唯一的原图集：' + name)
    image = images[0]
    width, height = image.size
    channels = image.channels
    pixels = array('f', [0]) * (width * height * channels)
    image.pixels.foreach_get(pixels)
    uv = payload['mesh']['uv']

    def painted_water(point):
        x = min(width-1, max(0, int(point[0]*width)))
        y = min(height-1, max(0, int(point[1]*height)))
        offset = (y*width+x)*channels
        r, green, blue = pixels[offset:offset+3]
        return green-r > .04 and blue-r > .018 and blue > green*.72

    groups = {SURFACES[name][0]: [], SURFACES[name][1]: []}
    for face in payload['mesh']['f']:
        coordinates = [uv[index] for index in face]
        center = tuple(sum(p[i] for p in coordinates)/len(coordinates) for i in range(2))
        # 用内部样点避开图集岛边缘的黑边；混合三角形保持原有静态画色。
        probes = [center] + [tuple(center[i]*.65+p[i]*.35 for i in range(2)) for p in coordinates]
        watery = sum(painted_water(point) for point in probes) >= len(probes)-1
        groups[SURFACES[name][1 if watery else 0]].append(face)
    if not all(groups.values()):
        raise ValueError('图集未提供可区分的流水和实体表面：' + name)
    _CACHE[name] = groups
    return groups


def _whole_shading(g, payload, vertices):
    """先走原整件的面序/平滑计算，再保留每个源面角的法线。

    分开的水面是开口网格，不能各自 recalc_face_normals；那既会改变
    面序，也会丢失水/石交界的平滑邻接。源件有开口/薄片，旧 bmesh
    的定向选择受摆放影响，因此在实际实例坐标计算，不能只旋转局部缓存。
    """
    name = payload['meta']['name']
    import bpy
    mesh = payload['mesh']
    faces, smooth, removed, collapsed = g.clean_faces(
        vertices, mesh['f'], mesh.get('smooth', True))
    if removed or collapsed:
        raise ValueError('表面分组源模型需先完成退化面清理：' + name)
    obj = g.create_object('SurfaceWhole_' + name, vertices, faces,
                          uv=mesh['uv'], smooth=smooth, hidden=True)
    try:
        data = obj.data
        normals = data.corner_normals
        shading = {}
        for source, polygon in zip(faces, data.polygons):
            if sorted(source) != sorted(polygon.vertices):
                raise ValueError('整件法线计算改变了源面对应关系：' + name)
            # create_object 的 Unity XYZ -> Blender XZY 会反转面序。
            # 在这里还原坐标/面序，最终建组时恰好转换一次。
            loops = list(reversed(polygon.loop_indices))
            shading[tuple(source)] = (
                tuple(data.loops[i].vertex_index for i in loops),
                tuple((normals[i].vector.x, normals[i].vector.z,
                       normals[i].vector.y) for i in loops),
                polygon.use_smooth)
        return shading
    finally:
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(data)


def stamp(g, payload, original_material, vertices):
    if payload['meta']['name'] not in SURFACES:
        return False
    shading = _whole_shading(g, payload, vertices)
    for material, faces in _faces(g, payload, original_material).items():
        prepared = [shading[tuple(face)] for face in faces]
        faces = [row[0] for row in prepared]
        normals = [row[1] for row in prepared]
        used = sorted({index for face in faces for index in face})
        indices = {old: new for new, old in enumerate(used)}
        g.addmesh(material, [vertices[index] for index in used],
                  [tuple(indices[index] for index in face) for face in faces],
                  [payload['mesh']['uv'][index] for index in used],
                  [row[2] for row in prepared], corner_normals=normals)
    return True
