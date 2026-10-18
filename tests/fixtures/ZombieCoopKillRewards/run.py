"""Execute host death settlement, co-op kill effects, and actual personal delivery."""
from pathlib import Path
import argparse
import hashlib
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=ROOT / 'Build/runtime-regressions/ZombieCoopKillRewards')
OUT = parser.parse_args().output.resolve()
OUT.mkdir(parents=True, exist_ok=True)


def member(path, signature):
    text = (ROOT / path).read_text(encoding='utf-8-sig')
    assert text.count(signature) == 1, signature
    start = text.index(signature)
    opening = text.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


wave = 'ZombieMode/ZombieModeRuntimeModule_WaveController.cs'
trigger = 'ZombieMode/ZombieModeRewardTriggerEffects.cs'
runtime = 'ZombieMode/ZombieModeRuntimeModule.cs'
generated = 'using System; using UnityEngine; namespace BossRush { internal sealed partial class ZombieModeRuntimeModule {\n'
for path, signature in (
    (wave, 'private void HandleZombieModeHealthDead('),
    (trigger, 'internal void HandleZombieModeOptionHealthDead('),
    (trigger, 'private static void TryApplyZombieModeTriggerLifesteal('),
    (runtime, 'internal static void TriggerDoomPulse('),
):
    generated += member(path, signature) + '\n'
(OUT / 'Production.cs').write_text(generated + '}}', encoding='utf-8')
paths = [HERE / 'Program.cs', OUT / 'Production.cs'] + [ROOT / path for path in (
    'ZombieMode/ZombieModeCoopKillRewards.cs', 'Utilities/CoopPersonalDelivery.cs',
    'Common/Coop/BossRushCoopJson.cs', 'Common/Data/BossRushJsonValue.cs', 'Utilities/SimpleJsonHelper.cs')]
(OUT / 'source-sha256.txt').write_text('\n'.join(hashlib.sha256(path.read_bytes()).hexdigest() + ' ' + str(path) for path in paths + [ROOT / wave, ROOT / trigger, ROOT / runtime]), encoding='utf-8')
includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in paths)
project = OUT / 'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
sys.path.insert(0, str(ROOT / 'tools'))
from run_runtime_regressions import run_project_fixture
code, output = run_project_fixture(project, OUT / 'runs', ROOT)
print(output)
raise SystemExit(code)
