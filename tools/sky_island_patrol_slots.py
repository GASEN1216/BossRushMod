"""普通巡守固定槽位随碰撞导航重取样（可重复）：`Assets/Data/SkyIsland/Patrols.json` 与 C# 兜底表一起写。

槽位必须落在当前 `ArtSource/SkyIsland/collision_navigation.json` 的导航三角形中心上
（`tests/SkyIslandPatrolPlacementPropertyTest.py`），导航一重烘，旧槽位就不再是三角形中心。
本工具保留每个槽位的 id、区域与顺序，逐个挑同区里离旧坐标最近、同时满足出生点 14 m、居民 8 m、
交互点 4 m、同区间距 5 m 的新三角形中心；避让口径直接用属性测试的 `context()`（静态交互体取当前 C# 落点），
不另写第二份。改写 JSON 的 `sourceNavigationSha256`、C# `SkyIslandPatrolRules` 的
`SourceNavigationSha256` 常量与 112 行兜底槽位。

用法：python tools/sky_island_patrol_slots.py
"""
import importlib.util
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / 'Assets/Data/SkyIsland/Patrols.json'
RULES = ROOT / 'SkyIsland/SkyIslandPatrolRules.cs'


def _test():
    spec = importlib.util.spec_from_file_location('patrol_test', ROOT / 'tests/SkyIslandPatrolPlacementPropertyTest.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def number(value):
    return repr(round(float(value), 6))


def main():
    test = _test()
    faces, markers, protected, navigation_hash = test.context()
    data = json.loads(DATA.read_text(encoding='utf-8-sig'))
    residents = [markers[m] for m in test.RESIDENT_MARKERS]
    spawn = markers['PlayerSpawn']
    chosen, moved = [], []
    for slot in data['slots']:
        region = slot['regionId']
        old = (slot['x'], slot['y'], slot['z'])
        centers = sorted((tuple(sum(v[k] for v in tri) / 3 for k in range(3)) for tri in faces[region]),
                         key=lambda c: math.hypot(c[0] - old[0], c[2] - old[2]))
        for center in centers:
            if test.horizontal(center, spawn) < 14 or any(test.horizontal(center, r) < 8 for r in residents):
                continue
            if any(test.horizontal(center, position) < 4 for _, position in protected):
                continue
            if any(other['regionId'] == region and test.horizontal(center, (other['x'], other['y'], other['z'])) < 5
                   for other in chosen):
                continue
            new = dict(slot, x=round(center[0], 6), y=round(center[1], 6), z=round(center[2], 6))
            chosen.append(new)
            moved.append(math.hypot(new['x'] - old[0], new['z'] - old[2]))
            break
        else:
            raise SystemExit('no valid triangle centre for ' + slot['id'])
    data['sourceNavigationSha256'] = navigation_hash
    data['slots'] = chosen
    DATA.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    raw = RULES.read_bytes().decode('utf-8-sig')
    bom = RULES.read_bytes().startswith(b'\xef\xbb\xbf')
    text = raw.replace('\r\n', '\n')
    text, count = re.subn(r'SourceNavigationSha256 = "[0-9a-f]{64}";', 'SourceNavigationSha256 = "%s";' % navigation_hash, text)
    assert count == 1, 'SourceNavigationSha256 constant not found exactly once'
    for slot in chosen:
        pattern = r'new SkyIslandPatrolSlot\("%s", "%s", -?[0-9.]+f, -?[0-9.]+f, -?[0-9.]+f\)' % (slot['id'], slot['regionId'])
        replacement = 'new SkyIslandPatrolSlot("%s", "%s", %sf, %sf, %sf)' % (
            slot['id'], slot['regionId'], number(slot['x']), number(slot['y']), number(slot['z']))
        text, count = re.subn(pattern, replacement, text)
        assert count == 1, 'fallback slot not found exactly once: ' + slot['id']
    RULES.write_bytes((b'\xef\xbb\xbf' if bom else b'') + text.replace('\n', '\r\n').encode('utf-8'))
    print(json.dumps({'slots': len(chosen), 'navigationSha256': navigation_hash,
                      'maxMoveMetres': round(max(moved), 3), 'meanMoveMetres': round(sum(moved) / len(moved), 3)}))


if __name__ == '__main__':
    main()
