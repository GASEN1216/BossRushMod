"""玩家可见文本只能用官方中文字体一定画得出来的符号。

背景（2026-09-19 人工实测第 16 条）：雷霆戒指的提示里写了 ⚡，游戏内是个空白豆腐块，
玩家根本不知道那是什么。查下来同一类问题不止一处——冷淬液图标回退的 ❄、许愿池与
百战留痕选项前缀的 ✓、宿命回响选中标记的 ▶、征程 HUD 的 ✓/✗、诅咒词缀描述里的
U+2212 减号「−」、百科正文的 • ◦ ▪ 项目符号，全是同一类越界字符。

判据用 **GBK 可编码性**，不靠人肉记「这个字形应该有吧」：
官方字体是中文字体，GBK 收录的符号（★☆○●◎◇■□△▲※→←↑↓√Ⅰ①─ 等）必然有字形；
GBK 编不出来的（Emoji、Dingbats ✓✗❄❌、U+2212 减号、U+2022 圆点、U+2194 双向箭头）
一律禁止。ASCII 与 Latin-1（含 × ÷ ·）低于 U+2000，不在检查范围内。

只查生产代码里的字符串字面量。注释、DevLog/Debug 日志、`tests/` 夹具、
以及只写进 `summary.md` 给人在编辑器里看的报告文本，都不算玩家可见。
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SKIP_TOP = {
    'Build', '鸭科夫源码', '.git', 'node_modules', 'docs', '.qoder',
    'skills', 'codex-skills', 'tmp', 'tests', 'output', 'wiki-site', '.claude', '.kiro',
}

#: 整文件豁免：内容不进游戏 UI，只写进给人在编辑器里读的报告文件。
SKIP_FILES = {
    # 这里的字符串全部进 BossRushTestReports/<runId>/summary.md，由 owner / AI 用编辑器读。
    'DebugAndTools/F3GameplayValidationAutotestJudges.cs',
}

#: 单点例外：确认过实机能显示、且换掉反而更差的。加一条要写清依据。
EXCEPTIONS = {
    # 好感度心形气泡 2026-02 上线至今一直在用，实测能显示；换成文字反而看不出是好感。
    ('Integration/Utils/NPCHeartBubbleHelper.cs', '♥'),
}

STRING_RE = re.compile(r'"((?:[^"\\\n]|\\.)*)"')
LOG_RE = re.compile(r'\b(DevLog|Debug\.Log|Debug\.LogWarning|Debug\.LogError|CriticalLog)\s*\(')


def is_risky(ch):
    """U+2000 起、且 GBK 编不出来的字符视为「官方中文字体可能没有字形」。"""
    if ord(ch) < 0x2000:
        return False
    try:
        ch.encode('gbk')
    except UnicodeEncodeError:
        return True
    return False


def scan_plain_text(offenders):
    """游戏内百科正文与随包数据表也是玩家可见文本，同一条规矩。"""
    for sub in ('WikiContent', os.path.join('Assets', 'Data')):
        base = os.path.join(ROOT, sub)
        if not os.path.isdir(base):
            continue
        for dirpath, _dirnames, filenames in os.walk(base):
            for name in filenames:
                if not name.endswith(('.md', '.json', '.tsv')):
                    continue
                path = os.path.join(dirpath, name)
                rel = os.path.relpath(path, ROOT).replace(os.sep, '/')
                try:
                    text = open(path, encoding='utf-8', errors='ignore').read()
                except OSError:
                    continue
                for lineno, line in enumerate(text.splitlines(), 1):
                    for ch in line:
                        if not is_risky(ch) or (rel, ch) in EXCEPTIONS:
                            continue
                        offenders.append('%s:%d  U+%04X  %s' % (rel, lineno, ord(ch), line.strip()[:110]))
                        break


def main():
    offenders = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        if os.path.abspath(dirpath) == ROOT:
            dirnames[:] = [d for d in dirnames if d not in SKIP_TOP and not d.startswith('.')]
        for name in filenames:
            if not name.endswith('.cs'):
                continue
            path = os.path.join(dirpath, name)
            rel = os.path.relpath(path, ROOT).replace(os.sep, '/')
            if rel in SKIP_FILES:
                continue
            try:
                text = open(path, encoding='utf-8-sig', errors='ignore').read()
            except OSError:
                continue
            for lineno, line in enumerate(text.splitlines(), 1):
                stripped = line.lstrip()
                if stripped.startswith('//') or stripped.startswith('*'):
                    continue
                if LOG_RE.search(line):
                    continue
                for match in STRING_RE.finditer(line):
                    for ch in match.group(1):
                        if not is_risky(ch) or (rel, ch) in EXCEPTIONS:
                            continue
                        offenders.append('%s:%d  U+%04X  %s' % (rel, lineno, ord(ch), stripped[:110]))
                        break

    scan_plain_text(offenders)

    if offenders:
        print('PlayerFacingGlyphGuard: FAIL')
        for line in sorted(set(offenders)):
            print('  ' + line)
        print('  玩家可见文本只能用 GBK 收录的符号；确认实机能显示的写进 EXCEPTIONS 并注明依据。')
        return 1

    print('PlayerFacingGlyphGuard: PASS')
    return 0


if __name__ == '__main__':
    sys.exit(main())
