// ============================================================================
// TidegateLetterUsage.cs - 闸伯的信的「读」
// ============================================================================
// 形态照 Integration/SkyIsland/SkyIslandCompassUsage.cs。信不消耗（耐久 999，见 TidegateItems），
// 读信面板与解锁竹筏航线都在 Tidegate/TidegateStory.ReadLetter（潮闸镇运行时的 owner），这里只在玩家按下使用时转过去，
// 不在任何每帧路径上。
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>闸伯的信使用行为：打开读信面板，不消耗。</summary>
    public class TidegateLetterUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T("使用：读信，信不会用掉。", "Use: read the letter. It isn't used up.")
                };
            }
        }

        public override bool CanBeUsed(Item item, object user)
        {
            return item != null;
        }

        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 只认主角色：别的角色（官方 AI 偶尔会试用背包里的东西）用了不开面板。
                CharacterMainControl character = user as CharacterMainControl;
                if (character != null && character != CharacterMainControl.Main) return;
                TidegateStory.ReadLetter();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[TidegateItems] 读信失败: " + e.Message);
            }
        }
    }
}
