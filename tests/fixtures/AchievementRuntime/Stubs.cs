using System;
using System.Collections.Generic;
using UnityEngine;

internal static class Probe
{
    internal static readonly List<string> Trace = new List<string>();
    internal static readonly List<string> Unlocks = new List<string>();
    internal static int HotkeyReads;
    internal static bool ThrowManagerInitialization;
}

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public string name;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            GameObject go = value as GameObject;
            if (go != null) foreach (Component component in go.Components) component.Destroyed = true;
            value.Destroyed = true;
        }
    }
    public class Component : Object { public GameObject gameObject; }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public GameObject(string name) { this.name = name; }
    }
    public enum KeyCode { L = 76, P = 80 }
    public static class Input
    {
        public static bool Pressed;
        public static KeyCode LastKey;
        public static bool GetKeyDown(KeyCode key) { LastKey = key; return Pressed; }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager
    {
        public static string Current = "Base_SceneV2";
        public static Scene GetActiveScene() { return new Scene { name = Current }; }
    }
}

namespace Duckov.UI { public class View { public static View ActiveView; } }

namespace ItemStatsSystem
{
    public sealed class Item { public int RawValue = 20; public int GetTotalRawValue() { return RawValue; } }
    public static class ItemAssetsCollection
    {
        public static Item Prefab;
        public static Item GetPrefab(int typeId) { return Prefab; }
    }
}

namespace Duckov.Economy
{
    public sealed class StockShopDatabase
    {
        public sealed class ItemEntry
        {
            public int typeID, maxStock;
            public bool forceUnlock, lockInDemo;
            public float priceFactor, possibility;
        }
    }
    public sealed class StockShop : UnityEngine.Object
    {
        public bool Eligible = true;
        public readonly List<Entry> entries = new List<Entry>();
        public sealed class Entry
        {
            public int ItemTypeID, CurrentStock;
            public bool Show;
            public readonly StockShopDatabase.ItemEntry Definition;
            public Entry(StockShopDatabase.ItemEntry definition) { Definition = definition; ItemTypeID = definition.typeID; }
        }
    }
}

namespace Saves
{
    public static class SavesSystem
    {
        private static Action collect, setFile;
        public static readonly Dictionary<string, int> Data = new Dictionary<string, int>();
        public static bool ThrowRead, ThrowWrite;
        public static event Action OnCollectSaveData
        {
            add { collect += value; Probe.Trace.Add("save-subscribe"); }
            remove { collect -= value; Probe.Trace.Add("save-unsubscribe"); }
        }
        public static event Action OnSetFile
        {
            add { setFile += value; Probe.Trace.Add("slot-subscribe"); }
            remove { setFile -= value; Probe.Trace.Add("slot-unsubscribe"); }
        }
        public static int CollectSubscribers { get { return collect == null ? 0 : collect.GetInvocationList().Length; } }
        public static int SlotSubscribers { get { return setFile == null ? 0 : setFile.GetInvocationList().Length; } }
        public static bool KeyExisits(string key) { if (ThrowRead) throw new InvalidOperationException(); return Data.ContainsKey(key); }
        public static T Load<T>(string key) { Probe.Trace.Add("load:" + key); return (T)(object)Data[key]; }
        public static void Save<T>(string key, T value)
        {
            Probe.Trace.Add("save:" + key);
            if (ThrowWrite) throw new InvalidOperationException();
            Data[key] = (int)(object)value;
        }
        public static void Collect() { if (collect != null) collect(); }
        public static void SwitchSlot() { if (setFile != null) setFile(); }
    }
}

