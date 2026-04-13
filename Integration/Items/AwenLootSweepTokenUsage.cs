using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 阿稳扫箱令使用行为。
    /// </summary>
    public class AwenLootSweepTokenUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = AwenLootSweepTokenConfig.GetUseDescription()
                };
            }
        }

        public override bool CanBeUsed(Item item, object user)
        {
            ModBehaviour mod = ModBehaviour.Instance;
            CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
            return mod != null && mod.CanUseAwenLootSweepToken(player, false);
        }

        protected override void OnUse(Item item, object user)
        {
            try
            {
                ModBehaviour mod = ModBehaviour.Instance;
                CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
                if (mod == null || player == null)
                {
                    return;
                }

                if (!mod.CanUseAwenLootSweepToken(player, false))
                {
                    mod.CanUseAwenLootSweepToken(player, true);
                    mod.TryRefundAwenLootSweepToken();
                    return;
                }

                if (!mod.TryActivateAwenLootSweepToken(player))
                {
                    mod.TryRefundAwenLootSweepToken();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AwenLootSweepToken] OnUse failed: " + e.Message);
            }
        }
    }
}
