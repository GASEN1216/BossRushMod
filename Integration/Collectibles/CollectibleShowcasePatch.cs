// ============================================================================
// CollectibleShowcasePatch.cs - 收集品 3D 模型的唯一接点
// ============================================================================
// 官方陈列柜（Showcase.RefreshSlot）、地上掉落（InteractablePickup）、作物、鱼缸、抽奖箱生成物品模型都调
// ItemGraphicInfo.CreateAGraphic(Item, Transform, bool, bool)，所以只在这里前置：收集品还没有 ItemGraphic 就按需挂上
// （CollectibleShowcaseModels）。非收集品只做一次按下标取表；异常吞掉，官方照常走自己的分支。
// ============================================================================

using System;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    [HarmonyPatch(typeof(ItemGraphicInfo), nameof(ItemGraphicInfo.CreateAGraphic), new Type[] { typeof(Item), typeof(Transform), typeof(bool), typeof(bool) })]
    internal static class CollectibleShowcasePatch
    {
        [HarmonyPrefix]
        internal static void BeforeCreateAGraphic(Item item)
        {
            try
            {
                CollectibleShowcaseModels.EnsureGraphic(item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 收集品模型挂载失败: " + e.Message);
            }
        }
    }
}
