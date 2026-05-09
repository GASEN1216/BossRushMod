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

        public override bool CanBeUsed(Item item, object user)
        {
            ZombieTideBeaconConfig.EnsureReusableInstance(item);

            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsZombieModeActive)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_BeaconNotZombieMode"));
                return false;
            }

            if (!inst.CanUseZombieModeBeacon())
            {
                NotificationText.Push(L10n.T(inst.GetZombieModeBeaconUnavailableReasonKey()));
                return false;
            }

            return true;
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
