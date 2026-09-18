"""Run complete backyard production services with isolated host adapters."""
from pathlib import Path
import hashlib
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/backmountain-lifecycle'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    linked = ['Integration/BackMountain/' + name + '.cs' for name in (
        'BackMountainConfig', 'BackMountainItems', 'BackMountainUnlocks', 'BackMountainRuntimeModule',
        'GardenSeedInjector', 'JukeboxTrackInjector', 'RaidMealService', 'RaidMealUsageBehavior', 'ShowcaseService')]
    linked += ['Audio/BossBgmTrackTable.cs', 'Common/Data/BossRushJsonValue.cs', 'Utilities/SimpleJsonHelper.cs', 'Common/Stats/RuntimeStatModifierTracker.cs', 'Integration/Items/ModeFItemConfigHelper.cs']
    ui_path = ROOT / 'Integration/BackMountain/ShowcaseUI.cs'
    ui = ui_path.read_text(encoding='utf-8-sig')
    lifecycle = ui[ui.index('        private static GameObject _root;'):ui.index('        #region 构建')]
    component = ui[ui.index('    internal sealed class ShowcasePanelLifetime'):ui.index('    public partial class ModBehaviour')]
    extracted = OUT / 'ShowcaseLifecycle.cs'
    extracted.write_text('using System; using UnityEngine; namespace BossRush { internal static partial class ShowcaseUI {\n' + lifecycle + '\n}\n' + component + '\n}', encoding='utf-8')
    paths = [ROOT / p for p in linked] + [HERE / 'Program.cs', HERE / 'Stubs.cs', extracted]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0067;0414</NoWarn></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    project += '</ItemGroup></Project>'
    target = OUT / 'Regression.csproj'
    target.write_text(project, encoding='utf-8')
    (OUT / 'source-hashes.txt').write_text('\n'.join(hashlib.sha256(p.read_bytes()).hexdigest() + ' ' + str(p.relative_to(ROOT)) for p in paths), encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(target), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
