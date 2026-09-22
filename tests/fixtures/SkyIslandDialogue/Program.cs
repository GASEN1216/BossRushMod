using System;
using BossRush;
using Cysharp.Threading.Tasks;
using NodeCanvas.DialogueTrees;

internal static class Program
{
    private static int checks, opened;
    private static bool valid = true;
    private static void Check(bool condition, string label)
    { checks++; if (!condition) throw new Exception("FAIL " + label); }
    private static SkyIslandResidentDialogue Start(string body = "one\ntwo")
    {
        return SkyIslandResidentDialogue.Run("resident", new UnityEngine.Transform(), body, () => opened++, () => valid);
    }
    private static void Main()
    {
        var first = Start();
        Check(first.Active && InputManager.Disabled && UniTask.Pending == 1, "subtitle owns one wait and input");
        var lateLine = DialogueTree.Lines[DialogueTree.Lines.Count - 1];
        int lines = DialogueTree.Lines.Count, choices = DialogueTree.Choices.Count;
        Check(Start() == null && DialogueTree.Lines.Count == lines, "shared conversation cannot be stolen");
        first.Dispose(); first.Dispose(); UniTask.Pump();
        Check(!first.Active && !InputManager.Disabled && UniTask.Pending == 0, "subtitle cancellation releases wait and input");
        Check(DialogueTree.Lines.Count == lines && DialogueTree.Choices.Count == choices && opened == 0, "cancelled subtitle never advances or opens fallback");
        // 2026-09-14 审核 F-13：取消只停了我们的等待，官方字幕协程还挂着；要替玩家经官方 Confirm 推完，下一段才发得出去。
        Check(Dialogues.DialogueUI.ConfirmCalls == 1, "cancelled subtitle is drained through the official confirm path");

        var second = Start("new");
        lateLine(); UniTask.Pump();
        Check(second.Active && DialogueTree.Choices.Count == choices, "late old subtitle cannot complete new request");
        DialogueTree.Lines[DialogueTree.Lines.Count - 1](); UniTask.Pump();
        Check(DialogueTree.Choices.Count == choices + 1 && InputManager.Disabled, "new subtitle advances to its choice");
        var lateChoice = DialogueTree.Choices[DialogueTree.Choices.Count - 1];
        second.Dispose(); UniTask.Pump();
        Check(!second.Active && !InputManager.Disabled && opened == 0 && UniTask.Pending == 0, "choice cancellation does not open stale panel");
        Check(Dialogues.DialogueUI.instance.ConfirmedChoiceForTest == 0, "cancelled choice is pushed out through the official confirmed choice");

        var third = Start("");
        lateChoice(0); UniTask.Pump();
        Check(third.Active && opened == 0, "late old choice cannot finish new conversation");
        DialogueTree.Choices[DialogueTree.Choices.Count - 1](0); UniTask.Pump();
        Check(!third.Active && !InputManager.Disabled && opened == 1, "valid selection opens one service panel");
        var goodbye = Start("");
        DialogueTree.Choices[DialogueTree.Choices.Count - 1](1); UniTask.Pump();
        Check(!goodbye.Active && opened == 1, "goodbye closes without panel");
        // 2026-09-15 第五轮：结局后的无声钟守没有可办的事，「我想办点事」点开是空面板。没有事就不问、不开。
        int choicesBefore = DialogueTree.Choices.Count;
        var idle = SkyIslandResidentDialogue.Run("resident", new UnityEngine.Transform(), "", () => opened++, () => valid, () => false);
        UniTask.Pump();
        Check(!idle.Active && opened == 1 && DialogueTree.Choices.Count == choicesBefore && !InputManager.Disabled,
            "nothing to do: no service question and no empty panel");
        DialogueActorFactory.Fail = true;
        var idleFallback = SkyIslandResidentDialogue.Run("resident", new UnityEngine.Transform(), "line", () => opened++, () => valid, () => false);
        UniTask.Pump();
        Check(!idleFallback.Active && opened == 1, "actor failure with nothing to do opens no empty panel");
        DialogueActorFactory.Fail = false;

        var interrupted = Start("scene transition"); valid = false;
        Check(!interrupted.CanContinue(), "owner predicate detects session invalidation");
        interrupted.Dispose(); UniTask.Pump();
        Check(!interrupted.Active && opened == 1 && !InputManager.Disabled, "invalid session closes without fallback");
        Check(Start() == null, "invalid session cannot open another conversation");
        valid = true;
        DialogueActorFactory.Fail = true;
        var fallback = Start();
        Check(!fallback.Active && opened == 2, "actor failure still offers services in valid session");
        DialogueActorFactory.Fail = false;
        var noActor = SkyIslandResidentDialogue.Run("resident", null, "line", () => opened++, () => valid);
        Check(!noActor.Active && opened == 3, "missing actor still offers services in valid session");
        Check(UniTask.Pending == 0 && !InputManager.Disabled && !DialogueManager.IsDialogueActive, "all owners release resources");
        CheckOptionalConversation();
        CheckDestroyedSceneDialogue();
        CheckLegacyStoryCancellation();
        CheckCleanupStopsOldDrain();
        DialogueManager.Cleanup();
        Console.WriteLine("PASS SkyIslandDialogue (" + checks + " assertions)");
    }

