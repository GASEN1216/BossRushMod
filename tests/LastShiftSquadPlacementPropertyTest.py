# -*- coding: utf-8 -*-
"""末班编组站守货岗的站位与覆盖（LastShiftYard/LastShiftSquads.cs 的 SquadDefs；2026-11-12 围圈小队，2026-11-13 改为守货岗）。

每处营地的每个站位用作者布局的真实数据复算（three.js 坐标，Unity = (x, y, -z)）：
- 落在发布导航（ArtSource/LastShiftYard/navigation.json）的三角形里、所在三角形不受列车占线影响（train 掩码 0）、离导航边界 ≥ 0.25 m；
- 逻辑网格（export/grid_flag.u8）格心可走、不带列车包络标记；离两股道中心线 ≥ 4 m（车体半宽 1.65 + 推挤 + 余量）；
- 离任何实体碰撞盒（collision_boxes.json，底面低于 2.2 m 的）水平 ≥ 0.4 m（角色胶囊 0.25 m + 余量）；
- 同一营地站位两两 ≥ 1.2 m、离圈心 0.8–3 m；离搜刮 / 交互的站位点 ≥ 1.5 m（不堵官方交互）；圈心离出生点 ≥ 25 m、离撤离点 ≥ 10 m。
- 不压在楼梯 / 坡道的占地里（布局 facilities 的司机室坡道与北跨线桥两段楼梯，外扩 0.8 m）：台阶是实心体，运行时贴地查询取到地面高度、
  胶囊就卡在台阶里（2026-11-13 实机日志：北公路卡口四个站位全压在中间楼梯上，整处没开）。导航三角形与碰撞盒都查不出这一类。
每处至少 3 个站位（运行时站不住的那个本趟不用，不足 3 个整处不用）。另做反向自检：往营地里塞一个压在饭桌上、一个压在股道上的站位，判据必须转红。
覆盖（owner 2026-11-13「玩家走到每个有宝箱的地方都有激战」）：布局里每只普通箱 / 风险柜（签收柜、零担箱跟车走，另算；出生点 30 m 内的南班房三只由南班固定岗管）
20 m 内都要有一处有箱子的守货岗（Loot / Vault）；风险柜与卡口那几处要由小队长带队（Posts 第一位是信号工长 / 调车长 / 押运班长）。
反向自检：拿掉东北退件那一处，东线风箱必须报「没有守货岗」。
证据（ArtSource/LastShiftYard）不进公开仓库：缺证据时退出码 2，--source-only 记 PARTIAL。
"""
import json
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'LastShiftYard/LastShiftSquads.cs'
ART = ROOT / 'ArtSource/LastShiftYard'
SQUAD_MIN = 3


LEADERS = ('Lamp', 'Shunt', 'Sarge')


def parse_defs(text):
    defs = []
    for m in re.finditer(r'new SquadDef\("(\w+)",\s*(-?[\d.]+)f,\s*(-?[\d.]+)f,\s*new\[\]\s*\{([^}]*)\},\s*new\[\]\s*\{([^}]*)\}(.*?)GarrisonKind\.(\w+)\)', text, re.S):
        numbers = [float(v.strip().rstrip('f')) for v in m.group(4).split(',') if v.strip()]
        posts = [v.strip() for v in m.group(5).split(',') if v.strip()]
        defs.append({'id': m.group(1), 'focus': (float(m.group(2)), float(m.group(3))),
                     'slots': [(numbers[i], numbers[i + 1]) for i in range(0, len(numbers) - 1, 2)], 'posts': posts,
                     'odd': len(numbers) % 2, 'kind': m.group(7)})
    return defs


