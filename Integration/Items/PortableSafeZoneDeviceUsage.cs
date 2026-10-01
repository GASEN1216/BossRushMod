using Duckov.UI;
using ItemStatsSystem;

namespace BossRush
{
    public sealed class PortableSafeZoneDeviceUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = PortableSafeZoneDeviceConfig.GetUseDescription()
                };
            }
        }

        /// <summary>
        /// 只判断能不能用，不弹提示：官方背包悬停（ItemDisplay.CanUse）、右键菜单、快捷栏都会反复调它，
        /// 在这里 Push 通知会让鼠标划过物品就弹「非丧尸模式不可用」。
        /// 不可用时官方「使用」按钮置灰、快捷栏播拒绝动画（官方各使用入口都先查 IsUsable）；
        /// 进入 OnUse 后若仍被拒，由 TryUseZombieModePortableSafeZoneDevice 按
        /// GetZombieModePortableSafeZoneUnavailableReasonKey 提示原因。
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            if (item == null || item.Durability < PortableSafeZoneDeviceConfig.MAX_DURABILITY)
            {
                return false;
            }

            ModBehaviour inst = ModBehaviour.Instance;
            return inst != null && inst.IsZombieModeActive && inst.CanUseZombieModePortableSafeZoneDevice();
        }

        protected override void OnUse(Item item, object user)
        {
            ModBehaviour inst = ModBehaviour.Instance;
            bool deployed = inst != null && inst.TryUseZombieModePortableSafeZoneDevice();
            if (deployed && item != null)
            {
                item.Durability = 0f;
            }
        }
    }
}
