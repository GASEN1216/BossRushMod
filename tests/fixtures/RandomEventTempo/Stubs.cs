using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace UnityEngine
{
    public struct Vector3 { }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } }
}
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { Add, PercentageAdd }
    public class Modifier
    {
        public ModifierType Type; public float Value; public object Source;
        public Modifier(ModifierType type, float value, object source) { Type = type; Value = value; Source = source; }
    }
    public class Stat
    {
        public List<Modifier> Modifiers = new List<Modifier>();
        public void AddModifier(Modifier modifier) { Modifiers.Add(modifier); }
        public void RemoveModifier(Modifier modifier) { Modifiers.Remove(modifier); }
        public float Delta { get { float result = 0; foreach (Modifier m in Modifiers) result += m.Value; return result; } }
    }
}
namespace ItemStatsSystem
{
    public class Item
    {
        public Dictionary<string, Stat> Stats = new Dictionary<string, Stat>();
        public Stat GetStat(string key) { Stat stat; return Stats.TryGetValue(key, out stat) ? stat : null; }
    }
}
public class CharacterMainControl { public Item CharacterItem; }
namespace BossRush
{
    public class ZombieModeAttributeModifierRecord { public Item CharacterItem; public Stat Stat; public Modifier Modifier; public string StatName; }
    public static class ZombieModeStatNames
    {
        public const string WalkSpeed = "WalkSpeed", RunSpeed = "RunSpeed", GunDamageMultiplier = "GunDamageMultiplier", MeleeDamageMultiplier = "MeleeDamageMultiplier";
    }
    internal class RuntimeScope
    {
        readonly List<Action> cleanup = new List<Action>();
        public void RegisterCleanup(Action action) { cleanup.Add(action); }
        public void Clear(string reason) { foreach (Action action in cleanup) action(); cleanup.Clear(); }
    }
    internal class ModBehaviour
    {
        public List<CharacterMainControl> Enemies = new List<CharacterMainControl>();
        public static void DevLog(string message) { }
        public void ShowRandomEventBanner(string text) { }
        public void CollectEventBuffTargets(List<CharacterMainControl> targets) { targets.Clear(); targets.AddRange(Enemies); }
    }
    internal static class L10n { public static string T(string cn, string en) { return en; } }
    internal static class RandomEventEffectHelpers
    {
        public static CharacterMainControl Player;
        public static ModBehaviour ResolveOwner(RandomEventContext ctx) { return ctx == null ? null : ctx.Owner; }
        public static CharacterMainControl ResolveAlivePlayer() { return Player; }
        public static void ClearScope(RandomEventContext ctx, RandomEventEndReason reason) { if (ctx != null && ctx.Scope != null) ctx.Scope.Clear(reason.ToString()); }
    }
}
