# -*- coding: utf-8 -*-
"""潮闸镇背对镜头的门前区（2026-11-17 owner「门上面被建筑挡住了，要蹭门才知道哪里有门」）。

`Tidegate/TidegateDoorways.cs` 把原型布局里的门写死，按官方默认机位（俯仰 55°、偏航 -30°）只给背对镜头的门登记门前区，
门前区深度取「楼高 × 背向分 / tan(俯仰)」与「回到墙面前横移出墙端」两个约束的小者，再加余量。这里：
1. 写死的门表与 `ArtSource/Tidegate/export/tidegate_layout.json` 逐项一致（楼号、边、沿墙坐标、门宽）；
2. 背向判定恰好是北墙、西墙（朝南、朝东的门本来看得见）；
3. 用光线步进独立复算「门洞前哪段地被本栋挡住」（门洞两边与中点），门前区要盖住它、又不能多出太多；
   另钉源码里的闭式确实取两个约束的小者（Python 端是它的移植）；
4. 判据自检：删一扇门、把机位偏航改成别的值，比对要转红。
证据（ArtSource/Tidegate）不进公开仓库：缺布局时只查源码，退出码 2，--source-only 记 PARTIAL。
"""
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'Tidegate/TidegateDoorways.cs'
LAYOUT = ROOT / 'ArtSource/Tidegate/export/tidegate_layout.json'
HEIGHTS = (3.0, 6.2, 9.0, 12.0)
OUTWARD = {'n': (0.0, 1.0), 's': (0.0, -1.0), 'e': (1.0, 0.0), 'w': (-1.0, 0.0)}


def parse(text):
    consts = {}
    for name in ('CameraPitch', 'CameraYaw', 'AwayThreshold', 'SideMargin', 'OutMargin', 'MinDepth'):
        m = re.search(r'\b' + name + r'\s*=\s*(-?[\d.]+)f', text)
        if not m:
            raise ValueError('缺常量 ' + name)
        consts[name] = float(m.group(1))
    doors = [(b, s, float(a), float(w)) for b, s, a, w in
             re.findall(r'new Door\("(\w+)",\s*\'([nsew])\',\s*(-?[\d.]+)f,\s*([\d.]+)f\)', text)]
    return consts, doors


def flat_forward(c):
    yaw = math.radians(c['CameraYaw'])
    return math.sin(yaw), math.cos(yaw)


def away(c, side):
    fx, fz = flat_forward(c)
    nx, nz = OUTWARD[side]
    return nx * fx + nz * fz


def closed_depth(c, side, at, width, footprint, height):
    """TidegateDoorways.HiddenDepth 的移植：楼高约束与「回到墙面前横移出墙端」约束取小。"""
    a = away(c, side)
    if a <= 0 or height <= 0:
        return 0.0
    by_height = height * a / math.tan(math.radians(c['CameraPitch']))
    fx, fz = flat_forward(c)
    tx, tz = (1.0, 0.0) if side in 'ns' else (0.0, 1.0)
    slide = (-fx * tx - fz * tz) / a
    if abs(slide) < 1e-4:
        return by_height
    x0, z0, x1, z1 = footprint
    lo, hi = (x0, x1) if side in 'ns' else (-z1, -z0)
    center = at if side in 'ns' else -at
    room = hi - (center - width / 2) if slide > 0 else (center + width / 2) - lo
    return max(0.0, min(by_height, room / abs(slide)))


def zone_depth(c, side, at, width, footprint, height):
    return max(c['MinDepth'], closed_depth(c, side, at, width, footprint, height) + c['OutMargin'])


def ray_hits_box(p, d, lo, hi):
    tmin, tmax = 1e-6, 1e9
    for i in range(3):
        if abs(d[i]) < 1e-12:
            if p[i] < lo[i] or p[i] > hi[i]:
                return False
            continue
        t1, t2 = (lo[i] - p[i]) / d[i], (hi[i] - p[i]) / d[i]
        tmin, tmax = max(tmin, min(t1, t2)), min(tmax, max(t1, t2))
        if tmin > tmax:
            return False
    return True


