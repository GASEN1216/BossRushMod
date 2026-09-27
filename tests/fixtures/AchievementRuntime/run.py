"""Compile complete achievement production owners and current root binding/cleanup statements."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/AchievementRuntime'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    paths = [ROOT / 'Achievement' / name for name in (
        'AchievementTriggers.cs', 'AchievementRuntimeModule.cs', 'AchievementRuntimeHooks.cs', 'AchievementMedalItem.cs')]
    hashes = {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}
    def read(relative):
        path = ROOT / relative
        raw = path.read_bytes()
        hashes[relative] = hashlib.sha256(raw).hexdigest()
        return raw.decode('utf-8-sig')
    registration = read('ModBehaviourRuntimeModules.cs')
    start = registration.index('achievementRuntime = new AchievementRuntimeModule();')
    end = registration.index('runtimeModuleHost.Register(achievementRuntime);', start)
    binding = registration[start:end + len('runtimeModuleHost.Register(achievementRuntime);')]
    host = read('ModBehaviour.cs')
    start = host.index('            CleanupAchievementRuntime();')
    end = host.index('            SafeRuntime.Run("BossRushUI.ResetStaticCaches"', start)
    cleanup = host[start:end]
    config = read('Achievement/AchievementMedalConfig.cs')
    constants = '\n'.join(re.findall(r'        public const (?:int|string) (?:TYPE_ID|STOCK_SAVE_KEY|DEFAULT_MAX_STOCK) = [^\r\n]+;', config))
    assert len(constants.splitlines()) == 3
    generated = OUT / 'ProductionAssembly.cs'
    generated.write_text('namespace BossRush { public partial class ModBehaviour {\n'
                         + 'internal void BindProductionAchievementForFixture() {\n' + binding + '\n}\n'
                         + 'internal void CleanupProductionAchievementSlotForFixture() {\n' + cleanup + '\n}\n}\n'
                         + 'public static partial class AchievementMedalConfig {\n' + constants + '\n}}', encoding='utf-8')
    paths += [generated, HERE / 'Stubs.cs', HERE / 'Program.cs']
    includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in paths)
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn>'
                       '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