def source_errors(text, defs):
    errors = []
    if not defs:
        errors.append('LastShiftSquads.cs 里解析不出 SquadDefs')
    m = re.search(r'internal const int SquadMin = (\d+), GarrisonCap = (\d+), GarrisonPopulation = (\d+);', text)
    if not m or int(m.group(1)) != SQUAD_MIN:
        errors.append('SquadMin 不是 %d（本测试按 3 人起开张核对）' % SQUAD_MIN)
    else:
        cap, population = int(m.group(2)), int(m.group(3))
        if cap < 2 * SQUAD_MIN:
            errors.append('GarrisonCap %d 不够同时开两处' % cap)
        loot_slots = sum(len(d['slots']) for d in defs if d.get('kind') in ('Loot', 'Vault', 'Checkpoint'))
        if population < loot_slots:
            errors.append('GarrisonPopulation %d 少于有箱子的守货岗与卡口满员 %d 人：走完一圈就刷不出人了' % (population, loot_slots))
    for d in defs:
        if d['odd']:
            errors.append(d['id'] + ': 站位坐标不是成对的 (x, z)')
        if len(d['posts']) < len(d['slots']):
            errors.append(d['id'] + ': 岗位表短于站位数')
        if d.get('kind') in ('Vault', 'Checkpoint') and (not d['posts'] or d['posts'][0] not in LEADERS):
            errors.append(d['id'] + ': 风险柜 / 卡口那几处要由小队长带队（Posts 第一位）')
        if sum(1 for p in d['posts'] if p in LEADERS) > 1:
            errors.append(d['id'] + ': 一处只带一个小队长')
    return errors


def coverage_errors(defs, layout):
    """每只普通箱 / 风险柜 20 m 内有一处有箱子的守货岗；签收柜、零担箱跟车走，出生点 30 m 内的由南班固定岗管。"""
    errors = []
    spawn = layout['spawn']['position']
    guards = [d for d in defs if d.get('kind') in ('Loot', 'Vault')]
    for box in layout['loot']:
        if box.get('type') in ('sealed', 'drop'):
            continue
        x, z = box['position'][0], box['position'][2]
        if math.hypot(x - spawn[0], z - spawn[2]) < 30:
            continue
        near = min((math.hypot(x - d['focus'][0], z - d['focus'][1]) for d in guards), default=1e9)
        if near > 20:
            errors.append('%s (%.0f, %.0f): 20 m 内没有守货岗（最近 %.1f m）' % (box['id'], x, z, near))
    return errors


class Evidence:
    def __init__(self):
        self.layout = json.loads((ART / 'export/lastshift_layout.json').read_text(encoding='utf-8'))
        self.layout_raw = self.layout
        nav = json.loads((ART / 'navigation.json').read_text(encoding='utf-8'))
        self.V, self.T, self.TM = nav['vertices'], nav['triangles'], nav['train']
        boxes = json.loads((ART / 'collision_boxes.json').read_text(encoding='utf-8'))
        self.boxes = [b for group in boxes.values() if isinstance(group, list) for b in group if 'center' in b and 'size' in b]
        g = self.layout['grid']
        self.grid = g
        self.flags = (ART / 'export' / g['flagFile']).read_bytes()
        count = {}
        for t in self.T:
            for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
                key = (min(a, b), max(a, b))
                count[key] = count.get(key, 0) + 1
        self.edges = [e for e, n in count.items() if n == 1]

    def flag(self, x, z):
        g = self.grid
        i, j = int(round(x - g['originX'])), int(round(z - g['originZ']))
        if 0 <= i < g['width'] and 0 <= j < g['depth']:
            return self.flags[j * g['width'] + i]
        return 0

    def triangle(self, x, z):
        ux, uz = x, -z
        for k, t in enumerate(self.T):
            a, b, c = ((self.V[t[n]][0], self.V[t[n]][2]) for n in range(3))

            def side(p, q, r):
                return (p[0] - r[0]) * (q[1] - r[1]) - (q[0] - r[0]) * (p[1] - r[1])
            p = (ux, uz)
            d1, d2, d3 = side(p, a, b), side(p, b, c), side(p, c, a)
            if not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0)):
                return k
        return None

    def edge_distance(self, x, z):
        ux, uz = x, -z
        best = 1e9
        for a, b in self.edges:
            ax, az, bx, bz = self.V[a][0], self.V[a][2], self.V[b][0], self.V[b][2]
            dx, dz = bx - ax, bz - az
            length = dx * dx + dz * dz
            t = 0.0 if length == 0 else max(0.0, min(1.0, ((ux - ax) * dx + (uz - az) * dz) / length))
            best = min(best, math.hypot(ux - (ax + t * dx), uz - (az + t * dz)))
        return best

    def box_clearance(self, x, z):
        best, which = 1e9, None
        for b in self.boxes:
            cx, cy, cz = b['center']
            sx, sy, sz = b['size']
            if cy - sy / 2 > 2.2:
                continue
            yaw = b.get('yaw') or 0
            co, sn = math.cos(yaw), math.sin(yaw)
            dx, dz = x - cx, z - cz
            lx, lz = co * dx - sn * dz, sn * dx + co * dz
            d = math.hypot(max(abs(lx) - sx / 2, 0), max(abs(lz) - sz / 2, 0))
            if d < best:
                best, which = d, b.get('id')
        return best, which


