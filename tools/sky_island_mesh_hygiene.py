"""消除生成件的零长边/零面积面；不焊接 UV 岛，不接触导航或碰撞。

球极点和半径为零的旋转截面原来输出退化四边形。仅折叠同一面的
相邻重合角点，保留有效三角和原顶点/UV 索引；不删正反双面叶片。
"""

from struct import pack, unpack


def clean_faces(vertices, faces, smooth=False):
    # Blender/FBX 坐标缓冲是 float32。世界远端的共线小叶在 double 中可能
    # 残留极小面积，写入网格才归零；按最终坐标精度判定，避免 443 个空三角。
    points = [unpack('fff', pack('fff', *p)) for p in vertices]
    clean, shading = [], []
    removed = collapsed = 0
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
        clean.append(tuple(corners))
        shading.append(smooth[index] if isinstance(smooth, (list, tuple)) else smooth)
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
