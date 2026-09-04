// ============================================================================
// RaidMealUsageBehavior.cs - 出击餐的「吃」
// ============================================================================
// 形态照 Integration/Items/BrickStoneUsage.cs。
//
// 【只在基地能吃】
//   出击餐的语义是「出击前吃、下一局生效」。在局内吃它没有任何意义
//   （登记的是"下一局"，而玩家已经在这一局里了），还会白白消耗掉一份。
//   所以 CanBeUsed 在非基地场景直接返回 false，物品不会被消耗。
//
// CA_UseItem.OnFinish 无条件扣数量。失败时预补一份，抵消紧随其后的官方扣减。
// ============================================================================

using System;
using ItemStatsSystem;
using Saves;

namespace BossRush
{
    /// <summary>出击餐使用行为：登记一份待生效的下一局增益。</summary>
    public class RaidMealUsageBehavior : UsageBehavior
    {
        /// <summary>物品描述里显示的使用说明。</summary>
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T("食用：下一局出击时生效", "Eat: takes effect on your next run")
                };
            }
        }

        /// <summary>只在基地可用——局内吃等于白吃一份。</summary>
        public override bool CanBeUsed(Item item, object user)
        {
            try
            {
                if (item == null) return false;
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner == null || !owner.IsBackMountainConfiguredEnabled()) return false;
                BackMountainItems.Definition def = BackMountainItems.GetDefinition(item.TypeID);
                if (def == null || def.IsSeed || SavesSystem.IsSaving) return false;
                return LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>登记这份出击餐。物品消耗交给框架，这里不手动扣。</summary>
        protected override void OnUse(Item item, object user)
        {
            bool registered = false;
            try
            {
                if (item == null) return;

                int typeId = item.TypeID;
                if (!RaidMealService.RegisterMeal(typeId))
                {
                    Duckov.UI.NotificationText.Push(
                        L10n.T("存档正忙，出击餐未登记；请稍后再试",
                            "Save is busy; the meal was not registered. Please try again."));
                    return;
                }
                registered = true;

                BackMountainItems.Definition def = BackMountainItems.GetDefinition(typeId);
                if (def != null)
                {
                    ModBehaviour.Instance?.ShowMessage(
                        L10n.T("吃下了", "You ate the ") + L10n.T(def.NameCN, def.NameEN)
                        + L10n.T("　下一局出击时生效", " — it takes effect on your next run"));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 出击餐使用失败: " + e.Message);
            }
            finally
            {
                if (!registered && item != null && item.Stackable)
                {
                    // 用官方 Count KV 预补，不能用会钳制 MaxStackCount 的 setter。
                    // OnFinish 同步接着执行 StackCount--，满堆和最后一份都原样保留。
                    item.SetInt("Count", item.StackCount + 1, true);
                }
            }
        }
    }
}
