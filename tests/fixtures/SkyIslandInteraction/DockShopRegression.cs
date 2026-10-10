// 真实构造/清理接线、组装 helper、商店交互与归属判据均由 run.py 抽取。
// 此处替换 Unity 生命周期调度、好感数据与商店 UI，故证据只到 L2。
using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using UnityEngine;

internal interface IFixtureLifecycle
{
    void EnableForFixture();
    void DestroyForFixture();
}

internal partial class InteractableBase
{
    internal bool interactableGroup, MarkerActive = true;
    // 不赋默认值，生产 helper 必须先初始化才能把成员挂入官方交互组。
    private List<InteractableBase> otherInterablesInGroup;
    internal List<InteractableBase> GroupForTest { get { return otherInterablesInGroup; } }
    protected virtual void OnDestroy() { }
}

namespace BossRush.Utils
{
    internal static class BossRushEagerReflectionCache
    {
        internal static readonly FieldInfo InteractableBase_OtherInterablesInGroup = typeof(InteractableBase)
            .GetField("otherInterablesInGroup", BindingFlags.Instance | BindingFlags.NonPublic);
    }
    internal static class NPCExceptionHandler
    {
        internal static void TryExecute(Action action, string label, bool log) { action(); }
    }
}

namespace BossRush
{
    internal interface INPCController { Transform NpcTransform { get; } }
    internal interface INPCShopConfig { bool ShopEnabled { get; } int ShopUnlockLevel { get; } }
    internal sealed class FixtureShopConfig : INPCShopConfig
    {
        public bool ShopEnabled { get; set; }
        public int ShopUnlockLevel { get; set; }
    }
    internal enum NPCShopPaymentStrategy { Cash }
    internal static class AffinityManager
    {
        internal static event Action<string, int, int> OnAffinityChanged;
        internal static event Action<string, int> OnLevelUp;
        internal static readonly Dictionary<string, INPCShopConfig> Configs = new Dictionary<string, INPCShopConfig>();
        internal static readonly Dictionary<string, int> Levels = new Dictionary<string, int>();
        internal static int ListenerCount
        {
            get { return (OnAffinityChanged == null ? 0 : OnAffinityChanged.GetInvocationList().Length)
                + (OnLevelUp == null ? 0 : OnLevelUp.GetInvocationList().Length); }
        }
        internal static object GetNPCConfig(string id)
        { INPCShopConfig value; return id != null && Configs.TryGetValue(id, out value) ? value : null; }
        internal static int GetLevel(string id) { int value; return Levels.TryGetValue(id, out value) ? value : 0; }
        internal static void Change(string id, int level)
        {
            int previous = GetLevel(id);
            Levels[id] = level;
            if (OnAffinityChanged != null) OnAffinityChanged(id, previous, level);
            if (OnLevelUp != null) OnLevelUp(id, level);
        }
        internal static void Reset()
        { Configs.Clear(); Levels.Clear(); OnAffinityChanged = null; OnLevelUp = null; }
    }

    internal abstract partial class NPCInteractableBase : InteractableBase
    {
        protected string npcId;
        protected bool isInitialized;
        protected INPCController npcController;
        protected abstract void SetupInteractName();
        protected abstract void DoInteract(CharacterMainControl character);
        protected virtual Vector3 GetDefaultInteractMarkerOffset() { return Vector3.zero; }
        protected virtual bool ShouldHideInteractMarker() { return true; }
        // 替身中没有父 NPC，也不给 ID 默认值；测试必须观察配置先于真实 Awake。
        private void TryGetNpcIdFromParent() { }
    }

    internal partial class NPCShopInteractable : IFixtureLifecycle
    {
        private bool fixtureAwake;
        internal string IdAtAwake;
        internal bool GroupedAtAwake, InactiveAtDestroy;
        internal int AwakeCalls, DestroyCalls;
        public void EnableForFixture()
        {
            if (!fixtureAwake)
            {
                fixtureAwake = true;
                AwakeCalls++;
                IdAtAwake = NpcId;
                InteractableBase owner = transform.parent.GetComponent<InteractableBase>();
                GroupedAtAwake = owner != null && owner.GroupForTest != null && owner.GroupForTest.Contains(this);
                Awake();
            }
            if (gameObject.activeInHierarchy) OnEnable();
        }
        public void DestroyForFixture()
        {
            DestroyCalls++;
            InactiveAtDestroy = !gameObject.activeSelf;
            OnDestroy();
        }
        internal bool InteractForTest()
        {
            if (!IsInteractable()) return false;
            DoInteract(null);
            return true;
        }
    }

    internal static partial class NPCShopSystem
    {
        private static bool isServiceActive;
        private static Transform currentNpcTransform;
        internal static string OpenedId;
        internal static int Opens, Closes;
        internal static bool ClosedBeforeAnchorDestroyed;
        internal static Transform CurrentForTest { get { return currentNpcTransform; } }
        internal static void OpenShop(string id, Transform owner, INPCController controller, NPCShopPaymentStrategy payment)
        {
            OpenedId = id; currentNpcTransform = owner; isServiceActive = true; Opens++;
        }
        internal static void CloseShop()
        {
            ClosedBeforeAnchorDestroyed = currentNpcTransform != null;
            currentNpcTransform = null; isServiceActive = false; Closes++;
        }
        internal static void Reset()
        { currentNpcTransform = null; isServiceActive = false; OpenedId = null; Opens = Closes = 0; ClosedBeforeAnchorDestroyed = false; }
    }
}