def marched_hidden(c, footprint, floor, height, side, at, width):
    """独立复算：门洞两边与中点正前方的地面点，往镜头的射线穿过本栋包围盒就算被挡；返回最远被挡距离。"""
    x0, z0, x1, z1 = footprint  # 原型坐标；Unity 是 (x, -z)
    lo, hi = (x0, floor, -z1), (x1, floor + height, -z0)
    pitch, yaw = math.radians(c['CameraPitch']), math.radians(c['CameraYaw'])
    to_camera = (-math.sin(yaw) * math.cos(pitch), math.sin(pitch), -math.cos(yaw) * math.cos(pitch))
    nx, nz = OUTWARD[side]
    center = at if side in 'ns' else -at
    far = 0.0
    for along in (center - width / 2 + 0.01, center, center + width / 2 - 0.01):
        if side in 'ns':
            wx, wz = along, (hi[2] if side == 'n' else lo[2])
        else:
            wx, wz = (lo[0] if side == 'w' else hi[0]), along
        t = 0.02
        while t < 20.0:
            if ray_hits_box((wx + nx * t, floor + 0.01, wz + nz * t), to_camera, lo, hi):
                far = max(far, t)
            t += 0.02
    return far


def check(consts, doors, layout):
    errors = []
    expected = []
    for b in layout['buildings']:
        for d in b['doors']:
            expected.append((b['id'], d[0], float(d[1]), float(d[2]) if len(d) > 2 else 2.0))
    if sorted(doors) != sorted(expected):
        errors.append('门表与布局不一致：多出 %s，缺少 %s' % (sorted(set(doors) - set(expected)), sorted(set(expected) - set(doors))))
    away_sides = {s for s in OUTWARD if away(consts, s) > consts['AwayThreshold']}
    if away_sides != {'n', 'w'}:
        errors.append('官方默认机位下背对镜头的应是北墙、西墙，算出来是 %s' % sorted(away_sides))
    buildings = {b['id']: b for b in layout['buildings']}
    for building, side, at, width in doors:
        if side not in away_sides or building not in buildings:
            continue
        b = buildings[building]
        for h in HEIGHTS:
            hidden = marched_hidden(consts, b['footprint'], b['floorY'], h, side, at, width)
            depth = zone_depth(consts, side, at, width, b['footprint'], h)
            if depth < hidden + 0.5:
                errors.append('%s %s 门：楼高 %.1f m 时被挡 %.2f m，门前区只有 %.2f m' % (building, side, h, hidden, depth))
            if depth > max(consts['MinDepth'], hidden + consts['OutMargin']) + 0.3:
                errors.append('%s %s 门：楼高 %.1f m 时门前区 %.2f m 比被挡带 %.2f m 多出太多' % (building, side, h, depth, hidden))
    return errors


def self_check(consts, doors, layout):
    missed = []
    if not check(consts, doors[1:], layout):
        missed.append('删一扇门')
    if not check(dict(consts, CameraYaw=150.0), doors, layout):
        missed.append('偏航改 150°')
    if not check(dict(consts, OutMargin=-1.0), doors, layout):
        missed.append('门前区比被挡带浅')
    return missed


def main():
    import json
    errors = []
    try:
        consts, doors = parse(SOURCE.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError) as e:
        print('  [FAIL] ' + str(e))
        print('TidegateDoorwayPropertyTest: FAIL')
        return 1
    if consts['CameraPitch'] != 55.0 or consts['CameraYaw'] != -30.0:
        errors.append('机位常量要和官方 CameraArm 默认值一致（55 / -30）')
    if not doors:
        errors.append('TidegateDoorways.Doors 是空的')
    text = SOURCE.read_text(encoding='utf-8-sig')
    for token in ('Mathf.Min(byHeight, room / Mathf.Abs(slide))', 'HiddenDepth(door, footprint, top - floor) + OutMargin',
                  'if (!FacesAway(door.Side)) continue;'):
        if token not in text:
            errors.append('TidegateDoorways.cs 与本测试移植的算法不一致，缺：' + token)
    if not LAYOUT.is_file():
        for e in errors:
            print('  [FAIL] ' + e)
        print('TidegateDoorwayPropertyTest: PARTIAL 缺 ArtSource/Tidegate/export/tidegate_layout.json（local-only 证据）')
        return 1 if errors else 2
    layout = json.loads(LAYOUT.read_text(encoding='utf-8'))
    errors += check(consts, doors, layout)
    missed = self_check(consts, doors, layout)
    if missed:
        errors.append('判据自检没转红：' + '、'.join(missed))
    for e in errors:
        print('  [FAIL] ' + e)
    count = sum(1 for d in doors if away(consts, d[1]) > consts['AwayThreshold'])
    print('TidegateDoorwayPropertyTest: ' + ('FAIL' if errors else 'PASS（%d 扇门，%d 扇背对镜头）' % (len(doors), count)))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
