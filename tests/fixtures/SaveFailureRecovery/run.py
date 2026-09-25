"""Execute real slot stores, coordinators, daily rollover and item bet recovery without game I/O."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/save-failure-recovery'
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source

def member(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    begin = source.index('{', start)
    depth = 0
    for i in range(begin, len(source)):
        depth += (source[i] == '{') - (source[i] == '}')
        if depth == 0:
            return source[start:i + 1]
    raise AssertionError(signature)
files = [
    'Common/Lifecycle/BossRushSaveCoordinatorEngine.cs',
    'Common/Lifecycle/BossRushSlotJsonStore.cs',
    'Common/Lifecycle/BossRushSaveFileThrottle.cs',
    'Common/Data/BossRushJsonValue.cs', 'Utilities/SimpleJsonHelper.cs',
    'Integration/DailyReport/DailyReportService.cs',
    'Integration/DailyReport/DailyReportModels.cs',
    'Integration/DailyReport/DailyReportTuning.cs',
    'Integration/DailyReport/DailyReportCodec.cs',
    'Integration/DailyReport/DailyReportBounty.cs',
    'Integration/DailyReport/DailyReportPersistence.cs',
    'Integration/DailyReport/DailyReportSaveCoordinator.cs',
    'ModeH/ModeHSeedStream.cs', 'ModeH/ModeHCashBetService.cs',
    'ModeH/ModeHItemBetStake.cs',
]
config = clean_source((ROOT / 'ModeH/ModeHConfig.cs').read_text(encoding='utf-8-sig'))
names = ['MinOdds', 'MaxOdds', 'CashBetHouseCutPermille',
         'CashBetAssumedWinPermilleByOdds', 'CashBetAmounts',
         'CashBetCalibrationMinSamples', 'MinGameQuality', 'MaxGameQuality',
         'ItemBetMaxPrizeItems', 'ItemBetValuePermille', 'ItemBetPrizeBandLowPermille']
constants = []
for name in names:
    match = re.search(r'public (?:const|static readonly) [^\n;]+\b' + name + r'\s*=\s*[^;]+;', config)
    assert match, name
    constants.append(match.group(0))
bet = clean_source((ROOT / 'ModeH/ModeHRuntimeModule_BetFlow.cs').read_text(encoding='utf-8-sig'))
extracted = 'using System; using System.Collections.Generic; using ItemStatsSystem; namespace BossRush {\n'
extracted += 'static class ModeHConfig {\n' + '\n'.join(constants) + '\n}\n'
extracted += 'partial class ModeHRuntimeModule {\n' + member(bet, 'private void SettleReservedBet(ModeHCashBetRecord record, bool won)') + '\n}\n}\n'
(OUT / 'Extracted.cs').write_text(extracted, encoding='utf-8')

paths = [ROOT / p for p in files] + [OUT / 'Extracted.cs', HERE / 'Stubs.cs', HERE / 'Program.cs']
project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0067;0169;0414</NoWarn></PropertyGroup><ItemGroup>'
project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
project += '</ItemGroup></Project>'
(OUT / 'Regression.csproj').write_text(project, encoding='utf-8')
hashes = {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() for p in files + ['ModeH/ModeHConfig.cs', 'ModeH/ModeHRuntimeModule_BetFlow.cs']}
(OUT / 'production-sha256.json').write_text(json.dumps(hashes, indent=2) + '\n', encoding='utf-8')
raise SystemExit(subprocess.call(['dotnet', 'run', '--project', str(OUT / 'Regression.csproj'), '--configuration', 'Release'], cwd=ROOT))
