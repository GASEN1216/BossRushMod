"""把一段台词按生产口径（SkyIslandResidentDialogue.Split）切成「一句一屏」，只用来核对改写前后屏数一致。

不是守卫，不进 run_guards：这是改文案时的一次性尺子。官方对话逐屏推进，
全自动验收的 `shot:xxx_lineN` 与 `assert:dialogue_line_contains` 按屏序取句，
屏数一变，断言就会落到别的句子上。
"""
import sys

TERMINAL = set('。！？；…!?.')
CLOSING = set('」』”’）)"】》')
MIN_SENTENCE_CHARS = 8


def _ends_sentence(text, index):
    c = text[index]
    if c not in TERMINAL:
        return False
    last = index + 1 >= len(text)
    if not last and text[index + 1] in TERMINAL:
        return False
    if c in '.!?':
        if last:
            return True
        nxt = text[index + 1]
        return nxt == ' ' or nxt in CLOSING
    return True


def _visible(buf):
    return sum(1 for ch in buf if not ch.isspace())


def split(body):
    lines = []
    if not body:
        return lines
    buf = []
    for paragraph in body.split('\n'):
        paragraph = paragraph.strip()
        i = 0
        while i < len(paragraph):
            buf.append(paragraph[i])
            if _ends_sentence(paragraph, i):
                while i + 1 < len(paragraph) and paragraph[i + 1] in CLOSING:
                    i += 1
                    buf.append(paragraph[i])
                if _visible(buf) >= MIN_SENTENCE_CHARS:
                    text = ''.join(buf).strip()
                    buf = []
                    if text:
                        lines.append(text)
            i += 1
        text = ''.join(buf).strip()
        buf = []
        if text:
            lines.append(text)
    return lines


if __name__ == '__main__':
    for arg in sys.argv[1:]:
        screens = split(arg)
        print('%d 屏 | %s' % (len(screens), ' // '.join(screens)))
