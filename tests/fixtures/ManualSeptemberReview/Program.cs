using System;
using System.Linq;
using System.Text.Json;
using BossRush;
using Cysharp.Threading.Tasks;
using UnityEngine;

class Program
{
    static int checks;
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message);checks++; }
    static PetNestPetRecord Pet(string id) { return new PetNestPetRecord { id=id,lineageKey="boss",state=(int)PetNestPetState.Deployed }; }
    static PetNestCompanionHandle CompleteSpawn()
    {
        var handle=new PetNestCompanionHandle { Character=new CharacterMainControl() };
        PetNestCompanionSpawner.Requests.Dequeue().SetResult(handle);
        return handle;
    }
    static void Delay() { UniTask.Delays.Dequeue().SetResult(true); }
    static void Main(string[] args)
    {
        var owner=new ModBehaviour();
        var attacker=new CharacterMainControl { IsMainCharacter=true,Team=Teams.scav };
        var victim=new CharacterMainControl { Team=Teams.scav };
        var hit=new DamageInfo { fromCharacter=attacker };
        var target=new Health { Character=victim };
        Check(!owner.CanTrigger(target,hit),"Mode E same-team damage cannot trigger set bonus");
        victim.Team=Teams.middle;
        Check(!owner.CanTrigger(target,hit),"neutral damage cannot trigger set bonus");
        victim.Team=Teams.wolf;
        Check(owner.CanTrigger(target,hit),"Mode E hostile damage still triggers set bonus");
        target.IsCompanion=true;
        Check(!owner.CanTrigger(target,hit),"companion remains excluded regardless of team");
        PetNestService.DeployedPet=Pet("a");
        PetNestBaseIdleSpawner.RefreshForScene(owner,1,true);
        Delay(); // A is now waiting for CreateCharacterAsync.
        PetNestService.DeployedPet=Pet("b");
        PetNestBaseIdleSpawner.NotifyDeployedPetChanged();
        Check(UniTask.Delays.Count==1,"changing cub starts replacement while old creation is pending");
        var old=CompleteSpawn();
        Check(old.Cleanups>0 && !old.Activated && PetNestBaseIdleSpawner.ActiveCount==0,"late A is recycled after seat changed");
        PetNestBaseIdleSpawner.RefreshForScene(owner,1,true);
        Check(UniTask.Delays.Count==1,"old finally cannot clear replacement in-flight state");
        Delay();
        var current=CompleteSpawn();
        Check(current.Activated && PetNestBaseIdleSpawner.ActiveCount==1,"only B is active");
        PetNestService.DeployedPet=null;
        PetNestBaseIdleSpawner.NotifyDeployedPetChanged();
        Check(current.Cleanups>0 && PetNestBaseIdleSpawner.ActiveCount==0,"clear seat recalls B immediately");

        PetNestService.DeployedPet=Pet("c");
        PetNestBaseIdleSpawner.NotifyDeployedPetChanged();
        Delay();
        PetNestBaseIdleSpawner.CleanupAll();
        var stopped=CompleteSpawn();
        Check(stopped.Cleanups>0 && !stopped.Activated,"cleanup cancels base creation after final await");
        PetNestService.DeployedPet.state=(int)PetNestPetState.Downed;
        PetNestBaseIdleSpawner.RefreshForScene(owner,1,true);
        Check(UniTask.Delays.Count==0,"downed cub cannot appear in base before recovery");
        PetNestBaseIdleSpawner.ResetStaticCaches();

        PetNestService.DeployedPet=Pet("raid-a");
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        Check(PetNestCompanionSpawner.Requests.Count==0,"sceneLoaded cannot spawn before official initialization");
        LevelManager.LevelInited=true;
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        Check(PetNestCompanionSpawner.Requests.Count==0,"LevelInited alone cannot borrow before AfterInit");
        LevelManager.AfterInit=true;
        CharacterMainControl.Main.CharacterItem=new ItemStatsSystem.Item { BaseCapacity=2 };
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        Check(PetNestCompanionSpawner.Requests.Count==0,"missing official pet inventory waits for retry");
        PetProxy.PetInventory=new ItemStatsSystem.Inventory { Capacity=2 };
        SceneLoader.IsSceneLoading=true;
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        Check(PetNestCompanionSpawner.Requests.Count==0,"loading screen keeps cub creation pending");
        SceneLoader.IsSceneLoading=false;
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        PetNestCompanionRuntime.CleanupOnce();
        old=CompleteSpawn();
        Check(old.Cleanups>0 && !PetNestCompanionRuntime.HasCompanion,"cancelled raid request cannot reappear with unchanged seat");
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        PetNestCompanionRuntime.CleanupOnce();
        PetNestService.DeployedPet=Pet("raid-b");
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        Check(PetNestCompanionSpawner.Requests.Count==2,"cancelled raid request does not block replacement");
        old=CompleteSpawn();
        Check(old.Cleanups>0 && !PetNestCompanionRuntime.HasCompanion,"cancelled raid request cannot activate");
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        Check(PetNestCompanionSpawner.Requests.Count==1,"old raid finally cannot clear newer request ownership");
        current=CompleteSpawn();
        Check(current.Activated && PetNestCompanionRuntime.ActiveCompanionPetId=="raid-b","replacement raid cub owns the slot");
        PetNestCompanionRuntime.CleanupOnce();
        PetNestCompanionRuntime.TrySpawnForScene(owner,7);
        PetNestService.DeployedPet=null;
        old=CompleteSpawn();
        Check(old.Cleanups>0 && !PetNestCompanionRuntime.HasCompanion,"seat revalidation rejects a late cub even without a notification");
        PetNestCompanionRuntime.ResetStaticCaches();
        VerifyCapacityOwnership();
        foreach(var task in UniTaskVoid.Pending) Check(task.IsCompleted && !task.IsFaulted,"all asynchronous requests finished without hidden exceptions");

        var reveal=new PetNestHatchRevealView();
        reveal.Click();
        Check(reveal.Complete && PetNestHatchRevealView.Closed==0,"skip shows complete results instead of closing");
        Check(PetNestHatchRevealView.Music==1,"skip plays shiny jackpot once");
        reveal.Click();
        Check(PetNestHatchRevealView.Closed==1 && PetNestHatchRevealView.Music==1,"second click closes without replaying audio");
        var wait=PetNestHatchRevealView.Wait();
        BossRushUI.Paused=true;
        for(int i=0;i<10;i++) Check(wait.MoveNext(),"paused reveal does not consume presentation time");
        BossRushUI.Paused=false;
        Check(wait.MoveNext() && !wait.MoveNext(),"reveal wait resumes after unpause");
        CheckExpeditionPause();

        Check(CodexSceneNames.Capture()=="sub","first-seen records combat subscene instead of shared main scene");
        Check(CodexSceneNames.Resolve("late")==null,"unready localization stays unknown");
        SceneInfoCollection.Infos["late"]=new SceneInfoEntry { DisplayName="Localized scene" };
        Check(CodexSceneNames.Resolve("late")=="Localized scene","missing scene localization is retried");
        L10n.IsChinese=false;
        SceneInfoCollection.Infos["late"].DisplayName="English scene";
        Check(CodexSceneNames.Resolve("late")=="English scene","language change invalidates scene cache");
        Check(CodexSceneNames.Resolve("raw_unknown")==null,"raw scene identifiers never become visible names");

        using(var doc=JsonDocument.Parse(System.IO.File.ReadAllText(args[0])))
        {
            int maps=0;
            foreach(var map in doc.RootElement.EnumerateArray())
            {
                string name=map.GetProperty("sceneName").GetString();
                Vector3[] points=map.GetProperty("spawnPoints").EnumerateArray().Select(Point).ToArray();
                var config=new BossRushMapConfig(name,map.GetProperty("sceneID").GetString(),name,name,points);
                JsonElement custom;
                if(map.TryGetProperty("customSpawnPos",out custom) && custom.ValueKind==JsonValueKind.Array)config.customSpawnPos=Point(custom);
                Vector3[] allowed=config.customSpawnPos.HasValue ? points.Concat(new[]{config.customSpawnPos.Value}).ToArray() : points;
                var derived=ModeHMapSupportRegistry.TryDeriveMap(config);
                Check(derived!=null,"map still supports derived Mode H: "+name);
                Check(allowed.Any(p=>Same(p,derived.PlayerSpawnPos)),"fighter uses actual spawn point: "+name);
                Check(derived.ArenaSpawnPoints.All(p=>!Same(p,derived.PlayerSpawnPos)),"fighter has an exclusive spawn: "+name);
                Check(derived.ArenaSpawnPoints.All(p=>allowed.Any(q=>Same(p,q))),"enemy positions come from source data: "+name);
                Check(allowed.Any(p=>Same(p,derived.SpectatorPos)),"spectator uses source data: "+name);
                Check(!Same(derived.ExitPos,derived.StagingPos),"exit never falls back underground: "+name);
                maps++;
            }
            Check(maps==9,"all nine BossRush maps exercised");
        }
        Console.WriteLine("ManualSeptemberReview: PASS ("+checks+" assertions)");
    }
    // Unity 会递归驱动 yield return 的子 IEnumerator，不能丢弃 Current 后误判暂停通过。
    static bool AdvancePresentation(System.Collections.Generic.Stack<System.Collections.IEnumerator> stack)
    {
        while (stack.Count > 0)
        {
            var current = stack.Peek();
            if (!current.MoveNext()) { stack.Pop(); continue; }
            var child = current.Current as System.Collections.IEnumerator;
            if (child != null) { stack.Push(child); continue; }
            return true;
        }
        return false;
    }
    static void CheckExpeditionPause()
    {
        var reveal = new PetNestExpeditionRevealView();
        var stack = new System.Collections.Generic.Stack<System.Collections.IEnumerator>();
        stack.Push(reveal.Play());
        BossRushUI.Paused = true;
        for (int i = 0; i < 20; i++) Check(AdvancePresentation(stack), "paused expedition keeps its presentation alive");
        Check(!reveal.HasDetail && PetNestExpeditionService.Revealed == 0,
            "paused expedition cannot reach result or mark it revealed");
        BossRushUI.Paused = false;
        for (int i = 0; i < 20 && !reveal.HasDetail; i++) AdvancePresentation(stack);
        Check(reveal.HasDetail && PetNestExpeditionService.Revealed == 0, "unpaused expedition reaches readable result before acknowledgement");
        BossRushUI.Paused = true;
        for (int i = 0; i < 20; i++) Check(AdvancePresentation(stack), "pausing during result preserves its hold time");
        Check(PetNestExpeditionService.Revealed == 0 && PetNestExpeditionRevealView.Closed == 0,
            "pause during hold neither consumes nor closes the card");
        BossRushUI.Paused = false;
        for (int i = 0; i < 20 && AdvancePresentation(stack); i++) { }
        // 2026-09-23（UA-13）：最后一张不再自动收起，「跳过」变「关闭」等玩家自己点；这里只核对只翻一次、翻完停在可关闭状态。
        Check(stack.Count == 0 && PetNestExpeditionService.Revealed == 1 && PetNestExpeditionRevealView.Closed == 0 && reveal.Finished,
            "resuming finishes the same expedition exactly once and waits for the player to close");
        Check(reveal.Summaries == 0, "a single card waits on its own result instead of a one-line summary");

        // 2026-09-24（A-40）：一次翻两张以上时收成一屏汇总再等关闭，前几张的结果不会一闪就没
        var batch = new PetNestExpeditionRevealView();
        batch.SetPending(3);
        int revealedBefore = PetNestExpeditionService.Revealed;
        var batchStack = new System.Collections.Generic.Stack<System.Collections.IEnumerator>();
        batchStack.Push(batch.Play());
        for (int i = 0; i < 400 && AdvancePresentation(batchStack); i++) { }
        Check(batchStack.Count == 0 && PetNestExpeditionService.Revealed == revealedBefore + 3
              && batch.Summaries == 1 && batch.Finished && PetNestExpeditionRevealView.Closed == 0,
            "multi-card reveal marks each card once, then shows one summary and waits for the player to close");
    }
    static void VerifyCapacityOwnership()
    {
        var player=CharacterMainControl.Main;
        var characterItem=new ItemStatsSystem.Item { BaseCapacity=2 };
        player.CharacterItem=characterItem;
        var inventory=new ItemStatsSystem.Inventory { Capacity=2 };
        PetProxy.PetInventory=inventory;
        var official=new CharacterMainControl();
        LevelManager.Instance.SetOfficialPet(official);
        var cub=new CharacterMainControl { IsCompanion=true };
        string reason;
        Check(PetNestPetProxyBridge.TryBorrowSeat(cub,out reason),"cub borrows ready official seat");
        Check(PetNestPetProxyBridge.ApplyCapacityBonus(player,4) && player.PetCapcity==6 && inventory.Capacity==6,
            "capacity bonus expands the official inventory immediately from 2 to 6");
        Check(PetNestPetProxyBridge.ApplyCapacityBonus(player,4) && inventory.Capacity==6,
            "repeated application never stacks bonus");
        var retained=new ItemStatsSystem.Item();var overflow=new ItemStatsSystem.Item();
        for(int i=0;i<6;i++)inventory.Content.Add(null);
        inventory.Content[0]=retained;inventory.Content[5]=overflow;
        retained.InInventory=inventory;overflow.InInventory=inventory;
        characterItem.BaseCapacity=3; // Official training/equipment changed while this bonus was active.
        PetNestPetProxyBridge.ReleaseSeat();
        Check(LevelManager.Instance.PetCharacter==official,"cleanup restores original pet seat");
        PetNestPetProxyBridge.RemoveCapacityBonus(player);
        Check(player.PetCapcity==3 && inventory.Capacity==3 && inventory.Content[0]==retained,
            "removal uses actual unmodified stat and retains legal slots");
        Check(inventory.Content[5]==null && PlayerStorage.Rescued.Contains(overflow),
            "items from disappearing slots are recovered before inventory shrink");
        int rescued=PlayerStorage.Rescued.Count;
        PetNestPetProxyBridge.RemoveCapacityBonus(player);
        Check(rescued==PlayerStorage.Rescued.Count,"repeated cleanup cannot duplicate overflow recovery");
        PetNestPetProxyBridge.ApplyCapacityBonus(player,4);
        var oldInventory=inventory;
        var nextPlayer=new CharacterMainControl { CharacterItem=new ItemStatsSystem.Item { BaseCapacity=8 } };
        var nextInventory=new ItemStatsSystem.Inventory { Capacity=8 };
        var nextItem=new ItemStatsSystem.Item();nextInventory.Content.Add(nextItem);
        CharacterMainControl.Main=nextPlayer;PetProxy.PetInventory=nextInventory;
        PetNestPetProxyBridge.RemoveCapacityBonus(nextPlayer);
        Check(characterItem.GetStatValue("PetCapcity".GetHashCode())==3 && oldInventory.Capacity==3,
            "scene cleanup removes bonus from the original CharacterItem");
        Check(nextInventory.Capacity==8 && nextInventory.Content[0]==nextItem,
            "late cleanup cannot shrink or move items from the new scene inventory");
        PetNestPetProxyBridge.ApplyCapacityBonus(nextPlayer,4);
        nextPlayer.CharacterItem.Destroyed=true;nextInventory.Destroyed=true;
        var reloadedPlayer=new CharacterMainControl { CharacterItem=new ItemStatsSystem.Item { BaseCapacity=2 } };
        var reloadedInventory=new ItemStatsSystem.Inventory { Capacity=12, Loading=true };
        var savedExtra=new ItemStatsSystem.Item();
        for(int i=0;i<12;i++)reloadedInventory.Content.Add(null);
        reloadedInventory.Content[11]=savedExtra;
        savedExtra.InInventory=reloadedInventory;
        CharacterMainControl.Main=reloadedPlayer;PetProxy.PetInventory=reloadedInventory;
        PetNestPetProxyBridge.RemoveCapacityBonus(reloadedPlayer);
        Check(PetNestPetProxyBridge.HasCapacityBonus && reloadedInventory.Capacity==12,
            "scene recovery waits until the official inventory finishes loading");
        reloadedInventory.Loading=false;
        PetNestPetProxyBridge.RemoveCapacityBonus(reloadedPlayer);
        Check(!PetNestPetProxyBridge.HasCapacityBonus && reloadedInventory.Capacity==2
            && reloadedInventory.Content[11]==null && PlayerStorage.Rescued.Contains(savedExtra),
            "destroyed scene owners recover saved extra-slot items against new actual capacity");
        nextPlayer=reloadedPlayer;
        nextPlayer.CharacterItem.HasCapacityStat=false;
        Check(!PetNestPetProxyBridge.ApplyCapacityBonus(nextPlayer,4) && !PetNestPetProxyBridge.HasCapacityBonus,
            "missing stat cannot report a successful capacity bonus");
        nextPlayer.CharacterItem.HasCapacityStat=true;
        PetNestPetProxyBridge.ApplyCapacityBonus(nextPlayer,4);
        var rejected=new ItemStatsSystem.Item { InInventory=reloadedInventory };
        reloadedInventory.Content[4]=rejected;
        PlayerStorage.ThrowBeforeAccept=true;
        PetNestPetProxyBridge.RemoveCapacityBonus(nextPlayer);
        Check(rejected.InInventory==reloadedInventory && reloadedInventory.Content[4]==rejected,
            "notification failure before buffer accept cannot orphan an item");
        PlayerStorage.ThrowBeforeAccept=false;PlayerStorage.ThrowAfterAccept=true;
        PetNestPetProxyBridge.ApplyCapacityBonus(nextPlayer,4);
        rescued=PlayerStorage.Rescued.Count;
        PetNestPetProxyBridge.RemoveCapacityBonus(nextPlayer);
        Check(PlayerStorage.Rescued.Count==rescued+1 && rejected.Destroyed && rejected.InInventory==null,
            "exception after buffer commit finishes detachment without duplicating item");
        PlayerStorage.ThrowAfterAccept=false;
        PetNestPetProxyBridge.ResetStaticCaches();
    }
    static Vector3 Point(JsonElement e) { var a=e.EnumerateArray().Select(x=>x.GetSingle()).ToArray();return new Vector3(a[0],a[1],a[2]); }
    static bool Same(Vector3 a,Vector3 b) { return Vector3.Distance(a,b)<.001f; }
}
