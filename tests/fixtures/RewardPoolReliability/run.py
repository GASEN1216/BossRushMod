"""Run real reward pools against official search/fallback semantics, without player data."""
from pathlib import Path
import hashlib
import json
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build/runtime-regressions/RewardPoolReliability'
OUT.mkdir(parents=True, exist_ok=True)
paths = ['Integration/DailyReport/DailyReportRewards.cs', 'ModeH/ModeHRewardItemPool.cs',
         'ModeH/ModeHSeedStream.cs', 'Common/Loot/BossRushQualityItemPool.cs']
(OUT / 'sources.json').write_text(json.dumps({p: hashlib.sha256((ROOT/p).read_bytes()).hexdigest()
    for p in paths}, indent=2), encoding='utf-8')
result = subprocess.run(['dotnet', 'build', str(HERE/'Regression.csproj'), '-c', 'Release',
    '--output', str(OUT/'bin'), '-p:BaseIntermediateOutputPath='+str(OUT/'obj')+'/'], cwd=ROOT)
if result.returncode:
    raise SystemExit(result.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT/'bin/Regression.dll')], cwd=ROOT).returncode)
