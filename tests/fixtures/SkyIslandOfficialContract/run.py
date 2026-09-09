"""运行生产场景合同验证及本机官方 DLL 的只读 IL 契约核查。"""
from pathlib import Path
import os
import re
import subprocess

ROOT = Path(__file__).resolve().parents[3]
out = ROOT / 'Build/runtime-regressions/SkyIslandOfficialContract'
out.mkdir(parents=True, exist_ok=True)
game = os.environ.get('GAME_PATH')
if game:
    dll = Path(game) / 'Duckov_Data/Managed/TeamSoda.Duckov.Core.dll'
else:
    rsp = ROOT / 'Build/BossRush.rsp'
    match = re.search(r'/lib:"([^"]+)"', rsp.read_text(encoding='utf-8-sig')) if rsp.exists() else None
    dll = Path(match.group(1)) / 'TeamSoda.Duckov.Core.dll' if match else Path('missing-game-assembly')
if not dll.is_file():
    raise SystemExit('缺少官方 DLL：先设置 GAME_PATH 或执行 Windows 编译生成响应文件')
code = subprocess.call([
    'dotnet', 'build', str(Path(__file__).with_name('Regression.csproj')),
    '--configuration', 'Release',
    '-p:BaseIntermediateOutputPath=' + str(out / 'obj') + os.sep,
    '-p:BaseOutputPath=' + str(out / 'bin') + os.sep,
], cwd=ROOT)
if code:
    raise SystemExit(code)
raise SystemExit(subprocess.call(['dotnet', str(out / 'bin/Release/net8.0/Regression.dll'), str(dll)], cwd=ROOT))
