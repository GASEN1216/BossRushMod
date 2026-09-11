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
    'PanelWidth', 'Pad', 'Gap', 'BannerMaxHeight', 'BannerMinHeight', 'PortraitSize',
    'TitleMinHeight', 'TitleFontMin', 'TitleFontMax', 'BodyMinHeight', 'BodyPreferredMax',
    'ChoiceMinHeight', 'ChoicePadY', 'ChoicePadX', 'FooterHeight', 'ScrollbarGutter', 'KeyHintWidth')}
CONTENT_W = C['PanelWidth'] - C['Pad'] * 2

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


def layout(title, body, choices, has_portrait, has_banner, banner_aspect=1024.0 / 288.0):
    """逐字重放 SkyIslandStoryPresentation.Show 的算术。返回各块的高度与面板高。"""
    title_w = CONTENT_W - C['PortraitSize'] - C['Gap'] if has_portrait else CONTENT_W
    # 标题开了自动缩放，最坏情况按下限字号量（缩到下限还超就是省略号，不会溢出）
    title_h = max(C['TitleMinHeight'], text_height(title, title_w, C['TitleFontMin']))
    header_h = max(C['PortraitSize'], title_h) if has_portrait else title_h

    banner_h = 0.0
    if has_banner:
        banner_h = min(max(CONTENT_W / banner_aspect, C['BannerMinHeight']), C['BannerMaxHeight'])

    choice_hs = []
    choices_h = 0.0
    for i, label in enumerate(choices):
        # 选项左侧有数字键帽，文字可用宽度要再扣 KeyHintWidth（与生产 ChoiceLabelWidth 同一个算式）。
        h = max(C['ChoiceMinHeight'],
                text_height(label, CONTENT_W - C['ChoicePadX'] * 2 - C['KeyHintWidth'], 21.0)
                + C['ChoicePadY'] * 2)
        choice_hs.append(h)
        choices_h += h + (C['Gap'] * 0.5 if i > 0 else 0.0)

    body_natural = min(C['BodyPreferredMax'],
                       max(text_height(body, CONTENT_W - C['ScrollbarGutter'], 20.0), C['BodyMinHeight']))

    divider_block = 1.0 + C['Gap']
    chrome = C['Pad'] * 2 + header_h + divider_block + choices_h + C['FooterHeight'] + C['Gap'] * 3
    body_h = body_natural
    panel_h = chrome + banner_h + (C['Gap'] if banner_h > 0 else 0.0) + body_h
    if panel_h > MAX_PANEL:
        excess = panel_h - MAX_PANEL
        give = min(excess, max(0.0, body_h - C['BodyMinHeight']))
        body_h -= give
        excess -= give
        if excess > 0 and banner_h > 0:
            give = min(excess, max(0.0, banner_h - C['BannerMinHeight']))
            banner_h -= give
            excess -= give
            if excess > 0:
                excess -= banner_h + C['Gap']
                banner_h = 0.0
        panel_h = min(MAX_PANEL,
                      chrome + banner_h + (C['Gap'] if banner_h > 0 else 0.0) + body_h)
    return dict(panel=panel_h, banner=banner_h, header=header_h, body=body_h,
                choices=choice_hs, choices_total=choices_h, divider=divider_block)


def check(name, lay):
    """断言：面板不超屏；最后一个选项底边高于页脚顶边；所有块都在面板内。"""
    errors = []
    panel = lay['panel']
    if panel > MAX_PANEL + 0.5:
        errors.append('%s: 面板 %.0f 超出可用高度 %.0f' % (name, panel, MAX_PANEL))

    top = panel * 0.5 - C['Pad']
    if lay['banner'] > 0:
        top -= lay['banner'] + C['Gap']
    top -= lay['header'] + C['Gap']
    top -= lay['divider']
    top -= lay['body'] + C['Gap']
    for i, h in enumerate(lay['choices']):
        bottom = top - h
        top = bottom - C['Gap'] * 0.5
        if i == len(lay['choices']) - 1:
            footer_top = -panel * 0.5 + C['Pad'] + C['FooterHeight']
            if bottom < footer_top - 0.5:
                errors.append('%s: 第 %d 个选项底边 %.1f 压到页脚顶边 %.1f'
                              % (name, i + 1, bottom, footer_top))
        if bottom < -panel * 0.5:
            errors.append('%s: 第 %d 个选项掉出面板底边' % (name, i + 1))
    return errors


def worst_strings():
    src = read(STORY)
    point = src.split('internal static string PointName(', 1)[1].split('private bool BlockedByCombat', 1)[0]
    titles = [s for pair in re.findall(PAIR, point) for s in pair]
    lore = src.split('private static string Lore(', 1)[1]
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
    lore = [s for pair in re.findall(PAIR, story.split('private static string Lore(', 1)[1]) for s in pair]
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
    title = max([title] + extra_titles + three_titles, key=len)
    body = max([body] + extra_bodies + three_bodies + weave_bodies, key=len)
    labels = labels + extra_labels + three_labels + weave_labels
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

    # 破坏探针：判据本身必须会红，否则上面的全绿只是恒真。
    huge = layout(title, body, [longest_label] * 40, False, True)
    if not check('探针40选项', huge):
        errors.append('探针失效：40 个选项都不报错，说明判据恒真')

    if errors:
        for e in errors:
            print('  - ' + e)
        print('SkyIslandStoryPanelLayoutPropertyTest: FAIL')
        raise SystemExit(1)

    sample = layout(title, body, [longest_label] * 5, False, True)
    print('PASS SkyIslandStoryPanelLayoutPropertyTest '
          '(最长标题 %d 字 / 最长正文 %d 字 / %d 条选项文案；5 选项+横幅时面板 %.0f/%.0f px，'
          '15 种组合全部不溢出，破坏探针被拒)'
          % (len(title), len(body), label_count, sample['panel'], MAX_PANEL))


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
    assert titles and choices and len(intros) == 6 and len(pack) >= 2 and len(materials) == 7 and len(recipes) == 8, \
        '批次三文案没解析到，正则与源码失步了'
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


if __name__ == '__main__':
    main()
