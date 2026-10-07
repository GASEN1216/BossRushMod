using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Mathf
    {
        public static bool Approximately(float a, float b) { return Math.Abs(a - b) < 0.00001f; }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static int Clamp(int x, int a, int b) { return Math.Max(a, Math.Min(b, x)); }
    }
}
namespace ItemStatsSystem
{
    public sealed class Stat
    {
        public float BaseValue;
        public float EquipmentBonus;
        private readonly List<Stats.Modifier> modifiers = new List<Stats.Modifier>();
        public float Value
        {
            get { float value = BaseValue + EquipmentBonus; foreach (var m in modifiers) value *= 1 + m.Value; return value; }
        }
        public void RemoveAllModifiersFromSource(object source) { modifiers.RemoveAll(m => m.Source == source); }
        public void AddModifier(Stats.Modifier modifier) { modifiers.Add(modifier); }
    }
    public sealed class Item
    {
        private readonly Dictionary<int, Stat> stats = new Dictionary<int, Stat>();
        public Item()
        {
            stats["MaxHealth".GetHashCode()] = new Stat { BaseValue = 1000f };
            stats["GunDamageMultiplier".GetHashCode()] = new Stat { BaseValue = 2f };
            stats["MeleeDamageMultiplier".GetHashCode()] = new Stat { BaseValue = 3f };
        }
        public Stat GetStat(int key) { return stats[key]; }
        public Stat GetStat(string key) { return GetStat(key.GetHashCode()); }
    }
}
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { PercentageMultiply }
    public sealed class Modifier
    {
        public readonly float Value;
        public readonly object Source;
        public Modifier(ModifierType type, float value, bool overrideOrder, int order, object source)
        { Value = value; Source = source; }
        public Modifier(ModifierType type, float value, object source) { Value = value; Source = source; }
    }
}
public sealed class AICharacterController { public float baseReactionTime = 2, reactionTime = 2, shootDelay = 4; }
public sealed class CharacterMainControl
{
    public readonly ItemStatsSystem.Item CharacterItem = new ItemStatsSystem.Item();
    public readonly AICharacterController AI = new AICharacterController();
    public readonly Health Health;
    public CharacterMainControl() { Health = new Health(this); }
    public T GetComponentInChildren<T>() where T : class { return AI as T; }
}
public sealed class Health
{
    private readonly CharacterMainControl owner;
    public float CurrentHealth = 1000;
    public float MaxHealth { get { return owner.CharacterItem.GetStat("MaxHealth".GetHashCode()).Value; } }
    public Health(CharacterMainControl owner) { this.owner = owner; }
    public void SetHealth(float value) { CurrentHealth = Math.Min(MaxHealth, value); }
}
namespace BossRush
{
    internal partial class ModeDRuntimeModule
    {
        internal void Scale(CharacterMainControl character, int wave) { ApplyModeDWaveScaling(character, wave); }
    }
    internal partial class DragonKingAbilityController
    {
        internal void ReduceChild(CharacterMainControl character) { ApplyDescendantStatReduction(character); }
    }
    public partial class ModBehaviour
    {
        private BossRushConfig config = new BossRushConfig();
        public static void DevLog(string value) { }
        internal void Configure(int percent, float global = 1) { config.bossHealthPercent = percent; config.bossStatMultiplier = global; }
        internal void Apply(CharacterMainControl c, bool boss = true, float? explicitMultiplier = null)
        { ApplyBossStatMultiplier(c, explicitMultiplier, boss); }
    }
    internal static class Program
    {
        private static int checks;
        private static void Equal(float actual, float expected, string label)
        { checks++; if (Math.Abs(actual - expected) > 0.001f) throw new Exception(label + ": " + actual + " != " + expected); }
        private static void Main()
        {
            var owner = new ModBehaviour();
            Equal(owner.GetBossHealthMultiplier(), 1, "new configuration preserves existing health");
            foreach (int percent in new[] { 10, 100, 200, -100, 1000 })
            {
                owner.Configure(percent);
                var boss = new CharacterMainControl(); owner.Apply(boss);
                Equal(boss.Health.MaxHealth, 1000 * owner.GetBossHealthMultiplier(), "configured boss health");
                Equal(boss.Health.CurrentHealth, boss.Health.MaxHealth, "spawns at full adjusted health");
                Equal(boss.CharacterItem.GetStat("GunDamageMultiplier".GetHashCode()).BaseValue, 2, "gun damage unchanged");
                Equal(boss.CharacterItem.GetStat("MeleeDamageMultiplier".GetHashCode()).BaseValue, 3, "melee damage unchanged");
                Equal(boss.AI.reactionTime, 2, "AI reaction unchanged");
                var minion = new CharacterMainControl(); owner.Apply(minion, false);
                Equal(minion.Health.MaxHealth, 1000, "ordinary enemies excluded");
            }
            owner.Configure(50, 2);
            var scaled = new CharacterMainControl(); owner.Apply(scaled);
            Equal(scaled.Health.MaxHealth, 1000, "composes with legacy global multiplier");
            Equal(scaled.CharacterItem.GetStat("GunDamageMultiplier".GetHashCode()).BaseValue, 4, "legacy damage scaling preserved");
            owner.Apply(scaled, true, 3);
            Equal(scaled.Health.MaxHealth, 3000, "campaign explicit scaling does not apply health percentage twice");
            owner.Configure(50);
            var equipped = new CharacterMainControl(); owner.Apply(equipped);
            equipped.CharacterItem.GetStat("MaxHealth".GetHashCode()).EquipmentBonus = 200;
            Equal(equipped.Health.MaxHealth, 600, "equipment applied later follows the overall health ratio");
            owner.Apply(equipped);
            Equal(equipped.Health.MaxHealth, 600, "health-only repeat application does not compound");
            foreach (int percent in new[] { 50, 100, 200 })
            {
                owner.Configure(percent);
                var waveBoss = new CharacterMainControl(); owner.Apply(waveBoss);
                new ModeDRuntimeModule().Scale(waveBoss, 2);
                Equal(waveBoss.Health.MaxHealth, 1030 * percent / 100f, "Mode D wave increment scales exactly once");
                Equal(waveBoss.Health.CurrentHealth, waveBoss.Health.MaxHealth, "Mode D current health matches its real limit");
            }
            foreach (int percent in new[] { 10, 50, 100, 200 })
            {
                owner.Configure(percent);
                var child = new CharacterMainControl(); owner.Apply(child);
                new DragonKingAbilityController().ReduceChild(child);
                Equal(child.Health.MaxHealth, 1000 * percent / 100f * DragonKingConfig.ChildProtectionDescendantStatMultiplier,
                    "dragon king child spawn preserves the configured health ratio after its stat reduction");
                Equal(child.Health.CurrentHealth, child.Health.MaxHealth,
                    "new child spawns at full final health when configured to 200 percent");
            }
            Console.WriteLine("PASS: " + checks + " production-linked checks");
        }
    }
}
