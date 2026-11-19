// ============================================================================
// CollectionRegisterContainer.cs - 收藏册「登记」用的临时容器（官方搜刮界面）
// ============================================================================
// 打开一个空容器（官方 LootView），玩家把收集品放进去，关上界面就登记：
//   - 还没登记过的收集品记入收藏册，物品收走；
//   - 已经登记过的、不是收集品的，退回背包，背包放不下进仓库。
// 容器形态照 Integration/Affinity/Services/NPCGiftContainerService.cs（本地库存 + 反射触发 OnStartLoot），
// 本地库存不挂在 LootBoxInventoriesParent 下，开箱掉落判定天然不碰它。
// 事实先写存档再收物品：存档写不进去时一件不收、全部退回。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed class CollectionRegisterContainer
    {
        internal const string TitleKey = "BossRush_CollectionBook_RegisterTitle";
        private const int Capacity = 24;

        private static FieldInfo inventoryReferenceField;
        private static FieldInfo displayNameKeyField;
        private static FieldInfo onStartLootField;
        private static bool reflectionReady;

        private readonly Func<List<Item>, List<Item>> register;
        private GameObject root;
        private Inventory inventory;
        private InteractableLootbox lootbox;
        private bool subscribed;

        /// <param name="onRegister">交给收藏册：传入容器里的全部物品，返回已登记、应当收走的那些。</param>
        internal CollectionRegisterContainer(Func<List<Item>, List<Item>> onRegister)
        {
            register = onRegister;
        }

        internal bool IsOpen { get { return root != null; } }

        internal static void InjectLocalization()
        {
            LocalizationHelper.InjectLocalization(TitleKey, L10n.T("收藏册登记：把收集品放进来，关上就登记", "Album registry: put collectibles in, close to register"));
        }

        internal bool Open()
        {
            if (root != null) return false;
            try
            {
                EnsureReflection();
                if (LootView.Instance == null || inventoryReferenceField == null) return false;
                InjectLocalization();
                root = new GameObject("BossRush_CollectionRegister");
                root.SetActive(false);
                // 放进的物品挂在这个库存下：根对象不跟基地场景一起卸载，切图时由 OnStartedLoadingScene 先登记、退回再关。
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
                InteractableLootbox.OnStopLoot += OnStopLoot;
                SceneLoader.onStartedLoadingScene += OnStartedLoadingScene;
                subscribed = true;
                Action<InteractableLootbox> start = onStartLootField != null ? onStartLootField.GetValue(null) as Action<InteractableLootbox> : null;
                if (start == null) { Close(); return false; }
                start(lootbox);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 打开登记容器失败: " + e.Message);
                Close();
                return false;
            }
        }

        private void OnStopLoot(InteractableLootbox box)
        {
            if (box == null || box != lootbox) return;
            Finish();
        }

        /// <summary>界面开着时开始读条（回主菜单、换图）：趁基地和主角都还在，先登记、退回，免得物品跟着场景走。</summary>
        private void OnStartedLoadingScene(SceneLoadingContext context)
        {
            Finish();
        }

        /// <summary>界面关上：登记、退回、清理。场景切换或模块卸载时也走这里，物品不会丢。</summary>
        internal void Finish()
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

            List<Item> taken = null;
            try { taken = items.Count > 0 && register != null ? register(items) : null; }
            catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "登记失败，全部退回: " + e.Message); taken = null; }

            foreach (Item item in items)
            {
                try
                {
                    item.Detach();
                    if (taken != null && taken.Contains(item)) item.DestroyTree();
                    else ReturnToPlayer(item);
                }
                catch (Exception e) { ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "退回物品失败: " + e.Message); }
            }
            Close();
        }

        private static void ReturnToPlayer(Item item)
        {
            if (item == null) return;
            if (ItemUtilities.SendToPlayerCharacterInventory(item, false)) return;
            ItemUtilities.SendToPlayerStorage(item, false);
        }

        private void Close()
        {
            if (subscribed)
            {
                InteractableLootbox.OnStopLoot -= OnStopLoot;
                SceneLoader.onStartedLoadingScene -= OnStartedLoadingScene;
                subscribed = false;
            }
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
        }

        private static void EnsureReflection()
        {
            if (reflectionReady) return;
            reflectionReady = true;
            const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
            inventoryReferenceField = typeof(InteractableLootbox).GetField("inventoryReference", instance);
            displayNameKeyField = typeof(InteractableLootbox).GetField("displayNameKey", instance);
            onStartLootField = typeof(InteractableLootbox).GetField("OnStartLoot", BindingFlags.NonPublic | BindingFlags.Static);
        }
    }
}
