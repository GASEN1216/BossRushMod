"""按实际版面读原图与正式 Sprite，旧动态底图必须被拒绝；不代替 owner 目检。"""
from pathlib import Path
import copy
import json
import os
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from daily_report_art_contract import measure_chrome, validate_chrome, measure_source_drift, validate_source_drift


def main():
    from PIL import Image, ImageDraw
    layout = json.loads((ROOT / 'Assets/Data/DailyReportLayout.json').read_text(encoding='utf8'))
    # 判据的输入独立于生成器：任一动态位置烤色都不能被卡片内同色比较漏掉。
    clean = Image.new('RGBA', tuple(layout['panel'][2:]), (252, 248, 240, 255))
    assert not validate_chrome(measure_chrome(clean, layout), layout)
    bad = clean.copy()
    grid = layout['grid']
    x = grid['x'] + (grid['columns'] - 1) * (grid['cellWidth'] + grid['gap'])
    y = grid['y'] + (grid['rows'] - 1) * (grid['cellHeight'] + grid['gap'])
    ImageDraw.Draw(bad).rectangle((x, y, x + grid['cellWidth'] - 1, y + grid['cellHeight'] - 1), fill=(226, 219, 205, 255))
    assert validate_chrome(measure_chrome(bad, layout), layout), '最后一格的烤色未被检测'
    # 2026-09-23：图标改成运行时 Sprite 后，纯色区的图标位也必须留白，否则底图与运行时各画一遍
    assert 'icons' in layout and 'income' in layout['icons'], '版面表缺少运行时图标位'
    baked = clean.copy()
    ix, iy, iw, ih = layout['icons']['income']
    ImageDraw.Draw(baked).ellipse((ix, iy, ix + iw - 1, iy + ih - 1), fill=(62, 122, 76, 255))
    baked_errors = validate_chrome(measure_chrome(baked, layout), layout)
    assert any('icon_income' in error for error in baked_errors), '烤进底图的图标未被检测'
    assert validate_chrome(None, layout), '缺实际像素不能算通过'
    # 2026-09-24：版面下移后原图重生成、包没重打，旧 Sprite 必须被同源判据拒绝，哪怕动态区碰巧还在纯色上。
    paper = Image.new('RGBA', (400, 300), (250, 246, 237, 255))
    ImageDraw.Draw(paper).rectangle((40, 40, 360, 200), fill=(226, 219, 205, 255))
    moved = Image.new('RGBA', paper.size, (250, 246, 237, 255))
    ImageDraw.Draw(moved).rectangle((40, 100, 360, 260), fill=(226, 219, 205, 255))
    assert not validate_source_drift(measure_source_drift(paper, paper.resize((200, 150)))), '同源缩放不能误报'
    assert validate_source_drift(measure_source_drift(paper, moved)), '版面挪动后的旧底图未被检测'
    assert validate_source_drift(None), '没比对原图不能算通过'

    raw = ROOT / 'Assets/ui/DailyReport/daily_report_bg.png'
    bundle = ROOT / 'Assets/ui/production_icons'
    if os.environ.get('BOSSRUSH_GUARD_SOURCE_ONLY') == '1':
        print('DailyReportArtPropertyTest: PARTIAL (source-only; actual release pixels not inspected)')
        return 2
    assert raw.is_file() and bundle.is_file(), '日报原图或正式 production_icons 包缺失'
    with Image.open(raw) as image:
        assert not validate_chrome(measure_chrome(image, layout), layout, tolerance=0)
    from verify_unity_resource_release import inspect, validate
    release = inspect(bundle)
    measured = release.get('daily_report_chrome')
    errors = validate_chrome(measured, layout)
    assert not errors, errors
    assert not validate_source_drift(measured.get('source_drift')), measured.get('source_drift')
    assert not validate(release, 'production_icons'), '正式发布验证必须消费相同像素判据'
    bad_release = copy.deepcopy(release)
    bad_release['daily_report_chrome']['regions'][0]['max_delta'] = 30
    assert any('baked dynamic region' in error for error in validate(bad_release, 'production_icons'))
    stale_release = copy.deepcopy(release)
    stale_release['daily_report_chrome']['source_drift'] = {'mean': 10.2, 'over16': 0.2}
    assert any('is stale' in error for error in validate(stale_release, 'production_icons'))
    print('DailyReportArtPropertyTest: PASS (raw + actual production Sprite, %d regions)' % len(measured['regions']))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
