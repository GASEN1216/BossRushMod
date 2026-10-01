"""普通巡守固定槽位从当前碰撞导航重新选点（可重复、结果确定）：`Assets/Data/SkyIsland/Patrols.json` 与 C# 兜底表一起写。

槽位必须落在当前 `ArtSource/SkyIsland/collision_navigation.json` 的导航三角形中心上
（`tests/SkyIslandPatrolPlacementPropertyTest.py`），导航一重烘，旧槽位就不再是三角形中心。

选点口径（2026-10-01 重写；owner 实机反馈旧槽位大多贴在岛的四角）：
1. 候选 = 本岛区每个导航三角形的中心，过属性测试的 `point_failures`：出生点 14 m、居民 8 m、交互点 4 m，
   以及「不在犄角旮旯」三条——离岛轮廓 ≥12 m（小岛 10 m）、离路网 / 桥头 / 地标 ≤20 m、离导航边界 ≥2 m。
   判据只在属性测试里写一份，这里直接调用。
2. 候选按「离路网 / 地标的距离」升序（同距再按 x、z），越靠玩家必经之处越先选。
3. 分散：从 30 m 的期望间距起，每次取排序最靠前、且与已选同区槽位都不近于期望间距的候选；
   取不到就把期望间距收 0.5 m 再找，最低到 SlotSpacing（5 m）仍凑不够就报错，不悄悄降判据。
   于是先沿路网铺开大间距，再往空处补，同区槽位在岛面上散开而不扎堆。
4. 每区数量取属性测试的 TARGETS（与 C# `TargetCount` 由属性测试互相核对）；id 按选中顺序编号 Patrol_<区>_NN。

输出：改写 JSON 的 `sourceNavigationSha256` 与 `slots`（`profiles` 原样保留）、C# `SkyIslandPatrolRules` 的
`SourceNavigationSha256` 常量与整段兜底槽位。生成后再跑 `tools/sky_island_runtime_placements.py`（巡守槽位进运行时表，
供场景审计查净空）与属性测试。

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
START_SPACING = 30.0  # 期望间距起点（m），约一屏多；2026-10-01 结果：大岛最终间距 11.5–21 m，钟庭与小岛 7–10 m
SPACING_STEP = 0.5
MIN_SPACING = 5.0  # 与 C# SlotSpacing、属性测试的同区间距一致


def _test():
    spec = importlib.util.spec_from_file_location('patrol_test', ROOT / 'tests/SkyIslandPatrolPlacementPropertyTest.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def number(value):
    """C# float 字面量：定点六位小数、不出现指数写法（属性测试按 -?\\d+(\\.\\d+)? 解析兜底表）。"""
    text = ('%.6f' % round(float(value), 6)).rstrip('0')
    return text + '0' if text.endswith('.') else text


def choose(test, ctx, region, count):
    faces = ctx[0]
    candidates = []
    for tri in faces[region]:
        center = tuple(sum(vertex[axis] for vertex in tri) / 3 for axis in range(3))
        if test.point_failures(center, region, ctx):
            continue
        reach = test.placement_metrics(center, region, ctx[4])[1]
        candidates.append((round(reach, 6), round(center[0], 6), round(center[2], 6), center))
    candidates.sort(key=lambda row: row[:3])
    chosen, spacing = [], START_SPACING
    while len(chosen) < count:
        pick = next((row for row in candidates
                     if all(test.horizontal(row[3], other[3]) >= spacing for other in chosen)), None)
        if pick is not None:
            chosen.append(pick)
            continue
        spacing -= SPACING_STEP
        if spacing < MIN_SPACING - 1e-9:
            raise SystemExit('岛区 %s 只凑出 %d / %d 个合格槽位（候选 %d）：先核对导航 / 路网，别降判据'
                             % (region, len(chosen), count, len(candidates)))
    nearest = [min(test.horizontal(a[3], b[3]) for b in chosen if b is not a) for a in chosen] if count > 1 else [0.0]
    return [row[3] for row in chosen], {'candidates': len(candidates), 'spacing': spacing,
                                        'minSpacing': round(min(nearest), 3)}


def main():
    test = _test()
    ctx = test.context()
    navigation_hash = ctx[3]
    data = json.loads(DATA.read_text(encoding='utf-8-sig'))
    slots, report = [], {}
    for region, count in test.TARGETS.items():
        centers, report[region] = choose(test, ctx, region, count)
        for index, center in enumerate(centers, 1):
            slots.append({'id': 'Patrol_%s_%02d' % (region, index), 'regionId': region,
                          'x': round(center[0], 6), 'y': round(center[1], 6), 'z': round(center[2], 6)})
    data['sourceNavigationSha256'] = navigation_hash
    data['slots'] = slots
    DATA.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

    raw = RULES.read_bytes()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig').replace('\r\n', '\n')
    text, count = re.subn(r'SourceNavigationSha256 = "[0-9a-f]{64}";', 'SourceNavigationSha256 = "%s";' % navigation_hash, text)
    assert count == 1, 'SourceNavigationSha256 constant not found exactly once'
    block = ''.join('                new SkyIslandPatrolSlot("%s", "%s", %sf, %sf, %sf),\n' % (
        slot['id'], slot['regionId'], number(slot['x']), number(slot['y']), number(slot['z'])) for slot in slots)
    text, count = re.subn(r'(?:[ \t]*new SkyIslandPatrolSlot\("[^"]+", "[^"]+", [^)\n]*\),\n)+',
                          lambda _: block, text)
    assert count == 1, 'fallback slot block not found exactly once'
    RULES.write_bytes((b'\xef\xbb\xbf' if bom else b'') + text.replace('\n', '\r\n').encode('utf-8'))
    print(json.dumps({'slots': len(slots), 'navigationSha256': navigation_hash, 'regions': report}, ensure_ascii=False))


if __name__ == '__main__':
    main()
