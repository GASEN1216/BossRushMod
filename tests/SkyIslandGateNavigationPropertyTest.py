"""以生产门参数、真实导航面和生产 AABB 封锁规则验证 32 种开闭组合。

同时检查物理门 OBB 覆盖整座桥的横截面，防止 A* 被保守封死但玩家仍可绕栏。
几何/图属性回归，不替代 Unity 动态碰撞、敌人实机移动与 A* 工作项执行。
"""
from collections import defaultdict, deque
from pathlib import Path
import copy
import json
import math
import re

from cs_source_util import clean_source
from SkyIslandNavigationPropertyTest import inside, height, xz

ROOT = Path(__file__).resolve().parents[1]
NUMBER = r"[-+]?(?:\d+(?:\.\d*)?|\.\d+)[fF]?"
BRIDGES = {"K1": "K1", "K2": "K2", "K3": "K3", "BellCourt": "EH", "ZhelingPass": "FG"}


def number(value):
    return float(value.rstrip("fF"))


def read_gates():
    source = clean_source((ROOT / "DebugAndTools/SkyIsland/SkyIslandGates.cs").read_text(encoding="utf-8-sig"))
    pattern = (r'Add\(root, wood, wallLayer, "([^"\n]+)", new Vector3\((' + NUMBER + r'),\s*(' + NUMBER +
               r'),\s*(' + NUMBER + r')\),\s*(' + NUMBER + r'),\s*(' + NUMBER + r')\)')
    result = {}
    for gate_id, x, y, z, yaw, width in re.findall(pattern, source):
        assert gate_id not in result, "重复门参数 " + gate_id
        result[gate_id] = {"position": [number(x), number(y), number(z)], "yaw": number(yaw), "width": number(width)}
    assert set(result) == set(BRIDGES), "必须解析完整的五扇生产门"
    # 只在能够解释生产 collider 与 A* 规则时运行几何回归；绑定变化必须同步模型。
    assert "blocker.center = Vector3.up * 3" in source
    assert "blocker.size = new Vector3(width + 3, 8, 1.8f)" in source
    assert "root.transform.localRotation = Quaternion.Euler(0, yaw, 0)" in source
    navigation = clean_source((ROOT / "DebugAndTools/ArenaPrototype/ArenaPrototypeNavigation.cs").read_text(encoding="utf-8-sig"))
    for token in ("triangle.GetVertex(0)", "triangle.GetVertex(1)", "triangle.GetVertex(2)",
                  "bounds.Intersects(area)", "node.Walkable = original && !blocked"):
        assert token in navigation, "生产导航封锁算法变化，需同步属性模型 " + token
    return result


def verify_bridge_notices(layout):
    source = clean_source((ROOT / "DebugAndTools/SkyIsland/SkyIslandGates.cs").read_text(encoding="utf-8-sig"))
    pattern = (r'AddSign\(root, wood, wallLayer, "([^"\n]+)", new Vector3\((' + NUMBER + r'),\s*(' +
               NUMBER + r'),\s*(' + NUMBER + r')\)\)')
    rows = re.findall(pattern, source)
    assert len(rows) == 10, "五桥必须各有两端木牌"
    bridges = {bridge["id"]: bridge for bridge in layout["bridges"]}
    nav = layout["navigation"]
    covered = set()
    for gate_id, x, y, z in rows:
        assert gate_id in BRIDGES, "木牌没有对应门 " + gate_id
        point = [number(x), number(y), number(z)]
        bridge = bridges[BRIDGES[gate_id]]
        endpoint = min((0, -1), key=lambda i: math.dist(point, bridge["path"][i]))
        assert math.dist(point, bridge["path"][endpoint]) < 12, "木牌没有位于桥头 " + gate_id
        island = bridge["from"] if endpoint == 0 else bridge["to"]
        assert (gate_id, island) not in covered, "木牌重复放在同一桥头 " + gate_id
        covered.add((gate_id, island))
        # 两根木桩和标牌中心都必须落在入口岛的实体对应导航面上。
        for offset in (-2.4, 0, 2.4):
            support = [point[0] + offset, point[1], point[2]]
            triangles = [[nav["vertices"][i] for i in face] for face, region in
                         zip(nav["triangles"], nav["triangleRegions"]) if region == island]
            assert any(inside(xz(support), [xz(p) for p in tri]) and
                       abs(height(xz(support), tri) - support[1]) < .001 for tri in triangles), "木牌木桩落点未贴入口岛地面 " + gate_id
    assert len(covered) == 10, "两端木牌缺失"
    assert "sign.transform.SetParent(world.transform, false)" in source, "木牌不能随门根隐藏"
    assert "label.text = Notice(gate.Id, open, story)" in source, "木牌缺少通行状态更新"
    assert "ZombieModeUIHelper.GetGameFont()" in source and "signs.Clear()" in source, "木牌缺少游戏字体或独立回收"