    private static void CheckDestroyedSceneDialogue()
    {
        var oldActor = new DuckovDialogueActor();
        var old = DialogueManager.ShowDialogueSequenceBilingual(oldActor, new[] { new[] { "旧", "old" } }).task;
        var oldCallback = DialogueTree.Lines[DialogueTree.Lines.Count - 1];
        UnityEngine.Object.Destroy(oldActor);
        Dialogues.DialogueUI.instance = new Dialogues.DialogueUI();
        UniTask.Pump();
        Check(old.IsCompleted && !DialogueManager.IsDialogueActive && UniTask.Pending == 0, "destroyed actor and replaced UI cancel a legacy tokenless story");
        try { old.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        var actor = new DuckovDialogueActor();
        var current = DialogueManager.ShowDialogueSequenceBilingual(actor, new[] { new[] { "新", "new" } }).task;
        oldCallback(); UniTask.Pump();
        Check(!current.IsCompleted && DialogueManager.IsDialogueActive, "late callback cannot finish the replacement scene story");
        FinishLine(); current.GetAwaiter().GetResult();
        Check(UniTask.Pending == 0 && !DialogueManager.IsDialogueActive, "new scene story can complete normally");
    }

    private static void CheckLegacyStoryCancellation()
    {
        LocalizationHelper.InjectLocalization("legacy_story", "legacy story");
        var actor = new DuckovDialogueActor();
        var story = DialogueManager.ShowDialogueSequence(actor, new[] { "legacy_story" }).task;
        UnityEngine.Object.Destroy(actor);
        UniTask.Pump();
        bool cancelled = false;
        try { story.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && !DialogueManager.IsDialogueActive, "legacy key story propagates cancellation before reward and story flags");
    }

    private static void CheckCleanupStopsOldDrain()
    {
        var cancellation = new System.Threading.CancellationTokenSource();
        var actor = new DuckovDialogueActor();
        var old = DialogueManager.ShowDialogueSequenceBilingual(actor,
            new[] { new[] { "旧", "old" } }, "old_cleanup", cancellation.Token).task;
        Dialogues.DialogueUI.ConfirmCompletesRequests = false;
        UniTask.HoldNextFrame = true;
        cancellation.Cancel(); UniTask.Pump();
        try { old.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        DialogueManager.Cleanup();
        var current = DialogueManager.ShowDialogueSequenceBilingual(new DuckovDialogueActor(),
            new[] { new[] { "新", "new" } }).task;
        Dialogues.DialogueUI.ConfirmCompletesRequests = true;
        UniTask.HoldNextFrame = false;
        UniTask.ReleaseDeferredFrames(); UniTask.Pump();
        Check(!current.IsCompleted && DialogueManager.IsDialogueActive,
            "drain from the cleaned generation cannot confirm a new request on the same UI");
        FinishLine(); current.GetAwaiter().GetResult();
        Check(UniTask.Pending == 0 && !InputManager.Disabled, "cleanup replacement releases input and all waits");
        cancellation.Dispose();
    }

    private static void FinishLine()
    {
        DialogueTree.Lines[DialogueTree.Lines.Count - 1]();
        UniTask.Pump();
    }

    private static void Pick(int index)
    {
        DialogueTree.Choices[DialogueTree.Choices.Count - 1](index);
        UniTask.Pump();
    }

    private static void CheckOptionalConversation()
    {
        const string body = "The route is open. Come back whenever you need.\nI have a letter for you. It arrived this morning.";
        int before = opened, lineCount = DialogueTree.Lines.Count;
        var business = Start(body);
        FinishLine(); FinishLine();
        Check(DialogueTree.Lines.Count == lineCount + 2, "service choice arrives after two lines, before optional lore");
        Pick(0);
        Check(!business.Active && opened == before + 1 && DialogueTree.Lines.Count == lineCount + 2,
            "early business opens services without playing the rest");

        var goodbye = Start(body);
        FinishLine(); FinishLine(); Pick(1);
        Check(!goodbye.Active && opened == before + 1, "early goodbye does not open services");

        lineCount = DialogueTree.Lines.Count;
        var more = Start(body);
        FinishLine(); FinishLine(); Pick(2);
        Check(DialogueTree.Lines.Count == lineCount + 3 && opened == before + 1,
            "tell me more advances to the third line without replaying the greeting");
        Check(DialogueTree.ShownText[lineCount + 2] == "I have a letter for you.",
            "optional conversation starts with the unread text, not the greeting");
        FinishLine(); FinishLine(); Pick(0);
        Check(!more.Active && opened == before + 2 && DialogueTree.Lines.Count == lineCount + 4,
            "all optional lines remain reachable, then services open once");

        var cancel = Start(body);
        FinishLine(); FinishLine();
        var late = DialogueTree.Choices[DialogueTree.Choices.Count - 1];
        lineCount = DialogueTree.Lines.Count;
        valid = false; cancel.Dispose(); UniTask.Pump(); late(2); UniTask.Pump();
        Check(!cancel.Active && opened == before + 2 && DialogueTree.Lines.Count == lineCount,
            "cancelled early choice cannot play lore or open a stale panel");
        valid = true;

        bool hasBusiness = true;
        var changed = SkyIslandResidentDialogue.Run("resident", new UnityEngine.Transform(), body,
            () => opened++, () => valid, () => hasBusiness);
        FinishLine(); hasBusiness = false; FinishLine();
        Check(!changed.Active && opened == before + 2, "service availability is rechecked after the greeting");

        hasBusiness = true;
        var earlyUnavailable = SkyIslandResidentDialogue.Run("resident", new UnityEngine.Transform(), body,
            () => opened++, () => valid, () => hasBusiness);
        FinishLine(); FinishLine(); hasBusiness = false; Pick(0);
        Check(!earlyUnavailable.Active && opened == before + 2,
            "service disappearing during the early choice opens no empty panel");

        hasBusiness = true;
        var lateUnavailable = SkyIslandResidentDialogue.Run("resident", new UnityEngine.Transform(), body,
            () => opened++, () => valid, () => hasBusiness);
        FinishLine(); FinishLine(); Pick(2); FinishLine(); FinishLine(); hasBusiness = false; Pick(0);
        Check(!lateUnavailable.Active && opened == before + 2,
            "service availability is rechecked after the final choice too");

        int choices = DialogueTree.Choices.Count;
        var idle = SkyIslandResidentDialogue.Run("resident", new UnityEngine.Transform(), body,
            () => opened++, () => valid, () => false);
        FinishLine(); FinishLine(); FinishLine(); FinishLine();
        Check(!idle.Active && DialogueTree.Choices.Count == choices && opened == before + 2,
            "resident without services finishes the story without an empty business choice");
        Check(UniTask.Pending == 0 && !InputManager.Disabled && !DialogueManager.IsDialogueActive,
            "optional conversation releases all waits and input");
    }
}
