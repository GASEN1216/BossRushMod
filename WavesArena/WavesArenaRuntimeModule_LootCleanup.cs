using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using Duckov.ItemUsage;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal IEnumerator LogBossLootInventory_LootAndRewards(InteractableLootbox lootbox)
        {
            if (lootbox == null)
            {
                yield break;
            }

            Inventory inv = lootbox.Inventory;
            if (inv == null)
            {
                yield break;
            }

            // 等待 LootBoxLoader 完成填充
            int tries = 0;
            const int maxTries = 50;
            while (tries < maxTries && inv.Loading)
            {
                tries++;
                yield return new WaitForSeconds(0.1f);
            }

            List<Item> content = inv.Content;
            if (content == null)
            {
                yield break;
            }

            try
            {
                ModBehaviour.DevLog("[BossRush] Boss 掉落实际物品列表开始, 总数=" + content.Count);
            }
            catch (Exception)
            {
                // 调试输出失败不应影响后续逻辑。
            }

            for (int i = 0; i < content.Count; i++)
            {
                Item item = content[i];
                if (item == null)
                {
                    continue;
                }

                int q = -1;
                int v = -1;
                string name = "<unknown>";
                string displayQ = "<unknown>";

                try
                {
                    q = item.Quality;
                }
                catch (Exception)
                {
                    q = -1;
                }

                try
                {
                    v = item.Value;
                }
                catch (Exception)
                {
                    v = -1;
                }

                try
                {
                    name = item.DisplayName;
                }
                catch (Exception)
                {
                    name = "<unknown>";
                }

                try
                {
                    displayQ = item.DisplayQuality.ToString();
                }
                catch (Exception)
                {
                    displayQ = "<unknown>";
                }

                ModBehaviour.DevLog("[BossRush] 实际掉落物: typeID=" + item.TypeID + ", 名称=" + name + ", Quality=" + q + ", DisplayQuality=" + displayQ + ", Value=" + v);
            }

            try
            {
                ModBehaviour.DevLog("[BossRush] Boss 掉落实际物品列表结束");
            }
            catch (Exception)
            {
                // 调试输出失败不应影响奖励箱处理。
            }

            // LootBoxLoader 填充完成后，根据实际物品数量调整 Inventory 容量
            // 这是解决"格子为64"问题的关键
            try
            {
                int lastPos = inv.GetLastItemPosition();
                int newCapacity = Mathf.Max(8, lastPos + 1);
                inv.SetCapacity(newCapacity);
                ModBehaviour.DevLog("[BossRush] Boss 奖励箱容量已调整为: " + newCapacity);
            }
            catch (Exception capEx)
            {
                ModBehaviour.DevLog("[BossRush] 调整 Boss 奖励箱容量失败: " + capEx.Message);
            }
        }

        internal IEnumerator CleanupDifficultyRewardLootboxInventory_LootAndRewards(InteractableLootbox lootbox, int highQualityCount)
        {
            if (lootbox == null)
            {
                yield break;
            }

            Inventory inv = lootbox.Inventory;
            if (inv == null)
            {
                yield break;
            }

            // 等待一小段时间，给 LootBoxLoader.Setup 机会完成
            const int maxTries = 30;
            int tries = 0;
            while (tries < maxTries && inv.Loading)
            {
                tries++;
                yield return new WaitForSeconds(0.1f);
            }

            try
            {
                List<Item> content = inv.Content;
                if (content == null || content.Count == 0)
                {
                    yield break;
                }

                // 调试：清理前先输出一次通关奖励箱实际物品列表
                try
                {
                    ModBehaviour.DevLog("[BossRush] 通关奖励清理前物品列表开始, 总数=" + content.Count);
                    for (int i = 0; i < content.Count; i++)
                    {
                        Item item = content[i];
                        if (item == null) continue;

                        int q = -1;
                        int v = -1;
                        string name = "<unknown>";
                        string displayQ = "<unknown>";

                        try { q = item.Quality; } catch (Exception) { q = -1; }
                        try { v = item.Value; } catch (Exception) { v = -1; }
                        try { name = item.DisplayName; } catch (Exception) { name = "<unknown>"; }
                        try { displayQ = item.DisplayQuality.ToString(); } catch (Exception) { displayQ = "<unknown>"; }

                        ModBehaviour.DevLog("[BossRush] 通关奖励实际物品(清理前): typeID=" + item.TypeID + ", 名称=" + name + ", Quality=" + q + ", DisplayQuality=" + displayQ + ", Value=" + v);
                    }
                    ModBehaviour.DevLog("[BossRush] 通关奖励清理前物品列表结束");
                }
                catch (Exception)
                {
                    // 调试输出失败不影响通关奖励清理逻辑。
                }

                int beforeCount = content.Count;

                // 按品质和价格分两档：高品质高价、高品质低价。
                // 低品质物品（Quality<5）一律不保留，无需单独装桶。
                // 复用实例级 scratch 列表，避免每次清理都分配。
                List<Item> preferred = difficultyRewardPreferredScratch;
                List<Item> fallbackHighQuality = difficultyRewardFallbackHighQualityScratch;
                preferred.Clear();
                fallbackHighQuality.Clear();

                const int priceThreshold = 2000;

                for (int i = 0; i < content.Count; i++)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if (item.Quality < 5)
                    {
                        continue;
                    }

                    int value = 0;
                    try
                    {
                        value = item.Value;
                    }
                    catch
                    {
                        value = 0;
                    }

                    if (value >= priceThreshold)
                    {
                        preferred.Add(item);
                    }
                    else
                    {
                        fallbackHighQuality.Add(item);
                    }
                }

                int target = highQualityCount;
                if (target < 1)
                {
                    target = 1;
                }
                if (target > beforeCount)
                {
                    target = beforeCount;
                }

                List<Item> keep = difficultyRewardKeepScratch;
                keep.Clear();

                // 1) 优先保留高品质高价物品
                for (int i = 0; i < preferred.Count && keep.Count < target; i++)
                {
                    keep.Add(preferred[i]);
                }

                // 2) 如果高价高品质不足以填满目标数量，则继续用高品质但低价的物品补齐
                for (int i = 0; i < fallbackHighQuality.Count && keep.Count < target; i++)
                {
                    keep.Add(fallbackHighQuality[i]);
                }

                // 3) 仍然完全不保留低品质物品（Quality<5），保证 Quality>=5

                int removed = 0;

                // 反向遍历，删除未入选的物品
                for (int i = content.Count - 1; i >= 0; i--)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if (!keep.Contains(item))
                    {
                        removed++;
                        ItemTreeExtensions.DestroyTree(item);
                    }
                }

                // 调试：清理后再次输出通关奖励箱实际物品列表
                try
                {
                    List<Item> afterContent = inv.Content;
                    if (afterContent != null)
                    {
                        ModBehaviour.DevLog("[BossRush] 通关奖励清理后物品列表开始, 总数=" + afterContent.Count);
                        for (int i = 0; i < afterContent.Count; i++)
                        {
                            Item item = afterContent[i];
                            if (item == null) continue;

                            int q2 = -1;
                            int v2 = -1;
                            string name2 = "<unknown>";
                            string displayQ2 = "<unknown>";

                            try { q2 = item.Quality; } catch (Exception) { q2 = -1; }
                            try { v2 = item.Value; } catch (Exception) { v2 = -1; }
                            try { name2 = item.DisplayName; } catch (Exception) { name2 = "<unknown>"; }
                            try { displayQ2 = item.DisplayQuality.ToString(); } catch (Exception) { displayQ2 = "<unknown>"; }

                            ModBehaviour.DevLog("[BossRush] 通关奖励实际物品(清理后): typeID=" + item.TypeID + ", 名称=" + name2 + ", Quality=" + q2 + ", DisplayQuality=" + displayQ2 + ", Value=" + v2);
                        }
                        ModBehaviour.DevLog("[BossRush] 通关奖励清理后物品列表结束");
                    }
                }
                catch (Exception)
                {
                    // 调试输出失败不影响通关奖励清理逻辑。
                }

                if (removed > 0)
                {
                    ModBehaviour.DevLog("[BossRush] 调整通关奖励箱内容: 原总数=" + beforeCount + ", 目标高品质数量=" + target + ", 实际保留数量=" + keep.Count + ", 移除数量=" + removed);
                }
            }
            catch (Exception cleanEx)
            {
                ModBehaviour.DevLog("[BossRush] 清理通关奖励箱低品质物品失败: " + cleanEx.Message);
            }
            finally
            {
                // 清空 scratch 列表，释放对 Item 的引用，避免跨清理批次悬挂。
                ClearDifficultyRewardCleanupScratch();
            }
        }

        /// <summary>
        /// 清空通关奖励清理用的复用 scratch 列表，释放其中持有的 Item 引用。
        /// </summary>
        internal void ClearDifficultyRewardCleanupScratch()
        {
            difficultyRewardPreferredScratch.Clear();
            difficultyRewardFallbackHighQualityScratch.Clear();
            difficultyRewardKeepScratch.Clear();
        }

    }
}
