using System;
using System.Collections.Generic;

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
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            go.Destroyed = true;
            foreach (Object component in go.Components) component.Destroyed = true;
        }
    }
    public class MonoBehaviour : Object { }
    public sealed class GameObject : Object
    {
        public string name;
        public readonly List<Object> Components = new List<Object>();
    }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction<T>(T value);
    public sealed class UnityEvent<T>
    {
        private readonly List<UnityAction<T>> listeners = new List<UnityAction<T>>();
        public int Count { get { return listeners.Count; } }
        public void AddListener(UnityAction<T> listener) { listeners.Add(listener); }
        public void RemoveListener(UnityAction<T> listener) { listeners.Remove(listener); }
        public void Invoke(T value) { foreach (var listener in listeners.ToArray()) listener(value); }
    }
}
namespace BossRush
{
    public enum Teams { player, wolf, bear }
    public struct DamageInfo { public int Id; }
    public sealed class Health : UnityEngine.Object
    {
        public readonly UnityEngine.Events.UnityEvent<DamageInfo> OnDeadEvent = new UnityEngine.Events.UnityEvent<DamageInfo>();
    }
    public sealed class CharacterMainControl : UnityEngine.MonoBehaviour
    {
        public readonly UnityEngine.GameObject gameObject;
        public Health Health;
        public Teams Team;
        public bool dropBoxOnDead = true;
        private Action<DamageInfo> loot;
        public int LootListeners { get { return loot == null ? 0 : loot.GetInvocationList().Length; } }
        public event Action<DamageInfo> BeforeCharacterSpawnLootOnDead
        { add { loot += value; } remove { loot -= value; } }
        public CharacterMainControl(string name)
        {
            gameObject = new UnityEngine.GameObject { name = name };
            Health = new Health();
            gameObject.Components.Add(this);
            gameObject.Components.Add(Health);
        }
        public T GetComponent<T>() where T : class
        {
            if (Destroyed) throw new InvalidOperationException("destroyed component lookup");
            return Health as T;
        }
        public void FireLoot() { if (loot != null) loot(new DamageInfo()); }
    }
    public static class ModBehaviour { public static void DevLog(string text) { } }
    internal sealed partial class ModeELootPolicy
    {
        private readonly ModeEFEnemyRegistry enemyRegistry;
        internal bool modeEActive;
        internal Teams modeEPlayerFaction;
        internal ModeELootPolicy(ModeEFEnemyRegistry registry) { enemyRegistry = registry; }
        private void UnregisterModeEEnemyLootHandler(CharacterMainControl enemy) { enemyRegistry.UnregisterModeEEnemyLootHandler(enemy); }
        internal void Register(CharacterMainControl enemy, Teams faction) { RegisterModeEEnemyLootHandler(enemy, faction); }
    }
}
