"""全自动实机验收（F3「自动验收」）能离线证明的那一半：步骤表、剧情阶段、快照、线性对比度、可见度、manifest。

抽取对象（整份 `#if BOSSRUSH_DEV`，只去掉首尾那一对包裹，行号保持不变）：
- `DebugAndTools/F3GameplayValidationAutotestJudges.cs` 与 `F3GameplayValidationAutotestModels.cs`：纯判据与步骤表模型；
- `DebugAndTools/SkyIsland/SkyIslandStoryServiceAutotest.cs`：剧情存档门面的 Dev 入口（与链接的生产 SkyIslandStoryService 同一个 partial）。

**不要**给整个工程定义 BOSSRUSH_DEV：别的生产文件里的 Dev 区块引用 Unity，这里只有显式逐字抽出的文件进 Dev 口径。
Judges 的代码部分（剥掉注释与字符串字面量之后）一旦出现 Unity 标识，这里当场失败——那一份的全部意义就是能离线执行。
`SteamScreenshotNote` 这个字符串常量里写着 `UnityEngine.ScreenCapture`，是说明文字不是引用，所以扫描前先把字面量内容抹掉。

入口：`python tools/run_runtime_regressions.py --filter F3AutotestJudges`。
不要在本目录直接 `dotnet run`：会留下 bin/obj，之后聚合执行器报 CS0579。
"""
from pathlib import Path
import hashlib
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build' / 'runtime-regressions' / 'F3AutotestJudges'
JUDGES = 'DebugAndTools/F3GameplayValidationAutotestJudges.cs'
MODELS = 'DebugAndTools/F3GameplayValidationAutotestModels.cs'
STORY_AUTOTEST = 'DebugAndTools/SkyIsland/SkyIslandStoryServiceAutotest.cs'
FORBIDDEN = ('UnityEngine', 'Mathf.', 'GameObject', 'Transform', 'Texture2D')

sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source  # noqa: E402  共享的注释剥离状态机（不用正则）


def unwrap_dev(text, name):
    """去掉首个非空行 `#if BOSSRUSH_DEV` 与末个非空行 `#endif`，换成空行，行号与生产文件一致。"""
    lines = text.splitlines(keepends=True)
    filled = [i for i, line in enumerate(lines) if line.strip()]
    if not filled:
        raise SystemExit(name + ' 是空文件')
    first, last = filled[0], filled[-1]
    if lines[first].strip() != '#if BOSSRUSH_DEV' or lines[last].strip() != '#endif':
        raise SystemExit(name + ' 不再是整份 #if BOSSRUSH_DEV … #endif 包裹：抽取口径需要跟着改')
    body = lines[first + 1:last]
    for number, line in enumerate(body, first + 2):
        stripped = line.strip()
        if stripped.startswith('#') and 'BOSSRUSH_DEV' in stripped:
            # 内层再有 Dev 条件时，这里不定义符号会让那一段静默消失，等于没测。
            raise SystemExit(name + ':' + str(number) + ' 内层还有 BOSSRUSH_DEV 条件编译：' + stripped)
    return '\n' * (first + 1) + ''.join(body) + '\n'


def blank_literals(code):
    """把普通 / 逐字字符串与字符字面量的内容抹成空格，只留代码。插值字符串（$"…"）的洞里是代码，整段保留不抹。"""
    out = []
    i, n = 0, len(code)
    while i < n:
        ch = code[i]
        if ch == '$':
            # $"…" / $@"…" / @$"…"：保守地当代码扫描（可能假红，不会假绿）。
            j = i + 1
            while j < n and code[j] in '@$':
                j += 1
            out.append(code[i:j])
            if j < n and code[j] == '"':
                end = j + 1
                while end < n and code[end] != '"' and code[end] != '\n':
                    end += 2 if code[end] == '\\' else 1
                out.append(code[j:end + 1])
                i = end + 1
            else:
                i = j
            continue
        if ch == '@' and i + 1 < n and code[i + 1] == '"':
            j = i + 2
            while j < n:
                if code[j] == '"':
                    if j + 1 < n and code[j + 1] == '"':
                        j += 2
                        continue
                    break
                j += 1
            out.append('@"' + ''.join('\n' if c == '\n' else ' ' for c in code[i + 2:j]) + '"')
            i = j + 1
            continue
        if ch in '"\'':
            j = i + 1
            while j < n and code[j] != ch and code[j] != '\n':
                j += 2 if code[j] == '\\' else 1
            out.append(ch + ' ' * max(0, min(j, n) - i - 1) + (ch if j < n and code[j] == ch else ''))
            i = j + 1 if j < n and code[j] == ch else j
            continue
        out.append(ch)
        i += 1
    return ''.join(out)


def code_only(text):
    return blank_literals(clean_source(text))


def self_test_scanner():
    """扫描器自己不能把代码一起抹掉：否则禁用标识检查恒绿。"""
    assert 'UnityEngine' in code_only('using UnityEngine;\n'), 'scanner lost a using directive'
    assert 'Mathf.' in code_only('double x = Mathf.Abs(y);\n'), 'scanner lost a call'
    assert 'UnityEngine' not in code_only('const string s = "UnityEngine.ScreenCapture";\n'), 'string literal leaked'
    assert 'UnityEngine' not in code_only('// UnityEngine\n/* Transform */ int a;\n'), 'comment leaked'
    assert 'Transform' in code_only('var t = $"{Transform.x}";\n'), 'interpolated hole must stay scanned'
    assert 'GameObject' in code_only("char q = '\"'; GameObject g;\n"), 'char literal desynced the scanner'


if __name__ == '__main__':
    self_test_scanner()
    judges_src = (ROOT / JUDGES).read_text(encoding='utf-8-sig')
    models_src = (ROOT / MODELS).read_text(encoding='utf-8-sig')
    story_src = (ROOT / STORY_AUTOTEST).read_text(encoding='utf-8-sig')
    judges = unwrap_dev(judges_src, JUDGES)
    models = unwrap_dev(models_src, MODELS)
    story = unwrap_dev(story_src, STORY_AUTOTEST)
    scanned = code_only(judges + models)
    for anchor in ('class F3AutotestJudges', 'BossRushJsonParser', 'SkyIslandStoryCodec'):
        if anchor not in scanned:
            raise SystemExit('扫描视图里找不到 ' + anchor + '：清洗把代码也抹掉了，禁用标识检查没有意义')
    for token in FORBIDDEN:
        if token in scanned:
            line = scanned[:scanned.index(token)].count('\n') + 1
            raise SystemExit(JUDGES + ':' + str(line) + ' 引用了 Unity（' + token + '）：纯判据必须能脱离游戏执行')
    gen = OUT / 'gen'
    gen.mkdir(parents=True, exist_ok=True)
    (gen / 'AutotestJudges.cs').write_text(judges, encoding='utf-8')
    (gen / 'AutotestModels.cs').write_text(models, encoding='utf-8')
    (gen / 'SkyIslandStoryServiceAutotest.cs').write_text(story, encoding='utf-8')
    (gen / 'source.sha256').write_text(
        hashlib.sha256((judges_src + '\0' + models_src + '\0' + story_src).encode('utf-8')).hexdigest(), encoding='utf-8')
    build = subprocess.run([
        'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
        '--output', str(OUT / 'bin'),
        '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
    ], cwd=ROOT)
    if build.returncode:
        raise SystemExit(build.returncode)
    raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin' / 'Regression.dll'), str(ROOT)], cwd=ROOT).returncode)
