// ============================================================================
// RespawnItemUsage.cs - 刷怪消耗品使用行为
// ============================================================================
// 模块说明：
//   实现挑衅烟雾弹和混沌引爆器的 UsageBehavior，
//   使物品能通过原版 CA_UseItem 流程正常使用。
//   物品消耗由游戏框架自动处理（CA_UseItem.OnFinish）。
// ============================================================================

using System;
using Duckov.UI;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 刷怪消耗品使用行为（挑衅烟雾弹 / 混沌引爆器）
    /// </summary>
    public class RespawnItemUsage : UsageBehavior
    {
        /// <summary>
        /// 显示设置（物品描述中显示的使用说明）
        /// </summary>
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T("使用：召唤Boss", "Use: Summon Bosses")
                };
            }
        }

        /// <summary>
        /// 检查物品是否可以使用
        /// 仅在 Mode E 激活时可用，非 Mode E 时返回 false 阻止使用
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            var inst = ModBehaviour.Instance;
            if (inst == null) return false;

            // 非 Mode E 激活状态：显示警告，返回 false 阻止使用（物品不消耗）
            if (!inst.IsModeEActive)
            {
                NotificationText.Push(L10n.T(
                    "该物品只能在划地为营模式中使用！",
                    "This item can only be used in Faction Battle mode!"
                ));
                return false;
            }

            return true;
        }

        /// <summary>
        /// 使用物品时的逻辑
        /// 根据 TypeID 调用对应的效果方法
        /// 物品消耗由游戏框架自动处理（CA_UseItem.OnFinish），这里不需要手动消耗
        /// </summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                var inst = ModBehaviour.Instance;
                if (inst == null) return;

                int typeId = item.TypeID;

                if (typeId == RespawnItemConfig.TAUNT_SMOKE_TYPE_ID)
                {
                    inst.UseTauntSmoke();
                }
                else if (typeId == RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID)
                {
                    inst.UseChaosDetonator();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[RespawnItemUsage] 使用物品出错: " + e.Message);
            }
        }
    }
}
