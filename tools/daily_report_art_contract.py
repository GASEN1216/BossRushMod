"""日报动态控件区域必须留白；同一判据用于原图和正式 Sprite 解码像素。

2026-09-23 第五轮：图标改成运行时独立 Sprite 之后，落在卡片纯色区上的图标位也并进留白判据——
底图里再烤一份图标，运行时的 Sprite 就会叠在上面画第二遍（与签到格当年的双边框同一类毛病）。
落在缎带头、悬赏条上的图标位本来就不是卡片底色，不在判据里。
"""

DAILY_REPORT_ALIAS = 'assets/ui/dailyreport/daily_report_bg.png'

#: 落在卡片纯色区的运行时图标（版面表 "icons" 的键）。
FLAT_ICONS = ('issue', 'deadline', 'weather', 'income', 'bounty', 'headline', 'broadcast', 'fortune', 'gossip')


def legend_count(layout):
    """图例项数取自版面表（2026-09-22 起 5 项）；旧表没有 count 字段按 4 项。"""
    return int(layout['legend'].get('count', 4))


def flat_icons(layout):
    """版面表里存在的纯色区图标位；旧表（schema 1）没有 "icons" 段时为空。"""
    icons = layout.get('icons', {})
    return [name for name in FLAT_ICONS if name in icons]


def measure_chrome(image, layout):
    image = image.convert('RGBA')
    rects, grid, legend = layout['rects'], layout['grid'], layout['legend']
    width, height = layout['panel'][2:]

    def pixel(x, y):
        return tuple(image.getpixel((min(image.width - 1, int(x * image.width / width)),
                                     min(image.height - 1, int(y * image.height / height)))))

    signin = rects['signin']
    reference = pixel(signin[0] + signin[2] - 30, signin[1] + 12)
    regions = [('button', rects['button'])]
    for row in range(grid['rows']):
        for column in range(grid['columns']):
            regions.append(('cell_%d_%d' % (row, column), (
                grid['x'] + column * (grid['cellWidth'] + grid['gap']),
                grid['y'] + row * (grid['cellHeight'] + grid['gap']),
                grid['cellWidth'], grid['cellHeight'])))
    for index in range(legend_count(layout)):
        regions.append(('legend_%d' % index, (
            rects['legend'][0] + index * legend['itemWidth'],
            rects['legend'][1] + (rects['legend'][3] - legend['swatch']) / 2,
            legend['swatch'], legend['swatch'])))
    for name in flat_icons(layout):
        regions.append(('icon_' + name, tuple(layout['icons'][name])))

    measured = []
    for name, (x, y, w, h) in regions:
        # 取中心和边缘内侧；避开压缩块边界的轻微混色，允许 BC7 的小量误差。
        values = [pixel(x + u * w, y + v * h)
                  for u in (.1, .3, .5, .7, .9) for v in (.1, .3, .5, .7, .9)]
        delta = max(abs(value[channel] - reference[channel])
                    for value in values for channel in range(4))
        measured.append({'name': name, 'center': pixel(x + w / 2, y + h / 2), 'max_delta': delta})
    return {'size': list(image.size), 'reference': reference, 'regions': measured}


def validate_chrome(measured, layout, tolerance=4):
    if not measured:
        return ['Daily report production Sprite pixels were not inspected']
    expected = {'button'} | {'legend_%d' % i for i in range(legend_count(layout))} | {
        'cell_%d_%d' % (r, c)
        for r in range(layout['grid']['rows']) for c in range(layout['grid']['columns'])} | {
        'icon_' + name for name in flat_icons(layout)}
    rows = measured.get('regions', [])
    if len(rows) != len(expected) or {row['name'] for row in rows} != expected:
        return ['Daily report dynamic region coverage incomplete']
    return ['Daily report baked dynamic region: ' + row['name']
            for row in rows if row['max_delta'] > tolerance]