def gate_bounds(gate):
    x, y, z = gate["position"]
    yaw = math.radians(gate["yaw"])
    half_width, half_depth = (gate["width"] + 3) / 2, .9
    ex = abs(math.cos(yaw)) * half_width + abs(math.sin(yaw)) * half_depth
    ez = abs(math.sin(yaw)) * half_width + abs(math.cos(yaw)) * half_depth
    return (x - ex, y - 1, z - ez, x + ex, y + 7, z + ez)


def intersects(a, b):
    return all(a[i] <= b[i + 3] and b[i] <= a[i + 3] for i in range(3))


def nearest_path(gate, bridge):
    point = gate["position"]
    best = None
    accumulated = 0.0
    total = sum(math.dist(xz(a), xz(b)) for a, b in zip(bridge["path"], bridge["path"][1:]))
    for a, b in zip(bridge["path"], bridge["path"][1:]):
        dx, dz = b[0] - a[0], b[2] - a[2]
        length = math.hypot(dx, dz)
        t = max(0, min(1, ((point[0] - a[0]) * dx + (point[2] - a[2]) * dz) / (length * length)))
        nearest = [a[i] + t * (b[i] - a[i]) for i in range(3)]
        distance = math.dist(xz(point), xz(nearest))
        if best is None or distance < best[0]:
            best = (distance, nearest, (dx / length, dz / length), accumulated + t * length, total)
        accumulated += length
    return best


def physical_span(gate, bridge):
    distance, center, tangent, station, total = nearest_path(gate, bridge)
    assert distance < .15, "实体门偏离桥面中线 " + bridge["id"]
    yaw = math.radians(gate["yaw"])
    cosine, sine = math.cos(yaw), math.sin(yaw)
    # 桥截面两端必须都在旋转 BoxCollider 内；只封导航不能掩盖实体门侧漏。
    for sign in (-1, 1):
        endpoint = [center[0] - tangent[1] * bridge["width"] * .5 * sign,
                    center[1], center[2] + tangent[0] * bridge["width"] * .5 * sign]
        dx, dy, dz = [endpoint[i] - gate["position"][i] for i in range(3)]
        local_x = dx * cosine - dz * sine
        local_z = dx * sine + dz * cosine
        assert abs(local_x) <= (gate["width"] + 3) / 2 + .01 and abs(local_z) <= .91, "实体门没有横跨完整桥宽 " + bridge["id"]
        assert -1 <= dy <= 7, "实体门未覆盖桥面高程 " + bridge["id"]
    return {"stationMeters": round(station, 2), "bridgeLengthMeters": round(total, 2),
            "distanceFromFirstIsland": round(station, 2), "distanceFromSecondIsland": round(total - station, 2)}


class Topology:
    def __init__(self, layout):
        nav = layout["navigation"]
        # A* Int3 的毫米量化不会改变检查边界的归属。
        vertices = [[round(v, 3) for v in p] for p in nav["vertices"]]
        self.triangles = [[vertices[i] for i in face] for face in nav["triangles"]]
        self.regions = nav["triangleRegions"]
        self.bounds = [tuple(min(p[i] for p in tri) for i in range(3)) + tuple(max(p[i] for p in tri) for i in range(3))
                       for tri in self.triangles]
        edges = defaultdict(list)
        for index, face in enumerate(nav["triangles"]):
            for a, b in zip(face, face[1:] + face[:1]):
                edges[tuple(sorted((a, b)))].append(index)
        self.links = defaultdict(set)
        for faces in edges.values():
            if len(faces) == 2:
                a, b = faces
                self.links[a].add(b); self.links[b].add(a)
        self.region_nodes = defaultdict(set)
        for index, region in enumerate(self.regions):
            self.region_nodes[region].add(index)
        self.markers = {}
        for marker in layout["markers"]:
            point = marker["position"]
            nodes = {i for i, tri in enumerate(self.triangles) if inside(xz(point), [xz(p) for p in tri])
                     and abs(height(xz(point), tri) - point[1]) < .02}
            assert nodes, "实际导航没有覆盖标记 " + marker["id"]
            self.markers[marker["id"]] = nodes

    def reach(self, starts, blocked, allowed=None):
        seen = set(starts) - blocked
        queue = deque(seen)
        while queue:
            for nxt in self.links[queue.popleft()]:
                if nxt not in seen and nxt not in blocked and (allowed is None or nxt in allowed):
                    seen.add(nxt); queue.append(nxt)
        return seen


