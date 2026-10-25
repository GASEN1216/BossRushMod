using System;
using System.Reflection;
using BossRush;
using Saves;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    private static void Set(object target, string key, object value) { target.GetType().GetField(key, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    private static T Get<T>(object target, string key) { return (T)target.GetType().GetField(key, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static DangerousPrototypeCommission Setup(out DangerousPrototypeCommissionInteractable option)
    {
        SavesSystem.CurrentSlot = 2;
        LevelManager.AfterInit = true;
        LevelManager.Instance = new LevelManager { IsBaseLevel = true };
        CharacterMainControl.Main = new CharacterMainControl { Health = new Health() };
        SceneLoader.IsSceneLoading = false;
        ModBehaviour.Errors.Clear();
        DialogueManager.Requests.Clear();
        DangerousPrototypeCommissionPersistence.WrittenSlots.Clear();
        var module = new DangerousPrototypeCommission();
        module.OnAwake(new ModBehaviour());
        Attach(module, out option);
        Check(module.CanAccept, "eligible base player can start commission");
        return module;
    }
    private static void Attach(DangerousPrototypeCommission module, out DangerousPrototypeCommissionInteractable option)
    {
        var host = new GoblinInteractable();
        option = new DangerousPrototypeCommissionInteractable { Owner = module };
        host.Group.Add(option);
        Set(module, "host", host); Set(module, "option", option); Set(module, "nextScan", float.MaxValue);
    }
    private static void Complete(int index) { DialogueManager.Requests[index].Completion.SetResult(true); }
    private static void LateCase(string name, Action<DangerousPrototypeCommission, DangerousPrototypeCommissionInteractable> invalidate, bool mustCancel)
    {
        DangerousPrototypeCommissionInteractable option;
        var module = Setup(out option);
        module.Accept(option);
        Check(DialogueManager.Requests.Count == 1, name + ": dialogue suspended");
        invalidate(module, option);
        if (mustCancel) Check(DialogueManager.Requests[0].Token.IsCancellationRequested, name + ": cancelled promptly");
        Complete(0);
        Check(DangerousPrototypeCommissionPersistence.WrittenSlots.Count == 0, name + ": late callback cannot write any slot");
        module.OnDestroy();
        Check(SceneLoader.Subscribers == 0, name + ": loading subscription released");
        Check(ModBehaviour.Errors.Count == 0, name + ": no swallowed runtime exception");
    }
    private static void Main()
    {
        DangerousPrototypeCommissionInteractable option;
        var module = Setup(out option);
        module.Accept(new DangerousPrototypeCommissionInteractable { Owner = module });
        Check(DialogueManager.Requests.Count == 0, "foreign interactable cannot open commission");
        module.Accept(option); module.Accept(option);
        Check(DialogueManager.Requests.Count == 1, "double interaction cannot launch duplicate dialogues");
        Complete(0);
        Check(DangerousPrototypeCommissionPersistence.WrittenSlots.Count == 1
            && DangerousPrototypeCommissionPersistence.WrittenSlots[0] == 2, "normal completion writes original slot exactly once");
        Check(option == null, "completed commission destroys its interaction component with its object");
        module.OnDestroy();
        LateCase("slot event", (m, o) => { SavesSystem.CurrentSlot = 3; Get<DangerousPrototypeCommissionPersistence>(m, "persistence").ChangeSlot(); }, true);
        LateCase("silent slot drift", (m, o) => SavesSystem.CurrentSlot = 3, false);
        LateCase("scene event", (m, o) => m.OnSceneLoaded(new SceneRuntimeContext()), true);
        // The real root skips module ticks during loading; cancellation must use the event.
        LateCase("loading event without module tick", (m, o) => SceneLoader.StartLoading(), true);
        LateCase("death", (m, o) => { CharacterMainControl.Main.Health.IsDead = true; m.OnUpdate(0f, 0f); }, true);
        LateCase("new player", (m, o) => CharacterMainControl.Main = new CharacterMainControl { Health = new Health() }, false);
        LateCase("new level", (m, o) => LevelManager.Instance = new LevelManager { IsBaseLevel = true }, false);
        LateCase("NPC destroyed", (m, o) => UnityEngine.Object.Destroy(Get<GoblinInteractable>(m, "host").gameObject), false);
        LateCase("option destroyed", (m, o) => UnityEngine.Object.Destroy(o.gameObject), false);
        LateCase("shutdown", (m, o) => m.OnDestroy(), true);
        module = Setup(out option); module.Accept(option);
        GoblinNPCController oldController = Get<GoblinInteractable>(module, "host").Controller;
        Get<DangerousPrototypeCommissionPersistence>(module, "persistence").ChangeSlot();
        Check(oldController.Ends == 1, "cancellation releases NPC dialogue immediately even if backend hangs");
        Attach(module, out option); module.Accept(option);
        GoblinNPCController newController = Get<GoblinInteractable>(module, "host").Controller;
        Check(DialogueManager.Requests.Count == 2, "fresh interaction can restart after cancelled session");
        Complete(0);
        Check(!module.CanAccept && DangerousPrototypeCommissionPersistence.WrittenSlots.Count == 0, "stale finally cannot clear new dialogue owner");
        Check(newController.Ends == 0 && oldController.Ends == 1, "stale finally cannot end new NPC dialogue or repeat old cleanup");
        Complete(1);
        Check(DangerousPrototypeCommissionPersistence.WrittenSlots.Count == 1, "only the new dialogue commits");
        Check(newController.Ends == 1, "new dialogue releases its NPC exactly once");
        module.OnDestroy();
        Console.WriteLine("PrototypeCommissionLifecycle: PASS " + checks);
    }
}
