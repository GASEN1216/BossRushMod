// ============================================================================
// SkyIslandWorldStoryEcho.cs - 鸣风栈道双航标门装置上的「引风」（噬风·回响的选项接线）
// ============================================================================
// 从 SkyIslandWorldStory.cs 拆出来单独放（主文件离 1200 行预算只剩几十行）：主文件只在 Search_E 那一格多一句调用。
//
// 纪律（同 StormChoice / AddIf）：
// - **先判断再挂**：能不能挂与点下去会不会被拒共用 SkyIslandSession.CanSummonStormEcho → SkyIslandStoryRules.CanSummonStormEcho；
// - 不挂灰项：挂不出来时把「还差什么」收进正文（Hint）；没打过噬风（首战选项还挂着）与这一趟已经引过时什么都不留；
// - 开战后面板收起，回执改走字幕。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class SkyIslandWorldStory
    {
        private void StormEchoChoice(List<SkyIslandStoryPresentation.Choice> choices)
        {
            string blocker;
            if (!session.CanSummonStormEcho(out blocker))
            {
                Hint(blocker);
                return;
            }
            choices.Add(new SkyIslandStoryPresentation.Choice(SkyIslandStormEchoRules.ChoiceLabel, delegate
            {
                string message;
                if (!session.TryBeginStormEcho(out message)) return message;
                presentation.Dispose();
                session.Announce(message, false);
                story.LogTiming("challenge", SkyIslandStormEchoRules.EncounterId);
                return message;
            }));
        }

        /// <summary>
        /// 罗盘的可选目标：与地图圈同一份 <see cref="SkyIslandMapMarkers.SideTargets"/>。双航标门只因「引风」被圈时
        /// （噬风打过、K3 也开了），要这一趟真引得了（核在包里、风晶够、还没引过）才指它；
        /// 否则罗盘会一直停在栈道上，再也指不到缺灯的地方与风晶簇。
        /// </summary>
        private IEnumerable<string> CompassSideTargets()
        {
            SkyIslandStoryData data = story.Current;
            string blocker;
            bool skipGate = data.StormResolved && data.Has(SkyIslandStoryFlag.ShortcutK3) && !session.CanSummonStormEcho(out blocker);
            foreach (string name in SkyIslandMapMarkers.SideTargets(data))
                if (!skipGate || name != "Search_E") yield return name;
        }

        /// <summary>罗盘指双航标门时报的话；结局后说清在那儿烧风晶（玩家反馈 2026-10-17「双航标门在哪里，风晶哪里烧」）。</summary>
        private string GateCompassTask()
        {
            // 不说「木牌」：Search_E 旁的小木牌只写地名，写通行状态的门牌在 K3 桥头与栈道北头的钟庭门两侧，叫「木牌」会把人引错。
            return SkyIslandStormEchoRules.UnlockedBySave(story.Current)
                ? L10n.T("鸣风栈道南头的双航标门，在那儿烧风晶引风", "the twin-beacon gate at the south end of Windsong Boardwalk, where you burn the windcrystal")
                : L10n.T("鸣风栈道南头的双航标门", "the twin-beacon gate at the south end of Windsong Boardwalk");
        }
    }
}
