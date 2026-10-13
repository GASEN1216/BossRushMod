using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace UnityEngine
{
    public struct Vector3
    {
        public float sqrMagnitude { get { return 1; } }
        public Vector3 normalized { get { return this; } }
        public static Vector3 forward, up;
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float f) { return a; }
    }
    public class Transform { public Vector3 forward, position; }
    public static class Mathf { public static int Clamp(int n, int min, int max) { return Math.Max(min, Math.Min(n, max)); } }
}
namespace ItemStatsSystem
{
    public sealed class Variable
    {
        public string Key;
        public float Value;
        public bool Display;
        public float GetFloat() { return Value; }
    }
    public sealed class Variables : List<Variable>
    {
        public Variable GetEntry(string key) { return Find(x => x.Key == key); }
    }
    public sealed partial class Item
    {
        public Variables Variables = new Variables();
        public string DisplayName { get { return "item"; } }
        public bool FloatThrowBefore, FloatThrowAfter;
        public static bool DropReject, DropThrowAfter;
        public object ActiveAgent { get { return AgentUtilities == null ? null : AgentUtilities.ActiveAgent; } }
        public void SetFloat(string key, float value, bool create)
        {
            if (FloatThrowBefore) throw new Exception("before variable commit");
            Variable entry = Variables.GetEntry(key);
            if (entry == null) { entry = new Variable { Key = key }; Variables.Add(entry); }
            entry.Value = value;
            if (FloatThrowAfter) throw new Exception("after variable commit");
        }
        public object Drop(UnityEngine.Vector3 pos, bool active, UnityEngine.Vector3 dir, float speed)
        {
            if (DropReject) return null;
            AgentUtilities = new AgentUtilities { ActiveAgent = new object() };
            if (DropThrowAfter) throw new Exception("after drop receipt");
            return ActiveAgent;
        }
    }
    public static class InventoryExtensions
    {
        public static bool AddAndMerge(this Inventory inventory, Item item, int slot)
        {
            // Official ordering: target count first, incoming count second, then insert remainder.
            foreach (Item target in inventory.Content.ToArray())
                if (target != null && target.TypeID == item.TypeID && target.StackCount < target.MaxStackCount)
                { target.Combine(item); if (item == null || item.StackCount <= 0) return true; }
            return inventory.AddItem(item);
        }
    }
}
namespace BossRush
{
    public class PointerEventData { }
    public static class TemperingSolventConfig { public const int TYPE_ID = 500108; }
    public static class ColdQuenchFluidConfig { public const int TYPE_ID = 500014; }
    public static class L10n { public static string T(string cn, string en) { return en; } }
    public static class NotificationText
    {
        public static bool Throw;
        public static void Push(string text) { if (Throw) throw new Exception("banner"); }
    }
    public static class ReforgeUIManager
    {
        private static Row pending;
        public static bool IsPropertyLockPending(Row row) { return pending == row; }
        public static void BeginPropertyLockPending(Row row) { pending = row; }
        public static void CancelPendingPropertyLock() { pending = null; }
        public static void PlayPropertyLockedFeedback(Row row) { }
        public static void NotifyPropertyLocked() { }
    }
    public partial class Row
    {
        public Item TargetItem;
        public string PropertyKey = "damage";
        public PropertyType PropType = PropertyType.Stat;
        public bool IsLocked = true, CanLock = true, CanUnlock = true;
        private bool applyingPropertyChange;
        private void ApplyVisualState() { }
    }
    public partial class GoblinNPCController
    {
        private bool rewardDeliveryInProgress;
        public UnityEngine.Transform transform = new UnityEngine.Transform();
        private static void EnsureRewardItemLoaded(int id) { }
    }
}
