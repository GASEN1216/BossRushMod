# -*- coding: utf-8 -*-
"""末班编组站读图通则（SkyIsland/AGENTS.md §6.1，2026-11-12）。

判据在 tools/map_readability_audit.py：0.12–0.6 m 不许有实心体；坡道 / 梯段 / 跨线桥引坡的栏杆跟坡分段；
跨线桥与司机室下不再是实心地面（架空层另起、桥墩立柱有实体）。另钉源码不变式：半掩体登记接在
LastShiftWorld.ApplyLayers 上、架空层按地面层分（COL_Ground 前缀）、站界栅栏在铁轨处断开并架限界门。
证据（ArtSource/LastShiftYard）不进公开仓库：缺证据时退出码 2，--source-only 记 PARTIAL。

2026-11-12 第二轮（owner 实机八条）另钉：
- 共面分层：Blender 组装必须跑 layer_coplanar，组装报告里同层重叠为 0；作者工程着色器在 GBuffer / 深度两条顶点路径都按层偏移，
  实例合批把层号烘进 alpha（不动几何——几何错开会越挪越高）。
- 架空层逐三角形朝上：构建报告里司机室楼面、桥面从上往下先打到 COL_Ground_Overhead（以前司机室楼面朝下，人一进去就掉）。
- 顶棚：雨棚、桥、悬车按区切进 BLD_<区>_Roof，触发区与三栋楼同在 LastShiftIndoorRooms。
- 离线走格（作者工程 LastShiftWalkSweep）：没有地面破洞、没有只碰到地面碰撞的卡点。
- 交互提示按物体高度（LastShiftWorld.InteractOffset / CrateMarkerOffset），不再一律 +1 m；余响的对话点在她胸口。
- 室内顶灯：人在三栋楼里时排 2×2 盏不随昼夜的顶灯。
"""
import copy
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))
import map_readability_audit as audit  # noqa: E402
import unity_project_path  # noqa: E402
import json  # noqa: E402
import re  # noqa: E402

LIT_ROOMS = ('signal', 'repair', 'bond')
CANOPIES = ('awningW', 'coalBay', 'washBay', 'bondBay', 'crewLeanTo', 'waitingCanopy', 'bridge', 'loco017')


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
    errors += round2_source_errors(blend)
    return errors


def strip_cs(text):
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.S)
    return '\n'.join(re.sub(r'//.*$', '', line) for line in text.splitlines())


