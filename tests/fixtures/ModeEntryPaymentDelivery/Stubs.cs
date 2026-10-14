using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ItemStatsSystem;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return this == o as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value) { if (!ReferenceEquals(value, null)) value.Destroyed = true; }
        public static void DestroyImmediate(Object value) { Destroy(value); }
    }
    public struct Vector3
    {
        public static Vector3 up, forward;
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float b) { return a; }
    }
    public class Transform { public Vector3 position; }
    public static class Random { public static float value; }
    public static class Debug { public static void LogWarning(object text) { } public static void LogError(object text) { } }
    public static class Application { public static bool isPlaying = true; }
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static int Min(int a, int b) { return Math.Min(a, b); } }
}
namespace Saves { public static class SavesSystem { public static int CurrentSlot = 1; } }
public sealed class DuckovItemAgent : UnityEngine.Object { }
public static class SceneLoader { public static bool IsSceneLoading; }
public sealed class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main;
    public readonly UnityEngine.Transform transform = new UnityEngine.Transform();
    public Item CharacterItem;
}
public static class PlayerStorage
{
    public static readonly List<ItemStatsSystem.Data.ItemTreeData> IncomingItemBuffer = new List<ItemStatsSystem.Data.ItemTreeData>();
}
namespace ItemStatsSystem.Data { public sealed class ItemTreeData { public int rootInstanceID, RootTypeID; } }
namespace ItemStatsSystem
{
    public sealed class AgentUtilities { public DuckovItemAgent ActiveAgent; }
    public sealed class Slot { public Item Master; }
    public sealed partial class Item : UnityEngine.Object
    {
        public int TypeID, MaxStackCount = 1;
        public bool IsCharacter, RejectPlug, ThrowAfterPlug;
        public Inventory Inventory, InInventory;
        public Slot PluggedIntoSlot;
        public AgentUtilities AgentUtilities;
        public string DisplayName = "fixture item";
        public bool IsBeingDestroyed { get { return Destroyed; } }
        public bool Stackable { get { return MaxStackCount > 1; } }
        public DuckovItemAgent ActiveAgent { get { return AgentUtilities == null ? null : AgentUtilities.ActiveAgent; } }
        private int count = 1;
        public Action<Item> OnCount;
        public int StackCount
        {
            get { return Stackable ? count : 1; }
            set { count = Math.Min(value, MaxStackCount); if (OnCount != null) OnCount(this); if (count < 1) DestroyTree(); }
        }
        public Item GetCharacterItem()
        {
            if (IsCharacter) return this;
            if (PluggedIntoSlot != null) return PluggedIntoSlot.Master.GetCharacterItem();
            if (InInventory != null && InInventory.Master != null) return InInventory.Master.GetCharacterItem();
            return null;
        }
        public bool TryPlug(Item weapon, bool a, object b, int c)
        {
            if (RejectPlug) return false;
            weapon.PluggedIntoSlot = new Slot { Master = this };
            if (ThrowAfterPlug) throw new InvalidOperationException("slot notification after commit");
            return true;
        }
        public void DestroyTree() { Detach(); UnityEngine.Object.Destroy(this); }
        public void Detach() { if (InInventory != null) InInventory.RemoveItem(this); PluggedIntoSlot = null; }
        public void SetInt(string key, int value, bool create) { count = value; }
        public DuckovItemAgent Drop(UnityEngine.Vector3 position, bool a, UnityEngine.Vector3 direction, float b)
        {
            if (Probe.RejectDrop) return null;
            Detach(); AgentUtilities = new AgentUtilities { ActiveAgent = new DuckovItemAgent() };
            Probe.Dropped += StackCount;
            if (Probe.ThrowAfterDrop) throw new InvalidOperationException("drop notification after commit");
            return ActiveAgent;
        }
        public void Drop(CharacterMainControl player, bool random) { Drop(player.transform.position, random, UnityEngine.Vector3.forward, 0f); }
    }
    public sealed class Inventory : IEnumerable<Item>
    {
        public readonly List<Item> Content = new List<Item>();
        public Item Master;
        public int Capacity = 8;
        public bool Reject;
        public Action<Item> AfterAdd;
        public Item GetItemAt(int at) { return at >= 0 && at < Content.Count ? Content[at] : null; }
        public int GetFirstEmptyPosition(int start) { for (int i = start; i < Capacity; i++) if (GetItemAt(i) == null) return i; return -1; }
        public bool AddItem(Item item) { int at = GetFirstEmptyPosition(0); return at >= 0 && AddAt(item, at); }
        public bool AddAt(Item item, int at)
        {
            if (Reject || item == null || item.InInventory != null || item.PluggedIntoSlot != null || at >= Capacity) return false;
            while (Content.Count <= at) Content.Add(null);
            Content[at] = item; item.InInventory = this;
            if (AfterAdd != null) AfterAdd(item);
            return true;
        }
        public bool RemoveItem(Item item)
        { int at = Content.IndexOf(item); if (at < 0) return false; Content[at] = null; item.InInventory = null; return true; }
        public IEnumerator<Item> GetEnumerator() { return Content.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
    public static class ItemAssetsCollection
    {
        public static Item Last;
        public static Item InstantiateSync(int id)
        { Probe.Created++; return Probe.FailCreate ? null : (Last = new Item { TypeID = id, MaxStackCount = id == 500001 ? Probe.TicketMaxStack : 1 }); }
    }
}
public static partial class ItemUtilities
{
    public static bool SendToPlayerCharacterInventory(Item item, bool combine)
    { return CharacterMainControl.Main != null && CharacterMainControl.Main.CharacterItem.Inventory.AddItem(item); }
    public static void SendToPlayer(Item item, bool drop, bool combine)
    { if (!SendToPlayerCharacterInventory(item, combine) && drop) item.Drop(CharacterMainControl.Main, true); }
}
namespace Duckov.Economy
{
    public struct Cost
    {
        public long money;
        public ItemEntry[] items;
        public bool Enough { get { return true; } }
        public struct ItemEntry { public int id; public long amount; }
    }
}
namespace Duckov.UI
{
    public sealed class MapSelectionEntry
    {
        public bool ConditionsSatisfied = true;
        public Duckov.Economy.Cost Cost;
        public BossRush.BossRushMapEntryClickHandler Marker;
        public T GetComponent<T>() where T : class { return Marker as T; }
    }
}
namespace BossRush
{
    internal static class BossRushCoop { internal static string Asset = "asset"; internal static string LocalAssetId { get { return Asset + "-" + Saves.SavesSystem.CurrentSlot; } } }
    internal struct SkyIslandIngredient { internal int TypeId, Count; }
    internal enum BossRushPendingEntryKind { None, ModeG, ModeH }
    public enum ModeHAvailabilityStatus { Available, Unavailable }
    internal static class ModeHConfig { internal const string LocalizationKeyPrefix = "H_"; }
    internal sealed class ModeHRuntimeModule { internal bool IsSceneEntryPending; }
    internal sealed class ModeHSupportedMap { internal string SceneName = "arena", SceneId = "main"; }
    internal static class ModeHMapSupportRegistry
    {
        internal static bool TryGetPrimaryMap(out ModeHSupportedMap map) { map = new ModeHSupportedMap(); return true; }
        internal static bool TryGetMap(string name, out ModeHSupportedMap map) { map = new ModeHSupportedMap { SceneName = name }; return true; }
    }
    internal static class ModeHPresentationAssetCache { internal static bool TryPreflight() { return true; } }
    internal static class ModeHRuntimeGates
    {
        public static bool IsModeHRiskScanReady = true, IsModeHContentReady = true;
        public static bool IsModeHExternalAssetRiskBlocked, IsModeHRecoveryOnlyBlocked, IsModeHRunOwnerActive;
        public static bool IsRecoveryEntryAllowed() { return true; }
    }
    public sealed class ModBehaviour
    {
        internal static ModBehaviour Instance;
        internal bool Planned;
        internal int Opens, DirectStarts;
        internal ModeHRuntimeModule ModeHRuntime;
        internal static void DevLog(string text) { }
        internal static void CriticalLog(string text) { }
        internal void ShowMessage(string text) { if (Probe.ThrowMessage) throw new Exception("message notification"); }
        internal bool IsModeHConfiguredEnabled() { return true; }
        internal bool HasLegacyModeConflictForModeH(out string reason) { reason = null; return false; }
        internal void OpenModeHMapSelection() { Opens++; BossRushMapSelectionHelper.MarkEntryFlowFromMapSelectionUi(BossRushPendingEntryKind.ModeH); Planned = true; }
        internal void StartBossRush(object x) { DirectStarts++; }
        internal static BossRushMapConfig[] GetAllMapConfigs() { return new[] { new BossRushMapConfig { sceneName = "arena", sceneID = "main" } }; }
    }
    internal sealed class BossRushMapConfig { internal string sceneName, sceneID; }
    internal static class L10n { internal static string T(string key) { return key; } internal static string T(string a, string b) { return b; } }
    public sealed class BossRushMapEntryClickHandler { internal int entryIndex; }
    internal static partial class BossRushMapSelectionHelper
    {
        private static int pendingMapEntryIndex = -1, pendingModeHSceneGeneration, modeHSceneGenerationCounter;
        private static int pendingModeHSlot = -1;
        private static Duckov.Economy.Cost.ItemEntry[] pendingModeHCostItems;
        private static bool pendingInitialSpawn, pendingPrepaidTicketForCurrentEntry;
        private static BossRushEntryFlowSource pendingEntryFlowSource;
        private static BossRushPendingEntryKind pendingEntryKind;
        private static string pendingModeHTargetSceneName, pendingModeHTargetSceneId;
        internal static int GetBossRushTicketTypeId() { return 500001; }
        private static void SetBossRushArenaPlanned(bool value) { ModBehaviour.Instance.Planned = value; }
        internal static void CaptureInitialSpawnSelection(int index) { }
        internal static int SelectedIndex { get { return pendingMapEntryIndex; } }
        internal static void Fallback() { FallbackToDirectTeleport(); }
    }
    internal sealed partial class ModeDItemPool
    {
        private Item lastGivenWeapon;
        private readonly List<int> modeDWeaponPool = new List<int> { 101 };
        private readonly Func<bool> needsMeleeStarterKit = () => false;
        private int GetRandomItemByQuality(List<int> pool, int min, int max) { return pool[0]; }
        private void TryAddRandomAttachmentsFullRandom(Item item) { }
        private void EnsureStarterGunHasBulletType(Item item) { }
        private void FillGunMagazine(Item item) { }
        private void GiveStarterAmmo(CharacterMainControl player) { }
        private void GiveRandomArmor(CharacterMainControl player) { }
        private void GiveRandomHelmet(CharacterMainControl player) { }
        private void GiveRandomMeleeWeapon(CharacterMainControl player) { }
        private void GiveStarterMedical(CharacterMainControl player, int n) { }
        private void GiveRandomTotem(CharacterMainControl player) { }
        private void GiveRandomMask(CharacterMainControl player) { }
        private void GiveRandomBackpack(CharacterMainControl player) { }
    }
}
