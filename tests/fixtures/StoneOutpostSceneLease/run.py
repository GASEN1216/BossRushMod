"""在 Build 下编译并执行生产租约回归，不启动游戏。"""
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build' / 'runtime-regressions' / 'StoneOutpostSceneLease'
build = subprocess.run([
    'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
    '--output', str(OUT / 'bin'),
    '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
], cwd=ROOT)
if build.returncode:
    raise SystemExit(build.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin' / 'Regression.dll')], cwd=ROOT).returncode)
