"""把 F3 天空岛运行时用例的「纯判据」区逐字抽出来，与生产纯规则一起编译执行（不启动 Unity、不碰存档）。

抽取对象：`DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs` 里 `#region 纯判据` 到它的 `#endregion`，
以及 `DebugAndTools/F3GameplayValidationSkyIsland.cs` 里的 `SkyIslandSkipCase`。纯判据区一旦引用 Unity，这里直接失败——
那一区的全部意义就是能在这里跑。

入口：`python tools/run_runtime_regressions.py --filter SkyIslandValidationJudges`。
不要在本目录直接 `dotnet run`：会留下 bin/obj，之后聚合执行器报 CS0579。
"""
from pathlib import Path
import hashlib
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build' / 'runtime-regressions' / 'SkyIslandValidationJudges'


def extract(source, signature):
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def pure_region(source):
    start = source.index('#region 纯判据')
    body_start = source.index('\n', start) + 1
    end = source.index('#endregion', body_start)
    return source[body_start:end]


if __name__ == '__main__':
    cases = (ROOT / 'DebugAndTools/F3GameplayValidationSkyIslandRuntimeCases.cs').read_text(encoding='utf-8-sig')
    suite = (ROOT / 'DebugAndTools/F3GameplayValidationSkyIsland.cs').read_text(encoding='utf-8-sig')
    judges = pure_region(cases)
    for token in ('UnityEngine', 'Mathf.', 'GameObject', 'Transform', 'SkyIslandSession ', 'NoteIndex.Instance'):
        if token in judges:
            raise SystemExit('纯判据区引用了 Unity 或运行时对象（' + token + '）：它必须能脱离游戏执行')
    skip_signal = extract(suite, 'internal sealed class SkyIslandSkipCase : Exception')
    generated = ('using System;\nusing System.Collections.Generic;\n\nnamespace BossRush\n{\n'
                 '    internal sealed partial class F3GameplayValidationRunner\n    {\n'
                 + judges + '    }\n\n    ' + skip_signal + '\n}\n')
    gen = OUT / 'gen'
    gen.mkdir(parents=True, exist_ok=True)
    (gen / 'Judges.cs').write_text(generated, encoding='utf-8')
    (gen / 'source.sha256').write_text(hashlib.sha256((judges + skip_signal).encode('utf-8')).hexdigest(), encoding='utf-8')
    build = subprocess.run([
        'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
        '--output', str(OUT / 'bin'),
        '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
    ], cwd=ROOT)
    if build.returncode:
        raise SystemExit(build.returncode)
    raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin' / 'Regression.dll')], cwd=ROOT).returncode)
