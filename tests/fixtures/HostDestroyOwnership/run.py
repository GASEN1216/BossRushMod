"""Execute the production Awake/OnDestroy entry points and module dispatcher."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/HostDestroyOwnership'
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source


def extract(source, signature):
    clean = clean_source(source)
    assert clean.count(signature) == 1, 'Missing or ambiguous production method: ' + signature
    start = clean.index(signature)
    opening = clean.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (clean[end] == '{') - (clean[end] == '}')
        end += 1
    return clean[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sources = {
        'ModBehaviour.cs': ['void Awake()', 'void OnDestroy()'],
        'Achievement/AchievementRuntimeHooks.cs': ['internal void CleanupAchievementRuntime()'],
    }
    hashes, methods = {}, []
    for relative, signatures in sources.items():
        raw = (ROOT / relative).read_bytes()
        hashes[relative] = hashlib.sha256(raw).hexdigest()
        methods += [extract(raw.decode('utf-8-sig'), signature) for signature in signatures]
    generated = OUT / 'ProductionEntry.cs'
    generated.write_text('using System; namespace BossRush { public partial class ModBehaviour {\n'
                         + '\n'.join(methods) + '\n}}', encoding='utf-8')
    paths = [generated, HERE / 'Program.cs', HERE / 'Stubs.cs']
    for relative in ('Common/Lifecycle/BossRushRuntimeModuleHost.cs',
                     'Common/Lifecycle/BossRushRuntimeModuleBase.cs',
                     'Common/Lifecycle/IBossRushRuntimeModule.cs'):
        path = ROOT / relative
        paths.append(path)
        hashes[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    includes = ''.join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in paths)
    project = OUT / 'Regression.csproj'
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                       '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>'
                       '<ItemGroup>' + includes + '</ItemGroup></Project>', encoding='utf-8')
    (OUT / 'production-sha256.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
    result = subprocess.call(['dotnet', 'build', str(project), '--configuration', 'Release', '--nologo'], cwd=ROOT)
    if result:
        return result
    return subprocess.call(['dotnet', str(OUT / 'bin/Release/net8.0/Regression.dll')], cwd=ROOT)


if __name__ == '__main__':
    raise SystemExit(main())
