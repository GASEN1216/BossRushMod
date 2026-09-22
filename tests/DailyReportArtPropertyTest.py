"""按实际版面读原图与正式 Sprite，旧动态底图必须被拒绝；不代替 owner 目检。"""
from pathlib import Path
import copy
import json
import os
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from daily_report_art_contract import measure_chrome, validate_chrome


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
    assert validate_chrome(None, layout), '缺实际像素不能算通过'

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
    assert not validate(release, 'production_icons'), '正式发布验证必须消费相同像素判据'
    bad_release = copy.deepcopy(release)
    bad_release['daily_report_chrome']['regions'][0]['max_delta'] = 30
    assert any('baked dynamic region' in error for error in validate(bad_release, 'production_icons'))
    print('DailyReportArtPropertyTest: PASS (raw + actual production Sprite, 35 regions)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
