"""Run true E personal reward and delivery code with native item/save adapters."""
from pathlib import Path
import argparse,hashlib,subprocess,sys
from xml.sax.saxutils import escape
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).resolve().parent
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=ROOT/'Build/runtime-regressions/ModeECoopEpoch')
OUT=parser.parse_args().output.resolve();OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT/'tests'));from cs_source_util import clean_source
source=clean_source((ROOT/'ModeE/ModeERuntimeModule_Coop.cs').read_text(encoding='utf-8-sig'))
def block(signature):
 start=source.index(signature);end=source.index('{',start)+1;depth=1
 while depth:depth+=(source[end]=='{')-(source[end]=='}');end+=1
 return source[start:end]
code='using System;using System.Collections.Generic;using UnityEngine;using UnityEngine.SceneManagement;using ItemStatsSystem;using Duckov.UI.DialogueBubbles;namespace BossRush {\n'
code+='\n'.join(block(x) for x in ('internal sealed class CoopModeEPersonal','internal sealed class CoopModeEParticipant','internal sealed class CoopModeEHireRequest'))
code+='internal partial class ModeERuntimeModule {\n'+ '\n'.join(block(x) for x in ('private sealed class CoopModeEPlayerTeam','private void RememberCoopModeEPlayerTeam(','private void SetCoopModeEPlayerTeam(','private bool RestoreCoopModeEPlayerTeams(','private void RetireLostCoopProjection(','private sealed class CoopModeERewardLedger','private void PreserveCoopModeERewardLedger(','private bool ApplyCoopModeEBossReward(','private static bool TryStoreCoopModeEReward(','private void BeginCoopModeEParticipants(','private bool IsCoopModeEParticipant(','internal bool IsCoopModeELocalAdmitted(','private bool ApplyCoopModeEPersonal(','private void DeliverCoopModeEDeath('))+'private readonly Dictionary<CharacterMainControl, CoopModeEPlayerTeam> coopModeEPlayerTeams = new Dictionary<CharacterMainControl, CoopModeEPlayerTeam>();}}'
esource=source;source=clean_source((ROOT/'Common/Coop/BossRushCoop.cs').read_text(encoding='utf-8-sig'))
code+='namespace BossRush {internal static partial class BossRushCoop {'+block('public static string GetPlayerId(')+'}}';source=esource
source=clean_source((ROOT/'DebugAndTools/F3GameplayValidationRunner.cs').read_text(encoding='utf-8-sig'))
code+='namespace BossRush {internal partial class ValidationIdentityProbe {'+block('internal int ValidationCountHostileCharacters(')
source=clean_source((ROOT/'DebugAndTools/F3GameplayValidationAutotestActions.cs').read_text(encoding='utf-8-sig'))
code+=block('private static bool IsAutotestHostileWithin(')+'}}';source=esource
fsource=clean_source((ROOT/'ModeF/ModeFFortifications_RepairRewardsCleanup.cs').read_text(encoding='utf-8-sig'));esource=source;source=fsource
code+='namespace BossRush {internal partial class ModeFRuntimeModule {'+block('internal bool TryGiveItemToPlayerOrDrop(')+'}}'
source=clean_source((ROOT/'SkyIsland/SkyIslandInventoryTransaction.cs').read_text(encoding='utf-8-sig'))
code+='namespace BossRush {internal static class SkyIslandInventoryTransaction {'+'\n'.join(block(x) for x in ('internal static bool HasOwner(','internal static void DestroyUnowned(','private static void SetCount(','internal static bool TryDeliver(Item item, Inventory pack,'))+'}}';source=esource
(OUT/'Production.cs').write_text(code,encoding='utf-8');(OUT/'source-hash.txt').write_text(hashlib.sha256((ROOT/'ModeE/ModeERuntimeModule_Coop.cs').read_bytes()).hexdigest(),encoding='utf-8')
paths=[OUT/'Production.cs',HERE/'Program.cs',HERE/'NativeDelivery.cs',HERE/'TeamRegression.cs',ROOT/'Common/Coop/BossRushCoopJson.cs',ROOT/'Common/Data/BossRushJsonValue.cs',ROOT/'Utilities/SimpleJsonHelper.cs']
includes=''.join('<Compile Include="'+escape(str(p),{'"':'&quot;'})+'" />' for p in paths);project=OUT/'Regression.csproj';project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>'+includes+'</ItemGroup></Project>',encoding='utf-8')
sys.path.insert(0,str(ROOT/'tools'));from run_runtime_regressions import run_project_fixture
code,output=run_project_fixture(project,OUT/'runs',ROOT);print(output)
if code:raise SystemExit(code)
for child in ('SmokeWorld','RepeatCallers'):
 code=subprocess.run([sys.executable,str(HERE/child/'run.py'),'--output',str(OUT/child)],cwd=ROOT).returncode
 if code:raise SystemExit(code)
