"""执行生产地图租约与栅格化源码；可传几何 JSON 和预览 PPM 输出路径。"""
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build/runtime-regressions/StoneOutpostMap'
result = subprocess.run([
    'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
    '--output', str(OUT / 'bin'), '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
], cwd=ROOT)
if result.returncode:
    raise SystemExit(result.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin/Regression.dll'), *sys.argv[1:]], cwd=ROOT).returncode)
