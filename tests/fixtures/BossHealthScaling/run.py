"""Exercise production health scaling and its configuration/host entry points."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/BossHealthScaling'
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / 'tests/fixtures/ModeDEntryOwnership'))
from run import member

config = (ROOT / 'Config/Config.cs').read_text(encoding='utf-8-sig')
bridge = (ROOT / 'Utilities/Utilities.cs').read_text(encoding='utf-8-sig')
waves = (ROOT / 'ModeD/ModeDRuntimeModule_Waves.cs').read_text(encoding='utf-8-sig')
child_path = ROOT / 'Integration/DragonKing/DragonKingAbilityController_ChildProtection.cs'
child = child_path.read_text(encoding='utf-8-sig')
king_config = (ROOT / 'Integration/DragonKing/DragonKingConfig.cs').read_text(encoding='utf-8-sig')
child_multiplier = next(line.strip() for line in king_config.splitlines()
                        if 'const float ChildProtectionDescendantStatMultiplier =' in line)
field = next(line.strip() for line in config.splitlines() if 'public int bossHealthPercent =' in line)
(OUT / 'Host.cs').write_text('using UnityEngine; namespace BossRush { public partial class ModBehaviour {\n'
    + 'private class BossRushConfig { public float bossStatMultiplier = 1f; ' + field + ' }\n'
    + member(config, 'internal float GetBossHealthMultiplier()') + '\n'
    + member(bridge, 'private void ApplyBossStatMultiplier(') + '\n} }', encoding='utf-8')
(OUT / 'Wave.cs').write_text('using System; using UnityEngine; using ItemStatsSystem; using ItemStatsSystem.Stats; '
    + 'namespace BossRush { internal partial class ModeDRuntimeModule { '
    + member(waves, 'private void ApplyModeDWaveScaling(') + ' } }', encoding='utf-8')
paths = [ROOT / 'Utilities/BossStatScaling.cs', HERE / 'Program.cs', OUT / 'Host.cs', OUT / 'Wave.cs']
(OUT / 'Child.cs').write_text('using System; namespace BossRush { internal static class DragonKingConfig { '
    + child_multiplier + ' } internal partial class DragonKingAbilityController { '
    + member(child, 'private void ApplyDescendantStatReduction(') + ' } }', encoding='utf-8')
paths.append(OUT / 'Child.cs')
project = OUT / 'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    + '</ItemGroup></Project>', encoding='utf-8')
(OUT / 'production-sha256.json').write_text(json.dumps({str(p): hashlib.sha256(p.read_bytes()).hexdigest()
    for p in [ROOT / 'Utilities/BossStatScaling.cs', ROOT / 'Utilities/Utilities.cs', ROOT / 'Config/Config.cs',
              ROOT / 'ModeD/ModeDRuntimeModule_Waves.cs', child_path, ROOT / 'Integration/DragonKing/DragonKingConfig.cs']}, indent=2))
raise SystemExit(subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT))
