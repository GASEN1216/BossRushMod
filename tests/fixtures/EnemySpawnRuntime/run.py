"""Link the complete production spawn core, scheduler and profiler."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get('BOSSRUSH_FIXTURE_OUTPUT', str(ROOT / "Build/runtime-regressions/EnemySpawnRuntime")))
OUT.mkdir(parents=True, exist_ok=True)
production = [ROOT / path for path in (
    "Utilities/EnemySpawnCore.cs", "Utilities/ModeEFSpawnPostprocessScheduler.cs", "Utilities/ModeEFSpawnProfiler.cs")]
hashes = {}
def member(path, marker):
    source = ROOT / path
    raw = source.read_text(encoding='utf-8-sig')
    start = raw.index(marker); opening = raw.index('{', start); depth = 0
    for index in range(opening, len(raw)):
        depth += (raw[index] == '{') - (raw[index] == '}')
        if not depth:
            hashes[path] = hashlib.sha256(source.read_bytes()).hexdigest()
            return raw[start:index + 1]
    raise ValueError(marker)

gates = ['namespace BossRush {']
for mode, lifecycle, entry, signatures in [
    ('E', 'ModeE/ModeERuntimeModule.cs', 'ModeE/ModeEStartup.cs', ['private int BeginModeESession()', 'private void InvalidateModeESession()', 'internal bool IsModeESessionStillValid(', 'internal bool IsModeEOrModeFSpawnSessionStillValid(']),
    ('F', 'ModeF/ModeFRuntimeModule.cs', 'ModeF/ModeFEntry.cs', ['private int BeginModeFSession()', 'private void InvalidateModeFSession()', 'internal bool IsModeFSessionStillValid(']),
]:
    gates.append('internal sealed partial class Mode' + mode + 'RuntimeModule {')
    gates.extend(member(lifecycle, sig) for sig in ['public override void OnAwake(', 'public override void OnDestroy()'])
    gates.extend(member(entry, sig) for sig in signatures)
    gates.append('}')
gates.append('}')
(OUT / 'ProductionGates.cs').write_text('\n'.join(gates), encoding='utf-8')
sources = production + [HERE / "Stubs.cs", HERE / "Program.cs", HERE / 'GateOwners.cs', OUT / 'ProductionGates.cs']
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
    '<NoWarn>0649</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
hashes.update({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in production})
(OUT / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
raise SystemExit(subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT))
