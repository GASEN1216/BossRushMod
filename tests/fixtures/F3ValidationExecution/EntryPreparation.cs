using System;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem;

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        internal virtual void DestroyContents() { }
        internal static void Destroy(Object value)
        { if (ReferenceEquals(value, null) || value.Destroyed) return; value.Destroyed = true; value.DestroyContents(); }
        internal static void DontDestroyOnLoad(Object value) { ((GameObject)value).Persistent = true; }
        public static bool operator ==(Object left, Object right)
        {
            bool a = ReferenceEquals(left, null) || left.Destroyed, b = ReferenceEquals(right, null) || right.Destroyed;
            return a || b ? a && b : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    internal class Component : Object { internal GameObject gameObject; }
    internal sealed class GameObject : Object
    {
        internal static GameObject LastCreated;
        internal static Action<ItemStatsSystem.Inventory> ConfigureInventory;
        private Component component;
        internal bool Active, Persistent;
        internal GameObject(string name) { LastCreated = this; }
        internal void SetActive(bool active) { Active = active; }
        internal T AddComponent<T>() where T : Component
        {
            T result = (T)Activator.CreateInstance(typeof(T), true); result.gameObject = this; component = result;
            if (result is ItemStatsSystem.Inventory && ConfigureInventory != null) ConfigureInventory((ItemStatsSystem.Inventory)(object)result);
            return result;
        }
        internal T GetComponent<T>() where T : Component { return component as T; }
        internal override void DestroyContents() { Destroy(component); }
    }
}

namespace ItemStatsSystem
{
    internal static class ItemTreeExtensions
    {
        // Native extension signature; destruction is an engine boundary, not an Item instance method.
        internal static void DestroyTree(this Item item) { UnityEngine.Object.Destroy(item); }
    }
    internal sealed class Item : UnityEngine.Object
    {
        internal int StackCount = 1, DestroyCount, TypeID;
        internal string Metadata;
        internal Inventory Inventory;
        internal Items.SlotCollection Slots;
        internal Inventory InInventory;
        internal Items.Slot PluggedIntoSlot;
        internal override void DestroyContents()
        {
            DestroyCount++;
            UnityEngine.Object.Destroy(Inventory);
            if (Slots != null) foreach (var slot in Slots) UnityEngine.Object.Destroy(slot.Content);
        }
        internal void Detach()
        {
            if (InInventory != null) InInventory.Content[InInventory.Content.IndexOf(this)] = null;
            if (PluggedIntoSlot != null) PluggedIntoSlot.Content = null;
            InInventory = null; PluggedIntoSlot = null;
        }
    }
    internal static class ItemAssetsCollection
    {
        internal static bool FactoryFails;
        internal static Item Last;
        internal static Item InstantiateSync(int type) { Last = FactoryFails ? null : new Item { Metadata = "loan:" + type, TypeID = type }; return Last; }
    }
    internal sealed class Inventory : UnityEngine.Component
    {
        internal readonly List<Item> Content = new List<Item>();
        internal int Capacity = 30, CapacityLimit = int.MaxValue, FailAddAt = -1;
        internal bool FailSetCapacity;
        internal Action AfterSetCapacity, AfterAddAt;
        internal void SetCapacity(int value)
        { if (FailSetCapacity) throw new InvalidOperationException("native capacity callback"); Capacity = Math.Min(value, CapacityLimit); if (AfterSetCapacity != null) AfterSetCapacity(); }
        internal override void DestroyContents()
        { foreach (var item in Content) UnityEngine.Object.Destroy(item); }
        internal Item GetItemAt(int index) { return index < 0 || index >= Content.Count ? null : Content[index]; }
        internal int GetFirstEmptyPosition(int start)
        { for (int i = start; i < Capacity; i++) if (GetItemAt(i) == null) return i; return -1; }
        internal bool AddAt(Item item, int index)
        {
            if (index == FailAddAt || index >= Capacity || GetItemAt(index) != null) return false;
            while (Content.Count <= index) Content.Add(null);
            Content[index] = item; item.InInventory = this; if (AfterAddAt != null) AfterAddAt(); return true;
        }
    }
}
namespace ItemStatsSystem.Items
{
    internal sealed class SlotCollection : List<Slot>
    {
        internal Slot GetSlot(string key) { return Find(s => s.Key == key); }
    }
    internal sealed class Slot
    {
        internal string Key = "armor";
        internal ItemStatsSystem.Item Content;
        internal bool Plug(ItemStatsSystem.Item item, out ItemStatsSystem.Item displaced)
        { displaced = null; if (Content != null) return false; Content = item; item.PluggedIntoSlot = this; return true; }
    }
}
internal static class ItemUtilities
{
    internal static bool DeliveryFails, DeliverToStorage;
    internal static List<ItemStatsSystem.Item> FindAllBelongsToPlayer(Predicate<ItemStatsSystem.Item> predicate)
    {
        var result = new List<ItemStatsSystem.Item>();
        if (PlayerStorage.Inventory != null)
            foreach (var item in PlayerStorage.Inventory.Content) if (item != null && predicate(item)) result.Add(item);
        foreach (var item in CharacterMainControl.Main.CharacterItem.Inventory.Content) if (item != null && predicate(item)) result.Add(item);
        return result;
    }
    internal static bool SendToPlayerCharacterInventory(ItemStatsSystem.Item item, bool merge)
    {
        if (DeliveryFails) return false;
        var inventory = DeliverToStorage ? PlayerStorage.Inventory : CharacterMainControl.Main.CharacterItem.Inventory;
        return inventory != null && inventory.AddAt(item, inventory.GetFirstEmptyPosition(0));
    }
}
internal static class PlayerStorage { internal static ItemStatsSystem.Inventory Inventory; }
namespace Saves { internal static class SavesSystem { internal static int CurrentSlot; } }
namespace BossRush
{
    internal static class ModBehaviour { internal const bool DevModeEnabled = true; internal static void DevLog(string value) { } }
    internal static class BossRushCoop
    {
        internal static bool IsConnected; internal static string LocalAssetId = "original-slot-A";
        internal static readonly BossRushCoopSceneBarrier SceneReady = new BossRushCoopSceneBarrier();
        internal static string ReadyPeer = "host";
        internal static void MarkSceneReady(string key) { SceneReady.Mark(ReadyPeer, key); }
        internal static bool AllPlayersSceneReady(string key) { return SceneReady.AllReady(key, new[] { "host", "guest1", "guest2" }); }
        internal static void ResetSceneReady() { IsConnected = false; ReadyPeer = "host"; SceneReady.Clear(); }
    }
}

