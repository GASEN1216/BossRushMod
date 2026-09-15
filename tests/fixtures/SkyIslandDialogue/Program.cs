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
        DialogueManager.Cleanup();
        Console.WriteLine("PASS SkyIslandDialogue (" + checks + " assertions)");
    }
}
