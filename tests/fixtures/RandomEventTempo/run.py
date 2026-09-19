"""Execute all new tempo events against real temporary-stat attachment code."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/random-event-tempo'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    paths = [ROOT / path for path in ('RandomEvents/RandomEventTempo.cs', 'RandomEvents/RandomEventModels.cs',
             'RandomEvents/RandomEventsTuning.cs', 'Common/Stats/RuntimeStatModifierTracker.cs')]
    paths += [HERE / 'Program.cs', HERE / 'Stubs.cs']
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in paths)
    project += '</ItemGroup></Project>'
    path = OUT / 'RandomEventTempo.csproj'
    path.write_text(project, encoding='utf-8')
    return subprocess.call(['dotnet', 'run', '--project', str(path), '--configuration', 'Release'], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
