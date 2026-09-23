// ============================================================================
// CourierPaidLootSweepDelivery.cs - 阿稳扫箱：结果物品回到玩家手里的那一段
// ============================================================================
// 从 CourierPaidLootSweepService.cs 原样提取（2026-09-23，AGENTS §4.15：大文件超 1200 行时拆到同一 partial 的新文件，
// 行为逐字不变）。包括：结果箱物品退回 / 寄快递、玩家自己塞进去的物品留箱，以及两条总横幅。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.UI;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public static partial class CourierPaidLootSweepService
    {
        private static bool TryReturnResultItemsToPlayer(Inventory resultInventory)
        {
            return TryReturnResultItemsToPlayer(resultInventory, false);
        }

        /// <summary>
        /// 交还代收箱。扫箱产出静默寄快递、汇总一条横幅；剩下的（玩家自己塞的、快递站不可写或序列化失败的）
        /// 先直接塞回背包——这一步不弹官方横幅；塞不下时：
        ///   - keepPlayerItemsWhenFull=true（玩家点「开启下次扫箱」）：留在箱里，汇总提示一条，本次不放行；
        ///   - false（被动收尾，箱子马上要没了）：照官方 SendToPlayer 送仓库 / 快递，宁可多几条横幅也不能丢东西。
        /// 旧写法剩下的东西一律逐件 SendToPlayer，背包满时每件一条横幅，而且玩家自己塞的东西被免费寄走（2026-09-23 复核第 13 项）。
        /// </summary>
        private static bool TryReturnResultItemsToPlayer(Inventory resultInventory, bool keepPlayerItemsWhenFull)
        {
            if (resultInventory == null || resultInventory.Content == null)
            {
                return true;
            }
            bool deliveredAll = true;
            // 只有扫箱产出的物品寄快递；玩家自己塞进箱子的东西照旧交还背包（否则开启下次扫箱就成了免快递费的寄件口）。
            List<Item> items = new List<Item>();
            List<Item> remainingItems = new List<Item>();
            for (int i = 0; i < resultInventory.Content.Count; i++)
            {
                Item item = resultInventory.Content[i];
                if (item == null)
                {
                    continue;
                }

                if (pendingResultSweepItems.Contains(item)) items.Add(item);
                else remainingItems.Add(item);
            }

            if (items.Count <= 0 && remainingItems.Count <= 0)
            {
                return true;
            }

            // 阿稳代收的整箱一律寄进快递：静默入缓冲、一次落盘、只报一条汇总横幅。
            // 逐件 SendToPlayer 在背包满时每件都推一条官方横幅，会把要看的消息全挡住（2026-09-22 实测第 13 条）。
            int mailedCount = CourierService.BufferItemsSilently(items, remainingItems);
            if (mailedCount > 0)
            {
                ShowSweepResultMailedBanner(mailedCount);
            }

            // 玩家塞进来的东西、快递站不可写或个别物品序列化失败：先塞回背包，塞不下再按 keepPlayerItemsWhenFull 分流，
            // 交付不了的留在箱里、不放行下一趟。
            int keptInCrate = 0;
            for (int i = 0; i < remainingItems.Count; i++)
            {
                Item item = remainingItems[i];
                if (item == null)
                {
                    continue;
                }

                try
                {
                    item.Detach();
                }
                catch {}

                bool toBackpack = false;
                try
                {
                    toBackpack = ItemUtilities.SendToPlayerCharacter(item, false);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 塞回背包失败: " + e.Message);
                }
                if (toBackpack)
                {
                    continue;
                }

                if (keepPlayerItemsWhenFull && TryKeepInCrate(resultInventory, item))
                {
                    keptInCrate++;
                    deliveredAll = false;
                    continue;
                }

                try
                {
                    ItemUtilities.SendToPlayer(item, true, true);
                }
                catch (Exception e)
                {
                    if (item != null && item.InInventory == null && item.PluggedIntoSlot == null)
                    {
                        deliveredAll = false;
                        try { resultInventory.AddItem(item); }
                        catch (Exception restoreError) { ModBehaviour.DevLog("[CourierPaidLootSweep] 保留未交付物品失败: " + restoreError.Message); }
                    }
                    ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 返还总箱物品失败: " + e.Message);
                }
            }

            if (keptInCrate > 0)
            {
                ShowSweepResultKeptBanner(keptInCrate);
            }
            return deliveredAll;
        }

        /// <summary>背包塞不下的东西放回代收箱（能叠就叠）。放不回去返回 false，由调用方照官方交付兜底。</summary>
        private static bool TryKeepInCrate(Inventory resultInventory, Item item)
        {
            if (resultInventory == null || item == null)
            {
                return false;
            }

            try
            {
                if (resultInventory.AddAndMerge(item, 0))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 放回代收箱失败: " + e.Message);
            }
            return false;
        }

        /// <summary>背包满、东西留在箱里：只报一条汇总（不是每件一条）。</summary>
        private static void ShowSweepResultKeptBanner(int keptCount)
        {
            try
            {
                NotificationText.Push(L10n.T(
                    "<color=" + SweepHighlightHex + ">背包满了：还有 " + keptCount + " 件留在代收箱里，腾出位置后再开启下次扫箱。</color>",
                    "<color=" + SweepHighlightHex + ">Backpack full: " + keptCount + " items stay in the pickup crate. Make room, then start the next sweep.</color>"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 显示留箱横幅失败: " + e.Message);
            }
        }

        private static void ShowSweepResultMailedBanner(int mailedCount)
        {
            try
            {
                NotificationText.Push(L10n.T(
                    "<color=" + SweepSuccessHex + ">阿稳已把扫箱箱子里的 " + mailedCount + " 件物品放进快递</color>",
                    "<color=" + SweepSuccessHex + ">Awen sent " + mailedCount + " sweep crate items to your base deliveries</color>"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierPaidLootSweep] [WARNING] 显示扫箱寄件横幅失败: " + e.Message);
            }
        }
    }
}