internal static class EntryPreparation
{
    private static ItemStatsSystem.Item root, armor, stack;
    private static ItemStatsSystem.Items.Slot armorSlot;
    private static void Fresh()
    {
        root = new ItemStatsSystem.Item { Inventory = new ItemStatsSystem.Inventory(), Slots = new ItemStatsSystem.Items.SlotCollection() };
        armor = new ItemStatsSystem.Item { Metadata = "durability=47;affix=rare;child=original" };
        stack = new ItemStatsSystem.Item { StackCount = 19, Metadata = "original-stack" };
        armorSlot = new ItemStatsSystem.Items.Slot(); root.Slots.Add(armorSlot);
        ItemStatsSystem.Item displaced; armorSlot.Plug(armor, out displaced); root.Inventory.AddAt(stack, 4);
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = root };
        // Entering the real arena destroys Base's PlayerStorage; it is absent by default here too.
        PlayerStorage.Inventory = null;
        UnityEngine.GameObject.ConfigureInventory = null;
        ItemStatsSystem.ItemAssetsCollection.FactoryFails = ItemUtilities.DeliveryFails = ItemUtilities.DeliverToStorage = false;
        Saves.SavesSystem.CurrentSlot = 3; BossRushCoop.LocalAssetId = "original-slot-A";
    }
    private static bool Naked() { return armorSlot.Content == null && root.Inventory.GetItemAt(4) == null; }
    private static ItemStatsSystem.Inventory Escrow { get { return UnityEngine.GameObject.LastCreated.GetComponent<ItemStatsSystem.Inventory>(); } }
    internal static void Run(Action<bool, string> check)
    {
        Fresh(); F3ModeDLoadoutLease lease; string reason;
        check(!F3ModeDLoadoutLease.TryPrepare(false, Naked, out lease, out reason)
            && armorSlot.Content == armor && stack.InInventory == root.Inventory, "D preparation rejects a non-dedicated slot without touching original objects");
        UnityEngine.GameObject.ConfigureInventory = inventory => inventory.CapacityLimit = 1;
        check(!F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason)
            && reason == "mode_d_original_storage_full" && armorSlot.Content == armor && stack.InInventory == root.Inventory,
            "D preparation preflights escrow's actual capacity before detaching any original");
        Fresh();
        check(F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason) && Naked()
            && PlayerStorage.Inventory == null && armor.InInventory == Escrow && stack.InInventory == Escrow
            && Escrow.gameObject.Persistent && !Escrow.gameObject.Active,
            "D preparation holds exact originals in a persistent native Inventory while the arena has no PlayerStorage");
        var carrier = Escrow.gameObject;
        var starter = new ItemStatsSystem.Item(); root.Inventory.AddAt(starter, 0);
        check(lease.TryRestore() && armorSlot.Content == armor && root.Inventory.GetItemAt(4) == stack
            && armor.Metadata == "durability=47;affix=rare;child=original" && stack.StackCount == 19 && starter.DestroyCount == 1
            && carrier == null,
            "D finally returns the same objects to the same original slot and position and reclaims only the test starter");
        check(lease.TryRestore() && starter.DestroyCount == 1 && armor.DestroyCount == 0 && stack.DestroyCount == 0,
            "D repeated restore never deletes original objects or repeats starter cleanup");
        Fresh(); UnityEngine.GameObject.ConfigureInventory = inventory => inventory.FailAddAt = 1;
        check(!F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason)
            && armorSlot.Content == armor && root.Inventory.GetItemAt(4) == stack,
            "D partial official storage failure returns already moved originals and leaves untouched originals intact");
        Fresh(); check(F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason), "D slot-change setup acquires original loadout");
        var originalRoot = root; var originalArmor = armor; var originalStack = stack;
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new ItemStatsSystem.Item { Inventory = new ItemStatsSystem.Inventory() } };
        Saves.SavesSystem.CurrentSlot = 4; BossRushCoop.LocalAssetId = "replacement-slot-B";
        carrier = Escrow.gameObject;
        check(!lease.TryRestore() && originalArmor.InInventory == Escrow && originalStack.InInventory == Escrow
            && carrier != null && originalArmor.DestroyCount == 0 && originalStack.DestroyCount == 0
            && CharacterMainControl.Main.CharacterItem.Inventory.Content.Count == 0,
            "D cancellation after slot replacement keeps exact originals in storage and never writes them into the replacement asset");
        Saves.SavesSystem.CurrentSlot = 3; BossRushCoop.LocalAssetId = "original-slot-A";
        CharacterMainControl.Main.CharacterItem = originalRoot;
        check(lease.TryRestore() && armorSlot.Content == originalArmor && originalRoot.Inventory.GetItemAt(4) == originalStack,
            "D retained originals can be restored on returning to their captured slot and character root");
        Fresh(); check(F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason), "E/F loan setup preserves originals");
        check(lease.TryGiveLoan(500036, out reason) && root.Inventory.Content.Contains(ItemStatsSystem.ItemAssetsCollection.Last)
            && armor.InInventory == Escrow && stack.InInventory == Escrow,
            "entry fees are newly delivered to the real player root while original fee stacks remain held");
        var fee = ItemStatsSystem.ItemAssetsCollection.Last;
        check(lease.TryRestore() && fee.DestroyCount == 1 && stack.StackCount == 19 && stack.DestroyCount == 0,
            "unused test fee is reclaimed without spending or rebuilding an original whole stack");
        Fresh(); F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason); ItemUtilities.DeliverToStorage = true;
        PlayerStorage.Inventory = new ItemStatsSystem.Inventory();
        check(!lease.TryGiveLoan(500036, out reason) && ItemStatsSystem.ItemAssetsCollection.Last.DestroyCount == 1
            && lease.TryRestore() && armorSlot.Content == armor,
            "a delivery reporting success in storage cannot satisfy the personal entry loan and cleans only its new object");
        Fresh(); F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason); ItemStatsSystem.ItemAssetsCollection.FactoryFails = true;
        check(!lease.TryGiveLoan(500036, out reason) && lease.TryRestore() && armorSlot.Content == armor,
            "failed fee factory leaves exact originals restorable");
        Fresh(); F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason);
        var replacement = new ItemStatsSystem.Item { Inventory = new ItemStatsSystem.Inventory(), Slots = new ItemStatsSystem.Items.SlotCollection() };
        var replacementArmor = new ItemStatsSystem.Items.Slot(); replacement.Slots.Add(replacementArmor);
        carrier = Escrow.gameObject;
        UnityEngine.Object.Destroy(root);
        CharacterMainControl.Main.CharacterItem = replacement;
        check(lease.TryRestore() && replacementArmor.Content == armor && replacement.Inventory.GetItemAt(4) == stack
            && root == null && armor.DestroyCount == 0 && stack.StackCount == 19 && carrier == null,
            "a real destroyed death/scene root cannot block exact original objects returning to the same asset's new body");
        Fresh(); stack.TypeID = 500045; F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason);
        lease.TryGiveLoan(500045, out reason); var invitation = ItemStatsSystem.ItemAssetsCollection.Last;
        bool onlyLoanVisible = false;
        check(lease.TryPayFee(500045, delegate
        {
            var available = ItemUtilities.FindAllBelongsToPlayer(i => i.TypeID == 500045);
            onlyLoanVisible = available.Count == 1 && available[0] == invitation;
            available[0].Detach(); available[0].DestroyTree(); return true;
        }, out reason) && onlyLoanVisible && stack.StackCount == 19 && stack.InInventory == Escrow,
            "native storage-first payment sees only the fresh loan and cannot consume the preserved original fee stack");
        check(lease.TryRestore() && root.Inventory.GetItemAt(4) == stack && stack.DestroyCount == 0,
            "after real fee consumption the original whole stack returns unchanged");
        Fresh(); stack.TypeID = 500045; F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason);
        check(!lease.TryPayFee(500045, delegate { throw new InvalidOperationException("official payment fault"); }, out reason)
            && stack.InInventory == Escrow && lease.TryRestore() && stack.DestroyCount == 0,
            "a thrown native payment restores all hidden originals and never rebuilds or deletes a fee object");
        Fresh(); UnityEngine.GameObject.ConfigureInventory = inventory => inventory.FailSetCapacity = true;
        check(!F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason) && lease == null
            && armorSlot.Content == armor && stack.InInventory == root.Inventory && UnityEngine.GameObject.LastCreated == null,
            "native capacity notification failure releases only an empty carrier and never detaches originals");
        Fresh(); F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason);
        carrier = Escrow.gameObject;
        var blockedRoot = new ItemStatsSystem.Item { Inventory = new ItemStatsSystem.Inventory(), Slots = new ItemStatsSystem.Items.SlotCollection() };
        var blockedSlot = new ItemStatsSystem.Items.Slot(); blockedRoot.Slots.Add(blockedSlot);
        blockedRoot.Inventory.Capacity = 0;
        UnityEngine.Object.Destroy(root); CharacterMainControl.Main.CharacterItem = blockedRoot;
        check(!lease.TryRestore() && carrier != null && stack.InInventory == Escrow && stack.DestroyCount == 0,
            "new body with insufficient inventory capacity retains originals and never destroys a nonempty escrow");
        blockedRoot.Inventory.Capacity = 30;
        check(lease.TryRestore() && blockedRoot.Inventory.GetItemAt(4) == stack && blockedSlot.Content == armor && carrier == null,
            "pending death-body restore retries exact objects and destroys the carrier only after all originals leave it");
        Fresh(); F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason);
        PlayerStorage.Inventory = new ItemStatsSystem.Inventory();
        var storedFee = new ItemStatsSystem.Item { TypeID = 500045, StackCount = 11 };
        PlayerStorage.Inventory.AddAt(storedFee, 2); carrier = Escrow.gameObject;
        var blocker = new ItemStatsSystem.Item();
        check(!lease.TryPayFee(500045, delegate { PlayerStorage.Inventory.AddAt(blocker, 2); return false; }, out reason)
            && storedFee.InInventory == Escrow && carrier != null && storedFee.DestroyCount == 0
            && !lease.TryRestore(),
            "native fee return blocked after payment retains the exact external stack in expanded escrow");
        blocker.Detach(); blocker.DestroyTree();
        check(lease.TryRestore() && PlayerStorage.Inventory.GetItemAt(2) == storedFee && storedFee.StackCount == 11 && carrier == null,
            "external fee return and original loadout both complete before escrow release");
        foreach (bool capacityCallback in new[] { true, false })
        {
            Fresh(); var capturedRoot = root;
            var unrelatedRoot = new ItemStatsSystem.Item { Inventory = new ItemStatsSystem.Inventory(), Slots = new ItemStatsSystem.Items.SlotCollection() };
            unrelatedRoot.Slots.Add(new ItemStatsSystem.Items.Slot());
            Action changeSlot = delegate { Saves.SavesSystem.CurrentSlot = 4; BossRushCoop.LocalAssetId = "replacement-slot-B"; CharacterMainControl.Main.CharacterItem = unrelatedRoot; };
            UnityEngine.GameObject.ConfigureInventory = inventory =>
            {
                if (capacityCallback) inventory.AfterSetCapacity = changeSlot; else inventory.AfterAddAt = changeSlot;
            };
            check(!F3ModeDLoadoutLease.TryPrepare(true, Naked, out lease, out reason) && reason == "mode_entry_prepare_owner_changed"
                && armor.InInventory == Escrow && stack.InInventory == Escrow && Escrow.gameObject != null
                && unrelatedRoot.Inventory.Content.Count == 0 && unrelatedRoot.Slots[0].Content == null,
                "native " + (capacityCallback ? "capacity" : "transfer") + " callback slot change never admits or restores original objects into the new asset");
            Saves.SavesSystem.CurrentSlot = 3; BossRushCoop.LocalAssetId = "original-slot-A"; CharacterMainControl.Main.CharacterItem = capturedRoot;
            Escrow.AfterSetCapacity = Escrow.AfterAddAt = null;
            check(lease.TryRestore() && armorSlot.Content == armor && capturedRoot.Inventory.GetItemAt(4) == stack && armor.DestroyCount == 0,
                "callback-interrupted preparation preserves originals for the captured slot's later restore");
        }
    }
}
