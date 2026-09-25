using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    internal static class Program
    {
        internal static readonly List<string> Events=new List<string>();
        private static void Check(bool value,string label)
        { if (!value) throw new Exception(label+" / "+string.Join(",",Events)); Console.WriteLine("PASS "+label); }
        private static ModBehaviour Owner()
        { var owner=new ModBehaviour { modeEActive=true }; owner.Setup(); return owner; }
        private static InteractableLootbox Box(float x)
        { var go=new GameObject(); go.transform.position=new Vector3(x,0,0); var box=go.AddComponent<InteractableLootbox>(); BossRushLootboxUtility.Boxes.Add(box); return box; }
        private static void Main()
        {
            var owner=Owner(); var other=Owner();
            var far=Box(6); var near=Box(2); var tied=Box(-2);
            Check(owner.GetCurrentAwenLootSweepTargetCount()==3 && BossRushLootboxUtility.Scans==1,"first cache read scans once");
            Time.realtimeSinceStartup=.199f;
            Check(owner.GetCurrentAwenLootSweepTargetCount()==3 && BossRushLootboxUtility.Scans==1,"cached count stays valid before 200ms");
            Time.realtimeSinceStartup=.2f;
            Check(owner.GetCurrentAwenLootSweepTargetCount()==3 && BossRushLootboxUtility.Scans==2,"cache refresh occurs at exact 200ms boundary");
            var targets=new List<AwenLootSweepTarget>(); owner.CopyCurrentAwenLootSweepTargets(targets);
            Check(targets.Select(t=>t.Lootbox).SequenceEqual(new[]{near,far,tied}),"route uses nearest next hop and stable source-order ties");
            targets[0].VisitPosition=new Vector3(99,0,0); owner.CopyCurrentAwenLootSweepTargets(targets);
            Check(targets[0].VisitPosition.x==2,"cached snapshots return independent target objects");
            UnityEngine.Object.Destroy(near.gameObject); owner.CopyCurrentAwenLootSweepTargets(targets);
            Check(targets.Count==2,"destroyed cached lootbox is omitted using Unity fake null");
            int scans=BossRushLootboxUtility.Scans;
            Check(owner.CanUseAwenLootSweepToken() && BossRushLootboxUtility.Scans==scans,"availability uses cached count without fresh route work");
            var latest=Box(1); Events.Clear();
            Check(owner.TryActivateAwenLootSweepToken(null),"activation starts sweep");
            var runner=owner.courierNPCInstance.GetComponent<AwenLootSweepRunner>();
            Check(runner.Snapshot[0].Lootbox==latest && BossRushLootboxUtility.Scans==scans+1
                  && Events.SequenceEqual(new[]{"register","begin","banner"}),"activation takes fresh targets then begins before banner");
            other.GetCurrentAwenLootSweepTargetCount(); Events.Clear();
            owner.ResetModeEFLootboxTrackerState();
            Check(Events.SequenceEqual(new[]{"cancel:True","release:True:False"}),"reset cancels runner before pending-result delivery");
            scans=BossRushLootboxUtility.Scans; other.GetCurrentAwenLootSweepTargetCount();
            Check(scans==BossRushLootboxUtility.Scans,"reset does not invalidate another owner cache");
            owner.GetCurrentAwenLootSweepTargetCount(); Check(BossRushLootboxUtility.Scans==scans+1,"reset invalidates its own cache");

            owner.modeFActive=true; owner.TryRegisterModeEFLootbox(latest);
            Check(latest.Mode==BossRushTrackedLootboxMode.ModeF && latest.Session==2,"active F context takes precedence over E");
            owner.modeFState.IsActive=false; owner.TryRegisterModeEFLootbox(latest);
            Check(latest.Mode==BossRushTrackedLootboxMode.ModeE && latest.Session==1,"inactive F state falls through to E context");
            owner.ESessionValid=false; latest.Session=77; owner.TryRegisterModeEFLootbox(latest);
            Check(latest.Session==77,"invalid session refuses marker stamp");
            for(int i=0;i<25;i++) owner.RegisterModeEFBossDeathForSweepToken();
            Check(owner.modeFRuntime.Grants==0,"invalid session deaths do not accumulate grants");
            owner.ESessionValid=true;
            for(int i=0;i<19;i++) owner.RegisterModeEFBossDeathForSweepToken();
            Check(owner.modeFRuntime.Grants==0,"nineteen deaths do not grant token");
            Events.Clear(); owner.RegisterModeEFBossDeathForSweepToken();
            Check(owner.modeFRuntime.Grants==1 && Events.SequenceEqual(new[]{"register","give:False:True","banner"}),"twentieth death registers then grants token before banner");
            other.RegisterModeEFBossDeathForSweepToken(); Check(other.modeFRuntime.Grants==0,"grant counter belongs to its owner");
            owner.CaptureModeEFLootboxBaseline(); for(int i=0;i<19;i++) owner.RegisterModeEFBossDeathForSweepToken();
            Check(owner.modeFRuntime.Grants==1,"baseline resets death counter");

            Events.Clear(); owner.modeFRuntime.Delivery=false;
            Check(!owner.TryRefundAwenLootSweepToken() && Events.SequenceEqual(new[]{"register","give:False:True"}),"refund reports failed delivery without success");
            owner.modeFRuntime.Delivery=true;
            Check(owner.TryRefundAwenLootSweepToken(),"refund succeeds through original delivery path");
            AwenLootSweepTokenConfig.Registration=false; Events.Clear();
            Check(!owner.TryActivateAwenLootSweepToken(null) && !Events.Contains("begin"),"failed runtime registration never starts runner");
            AwenLootSweepTokenConfig.Registration=true;
            var playerGo=new GameObject(); CharacterMainControl.Main=playerGo.AddComponent<CharacterMainControl>();
            owner.modeEActive=false; owner.modeFActive=false; owner.IsActive=false;
            DialogueBubblesManager.Messages.Clear(); Time.unscaledTime=10;
            owner.CanUseAwenLootSweepToken(null,true); owner.CanUseAwenLootSweepToken(null,true);
            Check(DialogueBubblesManager.Messages.Count==1,"identical failure bubbles are throttled");
            Time.unscaledTime=11.25f; owner.CanUseAwenLootSweepToken(null,true);
            Check(DialogueBubblesManager.Messages.Count==2,"bubble cooldown expires at exact boundary");
            owner.IsActive=true; owner.IsModeDActive=true;
            Check(!owner.CanUseAwenLootSweepInCurrentMode(),"Mode D remains outside arena sweep gate");
            owner.IsModeDActive=false; Check(owner.CanUseAwenLootSweepInCurrentMode(),"standard arena remains supported");

            owner.NotifyAwenLootSweepRunnerDestroyed(other.courierNPCInstance.AddComponent<AwenLootSweepRunner>());
            Events.Clear(); owner.ResetModeEFLootboxTrackerState(); Check(Events[0]=="cancel:True","foreign runner teardown does not clear owner reference");
            UnityEngine.Object.Destroy(owner.courierNPCInstance); Events.Clear(); owner.ResetModeEFLootboxTrackerState();
            Check(Events.SequenceEqual(new[]{"release:True:False"}),"destroyed runner is not cancelled again");
        }
    }
}
