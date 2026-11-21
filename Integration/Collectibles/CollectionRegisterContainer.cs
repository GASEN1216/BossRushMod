// ============================================================================
// CollectionRegisterContainer.cs - 收藏册「登记」用的单格容器（官方搜刮界面）
// ============================================================================
// 照 NPC 送礼的单格容器（Integration/Affinity/Services/NPCGiftContainerService.cs）：
//   - 打开只有一格的本地库存（官方 LootView），玩家放进一件收集品；
//   - 容器栏里克隆官方「整理」按钮当「登记」按钮：格子里是还没登记过的收集品才点得动，按钮字写明为什么点不动；
//   - 点了才登记：收走这一件，格子空出来，可以接着放下一件；
//   - 关上界面、开始读条、换存档槽、模块卸载时，格子里没登记的那件退回背包（放不下进仓库），关上不再自动登记。
// 本地库存不挂在 LootBoxInventoriesParent 下，开箱掉落判定天然不碰它。
// 事实先写存档再收物品：存档写不进去时这一件留在格子里，关上时退回。
// 按钮挂在官方 LootView 的层级里（LootView 常驻），关容器时必须一并销毁，否则下一个普通箱子会带着它。
// 打开走官方 OnStartLoot：逐个订阅者调用、互相隔离（别的 Mod 抛异常不能拦住排在后面的 LootView），
// 调完 LootView 还没开就直接设目标箱子再开；还是打不开、超时或按钮没建出来，都告诉玩家并回到收藏册，不静默收场。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class CollectionRegisterContainer
    {
        internal const string TitleKey = "BossRush_CollectionBook_RegisterTitle";
        private const int Capacity = 1;
        /// <summary>等 LootView 把容器栏摆好再克隆按钮（口径同送礼的 0.15 秒）。</summary>
        private const float ButtonDelay = 0.15f;
        /// <summary>这么久 LootView 还没打开，就当打不开：退回物品、收工。</summary>
        private const float OpenTimeout = 3f;

        private static FieldInfo inventoryReferenceField;
        private static FieldInfo displayNameKeyField;
        private static FieldInfo onStartLootField;
        private static FieldInfo targetDisplayField;
        private static FieldInfo sortButtonField;
        private static FieldInfo targetLootBoxField;
        private static bool reflectionReady;

        private readonly Func<List<Item>, List<Item>> register;
        private readonly Func<int, bool> isRegistered;
        private readonly Action<bool> onClosed;
        private readonly Action<string> onFailed;
        private GameObject root;
        private Inventory inventory;
        private InteractableLootbox lootbox;
        private GameObject buttonObject;
        private Button button;
        private float openedAt;
        private bool subscribed;
        private bool registeredAny;

        /// <param name="onRegister">交给收藏册：传入格子里的那一件，返回已登记、应当收走的那些（写存档失败返回 null）。</param>
        /// <param name="isRegisteredTypeId">这件收集品是否已经登记过（决定按钮能不能点）。</param>
        /// <param name="onClosedByPlayer">玩家自己关上界面时回调，参数是这一趟有没有登记成功过。</param>
        /// <param name="onOpenFailed">登记格子没能打开（打不开、超时、按钮没建出来）时回调，参数是给玩家看的提示。</param>
        internal CollectionRegisterContainer(Func<List<Item>, List<Item>> onRegister, Func<int, bool> isRegisteredTypeId,
            Action<bool> onClosedByPlayer, Action<string> onOpenFailed)
        {
            register = onRegister;
            isRegistered = isRegisteredTypeId;
            onClosed = onClosedByPlayer;
            onFailed = onOpenFailed;
        }

        internal bool IsOpen { get { return root != null; } }

        internal static void InjectLocalization()
        {
            LocalizationHelper.InjectLocalization(TitleKey, L10n.T("收藏册登记", "Album Registry"));
        }

        internal bool Open()
        {
            if (root != null) return false;
            try
            {
                EnsureReflection();
                if (LootView.Instance == null || inventoryReferenceField == null)
                {
                    Debug.LogWarning(CollectibleTuning.LogPrefix + "登记容器打不开：" + (LootView.Instance == null ? "LootView 不在" : "缺 inventoryReference 字段"));
                    NotifyOpenFailed();
                    return false;
                }
                InjectLocalization();
                root = new GameObject("BossRush_CollectionRegister");
                root.SetActive(false);
                // 放进的物品挂在这个库存下：根对象不跟基地场景一起卸载，切图时由 OnStartedLoadingScene 先退回再关。
                UnityEngine.Object.DontDestroyOnLoad(root);
                inventory = root.AddComponent<Inventory>();
                inventory.SetCapacity(Capacity);
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(0.1f, 0.1f, 0.1f);
                collider.enabled = false;
                lootbox = root.AddComponent<InteractableLootbox>();
                BossRush.Utils.NPCInteractionGroupHelper.GetOrCreateGroupList(lootbox, CollectibleTuning.LogPrefix);
                lootbox.enabled = false;
                lootbox.needInspect = false;
                inventoryReferenceField.SetValue(lootbox, inventory);
                if (displayNameKeyField != null) displayNameKeyField.SetValue(lootbox, TitleKey);
                root.SetActive(true);
                registeredAny = false;
                openedAt = Time.unscaledTime;
                inventory.onContentChanged += OnContentChanged;
                InteractableLootbox.OnStopLoot += OnStopLoot;
                SceneLoader.onStartedLoadingScene += OnStartedLoadingScene;
                subscribed = true;
                Action<InteractableLootbox> start = onStartLootField != null ? onStartLootField.GetValue(null) as Action<InteractableLootbox> : null;
                if (start != null) RaiseStartLootIsolated(start);
                if (root == null) { NotifyOpenFailed(); return false; }
                if (!IsShowing(LootView.Instance) && !OpenLootViewDirectly(LootView.Instance))
                {
                    Debug.LogWarning(CollectibleTuning.LogPrefix + "登记容器打不开：OnStartLoot 之后 LootView 仍未打开");
                    Fail();
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(CollectibleTuning.LogPrefix + "打开登记容器失败: " + e.Message);
                Fail();
                return false;
            }
        }

        /// <summary>
        /// 逐个调用官方 OnStartLoot 的订阅者，一个抛异常不拦后面的。
        /// 官方多播链里谁先抛，排在后面的就收不到；LootView 在关卡 UI 醒来时才订阅，总排在 Mod 的订阅者后面，
        /// 前面任何一个订阅者（多半是别的 Mod）对这个没有掉落表的单格箱子抛异常，登记格子就开不出来（2026-11-21 玩家反馈）。
        /// </summary>
        private void RaiseStartLootIsolated(Action<InteractableLootbox> start)
        {
            Delegate[] handlers = start.GetInvocationList();
            for (int i = 0; i < handlers.Length && root != null; i++)
            {
                Action<InteractableLootbox> handler = handlers[i] as Action<InteractableLootbox>;
                if (handler == null) continue;
                try { handler(lootbox); }
                catch (Exception e)
                {
                    string who = handler.Method != null && handler.Method.DeclaringType != null
                        ? handler.Method.DeclaringType.FullName + "." + handler.Method.Name : "?";
                    Debug.LogWarning(CollectibleTuning.LogPrefix + "开箱事件订阅者对登记格子抛异常，已跳过: " + who + " " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        private bool IsShowing(LootView view)
        {
            return view != null && view.open && inventory != null && view.TargetInventory == inventory;
        }

        /// <summary>OnStartLoot 没把 LootView 打开时，直接把目标箱子设成登记格子再开（送礼容器同一条后备路）。</summary>
        private bool OpenLootViewDirectly(LootView view)
        {
            if (view == null || targetLootBoxField == null || lootbox == null) return false;
            try
            {
                targetLootBoxField.SetValue(view, lootbox);
                view.Open(null);
            }
            catch (Exception e) { Debug.LogWarning(CollectibleTuning.LogPrefix + "直接打开 LootView 失败: " + e.Message); }
            return IsShowing(view);
        }

        /// <summary>登记格子开不出来：退回格子里的东西、收工，再告诉玩家（不静默收场）。</summary>
        private void Fail()
        {
            CloseAndReturn(false);
            NotifyOpenFailed();
        }

        private void NotifyOpenFailed()
        {
            if (onFailed == null) return;
            try { onFailed(L10n.T("登记格子没打开，过一会儿再点一次「登记收集品」。", "The register slot didn't open. Try \"Register Items\" again in a moment.")); }
            catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "登记失败提示回调异常: " + e.Message); }
        }

        /// <summary>每帧由模块调用；容器没开时 O(1) 早返。LootView 摆好后克隆一次「登记」按钮。</summary>
        internal void Tick()
        {
            if (root == null || buttonObject != null) return;
            LootView view = LootView.Instance;
            if (!IsShowing(view))
            {
                if (Time.unscaledTime - openedAt > OpenTimeout)
                {
                    Debug.LogWarning(CollectibleTuning.LogPrefix + "登记容器 " + OpenTimeout + " 秒内没在 LootView 里出现，关闭");
                    Fail();
                }
                return;
            }
            if (Time.unscaledTime - openedAt < ButtonDelay) return;
            if (!CreateButton(view))
            {
                // 没有按钮就没法登记：不退回到「关上就登记」，退回物品、关掉界面，留日志查原因。
                Debug.LogWarning(CollectibleTuning.LogPrefix + "登记按钮创建失败，关闭登记容器");
                Fail();
            }
        }

        // ---------------- 按钮 ----------------

        /// <summary>克隆容器栏的官方「整理」按钮，盖在它原来的位置上（送礼同一手法）。</summary>
        private bool CreateButton(LootView view)
        {
            try
            {
                InventoryDisplay display = targetDisplayField != null ? targetDisplayField.GetValue(view) as InventoryDisplay : null;
                Button sort = display != null && sortButtonField != null ? sortButtonField.GetValue(display) as Button : null;
                if (sort == null) return false;
                buttonObject = UnityEngine.Object.Instantiate(sort.gameObject, sort.transform.parent);
                buttonObject.name = "BossRush_CollectionRegisterButton";
                RectTransform rect = buttonObject.GetComponent<RectTransform>();
                RectTransform sortRect = sort.GetComponent<RectTransform>();
                if (rect != null && sortRect != null)
                {
                    rect.anchorMin = sortRect.anchorMin;
                    rect.anchorMax = sortRect.anchorMax;
                    rect.pivot = sortRect.pivot;
                    rect.anchoredPosition = sortRect.anchoredPosition;
                    rect.sizeDelta = sortRect.sizeDelta;
                }
                buttonObject.SetActive(true);
                button = buttonObject.GetComponent<Button>();
                if (button == null) { DestroyButton(); return false; }
                // 克隆不带官方运行时挂的「整理」回调；预制体里若有持久回调也关掉，免得点「登记」顺手整理。
                button.onClick.RemoveAllListeners();
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                button.onClick.AddListener(OnRegisterClicked);
                TextMeshProUGUI label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    // 官方按钮宽度是给「整理」两个字的，英文与五个字的状态要能缩进去。
                    float size = label.fontSize;
                    label.enableAutoSizing = true;
                    label.fontSizeMax = size;
                    label.fontSizeMin = Mathf.Min(size, 12f);
                }
                RefreshButton();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(CollectibleTuning.LogPrefix + "克隆登记按钮失败: " + e.Message);
                DestroyButton();
                return false;
            }
        }

        private void OnContentChanged(Inventory changed, int index)
        {
            RefreshButton();
        }

        /// <summary>按钮字随格子里的东西走：空格、不是收集品、已经登记过都点不动，字里写明原因。</summary>
        private void RefreshButton()
        {
            if (button == null) return;
            try
            {
                Item item = FirstItem();
                string text;
                bool ready = false;
                if (item == null) text = L10n.T("放入收集品", "Place Item");
                else if (CollectibleCatalog.Get(item.TypeID) == null) text = L10n.T("不是收集品", "Not Collectible");
                else if (isRegistered != null && isRegistered(item.TypeID)) text = L10n.T("已登记过", "Already Logged");
                else { text = L10n.T("登记", "Register"); ready = true; }

                button.interactable = ready;
                if (ready) ZombieModeUIHelper.SetButtonBaseColor(button, BossRushUIColors.AccentFill);
                else ZombieModeUIHelper.ApplyButtonColors(button, BossRushUIColors.Disabled, BossRushUIColors.Disabled, BossRushUIColors.Disabled);
                TextMeshProUGUI label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) label.text = text;
                else
                {
                    Text legacy = buttonObject.GetComponentInChildren<Text>(true);
                    if (legacy != null) legacy.text = text;
                }
            }
            catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "刷新登记按钮失败: " + e.Message); }
        }

        private void OnRegisterClicked()
        {
            Item item = FirstItem();
            if (item == null || CollectibleCatalog.Get(item.TypeID) == null || (isRegistered != null && isRegistered(item.TypeID)))
            {
                RefreshButton();
                return;
            }
            List<Item> taken = null;
            try { taken = register != null ? register(new List<Item> { item }) : null; }
            catch (Exception e) { Debug.LogWarning(CollectibleTuning.LogPrefix + "登记失败，物品留在格子里: " + e.Message); taken = null; }
            if (taken != null && taken.Contains(item))
            {
                registeredAny = true;
                try
                {
                    item.Detach();
                    item.DestroyTree();
                }
                catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "收走已登记物品失败: " + e.Message); }
            }
            RefreshButton();
        }

        // ---------------- 关闭 ----------------

        private void OnStopLoot(InteractableLootbox box)
        {
            if (box == null || box != lootbox) return;
            CloseAndReturn(true);
        }

        /// <summary>界面开着时开始读条（回主菜单、换图）：趁基地和主角都还在，先退回，免得物品跟着场景走。</summary>
        private void OnStartedLoadingScene(SceneLoadingContext context)
        {
            CloseAndReturn(false);
        }

        /// <summary>
        /// 关上：格子里没登记的退回背包（放不下进仓库），再清理。场景切换、换槽或模块卸载时也走这里，物品不会丢。
        /// <paramref name="byPlayer"/> 为真表示玩家自己关的界面，才回调 owner（决定要不要回到收藏册）。
        /// </summary>
        internal void CloseAndReturn(bool byPlayer)
        {
            if (root == null) return;
            var items = new List<Item>();
            try
            {
                if (inventory != null)
                {
                    foreach (Item item in inventory)
                    {
                        if (item != null) items.Add(item);
                    }
                }
            }
            catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "读取登记容器失败: " + e.Message); }

            foreach (Item item in items)
            {
                try
                {
                    item.Detach();
                    ReturnToPlayer(item);
                }
                catch (Exception e) { Debug.LogWarning(CollectibleTuning.LogPrefix + "退回物品失败: " + e.Message); }
            }
            bool any = registeredAny;
            Close();
            if (byPlayer && onClosed != null)
            {
                try { onClosed(any); }
                catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "登记容器关闭回调失败: " + e.Message); }
            }
        }

        private Item FirstItem()
        {
            if (inventory == null) return null;
            foreach (Item item in inventory)
            {
                if (item != null) return item;
            }
            return null;
        }

        private static void ReturnToPlayer(Item item)
        {
            if (item == null) return;
            if (ItemUtilities.SendToPlayerCharacterInventory(item, false)) return;
            ItemUtilities.SendToPlayerStorage(item, false);
        }

        private void DestroyButton()
        {
            if (buttonObject != null) UnityEngine.Object.Destroy(buttonObject);
            buttonObject = null;
            button = null;
        }

        private void Close()
        {
            if (subscribed)
            {
                if (inventory != null) inventory.onContentChanged -= OnContentChanged;
                InteractableLootbox.OnStopLoot -= OnStopLoot;
                SceneLoader.onStartedLoadingScene -= OnStartedLoadingScene;
                subscribed = false;
            }
            DestroyButton();
            if (root != null)
            {
                try
                {
                    if (LootView.Instance != null && LootView.Instance.open) LootView.Instance.Close();
                }
                catch (Exception) { /* 界面已关 */ }
                UnityEngine.Object.Destroy(root);
            }
            root = null;
            inventory = null;
            lootbox = null;
            registeredAny = false;
        }

        private static void EnsureReflection()
        {
            if (reflectionReady) return;
            reflectionReady = true;
            const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
            inventoryReferenceField = typeof(InteractableLootbox).GetField("inventoryReference", instance);
            displayNameKeyField = typeof(InteractableLootbox).GetField("displayNameKey", instance);
            onStartLootField = typeof(InteractableLootbox).GetField("OnStartLoot", BindingFlags.NonPublic | BindingFlags.Static);
            targetDisplayField = typeof(LootView).GetField("lootTargetInventoryDisplay", instance);
            sortButtonField = typeof(InventoryDisplay).GetField("sortButton", instance);
            targetLootBoxField = typeof(LootView).GetField("targetLootBox", instance);
        }
    }
}
