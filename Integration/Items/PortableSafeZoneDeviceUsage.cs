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

        public override bool CanBeUsed(Item item, object user)
        {
            if (item == null || item.Durability < PortableSafeZoneDeviceConfig.MAX_DURABILITY)
            {
                return false;
            }

            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsZombieModeActive)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_PortableSafeZoneNotZombieMode"));
                return false;
            }

            if (!inst.CanUseZombieModePortableSafeZoneDevice())
            {
                NotificationText.Push(L10n.T(inst.GetZombieModePortableSafeZoneUnavailableReasonKey()));
                return false;
            }

            return true;
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
