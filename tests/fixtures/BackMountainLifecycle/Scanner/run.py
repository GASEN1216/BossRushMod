"""Execute production display scanner with explicit building-area and official slot boundaries."""
from pathlib import Path
import hashlib
import sys
from xml.sax.saxutils import escape

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
OUT = ROOT / 'Build/backmountain-lifecycle/scanner'
OUT.mkdir(parents=True, exist_ok=True)
source = ROOT / 'Integration/BackMountain/ShowcaseDisplayScanner.cs'
# F3 diagnostics only inspect prefab tags; the complete runtime scanner stays verbatim.
runtime = source.read_text(encoding='utf-8-sig').split('        #region F3')[0] + '    }\n}\n'
generated = OUT / 'ProductionScanner.cs'
generated.write_text(runtime, encoding='utf-8')
paths = [generated, HERE / 'Program.cs']
project = OUT / 'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    + ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in paths) + '</ItemGroup></Project>', encoding='utf-8')
(OUT / 'production-sha256.txt').write_text(hashlib.sha256(source.read_bytes()).hexdigest() + ' ' + str(source), encoding='utf-8')
sys.path.insert(0, str(ROOT / 'tools'))
from run_runtime_regressions import run_project_fixture
code, output = run_project_fixture(project, OUT / 'runs', ROOT)
print(output)
raise SystemExit(code)