def stair_rects(layout):
    """楼梯 / 坡道的水平占地 (x0, x1, z0, z1)，three.js 坐标。"""
    fac = layout.get('facilities', {})
    rects = []
    r = fac.get('ramp')
    if r:
        rects.append((r['x'] - r['w'] / 2, r['x'] + r['w'] / 2, min(r['fromZ'], r['toZ']), max(r['fromZ'], r['toZ'])))
    for s in (fac.get('northBridge') or {}).get('stairs', []):
        rects.append((s['x'] - s['w'] / 2, s['x'] + s['w'] / 2, min(s['lipZ'], s['footZ']), max(s['lipZ'], s['footZ'])))
    return rects


def on_stairs(rects, x, z, margin=0.8):
    return any(x0 - margin <= x <= x1 + margin and z0 - margin <= z <= z1 + margin for x0, x1, z0, z1 in rects)


def rail_x(z, line):
    n = max(0.0, min(1.0, (8 - z) / 32))
    return (1 if line == 'E' else -1) * 24 * n * n * (3 - 2 * n)


def check(defs, ev):
    errors = []
    lay = ev.layout
    flags = lay['grid']['flags']
    spawn = lay['spawn']['position']
    uses = [tuple(l['use']) for l in lay['loot'] if l.get('use')] + [tuple(i['use']) for i in lay['interacts'] if i.get('use')]
    stairs = stair_rects(lay)
    if not stairs:
        errors.append('布局里读不到坡道 / 楼梯占地（facilities.ramp / northBridge.stairs），压楼梯的判据没法查')
    for d in defs:
        fx, fz = d['focus']
        if math.hypot(fx - spawn[0], fz - spawn[2]) < 25:
            errors.append(d['id'] + ': 圈心离出生点不到 25 m')
        for ex in lay['exits']:
            if math.hypot(fx - ex['position'][0], fz - ex['position'][2]) < 10:
                errors.append(d['id'] + ': 圈心离撤离点 ' + ex['id'] + ' 不到 10 m')
        good = 0
        for n, (x, z) in enumerate(d['slots']):
            tag = '%s[%d] (%.1f, %.1f)' % (d['id'], n, x, z)
            problems = []
            k = ev.triangle(x, z)
            if k is None:
                problems.append('不在导航上')
            elif ev.TM[k] != 0:
                problems.append('所在导航三角形受列车占线')
            elif ev.edge_distance(x, z) < 0.25:
                problems.append('离导航边界不到 0.25 m')
            f = ev.flag(x, z)
            if not f & flags['walkable']:
                problems.append('逻辑网格不可走')
            if f & (flags['trainW'] | flags['trainE']):
                problems.append('在列车包络里')
            if min(abs(x - rail_x(z, 'W')), abs(x - rail_x(z, 'E'))) < 4.0:
                problems.append('离股道中心线不到 4 m')
            if on_stairs(stairs, x, z):
                problems.append('压在楼梯 / 坡道上')
            clearance, which = ev.box_clearance(x, z)
            if clearance < 0.4:
                problems.append('离碰撞盒 %s 只有 %.2f m' % (which, clearance))
            r = math.hypot(x - fx, z - fz)
            if r < 0.8 or r > 3.0:
                problems.append('离圈心 %.2f m（应 0.8–3 m）' % r)
            for u in uses:
                if math.hypot(x - u[0], z - u[1]) < 1.5:
                    problems.append('堵着交互站位 (%.1f, %.1f)' % u)
            if problems:
                errors.append(tag + ': ' + '；'.join(problems))
            else:
                good += 1
        if good < SQUAD_MIN:
            errors.append(d['id'] + ': 站得住的位置 %d 个，不足 %d' % (good, SQUAD_MIN))
        s = d['slots']
        for i in range(len(s)):
            for j in range(i + 1, len(s)):
                if math.hypot(s[i][0] - s[j][0], s[i][1] - s[j][1]) < 1.2:
                    errors.append('%s: 第 %d、%d 个站位挨得太近' % (d['id'], i, j))
    return errors