namespace BossRush
{
    public abstract class BossRushRuntimeModuleBase
    {
        public virtual string ModuleName { get { return string.Empty; } }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnUpdate(float deltaTime, float unscaledDeltaTime) { }
        public virtual void OnDestroy() { }
    }
    public sealed class HealthChangeEvent
    {
        private Action<Health> listeners;
        public int Count { get { return listeners == null ? 0 : listeners.GetInvocationList().Length; } }
        public void AddListener(Action<Health> listener) { listeners += listener; Probe.Trace.Add("health-subscribe"); }
        public void RemoveListener(Action<Health> listener) { listeners -= listener; Probe.Trace.Add("health-unsubscribe"); }
        public void Invoke(Health health) { if (listeners != null) listeners(health); }
    }
    public sealed class Health : Component
    {
        private static Action<Health, DamageInfo> hurt;
        public float CurrentHealth = 100;
        public readonly HealthChangeEvent OnHealthChange = new HealthChangeEvent();
        public static event Action<Health, DamageInfo> OnHurt
        {
            add { hurt += value; Probe.Trace.Add("hurt-subscribe"); }
            remove { hurt -= value; Probe.Trace.Add("hurt-unsubscribe"); }
        }
        public static int HurtSubscribers { get { return hurt == null ? 0 : hurt.GetInvocationList().Length; } }
        public static void Hurt(Health health, float damage) { if (hurt != null) hurt(health, new DamageInfo { finalDamage = damage }); }
    }
    public struct DamageInfo { public float finalDamage; }
    public sealed class CharacterMainControl : Component
    {
        public static CharacterMainControl Main;
        public Health Health;
        public CharacterMainControl(string name)
        {
            this.name = name;
            gameObject = new GameObject(name);
            Health = new Health { gameObject = gameObject };
            gameObject.Components.Add(this);
            gameObject.Components.Add(Health);
        }
    }
    public sealed class InteractablePickup
    {
        private static Action<InteractablePickup, CharacterMainControl> pickup;
        public static event Action<InteractablePickup, CharacterMainControl> OnPickupSuccess
        {
            add { pickup += value; Probe.Trace.Add("pickup-subscribe"); }
            remove { pickup -= value; Probe.Trace.Add("pickup-unsubscribe"); }
        }
        public static int Subscribers { get { return pickup == null ? 0 : pickup.GetInvocationList().Length; } }
        public static void Pick(CharacterMainControl player) { if (pickup != null) pickup(new InteractablePickup(), player); }
    }
    public static class LevelManager
    {
        private static Action initialized;
        public static event Action OnAfterLevelInitialized
        {
            add { initialized += value; Probe.Trace.Add("level-subscribe"); }
            remove { initialized -= value; Probe.Trace.Add("level-unsubscribe"); }
        }
        public static int Subscribers { get { return initialized == null ? 0 : initialized.GetInvocationList().Length; } }
        public static void Rebind() { if (initialized != null) initialized(); }
    }
    public static class ModeGRuntimeGates
    {
        public static bool DamageWindow;
        public static bool IsModeGAchievementDamageWindowActive { get { return DamageWindow; } }
    }
    public static class AchievementTracker
    {
        public static bool HasTakenDamage, HasUsedHealItem, HasPickedUpItem;
        public static int TotalBossKills, TotalDragonKingKills, TotalClears, MaxInfiniteHellWave;
        public static float ArenaEnterTime, Elapsed;
        public static void ResetSessionStats()
        {
            Probe.Trace.Add("session-reset");
            HasTakenDamage = HasUsedHealItem = HasPickedUpItem = false;
            ArenaEnterTime++;
        }
        public static float GetElapsedTime() { return Elapsed; }
        public static void OnClear() { TotalClears++; }
        public static void OnInfiniteHellWaveComplete(int wave) { MaxInfiniteHellWave = Math.Max(MaxInfiniteHellWave, wave); }
        public static void OnBossKilled(string type) { TotalBossKills++; if (type == "DragonKing") TotalDragonKingKills++; }
        public static void OnPlayerTakeDamage(float damage) { HasTakenDamage = true; }
        public static void OnPlayerPickupItem() { HasPickedUpItem = true; }
        public static void OnPlayerUseHealItem() { HasUsedHealItem = true; }
    }
    public sealed class BossRushAchievementDef { }
    public struct BossRushAchievementUnlockedEvent { public BossRushAchievementDef Achievement; }
    public static class BossRushAchievementManager
    {
        public static void Initialize()
        {
            Probe.Trace.Add("manager-initialize");
            if (Probe.ThrowManagerInitialization) throw new InvalidOperationException();
        }
        public static bool TryUnlock(string key) { Probe.Unlocks.Add(key); return true; }
        public static void CheckCompletionistAchievement() { Probe.Trace.Add("completionist"); }
        public static void ResetStaticCaches() { Probe.Trace.Add("manager-reset"); }
    }
    public static class AchievementIconLoader { public static void ResetStaticCaches() { Probe.Trace.Add("icons-reset"); } }
    public sealed class AchievementView
    {
        public static AchievementView Instance;
        public static void EnsureInstance() { Probe.Trace.Add("view-ensure"); if (Instance == null) Instance = new AchievementView(); }
        public static void Shutdown() { Probe.Trace.Add("view-shutdown"); Instance = null; }
        public void Toggle() { Probe.Trace.Add("toggle"); }
    }
    public static class SteamAchievementPopup
    {
        public static void EnsureInstance() { Probe.Trace.Add("popup-ensure"); }
        public static void Shutdown() { Probe.Trace.Add("popup-shutdown"); }
        public static void Show(BossRushAchievementDef achievement) { Probe.Trace.Add("popup-show"); }
    }
    public static class BossRushEventBus
    {
        private static readonly List<Delegate> subscribers = new List<Delegate>();
        public static int Count { get { return subscribers.Count; } }
        public static void Subscribe<T>(Action<T> action) { subscribers.Add(action); Probe.Trace.Add("bus-subscribe"); }
        public static void Unsubscribe<T>(Action<T> action) { subscribers.Remove(action); Probe.Trace.Add("bus-unsubscribe"); }
        public static void Publish(BossRushAchievementUnlockedEvent value)
        {
            foreach (Delegate subscriber in subscribers.ToArray()) ((Action<BossRushAchievementUnlockedEvent>)subscriber)(value);
        }
    }
    public static class SafeRuntime { public static void Run(string name, Action action) { try { action(); } catch { } } }
    public static partial class AchievementMedalConfig { public static void InjectLocalization() { Probe.Trace.Add("medal-localization"); } }
    public static class ObjectCache { public static Duckov.Economy.StockShop[] Shops; public static Duckov.Economy.StockShop[] GetStockShops() { return Shops; } }
    internal sealed class ModeDRuntimeModule { internal bool IsActive; }
    internal sealed class WavesArenaRuntimeModule { internal bool InfiniteHellMode; internal int BossesPerWave; }
    internal sealed class FixtureConfig
    {
        internal int Hotkey;
        internal int achievementHotkey { get { Probe.HotkeyReads++; return Hotkey; } }
    }
    internal sealed class FixtureModuleHost
    {
        internal AchievementRuntimeModule Registered;
        internal void Register(AchievementRuntimeModule runtime) { Registered = runtime; Probe.Trace.Add("register"); }
    }
    public partial class ModBehaviour : UnityEngine.Object
    {
        internal AchievementRuntimeModule achievementRuntime;
        internal ModeDRuntimeModule modeDRuntime;
        internal WavesArenaRuntimeModule wavesArenaRuntime;
        internal FixtureConfig config;
        internal FixtureModuleHost runtimeModuleHost;
        internal bool IsActive;
        private const string BaseSceneName = "Base_SceneV2";
        internal bool IsBaseHubNormalMerchantShop(Duckov.Economy.StockShop shop) { return shop != null && shop.Eligible; }
        public static void DevLog(string message) { }
        internal void BeginSessionForFixture() { BeginAchievementSession("fixture"); }
        internal bool KillForFixture(CharacterMainControl boss) { return CheckBossKillAchievementsOnce(boss); }
        internal void ClearForFixture() { CheckClearAchievements(); }
        internal void HellForFixture(int wave) { CheckInfiniteHellAchievements(wave); }
        internal void InjectMedalsForFixture(string scene = null) { InjectAchievementMedalIntoShops(scene); }
    }
}
