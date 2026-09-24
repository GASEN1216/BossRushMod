using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using BossRush;
using Cysharp.Threading.Tasks;
using Duckov.Crops;
using Duckov.Economy;
using HarmonyLib;
using ItemStatsSystem;
using Saves;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static void Reset()
    {
        ModBehaviour.Instance = new ModBehaviour { Enabled = true };
        CharacterMainControl.Main = new CharacterMainControl();
        SavesSystem.CurrentSlot = 2;
        SceneManager.Handle = 10;
        SceneLoader.IsSceneLoading = false;
        L10n.IsChinese = true;
        ModBehaviour.Logs.Clear();
        UnityEngine.Debug.Warnings.Clear();
        ItemAssetsCollection.ThrowOnRead = false;
        ItemAssetsCollection.Metadata.Clear();
        ItemAssetsCollection.Metadata.Add(100, new ItemMetaData { Chinese = "苹果", English = "Apple" });
        ItemAssetsCollection.Metadata.Add(500065, new ItemMetaData { Chinese = "龙息果", English = "Dragonbreath Fruit" });
    }

    private static Crop NewCrop(int typeId = 500065, int amount = 2)
    { return new Crop { Value = new CropInfo { resultNormal = typeId, resultAmount = amount } }; }

    private static async Task ExpectFailure(UniTask task, Exception expected, string name)
    {
        Exception actual = null;
        try { await task; }
        catch (Exception error) { actual = error; }
        Check(ReferenceEquals(actual, expected), name);
    }

    private static async Task Completion()
    {
        foreach (int id in new[] { 100, 500065 })
        {
            Reset();
            int amount = id == 100 ? 1 : 2;
            await GardenHarvestNoticePatch.Observe(UniTask.From(Task.CompletedTask), NewCrop(id, amount));
            string text = ModBehaviour.Instance.Banners.Single();
            Check(text.Contains(id == 100 ? "苹果" : "龙息果") && text.Contains("×" + amount), "completed delivery shows correct product and amount " + id);
            Check(text.Contains("基地仓库") && text.Contains("马蜂自提点"), "completed delivery explains storage and overflow pickup " + id);
        }

        Reset();
        var owner = ModBehaviour.Instance;
        var source = new TaskCompletionSource<bool>();
        Crop crop = NewCrop();
        UniTask notice = GardenHarvestNoticePatch.Observe(UniTask.From(source.Task), crop);
        Check(!notice.RawTask.IsCompleted && owner.Banners.Count == 0, "unfinished delivery never announces success");
        crop.Value = new CropInfo { resultNormal = 100, resultAmount = 99 };
        UnityEngine.Object.Destroy(crop.gameObject);
        Check(crop == null, "destroyed crop follows Unity null semantics");
        source.SetResult(true);
        await notice;
        Check(owner.Banners.Count == 1 && owner.Banners[0].Contains("龙息果") && owner.Banners[0].Contains("×2"), "normal crop destruction preserves captured harvest identity and amount");

        Reset();
        source = new TaskCompletionSource<bool>();
        notice = GardenHarvestNoticePatch.Observe(UniTask.From(source.Task), NewCrop());
        L10n.IsChinese = false;
        source.SetResult(true);
        await notice;
        string english = ModBehaviour.Instance.Banners.Single();
        Check(english.Contains("Dragonbreath Fruit") && english.Contains("×2") && english.Contains("Harvested"), "completion resolves current language for item and sentence");
        Check(english.Contains("base storage") && english.Contains("Package Pickup"), "English explains storage and overflow pickup");

        Reset();
        await GardenHarvestNoticePatch.Observe(UniTask.From(Task.CompletedTask), NewCrop());
        await GardenHarvestNoticePatch.Observe(UniTask.From(Task.CompletedTask), NewCrop());
        Check(ModBehaviour.Instance.Banners.Count == 2, "two same-product harvests invoke one banner each");
    }

    private static async Task FailuresAndPassthrough()
    {
        Reset();
        var expected = new InvalidOperationException("delivery rejected");
        await ExpectFailure(GardenHarvestNoticePatch.Observe(UniTask.From(Task.FromException(expected)), NewCrop()), expected,
            "synchronous delivery failure remains observable");
        Check(ModBehaviour.Instance.Banners.Count == 0, "failed delivery never announces success");

        Reset();
        var source = new TaskCompletionSource<bool>();
        UniTask notice = GardenHarvestNoticePatch.Observe(UniTask.From(source.Task), NewCrop());
        source.SetException(expected);
        await ExpectFailure(notice, expected, "delayed failure preserves original exception");
        Check(ModBehaviour.Instance.Banners.Count == 0, "delayed failure emits no success notice");

        Reset();
        source = new TaskCompletionSource<bool>();
        notice = GardenHarvestNoticePatch.Observe(UniTask.From(source.Task), NewCrop());
        source.SetCanceled();
        bool cancelled = false;
        try { await notice; } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && ModBehaviour.Instance.Banners.Count == 0, "cancelled delivery remains cancelled without success notice");

        Action[] unavailable = {
            () => ModBehaviour.Instance = null,
            () => CharacterMainControl.Main = null,
            () => ModBehaviour.Instance.Enabled = false,
        };
        foreach (Action change in unavailable)
        {
            Reset();
            var owner = ModBehaviour.Instance;
            source = new TaskCompletionSource<bool>();
            change();
            notice = GardenHarvestNoticePatch.Observe(UniTask.From(source.Task), NewCrop());
            Check(ReferenceEquals(notice.RawTask, source.Task), "missing host forwards original delivery task");
            source.SetException(expected);
            await ExpectFailure(notice, expected, "missing host does not swallow original delivery failure");
            Check(owner.Banners.Count == 0, "missing host never announces delivery");
        }

        foreach (Crop invalid in new[] { null, NewCrop(0), NewCrop(-1), NewCrop(500065, 0), NewCrop(500065, -1), new Crop { ThrowOnRead = true } })
        {
            Reset();
            source = new TaskCompletionSource<bool>();
            notice = GardenHarvestNoticePatch.Observe(UniTask.From(source.Task), invalid);
            Check(ReferenceEquals(notice.RawTask, source.Task), "invalid harvest snapshot forwards original task");
            source.SetException(expected);
            await ExpectFailure(notice, expected, "invalid snapshot preserves delivery failure");
            Check(ModBehaviour.Instance.Banners.Count == 0, "invalid snapshot emits no notice");
        }
    }

    private static async Task Lifetime()
    {
        var changes = new Dictionary<string, Action> {
            { "slot change", () => SavesSystem.CurrentSlot++ },
            { "scene handle change", () => SceneManager.Handle++ },
            { "scene loading", () => SceneLoader.IsSceneLoading = true },
            { "owner replaced", () => ModBehaviour.Instance = new ModBehaviour() },
            { "owner destroyed", () => UnityEngine.Object.Destroy(ModBehaviour.Instance.gameObject) },
            { "owner unbound", () => ModBehaviour.Instance = null },
            { "module disabled", () => ModBehaviour.Instance.Enabled = false },
            { "player replaced", () => CharacterMainControl.Main = new CharacterMainControl() },
            { "player destroyed", () => UnityEngine.Object.Destroy(CharacterMainControl.Main.gameObject) },
            { "player unbound", () => CharacterMainControl.Main = null },
        };
        foreach (var change in changes)
        {
            Reset();
            var owner = ModBehaviour.Instance;
            var source = new TaskCompletionSource<bool>();
            UniTask notice = GardenHarvestNoticePatch.Observe(UniTask.From(source.Task), NewCrop());
            change.Value();
            source.SetResult(true);
            await notice;
            Check(owner.Banners.Count == 0 && (ModBehaviour.Instance == null || ModBehaviour.Instance.Banners.Count == 0), change.Key + " prevents stale notice");
        }

        Reset();
        ItemAssetsCollection.ThrowOnRead = true;
        await GardenHarvestNoticePatch.Observe(UniTask.From(Task.CompletedTask), NewCrop());
        Check(ModBehaviour.Instance.Banners.Count == 0 && ModBehaviour.Logs.Count > 0, "metadata failure cannot turn successful delivery into a failed task");
        Reset();
        ModBehaviour.Instance.ThrowOnBanner = true;
        await GardenHarvestNoticePatch.Observe(UniTask.From(Task.CompletedTask), NewCrop());
        Check(ModBehaviour.Logs.Count > 0, "banner failure is isolated from delivery result");
        Reset();
        ItemAssetsCollection.Metadata.Clear();
        await GardenHarvestNoticePatch.Observe(UniTask.From(Task.CompletedTask), NewCrop());
        Check(ModBehaviour.Instance.Banners.Single().Contains("作物"), "empty item name uses localized fallback");
    }

    private static readonly MethodInfo Delivery = AccessTools.Method(typeof(Cost), "Return", new[] { typeof(bool), typeof(bool), typeof(int), typeof(List<Item>) });
    private static readonly MethodInfo Forget = AccessTools.Method(typeof(UniTaskExtensions), "Forget", new[] { typeof(UniTask) });
    private static readonly MethodInfo Observe = AccessTools.Method(typeof(GardenHarvestNoticePatch), "Observe");

    private static void IlContract()
    {
        Reset();
        Check(typeof(CodeInstruction).Assembly.GetName().Name == "0Harmony", "IL verification uses installed Harmony CodeInstruction");
        foreach (bool nop in new[] { false, true })
        {
            var delivery = new CodeInstruction(OpCodes.Call, Delivery);
            var forget = new CodeInstruction(OpCodes.Call, Forget);
            Label label = new DynamicMethod("LabelOwner", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
            delivery.labels.Add(label);
            var input = new List<CodeInstruction> { new CodeInstruction(OpCodes.Nop), delivery };
            if (nop) input.Add(new CodeInstruction(OpCodes.Nop));
            input.Add(forget);
            input.Add(new CodeInstruction(OpCodes.Ret));
            var output = GardenHarvestNoticePatch.Transpiler(input).ToList();
            Check(output.Count == input.Count + 2 && output.Count(c => Equals(c.operand, Observe)) == 1, "valid IL adds exactly one two-instruction observer " + nop);
            Check(output.Where(c => input.Contains(c)).SequenceEqual(input), "original instructions retain order and identity " + nop);
            int at = output.IndexOf(delivery);
            Check(output[at + 1].opcode == OpCodes.Ldarg_0 && output[at + 2].opcode == OpCodes.Call && Equals(output[at + 2].operand, Observe), "observer consumes returned task plus current crop " + nop);
            Check(output.Count(c => Equals(c.operand, Delivery)) == 1 && output.Count(c => Equals(c.operand, Forget)) == 1 && delivery.labels.Contains(label), "delivery Forget and original branch label are retained " + nop);
        }

        var invalid = new Dictionary<string, List<CodeInstruction>> {
            { "missing delivery", new List<CodeInstruction> { new CodeInstruction(OpCodes.Call, Forget), new CodeInstruction(OpCodes.Ret) } },
            { "missing Forget", new List<CodeInstruction> { new CodeInstruction(OpCodes.Call, Delivery), new CodeInstruction(OpCodes.Ret) } },
            { "other Forget overload", new List<CodeInstruction> { new CodeInstruction(OpCodes.Call, Delivery), new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(UniTaskExtensions), "Forget", new[] { typeof(UniTask), typeof(bool) })) } },
            { "instruction between calls", new List<CodeInstruction> { new CodeInstruction(OpCodes.Call, Delivery), new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Call, Forget) } },
            { "two delivery calls", new List<CodeInstruction> { new CodeInstruction(OpCodes.Call, Delivery), new CodeInstruction(OpCodes.Call, Forget), new CodeInstruction(OpCodes.Call, Delivery), new CodeInstruction(OpCodes.Call, Forget) } },
            { "unexpected callvirt", new List<CodeInstruction> { new CodeInstruction(OpCodes.Callvirt, Delivery), new CodeInstruction(OpCodes.Call, Forget) } },
        };
        foreach (var sample in invalid)
        {
            UnityEngine.Debug.Warnings.Clear();
            var output = GardenHarvestNoticePatch.Transpiler(sample.Value).ToList();
            Check(output.SequenceEqual(sample.Value) && output.All(c => !Equals(c.operand, Observe)), sample.Key + " preserves original IL");
            Check(UnityEngine.Debug.Warnings.Count == 1, sample.Key + " diagnoses rejected binding");
        }
    }

    private static async Task Run()
    {
        IlContract();
        await Completion();
        await FailuresAndPassthrough();
        await Lifetime();
        Console.WriteLine("GardenHarvestNotice regression checks=" + checks);
    }

    private static int Main()
    {
        try { Run().GetAwaiter().GetResult(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
