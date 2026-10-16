from pathlib import Path
from xml.sax.saxutils import escape
import hashlib,json

def run(root,out,member,execute):
    out.mkdir(parents=True,exist_ok=True);here=Path(__file__).resolve().parent
    catalog=root/'RandomEvents/RandomEventCatalog.cs';bridge=root/'RandomEvents/RandomEventEffectsBridge.cs'
    code='using System;using UnityEngine;namespace BossRush {internal static partial class RandomEventEffectHelpers {'+member(catalog.read_text(encoding='utf-8-sig'),'internal static CharacterMainControl ResolveAlivePlayer()')+'}internal partial class RandomEventEffectsBridge {'
    for signature in ('internal Vector3 GetRandomEventSafePointAwayFromPlayer(','private static bool IsRandomEventPointSafeForParty('):code+=member(bridge.read_text(encoding='utf-8-sig'),signature)
    code+='}}';(out/'Production.cs').write_text(code,encoding='utf-8')
    sources=[out/'Production.cs',here/'Program.cs'];project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'+''.join('<Compile Include="'+escape(str(p))+'"/>' for p in sources)+'</ItemGroup></Project>';(out/'Regression.csproj').write_text(project,encoding='utf-8')
    (out/'production-sha256.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in (catalog,bridge)},indent=2),encoding='utf-8')
    return execute(out/'Regression.csproj',out/'execution',root)
