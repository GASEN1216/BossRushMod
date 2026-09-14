"""Execute the production permanent-NPC dialogue parser directly (only L10n / DevLog / Random are stubbed)."""
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build' / 'runtime-regressions' / 'PermanentDuckNpcDialogue'
build = subprocess.run([
    'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
    '--output', str(OUT / 'bin'),
    '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
], cwd=ROOT)
if build.returncode:
    raise SystemExit(build.returncode)
# 第 8 组断言读真实的 Assets/Data/DuckNpcs.json，所以工作目录必须是仓库根。
raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin' / 'Regression.dll')], cwd=ROOT).returncode)