def round2_source_errors(blend):
    errors = []
    flat = blend.replace('\r\n', '\n')
    if 'def layer_coplanar(colls):' not in flat or '\n    layer_coplanar(colls)\n' not in flat:
        errors.append('build_lastshift_blend.py 没在组装里跑共面分层 layer_coplanar')
    if "layers.append(int(o.get('lsy_layer', 0)))" not in flat:
        errors.append('build_lastshift_blend.py 实例表没写共面层号 layers')
    code = {p.name: strip_cs(p.read_text(encoding='utf-8-sig')) for p in (ROOT / 'LastShiftYard').glob('*.cs')}
    for name, text in code.items():
        if re.search(r'GetOrCreateStandaloneInteractable<[^>]+>\([^;]*?new Vector3\(0f, 1f, 0f\)', text, re.S):
            errors.append(name + ': 交互点又写回一律 +1 m（应走 LastShiftWorld.InteractOffset / CrateMarkerOffset）')
    world = code.get('LastShiftWorld.cs', '')
    for token in ('internal static Vector3 InteractOffset(string id)', 'internal static Vector3 CrateMarkerOffset(string kind)'):
        if token not in world:
            errors.append('LastShiftWorld.cs 缺交互高度表：' + token)
    if 'loot.interactMarkerOffset = LastShiftWorld.CrateMarkerOffset(box.Kind);' not in code.get('LastShiftLoot.cs', ''):
        errors.append('LastShiftLoot.cs: 官方箱子预制件的提示高度没按本图箱柜改')
    talk = re.search(r'TalkLocal = new Vector3\(([-\d.]+)f, ([-\d.]+)f, ([-\d.]+)f\)', code.get('LastShiftYuXiang.cs', ''))
    if not talk or not 0.4 <= float(talk.group(2)) <= 0.8:
        errors.append('LastShiftYuXiang.cs: 余响的对话点要在她胸口（0.4–0.8 m），别回到头顶')
    lighting = code.get('LastShiftLighting.cs', '')
    for token in ('world.IndoorRoofs.TryGetInside(out room, out bounds)', 'Array.IndexOf(LitRooms, room) < 0', 'int take = Math.Min(MaxLamps - 1 - count, lamps.Count);'):
        if token not in lighting:
            errors.append('LastShiftLighting.cs 室内顶灯缺：' + token)
    if not all('"' + r + '"' in lighting for r in LIT_ROOMS):
        errors.append('LastShiftLighting.cs: 三栋楼都要点室内灯')
    project = unity_project_path.find_unity_project()
    if project:
        builder = strip_cs((Path(project) / 'Assets/Editor/LastShiftBuilder.cs').read_text(encoding='utf-8-sig'))
        for token in ('BuildCanopyRoofs(root);', 'layers = proto["layers"] as JArray', 'Overhead deck triangle', 'Overhead floor missing at three('):
            if token not in builder:
                errors.append('LastShiftBuilder.cs 缺第二轮环节：' + token)
        for cid in CANOPIES:
            if 'new Canopy("' + cid + '"' not in builder:
                errors.append('LastShiftBuilder.cs 顶棚表少了 ' + cid)
        shader = strip_cs((Path(project) / 'Assets/LastShiftYard/Shaders/LastShiftEnvironment.shader').read_text(encoding='utf-8-sig'))
        if shader.count('LastShiftLayerBias(') < 2:
            errors.append('LastShiftEnvironment.shader: GBuffer 与深度两条顶点路径都要按共面层偏移深度')
        if not (Path(project) / 'Assets/Editor/LastShiftWalkSweep.cs').is_file():
            errors.append('作者工程缺离线走格 LastShiftWalkSweep.cs')
    return errors


def round2_evidence_errors():
    errors = []
    art = ROOT / 'ArtSource/LastShiftYard'
    blend = json.loads((art / 'lastshift_blend_report.json').read_text(encoding='utf-8'))
    cop = blend.get('stats', {}).get('coplanar')
    if not cop or cop.get('sameLayerOverlap_m2', 1) > 0.01 or cop.get('capped'):
        errors.append('组装报告：共面分层缺失、同层仍有重叠或层号截顶：' + json.dumps(cop, ensure_ascii=False)[:200])
    unity = json.loads((art / 'lastshift_unity_build_report.json').read_text(encoding='utf-8'))
    ray = unity.get('probe', {}).get('raycasts', {})
    for key, want in (('three(-10,8)', 'COL_Ground_Overhead@5.20'), ('three(0,-49)', 'COL_Ground_Overhead@5.40')):
        first = [h for h in ray.get(key, '').split(' | ') if h.startswith('COL_Ground')][:1]
        if first != [want]:
            errors.append('构建报告：%s 从上往下先打到的地面应是 %s，实际 %s' % (key, want, first))
    rooms = unity.get('indoorRooms', {})
    missing = [r for r in LIT_ROOMS + CANOPIES if r not in rooms]
    if missing:
        errors.append('构建报告：室内 / 顶棚触发区缺 ' + ', '.join(missing))
    sweep = art / 'lastshift_walk_sweep.json'
    if not sweep.is_file():
        errors.append('缺离线走格报告 ArtSource/LastShiftYard/lastshift_walk_sweep.json')
    else:
        w = json.loads(sweep.read_text(encoding='utf-8'))
        if w.get('floorHoles', 1) != 0 or w.get('groundOnlyClusters', 1) != 0:
            errors.append('离线走格：地面破洞 %s、只碰地面碰撞的卡点 %s' % (w.get('floorHoles'), w.get('groundOnlyClusters')))
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
    errors += round2_evidence_errors()
    missed = self_check(audit._load(ROOT / 'ArtSource/LastShiftYard/collision_boxes.json'))
    if missed:
        errors.append('判据自检没转红：' + ', '.join(missed))
    for e in errors:
        print('  [FAIL] ' + e)
    print('LastShiftReadabilityPropertyTest: ' + ('FAIL' if errors else 'PASS'))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
