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
                    // 与实际读数（SkyIslandWorldStory.CompassReading）和物品描述同一份口径：
                    // 蛙卵 → 信鸽 → 当前目标 / 支线 → 缺灯处（带着晴岚风晶时）→ 还没采的风晶簇。
                    // 按住物品看到的这句是玩家最先读到的说明，落后于行为就是在骗人。
                    description = L10n.T(
                        "使用：找群岛上的下一个目标，不消耗。\n捧着蛙卵时先指蛙鸣池，其余依次是信鸽、主线、支线。\n带着风晶还能找没点亮的灯，最后找这趟还没采的风晶簇。",
                        "Use: points to your next stop on the isles. Not consumed.\nCarrying frogspawn, it points to Frogsong Pool first. After that: pigeons, main objectives, side paths.\nWith a windcrystal it also finds unlit lamps, and last the clusters you haven't gathered this raid.")
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
                Duckov.UI.NotificationText.Push(L10n.T("风标只是乱转，它只认得晴岚群岛的风。",
                    "The vane just spins. It only knows the winds of the Qinglan isles."));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SkyIslandItems] 风标罗盘使用失败: " + e.Message);
            }
        }
    }
}