def self_check(defs, ev):
    """反向自检：压在饭桌上、压在股道上、贴着另一人的站位都要被判出来。"""
    missed = []
    probes = (
        ('table', [{'id': 'probe', 'focus': (-47.0, 32.6), 'slots': [(-47.0, 32.0)] + defs[0]['slots'][1:], 'posts': [], 'odd': 0}], '碰撞盒'),
        ('track', [{'id': 'probe', 'focus': (0.0, 30.0), 'slots': [(0.0, 31.5), (1.5, 30.0), (-1.5, 30.0)], 'posts': [], 'odd': 0}], '股道'),
        ('crowd', [{'id': 'probe', 'focus': defs[1]['focus'], 'slots': defs[1]['slots'] + [defs[1]['slots'][0]], 'posts': [], 'odd': 0}], '挨得太近'),
        # 2026-11-13 实机出过的那一处：北公路卡口原站位压在北跨线桥中间楼梯上。
        ('stairs', [{'id': 'probe', 'focus': (0.0, -45.0), 'slots': [(1.5, -45.0), (0.0, -43.5), (-1.5, -45.0)], 'posts': [], 'odd': 0}], '楼梯'),
    )
    for name, bad, word in probes:
        if not any(word in e for e in check(bad, ev)):
            missed.append(name)
    if not any('eastRisk' in e for e in coverage_errors([d for d in defs if d['id'] != 'returns'], ev.layout)):
        missed.append('coverage')
    return missed


def main():
    text = SOURCE.read_text(encoding='utf-8-sig')
    defs = parse_defs(text)
    errors = source_errors(text, defs)
    if not all((ART / rel).is_file() for rel in ('export/lastshift_layout.json', 'navigation.json', 'collision_boxes.json')):
        for e in errors:
            print('  [FAIL] ' + e)
        print('LastShiftSquadPlacementPropertyTest: PARTIAL 缺 ArtSource/LastShiftYard 导航 / 碰撞 / 布局（local-only 证据）')
        return 1 if errors else 2
    ev = Evidence()
    if defs:
        errors += check(defs, ev)
        errors += coverage_errors(defs, ev.layout)
        missed = self_check(defs, ev)
        if missed:
            errors.append('判据自检没转红：' + ', '.join(missed))
    for e in errors:
        print('  [FAIL] ' + e)
    slots = sum(len(d['slots']) for d in defs)
    print('LastShiftSquadPlacementPropertyTest: ' + ('FAIL' if errors else 'PASS（%d 处营地、%d 个站位）' % (len(defs), slots)))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
