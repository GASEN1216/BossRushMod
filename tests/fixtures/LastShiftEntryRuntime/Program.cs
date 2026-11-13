using System;
using System.IO;
using System.Linq;
using BossRush;
using Cysharp.Threading.Tasks;
using Duckov.MiniMaps;
using ItemStatsSystem;
using NodeCanvas.DialogueTrees;
using SodaCraft.Localizations;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static readonly GameObject hostObject = new GameObject("Host");
    private static readonly ModBehaviour host = hostObject.AddComponent<ModBehaviour>();
    private static readonly LastShiftRuntimeModule module = new LastShiftRuntimeModule();
    private static GameObject Entry { get { return GameObject.All.SingleOrDefault(x => x != null && x.name == "BossRush_LastShiftEntry"); } }
    private static LastShiftEntryInteractable Boarding { get { return Entry.GetComponentInChildren<LastShiftEntryInteractable>(); } }
    private static LastShiftDeviceInteractable Talking { get { return Entry.GetComponentInChildren<LastShiftDeviceInteractable>(); } }

    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new Exception("FAIL " + label);
    }

    private static void Tick() { module.OnUpdate(0, 0); }
    private static void Advance(float seconds = 2f) { Time.unscaledTime += seconds; Tick(); }
    private static void Page()
    {
        DialogueTree.Lines.Last()();
        UniTask.Pump();
    }
    private static void Pages(int count)
    {
        for (int i = 0; i < count; i++) Page();
    }
    private static void Choice(int choice)
    {
        DialogueTree.Choices.Last()(choice);
        UniTask.Pump();
    }
    private static void Escape()
    {
        Check(UIInputManager.Escape(), "conversation claims official cancel input");
        UniTask.Pump();
    }
    private static Scene Warehouse(int handle, uint raid)
    {
        var scene = SceneManager.Load(handle, LastShiftEntryPoint.SceneName);
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = raid };
        LevelManager.Instance.IsRaidMap = true;
        LevelManager.LevelInited = true;
        SceneLoader.IsSceneLoading = false;
        CharacterMainControl.Main = new GameObject("Player").AddComponent<CharacterMainControl>();
        CharacterMainControl.Main.transform.position = LastShiftYuXiang.Position;
        LevelManager.Initialized();
        Tick();
        return scene;
    }
    private static void ResetStory()
    {
        LastShiftStory.Current = new LastShiftStoryData { Runs = 2 };
        TidegateInventory.HasStub = false;
        TidegateInventory.HasStamp = false;
    }

    private static void Main()
    {
        ModBehaviour.ModPath = Path.Combine(Path.GetTempPath(), "lastshift-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(ModBehaviour.ModPath, "Assets", "arenas"));
        File.WriteAllText(Path.Combine(ModBehaviour.ModPath, LastShiftRaidScene.BundleRelativePath), "");
        File.WriteAllText(Path.Combine(ModBehaviour.ModPath, LastShiftEntry.BundleRelativePath), "");
        try
        {
            SceneManager.Load(90, "OfficialMain");
            DuckNpcFactory.OfficialHolder = new GameObject("OfficialNpcHolder");
            module.OnAwake(host);
            module.OnAwake(host);
            Check(SceneLoader.Subscribers == 1 && SceneManager.UnloadSubscribers == 1 && Application.Subscribers == 1
                && LocalizationManager.Subscribers == 1,
                "repeated initialization keeps one lifecycle subscription");
            EntryLifecycle();
            ConversationBranches();
            BoardingBranches();
            module.OnDestroy();
            Check(SceneLoader.Subscribers == 0 && SceneManager.UnloadSubscribers == 0 && Application.Subscribers == 0
                && LocalizationManager.Subscribers == 0 && UIInputManager.CancelSubscribers == 0 && Entry == null && AssetBundle.Loaded == AssetBundle.Unloaded,
                "shutdown releases subscriptions, entry and bundle");
            Check(!InputManager.Disabled && UniTask.Pending == 0, "official input and waits are released");
            LegacySpawn();
            Console.WriteLine("PASS LastShiftEntryRuntime (" + checks + " assertions)");
        }
        finally { Directory.Delete(ModBehaviour.ModPath, true); }
    }

    private static void LegacySpawn()
    {
        int index = DuckNpcFactory.Requests.Count;
        var spawn = DuckNpcSpawner.SpawnAsync(LastShiftYuXiang.BlueprintId, LastShiftYuXiang.Position, Vector3.left).task;
        var npc = DuckNpcFactory.Create(index);
        Check(!spawn.IsCompleted && npc.transform.parent == DuckNpcFactory.OfficialHolder.transform,
            "existing three-argument callers still await equipment in their official holder");
        DuckNpcOutfitter.Complete(npc);
        Check(spawn.GetAwaiter().GetResult() == npc, "legacy spawn returns the equipped NPC");
        DuckNpcSpawner.Despawn(npc);
    }

    private static void EntryLifecycle()
    {
        var first = Warehouse(1, 0);
        Check(Entry != null && Entry.scene.handle == 1 && DuckNpcFactory.Requests.Count == 1, "first warehouse creates both owners");
        Check(!Talking.CanUse && !Boarding.CanUse, "interaction requires the visible conductor");
        SceneLoader.Begin();
        Check(Entry == null && AssetBundle.Unloaded == 1, "official loading removes the entry and releases its bundle once");
        SceneManager.Unload(first);
        SceneManager.Load(2, "OtherRaid");
        SceneLoader.IsSceneLoading = false;
        Advance();
        Check(Entry == null, "other raid never spawns the ticket desk");
        ResetStory();
        var closed = hostObject.AddComponent<LastShiftSession>();
        closed.IsClosed = true;
        var next = Warehouse(3, 1);
        Check(Entry != null && Entry.scene.handle == 3 && DuckNpcFactory.Requests.Count == 2,
            "new visit rebuilds entry while old closed session and old spawn request still exist");
        UnityEngine.Object.Destroy(closed);
        var conductor = DuckNpcFactory.Complete(1);
        var stale = DuckNpcFactory.Create(0);
        Check(conductor != null && conductor.transform.parent == Entry.transform && conductor.gameObject.scene.handle == 3,
            "latest NPC belongs to the latest entry scene");
        Check(stale == null && DuckNpcFactory.Released.Contains(stale) && conductor != null
            && !DuckNpcOutfitter.Pending.ContainsKey(stale),
            "late old factory request releases only its own NPC before outfit");
        Advance();
        Check(Talking.CanUse && Boarding.CanUse && DuckNpcFactory.Requests.Count == 2,
            "repeated updates retain one desk and one visible conductor");
        Check(Entry.GetComponentInChildren<SimplePointOfInterest>().Key == "BossRush_LastShiftYard_MapDesk",
            "warehouse marker stays after previously completing two yard runs");
        GameObject conductorObject = conductor.gameObject;
        Check(DialogueActorFactory.Cached.Contains(conductorObject), "actor is cached while scene is active");
        SceneManager.Unload(next);
        Check(Entry == null && conductor == null && AssetBundle.Loaded == AssetBundle.Unloaded,
            "additive scene unload outside SceneLoader releases entry and child NPC");
        Check(!DialogueActorFactory.Cached.Contains(conductorObject), "native unload removes the destroyed actor cache key");
        AssetBundle.FailShader = true;
        var broken = Warehouse(4, 2);
        Check(Entry == null && AssetBundle.Loaded == AssetBundle.Unloaded, "failure while inactive releases the loaded bundle");
        AssetBundle.FailShader = false;
        SceneManager.Unload(broken);
        Warehouse(5, 3);
        Check(Entry != null && DuckNpcFactory.Requests.Count == 3, "new scene bypasses previous scene retry delay");
        var staleScene = SceneManager.GetSceneByName(LastShiftEntryPoint.SceneName);
        SceneManager.Unload(staleScene);
        Warehouse(6, 4);
        Check(DuckNpcFactory.Requests.Count == 4, "re-entry can spawn while the previous request is pending");
        DuckNpcFactory.Create(2, false);
        Advance();
        Check(DuckNpcFactory.Requests.Count == 4, "stale failure preserves the new pending request");
        DialogueActorFactory.Fail = true;
        var badNpc = DuckNpcFactory.Complete(3);
        Check(badNpc == null && !Talking.CanUse, "failed actor creation releases half-built NPC");
        Advance();
        Check(DuckNpcFactory.Requests.Count == 4, "failed NPC generation respects retry budget");
        DialogueActorFactory.Fail = false;
        Advance(31f);
        var partial = DuckNpcFactory.Create(4);
        Check(partial != null && partial.transform.parent == Entry.transform && partial.gameObject.scene.handle == 6
            && DuckNpcOutfitter.Pending.ContainsKey(partial) && !Talking.CanUse && !Boarding.CanUse,
            "official factory result transfers from the main holder before outfit starts");
        SceneLoader.Begin();
        Check(partial == null && DuckNpcFactory.Released.Contains(partial) && DuckNpcOutfitter.Pending.ContainsKey(partial),
            "teardown immediately destroys partial NPC even with equipment still pending");
        SceneManager.Unload(SceneManager.GetSceneByName(LastShiftEntryPoint.SceneName));
        Warehouse(10, 8);
        var replacement = DuckNpcFactory.Complete(5);
        DuckNpcOutfitter.Complete(partial);
        Check(replacement != null && replacement.transform.parent == Entry.transform
            && Talking.CanUse && Boarding.CanUse && DialogueActorFactory.Cached.Count == 1,
            "late old equipment completion cannot clear the new actor or install another one");
    }

    private static void ConversationBranches()
    {
        ResetStory();
        int before = DialogueTree.Lines.Count;
        Talking.Interact();
        Check(LastShiftStory.Current.Met && DialogueTree.ShownText.Last().StartsWith("This station closed"), "NPC interaction enters official dialogue and marks met");
        LocalizationManager.SetLanguage(SystemLanguage.Chinese);
        Page();
        Check(DialogueTree.ShownText.Last().StartsWith("可货车") && DialogueTree.Lines.Count == before + 2,
            "next official subtitle resolves the newly selected language");
        Check(Entry.GetComponentInChildren<DuckovDialogueActor>().DisplayName == "余响"
            && Entry.GetComponentInChildren<SimplePointOfInterest>().DisplayedName == "末班车剪票处",
            "actor name and visible map pin refresh language");
        Page();
        LocalizationManager.SetLanguage(SystemLanguage.English);
        LastShiftStory.Current.Notes = 7;
        TidegateInventory.HasStub = true;
        Talking.Interact();
        Escape();
        Check(LastShiftStory.Current.Told == 0 && TidegateInventory.HasStub, "cancel before reading leaves reactions and stub intact");
        Talking.Interact();
        Pages(4);
        Check(LastShiftStory.Current.Told == 7 && DialogueTree.ShownChoices.Last()[0] == "Give her the stub",
            "read reactions and use official hand-in choices");
        Escape();
        Choice(0);
        Check(!LastShiftStory.Journal && TidegateInventory.HasStub, "late choice after Esc cannot consume stub");
        Talking.Interact();
        Page();
        Choice(1);
        Check(!LastShiftStory.Journal && TidegateInventory.HasStub, "keep-stub branch never writes the ending");
        LastShiftStory.FailSave = true;
        Talking.Interact();
        Page();
        Choice(0);
        Check(TidegateInventory.HasStub && DialogueTree.ShownText.Last().Contains("Next time"), "failed hand-in uses the official failure dialogue");
        Pages(2);
        LastShiftStory.FailSave = false;
        Talking.Interact();
        Page();
        Choice(0);
        Check(LastShiftStory.Journal && !TidegateInventory.HasStub && DialogueTree.ShownText.Last().Contains("turns the stub over"),
            "successful choice writes ending then starts official ending pages");
        Escape();
        Check(LastShiftSession.Departures == 0 && ItemAssetsCollection.Given == 0, "standalone hand-in and ending Esc never board");
    }

    private static void BoardingBranches()
    {
        ResetStory();
        Boarding.Interact();
        Pages(2);
        Check(DialogueTree.ShownChoices.Last()[0] == "Punch and board", "ticket punch opens official boarding choice");
        Escape();
        Choice(0);
        MonoBehaviour.FinishCoroutines();
        Check(LastShiftSession.Departures == 0 && ItemAssetsCollection.Given == 0 && !LastShiftStory.Current.Met, "boarding choice Esc has no boarding side effects");
        TidegateInventory.HasStub = true;
        TidegateInventory.HasStamp = true;
        Boarding.Interact();
        Pages(4);
        Choice(1);
        Check(TidegateInventory.HasStub && !LastShiftStory.Journal && LastShiftTicketPunch.Punches == 0, "wait branch keeps stub and does not punch");
        Boarding.Interact();
        Pages(4);
        Choice(0);
        Check(LastShiftStory.Journal && !TidegateInventory.HasStub && LastShiftTicketPunch.Punches == 0, "confirmed boarding submits stub and waits for ending");
        var oldEnding = DialogueTree.Lines.Last();
        SceneLoader.Begin();
        oldEnding();
        UniTask.Pump();
        MonoBehaviour.FinishCoroutines();
        Check(LastShiftTicketPunch.Punches == 0 && LastShiftSession.Departures == 0 && LastShiftStory.Journal,
            "scene teardown keeps committed ending but does not depart");
        SceneManager.Unload(SceneManager.GetSceneByName(LastShiftEntryPoint.SceneName));
        Warehouse(7, 5);
        DuckNpcFactory.Complete(6);
        ResetStory();
        TidegateInventory.HasStub = true;
        Boarding.Interact();
        Pages(3);
        Choice(0);
        Escape();
        Check(LastShiftTicketPunch.Punches == 1 && ItemAssetsCollection.Given == 1 && LastShiftSession.Departures == 0,
            "Esc skips the committed ending and continues already-confirmed boarding after the punch animation");
        MonoBehaviour.FinishCoroutines();
        Check(LastShiftSession.Departures == 1, "punch delay enters the raid exactly once");
        Advance();
        Check(Entry == null, "active yard session clears the entry");
        SceneManager.Unload(SceneManager.GetSceneByName(LastShiftEntryPoint.SceneName));
        UnityEngine.Object.Destroy(host.GetComponent<LastShiftSession>());
        Warehouse(8, 6);
        DuckNpcFactory.Complete(7);
        Boarding.Interact();
        Pages(2);
        Choice(0);
        SceneLoader.Begin();
        MonoBehaviour.FinishCoroutines();
        Check(LastShiftSession.Departures == 1, "scene loading during punch delay cannot board after scene teardown");
        SceneManager.Unload(SceneManager.GetSceneByName(LastShiftEntryPoint.SceneName));
        Warehouse(9, 7);
        var actorOwner = DuckNpcFactory.Complete(8);
        ResetStory();
        TidegateInventory.HasStub = true;
        Boarding.Interact();
        Pages(3);
        Choice(0);
        UnityEngine.Object.Destroy(actorOwner.gameObject.GetComponent<DuckovDialogueActor>());
        UniTask.Pump();
        MonoBehaviour.FinishCoroutines();
        Check(LastShiftStory.Journal && LastShiftSession.Departures == 1 && !InputManager.Disabled,
            "destroyed actor cancels a committed ending without departing");
    }
}
