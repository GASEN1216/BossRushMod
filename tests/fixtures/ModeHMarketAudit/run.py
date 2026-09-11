from pathlib import Path
import subprocess,sys,hashlib
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
OUT=ROOT/'Build'/'runtime-regressions'/'ModeHMarketAudit'
OUT.mkdir(parents=True,exist_ok=True)
PROD=OUT/'Production'; PROD.mkdir(exist_ok=True)
files=['ModeHCombatTelemetry.cs','ModeHTransferMarket.cs','ModeHContentModels.cs','ModeHStateDtos.cs','ModeHStateModel.cs','ModeHConfig.cs','ModeHDraftController.cs','ModeHEncounterPlanner.cs','ModeHSeedStream.cs','ModeHCanonicalDigest.cs']
for name in files:(PROD/name).write_bytes((ROOT/'ModeH'/name).read_bytes())
(PROD/'BossRushJsonValue.cs').write_bytes((ROOT/'Common/Data/BossRushJsonValue.cs').read_bytes())
(PROD/'SimpleJsonHelper.cs').write_bytes((ROOT/'Utilities/SimpleJsonHelper.cs').read_bytes())
def method(text,signature):
 start=text.index(signature);op=text.index('{',start);depth=1;end=op+1
 while depth:
  depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[start:end]
source=(ROOT/'ModeH/ModeHRuntimeModule_CombatProfiles.cs').read_text(encoding='utf-8-sig')
extracted='''using System; using System.Collections.Generic; namespace BossRush {
internal sealed class SeedOwner { public long RunSeed=1; public int MatchIndex; public ModeHLifecycle Lifecycle; }
internal sealed class MessageOwner { public string Message; public void ShowMessage(string s){Message=s;} }
internal static class L10n { public static string T(string zh,string en){return en;} }
internal sealed class PlanInputs {
 public ModeHSeasonDto _season; private SeedOwner _runState=new SeedOwner();
 private bool _commandsClosed=false; private MessageOwner _owner=new MessageOwner(); public bool Closed;
 public string Message { get {return _owner.Message;} }
 public List<string> Archetypes(){return BuildLiveArchetypeIds();}
 public List<string> Enemies(){return BuildPlanEnemyPool();}
 public bool Viable(ModeHContractDto contract,IList<ModeHEchoAssignmentDto> echoes,out string reason){return CanConstructFullSeason(contract,echoes,out reason);}
 public void Accept(ModeHOfferDto offer,int matchIndex){_runState.MatchIndex=matchIndex;_runState.Lifecycle=ModeHLifecycle.TransferWindow;AcceptTransferOffer(offer);}
 private void CloseTransferWindow(string reason){Closed=true;}
 private void LogFailure(string reason,Exception error){throw new Exception(reason,error);}
'''
for signature in ['private ModeHProfileDto FindSeasonProfile(', 'private List<string> BuildLiveArchetypeIds()', 'private List<string> BuildLiveArchetypeIds(ModeHContractDto contract)', 'private List<string> BuildPlanEnemyPool()', 'private List<string> BuildPlanEnemyPool(ModeHContractDto contract)']:
 extracted+=method(source,signature)+'\n'
extracted+=method(source,'private bool CanConstructFullSeason(')+'\n'
extracted+=method(source,'private bool CanConstructRemainingSeason(')+'\n'
flow=(ROOT/'ModeH/ModeHRuntimeModule_MatchFlow.cs').read_text(encoding='utf-8-sig')
season_flow=(ROOT/'ModeH/ModeHRuntimeModule_SeasonFlow.cs').read_text(encoding='utf-8-sig')
extracted+=method(season_flow,'private void AcceptTransferOffer(')+'\n'
extracted+=method(source,'private string ResolveEchoReturnStableKey(IList<ModeHEchoAssignmentDto> assignments)')+'\n'
pick=method(flow,'private void OnDraftPick(')
assert pick.index('CanConstructFullSeason(contract, assignments, out failureReasonId)') < pick.index('_season.contract = contract;'), 'viability must run before accepting contract'
(PROD/'PlanInputs.cs').write_text(extracted+'}}',encoding='utf-8')
(OUT/'Harness.cs').write_bytes((HERE/'Harness.cs').read_bytes())
(OUT/'source-sha256.txt').write_text('\n'.join(hashlib.sha256((PROD/n).read_bytes()).hexdigest()+'  ModeH/'+n for n in files),encoding='utf-8')
(OUT/'Fixture.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup><Compile Include="Production/*.cs"/><Compile Include="Harness.cs"/></ItemGroup></Project>''',encoding='utf-8')
r=subprocess.run(['dotnet','run','--project',str(OUT/'Fixture.csproj'),'--configuration','Release','--verbosity','quiet'],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace')
o=r.stdout+r.stderr;(OUT/'execution.log').write_text(o,encoding='utf-8');print(o,end='');sys.exit(r.returncode)
