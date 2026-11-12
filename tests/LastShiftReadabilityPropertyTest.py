# -*- coding: utf-8 -*-
"""末班编组站读图通则（SkyIsland/AGENTS.md §6.1，2026-11-12）。

判据在 tools/map_readability_audit.py：0.12–0.6 m 不许有实心体；坡道 / 梯段 / 跨线桥引坡的栏杆跟坡分段；
跨线桥与司机室下不再是实心地面（架空层另起、桥墩立柱有实体）。另钉源码不变式：半掩体登记接在
LastShiftWorld.ApplyLayers 上、架空层按地面层分（COL_Ground 前缀）、站界栅栏在铁轨处断开并架限界门。
证据（ArtSource/LastShiftYard）不进公开仓库：缺证据时退出码 2，--source-only 记 PARTIAL。
"""
import copy
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))
import map_readability_audit as audit  # noqa: E402


def source_errors():
    errors = []
    world = (ROOT / 'LastShiftYard/LastShiftWorld.cs').read_text(encoding='utf-8')
    if 'RaidHalfCover.Register(collision, wallLayer, TryGroundAt)' not in world:
        errors.append('LastShiftWorld.ApplyLayers 没有登记半掩体（RaidHalfCover.Register）')
    if 'collider.name.StartsWith("COL_Ground", StringComparison.Ordinal)' not in world:
        errors.append('LastShiftWorld.ApplyLayers 没把 COL_Ground_Overhead（桥面 / 司机室楼面）分进地面层')
    details = (ROOT / 'docs/design/last-shift-yard/v2/js/yard-details.mjs')
    if details.is_file():
        text = details.read_text(encoding='utf-8')
        if 'station boundary fence' not in text or 'trainEnvelope(x,z)' not in text:
            errors.append('yard-details.mjs 站界栅栏缺失或没在铁轨处断开')
    blend = (ROOT / 'tools/build_lastshift_blend.py').read_text(encoding='utf-8')
    for token in ('def rail_segments(', 'def readable_solids(', 'LOW_LIMIT = 0.6'):
        if token not in blend:
            errors.append('build_lastshift_blend.py 缺读图环节：' + token)
    return errors


def self_check(boxes):
    bad = copy.deepcopy(boxes)
    bad.setdefault('crate', []).append({'id': 'probe', 'center': [0, 0.25, 0], 'size': [1, 0.5, 1], 'yaw': 0})
    bad['rail'] = bad.get('rail', []) + [{'id': 'rampRailProbe', 'center': [0, 0.5, 0], 'size': [0.2, 1, 9], 'yaw': 0}]
    errs = audit.audit_lastshift(bad, {'overhead': {'boxes': 0}})
    return [tag for tag in ('R2 probe', 'R3 rampRailProbe', 'R5') if not any(e.startswith(tag) for e in errs)]


def main():
    errors = source_errors()
    violations, present = audit.run('lastshift')
    if not present:
        for e in errors:
            print('  [FAIL] ' + e)
        print('LastShiftReadabilityPropertyTest: PARTIAL 缺 ArtSource/LastShiftYard/collision_boxes.json（local-only 证据）')
        return 1 if errors else 2
    errors += violations
    missed = self_check(audit._load(ROOT / 'ArtSource/LastShiftYard/collision_boxes.json'))
    if missed:
        errors.append('判据自检没转红：' + ', '.join(missed))
    for e in errors:
        print('  [FAIL] ' + e)
    print('LastShiftReadabilityPropertyTest: ' + ('FAIL' if errors else 'PASS'))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
