using System;
using System.Linq;
using System.Threading.Tasks;
using BossRush;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;

// 执行生产 BossRushStartupGuard（迟到激活判定）与 BossRushEquipmentRescue（证据、重建、身份核对）。
// 覆盖 2026-11-26 外部复审的两个时序缺口：
//   1. 官方 CreateMainCharacterAsync 先恢复物品树、隔几帧才写 mainCharacter；本 Mod 在这几帧里激活，
//      读档失败没有补丁记号，旧判定（主角已存在才算迟到）漏救。
//   2. 重建要等几帧；期间换了存档槽 / 关卡 / 主角，旧判定把旧档装备塞给当前角色。
internal static class Program
{
    private const int Gun = 500101;
    private const string WeaponSlot = "PrimaryWeapon";

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Level
    {
        internal LevelManager Manager;
        internal CharacterMainControl Main;
        internal Item Root;
    }

    private static Level NewLevel(bool characterReady, bool inited)
    {
        var root = new Item { TypeID = 1, name = "CharacterItem" };
        root.Slots.Add(WeaponSlot);
        var main = new CharacterMainControl { CharacterItem = root };
        var manager = new LevelManager { levelInited = inited };
        if (characterReady) manager.MainCharacter = main;
        LevelManager.instance = manager;
        World.SceneObjects.Add(root);
        return new Level { Manager = manager, Main = main, Root = root };
    }

    private static Item NewOrphanShell()
    {
        // 替身把空壳设为未激活：证明孤儿扫描包含未激活对象。
        var shell = new Item { TypeID = Gun, name = "FallbackItem_" + Gun, Active = false };
        World.SceneObjects.Add(shell);
        return shell;
    }

    private static void StoreRecord()
    {
        var record = new ItemTreeData { rootInstanceID = 1 };
        var rootEntry = new ItemTreeData.DataEntry { instanceID = 1, typeID = 1 };
        rootEntry.slotContents.Add(new ItemTreeData.SlotInstanceIDPair { slot = WeaponSlot, instanceID = 2 });
        record.entries.Add(rootEntry);
        record.entries.Add(new ItemTreeData.DataEntry { instanceID = 2, typeID = Gun });
        SavesSystem.Stored = record;
    }

    private static Item Rebuild(ItemTreeData data)
    {
        var item = new Item { TypeID = data.RootData.typeID, name = "Gun" };
        World.SceneObjects.Add(item);
        return item;
    }

    private static void Reset()
    {
        BossRushStartupGuard.Shutdown();
        UniTask.DropFrames();
        World.SceneObjects.Clear();
        World.SceneScans = 0;
        LevelManager.instance = null;
        SavesSystem.CurrentSlot = 1;
        SavesSystem.LoadCalls = 0;
        ItemTreeData.Calls = 0;
        ItemTreeData.Hook = data => new UniTask<Item> { task = Task.FromResult(Rebuild(data)) };
        ItemUtilities.InventorySends = 0;
        ItemUtilities.StorageSends = 0;
        NotificationText.Messages.Clear();
        UnityEngine.Debug.Lines.Clear();
        StoreRecord();
    }

    private static void Activate(ModBehaviour host)
    {
        BossRushStartupGuard.BeginAwake();
        BossRushStartupGuard.CompleteAwake(host);
    }

    private static void Frames(int count)
    {
        for (int i = 0; i < count; i++) UniTask.StepFrame();
    }

    private static void ActivationDuringCharacterCreation()
    {
        Reset();
        // 关卡场景已在加载：LevelManager 在场，物品树已恢复失败（空壳孤儿在场），主角还没写入。
        Level level = NewLevel(false, false);
        Item orphan = NewOrphanShell();
        var host = new ModBehaviour();
        Activate(host);
        Check(LevelManager.Subscribers == 1, "Rescue must subscribe to level initialization exactly once");

        Frames(3);
        level.Manager.MainCharacter = level.Main;
        level.Manager.levelInited = true;
        LevelManager.RaiseAfterLevelInitialized();
        Frames(3);

        Item content = level.Root.Slots[WeaponSlot].Content;
        Check(ItemTreeData.Calls == 1, "Activation inside a loading level must trigger one rescue attempt, got " + ItemTreeData.Calls);
        Check(content != null && content.TypeID == Gun && content.name == "Gun", "The weapon must be rebuilt into its original slot");
        Check(orphan.Destroyed, "The fallback orphan must be destroyed once the real item is back");
        Check(ItemUtilities.InventorySends == 0 && ItemUtilities.StorageSends == 0, "A plugged weapon must not also be sent elsewhere");
        Check(NotificationText.Messages.Count == 1, "The player must get exactly one put-back notice");
        Check(UnityEngine.Debug.Lines.Any(l => l.Contains("装备抢救 reason=late-activation") && l.Contains("restored=1")),
            "The release log line must report the restored item");
    }

