using System;
using System.Collections.Generic;

// 只替代宿主类型和库存边界；生产的 Eligible / Find / Consume / Reward 保持完整链接。
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an && bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null))
            {
                foreach (Component part in go.Parts) part.Destroyed = true;
                foreach (GameObject child in go.Children) Destroy(child);
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponentInChildren<T>(); }
    }
    public sealed class GameObject : Object
    {
        public readonly List<Component> Parts = new List<Component>();
        public readonly List<GameObject> Children = new List<GameObject>();
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this }; Parts.Add(component); return component;
        }
        public T GetComponent<T>() where T : Component
        {
            foreach (Component part in Parts) if (part is T) return (T)part;
            return null;
        }
        public T GetComponentInChildren<T>() where T : Component
        {
            T local = GetComponent<T>(); if (local != null) return local;
            foreach (GameObject child in Children) { T result = child.GetComponentInChildren<T>(); if (result != null) return result; }
            return null;
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 right { get { return new Vector3(1f, 0f, 0f); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float amount) { return new Vector3(a.x * amount, a.y * amount, a.z * amount); }
    }
    public static class Mathf { public static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); } }
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(object value) { Warnings.Add(value.ToString()); }
    }
}

namespace ItemStatsSystem
{
    using UnityEngine;
    public sealed class Inventory : Component
    {
        public List<Item> Content;
        public HashSet<int> Locked;
        public bool RejectRemove, RejectAddAt, RejectAddItem;
        public Action<Item> BeforeRemove, AfterRemove;
        public Action<Item, int> BeforeAddAt, AfterAddAt;
        public int AddItemCalls;
        public Item GetItemAt(int slot) { return slot >= 0 && slot < Content.Count ? Content[slot] : null; }
        public bool IsIndexLocked(int slot) { return Locked != null && Locked.Contains(slot); }
        public bool RemoveItem(Item item)
        {
            if (BeforeRemove != null) BeforeRemove(item);
            if (RejectRemove) return false;
            int slot = Content.IndexOf(item); if (slot < 0) return false;
            Content[slot] = null; item.InInventory = null;
            if (AfterRemove != null) AfterRemove(item);
            return true;
        }
        public bool AddAt(Item item, int slot)
        {
            if (BeforeAddAt != null) BeforeAddAt(item, slot);
            if (RejectAddAt || GetItemAt(slot) != null || item.InInventory != null) return false;
            while (Content.Count <= slot) Content.Add(null);
            Content[slot] = item; item.InInventory = this;
            if (AfterAddAt != null) AfterAddAt(item, slot);
            return true;
        }
        public bool AddItem(Item item)
        {
            AddItemCalls++;
            if (RejectAddItem) return false;
            for (int slot = 0; slot < Content.Count; slot++)
                if (Content[slot] == null && !IsIndexLocked(slot)) return AddAt(item, slot);
            return false;
        }
    }
    public sealed class Item : Component
    {
        public int TypeID, Quality, Value, MaxStackCount;
        public bool IsBeingDestroyed, Sticky, CanBeSold, Stackable, UseDurability;
        public float Durability, MaxDurabilityWithLoss;
        public Inventory InInventory, Inventory;
        public List<object> Slots;
        public HashSet<string> Tags;
        public object PluggedIntoSlot;
        public UnityEngine.Object ActiveAgent;
        public Action<int> BeforeStackWrite, AfterStackWrite;
        public Action BeforeDrop, AfterDrop;
        public int DropCalls, DestroyCalls;
        private int stackCount;
        public int StackCount
        {
            get { return stackCount; }
            set
            {
                if (BeforeStackWrite != null) BeforeStackWrite(value);
                stackCount = value;
                if (AfterStackWrite != null) AfterStackWrite(value);
            }
        }
        public void Drop(CharacterMainControl player, bool drop) { PerformDrop(); }
        public void Drop(Vector3 position, bool drop, Vector3 direction, float speed) { PerformDrop(); }
        private void PerformDrop()
        {
            DropCalls++;
            if (BeforeDrop != null) BeforeDrop();
            ActiveAgent = new GameObject();
            if (AfterDrop != null) AfterDrop();
        }
        public void DestroyTree()
        {
            DestroyCalls++; IsBeingDestroyed = true; Object.Destroy(gameObject);
        }
    }
    public static class ItemAssetsCollection
    {
        public static Func<int, Item> Factory;
        public static Func<int, Item> PrefabLookup;
        public static Item InstantiateSync(int typeId) { return Factory != null ? Factory(typeId) : null; }
        public static Item GetPrefab(int typeId) { return PrefabLookup != null ? PrefabLookup(typeId) : null; }
    }
}
namespace Duckov.Utilities
{
    public sealed class ItemWishlist
    {
        public static ItemWishlist Instance;
        public HashSet<int> Wanted;
        public bool IsManuallyWishlisted(int typeId) { return Wanted != null && Wanted.Contains(typeId); }
    }
}
namespace Duckov.ItemUsage { public sealed class Drug : UnityEngine.Component { public float healValue; } }
public sealed class ItemSetting_Bullet : UnityEngine.Component { }
public sealed class ItemSetting_Skill : UnityEngine.Component
{
    public enum OnReleaseAction { none, reduceCount }
    public OnReleaseAction onRelease;
    public object Skill;
}
public sealed class Skill_Grenade { public bool createExplosion; public DamageInfo damageInfo; }
public struct DamageInfo { public float damageValue; }
public sealed class CharacterMainControl : UnityEngine.Component { public ItemStatsSystem.Item CharacterItem; }
namespace BossRush
{
    public static class ModBehaviour { public static void DevLog(string message) { } }
    public static class BossRushItemIds { public const int BossRushTicket = 500001; }
    internal static class SkyIslandInventoryTransaction
    {
        // 边界替身：只记录库存/插槽/地面代理所有权，不替代投喂事务。
        internal static bool HasOwner(ItemStatsSystem.Item item)
        {
            return item != null && (item.InInventory != null || item.PluggedIntoSlot != null || item.ActiveAgent != null);
        }
        internal static void DestroyUnowned(ItemStatsSystem.Item item)
        {
            if (item != null && !item.IsBeingDestroyed && !HasOwner(item)) item.DestroyTree();
        }
    }
}
