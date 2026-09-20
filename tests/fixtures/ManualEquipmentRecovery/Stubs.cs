using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace UnityEngine
{
    public class GameObject { }
    public class Coroutine { }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex; }
    public static class SceneManager
    {
        public static int Index = 3;
        public static Scene GetActiveScene() { return new Scene { buildIndex = Index }; }
    }
}
namespace ItemStatsSystem
{
    public class Item
    {
        public int TypeID, Quality, Value, MaxStackCount, StackCount;
        public string DisplayName = "fixture";
        public float MaxDurability, Durability, DurabilityLoss, SavedReforgeBonus;
        public int ConfigureCalls, RestoreCalls;
        public bool ReforgeRestored, HasSetting, HasAgent;
        public Stats Stats;
        public TagCollection Tags = new TagCollection();
        public T GetComponent<T>() where T : class
        {
            if (typeof(T) == typeof(ItemSetting_MeleeWeapon) && HasSetting) return new ItemSetting_MeleeWeapon() as T;
            if (typeof(T) == typeof(ItemAgent_MeleeWeapon) && HasAgent) return new ItemAgent_MeleeWeapon() as T;
            return null;
        }
    }
    public class Stat { public float BaseValue; }
    public class Stats
    {
        public readonly Stat Damage = new Stat();
        public Stat GetStat(string key) { return Damage; }
    }
}
public class ItemSetting_Gun { }
public class ItemSetting_MeleeWeapon { }
public class ItemAgent_MeleeWeapon { }
public class DuckovItemAgent { }
public class TagCollection : HashSet<string> { }
public class Health { public bool IsDead; }
public class CharacterMainControl
{
    public static CharacterMainControl Main;
    public Health Health = new Health();
}
namespace BossRush
{
    enum AffixEquipMask { None, Gun, Melee, Armor, Helmet, FaceMask }
    static partial class AffixItemData
    {
        public static bool IsAffixEligible(Item item) { return GetEquipMask(item) != AffixEquipMask.None; }
    }
    static class ModBehaviour
    {
        public static bool RuntimeActive = true;
        public static bool CanRunGameplayRuntimeCached() { return RuntimeActive; }
        public static void DevLog(string text) { }
    }
    static class EquipmentHelper { public static void AddRepairableTag(Item item) { item.Tags.Add("Repairable"); } }
    static class ReforgeDataPersistence
    {
        public static void UnmarkRestored(Item item) { item.ReforgeRestored = false; }
        public static bool TryRestoreReforgeData(Item item)
        {
            if (item.ReforgeRestored || item.Stats == null) return false;
            item.Stats.Damage.BaseValue += item.SavedReforgeBonus;
            item.ReforgeRestored = true;
            item.RestoreCalls++;
            return true;
        }
    }
    static class ConfigAdapter
    {
        // Simulate the real configurators replacing base stats and filling wear.
        // Public item profile/IDs are linked from the full production files.
        public static bool Configure(Item item, string name, string expected)
        {
            if (name != expected) throw new Exception("wrong production registration base name");
            item.ConfigureCalls++;
            item.HasSetting = item.HasAgent = true;
            item.Stats = new Stats();
            item.Stats.Damage.BaseValue = 100;
            NewWeaponItemAttributes.Apply(item, item.TypeID);
            return true;
        }
    }
    static class ViperDaggerWeaponConfig
    { public static bool TryConfigure(Item item, string name) { return ConfigAdapter.Configure(item, name, "ViperDagger"); } }
    static class FrostSpearWeaponConfig
    { public static bool TryConfigure(Item item, string name) { return ConfigAdapter.Configure(item, name, "FrostSpear"); } }
    static class SummonStaffWeaponConfig
    { public static bool TryConfigure(Item item, string name) { return ConfigAdapter.Configure(item, name, "SummonStaff"); } }
    static class SummonStaffConfig { public const float TotalActionDuration = 1.2f; }
    class ActionBase
    {
        public bool Running, isActiveAndEnabled = true;
        protected float actionElapsedTime;
        protected virtual bool OnAbilityStart() { return true; }
        protected virtual void OnAbilityUpdate(float dt) { }
        protected virtual void OnAbilityStop() { }
        public void StopAction() { if (!Running) return; Running = false; OnAbilityStop(); }
    }
    sealed class PendingSpawn { public void Forget() { } }
    partial class SummonStaffAction : ActionBase
    {
        private int activeSpawnRequestId;
        private bool spawningStarted, spawningComplete, keepPendingSpawnOnStop;
        public int Requests;
        private PendingSpawn SpawnAlliesAsync(int id) { Requests++; return new PendingSpawn(); }
        public int Start() { actionElapsedTime = 0f; Running = OnAbilityStart(); OnAbilityUpdate(0f); return activeSpawnRequestId; }
        public void Advance(float elapsed) { actionElapsedTime = elapsed; if (Running) OnAbilityUpdate(0f); }
        public bool Valid(int request, CharacterMainControl player) { return IsRequestValid(request, player, 3); }
        public void Complete() { spawningComplete = true; Advance(0.1f); }
        public void Destroy() { isActiveAndEnabled = false; OnDestroy(); }
    }
    partial class SummonStaffManager
    {
        public static bool Holding = true;
        private SummonStaffAction abilityAction;
        private Coroutine preparation;
        private bool preparationComplete;
        private void StopCoroutine(Coroutine routine) { }
        public SummonStaffManager(SummonStaffAction action) { abilityAction = action; }
        internal static bool IsHoldingSummonStaff(CharacterMainControl player) { return Holding; }
        public void HoldChanged(CharacterMainControl player) { OnHoldItemChanged(player, null); }
    }
}
