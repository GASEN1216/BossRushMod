using System;
using Duckov.ItemUsage;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>只消费明确展示的同一背包物品。排除锁格、愿望清单、任务/自定义物品、容器与已使用药包。</summary>
    internal static class GreedyMimicInventory
    {
        internal static bool Eligible(Item item, Inventory inventory, int slot, GreedyMimicFood food)
        {
            if ((int)food < 0 || (int)food > 2 || item == null || inventory == null || item.IsBeingDestroyed || item.InInventory != inventory
                || slot < 0 || inventory.GetItemAt(slot) != item || inventory.IsIndexLocked(slot)
                || item.TypeID <= 0 || item.TypeID >= BossRushItemIds.BossRushTicket || item.Quality < 1 || item.Quality > 3 || item.Sticky || !item.CanBeSold
                || item.Inventory != null || (item.Slots != null && item.Slots.Count > 0)
                || item.Tags.Contains("Quest") || item.Tags.Contains("Special")
                || (ItemWishlist.Instance != null && ItemWishlist.Instance.IsManuallyWishlisted(item.TypeID))) return false;
            if ((item.Stackable ? item.StackCount : 1) < GreedyMimicRules.Cost(food)) return false;
            if (food == GreedyMimicFood.Ammunition) return item.GetComponent<ItemSetting_Bullet>() != null;
            if (food == GreedyMimicFood.Explosive)
            {
                ItemSetting_Skill skill = item.GetComponent<ItemSetting_Skill>();
                Skill_Grenade grenade = skill != null ? skill.Skill as Skill_Grenade : null;
                return skill != null && skill.onRelease == ItemSetting_Skill.OnReleaseAction.reduceCount
                    && grenade != null && grenade.createExplosion && grenade.damageInfo.damageValue > 0f;
            }
            Drug drug = item.GetComponentInChildren<Drug>(true);
            return drug != null && drug.healValue > 0 && (!item.UseDurability || item.Durability >= item.MaxDurabilityWithLoss - 0.01f);
        }

        internal static Item Find(CharacterMainControl player, GreedyMimicFood food, out int slot)
        {
            slot = -1;
            Inventory pack = player != null && player.CharacterItem != null ? player.CharacterItem.Inventory : null;
            if (pack == null) return null;
            Item best = null;
            for (int i = 0; i < pack.Content.Count; i++)
            {
                Item item = pack.Content[i];
                if (Eligible(item, pack, i, food) && (best == null || item.Quality < best.Quality || (item.Quality == best.Quality && item.Value < best.Value)))
                { best = item; slot = i; }
            }
            return best;
        }

        internal static bool Consume(Item item, CharacterMainControl player, int slot, GreedyMimicFood food, Action applyMeal)
        {
            Inventory pack = player != null && player.CharacterItem != null ? player.CharacterItem.Inventory : null;
            if (!Eligible(item, pack, slot, food)) return false;
            int original = item.Stackable ? item.StackCount : 1;
            int take = GreedyMimicRules.Cost(food);
            bool touched = false;
            try
            {
                touched = true;
                if (take < original)
                {
                    try { item.StackCount = original - take; }
                    catch (Exception) { if (item.StackCount != original - take) throw; }
                    if (item.StackCount != original - take) throw new InvalidOperationException("投喂数量未扣除");
                }
                else
                {
                    try { pack.RemoveItem(item); }
                    catch (Exception) { if (pack.Content.Contains(item) || SkyIslandInventoryTransaction.HasOwner(item)) throw; }
                    if (pack.Content.Contains(item) || SkyIslandInventoryTransaction.HasOwner(item)) throw new InvalidOperationException("投喂原件未取出");
                }
                // 新接收体与可见成长完成后才销毁足额原件；建模失败可归还同一个原件。
                if (applyMeal == null) throw new InvalidOperationException("投喂成长回调缺失");
                applyMeal();
                if (take == original) SkyIslandInventoryTransaction.DestroyUnowned(item);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GreedyMimic] 投喂失败，归还原件: " + e.Message);
                try
                {
                    if (touched && item != null && !item.IsBeingDestroyed)
                    {
                        if (take < original)
                        {
                            if (item.StackCount != original) item.StackCount += take;
                        }
                        else if (!SkyIslandInventoryTransaction.HasOwner(item))
                        {
                            RestoreWhole(item, pack, slot, player);
                        }
                    }
                }
                catch (Exception rollback) { Debug.LogWarning("[GreedyMimic] 原件恢复通知失败: " + rollback.Message); }
                return false;
            }
        }

        private static void RestoreWhole(Item item, Inventory pack, int slot, CharacterMainControl player)
        {
            // 原槽被回调占用、AddAt 返回失败及提交后的通知异常，都按原件实际归属继续。
            try { if (pack.GetItemAt(slot) == null) pack.AddAt(item, slot); }
            catch (Exception e) { ModBehaviour.DevLog("[GreedyMimic] 原槽归还通知异常: " + e.Message); }
            if (SkyIslandInventoryTransaction.HasOwner(item) || pack.Content.Contains(item)) return;
            try { pack.AddItem(item); }
            catch (Exception e) { ModBehaviour.DevLog("[GreedyMimic] 背包归还通知异常: " + e.Message); }
            if (SkyIslandInventoryTransaction.HasOwner(item) || pack.Content.Contains(item)) return;
            item.Drop(player, true);
        }

        internal static void Reward(int typeId, int count, Vector3 position)
        {
            if (ItemAssetsCollection.GetPrefab(typeId) == null)
                throw new InvalidOperationException("奖励原型未注册: " + typeId);
            int remaining = count;
            for (int attempt = 0; attempt < 4 && remaining > 0; attempt++)
            {
                Item reward = null;
                try
                {
                    reward = ItemAssetsCollection.InstantiateSync(typeId);
                    if (reward == null || reward.TypeID != typeId)
                        throw new InvalidOperationException("奖励类型不符: " + typeId);
                    int amount = reward.Stackable ? Mathf.Clamp(remaining, 1, reward.MaxStackCount) : 1;
                    if (reward.Stackable) reward.StackCount = amount;
                    reward.Drop(position + Vector3.up * 0.8f + Vector3.right * attempt * 0.25f, true, Vector3.up, 3f);
                    remaining -= amount;
                }
                finally { SkyIslandInventoryTransaction.DestroyUnowned(reward); }
            }
            if (remaining > 0) Debug.LogWarning("[GreedyMimic] 物品堆叠上限异常，未投递数量: " + remaining);
        }
    }
}
