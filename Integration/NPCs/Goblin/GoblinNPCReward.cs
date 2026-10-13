// ============================================================================
// GoblinNPCReward.cs - 哥布林NPC奖励物品赠送
// ============================================================================
// 模块说明：
//   哥布林NPC控制器的partial class，负责奖励物品赠送功能
//   包括：
//   - 结婚后每日赠送冷凝液
//   - 物品掉落处理
//   - 横幅提示显示
// ============================================================================

using System;
using ItemStatsSystem;
using UnityEngine;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// 哥布林NPC控制器 - 奖励物品赠送
    /// </summary>
    public partial class GoblinNPCController
    {
        private bool rewardDeliveryInProgress;

        /// <summary>
        /// 尝试赠送奖励物品给玩家
        /// </summary>
        /// <param name="typeId">物品类型ID</param>
        /// <param name="stackCount">物品数量</param>
        /// <param name="successBanner">成功时显示的横幅文本</param>
        /// <param name="fullInventoryBanner">背包满时显示的横幅文本</param>
        /// <returns>是否成功赠送</returns>
        public bool TryGiveRewardItem(int typeId, int stackCount, string successBanner = null, string fullInventoryBanner = null)
        {
            if (rewardDeliveryInProgress) return false;
            rewardDeliveryInProgress = true;
            Item rewardItem = null;
            bool delivered = false;
            try
            {
                // 确保物品资源已加载
                EnsureRewardItemLoaded(typeId);

                // 实例化物品
                rewardItem = ItemAssetsCollection.InstantiateSync(typeId);
                if (rewardItem == null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] [WARNING] 奖励物品实例化失败: typeId=" + typeId);
                    return false;
                }

                // 设置堆叠数量
                if (stackCount > 1)
                {
                    int maxStackCount = rewardItem.MaxStackCount > 0 ? rewardItem.MaxStackCount : stackCount;
                    rewardItem.StackCount = Mathf.Clamp(stackCount, 1, maxStackCount);
                }

                CharacterMainControl player = CharacterMainControl.Main;
                Inventory inventory = player != null && player.CharacterItem != null ? player.CharacterItem.Inventory : null;
                bool dropped = false;
                delivered = SkyIslandInventoryTransaction.TryDeliver(rewardItem, inventory, remaining =>
                {
                    if (inventory != null)
                    {
                        try { inventory.AddItem(remaining); }
                        catch (Exception e) { ModBehaviour.DevLog("[GoblinNPC] 奖励入包通知异常: " + e.Message); }
                        if (remaining.InInventory == inventory) return true;
                    }
                    dropped = DropRewardItemAtNpc(remaining, fullInventoryBanner);
                    return dropped;
                });
                if (delivered && !dropped) ShowRewardBanner(successBanner);
                return delivered;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 赠送奖励物品失败: " + e.Message);
                return false;
            }
            finally
            {
                try { if (!delivered) SkyIslandInventoryTransaction.DestroyUnowned(rewardItem); }
                finally { rewardDeliveryInProgress = false; }
            }
        }

        /// <summary>
        /// 确保奖励物品的AssetBundle已加载
        /// </summary>
        private static void EnsureRewardItemLoaded(int typeId)
        {
            // 回火液是克隆冷淬液的运行时物品，没有自己的 bundle
            if (typeId == TemperingSolventConfig.TYPE_ID)
            {
                TemperingSolventConfig.EnsureRuntimeRegistration();
                return;
            }

            // 自定义物品已经在初始化时加载，无需重复加载
            if (typeId <= 0 || ItemFactory.IsCustomItem(typeId))
            {
                return;
            }

            // 根据物品ID确定需要加载的Bundle
            string bundleName = null;
            switch (typeId)
            {
                case ColdQuenchFluidConfig.TYPE_ID:
                    bundleName = ColdQuenchFluidConfig.BUNDLE_NAME;
                    break;
                // 可以在这里添加其他物品的Bundle映射
            }

            if (string.IsNullOrEmpty(bundleName))
            {
                return;
            }

            // 加载Bundle
            int loadedCount = ItemFactory.LoadBundle(bundleName);
            ModBehaviour.DevLog("[GoblinNPC] 确保奖励物品已加载: typeId=" + typeId + ", bundle=" + bundleName + ", loadedCount=" + loadedCount);
        }

        /// <summary>
        /// 将奖励物品掉落在NPC脚边
        /// </summary>
        private bool DropRewardItemAtNpc(Item rewardItem, string fullInventoryBanner = null)
        {
            if (rewardItem == null)
            {
                return false;
            }

            // 计算掉落方向（NPC朝向）
            Vector3 dropDirection = transform.forward;
            if (dropDirection.sqrMagnitude <= 0.001f)
            {
                dropDirection = Vector3.forward;
            }

            // 计算掉落位置（NPC脚边稍微抬高）
            Vector3 dropPosition = transform.position + Vector3.up * 0.15f;
            try { rewardItem.Drop(dropPosition, true, dropDirection.normalized, 0f); }
            catch (Exception e) { ModBehaviour.DevLog("[GoblinNPC] 奖励落地通知异常: " + e.Message); }
            if (rewardItem == null || rewardItem.ActiveAgent == null) return false;

            // 显示提示
            string fallbackMessage = L10n.T("背包已满，奖励已掉落在叮当脚边。", "Inventory full. The reward was dropped at Dingdang's feet.");
            if (string.IsNullOrEmpty(fullInventoryBanner))
            {
                ShowRewardBanner(fallbackMessage);
            }
            else
            {
                ShowRewardBanner(fullInventoryBanner);
            }

            ModBehaviour.DevLog("[GoblinNPC] 奖励物品已掉落在NPC脚边: typeId=" + rewardItem.TypeID + ", position=" + dropPosition);
            return true;
        }

        /// <summary>
        /// 显示奖励横幅提示
        /// </summary>
        private void ShowRewardBanner(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                NotificationText.Push(text);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 显示横幅失败: " + e.Message);
            }
        }
    }
}
