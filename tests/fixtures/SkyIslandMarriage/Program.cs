using System;
using System.Reflection;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
    private static CharacterMainControl Npc(string id)
    {
        var go = new GameObject(id); var npc = go.AddComponent<CharacterMainControl>(); npc.Health = new Health();
        var interact = new GameObject("InteractRoot"); interact.transform.SetParent(go.transform);
        interact.AddComponent<PermanentDuckNpcInteractable>(); return npc;
    }
    private static SkyIslandSearchPoint Device(SkyIslandSession session, string name)
    {
        var go = new GameObject(name); go.transform.SetParent(session.root.transform);
        return go.AddComponent<SkyIslandSearchPoint>();
    }
    private static SkyIslandSession Session(bool weibai)
    {
        SkyIslandOfficialQuestGivers.Reset(); SkyIslandOfficialQuestGivers.UiReady = true;
        PermanentDuckNpcRegistry.Instances.Clear(); AffinityManager.Spouse = null;
        AffinityManager.Following = false; Saves.SavesSystem.CurrentSlot = 1;
        SceneLoader.IsSceneLoading = false; MarriageAsync.Reset();
        SceneManager.Current = new Scene { handle = 1, name = "SkyIslandRaid" };
        CharacterMainControl.Main = Npc("player");
        var host = new GameObject("mod"); ModBehaviour.Instance = host.AddComponent<ModBehaviour>();
        var session = host.AddComponent<SkyIslandSession>();
        session.root = new GameObject("island"); session.player = CharacterMainControl.Main;
        session.Valid = true; session.residents = new SkyIslandResidents { SpawnFinished = true };
        session.worldStory = new SkyIslandWorldStory();
        session.story = new SkyIslandStoryService { IsCurrentSlot = true };
        SkyIslandOfficialQuestStory.Source = session.story; SkyIslandOfficialQuestStory.OnIsland = true;
        SkyIslandOfficialQuestTable.Island = new[] { new SkyIslandOfficialQuestDefinition { GiverId=5901 },
            new SkyIslandOfficialQuestDefinition { GiverId=5902 }, new SkyIslandOfficialQuestDefinition { GiverId=5903 } };
        foreach(var def in SkyIslandOfficialQuestTable.Island)
        {
            string id = SkyIslandOfficialQuestTable.ResidentOfGiver(def.GiverId);
            Device(session, SkyIslandOfficialQuestTable.FallbackMarkerOfGiver(def.GiverId));
            if(def.GiverId == 5901 && !weibai) continue;
            var npc = Npc(id); var owner = npc.GetComponentInChildren<PermanentDuckNpcInteractable>();
            session.residents.Owners[id] = owner;
            if (def.GiverId == 5901) PermanentDuckNpcRegistry.Instances[id] = npc;
            SkyIslandOfficialQuestGivers.Initially(owner, def.GiverId);
        }
        return session;
    }
    private static QuestGiver Board(SkyIslandSession session) { return session.FindDeviceInteractable("Search_B").GetComponentInChildren<QuestGiver>(); }
    private static void Tick(SkyIslandResidentInteractable interaction)
    { typeof(SkyIslandResidentInteractable).GetMethod("Update", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(interaction, null); }
    private static void Givers()
    {
        var session=Session(false); SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(Board(session)!=null, "spouse staying home: board is available");
        session=Session(true); SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(Board(session)==null && SkyIslandOfficialQuestGivers.DoneCount==3, "normal resident owns giver");
        var npc=PermanentDuckNpcRegistry.GetInstance("sky_weibai");
        var old=npc.GetComponentInChildren<QuestGiver>(); DuckNpcSpawner.Despawn(npc);
        Check(old==null, "Unity parent destruction invalidates child giver");
        SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(Board(session)!=null, "mid-raid wedding immediately repairs board entry");
        int count=session.FindDeviceInteractable("Search_B").transform.Children.Count;
        for(int i=0;i<120;i++) SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(session.FindDeviceInteractable("Search_B").transform.Children.Count==count, "repair is idempotent");
        // 部分任务入口一直缺失，耗尽共用预算后，已成功的入口再因婚礼失效。
        session=Session(true);
        UnityEngine.Object.Destroy(session.residents.Owners["resident_5903"].gameObject);
        UnityEngine.Object.Destroy(session.FindDeviceInteractable("device_5903").gameObject);
        for(int i=0;i<45;i++) SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(SkyIslandOfficialQuestGivers.Attempts==40, "missing UI or device has bounded attempts");
        DuckNpcSpawner.Despawn(PermanentDuckNpcRegistry.GetInstance("sky_weibai"));
        SkyIslandOfficialQuestGivers.UiReady=false;
        SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(Board(session)==null && SkyIslandOfficialQuestGivers.Attempts==1, "invalidation resets exhausted budget before checking UI");
        SkyIslandOfficialQuestGivers.UiReady=true; SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(Board(session)!=null, "late UI receives fallback after marriage");
        session=Session(false);
        var follower=Npc("sky_weibai"); PermanentDuckNpcRegistry.Instances["sky_weibai"]=follower;
        SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(follower.GetComponentInChildren<QuestGiver>()!=null && Board(session)==null, "following spouse gets giver on existing relationship group");
        DuckNpcSpawner.Despawn(follower); PermanentDuckNpcRegistry.UnregisterInstance("sky_weibai");
        SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(Board(session)!=null, "sending following spouse home repairs entry within this raid");
        UnityEngine.Object.Destroy(session.root);
        session=Session(false); SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
        Check(Board(session)!=null, "new raid after scene destruction repairs board");
        follower=Npc("sky_weibai"); PermanentDuckNpcRegistry.Instances["sky_weibai"]=follower;
        SkyIslandResidentInteractable.AttachPermanent(follower,"sky_weibai");
        Check(Board(session)!=null && follower.GetComponentInChildren<QuestGiver>()!=null,
            "late following spouse also offers same quest after board already took over");
    }
    private static void Interactions()
    {
        foreach(string id in new[] {"sky_qinghe","sky_weibai"})
        {
            var session=Session(false); var npc=Npc(id);
            SkyIslandResidentInteractable.AttachPermanent(npc,id);
            SkyIslandResidentInteractable.AttachPermanent(npc,id);
            var owner=npc.GetComponentInChildren<PermanentDuckNpcInteractable>();
            Check(owner.Group.Count==(id=="sky_weibai"?2:1), id+": storyline and quest attach once without replacing marriage options");
            var talk=npc.GetComponentInChildren<SkyIslandResidentInteractable>();
            Check(talk.Available, id+": unmarried island resident can talk");
            talk.Click(); Check(session.worldStory.Talks==1, id+": actual click reaches current island owner");
            AffinityManager.Spouse=id; talk.Click(); Check(session.worldStory.Talks==2, id+": married follower retains island services");
            // 同一交互体跨 scene 使用：它不得保留旧 island callback。
            SceneManager.Current = new Scene { handle=2,name="Base" };
            npc.gameObject.scene=SceneManager.Current; CharacterMainControl.Main.gameObject.scene=SceneManager.Current;
            SkyIslandOfficialQuestStory.OnIsland=false;
            talk.Click(); var dialogue=SkyIslandResidentDialogue.Last;
            Check(dialogue.Active && !dialogue.Business && dialogue.Body==id+":True:False" && session.worldStory.Talks==2,
                id+": base click speaks marriage/location text without island business");
            session.story.IsCurrentSlot=false; Tick(talk);
            Check(!dialogue.Active, id+": slot change cancels base dialogue");
            session.story.IsCurrentSlot=true; talk.Click(); dialogue=SkyIslandResidentDialogue.Last;
            SceneLoader.IsSceneLoading=true; Tick(talk);
            Check(!dialogue.Active && !talk.Available, id+": scene load cancels and disables talk");
            SceneLoader.IsSceneLoading=false; talk.Click(); dialogue=SkyIslandResidentDialogue.Last;
            CharacterMainControl.Main.Health.IsDead=true; Tick(talk);
            Check(!dialogue.Active, id+": death cancels dialogue");
            CharacterMainControl.Main.Health.IsDead=false; talk.Click(); dialogue=SkyIslandResidentDialogue.Last;
            AffinityManager.Spouse=null; Tick(talk);
            Check(!dialogue.Active && !talk.Available, id+": divorce cancels base story option");
            AffinityManager.Spouse=id; talk.Click(); dialogue=SkyIslandResidentDialogue.Last;
            UnityEngine.Object.Destroy(npc.gameObject); Tick(talk);
            Check(!dialogue.Active, id+": destroyed speaker cancels its dialogue");
        }
        var s=Session(false); var other=Npc("ordinary"); SkyIslandResidentInteractable.AttachPermanent(other,"ordinary");
        Check(other.GetComponentInChildren<SkyIslandResidentInteractable>()==null,"other permanent NPCs gain no sky storyline");
    }
    private static void Divorce()
    {
        foreach(string id in new[]{"sky_qinghe","sky_weibai"})
        {
            Session(false); var npc=Npc(id); PermanentDuckNpcRegistry.Instances[id]=npc;
            ModBehaviour.Instance.HandleDivorceNpcRelocation(id);
            Check(npc==null && PermanentDuckNpcRegistry.GetInstance(id)==null,id+": divorce destroys actual spouse and unregisters");
            Check(ModBehaviour.Instance.Invalidations==1 && ModBehaviour.Instance.PlaceholderRemovals==1,id+": late restore and placeholder cleared too");
            ModBehaviour.Instance.HandleDivorceNpcRelocation(id);
            Check(PermanentDuckNpcRegistry.GetInstance(id)==null,id+": repeated cleanup is safe and never spawns at base");
        }
    }
    private static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Givers(); Interactions(); Divorce();
        MarriageLifecycleRegression.Run(Npc, Session, Check);
        ActorReuseRegression.Run(Npc, Check);
        Console.WriteLine("PASS SkyIslandMarriage: "+checks+" checks (production control flow; Unity/official UI substitutes)");
    }
}
