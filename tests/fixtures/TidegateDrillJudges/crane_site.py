"""有作者资源时，核对实景吊机、落区和港区可绕通路。公开签出仍能运行 C# 的落货周期与致死边界。"""
from collections import deque
import json
import math
import struct


def verify_site(folder, layout):
    with (folder / "tidegate_props.glb").open("rb") as stream:
        stream.seek(12)
        length, kind = struct.unpack("<II", stream.read(8))
        assert kind == 0x4e4f534a
        nodes = json.loads(stream.read(length))["nodes"]
    matrix = next(n["matrix"] for n in nodes if n.get("name") == "PROP_dockCrane_-44_37")
    # GLTF 列主序矩阵；作者吊臂的末端，局部 x=4.6，吊钩的 z=0.04。导航栅格沿用作者 z。
    x = matrix[12] + 4.6 * matrix[0] + 0.04 * matrix[8]
    z = matrix[14] + 4.6 * matrix[2] + 0.04 * matrix[10]
    assert abs(x + 48.6) < 0.001 and abs(z - 36.96) < 0.001, "吊机或吊臂已移动"
    hx, hz = 0.65, 0.80
    g = layout["grid"]
    w, h = g["W"], g["H"]
    heights = struct.unpack("<" + str(w*h) + "f", (folder/g["heightFile"]).read_bytes())
    flags = (folder/g["flagFile"]).read_bytes()
    def dry(a, b):
        i = (b-g["OZ"])*w + a-g["OX"]
        return heights[i] > 0.5 and not flags[i] & (g["flags"]["WALL"] | g["flags"]["PROP"])
    for a in range(math.floor(x-hx), math.floor(x+hx)+1):
        for b in range(math.floor(z-hz), math.floor(z+hz)+1):
            assert dry(a, b), "落区占了实心道具或深水"
    def clearance(pos):
        return math.hypot(max(abs(pos[0]-x)-hx, 0), max(abs(pos[2]-z)-hz, 0))
    assert min(clearance(box["position"]) for box in layout["containers"]) > 1, "占了普通箱"
    assert all(clearance(e["position"]) > e["radius"] + 2 for e in layout["exits"]), "占了撤离环"
    def steps(end, avoid):
        start = (-59, 31)
        todo = deque([(start, 0)]); seen = {start}
        while todo:
            (a, b), n = todo.popleft()
            if (a, b) == end: return n
            for p, q in ((a+1,b), (a-1,b), (a,b+1), (a,b-1)):
                if not (-65 <= p <= -32 and 28 <= q <= 39) or (p,q) in seen or not dry(p,q): continue
                if avoid and abs(p+.5-x) <= hx+.75 and abs(q+.5-z) <= hz+.75: continue
                seen.add((p,q)); todo.append(((p,q), n+1))
        return None
    results = [(steps(dest, False), steps(dest, True)) for dest in ((-40,36), (-47,36))]
    assert all(unblocked is not None and unblocked == blocked for unblocked, blocked in results), "港区通行或鱼箱安全互动侧受阻"
    print("Tidegate crane:", (round(x, 2), round(-z, 2)), "port and fish box routes with/without 0.75m clearance:", results)
