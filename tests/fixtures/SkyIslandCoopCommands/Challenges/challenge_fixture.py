from pathlib import Path
from xml.sax.saxutils import escape
import hashlib,json

def run(root,out,member,execute):
    here=Path(__file__).resolve().parent;out.mkdir(parents=True,exist_ok=True)
    sky=root/'SkyIsland';enc=(sky/'SkyIslandEncounters.cs').read_text(encoding='utf-8-sig');coop=(sky/'SkyIslandSessionCoop.cs').read_text(encoding='utf-8-sig');session=(sky/'SkyIslandSession.cs').read_text(encoding='utf-8-sig')
    code='using System;using System.Collections.Generic;using UnityEngine;namespace BossRush { internal sealed partial class SkyIslandEncounters {\n'
    for signature in ('private sealed class Encounter','private Encounter Find(','private bool AnySpawning()','internal bool BeginChallenge(','internal bool CanBeginChallenge(','internal void Tick()'):
        code+=member(enc,signature)+'\n'
    for signature in ('internal bool TryGetChallengeOwner(','internal bool BeginRemoteChallenge('):code+=member(coop,signature)+'\n'
    code+='} internal sealed partial class SkyIslandSession {\n'
    for signature in ('internal sealed class CoopChallenge {','private sealed class CoopChallengeReply {','private bool RequestCoopChallenge(','private void OnCoopChallenge(','private bool ApplyCoopChallengeReply(','private bool CompleteCoopChallenge(','internal void CreditChallengeBounty('):code+=member(coop,signature)+'\n'
    for signature in ('private static bool TryChallengeOutcome(','private bool EncounterWasSaved(','private void OnEncounterCleared('):code+=member(session,signature)+'\n'
    tick=(sky/'SkyIslandSessionTick.cs').read_text(encoding='utf-8-sig')
    import re
    line=re.findall(r'^\s*residents.SetVisible\("sky_zheling"[^;]+;',tick,re.M)
    assert len(line)==1
    code+='internal void ResidentsForTest() {'+line[0]+'}\n}}';(out/'Production.cs').write_text(code,encoding='utf-8')
    paths=[sky/n for n in ('SkyIslandEncounters.cs','SkyIslandSessionCoop.cs','SkyIslandSession.cs','SkyIslandCoopRuntime.cs','SkyIslandStoryRules.cs','SkyIslandSessionTick.cs')]+[root/n for n in ('Utilities/CoopPersonalDelivery.cs','Common/Coop/BossRushCoopJson.cs','Common/Data/BossRushJsonValue.cs','Utilities/SimpleJsonHelper.cs')]
    (out/'production-sha256.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},indent=2),encoding='utf-8')
    sources=[out/'Production.cs',here/'Boundary.cs',here/'Program.cs',sky/'SkyIslandCoopRuntime.cs',sky/'SkyIslandStoryRules.cs']+paths[6:]
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p))+'"/>' for p in sources)+'</ItemGroup></Project>'
    (out/'Regression.csproj').write_text(project,encoding='utf-8');return execute(out/'Regression.csproj',out/'execution',root)
