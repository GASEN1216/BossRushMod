using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object component in go.Components) Destroy(component);
        }
    }
    public class GameObject : Object
    {
        public readonly List<Object> Components = new List<Object>();
        public string name = "";
        public T GetComponent<T>() where T : Object { return Components.Find(x => x is T) as T; }
        public T AddComponent<T>() where T : Object, new() { var value = new T(); Components.Add(value); return value; }
    }
    public static class Random
    {
        public static float value;
        public static float Range(float min, float max) { return value; }
    }
}

public class InteractableLootbox : UnityEngine.Object
{
    public Inventory Inventory;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
}
public class CharacterMainControl : UnityEngine.Object
{
    public string Kind;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public Preset characterPreset;
}
public class Preset { public string nameKey; }

namespace ItemStatsSystem
{
    public class Item : UnityEngine.Object
    {
        public int TypeID;
        public object ParentObject;
        public Inventory InInventory;
        public float MaxDurability = 100, Durability, DurabilityLoss = 10;
        public void DestroyTree() { Destroyed = true; }
    }
    public class Inventory : UnityEngine.Object
    {
        public int Capacity = 8, Growths;
        public bool Reject, ThrowBefore, ThrowAfter, FailGrowth;
        public List<Item> Content = new List<Item>();
        public void SetCapacity(int value)
        {
            if (FailGrowth) throw new Exception("capacity fault");
            Capacity = value; Growths++;
        }
        public int GetFirstEmptyPosition(int start)
        {
            for (int i = start; i < Capacity; i++)
                if (i >= Content.Count || Content[i] == null) return i;
            return -1;
        }
        // 官方 Inventory.AddItem / AddAt 的容量与挂载时序边界；不模拟 Unity 渲染。
        public bool AddItem(Item item)
        {
            if (ThrowBefore) throw new Exception("before attachment");
            int at = GetFirstEmptyPosition(0);
            if (Reject || at < 0 || item == null || item.ParentObject != null) return false;
            while (Content.Count <= at) Content.Add(null);
            Content[at] = item;
            item.ParentObject = this; item.InInventory = this;
            if (ThrowAfter) throw new Exception("content changed observer");
            return true;
        }
    }
    public static class ItemAssetsCollection
    {
        public static Item Last;
        public static bool ReturnNull;
        public static int Creates;
        public static Item InstantiateSync(int id)
        {
            Creates++;
            Last = ReturnNull ? null : new Item { TypeID = id };
            return Last;
        }
    }
}

namespace BossRush
{
    public partial class ModBehaviour
    {
        public bool Enabled = true;
        public static readonly List<string> Logs = new List<string>();
        public static void DevLog(string message) { Logs.Add(message); }
        public void Seed(Inventory inv, string kind)
        {
            TryAddBackMountainSeedLoot(inv, new CharacterMainControl
            { Kind = kind, gameObject = new UnityEngine.GameObject { name = kind } });
        }
        public void Descendant(Inventory inv)
        { var routine = AddDragonDescendantLoot(inv); while (routine.MoveNext()) { } }
        public bool King(Inventory inv) { return TryAddDragonKingLootItem(inv, 500001, "reward"); }
        private bool IsBackMountainConfiguredEnabled() { return Enabled; }
        private bool IsDragonDescendantBoss(CharacterMainControl boss) { return boss.Kind == "descendant"; }
        private bool IsDragonKingBoss(CharacterMainControl boss) { return boss.Kind == "king"; }
        private bool EnsureDragonBossRewardPrefabLoaded(int id, string prefix) { return BackMountainItems.Registered; }
        private void CheckDragonDescendantCollectionAchievement() { }
        private void CheckDragonKingCollectionAchievement() { }
        private void LogLootWarningLimited(string key, string message, Exception error) { DevLog(message); }
    }
    public static class BackMountainItems
    {
        public static bool Registered = true;
        public static bool EnsureRuntimeRegistration(int id) { return Registered; }
    }
    public enum BackMountainFacility { Garden }
    public static class BackMountainUnlocks
    {
        public static bool Unlocked = true;
        public static bool IsFacilityUnlocked(BackMountainFacility value) { return Unlocked; }
    }
    public static class BackMountainConfig { public const string LogPrefix = "garden"; }
    public static class BossRushItemIds
    { public const int DragonSeed = 500062, EmberSeed = 500063, PhantomSpore = 500064; }
    public static class PhantomWitchConfig { public const string BossNameKey = "PhantomWitch"; }
    public static class DragonDescendantConfig
    {
        public const float DROP_CHANCE_WEAPON = .1f, DROP_CHANCE_HELM = .3f;
        public const int DRAGON_BREATH_TYPE_ID = 500001, DRAGON_HELM_TYPE_ID = 500002, DRAGON_ARMOR_TYPE_ID = 500003;
    }
    public static class DragonBreathWeaponConfig { public static void ConfigureWeapon(Item item) { } }
    public static class AchievementTracker
    {
        public static int Collections;
        public static void OnCollectDragonDescendantLoot(int id) { Collections++; }
        public static void OnCollectDragonKingLoot(int id) { Collections++; }
    }
}