    private static void MainMenuActivationDoesNoWork()
    {
        Reset();
        var host = new ModBehaviour();
        Activate(host); // 主菜单：没有 LevelManager
        Level level = NewLevel(true, true);
        LevelManager.RaiseAfterLevelInitialized();
        Frames(5);
        Check(SavesSystem.LoadCalls == 0 && World.SceneScans == 0 && ItemTreeData.Calls == 0,
            "A normal load after main-menu activation must not read the save, scan the scene or rebuild anything");
        Check(level.Root.Slots[WeaponSlot].Content == null, "Nothing may be conjured without a failure");
    }

    private static Item StartDeferredRescue(out TaskCompletionSource<Item> pending, out Level level, out Item orphan)
    {
        Reset();
        level = NewLevel(true, true);
        orphan = NewOrphanShell();
        var gate = new TaskCompletionSource<Item>();
        pending = gate;
        ItemTreeData.Hook = data => new UniTask<Item> { task = gate.Task };
        Activate(new ModBehaviour());
        Frames(2);
        Check(ItemTreeData.Calls == 1, "The rebuild must be in flight before the context changes");
        var rebuilt = new Item { TypeID = Gun, name = "Gun" };
        World.SceneObjects.Add(rebuilt);
        return rebuilt;
    }

    private static void SlotAndLevelSwitchDuringRebuild()
    {
        TaskCompletionSource<Item> pending;
        Level old;
        Item orphan;
        Item rebuilt = StartDeferredRescue(out pending, out old, out orphan);
        // 等重建期间：退回主菜单换到槽 2，进了槽 2 的关卡。
        SavesSystem.CurrentSlot = 2;
        old.Manager.Destroyed = true;
        old.Main.Destroyed = true;
        old.Root.Destroyed = true;
        Level next = NewLevel(true, true);
        pending.SetResult(rebuilt);
        Frames(2);

        Check(rebuilt.Destroyed, "A rebuild that finishes in another save context must be destroyed");
        Check(ItemUtilities.InventorySends == 0 && ItemUtilities.StorageSends == 0 && next.Main.Received.Count == 0,
            "Old-save equipment must never reach the new save's character, inventory or storage");
        Check(next.Root.Slots[WeaponSlot].Content == null, "The new character's slot must stay untouched");
        Check(!orphan.Destroyed, "An aborted rescue must not destroy the evidence");
        Check(NotificationText.Messages.Count == 0, "An aborted rescue must not tell the player anything was put back");
    }

    private static void SlotOnlySwitchDuringRebuild()
    {
        TaskCompletionSource<Item> pending;
        Level level;
        Item orphan;
        Item rebuilt = StartDeferredRescue(out pending, out level, out orphan);
        SavesSystem.CurrentSlot = 2;
        pending.SetResult(rebuilt);
        Frames(2);
        Check(rebuilt.Destroyed && level.Root.Slots[WeaponSlot].Content == null && ItemUtilities.InventorySends == 0,
            "A save-slot change alone must void the rescue");
    }

    private static void UnchangedContextStillRestores()
    {
        TaskCompletionSource<Item> pending;
        Level level;
        Item orphan;
        Item rebuilt = StartDeferredRescue(out pending, out level, out orphan);
        pending.SetResult(rebuilt);
        Frames(2);
        Check(ReferenceEquals(level.Root.Slots[WeaponSlot].Content, rebuilt) && !rebuilt.Destroyed && orphan.Destroyed,
            "When nothing changed during the rebuild the weapon must go back to its slot");
    }

    public static int Main()
    {
        ActivationDuringCharacterCreation();
        MainMenuActivationDoesNoWork();
        SlotAndLevelSwitchDuringRebuild();
        SlotOnlySwitchDuringRebuild();
        UnchangedContextStillRestores();
        Reset();
        Console.WriteLine("EquipmentRescueTiming: PASS (activation during character creation, main-menu no-op, slot+level switch, slot-only switch, unchanged context)");
        return 0;
    }
}
