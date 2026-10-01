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

        /// <summary>
        /// 只判断能不能用，不弹提示（理由同 ZombieTideBeaconUsage.CanBeUsed：官方悬停 / 菜单 / 快捷栏会反复调它）。
        /// 进入 OnUse 后若仍被拒，由 ShowZombieModeMapSelection 的失败原因提示。
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            string failureReason;
            return ZombieModeMapSelectionHelper.CanOpenZombieModeMapSelection(out failureReason);
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
