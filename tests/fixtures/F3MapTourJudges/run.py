"""F3 地图选择器逐图进场（MAP_TOUR_*）纯判据的执行回归。

链接生产文件 `DebugAndTools/F3GameplayValidationMapTourJudges.cs` 原样编译（不复制、不改写）。
先核它剥掉注释与字符串之后不引用 Unity：纯判据的意义就是能离线执行。
用例 id 两边同口径：把 `tools/gameplay_coverage.py` 对 `MAP_TOUR_*` 的展开与场景名成对传给程序，由生产 `CaseId` 逐个复算。

入口：`python tools/run_runtime_regressions.py --filter F3MapTourJudges`。不要在本目录直接 `dotnet run`（会留下 bin/obj）。
"""
from pathlib import Path
import json
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build' / 'runtime-regressions' / 'F3MapTourJudges'
JUDGES = 'DebugAndTools/F3GameplayValidationMapTourJudges.cs'
FORBIDDEN = ('UnityEngine', 'Mathf.', 'GameObject', 'Transform', 'Vector3', 'Physics.', 'AstarPath')

sys.path.insert(0, str(ROOT / 'tests'))
sys.path.insert(0, str(ROOT / 'tools'))
from cs_source_util import clean_source  # noqa: E402
import gameplay_coverage  # noqa: E402


def scene_pairs():
    rows = []
    for path in sorted((ROOT / 'Assets/SpawnPoints').glob('*.json')):
        row = json.loads(path.read_text(encoding='utf-8-sig'))
        if row.get('sceneName') and row.get('sceneID') and row.get('spawnPoints'):
            rows.append((row.get('sortOrder', 0), row['sceneName']))
    names = [name for _, name in sorted(rows)]
    ids = gameplay_coverage.expand_case('MAP_TOUR_*')
    if len(names) != len(ids) or not names:
        raise SystemExit('MAP_TOUR_* 展开与地图数据对不上：%d 张图 / %d 个 id' % (len(names), len(ids)))
    pairs = []
    for name, case in zip(names, ids):
        pairs += [name, case]
    return pairs


if __name__ == '__main__':
    code = clean_source((ROOT / JUDGES).read_text(encoding='utf-8-sig'))
    if 'class F3MapTourJudges' not in code:
        raise SystemExit('清洗后找不到 F3MapTourJudges：禁用标识检查没有意义')
    for token in FORBIDDEN:
        if token in code:
            raise SystemExit(JUDGES + ' 引用了 Unity（' + token + '）：纯判据必须能脱离游戏执行')
    build = subprocess.run([
        'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
        '--output', str(OUT / 'bin'),
        '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
    ], cwd=ROOT)
    if build.returncode:
        raise SystemExit(build.returncode)
    raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin' / 'Regression.dll')] + scene_pairs(), cwd=ROOT).returncode)
