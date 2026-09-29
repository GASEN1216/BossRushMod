using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 交付时收走的物品：整份预留在调用方手里，客户端交付事务成功才 <see cref="Commit"/>，否则 Dispose 原样归还。
    /// 预留与归还复用合成台同一套背包事务（<see cref="SkyIslandInventoryTransaction"/>），不另写第二套扣物品逻辑。
    /// </summary>
    internal sealed class OfficialQuestItemReservation : IDisposable
    {
        private SkyIslandInventoryTransaction transaction;

        internal OfficialQuestItemReservation(SkyIslandInventoryTransaction reserved) { transaction = reserved; }

        internal void Commit()
        {
            if (transaction != null) transaction.Commit();
        }

        public void Dispose()
        {
            if (transaction != null) transaction.Dispose();
            transaction = null;
        }
    }

    /// <summary>本次新生成奖品的可回滚投递。未合堆的原件或官方 Buffer 收据都能在同一调用栈内精确撤销。</summary>
    internal sealed class OfficialQuestRewardReservation : IDisposable
    {
        private sealed class Receipt
        {
            internal Item Item;
            internal int InstanceId, TypeId;
        }

        private readonly List<Receipt> receipts = new List<Receipt>();
        private bool committed;

        internal void Track(Item item)
        {
            receipts.Add(new Receipt { Item = item, InstanceId = item.GetInstanceID(), TypeId = item.TypeID });
        }

        internal void Commit() { committed = true; }

        public void Dispose()
        {
            if (!committed)
            {
                for (int r = receipts.Count - 1; r >= 0; r--)
                {
                    Receipt receipt = receipts[r];
                    // 官方 Push 可能已销毁原件，再在通知里抛错；只移除该原件产生的缓冲收据。
                    List<ItemTreeData> buffer = PlayerStorage.IncomingItemBuffer;
                    if (buffer != null)
                        for (int i = buffer.Count - 1; i >= 0; i--)
                            if (buffer[i] != null && buffer[i].rootInstanceID == receipt.InstanceId && buffer[i].RootTypeID == receipt.TypeId)
                                buffer.RemoveAt(i);
                    if (receipt.Item == null || receipt.Item.IsBeingDestroyed) continue;
                    try { receipt.Item.Detach(); }
                    catch (Exception e) { ModBehaviour.DevLog("[OfficialQuest] 奖励回滚通知异常: " + e.Message); }
                    SkyIslandInventoryTransaction.DestroyUnowned(receipt.Item);
                }
            }
            receipts.Clear();
        }
    }

    /// <summary>
    /// 官方任务投影的物品一侧：数身上有几个、预留提交物、预先生成奖励物、交付后发放。
    /// 只在交付那一拍与任务页刷新时调用；数物品只遍历主角背包一次（有界），不扫仓库、不扫场景。
    /// </summary>
    internal static class OfficialQuestItems
    {
        internal static int HeldInBackpack(int typeId)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Inventory inventory = main != null && main.CharacterItem != null ? main.CharacterItem.Inventory : null;
                if (inventory == null || inventory.Content == null) return 0;
                int total = 0;
                for (int i = 0; i < inventory.Content.Count; i++)
                {
                    Item item = inventory.Content[i];
                    if (item != null && !item.IsBeingDestroyed && item.TypeID == typeId) total += item.Stackable ? item.StackCount : 1;
                }
                return total;
            }
            catch (Exception)
            {
                // 背包读不到时按没有处理：只会让目标暂时显示未完成，交付事务会再核一次
                return 0;
            }
        }

        internal static string DisplayName(int typeId)
        {
            try
            {
                string name = ItemAssetsCollection.GetMetaData(typeId).DisplayName;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception)
            {
                // 元数据读不到时退回编号
            }
            return "#" + typeId;
        }

        /// <summary>按提交表整份预留；没有提交表时成功且 reservation 为 null。凑不够或背包变化时什么都不动。</summary>
        internal static bool TryReserve(OfficialQuestSubmission[] submissions, out OfficialQuestItemReservation reservation, out string reason)
        {
            reservation = null;
            reason = null;
            if (submissions == null || submissions.Length == 0) return true;
            var plan = new List<OfficialQuestItemStack>();
            OfficialQuestSubmission missing;
            if (!OfficialQuestItemRules.TryPlan(submissions, HeldInBackpack, plan, out missing))
            {
                reason = L10n.T("东西没带够：", "You don't have everything on you: ")
                    + (missing != null && missing.Description != null ? missing.Description() : string.Empty);
                return false;
            }
            var inputs = new SkyIslandIngredient[plan.Count];
            for (int i = 0; i < plan.Count; i++) inputs[i] = new SkyIslandIngredient(plan[i].TypeId, plan[i].Count);
            SkyIslandInventoryTransaction transaction;
            if (!SkyIslandInventoryTransaction.TryReserve(CharacterMainControl.Main, inputs, out transaction))
            {
                reason = L10n.T("要交的东西得放在背包里，再试一次。", "Keep the items to hand in inside your backpack, then try again.");
                return false;
            }
            reservation = new OfficialQuestItemReservation(transaction);
            return true;
        }

        /// <summary>
        /// 交付前先把奖励物全部生成好：任何一件的 prefab 不在就整体放弃（官方 InstantiateSync 缺资源时返回空壳而不是 null），
        /// 交付失败时由 <see cref="Discard"/> 销毁，所以玩家不会拿到半份奖励，也不会因为奖励缺件白交了东西。
        /// </summary>
        internal static bool TryCreate(OfficialQuestItemStack[] rewards, List<Item> created, out string reason)
        {
            reason = null;
            if (rewards == null || rewards.Length == 0) return true;
            try
            {
                for (int r = 0; r < rewards.Length; r++)
                {
                    int typeId = rewards[r].TypeId, left = rewards[r].Count;
                    if (typeId <= 0 || left <= 0) continue;
                    if (BossRushDynamicItemRegistry.IsBossRushDynamicItemType(typeId)) BossRushDynamicItemRegistry.EnsureRegistered(typeId);
                    Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                    if (prefab == null) throw new InvalidOperationException("奖励物品资源缺失: " + typeId);
                    int perStack = prefab.Stackable ? Math.Max(1, prefab.MaxStackCount) : 1;
                    while (left > 0)
                    {
                        Item item = ItemAssetsCollection.InstantiateSync(typeId);
                        if (item == null) throw new InvalidOperationException("奖励物品生成失败: " + typeId);
                        created.Add(item);
                        int count = Math.Min(left, perStack);
                        if (perStack > 1) item.StackCount = count;
                        left -= count;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OfficialQuest] [WARNING] 奖励物品未就绪，交付中止: " + e.Message);
                Discard(created);
                reason = L10n.T("奖励还没备齐，过会儿再来。", "The reward isn't ready yet. Come back in a bit.");
                return false;
            }
        }

        /// <summary>准备回滚所需的原件/收据后投递。此时客户端仍冻结采集，失败不会消费任务资格。</summary>
        internal static bool TryGive(List<Item> created, bool inboxOnly, out OfficialQuestRewardReservation delivered, out string reason)
        {
            delivered = new OfficialQuestRewardReservation();
            reason = null;
            if (created == null) return true;
            for (int i = 0; i < created.Count; i++)
            {
                Item item = created[i];
                if (item != null)
                {
                    delivered.Track(item);
                    if (TryPlace(item, inboxOnly)) continue;
                }
                reason = L10n.T("奖励这会儿送不出去，要交的东西没收走，过会儿再试。",
                    "The reward can't go out right now. Nothing was taken from you. Try again shortly.");
                return false;
            }
            return true;
        }

        private static bool TryPlace(Item item, bool inboxOnly)
        {
            int instanceId = item.GetInstanceID(), typeId = item.TypeID;
            if (!inboxOnly)
            {
                // 事务内禁合堆，防止 Combine 的回调在来源扣量前抛错，令整份奖品无法精确回滚。
                try { ItemUtilities.SendToPlayerCharacter(item, true); }
                catch (Exception e) { ModBehaviour.DevLog("[OfficialQuest] 奖励入包通知异常: " + e.Message); }
                if (SkyIslandInventoryTransaction.HasOwner(item)) return true;
                try { if (PlayerStorage.Inventory != null) PlayerStorage.Inventory.AddItem(item); }
                catch (Exception e) { ModBehaviour.DevLog("[OfficialQuest] 奖励入库通知异常: " + e.Message); }
                if (SkyIslandInventoryTransaction.HasOwner(item)) return true;
            }
            try { ItemUtilities.SendToPlayerStorage(item, true); }
            catch (Exception e) { ModBehaviour.DevLog("[OfficialQuest] 奖励寄存通知异常: " + e.Message); }
            return SkyIslandInventoryTransaction.HasOwner(item) || SkyIslandInventoryTransaction.HasBufferReceipt(instanceId, typeId);
        }

        internal static bool CanCollectAssets(bool inboxOnly)
        {
            if (SavesSystem.CurrentSlot < 0 || SavesSystem.IsSaving || PlayerStorageBuffer.Instance == null || PlayerStorage.IncomingItemBuffer == null) return false;
            if (inboxOnly) return true;
            CharacterMainControl player = CharacterMainControl.Main;
            return player != null && player.CharacterItem != null && player.Health != null &&
                PlayerStorage.Instance != null && PlayerStorage.Instance.HasInitialized() && !PlayerStorage.Loading && PlayerStorage.Inventory != null;
        }

        /// <summary>由客户端共享保存引擎调用；只在整笔交付完成后采集，不自行 SaveFile，也不写入不同槽。</summary>
        internal static Func<bool> AssetCollector(bool inboxOnly)
        {
            int slot = SavesSystem.CurrentSlot;
            return () =>
            {
                try
                {
                    if (SavesSystem.CurrentSlot != slot || !CanCollectAssets(inboxOnly)) return false;
                    if (!inboxOnly)
                    {
                        CharacterMainControl player = CharacterMainControl.Main;
                        player.CharacterItem.Save("MainCharacterItemData");
                        SavesSystem.Save<float>("MainCharacterHealth", player.Health.CurrentHealth);
                        PlayerStorage.Inventory.Save("PlayerStorage");
                    }
                    PlayerStorageBuffer.SaveBuffer();
                    return true;
                }
                catch (Exception) { return false; }
            };
        }

        /// <summary>交付没成：销毁预先生成、还没发出去的奖励物。</summary>
        internal static void Discard(List<Item> created)
        {
            if (created == null) return;
            for (int i = 0; i < created.Count; i++) SkyIslandInventoryTransaction.DestroyUnowned(created[i]);
            created.Clear();
        }
    }
}
