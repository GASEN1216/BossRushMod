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
    }
}
