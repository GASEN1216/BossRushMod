"""链接完整生产 SkyIslandCaptionQueue（纯逻辑、无 Unity 依赖），执行字幕排队的优先级 / 去重 / 队满丢弃规则。"""
from pathlib import Path
import os
import subprocess

ROOT = Path(__file__).resolve().parents[3]
out = ROOT / 'Build/runtime-regressions/SkyIslandHudPolicy'
out.mkdir(parents=True, exist_ok=True)
code = subprocess.call(['dotnet', 'build', str(Path(__file__).with_name('Regression.csproj')),
    '--configuration', 'Release',
    '-p:BaseIntermediateOutputPath=' + str(out / 'obj') + os.sep,
    '-p:BaseOutputPath=' + str(out / 'bin') + os.sep], cwd=ROOT)
if code:
    raise SystemExit(code)
raise SystemExit(subprocess.call(['dotnet', str(out / 'bin/Release/net8.0/Regression.dll')], cwd=ROOT))
