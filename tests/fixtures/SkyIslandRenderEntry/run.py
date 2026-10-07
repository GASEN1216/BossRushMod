"""Run the actual rendering setup with isolated shader/material boundary doubles."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'Build/runtime-regressions/SkyIslandRenderEntry'
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / 'tests/fixtures/ModeDEntryOwnership'))
from run import member

source = ROOT / 'SkyIsland/SkyIslandRendering.cs'
raw = source.read_text(encoding='utf-8-sig')
(OUT / 'Rendering.cs').write_text('using System; using System.Collections.Generic; using UnityEngine; '
    'using UnityEngine.Rendering; namespace BossRush { internal sealed class SkyIslandRendering {'
    'private readonly List<Material> owned = new List<Material>();\n'
    + member(raw, 'internal void Apply(') + '\n'
    + member(raw, 'private static void ValidateWorldShader(') + '}}', encoding='utf-8')
paths = [OUT / 'Rendering.cs', HERE / 'Program.cs']
project = OUT / 'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    + ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    + '</ItemGroup></Project>', encoding='utf-8')
(OUT / 'production-sha256.json').write_text(json.dumps({str(source): hashlib.sha256(source.read_bytes()).hexdigest()}))
raise SystemExit(subprocess.call(['dotnet', 'run', '--project', str(project), '--configuration', 'Release'], cwd=ROOT))
