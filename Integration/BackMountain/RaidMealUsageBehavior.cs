// ============================================================================
// RaidMealUsageBehavior.cs - 后山收成的「吃」
// ============================================================================
// 形态照 Integration/Items/BrickStoneUsage.cs。
//
// 三种收成都立即变身为对应 Boss。RaidMealService 仅保留旧档已预备餐食兑现。
//
// CA_UseItem.OnFinish 无条件扣数量。失败时预补一份，抵消紧随其后的官方扣减。
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>后山收成使用行为：立即开始 30 秒对应 Boss 变身。</summary>
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
                    description = L10n.T("食用：化身对应 Boss 30 秒", "Eat: take the matching Boss form for 30s")
                };
            }
        }

        /// <summary>三种产物即时变身；种子不可直接吃。</summary>
        public override bool CanBeUsed(Item item, object user)
        {
            try
            {
                if (item == null) return false;
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner == null || !owner.IsBackMountainConfiguredEnabled()) return false;
                BackMountainItems.Definition def = BackMountainItems.GetDefinition(item.TypeID);
                // 三种收成共用变身门控，已有形态时不可叠加，种子不可直接吃。
                if (def == null || def.IsSeed) return false;
                if (item.TypeID == BossRushItemIds.DragonFruit
                    || item.TypeID == BossRushItemIds.EmberChili
                    || item.TypeID == BossRushItemIds.PhantomMushroom)
                    return BackMountainBossMorphService.CanUse;
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>开始对应形态。物品消耗交给框架，这里不手动扣。</summary>
        protected override void OnUse(Item item, object user)
        {
            bool registered = false;
            try
            {
                if (item == null) return;

                ModBehaviour owner = ModBehaviour.Instance;
                registered = owner != null && owner.IsBackMountainConfiguredEnabled()
                    && BackMountainBossMorphService.TryBegin(item.TypeID, owner);
                if (!registered)
                    Duckov.UI.NotificationText.Push(
                        L10n.T("当前无法变身；收成未消耗", "You cannot morph right now; the harvest was not consumed."));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 后山变身使用失败: " + e.Message);
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
