"""Execute production random-event flows with controlled asynchronous factories."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
from xml.sax.saxutils import escape
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
OUT=ROOT/'Build/runtime-regressions/RandomEventEffectsOwners'

def method(source,name):
    m=re.search(r'\b(?:internal|private)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\]]+\s+'+name+r'\(',source)
    assert m,name
    start=m.start();opening=source.index('{',start);end,depth=opening+1,1
    while depth:depth+=(source[end]=='{')-(source[end]=='}');end+=1
    return source[start:end]

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    spawn=ROOT/'RandomEvents/RandomEventEffectsBridge_Spawn.cs'
    loot=ROOT/'RandomEvents/RandomEventEffectsBridge_Loot.cs'
    host=ROOT/'RandomEvents/RandomEventsRuntimeModuleHostBridge.cs'
    core=ROOT/'RandomEvents/RandomEventEffectsBridge.cs'
    names=('SpawnRandomEventIntruderBoss','SpawnRandomEventIntruderBossAsync','ConfigureRandomEventIntruderBoss','DespawnRandomEventIntruderBoss','SpawnRandomEventParadeDucks','SpawnRandomEventParadeDucksAsync','InvokeRandomEventSpawnCompletion','HasRandomEventMerchantPreset','SpawnRandomEventMerchant','SpawnRandomEventMerchantAsync','DespawnRandomEventMerchant')
    production='\n'.join(method(spawn.read_text(encoding='utf-8-sig'),name) for name in names)
    production+='\n'+method(loot.read_text(encoding='utf-8-sig'),'RandomEventAirdropDropRoutine')
    production=production.replace('async UniTaskVoid','async System.Threading.Tasks.Task').replace('UniTask.Yield()','System.Threading.Tasks.Task.Yield()')
    bridges='\n'.join(method(host.read_text(encoding='utf-8-sig'),name) for name in ('SpawnRandomEventIntruderBoss','DespawnRandomEventIntruderBoss','SpawnRandomEventParadeDucks','HasRandomEventMerchantPreset','SpawnRandomEventMerchant','DespawnRandomEventMerchant','RandomEventAirdropDropRoutine'))
    generated=OUT/'Production.cs'
    generated.write_text('using System; using System.Collections; using UnityEngine; using UnityEngine.SceneManagement; using Duckov.ItemUsage; using Duckov.Economy; using Duckov.Economy.UI; using Duckov.Scenes; namespace BossRush { internal sealed partial class RandomEventsRuntimeModule {\n'+production+'\n} public partial class ModBehaviour {\n'+bridges+'\n}}',encoding='utf-8')
    files=[core,generated,HERE/'Stubs.cs',HERE/'Program.cs']
    project=OUT/'Regression.csproj'
    includes=''.join('<Compile Include="'+escape(str(p),{'"':'&quot;'})+'" />' for p in files)
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'+includes+'</ItemGroup></Project>',encoding='utf-8')
    (OUT/'production-sha256.json').write_text(json.dumps({p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (core,spawn,loot,host)},indent=2),encoding='utf-8')
    return subprocess.call(['dotnet','run','--project',str(project),'--configuration','Release'],cwd=ROOT)
if __name__=='__main__':raise SystemExit(main())
