#!/usr/bin/env python3
"""普通巡逻固定落点的离线验收；直接读取正式 JSON、硬编码兜底和当前碰撞导航。

导航包含/高度/三角中心由重心坐标独立复算，不调用选点算法。静态交互位置复用已有
SkyIslandInteractionCompetitionPropertyTest（搜刮、采集、信鸽、纪念物、装置和居民）；
反向探针只改 deepcopy 的数据，源文件按 SHA-256 核对。不能证明 Unity 实机 AI/碰撞。
"""
import copy
import hashlib
import json
import math
import re
import sys
from collections import Counter
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source
from SkyIslandInteractionCompetitionPropertyTest import build_interactables, RESIDENT_MARKERS

DATA = ROOT / "Assets/Data/SkyIsland/Patrols.json"
RULES = ROOT / "SkyIsland/SkyIslandPatrolRules.cs"
NAVIGATION = ROOT / "ArtSource/SkyIsland/collision_navigation.json"
LAYOUT = ROOT / "ArtSource/SkyIsland/layout.json"
TARGETS = {"A": 6, "B": 8, "C": 10, "D": 12, "E": 12, "F": 12,
           "G": 14, "H": 14, "S1": 6, "S2": 6, "S3": 6, "S4": 6}
RANKS = {"A": 1, "B": 2, "C": 3, "D": 4, "E": 5, "F": 6,
         "G": 7, "H": 8, "S1": 3, "S2": 4, "S3": 6, "S4": 7}
PROFILE_KEYS = {"regionId", "rank", "healthFactor", "damageFactor", "sightDistance", "reactionTime"}
SLOT_KEYS = {"id", "regionId", "x", "y", "z"}
TOLERANCE = 0.0001  # 六位小数坐标与 C# float 舍入的容差，远小于巡逻间距。


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def horizontal(a, b):
    return math.hypot(a[0] - b[0], a[2] - b[2])


def point(slot):
    return (slot["x"], slot["y"], slot["z"])


def barycentric_height(p, vertices):
    """XZ 包含与 Y 插值；输入是导航三角形，不借用旧 ground 网格或生成算法。"""
    a, b, c = vertices
    denominator = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
    if abs(denominator) < 1e-12:
        return None
    u = ((b[2] - c[2]) * (p[0] - c[0]) + (c[0] - b[0]) * (p[2] - c[2])) / denominator
    v = ((c[2] - a[2]) * (p[0] - c[0]) + (a[0] - c[0]) * (p[2] - c[2])) / denominator
    w = 1 - u - v
    if min(u, v, w) < -1e-8:
        return None
    return u * a[1] + v * b[1] + w * c[1]


def context():
    navigation_bytes = NAVIGATION.read_bytes()
    navigation = json.loads(navigation_bytes.decode("utf-8-sig"))["navigation"]
    vertices = navigation["vertices"]
    triangles = navigation["triangles"]
    regions = navigation["triangleRegions"]
    assert len(triangles) == len(regions), "导航面与区域标签数量不一致"
    faces = {region: [] for region in TARGETS}
    for tri, region in zip(triangles, regions):
        if region in faces:
            faces[region].append(tuple(vertices[i] for i in tri))
    assert all(faces.values()), "某个岛区没有导航面"
    layout = load(LAYOUT)
    markers = {marker["id"]: marker["position"] for marker in layout["markers"]}
    items, notes = build_interactables()
    assert not notes, "交互复算有降级，先核对来源：" + str(notes)
    assert len(RESIDENT_MARKERS) == 6, "居民锚点应完整读取六处"
    protected = [(marker["id"], marker["position"]) for marker in layout["markers"]
                 if marker["kind"] != "region"]
    protected += [(name, pos) for name, pos, _, _ in items if not name.startswith("resident:")]
    return faces, markers, protected, hashlib.sha256(navigation_bytes).hexdigest()


