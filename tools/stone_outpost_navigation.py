"""将单位格导航无损压缩成共用分割线的矩形网格，不改变可行走区域。"""


def build_navigation(cells, size, max_vertices=4095):
    cells = set(cells)
    if not cells or any(x < 0 or y < 0 or x >= size or y >= size for x, y in cells):
        raise ValueError('导航格子为空或超出边界')
    # 只有通行状态改变的列/行需要保留；各矩形共用完整切分线，避免 T 形断边。
    xcuts = [x for x in range(size + 1)
             if any(((x - 1, y) in cells) != ((x, y) in cells) for y in range(size))]
    ycuts = [y for y in range(size + 1)
             if any(((x, y - 1) in cells) != ((x, y) in cells) for x in range(size))]
    vertices, triangles, shared = [], [], {}
    covered = set()

    def vertex(x, y):
        key = (x, y)
        if key not in shared:
            shared[key] = len(vertices)
            vertices.append(key)
        return shared[key]

    for x0, x1 in zip(xcuts, xcuts[1:]):
        for y0, y1 in zip(ycuts, ycuts[1:]):
            if (x0, y0) not in cells:
                continue
            rectangle = {(x, y) for x in range(x0, x1) for y in range(y0, y1)}
            if not rectangle <= cells:
                raise ValueError('导航压缩跨入障碍区域')
            covered.update(rectangle)
            a, b, c, d = vertex(x0, y0), vertex(x1, y0), vertex(x1, y1), vertex(x0, y1)
            triangles.extend([(a, b, c), (a, c, d)])
    if covered != cells:
        raise ValueError('导航压缩丢失可行走区域')
    if len(vertices) > max_vertices:
        raise ValueError('超过游戏 A* 顶点上限：%d > %d' % (len(vertices), max_vertices))
    return vertices, triangles
