using System;
using ItemStatsSystem;

namespace BossRush
{
    public class CalmingDropsUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = CalmingDropsConfig.GetUseDescription()
                };
            }
        }

        public override bool CanBeUsed(Item item, object user)
        {
            CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
            return NurseHealingService.HasDebuffs(player);
        }

        protected override void OnUse(Item item, object user)
        {
            try
            {
                CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
                int cleared = NurseHealingService.ClearAllDebuffs(player);
                ModBehaviour.DevLog("[CalmingDropsUsage] Cleared debuffs: " + cleared);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CalmingDropsUsage] OnUse failed: " + e.Message);
            }
        }
    }
}
