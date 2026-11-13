# -*- coding: utf-8 -*-
"""潮闸镇读图通则（SkyIsland/AGENTS.md §6.1，2026-11-12）。

玩家反馈「分不清哪些能挡子弹」「莫名其妙卡脚」「边界不明」。判据在 tools/map_readability_audit.py，
包括按实体语义分类、旧发布通行基线、逻辑网格盒与逐件盒分工及边缘实物。
另钉源码不变式：半掩体登记接在 TidegateWorld.ApplyLayers 上；同步脚本带上逻辑网格地块（边界按地块选实物）。
证据（ArtSource/Tidegate）不进公开仓库：缺证据时退出码 2，--source-only 记 PARTIAL。
"""
import copy
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))
import map_readability_audit as audit  # noqa: E402


def source_errors():
    errors = []
    world = (ROOT / 'Tidegate/TidegateWorld.cs').read_text(encoding='utf-8')
    if 'RaidHalfCover.Register(Root.transform.Find("Collision"), wallLayer, TryGroundAt)' not in world:
        errors.append('TidegateWorld.ApplyLayers 没有登记半掩体（RaidHalfCover.Register）')
    sync = (ROOT / 'tools/sync_tidegate_to_unity.py').read_text(encoding='utf-8')
    if "'Data/grid_zone.bytes'" not in sync:
        errors.append('sync_tidegate_to_unity.py 没把 grid_zone 同步给作者工程（边界按地块选实物要用）')
    blend = (ROOT / 'tools/build_tidegate_blend.py').read_text(encoding='utf-8')
    for token in ("boxes['kindPolicy']", "boxes['scatterThinned']", "boxes['collisionPolicy'] = 'solid-geometry'",
                  'building_obbs(', 'verify_standing_markers('):
        if token not in blend:
            errors.append('build_tidegate_blend.py 缺逐件碰撞环节：' + token)
    return errors


def self_check(boxes, report):
    """往旧台账塞 0.4 m 实体、新台账塞零厚度实体，两种台账都试无实体轮胎与缺边界。"""
    bad = copy.deepcopy(boxes)
    height = 0 if bad.get('collisionPolicy') == 'solid-geometry' else 0.4
    bad['Obb'].append({'id': 'probe', 'kind': 'tire', 'source': 'instance', 'center': [0, 1, 0], 'size': [1, height, 1], 'yaw': 0, 'visibleHeight': height})
    bad['kindPolicy']['tire'] = {'unitHeight': 0.9, 'policy': 'none', 'maxScale': 1}
    errs = audit.audit_tidegate(bad, dict(report, boundaryStations={}))
    return [tag for tag in ('R2 probe', 'R1 tire', 'R4') if not any(e.startswith(tag) for e in errs)]


def main():
    errors = source_errors()
    violations, present = audit.run('tidegate')
    if not present:
        for e in errors:
            print('  [FAIL] ' + e)
        print('TidegateReadabilityPropertyTest: PARTIAL 缺 ArtSource/Tidegate/collision_boxes.json（local-only 证据）')
        return 1 if errors else 2
    errors += violations
    boxes = audit._load(ROOT / 'ArtSource/Tidegate/collision_boxes.json')
    report = audit._load(ROOT / 'ArtSource/Tidegate/tidegate_unity_build_report.json') or {}
    missed = self_check(boxes, report)
    if missed:
        errors.append('判据自检没转红：' + ', '.join(missed))
    for e in errors:
        print('  [FAIL] ' + e)
    print('TidegateReadabilityPropertyTest: ' + ('FAIL' if errors else 'PASS'))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
