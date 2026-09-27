"""Full production Arena spawn, cache and loot templates, plus original lifecycle."""
from pathlib import Path
import sys, json, hashlib, subprocess
from xml.sax.saxutils import escape
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
OUT=ROOT/'Build/runtime-regressions/ArenaHostRemainder';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT/'tests'))
from ArchitectureStructureGuard import extract_method_body
production=[ROOT/'WavesArena'/('WavesArenaRuntimeModule_'+name+'.cs') for name in ('LegacySpawn','LootTemplates','CharacterRegistry')]
production.append(ROOT/'Integration/Mutators/MutatorBossRegenRuntime.cs')
lifecycle=ROOT/'WavesArena/WavesArenaRuntimeModule.cs'
source=lifecycle.read_text(encoding='utf8')
parts=[]
for sig in ('public override void OnAwake(ModBehaviour owner)','public override void OnDestroy()'):
    parts.append(sig+extract_method_body(source,sig))
(OUT/'Lifecycle.cs').write_text('namespace BossRush { internal sealed partial class WavesArenaRuntimeModule {\n'+'\n'.join(parts)+'\n}}',encoding='utf8')
sources=production+[HERE/'Stubs.cs',HERE/'Async.cs',HERE/'Program.cs',OUT/'Lifecycle.cs']
includes=''.join('<Compile Include="'+escape(str(p),{'"':'&quot;'})+'" />' for p in sources)
project=OUT/'Regression.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>'+includes+'</ItemGroup></Project>',encoding='utf8')
(OUT/'production-sha256.json').write_text(json.dumps({str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in production+[lifecycle]},indent=2),encoding='utf8')
raise SystemExit(subprocess.call(['dotnet','run','--project',str(project),'--configuration','Release'],cwd=ROOT))
