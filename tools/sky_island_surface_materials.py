"""按原图集中的表面给 Tripo 件分出第二种材质：瀑布的流水面、路灯的灯罩玻璃。

只拆分面组，位置、三角形、面角 UV 和原图集均不改。
- 瀑布：水色边界以原画为准；泡沫与无明确青蓝色的混合边缘留在静态表面，避免把苔藓识别成流水。
- 路灯（2026-10-01）：灯罩玻璃在图集里是画出来的不透明暖色面，原先在灯罩里塞的发光灯芯被挡住，夜里灯不亮。
  灯头高度带内、图集中位亮度高于 LAMP_GLASS_LUMA 的面分进带自发光的「玻璃」材质（着色器按日照压暗，白天约三成、
  星夜全亮）；玻璃面中心同时登记成夜灯灯位（生成器 LAMP_LIGHTS → 场景标记 NightLamp_*，运行时照亮周围）。
  实测两件灯的玻璃面中位亮度都 ≥ 180/255，灯头其余部分（铜框、灯帽、藤叶）都 ≤ 155/255。

法线（2026-09-30 第二轮）：先在整件上按 sky_island_tripo_props.sector_normals 焊接、定向、求平滑扇区法线，
再按面分给水、石两组，交界处两侧法线逐位相同。原先在图集接缝拆开的顶点上各自平滑，
平滑面上留下 9–11% 的折痕（`Build/sky-material-audit-20260930/material_audit_round2.md` R1）。
"""

from array import array

# 模型 → (静态面材质, 分出去的面材质)。喷泉顶端的实体晶体与流水同为青蓝色，不能依靠颜色区分，保留原静态材质。
SURFACES = {
    'waterfall': ('TripoCascadeStone', 'TripoCascadeWater'),
    'street_lamp': ('TripoStreetLamp', 'TripoStreetLampGlass'),
    'brass_lamp_post': ('TripoBrassLampPost', 'TripoBrassLampPostGlass'),
}
# 灯头高度带（占模型高度的比例）：路灯的灯笼挂在弯臂上，铜灯柱的灯罩在杆顶。
LAMP_HEAD_BAND = {'street_lamp': (.60, .80), 'brass_lamp_post': (.76, .90)}
LAMP_GLASS_LUMA = 170 / 255
LAMP_GLASS_EMISSION = 1.0
LAMP_GLASS_COLOR = '#ffc27a'
_CACHE = {}


def register(g, name, texture_path):
    for material in SURFACES.get(name, ()):
        g.PALETTE[material] = '#ffffff'
        g.MODEL_TEXTURES[material] = texture_path
    if name in LAMP_HEAD_BAND:
        glass = SURFACES[name][1]
        g.EMISSION[glass] = LAMP_GLASS_EMISSION
        g.EMISSION_RGBA[glass] = LAMP_GLASS_COLOR


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

    def colour(point):
        x = min(width-1, max(0, int(point[0]*width)))
        y = min(height-1, max(0, int(point[1]*height)))
        offset = (y*width+x)*channels
        return pixels[offset:offset+3]

    def painted_water(point):
        r, green, blue = colour(point)
        return green-r > .04 and blue-r > .018 and blue > green*.72

    verts = payload['mesh']['v']
    top = max(p[1] for p in verts) or 1.0
    band = LAMP_HEAD_BAND.get(name)
    groups = {SURFACES[name][0]: [], SURFACES[name][1]: []}
    for face in payload['mesh']['f']:
        coordinates = [uv[index] for index in face]
        center = tuple(sum(p[i] for p in coordinates)/len(coordinates) for i in range(2))
        # 用内部样点避开图集岛边缘的黑边；混合三角形保持原有静态画色。
        probes = [center] + [tuple(center[i]*.65+p[i]*.35 for i in range(2)) for p in coordinates]
        if band is None:
            split = sum(painted_water(point) for point in probes) >= len(probes)-1
        else:
            height_fraction = sum(verts[index][1] for index in face) / len(face) / top
            lumas = sorted(.299*r + .587*green + .114*blue for r, green, blue in (colour(point) for point in probes))
            split = band[0] <= height_fraction <= band[1] and lumas[len(lumas)//2] > LAMP_GLASS_LUMA
        groups[SURFACES[name][1 if split else 0]].append(face)
    if not all(groups.values()):
        raise ValueError('图集未提供可区分的两种表面：' + name)
    _CACHE[name] = groups
    return groups


def lamp_centre(g, payload, original_material, vertices):
    """灯罩玻璃面的面积加权中心（实例世界坐标）；不是灯返回 None。"""
    name = payload['meta']['name']
    if name not in LAMP_HEAD_BAND:
        return None
    glass = _faces(g, payload, original_material)[SURFACES[name][1]]
    total, centre = 0.0, [0.0, 0.0, 0.0]
    for face in glass:
        a = vertices[face[0]]
        for k in range(1, len(face) - 1):
            b, c = vertices[face[k]], vertices[face[k + 1]]
            u = [b[i] - a[i] for i in range(3)]; v = [c[i] - a[i] for i in range(3)]
            cross = (u[1]*v[2] - u[2]*v[1], u[2]*v[0] - u[0]*v[2], u[0]*v[1] - u[1]*v[0])
            area = (cross[0]**2 + cross[1]**2 + cross[2]**2) ** .5 / 2
            for i in range(3):
                centre[i] += (a[i] + b[i] + c[i]) / 3 * area
            total += area
    return tuple(value / total for value in centre) if total > 0 else None


def _whole_shading(g, payload, vertices):
    """整件（实例世界坐标）求一次平滑扇区法线，按源面给出 (定向后的面, 逐角法线)。

    分开的水面是开口网格，不能各自定向、各自平滑；那会改变面序，也会丢失水 / 石交界的平滑邻接。
    源件有开口 / 薄片，定向在实际实例坐标上做，不用局部缓存。
    """
    import sky_island_tripo_props
    name = payload['meta']['name']
    mesh = payload['mesh']
    faces, _smooth, removed, collapsed = g.clean_faces(vertices, mesh['f'], True)
    if removed or collapsed:
        raise ValueError('表面分组源模型需先完成退化面清理：' + name)
    kept, oriented, normals = sky_island_tripo_props.sector_normals(vertices, faces)
    return {tuple(faces[index]): (face, row) for index, face, row in zip(kept, oriented, normals)}


def stamp(g, payload, original_material, vertices):
    if payload['meta']['name'] not in SURFACES:
        return False
    import sky_island_tripo_props
    uv = payload['mesh']['uv']
    shading = _whole_shading(g, payload, vertices)
    for material, faces in _faces(g, payload, original_material).items():
        prepared = [shading[tuple(face)] for face in faces if tuple(face) in shading]
        faces, normals = sky_island_tripo_props._world_clean(
            g, vertices, [row[0] for row in prepared], [row[1] for row in prepared], uv)
        used = sorted({index for face in faces for index in face})
        indices = {old: new for new, old in enumerate(used)}
        g.addmesh(material, [vertices[index] for index in used],
                  [tuple(indices[index] for index in face) for face in faces],
                  [uv[index] for index in used],
                  [True] * len(faces), corner_normals=normals)
    centre = lamp_centre(g, payload, original_material, vertices)
    if centre is not None:
        g.lamp_light('glass_' + payload['meta']['name'], centre)
    return True
