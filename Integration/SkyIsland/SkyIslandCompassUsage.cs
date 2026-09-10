// ============================================================================
// SkyIslandCompassUsage.cs - 风标罗盘的「用」
// ============================================================================
// 形态照 Integration/BackMountain/RaidMealUsageBehavior.cs。罗盘不消耗（耐久 999，见 SkyIslandItems），
// 读数由天空岛会话给——只有它知道本趟的信鸽落在哪、目标是哪几处；不在一趟有效的出击里就提示风标只会乱转。
// 会话组件挂在 Mod 宿主上，玩家按下使用才找一次，不在任何每帧路径上。
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>风标罗盘使用行为：在天空岛上读出信鸽或下一个目标的方位与距离。</summary>
    public class SkyIslandCompassUsage : UsageBehavior
    {
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T("使用：在晴岚群岛上指向信鸽或下一个目标（不消耗）",
                        "Use: on the Qinglan isles, points to a pigeon or your next objective (not consumed)")
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
                SkyIslandSession session = UnityEngine.Object.FindObjectOfType<SkyIslandSession>();
                if (session != null && session.UseCompass()) return;
                Duckov.UI.NotificationText.Push(L10n.T("风标只是乱转——它只认得晴岚群岛的风。",
                    "The vane just spins — it only knows the winds of the Qinglan isles."));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SkyIslandItems] 风标罗盘使用失败: " + e.Message);
            }
        }
    }
}