def validate(data, ctx):
    """收集各条独立不变式失败，反例不因前一条失败而绕过后面的判据。"""
    errors = []
    faces, markers, protected, navigation_hash = ctx
    if set(data) != {"version", "sourceNavigationSha256", "profiles", "slots"} or type(data.get("version")) is not int or data["version"] != 1:
        errors.append("schema")
    if data.get("sourceNavigationSha256") != navigation_hash:
        errors.append("source_navigation_hash")
    profiles, slots = data.get("profiles", []), data.get("slots", [])
    if not isinstance(profiles, list) or not isinstance(slots, list):
        return errors + ["array_schema"]
    if Counter(p.get("regionId") for p in profiles) != Counter(RANKS.keys()):
        errors.append("profile_regions")
    valid_profiles = []
    for profile in profiles:
        region = profile.get("regionId")
        if set(profile) != PROFILE_KEYS or type(profile.get("rank")) is not int or profile.get("rank") != RANKS.get(region):
            errors.append("rank:" + str(region))
        bounds = {"healthFactor": (0.1, 10), "damageFactor": (0.1, 5), "sightDistance": (1, 80), "reactionTime": (0.1, 10)}
        if any(not finite(profile.get(k)) or not lo <= profile[k] <= hi for k, (lo, hi) in bounds.items()):
            errors.append("profile_number:" + str(region))
            continue
        if type(profile.get("rank")) is not int:
            continue
        if profile["rank"] <= 3 and not 8 <= profile["sightDistance"] <= 12:
            errors.append("early_sight:" + str(region))
        valid_profiles.append(profile)
    for a in valid_profiles:
        for b in valid_profiles:
            fields = ("healthFactor", "damageFactor", "sightDistance", "reactionTime")
            if a["rank"] == b["rank"] and any(a[k] != b[k] for k in fields):
                errors.append("same_rank_tuning")
            if a["rank"] < b["rank"] and (a["healthFactor"] >= b["healthFactor"] or a["damageFactor"] >= b["damageFactor"]
                    or a["sightDistance"] > b["sightDistance"] or a["reactionTime"] < b["reactionTime"]):
                errors.append("difficulty_order")
    counts = Counter(slot.get("regionId") for slot in slots)
    for region, target in TARGETS.items():
        if counts[region] != target:
            errors.append("count:" + region)
    expected_ids = {f"Patrol_{region}_{i:02}" for region, count in TARGETS.items() for i in range(1, count + 1)}
    if Counter(s.get("id") for s in slots) != Counter(expected_ids):
        errors.append("slot_ids")
    good_slots = []
    for slot in slots:
        sid, region = slot.get("id"), slot.get("regionId")
        if set(slot) != SLOT_KEYS or region not in faces or not all(finite(slot.get(k)) for k in ("x", "y", "z")):
            errors.append("slot_schema:" + str(sid))
            continue
        if not str(sid).startswith("Patrol_" + region + "_"):
            errors.append("slot_region:" + str(sid))
        p = point(slot)
        heights = [height for tri in faces[region] if (height := barycentric_height(p, tri)) is not None]
        if not heights:
            errors.append("navigation:" + str(sid))
        elif all(abs(p[1] - height) > TOLERANCE for height in heights):
            errors.append("height:" + str(sid))
        centers = [tuple(sum(vertex[axis] for vertex in tri) / 3 for axis in range(3)) for tri in faces[region]]
        if not any(max(abs(p[i] - center[i]) for i in range(3)) <= TOLERANCE for center in centers):
            errors.append("triangle_center:" + str(sid))
        if horizontal(p, markers["PlayerSpawn"]) < 14 - TOLERANCE:
            errors.append("spawn_clearance:" + str(sid))
        if any(horizontal(p, markers[m]) < 8 - TOLERANCE for m in RESIDENT_MARKERS):
            errors.append("resident_clearance:" + str(sid))
        if any(horizontal(p, position) < 4 - TOLERANCE for _, position in protected):
            errors.append("interaction_clearance:" + str(sid))
        for other in good_slots:
            if region == other["regionId"] and horizontal(p, point(other)) < 5 - TOLERANCE:
                errors.append("spacing:" + str(sid))
        good_slots.append(slot)
    return errors


def check_rule_constants(source=None):
    src = clean_source(RULES.read_text(encoding="utf-8-sig") if source is None else source)
    for name, expected in (("SpawnClearance", 14), ("ResidentClearance", 8),
                           ("InteractionClearance", 4), ("SlotSpacing", 5),
                           ("ActivationRadius", 70), ("SuspensionRadius", 110),
                           ("SpawnInterval", 0.12), ("TickInterval", 0.25)):
        match = re.search(r"\b" + name + r"\s*=\s*([0-9.]+)f\b", src)
        assert match and float(match.group(1)) == expected, "生产避让常量漂移：" + name
    match = re.search(r"\bActiveLimit\s*=\s*(\d+)\s*;", src)
    assert match and int(match.group(1)) == 24, "普通巡逻激活上限漂移"


def constant_probes():
    source = RULES.read_text(encoding="utf-8-sig")
    cases = (("ActiveLimit = 24", "ActiveLimit = 25"),
             ("ActivationRadius = 70f", "ActivationRadius = 110f"),
             ("SuspensionRadius = 110f", "SuspensionRadius = 70f"),
             ("SpawnInterval = 0.12f", "SpawnInterval = 0.0f"),
             ("TickInterval = 0.25f", "TickInterval = 0.0f"))
    for original, mutated in cases:
        assert source.count(original) == 1, "常量探针锚点不唯一：" + original
        try:
            check_rule_constants(source.replace(original, mutated))
        except AssertionError:
            continue
        raise AssertionError("常量反例未转红：" + original)
    return len(cases)


