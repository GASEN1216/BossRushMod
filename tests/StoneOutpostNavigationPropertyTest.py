"""导航压缩必须逐格保持通行域，保留三角形连接，并拒绝超过游戏顶点上限的输入。"""
from pathlib import Path
from collections import Counter
import random
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from stone_outpost_navigation import build_navigation


def boundary(cells):
    result = Counter()
    for x, y in cells:
        for dx, dy, a, b in [(0,-1,(x,y),(x+1,y)),(0,1,(x,y+1),(x+1,y+1)),
                              (-1,0,(x,y),(x,y+1)),(1,0,(x+1,y),(x+1,y+1))]:
            if (x+dx,y+dy) not in cells:
                result[tuple(sorted((a,b)))] += 1
    return result


def check(cells, size):
    vertices, triangles = build_navigation(cells, size)
    edges = Counter()
    area = 0
    for triangle in triangles:
        a,b,c=[vertices[i] for i in triangle]
        cross=(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
        assert cross > 0, '三角形翻面或退化'
        area += cross / 2
        for i,j in zip(triangle,triangle[1:]+triangle[:1]):
            edges[tuple(sorted((i,j)))] += 1
    actual = Counter()
    for (i,j),count in edges.items():
        assert count in (1,2), '出现非流形边'
        if count == 2:
            continue
        a,b=vertices[i],vertices[j]
        assert a[0]==b[0] or a[1]==b[1], '边界跨越障碍或出现 T 形断边'
        x,y=a
        dx=0 if a[0]==b[0] else (1 if b[0]>a[0] else -1)
        dy=0 if a[1]==b[1] else (1 if b[1]>a[1] else -1)
        while (x,y)!=b:
            end=(x+dx,y+dy)
            actual[tuple(sorted(((x,y),end)))] += 1
            x,y=end
    assert area==len(cells), '通行面积改变'
    assert actual==boundary(cells), '外边界、障碍孔洞或窄通道改变'


def main():
    rng=random.Random(9084095)
    cases=[{(x,y) for x in range(96) for y in range(96)},
           {(x,y) for x in range(20) for y in range(20) if not (4<x<14 and 4<y<14)}]
    for cells in cases:
        check(cells,96)
    for _ in range(100):
        cells={(x,y) for x in range(16) for y in range(16) if rng.random()>.25}
        check(cells,16)
    try:
        build_navigation({(x,y) for x in range(96) for y in range(96) if (x+y)%2==0},96)
        raise AssertionError('超限输入未被拒绝')
    except ValueError as error:
        assert '上限' in str(error)
    print('StoneOutpostNavigationPropertyTest: PASS (102 layouts + vertex limit)')


if __name__ == '__main__':
    main()
