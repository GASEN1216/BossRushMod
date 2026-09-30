"""消除生成件的零长边/零面积面；不焊接 UV 岛，不接触导航或碰撞。

球极点和半径为零的旋转截面原来输出退化四边形。仅折叠同一面的
相邻重合角点，保留有效三角和原顶点/UV 索引；不删正反双面叶片。
同绕序重复面只在顶点索引一致或显式传入 UV 且位置/UV 一致时去重。
"""

from struct import pack, unpack


def clean_faces(vertices, faces, smooth=False, uv=None):
    # Blender/FBX 坐标缓冲是 float32。世界远端的共线小叶在 double 中可能
    # 残留极小面积，写入网格才归零；按最终坐标精度判定，避免 443 个空三角。
    points = [unpack('fff', pack('fff', *p)) for p in vertices]
    clean, shading = [], []
    removed = collapsed = 0
    seen = set()
    if uv is not None and len(uv) != len(vertices):
        raise ValueError("UV must match every source vertex")
    for index, face in enumerate(faces):
        corners = []
        for vertex in face:
            if not corners or _distance_squared(points[vertex], points[corners[-1]]) > 1e-14:
                corners.append(vertex)
            else:
                collapsed += 1
        if len(corners) > 1 and _distance_squared(points[corners[0]], points[corners[-1]]) <= 1e-14:
            corners.pop()
            collapsed += 1
        if len(corners) < 3 or not _has_area(points, corners):
            removed += 1
            continue
        shade = smooth[index] if isinstance(smooth, (list, tuple)) else smooth
        # UV 接缝不焊接；反绕序保留，循环移位仍属于同一面。
        # 未提供 UV 时只认相同索引，不能猜同位置点的贴图是否相同。
        attributes = tuple((points[i], tuple(uv[i])) for i in corners) if uv is not None else tuple(corners)
        key = (shade, _oriented_face_key(attributes))
        if key in seen:
            removed += 1
            continue
        seen.add(key)
        clean.append(tuple(corners))
        shading.append(shade)
    return clean, shading, removed, collapsed


def _distance_squared(a, b):
    return sum((a[i] - b[i]) ** 2 for i in range(3))


def _has_area(vertices, corners):
    origin = vertices[corners[0]]
    for i in range(1, len(corners) - 1):
        a = tuple(vertices[corners[i]][j] - origin[j] for j in range(3))
        b = tuple(vertices[corners[i + 1]][j] - origin[j] for j in range(3))
        cross = (a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0])
        if sum(v*v for v in cross) > 1e-18:
            return True
    return False


def _oriented_face_key(corners):
    """循环面序归一；不排序角点，否则会把双面薄片的背面误删。"""
    return min(corners[i:] + corners[:i] for i in range(len(corners)))
