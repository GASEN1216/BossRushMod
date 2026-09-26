"""Execute complete map, Arena host-state and common-NPC placement production files."""
from pathlib import Path
import hashlib
import json
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/HostMapArenaOwners"
OUT.mkdir(parents=True, exist_ok=True)
production = [
    ROOT / "MapSelection/BossRushMapRuntime.cs",
    ROOT / "Common/MapConfig/BossRushMapConfig.cs",
    ROOT / "Common/MapConfig/MapSpawnPointRegistry.cs",
    ROOT / "Common/Data/BossRushJsonValue.cs",
    ROOT / "Utilities/SimpleJsonHelper.cs",
    ROOT / "WavesArena/WavesArenaRuntimeModule_HostState.cs",
    ROOT / "Integration/NPCs/Common/CommonNpcSpawnPointPolicy.cs",
]
data = OUT / "map-input/Assets/SpawnPoints"
data.mkdir(parents=True, exist_ok=True)
maps = [
    ("a-beta.json", "Beta", "shared-id", 10, [1, 2, 3], [11, 12, 13], [21, 22, 23]),
    ("z-gamma.json", "Gamma", "gamma-id", 1, [4, 5, 6], None, [31, 32, 33]),
    ("m-alpha.json", "Alpha", "shared-id", 10, [7, 8, 9], None, None),
    ("b-omega.json", "Omega", "omega-id", None, [10, 20, 30], None, None),
]
for filename, name, scene_id, order, point, custom, sign in maps:
    record = dict(sceneName=name, sceneID=scene_id, displayNameCN=name, displayNameEN=name,
                  spawnPoints=[point], customSpawnPos=custom, defaultSignPos=sign)
    if order is not None:
        record["sortOrder"] = order
    (data / filename).write_text(json.dumps(record), encoding="utf-8")
sources = production + [HERE / "Stubs.cs", HERE / "Program.cs"]
includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
project = OUT / "Regression.csproj"
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                   '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
                   '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn>'
                   '</PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
(OUT / "production-sha256.json").write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                                                         for p in production}, indent=2), encoding="utf-8")
raise SystemExit(subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release",
                                 "--", str(OUT / "map-input")], cwd=ROOT))
