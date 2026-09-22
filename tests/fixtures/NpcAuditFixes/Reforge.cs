using System;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem;
namespace UnityEngine { }
namespace ItemStatsSystem
{
    public class Property { public string Key; public bool Display = true; }
    public class Item
    {
        public string DisplayName = "test";
        public List<Property> Modifiers = new List<Property>(), Stats = new List<Property>(), Variables = new List<Property>();
        public HashSet<string> Locked = new HashSet<string>();
    }
}
namespace Duckov.UI { public static class NotificationText { public static void Push(string text) { } } }
namespace Duckov.Economy
{
    public class Cost { public long Amount; public Cost(long amount) { Amount = amount; } }
    public static class EconomyManager
    {
        public static long Money;
        public static long Cash { get { return 0; } }
        public static bool ThrowAfterPay, ThrowAfterAdd;
        public static bool Pay(Cost cost, bool a, bool b) { if (Money < cost.Amount) return false; Money -= cost.Amount; if (ThrowAfterPay) throw new Exception("payment observer"); return true; }
        public static void Add(long amount) { Money += amount; if (ThrowAfterAdd) throw new Exception("refund observer"); }
    }
}
namespace BossRush
{
    public enum PropertyType { Modifier, Stat, Variable }
    public static class PropertyLockSystem { public static bool IsPropertyLocked(Item item, string key, PropertyType type) { return item.Locked.Contains(type + ":" + key); } }
    public class ReforgeResult { public bool Success, HasAppliedChanges; public string ErrorMessage; }
    public static partial class ReforgeSystem
    {
        public static ReforgeResult NextResult;
        public static int Calls;
        private static Item GetItemPrefab(Item item) { return item; }
        private static bool IsModifierEligibleForReforge(Item item, Item prefab, Property property) { return property.Display; }
        private static bool IsStatEligibleForReforge(Property property) { return property.Display; }
        private static bool IsVariableEligibleForReforge(Property property) { return property.Display; }
        public static ReforgeResult Reforge(Item item, int money, string user, float tendency) { Calls++; return NextResult; }
    }
    public class ModBehaviour
    {
        public static ModBehaviour Instance = new ModBehaviour();
        public static bool UsesPurification;
        public static long Purification;
        public static void DevLog(string s) { }
        public bool IsZombieModeTemporaryRealNpc(object npc) { return UsesPurification; }
        public bool TrySpendZombieModePurificationPointsForRealNpc(object npc, int fee, string reason) { if (Purification < fee) return false; Purification -= fee; return true; }
        public void RefundZombieModePurificationPointsForRealNpc(object npc, int fee, bool refund) { if (refund) Purification += fee; }
        public void StartCoroutine(object routine) { }
    }
    public class BossRushAudioManager { public static BossRushAudioManager Instance = new BossRushAudioManager(); public void PlayReforgeSFX() { } }
    public static class L10n { public static string T(string cn, string en) { return en; } }
    public static partial class ReforgeUIManager
    {
        private static bool isReforgeMode = true, isReforging;
        private static Item selectedItem;
        private static int currentMoney = 40;
        private static float currentTendencyChance = 0.5f;
        private static object currentController = new object();
        private static bool AffixForge_HandleButtonClick() { return false; }
        private static int GetTendencyCost() { return 10; }
        private static long GetPlayerMoney() { return ModBehaviour.UsesPurification ? ModBehaviour.Purification : Duckov.Economy.EconomyManager.Money; }
        private static void UpdateUIStateKeepSlider(long money) { }
        private static object RefreshUIAfterReforgeDelayed() { return null; }
        private static void ShowPropertyChanges() { }
        public static void Click(Item item) { selectedItem = item; OnReforgeButtonClick(); }
    }
}
class Program
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Reset(bool points) { ModBehaviour.UsesPurification = points; ModBehaviour.Purification = Duckov.Economy.EconomyManager.Money = 1000; ReforgeSystem.Calls = 0; }
    static void Main()
    {
        foreach (bool points in new[] { false, true })
        {
            foreach (PropertyType type in new[] { PropertyType.Modifier, PropertyType.Stat, PropertyType.Variable })
            {
                Reset(points); var item = new Item(); var list = type == PropertyType.Modifier ? item.Modifiers : type == PropertyType.Stat ? item.Stats : item.Variables;
                list.Add(new Property { Key = "power" }); item.Locked.Add(type + ":power"); ReforgeUIManager.Click(item);
                Check(ReforgeSystem.Calls == 0 && ModBehaviour.Purification == 1000 && Duckov.Economy.EconomyManager.Money == 1000, "all locked properties must be rejected before debit");
                item.Locked.Clear(); ReforgeSystem.NextResult = new ReforgeResult { Success = false }; ReforgeUIManager.Click(item);
                Check(ReforgeSystem.Calls == 1 && ModBehaviour.Purification == 1000 && Duckov.Economy.EconomyManager.Money == 1000, "unsuccessful result without applied changes refunds the charged currency");
                ReforgeSystem.NextResult = new ReforgeResult { Success = true }; ReforgeUIManager.Click(item);
                Check((points ? ModBehaviour.Purification : Duckov.Economy.EconomyManager.Money) == 950, "successful reforge charges investment plus polarity once");
            }
        }
        var partial = new Item(); partial.Stats.Add(new Property { Key = "one" }); partial.Stats.Add(new Property { Key = "two" }); partial.Locked.Add("Stat:one");
        Check(ReforgeSystem.CanExecuteReforge(partial), "partial lock retains unlocked property eligibility");
        Reset(false);
        ReforgeSystem.NextResult = new ReforgeResult { Success = false };
        Duckov.Economy.EconomyManager.ThrowAfterAdd = true;
        ReforgeUIManager.Click(partial);
        Duckov.Economy.EconomyManager.ThrowAfterAdd = false;
        ReforgeSystem.NextResult = new ReforgeResult { Success = true };
        ReforgeUIManager.Click(partial);
        Check(ReforgeSystem.Calls == 2 && Duckov.Economy.EconomyManager.Money == 950,
            "refund observer failure still releases busy and next click works");
        Reset(false);
        Duckov.Economy.EconomyManager.ThrowAfterPay = true;
        ReforgeUIManager.Click(partial);
        Duckov.Economy.EconomyManager.ThrowAfterPay = false;
        Check(ReforgeSystem.Calls == 0 && Duckov.Economy.EconomyManager.Money == 1000,
            "payment observer failure refunds actual debit before any reforge");
        Console.WriteLine("Reforge " + checks + " PASS");
    }
}
