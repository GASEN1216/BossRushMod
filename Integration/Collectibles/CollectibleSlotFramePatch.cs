// ============================================================================
// CollectibleSlotFramePatch.cs - 6–8 档收集品格子边框的唯一接点
// ============================================================================
// 官方背包、箱子、仓库、商店、装备槽、拖拽与右键菜单里的物品格都是 Duckov.UI.ItemDisplay，换物品一律走
// Setup(Item)（InventoryEntry.Setup、SlotDisplay.Setup 等都调它），所以只挂这一处后置补丁。
// 官方回收格子（NotifyReleased）不走 Setup，由 CollectibleSlotFrame 每帧核对 display.Target 自行收起。
// 补丁对非收集品只做一次按下标取表、一次字典查找；异常吞掉，不影响官方格子本身。
// ============================================================================

using System;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    [HarmonyPatch(typeof(ItemDisplay), nameof(ItemDisplay.Setup), new Type[] { typeof(Item) })]
    internal static class CollectibleSlotFramePatch
    {
        [HarmonyPostfix]
        internal static void AfterSetup(ItemDisplay __instance, Item target, Image ___icon, GameObject ___selectionIndicator)
        {
            try
            {
                CollectibleSlotFrame.BindItemDisplay(__instance, target, ___icon, ___selectionIndicator);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 格子边框绑定失败: " + e.Message);
            }
        }
    }
}
