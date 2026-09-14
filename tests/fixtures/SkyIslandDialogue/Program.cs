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

        var second = Start("new");
        lateLine(); UniTask.Pump();
        Check(second.Active && DialogueTree.Choices.Count == choices, "late old subtitle cannot complete new request");
        DialogueTree.Lines[DialogueTree.Lines.Count - 1](); UniTask.Pump();
        Check(DialogueTree.Choices.Count == choices + 1 && InputManager.Disabled, "new subtitle advances to its choice");
        var lateChoice = DialogueTree.Choices[DialogueTree.Choices.Count - 1];
        second.Dispose(); UniTask.Pump();
        Check(!second.Active && !InputManager.Disabled && opened == 0 && UniTask.Pending == 0, "choice cancellation does not open stale panel");

        var third = Start("");
        lateChoice(0); UniTask.Pump();
        Check(third.Active && opened == 0, "late old choice cannot finish new conversation");
        DialogueTree.Choices[DialogueTree.Choices.Count - 1](0); UniTask.Pump();
        Check(!third.Active && !InputManager.Disabled && opened == 1, "valid selection opens one service panel");
        var goodbye = Start("");
        DialogueTree.Choices[DialogueTree.Choices.Count - 1](1); UniTask.Pump();
        Check(!goodbye.Active && opened == 1, "goodbye closes without panel");

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