internal static class DockShopRegression
{
    private static InteractableBase Device(GameObject root)
    {
        var device = new GameObject("Search_A");
        device.transform.SetParent(root.transform, false);
        return device.AddComponent<InteractableBase>();
    }
    private static NPCShopInteractable Shop(InteractableBase device)
    {
        Transform child = device.transform.Find("FuzhouShopOption");
        return child == null ? null : child.GetComponent<NPCShopInteractable>();
    }
    private static void Reset()
    {
        AffinityManager.Reset(); NPCShopSystem.Reset();
        AffinityManager.Configs.Add("sky_fuzhou", new FixtureShopConfig { ShopEnabled = true, ShopUnlockLevel = 2 });
    }

    internal static void Run(Action<bool, string> check)
    {
        Reset();
        var absent = new SkyIslandWorldStory(new SkyIslandSession());
        absent.Dispose(); absent.Dispose();
        check(AffinityManager.ListenerCount == 0 && NPCShopSystem.Opens == 0,
            "missing dock device keeps story construction and disposal usable");

        var root = new GameObject("island");
        var device = Device(root);
        var group = BossRush.Utils.NPCInteractionGroupHelper.GetOrCreateGroupList(device, "test");
        var journal = new GameObject("journal").AddComponent<InteractableBase>();
        var sail = new GameObject("sail").AddComponent<InteractableBase>();
        group.Add(journal); group.Add(sail);
        var session = new SkyIslandSession { Device = device };
        var world = new SkyIslandWorldStory(session);
        NPCShopInteractable shop = Shop(device);
        check(shop != null && group.Count == 3 && group.Contains(shop),
            "production constructor adds a reachable dock shop beside existing interactions");
        check(shop.IdAtAwake == "sky_fuzhou" && shop.GroupedAtAwake && shop.AwakeCalls == 1,
            "dock NPC identity and group membership are ready before first Awake");
        check(!shop.gameObject.activeSelf && !shop.InteractForTest() && AffinityManager.ListenerCount == 2,
            "locked shop hides without losing affinity listeners or accepting interaction");
        AffinityManager.Change("other_npc", 3);
        check(!shop.gameObject.activeSelf && NPCShopSystem.Opens == 0, "other NPC affinity does not unlock Fuzhou shop");
        AffinityManager.Change("sky_fuzhou", 2);
        check(shop.gameObject.activeSelf && shop.InteractForTest() && NPCShopSystem.OpenedId == "sky_fuzhou"
            && ReferenceEquals(NPCShopSystem.CurrentForTest, device.transform),
            "Fuzhou affinity event reveals the dock option and opens the existing Fuzhou shop");
        world.AttachDockShopForTest(); world.AttachDockShopForTest();
        check(ReferenceEquals(Shop(device), shop) && group.Count == 3 && shop.AwakeCalls == 1
            && AffinityManager.ListenerCount == 2, "repeated attachment preserves one option and one listener pair");
        world.Dispose();
        check(shop == null && shop.DestroyCalls == 1 && shop.InactiveAtDestroy && group.Count == 2
            && ReferenceEquals(group[0], journal) && ReferenceEquals(group[1], sail),
            "production Dispose destroys its shop option and preserves unrelated group interactions");
        check(NPCShopSystem.Closes == 1 && NPCShopSystem.ClosedBeforeAnchorDestroyed && AffinityManager.ListenerCount == 0,
            "disposing current dock owner closes its shop before anchor destruction and unsubscribes listeners");
        world.Dispose(); world.AttachDockShopForTest();
        check(NPCShopSystem.Closes == 1 && shop.DestroyCalls == 1 && Shop(device) == null && group.Count == 2,
            "late cleanup and attachment cannot duplicate disposal or resurrect a disposed owner");
        UnityEngine.Object.Destroy(root);
        check(device == null && device.transform == null, "scene destruction cascades to dock components with Unity null semantics");
        world.Dispose();
        UnityEngine.Object.Destroy(journal.gameObject); UnityEngine.Object.Destroy(sail.gameObject);

        Reset();
        AffinityManager.Levels["sky_fuzhou"] = 2;
        var inactiveRoot = new GameObject("inactive-island");
        inactiveRoot.SetActive(false);
        device = Device(inactiveRoot);
        world = new SkyIslandWorldStory(new SkyIslandSession { Device = device });
        shop = Shop(device);
        check(shop != null && shop.AwakeCalls == 0 && AffinityManager.ListenerCount == 0,
            "inactive scene assembly defers Awake and listeners until activation");
        inactiveRoot.SetActive(true);
        check(shop.IdAtAwake == "sky_fuzhou" && shop.GroupedAtAwake && shop.InteractForTest(),
            "delayed Awake uses configured Fuzhou identity and opens the correct shop");
        var unrelated = new GameObject("other-shop-owner");
        NPCShopSystem.OpenShop("other_npc", unrelated.transform, null, NPCShopPaymentStrategy.Cash);
        world.Dispose();
        check(NPCShopSystem.Closes == 0 && ReferenceEquals(NPCShopSystem.CurrentForTest, unrelated.transform),
            "dock disposal leaves a different NPC shop open");
        UnityEngine.Object.Destroy(inactiveRoot); UnityEngine.Object.Destroy(unrelated);
        world.Dispose();

        Reset();
        root = new GameObject("destroyed-scene"); device = Device(root);
        world = new SkyIslandWorldStory(new SkyIslandSession { Device = device });
        group = device.GroupForTest; shop = Shop(device);
        UnityEngine.Object.Destroy(root);
        check(shop == null && shop.DestroyCalls == 1 && AffinityManager.ListenerCount == 0,
            "scene unload destroys hidden shop components and releases global listeners");
        world.Dispose(); world.Dispose();
        check(group.Count == 0 && shop.DestroyCalls == 1 && NPCShopSystem.Closes == 0,
            "cleanup after scene destruction removes stale group references once without reopening UI");
        Reset();
    }
}