def verify(layout, gates):
    topology = Topology(layout)
    bridges = {b["id"]: b for b in layout["bridges"]}
    blocked = {}
    report = {}
    for gate_id, gate in gates.items():
        bridge_id = BRIDGES[gate_id]
        bridge = bridges[bridge_id]
        report[gate_id] = physical_span(gate, bridge)
        area = gate_bounds(gate)
        blocked[gate_id] = {i for i, bbox in enumerate(topology.bounds) if intersects(area, bbox)}
        assert blocked[gate_id], "门没有封住任何导航面 " + gate_id
        affected = {topology.regions[i] for i in blocked[gate_id]}
        assert affected == {bridge_id}, "门误封其他区域 " + gate_id + " " + str(affected)
        allowed = topology.region_nodes[bridge_id] | topology.region_nodes[bridge["from"]] | topology.region_nodes[bridge["to"]]
        reachable = topology.reach(topology.region_nodes[bridge["from"]], blocked[gate_id], allowed)
        assert not (reachable & topology.region_nodes[bridge["to"]]), "闭门未切断目标桥 " + gate_id
        report[gate_id]["blockedTriangles"] = len(blocked[gate_id])

    ids = list(BRIDGES)
    island_ids = {i["id"] for i in layout["islands"]}
    for bits in range(32):
        shut = {gate_id for i, gate_id in enumerate(ids) if bits & (1 << i)}
        blocked_nodes = set().union(*(blocked[gate_id] for gate_id in shut)) if shut else set()
        actual = topology.reach(topology.markers["PlayerSpawn"], blocked_nodes)
        abstract_links = defaultdict(set)
        shut_bridges = {BRIDGES[gate_id] for gate_id in shut}
        for bridge_id, bridge in bridges.items():
            if bridge_id not in shut_bridges:
                abstract_links[bridge["from"]].add(bridge["to"])
                abstract_links[bridge["to"]].add(bridge["from"])
        expected = {"A"}; queue = deque(expected)
        while queue:
            for nxt in abstract_links[queue.popleft()]:
                if nxt not in expected:
                    expected.add(nxt); queue.append(nxt)
        for island in island_ids:
            reachable = bool(actual & topology.markers["POI_" + island])
            assert reachable == (island in expected), "门组合误封/漏通 " + str(shut) + " 区域 " + island
        if bits == 0:
            assert len(actual) == len(topology.triangles), "所有门开放后仍有导航孤岛"
        if len(shut) == 5:
            for marker in ("POI_D", "POI_F", "Search_S2", "Search_S3"):
                assert actual & topology.markers[marker], "初始主线/和解证据不可达 " + marker
            assert not (actual & topology.markers["POI_H"]), "初始归航钟庭未锁住"
    return report


def main():
    layout = json.loads((ROOT / "ArtSource/SkyIsland/layout.json").read_text(encoding="utf-8-sig"))
    gates = read_gates()
    verify_bridge_notices(layout)
    content = json.loads((ROOT / "Assets/Data/SkyIsland/World.json").read_text(encoding="utf-8-sig"))
    definitions = {gate["id"]: gate for gate in content["gates"]}
    assert set(definitions) == set(gates), "内容表与实际门集合不一致"
    def closed_at(flags):
        result = set()
        for key, gate in definitions.items():
            required = gate["requiredFlags"]
            is_open = bool(flags & required) if gate["any"] else flags & required == required
            if not is_open:
                result.add(key)
        return result
    assert closed_at(0) == set(gates), "初始故事必须关闭五门"
    assert not closed_at(32767), "全部故事旗标完成必须打开五门"
    assert "BellCourt" in closed_at(1) and "BellCourt" in closed_at(2) and "BellCourt" not in closed_at(3), "钟庭必须由双航标共同打开"
    assert "ZhelingPass" not in closed_at(1024) and "ZhelingPass" not in closed_at(2048), "折翎两种解决方式都必须开路"
    report = verify(layout, gates)
    mutations = [
        ("平移门离桥", lambda data: data["K1"]["position"].__setitem__(0, 1000)),
        ("门沿桥向而非横桥", lambda data: data["K2"].__setitem__("yaw", data["K2"]["yaw"] + 90)),
        ("门宽缩水", lambda data: data["K3"].__setitem__("width", 1)),
        ("钟庭门高程错位", lambda data: data["BellCourt"]["position"].__setitem__(1, 100)),
        ("折翎门放到邻岛", lambda data: data["ZhelingPass"].__setitem__("position", [250, 44, 180])),
    ]
    for label, mutate in mutations:
        changed = copy.deepcopy(gates); mutate(changed)
        try:
            verify(layout, changed)
        except AssertionError:
            continue
        raise AssertionError("负向探针未转红 " + label)
    print("SkyIslandGateNavigationPropertyTest: PASS 32 gate combinations / 5 mutations rejected")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
