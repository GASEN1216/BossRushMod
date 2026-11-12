# -*- coding: utf-8 -*-
"""自研出击地图读图审计（SkyIsland/AGENTS.md §6.1 读图通则，2026-11-12）。

玩家反馈「边界不明；分不清哪些能挡子弹、哪些是贴图；没有掩体拉扯；莫名其妙卡脚」。本工具按通则逐件判定
三张图的碰撞台账，返回违规清单。判据：

  R1 看得见 = 摸得着：无实体的散布种类最大可见高度 ≤ 0.6 m，或属于明确的软类（草、花、芦苇、海带、贝壳、水洼）。
  R2 实体高度禁区：碰撞顶面离地 0.12–0.6 m 的实体不许存在（可走甲板 / 坡道不在台账里）。
  R3 碰撞贴合可见模型：逻辑网格方盒不得压着按件出实体的物件（潮闸镇）；坡道栏杆必须跟坡分段（末班编组站）。
  R4 边界是实物：Unity 构建报告里有边界实物（潮闸镇按地块：木栅栏 / 钢栅栏 / 路障 / 警示桩）。
  R5 架空层不压实心（末班编组站）：跨线桥与司机室下的地面另起一层，桥墩 / 立柱有实体。
  R6 头目场有掩体（天空岛）：E 与 S1–S4 各有单独成组的 ArenaCover；灌木 / 绿篱不在实体组里。

证据都在 local-only 的 ArtSource 下（不进公开仓库），缺证据时返回 None，由调用的属性测试记 PARTIAL。
用法：python tools/map_readability_audit.py [tidegate|lastshift]，打印 JSON。
"""
from __future__ import annotations

import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LOW, HIGH = 0.12, 0.6
SOFT_KINDS = {'grassTuft', 'flowers', 'reedClump', 'kelp', 'shells', 'starfish', 'puddle'}


def _load(path: Path):
    return json.loads(path.read_text(encoding='utf-8')) if path.is_file() else None


def _rot(x, z, yaw):
    c, s = math.cos(yaw), math.sin(yaw)
    return x * c + z * s, -x * s + z * c


def audit_tidegate(boxes: dict, report: dict | None) -> list[str]:
    errors = []
    limit = boxes.get('lowLimit', HIGH)
    for e in boxes.get('Obb', []):
        if e['kind'] in ('wall', 'stair'):
            continue
        if LOW < e['visibleHeight'] < limit:
            errors.append('R2 %s：可见 %.2f m 却有实体（0.12–0.6 m 禁区）' % (e['id'], e['visibleHeight']))
    for kind, p in boxes.get('kindPolicy', {}).items():
        top = p['unitHeight'] * p['maxScale']
        if p['policy'] == 'none' and top > limit and kind not in SOFT_KINDS:
            errors.append('R1 %s：最高可见 %.2f m 却没有实体，也不是软类' % (kind, top))
    if 'kindPolicy' not in boxes:
        errors.append('R1 collision_boxes.json 缺 kindPolicy（tools/build_tidegate_blend.py 没重跑）')
    # R3：剩下的逻辑网格实心方盒里不得有按件出实体的物件中心
    for e in boxes.get('Obb', []):
        if e['source'] not in ('instance', 'object'):
            continue
        x, z = e['center'][0], e['center'][2]
        for b in boxes.get('Prop', []):
            if b['min'][0] <= x <= b['max'][0] and b['min'][2] <= z <= b['max'][2]:
                errors.append('R3 %s 还压在逻辑网格方盒 %s–%s 里' % (e['id'], b['min'], b['max']))
                break
    if not boxes.get('Obb'):
        errors.append('R3 collision_boxes.json 没有逐件盒子')
    if report is None:
        errors.append('R4 缺 ArtSource/Tidegate/tidegate_unity_build_report.json（Unity 构建报告）')
    else:
        stations = report.get('boundaryStations') or {}
        for need in ('Wood', 'Water', 'Steel', 'Road'):
            if not stations.get(need):
                errors.append('R4 边界缺 %s 段（boundaryStations=%s）' % (need, stations))
        if report.get('objectBoxColliders', 0) <= 0:
            errors.append('R3 Unity 构建报告里没有逐件碰撞盒')
    return errors


def audit_lastshift(boxes: dict, report: dict | None) -> list[str]:
    errors = []
    for kind, rows in boxes.items():
        if not isinstance(rows, list):
            continue
        for b in rows:
            h = b['size'][1]
            if 'segment' not in b and LOW < h < HIGH:
                errors.append('R2 %s：%.2f m 高的实心体在 0.12–0.6 m 禁区' % (b['id'], h))
            if kind == 'rail' and b['id'].startswith(('northRail', 'rampRail')):
                errors.append('R3 %s：坡道 / 梯段栏杆还是一整块水平盒' % b['id'])
    if not boxes.get('railSegments'):
        errors.append('R3 collision_boxes.json 没有跟坡分段的栏杆（railSegments）')
    if report is None:
        errors.append('R5 缺 ArtSource/LastShiftYard/lastshift_unity_build_report.json（Unity 构建报告）')
    else:
        overhead = report.get('overhead') or {}
        if overhead.get('boxes', 0) < 4:
            errors.append('R5 架空层碰撞缺失（overhead=%s）' % overhead)
    return errors


def audit_skyisland(geometry: dict) -> list[str]:
    errors = []
    meshes = geometry.get('visualMeshes', {})
    regions = {name.split('_')[1] for name in meshes if '_ArenaCover' in name}
    for need in ('E', 'S1', 'S2', 'S3', 'S4'):
        if need not in regions:
            errors.append('R6 %s 岛头目场没有单独成组的 ArenaCover 掩体' % need)
    for name in meshes:
        lower = name.lower()
        if ('bush' in lower or 'hedge' in lower) and '_Flora' not in name and '_GroundDetail' not in name:
            errors.append('R1 %s：灌木 / 绿篱进了实体组' % name)
    return errors


def run(map_id: str):
    """返回 (违规列表, 证据是否齐全)。"""
    if map_id == 'tidegate':
        art = ROOT / 'ArtSource' / 'Tidegate'
        boxes = _load(art / 'collision_boxes.json')
        if boxes is None:
            return None, False
        return audit_tidegate(boxes, _load(art / 'tidegate_unity_build_report.json')), True
    if map_id == 'lastshift':
        art = ROOT / 'ArtSource' / 'LastShiftYard'
        boxes = _load(art / 'collision_boxes.json')
        if boxes is None:
            return None, False
        return audit_lastshift(boxes, _load(art / 'lastshift_unity_build_report.json')), True
    if map_id == 'skyisland':
        geometry = _load(ROOT / 'ArtSource' / 'SkyIsland' / 'Validation' / 'sky_island_geometry.json')
        if geometry is None:
            return None, False
        return audit_skyisland(geometry), True
    raise ValueError(map_id)


def main(argv):
    maps = argv[1:] or ['tidegate', 'lastshift', 'skyisland']
    out, bad = {}, False
    for m in maps:
        errors, present = run(m)
        out[m] = {'evidence': present, 'violations': errors}
        bad |= bool(errors)
    print(json.dumps(out, ensure_ascii=False, indent=2))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
