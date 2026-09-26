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
        ModeGRuntimeGates.IsModeGRunInProgress=false; PetNestService.PetCount=0; PetNestCompanionRuntime.HasCompanion=false; DailyReportService.IsSignedToday=false;
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
            case CampaignGuideTable.SkyIslandGear: CharacterMainControl.Main.CharacterItem.Items.Add(new ItemStatsSystem.Item {TypeID=500086}); break;
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
        foreach(CampaignGuideTable.Definition def in CampaignGuideTable.Definitions)
        {
            Quest prefab=GameplayDataSettings.QuestCollection.Get(def.QuestId);
            Check(prefab!=null && (int)prefab.QuestGiverID==1 && prefab.gameObject.activeSelf,"guide template active under Jeff "+def.Id);
            Check(prefab.DisplayNameRaw==def.NameKey && prefab.Tasks.Count==1,"guide identity and target "+def.Id);
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
    private static void Chapters()
    {
        Reset();
        foreach(CampaignChapterDef def in CampaignContentCatalog.Chapters)
        {
            int id=CampaignQuestTable.QuestIdForOrder(def.Order);
            CampaignProgressService.States[def.ChapterId]=CampaignChapterState.Available;
            var prefab=GameplayDataSettings.QuestCollection.Get(id);
            Check(prefab.MeetsPrerequisit() && prefab.Tasks.Count==def.Objectives.Count,"chapter offered with all objectives "+id);
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
            Tick(); Check(quest.AreTasksFinished(),"all chapter objectives complete "+id);
            CampaignPersistence.HasWriteBarrier=true;
            Check(!quest.TryComplete() && Active(id)==quest,"failed commit keeps chapter active "+id);
            CampaignPersistence.HasWriteBarrier=false;
            Check(quest.TryComplete() && manager.HistoryQuests.Contains(quest),"chapter delivers after write recovery "+id);
            Check(quest.Rewards.All(r=>r.Claimed),"reward projection reflects delivered fact "+id);
            CampaignBaseObjectives.ResetStaticCaches(); Tick();
            Check(quest.Tasks.All(t=>t.IsFinished()) && quest.Tasks.All(t=>t.Description.Contains("(done)")),"completed chapter stays complete after base objects disappear "+id);
        }
    }
    private sealed class OtherClient : IOfficialQuestClient
    {
        public string LogTag {get{return "test";}} public bool Ready{get;set;}=true; public int Slot{get{return SavesSystem.CurrentSlot;}}
        public void BeginTick(){} public void EndTick(bool dirty){} public void RefreshMarkers(){}
        internal bool Accepted, Delivered, Done;
        internal OfficialQuestBinding Binding(int id)
        {
            return new OfficialQuestBinding {QuestId=id,GiverId=5901,ObjectName="test_"+id,NameKey="name",DescriptionKey="desc",Client=this,
                CanOffer=()=>!Accepted,CanDeliver=()=>Accepted && Done,IsAccepted=()=>Accepted,IsDelivered=()=>Delivered,
                Accept=(out string m)=>{m=null;Accepted=true;return true;}, Deliver=(out string m)=>{m=null;Delivered=true;return true;},
                Tasks=new[]{new OfficialQuestTaskBinding {TaskId=1,Done=()=>Done,Description=()=>"target"}}};
        }
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
    public static void Main(string[] args) { OfficialAssemblyContract.Run(args[0]); AllGuides(); Chapters(); SharedOwnership(); Console.WriteLine("JeffQuestFlow: PASS "+checks+" assertions"); }
}
