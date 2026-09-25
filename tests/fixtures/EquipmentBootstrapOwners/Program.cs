using System;
using System.Collections;
using System.Reflection;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

internal static class Program
{
    private static void Check(bool value, string reason)
    {
        if (!value) throw new Exception(reason + " | " + string.Join(",", Trace.Calls));
    }
    private static IEnumerator Last(ModBehaviour host) { return host.Coroutines[host.Coroutines.Count - 1]; }
    private static void Wait(IEnumerator routine, object expected, string reason)
    {
        Check(routine.MoveNext() && ReferenceEquals(routine.Current, expected), reason);
    }
    private static void CheckOrder(params string[] values)
    {
        int previous = -1;
        foreach (string value in values)
        {
            int next = Trace.Calls.FindIndex(previous + 1, entry => entry == value);
            Check(next > previous, "order: " + value);
            previous = next;
        }
    }
    private static void Main()
    {
        var host = new ModBehaviour();
        var secondHost = new ModBehaviour();
        FieldInfo latch = typeof(ReverseScaleRuntimeModule).GetField("reverseScaleInitialized", BindingFlags.NonPublic | BindingFlags.Instance);
        host.InitReverse();
        Check((bool)latch.GetValue(host.reverseScaleRuntime) && !(bool)latch.GetValue(secondHost.reverseScaleRuntime), "reverse initialization owner isolation");
        CheckOrder("reverse.ability", "reverse.effect", "localization");
        L10n.English = true;
        host.InitReverse();
        Check(LocalizationHelper.Values[ReverseScaleConfig.LOC_KEY_DISPLAY] == ReverseScaleConfig.Instance.DisplayNameEN, "repeated initialization refreshes language");
        ReverseScaleConfig.RegisterEquipmentConfigurator();
        var item = new Item();
        EquipmentFactory.Configurator(item, ReverseScaleConfig.ItemBaseName.ToLowerInvariant());
        Check(item.DisplayNameRaw == ReverseScaleConfig.LOC_KEY_DISPLAY && item.Variables.Values[ReverseScaleConfig.VAR_HEAL_PERCENT] == 50f && item.Variables.Values[ReverseScaleConfig.VAR_BOLT_COUNT] == 8f, "registered configurator preserves identity and displayed values");
        Check(!ModBehaviour.TryConfigureReverseScale(null, ReverseScaleConfig.ItemBaseName) && !ModBehaviour.TryConfigureReverseScale(item, "other"), "public compatibility configurator keeps rejects");
        Check(ModBehaviour.TryConfigureReverseScale(item, ReverseScaleConfig.ItemBaseName), "public compatibility configurator remains functional");
        host.SceneReverse("Menu");
        Check(host.Coroutines.Count == 0, "reverse menu setup must not schedule a coroutine");
        host.SceneReverse("Gameplay");
        IEnumerator reverse = Last(host);
        Check(reverse.MoveNext() && ((WaitForSeconds)reverse.Current).Seconds == 0.5f, "reverse outer helper half-second delay");
        Wait(reverse, ModBehaviour.ReverseScaleSharedWait05sForRuntime, "reverse nested half-second delay");
        Check(!reverse.MoveNext() && Trace.Count("reverse.equipment") == 1, "reverse equipment check runs only after both waits");
        Trace.Calls.Clear();
        host.CleanupReverse();
        CheckOrder("reverse.cleanup", "destroy:ReverseScaleEffectManager");

        Trace.Calls.Clear();
        host.InitScythe();
        host.InitScythe();
        secondHost.InitScythe();
        Check(PhantomWitchAssetManager.References == 1 && Trace.Count("create:PhantomWitchScytheAbilityManager") == 1, "scythe shared static reference and manager stay unique across owners");
        PhantomWitchAssetManager.References = 0;
        int before = host.Coroutines.Count;
        host.SceneScythe("Menu");
        Check(PhantomWitchAssetManager.References == 1 && host.Coroutines.Count == before, "scene cache loss restores asset reference without menu polling");
        ExercisePlayerBinding(host, host.SceneScythe, () => PhantomWitchScytheAbilityManager.Instance, ModBehaviour.PhantomWitchScytheSharedWait05sForRuntime, "PhantomWitchScytheAbilityManager");
        Trace.Calls.Clear();
        host.CleanupScythe();
        CheckOrder("hook.remove", "scythe.cleanup", "drop.cleanup", "visual.cleanup", "asset.release");
        host.CleanupScythe();
        Check(PhantomWitchAssetManager.References == 0 && Trace.Count("asset.release") == 1, "scythe repeated cleanup must not release twice");

        Trace.Calls.Clear();
        host.InitHalberd();
        host.InitHalberd();
        CheckOrder("create:FenHuangComboManager", "create:FenHuangHalberdAbilityManager");
        Check(Trace.Count("create:FenHuangComboManager") == 1 && Trace.Count("create:FenHuangHalberdAbilityManager") == 1, "halberd repeated initialization must keep both managers unique");
        before = host.Coroutines.Count;
        host.SceneHalberd("Menu");
        Check(host.Coroutines.Count == before, "halberd menu setup must not poll");
        ExercisePlayerBinding(host, host.SceneHalberd, () => FenHuangHalberdAbilityManager.Instance, ModBehaviour.FenHuangHalberdSharedWait05sForRuntime, "FenHuangHalberdAbilityManager");
        Trace.Calls.Clear();
        host.CleanupHalberd();
        CheckOrder("halberd.cleanup", "destroy:FenHuangComboManager", "mark.cleanup");
        Check(FenHuangComboManager.Instance == null, "destroyed combo component follows Unity fake-null semantics");
        Console.WriteLine("EquipmentBootstrapOwners: PASS");
    }

    private static void ExercisePlayerBinding(ModBehaviour host, Action<string> setup, Func<AbilityManager> manager, object wait, string label)
    {
        CharacterMainControl.Main = null;
        setup("Gameplay");
        IEnumerator timeout = Last(host);
        for (int i = 0; i < 30; i++) Wait(timeout, wait, label + " timeout wait " + i);
        Check(!timeout.MoveNext(), label + " must stop after 15 seconds without a player");
        setup("Gameplay");
        IEnumerator pending = Last(host);
        Wait(pending, wait, label + " first wait");
        var firstPlayer = new CharacterMainControl();
        CharacterMainControl.Main = firstPlayer;
        Check(!pending.MoveNext() && ReferenceEquals(manager().TargetCharacter, firstPlayer), label + " binds the player observed after waiting");
        int registrations = Trace.Count(label + ".register");
        setup("Gameplay");
        Check(!Last(host).MoveNext() && Trace.Count(label + ".register") == registrations, label + " does not register twice for the same player");
        var nextPlayer = new CharacterMainControl();
        CharacterMainControl.Main = nextPlayer;
        setup("Gameplay");
        Check(!Last(host).MoveNext() && ReferenceEquals(manager().TargetCharacter, nextPlayer) && Trace.Count(label + ".rebind") == 1, label + " rebinds a replacement player");
    }
}
