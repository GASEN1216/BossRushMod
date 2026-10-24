using System;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

// Host adapters model stat identity/order/removal. ShootDamage is extracted from official code by run.py.
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { Add = 0, PercentageAdd = 100, PercentageMultiply = 200 }
    public sealed class Modifier
    {
        public readonly ModifierType Type;
        public readonly float Value;
        public readonly int Order;
        public Modifier(ModifierType type, float value, bool overrideOrder, int order, object source)
        { Type = type; Value = value; Order = overrideOrder ? order : (int)type; }
    }
}

namespace ItemStatsSystem
{
    public sealed class Stat
    {
        public float BaseValue;
        public readonly List<Modifier> Modifiers = new List<Modifier>();
        public Stat(float value) { BaseValue = value; }
        public void AddModifier(Modifier modifier) { Modifiers.Add(modifier); }
        public void RemoveModifier(Modifier modifier) { Modifiers.Remove(modifier); }
        public float Value
        {
            get
            {
                var sorted = new List<Modifier>(Modifiers);
                sorted.Sort((a, b) => a.Order.CompareTo(b.Order));
                float result = BaseValue;
                for (int i = 0; i < sorted.Count; i++)
                {
                    Modifier current = sorted[i];
                    if (current.Type == ModifierType.Add) result += current.Value;
                    else if (current.Type == ModifierType.PercentageMultiply) result *= Math.Max(0f, 1f + current.Value);
                    else
                    {
                        float sum = current.Value;
                        while (i + 1 < sorted.Count && sorted[i + 1].Order == current.Order && sorted[i + 1].Type == current.Type)
                            sum += sorted[++i].Value;
                        result *= Math.Max(0f, 1f + sum);
                    }
                }
                return result;
            }
        }
    }
    public sealed class StatCollection
    {
        private readonly Dictionary<string, Stat> values = new Dictionary<string, Stat>();
        public Stat GetStat(string key) { Stat value; return values.TryGetValue(key, out value) ? value : null; }
        public void Add(string key, float value) { values.Add(key, new Stat(value)); }
    }
    public sealed class Item : UnityEngine.Object
    {
        public readonly StatCollection Stats = new StatCollection();
        public readonly ItemSetting_Gun Gun = new ItemSetting_Gun();
        public T GetComponent<T>() where T : class { return Gun as T; }
    }
}

internal sealed partial class OfficialGunFixture
{
    internal Item Item;
    internal float BulletDamageMultiplier = 1f, CharacterDamageMultiplier = 1f, DamageFactorToZombie = 1f;
    internal float Damage { get { return Item.Stats.GetStat("Damage").Value; } }
    internal int ShotCount { get { return Math.Max(1, Mathf.RoundToInt(Item.Stats.GetStat("ShotCount").Value)); } }
    internal struct ProjectileContext { internal float damage, damageFactorToZombie; }
}

namespace BossRush
{
    internal static class ZombieModePhaseGuards { internal static bool IsRunActive(int phase) { return phase > 0; } }
}

internal static class SpreadRegression
{
    private static void Close(float actual, float expected, string label)
    {
        if (Math.Abs(actual - expected) > 0.0001f)
            throw new InvalidOperationException(label + ": expected " + expected + ", actual " + actual);
    }
    private static Item Gun(float damage, float count, float angle)
    {
        var item = new Item();
        item.Stats.Add("Damage", damage); item.Stats.Add("ShotCount", count); item.Stats.Add("ShotAngle", angle);
        return item;
    }
    internal static void Run()
    {
        var state = new ZombieModeRunState { RunId = 31, LifecyclePhase = 1 };
        var module = new ZombieModeRuntimeModule(state);
        var player = new CharacterMainControl(); CharacterMainControl.Main = player;
        Item rifle = Gun(100f, 1f, 0f);
        player.Hold(rifle);
        state.OptionRuntime.ProjectileTridentStacks = 1;
        module.RebuildSpread();
        var official = new OfficialGunFixture { Item = rifle };
        Close(official.ShotCount, 3f, "trident pellet count");
        Close(official.ShootDamage() * official.ShotCount, 100f, "official trident total damage must not be split twice");
        Close(rifle.Stats.GetStat("ShotAngle").Value, 8f, "trident angle");
        Close(module.ReloadTradeoff(ZombieModeRewardType.ProjectileTrident), 0.07f, "trident reload cost retained");

        state.OptionRuntime.ProjectileShotgunSprayStacks = 1;
        module.RebuildSpread();
        Close(official.ShotCount, 5f, "stacked spread uses five pellets");
        Close(official.ShootDamage(), 20f, "official spray per-pellet damage");
        Close(official.ShootDamage() * official.ShotCount, 100f, "official spray total damage retained");
        Close(module.GunTradeoff(ZombieModeRewardType.ProjectileShotgunSpray), 0.05f, "spray gun cost retained");
        official.CharacterDamageMultiplier = 1f - module.GunTradeoff(ZombieModeRewardType.ProjectileShotgunSpray);
        Close(official.ShootDamage() * official.ShotCount, 95f, "spray total includes only declared gun cost");

        var external = new Modifier(ModifierType.PercentageAdd, 0.25f, true, 100, new object());
        rifle.Stats.GetStat("Damage").AddModifier(external);
        module.RebuildSpread(); module.RebuildSpread();
        Close(official.ShootDamage() * official.ShotCount, 118.75f, "rebuild preserves external gun modifier and declared cost");
        Close(rifle.Stats.GetStat("ShotCount").BaseValue, 1f, "spread never edits persistent pellet base");

        Item shotgun = Gun(120f, 8f, 24f);
        player.Hold(shotgun);
        Close(rifle.Stats.GetStat("ShotCount").Value, 1f, "switch restores old rifle pellets");
        Close(rifle.Stats.GetStat("ShotAngle").Value, 0f, "switch restores old rifle angle");
        Close(rifle.Stats.GetStat("Damage").Value, 125f, "switch preserves external damage modifier");
        official.Item = shotgun; official.CharacterDamageMultiplier = 1f;
        Close(official.ShotCount, 8f, "native shotgun count preserved");
        Close(shotgun.Stats.GetStat("ShotAngle").Value, 24f, "native shotgun spread preserved");
        Close(official.ShootDamage() * official.ShotCount, 120f, "native shotgun total damage preserved");
        player.Hold(rifle); module.RemoveZombieModeOptionRuntimeEffects();
        Close(rifle.Stats.GetStat("ShotCount").Value, 1f, "cleanup restores rifle count");
        Close(rifle.Stats.GetStat("ShotAngle").Value, 0f, "cleanup restores rifle angle");
        Close(rifle.Stats.GetStat("Damage").Value, 125f, "cleanup retains unrelated modifiers");
        Close(module.SnapshotCount, 0f, "cleanup releases spread snapshots");

        // Preserve the official low-damage floor: it is not an extra mod damage loss.
        official.Item = Gun(2f, 5f, 18f);
        Close(official.ShootDamage(), 1f, "official low-damage floor remains intact");
        Console.WriteLine("SpreadRegression PASS: official split/clamp, trident/spray totals, costs, repeated rebuild, switch and cleanup");
    }
}
