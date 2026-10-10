using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null)) return;
            obj.Destroyed = true;
            GameObject go = obj as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Component c in go.Components) c.Destroyed = true;
        }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an && bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        internal readonly List<Component> Components = new List<Component>();
        public int scene;
        public Transform transform;
        public GameObject() { transform = new Transform { gameObject = this }; All.Add(this); }
        public T Add<T>() where T : Component, new() { T c = new T { gameObject = this }; Components.Add(c); return c; }
        internal static void DestroyScene() { foreach (GameObject go in All) Destroy(go); All.Clear(); }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } }
    public class MonoBehaviour : Component { public bool isActiveAndEnabled = true; }
    public class Transform : Component { public Vector3 position; }
    public struct Vector3
    {
        public float x;
        public static float Distance(Vector3 a, Vector3 b) { return Math.Abs(a.x - b.x); }
    }
    public static class Debug { public static void LogWarning(string text) { } }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string name, Type[] args) { } }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Object { public Inventory Inventory; public bool Delivered; }
    public class Inventory
    {
        internal static bool DeliveryAllowed = true;
        internal static int NotificationFault;
        public readonly List<Item> Content = new List<Item>();
        public bool AddItem(Item item)
        {
            if (!DeliveryAllowed) return false;
            if (NotificationFault == 1) throw new Exception("before insertion");
            Content.Add(item);
            if (NotificationFault == 2) throw new Exception("after insertion before ownership notification");
            item.Delivered = true;
            CharacterMainControl.Main.Received++;
            if (NotificationFault == 3) throw new Exception("after ownership notification");
            return true;
        }
    }
    public static class ItemAssetsCollection
    {
        internal static bool Missing;
        public static int TryGetIDByName(string name, bool report) { return name == "SeaShell" ? 101 : -1; }
        public static Item InstantiateSync(int id) { return Missing ? null : new Item(); }
    }
}
namespace ItemStatsSystem.Items { public class Slot { } }
namespace Duckov.Utilities
{
    public class SceneLoadingContext { }
    public static class SceneLoader
    {
        public static bool IsSceneLoading;
        public static event Action<SceneLoadingContext> onStartedLoadingScene;
        internal static void Start() { IsSceneLoading = true; onStartedLoadingScene?.Invoke(new SceneLoadingContext()); }
        internal static int Subscribers { get { return onStartedLoadingScene?.GetInvocationList().Length ?? 0; } }
    }
}
public class DuckovItemAgent : UnityEngine.Component { }
public class ItemSetting_Gun
{
    public enum ReloadModes { fullMag, singleBullet }
    public ReloadModes reloadMode;
}
public struct Progress { public float current, total; public bool inProgress; }
public class ItemAgent_Gun : DuckovItemAgent
{
    public CharacterMainControl Holder;
    public float ReloadTime = 2f, Timer;
    public int BulletCount = 3;
    public bool Reloading;
    public ItemSetting_Gun GunItemSetting = new ItemSetting_Gun();
    public bool BeginReload() { return false; }
    public void UpdateStates() { }
    public void CancleReload() { }
    public bool IsReloading() { return Reloading; }
    public Progress GetReloadProgress() { return new Progress { current = Timer, total = ReloadTime, inProgress = Reloading }; }
}
public class CharacterMainControl : UnityEngine.Component
{
    public static CharacterMainControl Main;
    public bool IsMainCharacter = true;
    public Health Health = new Health { IsMainCharacterHealth = true };
    public DuckovItemAgent CurrentHoldItemAgent;
    public ItemStatsSystem.Item CharacterItem = new ItemStatsSystem.Item { Inventory = new ItemStatsSystem.Inventory() };
    public int Shells, Received, InventoryAmmo = 20;
    public string Message;
    public bool TryToReload(ItemStatsSystem.Item preferedBulletToLoad = null) { return false; }
    public void PopText(string text, float seconds) { Message = text; }
    public static event Action<CharacterMainControl, DuckovItemAgent> OnMainCharacterChangeHoldItemAgentEvent;
    public static event Action<CharacterMainControl, ItemStatsSystem.Items.Slot> OnMainCharacterSlotContentChangedEvent;
    internal static void HoldChanged() { OnMainCharacterChangeHoldItemAgentEvent?.Invoke(Main, Main.CurrentHoldItemAgent); }
    internal static void SlotChanged() { OnMainCharacterSlotContentChangedEvent?.Invoke(Main, new ItemStatsSystem.Items.Slot()); }
    internal static int Subscribers { get { return (OnMainCharacterChangeHoldItemAgentEvent?.GetInvocationList().Length ?? 0) + (OnMainCharacterSlotContentChangedEvent?.GetInvocationList().Length ?? 0); } }
}
public class DamageInfo { }
public class Health
{
    public bool IsDead, IsMainCharacterHealth;
    public static event Action<Health, DamageInfo> OnDead;
    internal void Die() { IsDead = true; OnDead?.Invoke(this, new DamageInfo()); }
}
public class LevelManager
{
    public static LevelManager Instance;
    public static event Action OnLevelBeginInitializing, OnAfterLevelInitialized;
    internal static void Begin() { OnLevelBeginInitializing?.Invoke(); }
    internal static void End() { OnAfterLevelInitialized?.Invoke(); }
}
public class AimMarker : UnityEngine.Component { public UnityEngine.Transform aimMarkerUI; }
namespace BossRush
{
    internal static class NewWeaponEquipState
    {
        internal static bool Equipped = true;
        internal static int Queries;
        internal static void MarkDirty() { }
        internal static bool IsTotemEquipped(int id) { Queries++; return Equipped && id == BossRushItemIds.ReloadMetronome; }
    }
    internal static class BossRushUI
    {
        internal static bool Paused, Hidden;
        internal static bool IsGamePaused() { return Paused; }
        internal static bool IsOfficialHudHidden() { return Hidden; }
    }
    internal static class L10n { internal static string T(string cn, string en) { return en; } }
    internal static class LocalizationHelper { internal static void InjectLocalization(string key, string text) { } }
    internal static class ModBehaviour { internal static void DevLog(string message) { } }
    internal sealed class ReloadMetronomeHud : UnityEngine.Component
    {
        internal static int Created;
        internal static ReloadMetronomeHud Last;
        internal UnityEngine.Transform Parent;
        internal bool Finished;
        internal ReloadMetronomeResult ShownResult;
        internal void SetResult(ReloadMetronomeResult result) { ShownResult = result; }
        internal void FinishReload() { Finished = true; }
        internal static ReloadMetronomeHud Create(UnityEngine.Transform parent)
        { Created++; Last = new UnityEngine.GameObject().Add<ReloadMetronomeHud>(); Last.Parent = parent; return Last; }
    }
    internal class NPCShopInteractable : UnityEngine.MonoBehaviour
    {
        protected string npcId, _overrideInteractNameKey, InteractName;
        protected bool overrideInteractName;
        protected virtual void SetupInteractName() { }
        protected virtual void DoInteract(CharacterMainControl c) { }
        internal void Open(CharacterMainControl c) { npcId = ReloadMetronomeConfig.VendorNpcId; DoInteract(c); }
    }
    internal static class NPCShopSystem
    {
        internal static int AffinityLevel = 5;
        internal static bool IsShopUnlocked(string id) { return id == ReloadMetronomeConfig.VendorNpcId && AffinityLevel >= 5; }
    }
    internal static class BossRushConfirmDialog
    {
        internal class Options { public string Title, Target, Body, ConfirmLabel, CancelLabel; public bool Danger; public UnityEngine.GameObject Anchor; public Action OnConfirm; }
        internal static Options Last;
        internal static void Show(Options o) { Last = o; }
    }
    internal static class SkyIslandShellLottery { internal const string ShellItemName = "SeaShell"; }
    internal struct SkyIslandIngredient { internal int Count; internal SkyIslandIngredient(int id, int count) { Count = count; } }
    internal sealed class SkyIslandInventoryTransaction : IDisposable
    {
        internal static Action OnReserve;
        private CharacterMainControl player;
        private int count;
        private bool committed;
        internal static bool TryReserve(CharacterMainControl player, SkyIslandIngredient[] inputs, out SkyIslandInventoryTransaction tx)
        {
            tx = null;
            if (player.Shells < inputs[0].Count) return false;
            player.Shells -= inputs[0].Count;
            tx = new SkyIslandInventoryTransaction { player = player, count = inputs[0].Count };
            OnReserve?.Invoke();
            return true;
        }
        internal static bool HasOwner(ItemStatsSystem.Item item) { return item != null && item.Delivered; }
        internal void Commit() { committed = true; }
        public void Dispose() { if (!committed) player.Shells += count; }
        internal static void DestroyUnowned(ItemStatsSystem.Item item) { if (item != null && !item.Delivered) UnityEngine.Object.Destroy(item); }
    }
}