def fallback_data():
    src = clean_source(RULES.read_text(encoding="utf-8-sig"))
    number = r"(-?\d+(?:\.\d+)?)f"
    profiles = re.findall(r'new SkyIslandPatrolProfile\("([A-Z0-9]+)", (\d+), ' + ', '.join([number] * 4) + r'\)', src)
    slots = re.findall(r'new SkyIslandPatrolSlot\("([A-Za-z0-9_]+)", "([A-Z0-9]+)", ' + ', '.join([number] * 3) + r'\)', src)
    assert len(profiles) == len(TARGETS) and len(slots) == sum(TARGETS.values()), "完整兜底表无法读取"
    match = re.search(r'\bSourceNavigationSha256\s*=\s*"([0-9a-f]{64})"\s*;', src)
    assert match, "完整兜底表没有采样来源 hash"
    return {"version": 1, "sourceNavigationSha256": match.group(1), "profiles": [dict(zip(("regionId", "rank", "healthFactor", "damageFactor", "sightDistance", "reactionTime"),
                 (r, int(rank), float(hp), float(damage), float(sight), float(reaction)))) for r, rank, hp, damage, sight, reaction in profiles],
            "slots": [dict(zip(("id", "regionId", "x", "y", "z"), (sid, region, float(x), float(y), float(z))))
                      for sid, region, x, y, z in slots]}


def negative_probes(data, ctx):
    _, markers, protected, _ = ctx
    cases = []
    def case(name, code, edit):
        candidate = copy.deepcopy(data)
        edit(candidate)
        failures = validate(candidate, ctx)
        assert any(failure == code or failure.startswith(code + ":") for failure in failures), \
            name + " 没有在预期判据上转红：" + str(failures)
        cases.append(name)
    def move(d, pos):
        d["slots"][0].update(dict(zip(("x", "y", "z"), pos)))
    case("陈旧 NAV 来源", "source_navigation_hash", lambda d: d.update(sourceNavigationSha256="0" * 64))
    case("岛外点", "navigation", lambda d: move(d, (1000, 0, 1000)))
    case("悬空点", "height", lambda d: d["slots"][0].update(y=d["slots"][0]["y"] + 2))
    case("减掉 A 岛敌人", "count", lambda d: d["slots"].pop(0))
    case("重复槽位身份", "slot_ids", lambda d: d["slots"][1].update(id=d["slots"][0]["id"]))
    case("同区重叠", "spacing", lambda d: d["slots"][1].update({k: d["slots"][0][k] for k in ("x", "y", "z")}))
    case("占用出生点", "spawn_clearance", lambda d: move(d, markers["PlayerSpawn"]))
    case("占用居民锚点", "resident_clearance", lambda d: move(d, markers[RESIDENT_MARKERS[0]]))
    case("占用交互点", "interaction_clearance", lambda d: move(d, protected[0][1]))
    case("错误区域 Rank", "rank", lambda d: d["profiles"][0].update(rank=8))
    case("倒置伤害梯度", "difficulty_order", lambda d: d["profiles"][0].update(damageFactor=4))
    case("过大初期视距", "early_sight", lambda d: d["profiles"][0].update(sightDistance=40))
    case("非有限坐标", "slot_schema", lambda d: d["slots"][0].update(x=float("nan")))
    return cases


def main():
    tracked = (DATA, RULES, NAVIGATION, LAYOUT)
    hashes = {path: hashlib.sha256(path.read_bytes()).hexdigest() for path in tracked}
    data = load(DATA)
    check_rule_constants()
    ctx = context()
    for name, value in (("JSON", data), ("Fallback", fallback_data())):
        errors = validate(value, ctx)
        assert not errors, name + " 不合格：" + " ; ".join(errors)
    assert data == fallback_data(), "正式 JSON 与硬编码兜底漂移"
    probes = negative_probes(data, ctx)
    rule_probes = constant_probes()
    assert all(hashlib.sha256(path.read_bytes()).hexdigest() == digest for path, digest in hashes.items()), "验收期间源文件变化，需要重新取样；反例只修改内存数据"
    print("[PASS] SkyIslandPatrolPlacementPropertyTest: 12 regions, 112 fixed triangle centers, JSON/Fallback parity")
    print("[PASS] navigation/height/count/spacing/entry/residents/interactions/rank; deepcopy negative probes=" + str(len(probes)) + ", constant probes=" + str(rule_probes))
    print("[INFO] sampled/current navigation SHA-256=" + ctx[3])
    print("[INFO] island counts: " + ", ".join(region + "=" + str(count) for region, count in TARGETS.items()))
    for label, positions in (("spawn", [ctx[1]["PlayerSpawn"]]), ("resident", [ctx[1][m] for m in RESIDENT_MARKERS]),
                             ("interaction", [pos for _, pos in ctx[2]])):
        minimum = min(horizontal(point(slot), pos) for slot in data["slots"] for pos in positions)
        print("[INFO] minimum " + label + " clearance=" + format(minimum, ".3f") + "m")
    minimum = min(horizontal(point(a), point(b)) for i, a in enumerate(data["slots"]) for b in data["slots"][i + 1:]
                  if a["regionId"] == b["regionId"])
    print("[INFO] minimum same-region spacing=" + format(minimum, ".3f") + "m")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (AssertionError, KeyError, TypeError, ValueError) as error:
        print("[FAIL] SkyIslandPatrolPlacementPropertyTest: " + str(error))
        raise SystemExit(1)
