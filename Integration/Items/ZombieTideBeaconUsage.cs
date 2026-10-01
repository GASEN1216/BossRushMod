using Duckov.UI;
using ItemStatsSystem;

namespace BossRush
{
    public class ZombieTideBeaconUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T(ZombieTideBeaconConfig.USE_DESC_CN, ZombieTideBeaconConfig.USE_DESC_EN)
                };
            }
        }

        /// <summary>
        /// 只判断能不能用，不弹提示：官方背包悬停（ItemDisplay.CanUse）、右键菜单、快捷栏都会反复调它，
        /// 在这里 Push 通知会让鼠标划过物品就弹「非丧尸模式不可用」。
        /// 不可用时官方「使用」按钮置灰、快捷栏播拒绝动画（官方各使用入口都先查 IsUsable）；
        /// 进入 OnUse 后若仍被拒，由 TryUseZombieModeBeacon 按 GetZombieModeBeaconUnavailableReasonKey 提示原因。
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            ZombieTideBeaconConfig.EnsureReusableInstance(item);

            ModBehaviour inst = ModBehaviour.Instance;
            return inst != null && inst.IsZombieModeActive && inst.CanUseZombieModeBeacon();
        }

        protected override void OnUse(Item item, object user)
        {
            ZombieTideBeaconConfig.EnsureReusableInstance(item);

            ModBehaviour inst = ModBehaviour.Instance;
            if (inst != null)
            {
                inst.TryUseZombieModeBeacon();
            }
        }
    }
}
