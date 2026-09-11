using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace UnityEngine { public static class Mathf { public static int RoundToInt(float x) { return (int)Math.Round(x); } } }
namespace Duckov.Utilities { public class Tag { public string name; } }
namespace ItemStatsSystem {
 public struct ItemMetaData { public int id; public Duckov.Utilities.Tag[] tags; }
 public static class ItemAssetsCollection { public static ItemMetaData GetMetaData(int id) { return new ItemMetaData(); } }
}
namespace BossRush {
 internal sealed class CharacterMainControl { public Health Health = new Health(); }
 internal sealed class Health { public float MaxHealth=100; public float CurrentHealth=100; }
 internal sealed class ModeHParticipantRef { public string ProfileId; public string StableKey; public int PlanSlotIndex=-1; public bool IsEnemy; public bool IsRelay; public CharacterMainControl Character; public int BatchIndex; }
 internal interface IModeHTelemetrySink { void OnParticipantHurt(ModeHParticipantRef t,ModeHParticipantRef a,float d,int w); void OnParticipantDead(ModeHParticipantRef t,ModeHParticipantRef k); }
 internal static class ModeHProfileRegistry {
  public static readonly Dictionary<string,ModeHProfileTemplate> Map=new Dictionary<string,ModeHProfileTemplate>();
  public static ModeHProfileTemplate GetByStableKey(string k){ModeHProfileTemplate v;return k!=null&&Map.TryGetValue(k,out v)?v:null;}
  public static ModeHProfileTemplate GetByTemplateId(string k){foreach(var v in Map.Values)if(v.ProfileTemplateId==k)return v;return null;}
  public static bool HasAnomaly(ModeHProfileTemplate t){return t!=null&&!string.IsNullOrEmpty(t.AnomalyId);}
  public static bool IsStableTemperament(ModeHProfileTemplate t){return t!=null&&Array.IndexOf(ModeHStableIds.StableTemperaments,t.TemperamentId)>=0;}
 }
 internal static class ModeHPresetRegistry { public static List<string> ProductionKeys { get {return ModeHProfileRegistry.Map.Keys.Where(k=>!Rejected.Contains(k)).OrderBy(k=>k).ToList();} } public static HashSet<string> Rejected=new HashSet<string>(); public static bool IsProductionKey(string k){return !Rejected.Contains(k)&&ModeHProfileRegistry.GetByStableKey(k)!=null;} }
 internal static class ModeHContentCatalog {
  public static List<ModeHMatchCorridor> MatchCorridors; public static List<ModeHSkeletonSpec> Skeletons; public static List<ModeHEntryScriptSpec> EntryScripts; public static List<ModeHArenaConditionSpec> ArenaConditions; public static List<ModeHSynergyCategory> SynergyCategories; public static List<ModeHArchetypeCapability> ArchetypeCapabilities; public static List<ModeHReconChoiceSpec> ReconChoices;
  internal static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true,PropertyNameCaseInsensitive=true};
  static List<T> Rows<T>(JsonElement doc,string key){return JsonSerializer.Deserialize<List<T>>(doc.GetProperty(key).GetRawText(),Json);}
  public static void Load(){
   var doc=JsonDocument.Parse(File.ReadAllText("Assets/Data/ModeH/ThreatPlans.json")).RootElement;
   MatchCorridors=Rows<ModeHMatchCorridor>(doc,"matchCorridor");Skeletons=Rows<ModeHSkeletonSpec>(doc,"skeletons");EntryScripts=Rows<ModeHEntryScriptSpec>(doc,"entryScripts");ArenaConditions=Rows<ModeHArenaConditionSpec>(doc,"arenaConditions");SynergyCategories=Rows<ModeHSynergyCategory>(doc,"synergyCategories");ArchetypeCapabilities=Rows<ModeHArchetypeCapability>(doc,"archetypeCapabilityMatrix");ReconChoices=Rows<ModeHReconChoiceSpec>(doc,"reconChoices");
   ModeHProfileRegistry.Map.Clear();foreach(var t in Rows<ModeHProfileTemplate>(JsonDocument.Parse(File.ReadAllText("Assets/Data/ModeH/BossProfiles.json")).RootElement,"profileTemplates")) if(t.ProductionCandidate)ModeHProfileRegistry.Map[t.StableKey]=t;
  }
 }
 internal static class ModeHCommandCompatibilityRegistry { public static List<ModeHBehaviorStatusDto> BuildBehaviorSnapshot(string k){return new List<ModeHBehaviorStatusDto>();} }
 internal static class ModBehaviour { public static string LastLog; public static void DevLog(string s){LastLog=s;} }
 class Program {
  static int checks;
  static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
  static ModeHProfileTemplate Add(string id,string key){var t=new ModeHProfileTemplate{ProfileTemplateId=id,StableKey=key,ArchetypeId="assault",TemperamentId="cautious"};ModeHProfileRegistry.Map[key]=t;return t;}
  static ModeHParticipantRef Enemy(string key){return new ModeHParticipantRef{IsEnemy=true,StableKey=key,Character=new CharacterMainControl()};}
  static ModeHSeasonDto Season(ModeHMatchReportDto r){return new ModeHSeasonDto{contract=new ModeHContractDto{contractMainProfileId="main",contractSubProfileId="sub"},profiles=new List<ModeHProfileDto>{new ModeHProfileDto{profileId="main",stableKey="main_key",status=(int)ModeHParticipantStatus.Available},new ModeHProfileDto{profileId="sub",stableKey="sub_key",status=(int)ModeHParticipantStatus.Available}},matchReports=new List<ModeHMatchReportDto>{r}};}
  static ModeHMatchReportDto Report(ModeHCombatTelemetry t){var r=new ModeHMatchReportDto{reportStatus=(int)ModeHMatchReportStatus.SettledPendingArchive};t.WriteReport(r,false,"",false);return r;}
  static void Main(){
   Add("enemy_special","enemy_key");Add("other_special","other_key");Add("main","main_key");Add("sub","sub_key");
   var t=new ModeHCombatTelemetry();t.BeginMatch(4,7,"");var first=Enemy("other_key");var final=Enemy("enemy_key");
   t.OnEnemyEntered(first);t.OnEnemyEntered(final);t.OnParticipantDead(first,null);t.OnParticipantDead(final,null);t.OnParticipantDead(first,null);
   Check(t.TryClaimVictory(true),"victory claimed");t.OnParticipantDead(Enemy("other_key"),null);
   var r=Report(t);Check(r.specialEnemyEligible&&r.finalDefeatedProfileSnapshot=="enemy_special","duplicate and late death preserve final source");
   var season=Season(r);string reason;var offer=ModeHTransferMarket.BuildOffer(season,4,out reason);
   Check(offer!=null&&offer.profileId=="enemy_special","victory report yields offer");
   Check(season.profiles.Find(x=>x.profileId==offer.profileId)!=null,"offer has visible profile before acceptance");
   Check(ModeHTransferMarket.TryAcceptOffer(season,offer.offerId,out reason),"audited offer accepted");
   Check(season.contract.contractMainProfileId=="main"&&season.contract.contractSubProfileId=="enemy_special","main retained and sub replaced");
   Check(season.profiles.Find(x=>x.profileId=="sub").status==(int)ModeHParticipantStatus.Released,"previous sub released");
   Check(!ModeHTransferMarket.TryAcceptOffer(season,offer.offerId,out reason),"offer cannot apply twice");
   foreach(int match in new[]{3,4,5}){
    t.BeginMatch(match,7,"");var e=Enemy("enemy_key");t.OnEnemyEntered(e);t.OnParticipantDead(e,null);t.TryClaimDefeatByDown();r=Report(t);
    Check(!r.specialEnemyEligible&&r.finalDefeatedProfileSnapshot=="","defeat has no eligibility "+match);
   }
   t.BeginMatch(4,7,"");t.TryClaimVictory(true);Check(!Report(t).specialEnemyEligible,"new match resets defeat evidence");
   t.BeginMatch(4,7,"");final=Enemy("enemy_key");t.OnEnemyEntered(final);t.OnParticipantDead(final,null);t.TryClaimVictory(true);r=Report(t);
   ModeHPresetRegistry.Rejected.Add("enemy_key");Check(!Report(t).specialEnemyEligible,"revoked certification cannot write eligible report");
   season=Season(r);Check(ModeHTransferMarket.BuildOffer(season,4,out reason)==null,"market checks certification again");ModeHPresetRegistry.Rejected.Clear();
   season=Season(r);r.specialEnemySourceTag="other_special";Check(ModeHTransferMarket.BuildOffer(season,4,out reason)==null,"conflicting snapshot rejected");r.specialEnemySourceTag="enemy_special";
   season=Season(r);r.winner=(int)ModeHMatchOutcome.PlayerDefeat;Check(ModeHTransferMarket.BuildOffer(season,4,out reason)==null,"defeat report rejected");r.winner=(int)ModeHMatchOutcome.PlayerVictory;
   season=Season(r);r.matchIndex=3;Check(ModeHTransferMarket.BuildOffer(season,4,out reason)==null,"old match report rejected");r.matchIndex=4;
   season=Season(r);season.echoAssignments=new List<ModeHEchoAssignmentDto>{new ModeHEchoAssignmentDto{profileId="enemy_special",destinationId="removed"}};Check(ModeHTransferMarket.BuildOffer(season,4,out reason)==null,"removed profile cannot return");
   season=Season(r);offer=ModeHTransferMarket.BuildOffer(season,4,out reason);ModeHPresetRegistry.Rejected.Add("enemy_key");Check(!ModeHTransferMarket.TryAcceptOffer(season,offer.offerId,out reason),"revoked offer rejected before mutation");Check(season.contract.contractSubProfileId=="sub","rejected offer preserves sub");
   Console.WriteLine("ModeHMarketAudit: PASS ("+checks+" production telemetry and market execution assertions)");
   PlanAudit();
  }
  static void PlanAudit(){
   ModeHPresetRegistry.Rejected.Clear();ModeHContentCatalog.Load();
   Check(!ModeHEncounterPlanner.HasLegalArrangement(new[]{"shotgun_brawler"},new List<string>()),"profile id is not an archetype");
   var templates=ModeHProfileRegistry.Map.Values.OrderBy(t=>t.ProductionOrder).ToList();int failed=0,success=0;var reasons=new Dictionary<string,int>();
   for(long seed=1;seed<=100;seed++){
    List<ModeHProfileDto> draft;string reason;Check(ModeHDraftController.TryBuildDraft(seed,templates,out draft,out reason),"draft "+seed+" "+reason);
    ModeHContractDto contract;Check(ModeHDraftController.TrySignContracts(draft,draft[0].profileId,draft[1].profileId,out contract,out reason),"sign "+reason);
    List<ModeHEchoAssignmentDto> echoes;Check(ModeHDraftController.TryAssignEchoDestinations(seed,draft,contract,out echoes,out reason),"echo "+reason);
    var season=new ModeHSeasonDto{profiles=draft,draftCandidateProfileIds=draft.Select(x=>x.profileId).ToList(),contract=contract,echoAssignments=echoes};var input=new PlanInputs{_season=season};var enemies=input.Enemies();var archetypes=input.Archetypes();
    Check(archetypes.SequenceEqual(new[]{draft[0].archetypeId,draft[1].archetypeId}),"profile to archetype mapping");Check(enemies.All(k=>draft.All(p=>p.stableKey!=k)),"draft exclusion");
    string echoId=ModeHDraftController.GetEchoProfileId(echoes,ModeHStableIds.EchoDestinationReturnEnemy);string echoKey=draft.Find(x=>x.profileId==echoId).stableKey;
    if(seed==1)Check(input.Viable(contract,echoes,out reason),"full-pool signing viability gate accepts complete season");
    for(int match=1;match<=6;match++){
     ModeHMatchPlanDto plan=null;int candidate;bool ok=false;reason=null;
     for(int retry=0;retry<=ModeHConfig.MaxAutomaticTechnicalRetriesPerMatch&&!ok;retry++)ok=ModeHEncounterPlanner.TryBuildPlan(seed,match,retry,enemies,match==5?echoKey:null,archetypes,out plan,out candidate,out reason);
     if(ok){success++;Check(plan.enemyStableKeys.All(k=>enemies.Contains(k)||(match==5&&k==echoKey)),"enemy identity boundary");Check(ModeHEncounterPlanner.HasLegalArrangement(archetypes,ModeHEncounterPlanner.CollectLockedArchetypes(ModeHEncounterPlanner.CollectCapabilityTags(plan.enemyStableKeys,ModeHContentCatalog.ArenaConditions.Find(x=>x.ConditionId==plan.conditionId)))),"roster veto honored");}
    else {failed++;int n;reasons.TryGetValue(reason??"unknown",out n);reasons[reason??"unknown"]=n+1;Console.WriteLine($"PLAN_FAIL seed={seed} match={match} reason={reason} pool={enemies.Count} archetypes={string.Join(",",archetypes)}");}
    }
   }
   Console.WriteLine("Plan audit: "+success+" / 600 constructible within production retry budget; failures="+failed+" "+string.Join(", ",reasons.Select(x=>x.Key+":"+x.Value)));
   Check(failed==0,"all sampled six-match seasons must be constructible");
   TransferViabilityAudit(templates,true);
   List<ModeHProfileDto> tooSmallDraft;string boundaryReason;
   Check(!ModeHDraftController.TryBuildDraft(1,templates.Take(5).ToList(),out tooSmallDraft,out boundaryReason)
       &&boundaryReason=="draft_pool_too_small","five certified profiles reject entry before filtering");
   ModeHMatchPlanDto emptyPlan;int emptyCandidate;
   Check(!ModeHEncounterPlanner.TryBuildPlan(1,1,0,new List<string>(),null,new[]{"assault"},out emptyPlan,out emptyCandidate,out boundaryReason)
       &&boundaryReason=="plan_enemy_pool_empty","empty enemy pool is an explicit rejection");
   int singleOk=0,singleRejected=0;
   foreach(string archetype in ModeHStableIds.AllArchetypes){
    for(int match=1;match<=6;match++){
     ModeHMatchPlanDto p;int c;bool ok=ModeHEncounterPlanner.TryBuildPlan(1,match,0,ModeHPresetRegistry.ProductionKeys,"Cname_Boss_Shot",new[]{archetype},out p,out c,out boundaryReason);
     if(ok){singleOk++;Check(ModeHEncounterPlanner.HasLegalArrangement(new[]{archetype},ModeHEncounterPlanner.CollectLockedArchetypes(ModeHEncounterPlanner.CollectCapabilityTags(p.enemyStableKeys,null))),"single remaining archetype respected");}else singleRejected++;
    }
   }
   Console.WriteLine("Single-archetype audit: "+singleOk+" constructible, "+singleRejected+" rejected within one candidate round");
   var eight=new[]{0,2,5,6,8,9,10,11}.Select(i=>templates[i]).ToList();
   var allKeys=ModeHPresetRegistry.ProductionKeys;
   foreach(string key in allKeys)if(eight.All(x=>x.StableKey!=key))ModeHPresetRegistry.Rejected.Add(key);
   List<ModeHProfileDto> smallDraft;Check(ModeHDraftController.TryBuildDraft(1,eight,out smallDraft,out boundaryReason),"eight audited candidates can draft");
   ModeHContractDto smallContract;ModeHDraftController.TrySignContracts(smallDraft,smallDraft[0].profileId,smallDraft[1].profileId,out smallContract,out boundaryReason);
   var smallInputs=new PlanInputs{_season=new ModeHSeasonDto{profiles=smallDraft,draftCandidateProfileIds=smallDraft.Select(x=>x.profileId).ToList(),contract=smallContract}};
   int smallOk=0;
   for(int match=1;match<=6;match++){
    ModeHMatchPlanDto p;int c;bool ok=false;
    for(int retry=0;retry<=ModeHConfig.MaxAutomaticTechnicalRetriesPerMatch&&!ok;retry++)ok=ModeHEncounterPlanner.TryBuildPlan(1,match,retry,smallInputs.Enemies(),smallDraft[2].stableKey,smallInputs.Archetypes(),out p,out c,out boundaryReason);
    if(ok)smallOk++;else Console.WriteLine("EIGHT_POOL_REJECT match="+match+" reason="+boundaryReason);
   }
   Console.WriteLine("Eight-certified candidate audit: "+smallOk+" / 6 constructible");
   Console.WriteLine("Eight-certified remainder: "+string.Join(",",smallInputs.Enemies().Select(k=>ModeHProfileRegistry.GetByStableKey(k).ProfileTemplateId))+"; roster="+string.Join(",",smallInputs.Archetypes()));
   List<ModeHEchoAssignmentDto> smallEchoes;ModeHDraftController.TryAssignEchoDestinations(1,smallDraft,smallContract,out smallEchoes,out boundaryReason);
   Check(!smallInputs.Viable(smallContract,smallEchoes,out boundaryReason)
       &&boundaryReason.StartsWith("season_viability_match_"),"unconstructible eight-certified roster is rejected before signing");
   Check(ReferenceEquals(smallInputs._season.contract,smallContract),"viability check does not mutate contract");
   Console.WriteLine("Signing viability gate: PASS (complete season accepted; known incomplete pool rejected without contract mutation)");
   TransferViabilityAudit(eight,false);
  }
  static void TransferViabilityAudit(List<ModeHProfileTemplate> templates,bool expectedAccepted){
   List<ModeHProfileDto> draft;string reason;
   Check(ModeHDraftController.TryBuildDraft(1,templates,out draft,out reason),"transfer draft: "+reason);
   ModeHContractDto contract;
   Check(ModeHDraftController.TrySignContracts(draft,draft[0].profileId,draft[1].profileId,out contract,out reason),"transfer sign: "+reason);
   List<ModeHEchoAssignmentDto> echoes;
   Check(ModeHDraftController.TryAssignEchoDestinations(1,draft,contract,out echoes,out reason),"transfer echoes: "+reason);
   var season=new ModeHSeasonDto{profiles=draft,draftCandidateProfileIds=draft.Select(x=>x.profileId).ToList(),contract=contract,echoAssignments=echoes};
   var input=new PlanInputs{_season=season};
   var telemetry=new ModeHCombatTelemetry();telemetry.BeginMatch(4,7,"");
   var finalEnemy=Enemy(input.Enemies()[input.Enemies().Count-1]);telemetry.OnEnemyEntered(finalEnemy);telemetry.OnParticipantDead(finalEnemy,null);
   Check(telemetry.TryClaimVictory(true),"transfer victory claimed");
   season.matchReports=new List<ModeHMatchReportDto>{Report(telemetry)};
   var offer=ModeHTransferMarket.BuildOffer(season,4,out reason);
   Check(offer!=null,"transfer offer built: "+reason);
   var previousSub=draft.Find(x=>x.profileId==contract.contractSubProfileId);int oldStatus=previousSub.status;
   string oldMain=contract.contractMainProfileId,oldSub=contract.contractSubProfileId;
   input.Accept(offer,4);
   Check(contract.contractMainProfileId==oldMain,"transfer preserves main");
   if(expectedAccepted){
    Check(input.Closed&&offer.status==(int)ModeHOfferStatus.Accepted,"constructible transfer accepted and window closed: "+ModBehaviour.LastLog+"; "+input.Message);
    Check(contract.contractSubProfileId==offer.profileId&&previousSub.status==(int)ModeHParticipantStatus.Released,"constructible transfer commits replacement");
   }else{
    Check(!input.Closed&&offer.status==(int)ModeHOfferStatus.Pending,"unconstructible transfer remains pending");
    Check(contract.contractSubProfileId==oldSub&&previousSub.status==oldStatus,"unconstructible transfer does not mutate roster");
    Check(!string.IsNullOrEmpty(input.Message),"unconstructible transfer explains rejection to player");
   }
   Console.WriteLine("Transfer viability gate: PASS ("+(expectedAccepted?"complete remaining schedule accepted":"incomplete remaining schedule rejected without mutation")+")");
  }
 }
}
