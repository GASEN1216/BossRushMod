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
// 【物品消耗由框架处理】
//   CA_UseItem.OnFinish 会自动扣数量，这里不要手动消耗——手动扣会扣两次。
// ============================================================================

using System;
using ItemStatsSystem;

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
            try
            {
                if (item == null) return;

                int typeId = item.TypeID;
                if (!RaidMealService.RegisterMeal(typeId)) return;

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
        }
    }
}
