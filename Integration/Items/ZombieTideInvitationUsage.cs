using Duckov.UI;
using ItemStatsSystem;

namespace BossRush
{
    public class ZombieTideInvitationUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T("BossRush_ZombieMode_InvitationUseDesc")
                };
            }
        }

        public override bool CanBeUsed(Item item, object user)
        {
            string failureReason;
            if (!ZombieModeMapSelectionHelper.CanOpenZombieModeMapSelection(out failureReason))
            {
                if (!string.IsNullOrEmpty(failureReason))
                {
                    NotificationText.Push(failureReason);
                }
                return false;
            }

            return true;
        }

        protected override void OnUse(Item item, object user)
        {
            string failureReason;
            if (!ZombieModeMapSelectionHelper.ShowZombieModeMapSelection(out failureReason) && !string.IsNullOrEmpty(failureReason))
            {
                NotificationText.Push(failureReason);
            }
        }
    }
}
