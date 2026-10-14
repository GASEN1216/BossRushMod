using System;
using System.Collections;
using System.Collections.Generic;
using BossRush;

internal enum ModeFPhase { Preparation, Bounty, HuntStorm, Extraction }
internal sealed class ValidationEvent
{
    internal int Invocations;
    internal Action OnInvoke;
    internal void Invoke() { Invocations++; if (OnInvoke != null) OnInvoke(); }
}
internal sealed class CountDownArea
{
    internal readonly SceneTransform transform = new SceneTransform();
    internal ValidationEvent onCountDownSucceed = new ValidationEvent();
}
internal sealed class FExtractionState
{
    internal bool IsActive = true, ExtractionPointSpawned = true;
    internal CountDownArea ActiveExtractionArea = new CountDownArea();
}
namespace Duckov.Economy { internal static class EconomyManager { internal static long Money; } }
namespace BossRush
{
    internal sealed class FExtractionHost
    {
        internal readonly ProductionFTrigger Trigger = new ProductionFTrigger();
        internal bool IsModeFActive = true;
        internal bool ModeFExtractionPointSpawnedForValidation { get { return Trigger.modeFState.ExtractionPointSpawned; } }
        internal int ModeFSuccessfulExtractionCountForValidation, Starts, Cleanup;
        internal ModeFPhase ModeFCurrentPhaseForValidation;
        internal bool TryStartModeF() { Starts++; return true; }
        internal bool DebugAdvanceModeFPhaseForValidation(out string reason)
        { reason = null; ModeFCurrentPhaseForValidation++; return true; }
        internal bool DebugTriggerModeFExtractionForValidation(out string reason) { return Trigger.Run(out reason); }
        internal void ValidationSafeCleanup() { Cleanup++; }
    }
    internal sealed class ZombieExtractionState
    {
        internal int RunId = 11, PurificationPoints;
        internal bool ExtractionChanneling;
        internal CountDownArea ActiveExtractionArea;
    }
}
internal static class ExtractionFlows
{
    private static void Drain(IEnumerator iterator, Action tick)
    {
        using (var stack = new ValidationCoroutineStack(iterator))
            while (stack.MoveNext()) { Time.realtimeSinceStartupAsDouble += 1; if (tick != null) tick(); }
    }
    private static void Fresh()
    {
        Time.realtimeSinceStartupAsDouble = 0; BossRushCoop.IsConnected = true;
        BossRushCoop.LocalAssetId = "original-slot-A"; Saves.SavesSystem.CurrentSlot = 3;
        CharacterMainControl.Main = new CharacterMainControl();
        Duckov.Economy.EconomyManager.Money = 20;
    }
    internal static void Run(Action<bool, string> check)
    {
        Fresh(); var connected = new ProductionFTrigger(); string reason;
        connected.modeFState.ActiveExtractionArea.onCountDownSucceed = null;
        check(connected.Run(out reason) && CharacterMainControl.Main.PositionCalls == 1
            && CharacterMainControl.Main.transform.position == connected.coopExtractionPosition,
            "F co-op helper places the admitted real local player in the actual circle without invoking disabled native listeners");
        connected.Admitted = false;
        check(!connected.Run(out reason) && reason == "local_personal_admission_missing" && CharacterMainControl.Main.PositionCalls == 1,
            "F co-op extraction helper cannot move an unadmitted observer or claim a successful trigger");
        Fresh(); var pending = new ProductionFExtractionCase(); double began = -1;
        Drain(pending.Run(), delegate
        {
            if (CharacterMainControl.Main.PositionCalls > 0 && began < 0) began = Time.realtimeSinceStartupAsDouble;
            if (began >= 0 && Time.realtimeSinceStartupAsDouble >= began + 16)
            { pending._host.ModeFSuccessfulExtractionCountForValidation = 1; pending._host.IsModeFActive = false; }
            if (began >= 0 && Time.realtimeSinceStartupAsDouble >= began + 20 && pending.ClosureClicks > 0) pending.BaseReady = true;
        });
        check(pending.Results.Count == 1 && pending.Results[0] == "PASS" && pending._host.Starts == 1 && pending.Restores == 1
            && pending._host.Trigger.modeFState.ActiveExtractionArea.onCountDownSucceed.Invocations == 0,
            "F actual case waits for asynchronous production receipt and native base readiness instead of breaking on the initial unresolved state");
        check(pending.ClosureClicks > 1 && pending.Metrics[0].Contains("resolved=True") && pending._host.Cleanup == 1,
            "F native base wait drives the real closure helper and finally reclaims the loadout");
        Fresh(); var unresolved = new ProductionFExtractionCase(); Drain(unresolved.Run(), null);
        check(unresolved.Results[0] == "FAIL" && unresolved.Restores == 1 && unresolved.Reasons[0] == "extraction_not_resolved",
            "F triggered but unacknowledged extraction remains FAIL through the actual timeout");
        Fresh(); BossRushCoop.IsConnected = false; var single = new ProductionFTrigger();
        check(single.Run(out reason) && single.modeFState.ActiveExtractionArea.onCountDownSucceed.Invocations == 1
            && CharacterMainControl.Main.PositionCalls == 0, "F offline validation still exercises the official countdown success event");
        Fresh(); var zombie = new ProductionZombieExtractionCase(); double channel = -1;
        Drain(zombie.Run(), delegate
        {
            if (zombie.Pickups > 0 && Time.realtimeSinceStartupAsDouble >= 2) zombie.runState.PurificationPoints = 3;
            zombie.AllInside = Time.realtimeSinceStartupAsDouble >= 5;
            if (zombie.Starts > 0 && channel < 0) channel = Time.realtimeSinceStartupAsDouble;
            if (channel >= 0 && Time.realtimeSinceStartupAsDouble >= channel + 16)
            { zombie.Active = false; Duckov.Economy.EconomyManager.Money = 23; }
        });
        check(zombie.Passed && zombie.Starts == 1 && zombie.runState.ActiveExtractionArea.onCountDownSucceed.Invocations == 0
            && zombie.Metrics.Contains("cash=20->23->23") && zombie.Metrics.Contains("l2_personal_receipt"),
            "Zombie actual helper waits for the point receipt, all living participants, countdown and personal payout; connected replay scope stays explicit");
        check(CharacterMainControl.Main.PositionCalls == 1 && zombie.AllInside,
            "Zombie only starts the actual UI extraction after the real local player and complete admitted cohort are in the circle");
        Fresh(); var denied = new ProductionZombieExtractionCase(); denied.coopZombieStarterChosen = false; Drain(denied.Run(), null);
        check(!denied.Passed && denied.Reason == "local_starter_unconfirmed" && denied.Pickups == 0 && denied.Starts == 0,
            "Zombie extraction cannot use a starter that the actual personal entrance has not confirmed");
        Fresh(); var outside = new ProductionZombieExtractionCase();
        Drain(outside.Run(), delegate { if (outside.Pickups > 0) outside.runState.PurificationPoints = 3; });
        check(!outside.Passed && outside.Reason == "living_participants_not_inside_extraction" && outside.Starts == 0,
            "Zombie living guest outside the extraction circle cannot be bypassed by the assisted test");
        Fresh(); var late = new ProductionZombieExtractionCase();
        Drain(late.Run(), delegate { if (late.Pickups > 0) late.runState.PurificationPoints = 3; late.AllInside = true;
            if (late.Starts > 0) { BossRushCoop.LocalAssetId = "slot-B"; Saves.SavesSystem.CurrentSlot = 4; } });
        check(!late.Passed && late.Reason == "settlement_asset_changed" && Duckov.Economy.EconomyManager.Money == 20,
            "Zombie extraction never verifies or attributes a pending payout to the replacement slot");
    }
}
