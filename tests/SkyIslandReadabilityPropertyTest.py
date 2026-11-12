# -*- coding: utf-8 -*-
"""天空岛读图通则（SkyIsland/AGENTS.md §6.1，2026-11-12）。

玩家反馈「太空旷、Boss 扎堆、没有掩体拉扯；分不清哪些能挡」。证据判据在 tools/map_readability_audit.py（R6）：
E 与 S1–S4 头目场各有单独成组的 ArenaCover；灌木 / 绿篱只在无实体组。源码不变式：
- 生成器：ArenaCover 各自成组、灌木压到 BUSH_LOW（≤ 0.6 m）、岛缘石块（H / S1 例外）进 _GroundDetail；
- 运行时：SkyIslandRendering 按名登记 ArenaCover 半掩体；遭遇组散开站位、45 m 错峰；遭遇战附近不拉新巡守。
证据（ArtSource/SkyIsland）不进公开仓库：缺证据时只查源码，退出码 2，--source-only 记 PARTIAL。
"""
import copy
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))
import map_readability_audit as audit  # noqa: E402


def read(rel):
    return (ROOT / rel).read_text(encoding='utf-8-sig')


def source_errors():
    errors = []
    gen = read('tools/generate_sky_island.py')
    for token in ("if '_ArenaCover' in obs['id']: CURRENT=obs['id']", "CURRENT=region+'_GroundDetail' if low else region"):
        if token not in gen:
            errors.append('generate_sky_island.py 缺：' + token)
    m = re.search(r'^BUSH_LOW = ([0-9.]+)', gen, re.M)
    if not m or float(m.group(1)) > 0.6:
        errors.append('generate_sky_island.py：BUSH_LOW 必须存在且 ≤ 0.6 m')
    if gen.count('BUSH_LOW') < 4:
        errors.append('generate_sky_island.py：岛缘、路边、门前三处灌木都要用 BUSH_LOW')
    nav = read('tools/sky_island_navigation.py')
    if nav.count('ArenaCover') < 1 or '"tall"' not in nav or '"half"' not in nav:
        errors.append('sky_island_navigation.py：头目场 ArenaCover（一高一矮）缺失')
    rendering = read('SkyIsland/SkyIslandRendering.cs')
    if 'RaidHalfCover.Register(root.transform, wallLayer, null, c => c.name.IndexOf("_ArenaCover", StringComparison.Ordinal) >= 0)' not in rendering:
        errors.append('SkyIslandRendering.Apply 没有按名登记 ArenaCover 半掩体')
    enc = read('SkyIsland/SkyIslandEncounters.cs')
    if 'SpreadRadii = { 6.5f, 4.5f, 3f }' not in enc or 'OtherGroupAliveNear(encounter, StaggerRange)' not in enc:
        errors.append('SkyIslandEncounters：遭遇组散开站位 / 45 m 错峰缺失')
    patrols = read('SkyIsland/SkyIslandPatrols.cs')
    tick = read('SkyIsland/SkyIslandSessionTick.cs')
    if 'Crowded(Position(cell))' not in patrols or 'patrols.Crowded = at => encounters.HasLivingEnemiesWithin(at, SkyIslandPatrols.CrowdedRadius);' not in tick:
        errors.append('SkyIslandPatrols：遭遇战附近不拉新巡守的门缺失或没注入')
    return errors


def main():
    errors = source_errors()
    violations, present = audit.run('skyisland')
    if present:
        errors += violations
        geometry = audit._load(ROOT / 'ArtSource/SkyIsland/Validation/sky_island_geometry.json')
        bad = copy.deepcopy(geometry)
        bad['visualMeshes'] = {k: v for k, v in bad['visualMeshes'].items() if '_ArenaCover' not in k or not k.startswith('VIS_E_')}
        bad['visualMeshes']['VIS_B_HedgeProbe_Leaf'] = {}
        missed = [tag for tag in ('R6 E', 'R1 VIS_B_HedgeProbe') if not any(e.startswith(tag) for e in audit.audit_skyisland(bad))]
        if missed:
            errors.append('判据自检没转红：' + ', '.join(missed))
    for e in errors:
        print('  [FAIL] ' + e)
    if not present and not errors:
        print('SkyIslandReadabilityPropertyTest: PARTIAL 缺 ArtSource/SkyIsland/Validation/sky_island_geometry.json（local-only 证据）')
        return 2
    print('SkyIslandReadabilityPropertyTest: ' + ('FAIL' if errors else 'PASS'))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
