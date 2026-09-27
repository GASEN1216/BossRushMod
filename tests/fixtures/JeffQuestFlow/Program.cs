using System;
using System.Linq;
using BossRush;
using Duckov.Quests;
using Duckov.Utilities;
using Saves;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static OfficialQuestProjection core;
    private static CampaignOfficialQuestClient client;
    private static QuestManager manager;
    private static void Check(bool ok, string label) { checks++; if (!ok) throw new Exception("ASSERT: "+label); }
    private static void Tick() { Time.unscaledTime += 1; core.Tick(); }
    private static Quest Active(int id) { return manager.ActiveQuests.SingleOrDefault(q=>q.ID==id); }
    private static void Reset()
    {
        if(core!=null)core.Dispose();
        if(manager!=null)manager.Dispose();
        GameplayDataSettings.QuestCollection = new QuestCollection();
        CampaignContentCatalog.ResetStaticCaches(); CampaignBaseObjectives.ResetStaticCaches();
        CampaignPersistence.Current = new CampaignSaveData();
        CampaignPersistence.HasWriteBarrier=CampaignPersistence.IsStoreFaulted=false;
        CampaignProgressService.States.Clear(); CampaignFacilityUnlocks.Tokens.Clear();
        CampaignObjectiveTracker.Progress.Clear(); CampaignObjectiveTracker.ArmedChapterId=null;
        ModBehaviour.Instance = new ModBehaviour();
        CharacterMainControl.Main = new CharacterMainControl();
        LevelManager.Instance.IsBaseLevel=true; SceneLoader.IsSceneLoading=false;
        ModeGRuntimeGates.IsModeGRunInProgress=false; PetNestService.PetCount=0; PetNestCompanionRuntime.HasCompanion=false; DailyReportService.IsSignedToday=false; SkyIslandPreludeFlow.RouteOpen=false;
        OfficialQuestItems.Reset(); CampaignProgressService.GuideCash=0; CampaignProgressService.FailDeliver=false;
        SavesSystem.CurrentSlot++; Time.unscaledTime=0;
        core=new OfficialQuestProjection(ModBehaviour.Instance); manager=new QuestManager();
        client=new CampaignOfficialQuestClient(new CampaignRuntimeModule()); client.RegisterAll(core); Tick();
    }
    private static void Observe(string id)
    {
        ModBehaviour owner=ModBehaviour.Instance;
        switch(id)
        {
            case CampaignGuideTable.ModeG: ModeGRuntimeGates.IsModeGRunInProgress=true; break;
            case CampaignGuideTable.ModeH:
                owner.ModeHRuntime._season.matchReports.Add(new ModeHMatchReportDto {reportStatus=(int)ModeHMatchReportStatus.SettledPendingArchive}); break;
            case CampaignGuideTable.ModeD: owner.Mode=CampaignContentCatalog.ModeModeD; break;
            case CampaignGuideTable.ModeE: owner.Mode=CampaignContentCatalog.ModeModeE; break;
            case CampaignGuideTable.ModeF: owner.Mode=CampaignContentCatalog.ModeModeF; break;
            case CampaignGuideTable.Zombie: owner.Mode=CampaignContentCatalog.ModeZombie; break;
            case CampaignGuideTable.PetNest: PetNestService.PetCount=1; break;
            case CampaignGuideTable.RandomEvents: owner.RandomEventsRuntime.Director.EventsFiredThisRun=1; break;
            case CampaignGuideTable.Garden: CampaignBaseObjectives.RegisterProvider(CampaignObjectiveKind.GardenBuilt,()=>true); break;
            case CampaignGuideTable.Trophy: CampaignBaseObjectives.RegisterProvider(CampaignObjectiveKind.TrophyDisplayed,()=>true); break;
            case CampaignGuideTable.DailyReport: DailyReportService.IsSignedToday=true; break;
            case CampaignGuideTable.SkyIslandGear: CharacterMainControl.Main.CharacterItem.Items.Add(new ItemStatsSystem.Item {TypeID=500086});
                OfficialQuestItems.Backpack[BossRushItemIds.SkyIslandBrassScrap]=5; break;
            case CampaignGuideTable.AffixForge: CharacterMainControl.Main.CharacterItem.Items.Add(new ItemStatsSystem.Item {Affix=true}); break;
            case CampaignGuideTable.Reforge: CharacterMainControl.Main.CharacterItem.Items.Add(new ItemStatsSystem.Item {Reforged=true}); break;
            default: throw new Exception("No observer fixture for "+id);
        }
        CampaignGuideFacts.ObserveAccepted(owner); Tick();
    }
    private static void AllGuides()
    {
        Reset(); Check(CampaignContentCatalog.Source=="Json", "loads real chapter data");
        Check(GameplayDataSettings.QuestCollection.Count==20, "all six chapters and fourteen guides registered");
        client.RegisterAll(core); Check(GameplayDataSettings.QuestCollection.Count==20,"registration is idempotent");
        CampaignFacilityUnlocks.Tokens.Add(CampaignFacilityUnlocks.BuildTokenForChapter(1));
        CampaignFacilityUnlocks.Tokens.Add(CampaignFacilityUnlocks.BuildTokenForChapter(2));
        SkyIslandPreludeFlow.RouteOpen=true;
        foreach(CampaignGuideTable.Definition def in CampaignGuideTable.Definitions)
        {
            Quest prefab=GameplayDataSettings.QuestCollection.Get(def.QuestId);
            Check(prefab!=null && (int)prefab.QuestGiverID==1 && prefab.gameObject.activeSelf,"guide template active under Jeff "+def.Id);
            var guideGrant=CampaignRewardTable.ForGuide(def.Id);
            Check(prefab.DisplayNameRaw==def.NameKey && prefab.Tasks.Count==1+(guideGrant.Submissions==null?0:guideGrant.Submissions.Length),"guide identity and target "+def.Id);
            Check(prefab.MeetsPrerequisit(),"guide offered in order "+def.Id);
            CampaignGuideFacts.ObserveAccepted(ModBehaviour.Instance);
            Check(!CampaignGuideTable.IsExperienced(def.Id),"not observed before acceptance "+def.Id);
            manager.ActivateQuest(def.QuestId,QuestGiverID.Jeff); Tick();
            Quest quest=Active(def.QuestId);
            Check(quest!=null && CampaignGuideTable.IsAccepted(def.Id),"official accept commits guide "+def.Id);
            Check(!quest.TryComplete(),"cannot deliver before experience "+def.Id);
            Check(CampaignGuideTable.NextOfferableId(null)==null,"pending guide prevents next offer "+def.Id);
            if(def.Id==CampaignGuideTable.ModeH)
            {
                ModBehaviour.Instance.ModeHRuntime._season.matchReports.Add(new ModeHMatchReportDto());
                CampaignGuideFacts.ObserveAccepted(ModBehaviour.Instance);
                Check(!CampaignGuideTable.IsExperienced(def.Id),"unsettled match cannot complete spectator guide");
            }
            Observe(def.Id);
            Check(quest.AreTasksFinished() && !CampaignGuideTable.IsCompleted(def.Id),"experience finishes target but requires Jeff delivery "+def.Id);
            LevelManager.Instance.IsBaseLevel=false;
            Check(!quest.TryComplete(),"guide cannot be delivered away from base "+def.Id);
            LevelManager.Instance.IsBaseLevel=true;
            Check(quest.TryComplete(),"Jeff delivery succeeds "+def.Id);
            Check(Active(def.QuestId)==null && manager.HistoryQuests.Contains(quest),"official history receives completed guide "+def.Id);
            Check(!quest.TryComplete() && !prefab.MeetsPrerequisit(),"completed guide does not repeat "+def.Id);
        }
        Check(manager.HistoryQuests.Count==14,"all fourteen guides complete");
        // Rebuild completed projections after real-like destruction of the quest manager's scene objects.
        manager.Dispose(); manager=new QuestManager(); Tick();
        Check(manager.ActiveQuests.Count==0 && manager.HistoryQuests.Count==14,"completed guides rebuild silently into history");
        Check(manager.HistoryQuests.All(q=>q.Tasks.OfType<OfficialQuestProjectionTask>().All(t=>t.Notifications==0)),"reload does not replay target notifications");
        var snapshot=(QuestManager.SaveData)OfficialQuestProjection.FilterSaveSnapshot(manager.GenerateSaveData());
        Check(snapshot.historyQuestsData.Count==0 && snapshot.completedQuests.Count==0,"guide identities stripped from official snapshot");
        Check(manager.HistoryQuests.Count==14,"save filter preserves live history");
        CampaignPersistence.Current=new CampaignSaveData(); SavesSystem.CurrentSlot++; Tick();
        Check(manager.HistoryQuests.Count==0 && manager.ActiveQuests.Count==0,"new slot has no previous guide projections");
    }
    private static void GatedGuidesDoNotBlockChain()
    {
        Reset();
        // Fresh save: sky island route closed and no campaign facility tokens yet.
        string[] order=CampaignGuideTable.Definitions.Select(d=>d.Id).ToArray();
        var offered=new System.Collections.Generic.List<string>();
        for(int guard=0;guard<order.Length;guard++)
        {
            string next=CampaignGuideTable.NextOfferableId(id=>
                id==CampaignGuideTable.Garden ? CampaignFacilityUnlocks.IsTokenGranted(CampaignFacilityUnlocks.BuildTokenForChapter(1))
                : id==CampaignGuideTable.Trophy ? CampaignFacilityUnlocks.IsTokenGranted(CampaignFacilityUnlocks.BuildTokenForChapter(2))
                : id!=CampaignGuideTable.SkyIslandGear || SkyIslandPreludeFlow.RouteOpen);
            if(next==null)break;
            var def=CampaignGuideTable.Find(next);
            Quest prefab=GameplayDataSettings.QuestCollection.Get(def.QuestId);
            Check(prefab.MeetsPrerequisit(),"production gate agrees with offered guide "+next);
            foreach(var other in CampaignGuideTable.Definitions.Where(d=>d.Id!=next))
                Check(!GameplayDataSettings.QuestCollection.Get(other.QuestId).MeetsPrerequisit(),"only one guide offered at a time "+other.Id);
            offered.Add(next);
            manager.ActivateQuest(def.QuestId,QuestGiverID.Jeff); Tick(); Observe(next); Check(Active(def.QuestId).TryComplete(),"deliver "+next);
        }
        Check(!offered.Contains(CampaignGuideTable.SkyIslandGear) && !offered.Contains(CampaignGuideTable.Garden) && !offered.Contains(CampaignGuideTable.Trophy),"unmet prerequisites are skipped on a fresh save");
        Check(offered.Count==order.Length-3,"closed sky island route does not block the remaining guides");
        SkyIslandPreludeFlow.RouteOpen=true; Tick();
        Check(GameplayDataSettings.QuestCollection.Get(CampaignGuideTable.Find(CampaignGuideTable.SkyIslandGear).QuestId).MeetsPrerequisit(),"sky island gear guide appears once the route is open");
    }
    private static int Expected(OfficialQuestItemStack[] items, int typeId) { return items == null ? 0 : items.Where(i=>i.TypeId==typeId).Sum(i=>i.Count); }
    private static int Given(int typeId) { return OfficialQuestItems.Given.Where(i=>i.TypeId==typeId).Sum(i=>i.Count); }
    private static void RewardsAndSubmissions()
    {
        // Pure allocation: submissions sharing a type are charged cumulatively, any-of picks in order, shortfall names the submission.
        var shared=new[]{ new OfficialQuestSubmission{TypeIds=new[]{1,2},Count=3}, new OfficialQuestSubmission{TypeIds=new[]{2},Count=2} };
        var plan=new System.Collections.Generic.List<OfficialQuestItemStack>(); OfficialQuestSubmission missing;
        Func<int,int> held=t=>t==1?1:(t==2?4:0);
        Check(OfficialQuestItemRules.TryPlan(shared,held,plan,out missing) && plan.Count==2 && plan[0].TypeId==1 && plan[0].Count==1 && plan[1].TypeId==2 && plan[1].Count==4,"shared type charged cumulatively");
        Func<int,int> tight=t=>t==1?1:(t==2?3:0);
        Check(!OfficialQuestItemRules.TryPlan(shared,tight,plan,out missing) && missing==shared[1] && plan.Count==0,"cumulative shortfall rejected and names the second submission");
        Check(OfficialQuestItemRules.Held(new OfficialQuestSubmission{TypeIds=new[]{2,2},Count=1},held)==4,"duplicate type ids are counted once");
        // Every Jeff quest shows its production reward rows (money + items) and they flip to claimed only after delivery.
        Reset(); CampaignFacilityUnlocks.Tokens.Add(CampaignFacilityUnlocks.BuildTokenForChapter(1));
        CampaignFacilityUnlocks.Tokens.Add(CampaignFacilityUnlocks.BuildTokenForChapter(2)); SkyIslandPreludeFlow.RouteOpen=true;
        int totalGuideCash=0;
        foreach(CampaignGuideTable.Definition def in CampaignGuideTable.Definitions)
        {
            var grant=CampaignRewardTable.ForGuide(def.Id);
            Check(grant.Cash>0 || (grant.Items!=null && grant.Items.Length>0),"guide pays something "+def.Id);
            totalGuideCash+=grant.Cash;
            Quest prefab=GameplayDataSettings.QuestCollection.Get(def.QuestId);
            int rows=(grant.Cash>0?1:0)+(grant.Items==null?0:grant.Items.Length);
            Check(prefab.Rewards.Count==rows,"guide reward rows match table "+def.Id);
            Check(prefab.Rewards.OfType<OfficialQuestProjectionReward>().All(r=>r.amount==grant.Cash),"money row shows guide cash "+def.Id);
            Check(prefab.Rewards.OfType<OfficialQuestProjectionItemReward>().Select(r=>r.typeId+":"+r.amount)
                .SequenceEqual((grant.Items??new OfficialQuestItemStack[0]).Select(i=>i.TypeId+":"+i.Count)),"item rows show table items "+def.Id);
            Check(prefab.Rewards.Select(r=>r.RewardId).Distinct().Count()==rows,"reward row ids are unique "+def.Id);
        }
        Check(totalGuideCash==74000,"guide cash total stays at the documented 74000");
        foreach(CampaignChapterDef chapter in CampaignContentCatalog.Chapters)
        {
            Quest prefab=GameplayDataSettings.QuestCollection.Get(CampaignQuestTable.QuestIdForOrder(chapter.Order));
            var items=CampaignRewardTable.ChapterItems(chapter.Order);
            Check(items!=null && items.Length>0,"chapter pays items besides cash "+chapter.ChapterId);
            Check(prefab.Rewards.OfType<OfficialQuestProjectionReward>().Single().amount==chapter.RewardCash
                && prefab.Rewards.OfType<OfficialQuestProjectionItemReward>().Count()==items.Length,"chapter reward rows "+chapter.ChapterId);
            var subs=CampaignRewardTable.ChapterSubmissions(chapter.Order);
            Check(prefab.Tasks.Count==chapter.Objectives.Count+(subs==null?0:subs.Length),"chapter submission targets projected "+chapter.ChapterId);
        }

        // Guide with a submission: the gear alone cannot deliver; 5 brass scrap are taken exactly once.
        Reset(); SkyIslandPreludeFlow.RouteOpen=true;
        foreach(CampaignGuideTable.Definition def in CampaignGuideTable.Definitions.TakeWhile(d=>d.Id!=CampaignGuideTable.SkyIslandGear))
        { manager.ActivateQuest(def.QuestId,QuestGiverID.Jeff); Tick(); Observe(def.Id); Check(Active(def.QuestId).TryComplete(),"prepare "+def.Id); }
        OfficialQuestItems.Given.Clear(); int cashBefore=CampaignProgressService.GuideCash;
        var gearDef=CampaignGuideTable.Find(CampaignGuideTable.SkyIslandGear);
        manager.ActivateQuest(gearDef.QuestId,QuestGiverID.Jeff); Tick();
        Quest gear=Active(gearDef.QuestId);
        CharacterMainControl.Main.CharacterItem.Items.Add(new ItemStatsSystem.Item {TypeID=500086});
        CampaignGuideFacts.ObserveAccepted(ModBehaviour.Instance); Tick();
        OfficialQuestItems.Backpack[BossRushItemIds.SkyIslandBrassScrap]=4;
        Check(CampaignGuideTable.IsExperienced(gearDef.Id) && !gear.AreTasksFinished(),"gear alone leaves the brass target open");
        Check(gear.Tasks.Last().Description.Contains("4/5"),"submission target shows carried count");
        Check(!gear.TryComplete() && OfficialQuestItems.HeldInBackpack(BossRushItemIds.SkyIslandBrassScrap)==4,"short submission cannot deliver or take items");
        OfficialQuestItems.Backpack[BossRushItemIds.SkyIslandBrassScrap]=7; Tick();
        Check(gear.AreTasksFinished(),"carrying enough brass finishes the target");
        Check(gear.TryComplete() && manager.HistoryQuests.Contains(gear),"guide with submission delivers");
        Check(OfficialQuestItems.HeldInBackpack(BossRushItemIds.SkyIslandBrassScrap)==2,"exactly five brass scrap taken");
        Check(CampaignProgressService.GuideCash-cashBefore==10000 && OfficialQuestItems.Given.Count==0,"gear guide pays its cash once and no items");
        Check(gear.Tasks.All(t=>t.IsFinished()) && gear.Tasks.Last().Description.Contains("(handed in)"),"submission target stays finished after items are gone");
        Check(!gear.TryComplete() && OfficialQuestItems.HeldInBackpack(BossRushItemIds.SkyIslandBrassScrap)==2,"no second take");

        // Chapter submission with alternatives, rollback on client failure and on missing reward prefab.
        Reset();
        CampaignChapterDef two=CampaignContentCatalog.Chapters.Single(c=>c.Order==2);
        int twoId=CampaignQuestTable.QuestIdForOrder(2);
        CampaignProgressService.States[two.ChapterId]=CampaignChapterState.Available;
        manager.ActivateQuest(twoId,QuestGiverID.Jeff); Tick();
        Quest q2=Active(twoId);
        CampaignProgressService.States[two.ChapterId]=CampaignChapterState.ReadyToDeliver;
        foreach(var objective in two.Objectives.Where(o=>o.IsBaseScope)) CampaignBaseObjectives.RegisterProvider(objective.Kind,()=>true);
        OfficialQuestItems.Backpack[BossRushItemIds.EmberChili]=1; Tick();
        Check(!q2.AreTasksFinished() && !q2.TryComplete(),"one harvest is not enough");
        OfficialQuestItems.Backpack[BossRushItemIds.PhantomMushroom]=1; Tick();
        Check(q2.AreTasksFinished(),"two different harvests satisfy the any-of submission");
        OfficialQuestItems.FailCreate=true;
        Check(!q2.TryComplete() && CampaignProgressService.GetState(two.ChapterId)==CampaignChapterState.ReadyToDeliver
            && OfficialQuestItems.HeldInBackpack(BossRushItemIds.EmberChili)==1 && OfficialQuestItems.Given.Count==0,"missing reward prefab aborts before taking anything");
        OfficialQuestItems.FailCreate=false;
        CampaignProgressService.FailDeliver=true; int discarded=OfficialQuestItems.Discarded;
        Check(!q2.TryComplete() && OfficialQuestItems.HeldInBackpack(BossRushItemIds.PhantomMushroom)==1 && OfficialQuestItems.Given.Count==0
            && OfficialQuestItems.Discarded>discarded,"failed client delivery returns harvests and discards prepared rewards");
        CampaignProgressService.FailDeliver=false;
        Check(q2.TryComplete() && manager.HistoryQuests.Contains(q2),"chapter 2 delivers");
        Check(OfficialQuestItems.HeldInBackpack(BossRushItemIds.EmberChili)==0 && OfficialQuestItems.HeldInBackpack(BossRushItemIds.PhantomMushroom)==0,"both harvests taken");
        var items2=CampaignRewardTable.ChapterItems(2);
        Check(items2.All(i=>Given(i.TypeId)==Expected(items2,i.TypeId)),"chapter 2 seeds given exactly once");
        Check(q2.Rewards.All(r=>r.Claimed),"reward rows claimed after delivery");
        int givenCount=OfficialQuestItems.Given.Count;
        manager.Dispose(); manager=new QuestManager(); Tick();
        Check(OfficialQuestItems.Given.Count==givenCount && manager.HistoryQuests.Any(q=>q.ID==twoId),"rebuilding the delivered projection gives nothing again");
    }
    private static void Chapters()
    {
        Reset();
        foreach(CampaignChapterDef def in CampaignContentCatalog.Chapters)
        {
            int id=CampaignQuestTable.QuestIdForOrder(def.Order);
            CampaignProgressService.States[def.ChapterId]=CampaignChapterState.Available;
            var prefab=GameplayDataSettings.QuestCollection.Get(id);
            var chapterSubs=CampaignRewardTable.ChapterSubmissions(def.Order);
            Check(prefab.MeetsPrerequisit() && prefab.Tasks.Count==def.Objectives.Count+(chapterSubs==null?0:chapterSubs.Length),"chapter offered with all objectives "+id);
            manager.ActivateQuest(id,QuestGiverID.Jeff); Tick();
            var quest=Active(id); Check(quest!=null && !quest.TryComplete(),"chapter accepted and cannot prematurely deliver "+id);
            CampaignProgressService.States[def.ChapterId]=CampaignChapterState.ReadyToDeliver; Tick();
            if(def.Objectives.Any(o=>o.IsBaseScope))
            {
                Check(!quest.TryComplete(),"missing base fact blocks delivery "+id);
                foreach(var target in quest.Tasks.Where(t=>!t.IsFinished()))
                    Check(!target.Description.Contains("(done)"),"unfinished base objective never labeled done "+id);
            }
            foreach(var objective in def.Objectives.Where(o=>o.IsBaseScope)) CampaignBaseObjectives.RegisterProvider(objective.Kind,()=>true);
            if(CampaignRewardTable.ChapterSubmissions(def.Order)!=null) OfficialQuestItems.Backpack[BossRushItemIds.DragonFruit]=2;
            Tick(); Check(quest.AreTasksFinished(),"all chapter objectives complete "+id);
            CampaignPersistence.HasWriteBarrier=true;
            Check(!quest.TryComplete() && Active(id)==quest,"failed commit keeps chapter active "+id);
            CampaignPersistence.HasWriteBarrier=false;
            Check(quest.TryComplete() && manager.HistoryQuests.Contains(quest),"chapter delivers after write recovery "+id);
            Check(quest.Rewards.All(r=>r.Claimed),"reward projection reflects delivered fact "+id);
            CampaignBaseObjectives.ResetStaticCaches(); Tick();
            Check(quest.Tasks.All(t=>t.IsFinished()) && quest.Tasks.All(t=>t.Description.Contains("(done)") || t.Description.Contains("(handed in)")),"completed chapter stays complete after base objects disappear "+id);
        }
    }
    private sealed class OtherClient : IOfficialQuestClient
    {
        public string LogTag {get{return "test";}} public bool Ready{get;set;}=true; public int Slot{get{return SavesSystem.CurrentSlot;}}
        public void BeginTick(){} public void EndTick(bool dirty){} public void RefreshMarkers(){}
        internal bool Accepted, Delivered, Done, FailAccept;
        internal OfficialQuestBinding Binding(int id)
        {
            return new OfficialQuestBinding {QuestId=id,GiverId=5901,ObjectName="test_"+id,NameKey="name",DescriptionKey="desc",Client=this,
                BeginDelivery=CampaignSaveCoordinator.BeginQuestDelivery,EndDelivery=CampaignSaveCoordinator.EndQuestDelivery,
                CanOffer=()=>!Accepted,CanDeliver=()=>Accepted && Done,IsAccepted=()=>Accepted,IsDelivered=()=>Delivered,
                Accept=(out string m)=>{if(FailAccept){m="accept write failed";return false;}m=null;Accepted=true;return true;}, Deliver=(out string m)=>{m=null;Delivered=true;return true;},
                RewardItems=new[]{new OfficialQuestItemStack(500080,2),new OfficialQuestItemStack(500078,3)},
                Tasks=new[]{new OfficialQuestTaskBinding {TaskId=1,Done=()=>Done,Description=()=>"target"}}};
        }
    }
    private static void AcceptFailureIsVisibleAndRetryable()
    {
        Reset();
        var other=new OtherClient {FailAccept=true};
        core.Register(other.Binding(590099)); Tick();
        Quest prefab=GameplayDataSettings.QuestCollection.Get(590099);
        ModBehaviour.Instance.LastMessage=null;
        manager.ActivateQuest(590099,(QuestGiverID)5901);
        Check(ModBehaviour.Instance.LastMessage=="accept write failed","failed Mod accept tells the player instead of failing silently");
        Tick(); Check(Active(590099)==null && prefab.MeetsPrerequisit(),"failed accept returns the quest to the available list");
        other.FailAccept=false; manager.ActivateQuest(590099,(QuestGiverID)5901); Tick();
        Check(other.Accepted && Active(590099)!=null,"retry after failed accept succeeds");
    }
    private static void SharedOwnership()
    {
        Reset();
        var clients=new[]{new OtherClient(),new OtherClient(),new OtherClient(),new OtherClient()};
        int[] ids={590001,590011,590012,590013};
        for(int i=0;i<ids.Length;i++){core.Register(clients[i].Binding(ids[i]));manager.ActivateQuest(ids[i],(QuestGiverID)5901);}
        Tick(); Check(GameplayDataSettings.QuestCollection.Count==24 && manager.ActiveQuests.Count==4,"all 24 ids coexist in one core");
        clients[0].Ready=false; Tick(); Check(Active(ids[0])!=null,"temporarily unavailable client is retained");clients[0].Ready=true;
        for(int i=0;i<ids.Length;i++){clients[i].Done=true;Tick();Check(Active(ids[i]).TryComplete(),"shared client delivers "+ids[i]);}
        Check(Given(500080)==8 && Given(500078)==12 && OfficialQuestItems.HeldInBackpack(500080)==8,"sky-island style client gets its reward items into the backpack once per quest");
        foreach(int qid in ids) Check(GameplayDataSettings.QuestCollection.Get(qid).Rewards.OfType<OfficialQuestProjectionItemReward>().Count()==2,"item reward rows shown for "+qid);
        // A foreign quest using an already claimed ID is never owned or filtered.
        var foreign=new GameObject("foreign").AddComponent<Quest>();
        HarmonyLib.AccessTools.Field(typeof(Quest),"id").SetValue(foreign,599999);GameplayDataSettings.QuestCollection.Add(foreign);
        core.Register(new OtherClient().Binding(599999));
        Check(!OfficialQuestProjection.IsOwnQuest(599999),"foreign ID collision is isolated");
        var data=(QuestManager.SaveData)manager.GenerateSaveData();
        data.activeQuestsData.Add(new Quest.SaveData {id=599999});data.completedQuests.Add(599999);data.everInspectedQuest.AddRange(ids);data.everInspectedQuest.Add(599999);
        var filtered=(QuestManager.SaveData)OfficialQuestProjection.FilterSaveSnapshot(data);
        Check(filtered.activeQuestsData.Count==1 && filtered.historyQuestsData.Count==0 && filtered.completedQuests.SequenceEqual(new[]{599999}) && filtered.everInspectedQuest.SequenceEqual(new[]{599999}),"four snapshot fields filter only owned IDs");
        core.Dispose(); Check(GameplayDataSettings.QuestCollection.Count==1 && foreign!=null,"cleanup retains foreign template");
        Check(manager.ActiveQuests.Count==0 && manager.HistoryQuests.Count==0,"cleanup removes all owned projections");
    }
    public static void Main(string[] args) { OfficialAssemblyContract.Run(args[0]); AllGuides(); GatedGuidesDoNotBlockChain(); AcceptFailureIsVisibleAndRetryable(); Chapters(); RewardsAndSubmissions(); SharedOwnership(); Console.WriteLine("JeffQuestFlow: PASS "+checks+" assertions"); }
}
