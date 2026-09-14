"""重放天空岛剧情面板的布局算术，证明「文字画到面板外」在结构上不可能发生。

## 为什么要有这份

旧版面板把每个元素钉在写死的 anchoredPosition 上、字号写死、`overflowMode` 取 TMP 默认的
Overflow —— 超框既不裁剪也不省略，直接画到面板外。实测有两处真的超：

  · 标题框 750×55 / 字号 30，三条英文 `PointName` 要 810~975 px，折两行需 72 px；
  · 第 6 个选项会和固定在 y=-282 的「继续旅程」叠在一起（今天最多 5 个选项，撞不上，
    但那是数出来的巧合，不是结构保证）。

新版改成「先量后排、容不下按优先级挤」。这份测试把那套算术在 Python 里重放一遍，
对**真实文案**跑最坏情况，断言：面板不超屏、所有元素都在面板内、选项与页脚不重叠。

## 它证明什么、不证明什么

估宽模型保守：CJK/全角按 1.0em、其余按 0.5em，比 TMP 实际字形**偏宽**，
因此这里放得下、实机一定放得下。它证明的是**算术**，不证明 Unity 的实际排版、
字体度量与 ScrollRect 手感——那些只能实机看，见 docs/制作教程/天空岛/天空岛_待人工验证清单.md。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source

PANEL = 'DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs'
STORY = 'DebugAndTools/SkyIsland/SkyIslandWorldStory.cs'
POINT_TEXT = 'DebugAndTools/SkyIsland/SkyIslandPointText.cs'
PAIR = r'L10n\.T\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)'


def read(rel):
    return clean_source((ROOT / rel).read_text(encoding='utf-8-sig'))


def const(src, name):
    """从生产源码读常量，**不在这里写第二份**：写死就会在生产改了之后继续用旧值算「安全」。"""
    m = re.search(r'\b' + name + r'\s*=\s*([0-9.]+)f', src)
    if not m:
        raise AssertionError('读不到面板常量 ' + name)
    return float(m.group(1))


PANEL_SRC = read(PANEL)
C = {n: const(PANEL_SRC, n) for n in (
    'PanelWidth', 'Pad', 'Gap', 'HeroMaxHeight', 'HeroMinHeight', 'HeroInset', 'PortraitSize',
    'TitleMinHeight', 'TitleFontMin', 'TitleFontMax', 'BodyMinHeight', 'BodyPreferredMax',
    'ChoiceMinHeight', 'ChoicePadY', 'ChoicePadX', 'ScrollbarGutter', 'KeyHintWidth',
    'DividerHeight', 'BodyFont', 'ChoiceFont', 'HeroFadeFraction')}
CONTENT_W = C['PanelWidth'] - C['Pad'] * 2


def cs_eval(expr, env, has_portrait):
    """把生产里的一条 C# 浮点表达式按 Python 求值。

    只认数字（带 f 后缀）、标识符、四则、`Mathf.Max` / `Mathf.Min` 与**顶层**的
    `portrait != null ? A : B`；认不出的写法直接抛异常——宁可红，也不许静默算成别的数。
    """
    e = re.sub(r'(\d+(?:\.\d+)?)f\b', r'\1', ' '.join(expr.split()))
    e = e.replace('Mathf.Max', 'max').replace('Mathf.Min', 'min')
    m = re.fullmatch(r'portrait != null \? (.+) : (.+)', e)
    if m:
        e = '(%s) if has_portrait else (%s)' % (m.group(1), m.group(2))
    if re.search(r'[^\w\s.+\-*/(),]', e):
        raise AssertionError('认不出的 C# 表达式：' + expr)
    scope = dict(env)
    scope['has_portrait'] = has_portrait
    return float(eval(e, {'__builtins__': {}, 'max': max, 'min': min}, scope))


def method_body(src, signature):
    """切出以 signature 开头的方法体（到配对的收尾大括号为止）。"""
    start = src.find(signature)
    if start < 0:
        raise AssertionError('找不到方法：' + signature)
    brace = src.index('{', start)
    depth = 0
    for i in range(brace, len(src)):
        if src[i] == '{':
            depth += 1
        elif src[i] == '}':
            depth -= 1
            if depth == 0:
                return src[brace + 1:i]
    raise AssertionError('方法体没有闭合：' + signature)


HERO_ASSIGN = re.compile(r'(?:\bfloat\s+)?\b(titleHeight|titleBlock|bandHeight)\s*=(?!=)\s*([^;]+);')


def show_title_block(src, title_h, has_portrait):
    """Show 里 `float titleBlock = …;` 按真实分支求值：它决定主视觉的地板，也是交给 BuildHero 的那一份。"""
    body = method_body(src, 'internal void Show(string title, string text, IList<Choice> choices,')
    m = re.search(r'\bfloat\s+titleBlock\s*=(?!=)\s*([^;]+);', body)
    if not m:
        raise AssertionError('Show 里找不到 titleBlock 的算式')
    env = dict(C)
    env['titleHeight'] = title_h
    return cs_eval(m.group(1), env, has_portrait)


def hero_geometry(src, title_block, hero_h, has_portrait):
    """按生产 BuildHero 的**真实分支**算它自己用的标题块与实底带高度，返回 (标题块, 实底带)。

    BuildHero 里按出现顺序的 `titleHeight` / `titleBlock` / `bandHeight` 赋值逐条求值，不在这里另写一份：
    2026-09-13 这份测试与离线预览都自己写了「标题块 = 标题本身」，而 BuildHero 里有立绘时另算的
    `max(PortraitSize, titleHeight)` 一直留着——居民面板的实底带被 208 的立绘顶到 169 高，
    盖掉 247 高插图的 68%，三处算术却全绿。
    """
    body = method_body(src, 'private static void BuildHero(')
    env = dict(C)
    env['titleBlock'] = title_block
    env['height'] = hero_h
    band = None
    for name, expr in HERO_ASSIGN.findall(body):
        env[name] = cs_eval(expr, env, has_portrait)
        if name == 'bandHeight':
            band = env[name]
            break
    if band is None:
        raise AssertionError('BuildHero 里找不到 bandHeight 的算式')
    return env['titleBlock'], band


# 一行标题的居民面板里，实底带最多占主视觉的一半（旧 bug 是 169 / 247 = 68%）。
BAND_MAX_SHARE = 0.5

# 参考分辨率 1920×1080、Expand 缩放，逻辑视口高度至少 1080。
# 生产里是 Clamp(viewport.y - 140, 520, 980)，最坏情况取下界。
MAX_PANEL = min(980.0, 1080.0 - 140.0)


def text_height(s, width, font):
    """保守估高：按 1.0em/0.5em 估宽折行，行高 1.25em，再加 MeasureTextHeight 的 +4。"""
    lines = 0
    for seg in s.replace('\\n', '\n').split('\n'):
        w = 0.0
        for ch in seg:
            o = ord(ch)
            wide = (0x2E80 <= o <= 0x9FFF) or (0xFF00 <= o <= 0xFFEF) or (0x3000 <= o <= 0x303F)
            w += (1.0 if wide else 0.5)
        w *= font
        lines += max(1, int(w // width) + (1 if w % width else 0))
    return lines * font * 1.25 + 4.0


def layout(title, body, choices, has_portrait, has_banner, banner_aspect=1024.0 / 288.0, src=None):
    """逐字重放 SkyIslandStoryPresentation.Show 的算术。返回各块的高度与面板高。

    2026-09-13 版式：标题（与立绘）压在**全出血**的主视觉上，插图不再单占一块。
    从上往下是 hero（无上 Pad）→ Gap → 分隔线 → Gap → 正文 → Gap → 选项 → Gap → 页脚 → 下 Pad。
    `src` 缺省为生产源码；破坏探针传入改过的源码，按同一套求值走一遍。
    """
    src = PANEL_SRC if src is None else src
    hero_content_w = C['PanelWidth'] - C['HeroInset'] * 2
    # 立绘贴主视觉右下角、不留 inset，标题让到左边：可用宽度 = 面板宽 − 左 inset − 立绘 − 间距。
    title_w = (C['PanelWidth'] - C['HeroInset'] - C['PortraitSize'] - C['Gap']
               if has_portrait else hero_content_w)
    # 标题开了自动缩放，最坏情况按下限字号量（缩到下限还超就是省略号，不会溢出）
    title_h = max(C['TitleMinHeight'], text_height(title, title_w, C['TitleFontMin']))
    # 标题块按生产 Show 的算式求值（不在这里写第二份）；BuildHero 自己用的那一份在收缩之后按真实分支再算。
    title_block = show_title_block(src, title_h, has_portrait)

    # hero 不会被整条撤掉（标题在它上面），只能压到这个地板；带立绘时还要装得下立绘。
    hero_floor = max(C['HeroMinHeight'], title_block + C['HeroInset'] * 2)
    if has_portrait:
        hero_floor = max(hero_floor, C['PortraitSize'])
    hero_art = 0.0
    if has_banner:
        # 全出血：按 PanelWidth 算，不是 CONTENT_W。
        hero_art = min(C['PanelWidth'] / banner_aspect, C['HeroMaxHeight'])
    hero_h = max(hero_floor, hero_art)

    choice_hs = []
    choices_h = 0.0
    for i, label in enumerate(choices):
        # 选项左侧有数字键帽，文字可用宽度要再扣 KeyHintWidth（与生产 ChoiceLabelWidth 同一个算式）。
        h = max(C['ChoiceMinHeight'],
                text_height(label, CONTENT_W - C['ChoicePadX'] * 2 - C['KeyHintWidth'], C['ChoiceFont'])
                + C['ChoicePadY'] * 2)
        choice_hs.append(h)
        choices_h += h + (C['Gap'] * 0.5 if i > 0 else 0.0)

    body_natural = max(text_height(body, CONTENT_W - C['ScrollbarGutter'], C['BodyFont']),
                       C['BodyMinHeight'])

    # 分隔线不再是 1px 裸 quad：它现在铺图集的 divider（8×8 / border 2），亮带在可拉伸中心区，
    # rect 高 1 时中心区归零、整条线画不出来。高度读生产常量，这里不写第二份。
    divider_block = C['DividerHeight'] + C['Gap']
    # 页脚「继续旅程」已删（2026-09-13）：它与 ESC 完全等价，纯冗余。
    # 现在是 hero → Gap → 分隔线 → Gap → 正文 → Gap → 选项 → 下 Pad，三个 Gap 里一个在 dividerBlock。
    chrome = C['Pad'] + hero_h + divider_block + choices_h + C['Gap'] * 2
    body_h = min(C['BodyPreferredMax'], body_natural)
    panel_h = chrome + body_h
    if panel_h > MAX_PANEL:
        excess = panel_h - MAX_PANEL
        give = min(excess, max(0.0, body_h - C['BodyMinHeight']))
        body_h -= give
        excess -= give
        if excess > 0:
            give = min(excess, max(0.0, hero_h - hero_floor))
            hero_h -= give
            chrome -= give
        panel_h = min(MAX_PANEL, chrome + body_h)
    # 实底带与 BuildHero 自己用的标题块：按生产 BuildHero 的真实分支求值（有立绘 / 无立绘各走各的）。
    hero_block, band = hero_geometry(src, title_block, hero_h, has_portrait)
    # 标题在 BuildHero 的标题块里垂直居中，上沿在 inset + block/2 + title_h/2，再加一个 inset 就是渐隐至少要有的高度。
    title_top = C['HeroInset'] * 2 + hero_block * 0.5 + title_h * 0.5
    return dict(panel=panel_h, hero=hero_h, hero_floor=hero_floor, title=title_block,
                hero_title=hero_block, band=band, art=hero_art, has_portrait=has_portrait,
                title_top=title_top, body=body_h, body_natural=body_natural,
                choices=choice_hs, choices_total=choices_h, divider=divider_block)


def resident_band_errors(name, lay):
    """一行标题 + 立绘 + 区域插图的居民面板：实底带不许盖掉大半张插图。"""
    if lay['art'] <= 0 or lay['hero'] <= 0:
        return ['%s: 没有插图，判据不适用' % name]
    share = lay['band'] / lay['hero']
    if share > BAND_MAX_SHARE:
        return ['%s: 实底带 %.0f 盖掉主视觉 %.0f 的 %.0f%%（上限 %.0f%%）'
                % (name, lay['band'], lay['hero'], share * 100, BAND_MAX_SHARE * 100)]
    return []


def check(name, lay):
    """断言：面板不超屏；最后一个选项底边高于页脚顶边；所有块都在面板内。"""
    errors = []
    panel = lay['panel']
    if panel > MAX_PANEL + 0.5:
        errors.append('%s: 面板 %.0f 超出可用高度 %.0f' % (name, panel, MAX_PANEL))

    # hero 全出血：顶上没有 Pad。
    top = panel * 0.5
    # 标题块必须整个落在 hero 里（它压在 hero 的渐隐上，掉出去就骑到亮插图上了）。
    if lay['title'] + C['HeroInset'] * 2 > lay['hero'] + 0.5:
        errors.append('%s: 标题块 %.1f + 上下 inset 装不进主视觉 %.1f'
                      % (name, lay['title'], lay['hero']))
    # 而且标题的**上沿**必须落在渐隐里，否则最上面那行会骑到亮插图上。
    fade = min(lay['hero'], max(lay['hero'] * C['HeroFadeFraction'], lay['title_top']))
    if lay['title_top'] > fade + 0.5:
        errors.append('%s: 标题上沿 %.1f 高过渐隐 %.1f，会骑到亮插图上'
                      % (name, lay['title_top'], fade))
    # Show 与 BuildHero 必须是同一份标题块（2026-09-14）：BuildHero 另算一份的话，
    # 居民面板的实底带会被立绘顶高、盖掉大半张插图，而上面的版式算术照样全绿。
    if abs(lay['hero_title'] - lay['title']) > 0.01:
        errors.append('%s: BuildHero 的标题块 %.1f 与 Show 的 %.1f 不是同一个口径（%s）'
                      % (name, lay['hero_title'], lay['title'], '有立绘' if lay['has_portrait'] else '无立绘'))
    top -= lay['hero'] + C['Gap']
    top -= lay['divider']
    top -= lay['body'] + C['Gap']
    for i, h in enumerate(lay['choices']):
        bottom = top - h
        top = bottom - C['Gap'] * 0.5
        if i == len(lay['choices']) - 1:
            # 页脚删掉之后，最后一项的下界判据变成面板自己的下 Pad。
            floor = -panel * 0.5 + C['Pad']
            if bottom < floor - 0.5:
                errors.append('%s: 第 %d 个选项底边 %.1f 越过面板下内边距 %.1f'
                              % (name, i + 1, bottom, floor))
        if bottom < -panel * 0.5:
            errors.append('%s: 第 %d 个选项掉出面板底边' % (name, i + 1))
    return errors


def worst_strings():
    src = read(STORY)
    point_src = read(POINT_TEXT)
    point = point_src.split('internal static string Name(', 1)[1].split('internal static string Brief(', 1)[0]
    titles = [s for pair in re.findall(PAIR, point) for s in pair]
    lore = point_src.split('internal static string Lore(', 1)[1]
    bodies = [s for pair in re.findall(PAIR, lore) for s in pair]
    labels = [s for pair in re.findall(
        r'(?:choices\.Add\(new SkyIslandStoryPresentation\.Choice\(|Add\(choices,\s*|Challenge\(|'
        r'ServiceChoice\(choices,\s*)\s*' + PAIR, src) for s in pair]
    assert titles and bodies and labels, '文案没解析到，正则与源码失步了'
    longest = lambda xs: max(xs, key=len)
    return longest(titles), longest(bodies), labels, len(labels)


def batch_two_strings():
    """内容批次二的面板文案：信的题目与正文、谜题页（见闻 + 场景 + 提问）与三个选项、名册的名字与整页、手记章节名。

    谜题页正文按「最长见闻 + 最长场景 + 最长提问」拼（中英混拼只会更长，偏保守）；名册一页按该页全部句子拼。
    """
    arg = r'"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"'
    puzzles = read('DebugAndTools/SkyIsland/SkyIslandPuzzles.cs')
    letters = read('DebugAndTools/SkyIsland/SkyIslandLetters.cs')
    crew = read('DebugAndTools/SkyIsland/SkyIslandCrew.cs')
    journal = read('DebugAndTools/SkyIsland/SkyIslandJournal.cs')
    story = read(STORY)
    options = [s for pair in re.findall(r'Option\(\s*' + arg, puzzles) for s in pair]
    prompts = [s for pair in re.findall(r'Step\(\s*' + arg, puzzles) for s in pair]
    intros = [s for m in re.findall(r'Puzzle\("Search_S\d",\s*SkyIslandStoryFlag\.\w+,\s*' + arg + r',\s*' + arg, puzzles)
              for s in m[2:4]]
    letter_rows = re.findall(r'Letter\("Letter_\d+",\s*"\w+",\s*"\w+",\s*[^,]+,\s*' + arg + r',\s*' + arg, letters)
    letter_titles = [s for m in letter_rows for s in m[0:2]]
    letter_bodies = [s for m in letter_rows for s in m[2:4]]
    names_block = crew.split('internal static string Name(int index)', 1)[1].split('internal static string Intro', 1)[0]
    crew_names = [s for pair in re.findall(r'case \d: return L10n\.T\(\s*' + arg, names_block) for s in pair]
    pages = crew.split('internal static string Page(int index', 1)[1].split('case ')[1:]
    crew_pages = [''.join(pair[0] for pair in re.findall(PAIR, page)) for page in pages] + \
                 [''.join(pair[1] for pair in re.findall(PAIR, page)) for page in pages]
    chapters_block = journal.split('internal static string ChapterName', 1)[1].split('internal static bool Recorded', 1)[0]
    chapter_names = [s for pair in re.findall(r'case \d: return L10n\.T\(\s*' + arg, chapters_block) for s in pair]
    lore = [s for pair in re.findall(PAIR, read(POINT_TEXT).split('internal static string Lore(', 1)[1])
            for s in pair]
    assert options and prompts and intros and letter_titles and crew_names and chapter_names and crew_pages and lore, \
        '批次二文案没解析到，正则与源码失步了'
    longest = lambda xs: max(xs, key=len)
    puzzle_page = longest(lore) + '\n\n' + longest(intros) + '\n\n' + longest(prompts)
    kept_letter = longest(letter_bodies) + '\n\n' + longest(
        [s for pair in re.findall(PAIR, story.split('private void ReadLetter(', 1)[1].split('private void ReleasePigeon', 1)[0])
         for s in pair])
    titles = letter_titles
    bodies = [puzzle_page, kept_letter, longest(crew_pages)]
    labels = options + crew_names + chapter_names
    return titles, bodies, labels


def main():
    title, body, labels, label_count = worst_strings()
    extra_titles, extra_bodies, extra_labels = batch_two_strings()
    three_titles, three_bodies, three_labels = batch_three_strings()
    weave_bodies, weave_labels = weave_strings()
    gnat_labels = gnat_strings()
    title = max([title] + extra_titles + three_titles, key=len)
    body = max([body] + extra_bodies + three_bodies + weave_bodies, key=len)
    labels = labels + extra_labels + three_labels + weave_labels + gnat_labels
    label_count = len(labels)
    longest_label = max(labels, key=len)
    errors = []

    # 最坏情况组合：最长标题 + 最长正文 + N 条最长选项，横幅与立绘两种形态各跑一遍。
    # 6 条：今天实际最多 5 条（风铃集留言板），多跑一条是给内容扩张留的预警。
    for count in (0, 1, 3, 5, 6):
        for has_portrait, has_banner, shape in ((False, True, '装置面板/横幅'),
                                                (True, False, '居民面板/立绘'),
                                                (False, False, '无插图')):
            lay = layout(title, body, [longest_label] * count, has_portrait, has_banner)
            errors += check('%s %d 选项' % (shape, count), lay)

    # 首次打开长正文时，滚动内容必须保留全部自然高度，而非 300px 的视口上限。
    long_body = body * 12
    scroll = layout(title, long_body, [], False, False)
    expected = text_height(long_body, CONTENT_W - C['ScrollbarGutter'], C['BodyFont'])
    assert scroll['body_natural'] >= expected > scroll['body']
    presentation = read(PANEL)
    natural = presentation.split('float bodyNatural =', 1)[1].split(';', 1)[0]
    assert 'BodyPreferredMax' not in natural, '自然高度不能截为视口上限，否则末段不可滚动到达'
    assert 'BuildBody(panel, bodyText, bodyHeight, bodyNatural, cursor)' in presentation

    # 破坏探针：判据本身必须会红，否则上面的全绿只是恒真。
    huge = layout(title, body, [longest_label] * 40, False, True)
    if not check('探针40选项', huge):
        errors.append('探针失效：40 个选项都不报错，说明判据恒真')

    # 居民面板（一行标题 + 立绘 + 区域插图）：实底带按生产 BuildHero 的真实分支算。
    resident = layout('晴禾的菜畦', '一句导语。', ['接一单委托'] * 3, True, True)
    errors += check('居民面板/一行标题', resident)
    errors += resident_band_errors('居民面板/一行标题', resident)

    # 破坏探针二：BuildHero 退回 2026-09-13 的写法（有立绘时另算 max(PortraitSize, titleHeight)）。
    # 「同一口径」与「实底带不许盖掉大半张插图」两条都必须红，否则这份测试又一次看不出这个缺陷。
    anchor = 'float titleHeight = titleBlock;'
    if anchor not in PANEL_SRC:
        errors.append('破坏探针二的锚点失效：BuildHero 里找不到 ' + anchor)
    else:
        legacy = PANEL_SRC.replace(anchor, anchor + '\n            titleBlock = portrait != null ? '
                                   'Mathf.Max(PortraitSize, titleHeight) : titleHeight;', 1)
        legacy_lay = layout('晴禾的菜畦', '一句导语。', ['接一单委托'] * 3, True, True, src=legacy)
        if not check('探针旧分支', legacy_lay):
            errors.append('破坏探针二失效：BuildHero 另算标题块时「同一口径」判据没有红')
        if not resident_band_errors('探针旧分支', legacy_lay):
            errors.append('破坏探针二失效：实底带盖掉 %.0f%% 插图时判据没有红'
                          % (legacy_lay['band'] / legacy_lay['hero'] * 100))

    if errors:
        for e in errors:
            print('  - ' + e)
        print('SkyIslandStoryPanelLayoutPropertyTest: FAIL')
        raise SystemExit(1)

    sample = layout(title, body, [longest_label] * 5, False, True)
    print('PASS SkyIslandStoryPanelLayoutPropertyTest '
          '(最长标题 %d 字 / 最长正文 %d 字 / %d 条选项文案；5 选项+横幅时面板 %.0f/%.0f px，'
          '15 种组合全部不溢出；居民面板实底带 %.0f/%.0f px（按 BuildHero 真实分支）；两个破坏探针被拒)'
          % (len(title), len(body), label_count, sample['panel'], MAX_PANEL, resident['band'], resident['hero']))


def batch_three_strings():
    """内容批次三的面板文案：合成台标题、正文（开场白 + 七种材料都在背包里时最长的摘要）、配方按钮与「打开合成台」一项。

    配方按钮与背包摘要由 `SkyIslandFieldcraftRules.RecipeLabel` / `PackSummary` 在运行时拼，这里照同一个格式把最坏情况拼出来：
    件数按两位数算，物品名取 `SkyIslandItemRules.NameCn` / `NameEn`。每站至多 4 条配方，面板按最坏 6 条选项复算。
    """
    rules = read('DebugAndTools/SkyIsland/SkyIslandFieldcraftRules.cs')
    items = read('DebugAndTools/SkyIsland/SkyIslandItemRules.cs')
    name_row = r'case BossRushItemIds\.(\w+): return "([^"]+)";'
    cn = dict(re.findall(name_row, items.split('internal static string NameCn(', 1)[1].split('internal static string NameEn(', 1)[0]))
    en = dict(re.findall(name_row, items.split('internal static string NameEn(', 1)[1].split('internal static string Name(', 1)[0]))

    def block(start, end):
        return rules.split(start, 1)[1].split(end, 1)[0]

    titles = [s for pair in re.findall(PAIR, block('internal static string StationName(', 'internal static string StationChoice(')) for s in pair]
    choices = [s for pair in re.findall(PAIR, block('internal static string StationChoice(', 'internal static string StationIntro(')) for s in pair]
    intros = [s for pair in re.findall(PAIR, block('internal static string StationIntro(', 'internal static string PackSummary(')) for s in pair]
    pack = re.findall(PAIR, block('internal static string PackSummary(', 'internal static string RecipeLabel('))
    materials = re.findall(r'BossRushItemIds\.(\w+)', block('internal static readonly int[] MaterialTypeIds', '};'))
    recipes = re.findall(r'Recipe\("\w+",\s*SkyIslandCraftStation\.\w+,\s*BossRushItemIds\.(\w+),\s*(\d+),'
                         r'((?:\s*In\(BossRushItemIds\.\w+,\s*\d+\),?)+)\)', rules)
    # 批次三 8 条 + 内容批次四 3 条（纱笠、灭蚊灯、蒲扇）；渡口工台因此有 5 条，面板仍按最坏 6 条选项复算。
    assert titles and choices and len(intros) == 6 and len(pack) >= 2 and len(materials) == 7 and len(recipes) == 11, \
        '批次三 / 四配方文案没解析到，正则与源码失步了'
    bodies = []
    labels = list(choices)
    for lang, names, opening, closing, verb in ((0, cn, '（', '）', '制作 '), (1, en, ' (', ')', 'Make ')):
        summary = pack[1][lang] + ' · '.join(names[m] + ' 30' for m in materials)
        for intro in intros[lang::2]:
            bodies.append(intro + '\n\n' + max(summary, pack[0][lang], key=len))
        for output, count, inputs in recipes:
            parts = [names[t] + ' %s/%s' % (n, n) for t, n in re.findall(r'In\(BossRushItemIds\.(\w+),\s*(\d+)\)', inputs)]
            labels.append(verb + names[output] + (' ×' + count if int(count) > 1 else '') + opening + ' · '.join(parts) + closing)
    return titles, bodies, labels


def weave_strings():
    """内容串联的面板文案：七处装置上的「点起风晶灯」按钮、还不会做的配方按钮、手记「总览 · 岛上的灯 · 群岛之物」一页的正文。

    按钮照 `SkyIslandLights.ChoiceLabel` / `SkyIslandFieldcraftRules.LockedLabel` 的格式拼最坏情况：件数按两位数算，
    还不会做的配方按「每件成品 × 每句门槛提示」全拼（比实际只多不少）；手记那一页把灯的一页与群岛之物的全部句子拼上。
    """
    lights = read('DebugAndTools/SkyIsland/SkyIslandLights.cs')
    rules = read('DebugAndTools/SkyIsland/SkyIslandFieldcraftRules.cs')
    journal = read('DebugAndTools/SkyIsland/SkyIslandJournal.cs')
    items = read('DebugAndTools/SkyIsland/SkyIslandItemRules.cs')
    name_row = r'case BossRushItemIds\.(\w+): return "([^"]+)";'
    cn = dict(re.findall(name_row, items.split('internal static string NameCn(', 1)[1].split('internal static string NameEn(', 1)[0]))
    en = dict(re.findall(name_row, items.split('internal static string NameEn(', 1)[1].split('internal static string Name(', 1)[0]))
    crystal = re.search(r'int crystal = BossRushItemIds\.(\w+);', lights).group(1)

    def block(source, start, end):
        return source.split(start, 1)[1].split(end, 1)[0]

    lamp_inputs = re.findall(r'Lamp\("Light_\w+",\s*"\w+",\s*"\w+",\s*"Letter_\d+",\s*"(?:[^"\\]|\\.)*",\s*"(?:[^"\\]|\\.)*",\s*'
                             r'((?:In\([^)]*\),?\s*)+)\)', lights)
    choice = re.findall(PAIR, block(lights, 'internal static string ChoiceLabel(', 'internal static string CostList('))
    locked = re.findall(PAIR, block(rules, 'internal static string LockedLabel(', 'internal static string LockedMessage('))
    hints = re.findall(PAIR, block(rules, 'internal static string UnlockHint(', 'internal static string LockedLabel('))
    outputs = re.findall(r'Recipe\("\w+",\s*SkyIslandCraftStation\.\w+,\s*BossRushItemIds\.(\w+),', rules)
    chapter = re.findall(PAIR, block(lights, 'internal static string Chapter(', 'private static SkyIslandLight[] Build('))
    uses = re.findall(PAIR, block(journal, 'internal static string Uses()', 'private static void Use('))
    assert len(lamp_inputs) == 7 and len(choice) == 2 and len(locked) == 3 and hints and outputs and chapter and uses, \
        '串联文案没解析到，正则与源码失步了'
    labels, bodies = [], []
    for lang, names in ((0, cn), (1, en)):
        for inputs in lamp_inputs:
            parts = [names[crystal if token == 'crystal' else token.split('.')[-1]] + ' 99/99'
                     for token, _ in re.findall(r'In\((\w+(?:\.\w+)?),\s*(\d+)\)', inputs)]
            labels.append(choice[0][lang] + ' · '.join(parts) + choice[1][lang])
        for output in outputs:
            for hint in hints:
                labels.append(locked[0][lang] + names[output] + locked[1][lang] + hint[lang] + locked[2][lang])
        bodies.append('\n'.join(pair[lang] for pair in chapter) + '\n\n' + '\n'.join(pair[lang] for pair in uses))
    return bodies, labels


def gnat_strings():
    """内容批次四的面板按钮：镜水寺池边「捧一团蛙卵」（夜里 / 白天两种写法）与蛙鸣池边「把蛙卵放回去」。

    照 `SkyIslandMosquitoRules.SpawnChoice` / `ReleaseChoice` 的拼法拼最坏情况：件数与进度都按两位数算。
    """
    rules = read('DebugAndTools/SkyIsland/SkyIslandMosquitoRules.cs')

    def block(start, end):
        return rules.split(start, 1)[1].split(end, 1)[0]

    spawn = re.findall(PAIR, block('internal static string SpawnChoice(', 'internal static string SpawnNeedsNight'))
    release = re.findall(PAIR, block('internal static string ReleaseChoice(', 'internal static string Released('))
    assert len(spawn) == 5 and len(release) == 2, '批次四蛙卵按钮文案没解析到，正则与源码失步了'
    labels = []
    for lang in (0, 1):
        labels.append(spawn[0][lang] + '99/99' + spawn[1][lang])
        labels.append(spawn[2][lang] + '99' + spawn[3][lang] + '99/99' + spawn[4][lang])
        labels.append(release[0][lang] + '99/99' + release[1][lang])
    return labels


if __name__ == '__main__':
    main()
