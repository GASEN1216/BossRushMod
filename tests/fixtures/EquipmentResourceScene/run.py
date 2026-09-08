"""链接真实装备/飞行管理器，执行资源 Scene 往返回归；不启动游戏。"""
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build' / 'runtime-regressions' / 'EquipmentResourceScene'
build = subprocess.run([
    'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
    '--output', str(OUT / 'bin'), '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
], cwd=ROOT)
if build.returncode:
    raise SystemExit(build.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin/Regression.dll')], cwd=ROOT).returncode)
