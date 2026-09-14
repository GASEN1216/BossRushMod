// ============================================================================
// SkyIslandStormEchoRules.cs - 噬风·回响：结局之后每趟能在鸣风栈道引一次的噬风（纯规则）
// ============================================================================
// 缺口（2026-09-14 B 轮逐条核实）：
// - 噬风是岛上唯一的 Boss。StormResolved 之后 SkyIslandSession.BeginStoryChallenge 直接拒绝，结局后再也见不到它；
// - 十盏灯点完之后，晴岚风晶只剩风晶灭蚊灯一个去处；
// - 敲钟之后目标卡只剩「补齐支线」，没有值得反复打的战斗目标。
//
// 做法：敲过钟、打过噬风之后，鸣风栈道的双航标门装置上挂出「引风」：背包里带着噬风之核（不消耗），烧一块晴岚风晶，
// 噬风的回响就带两名断风游猎回到栈道。每趟出击最多一次，按本趟计、不进存档。
//
// 分工：
// - 能不能挂 / 点下去会不会被拒：SkyIslandStoryRules.CanSummonStormEcho（同一份判据）；
// - 本文件：遭遇 id、解锁口径、会话与面板用的回话；
// - 奖励表：SkyIslandStormEchoReward（依赖物品表，单独放，免得遭遇夹具连带链接整张物品表）；
// - 编排：SkyIslandStormBoss 的回响模式（风眼钉在原地、每档多响一声，相位与脉冲常量与首战共用）；
// - 会话接线：SkyIslandSessionEcho.cs；选项接线：SkyIslandWorldStoryEcho.cs。
//
// 纯逻辑、只依赖 System 与剧情存档类型，隔离回归直接链接。
// ============================================================================

using System;

namespace BossRush
{
    internal static class SkyIslandStormEchoRules
    {
        /// <summary>
        /// 回响的遭遇 id。**刻意不沿用 "Storm"**：遭遇 owner 对手动组的一次性判断读 EncounterWasSaved("Storm")，
        /// 打过噬风之后它恒为真，沿用这个 id 会被直接当成已清场。回响的清场也不写进存档的 clearedEncounters（会话按本趟记）。
        /// </summary>
        internal const string EncounterId = "StormEcho";

        /// <summary>每趟出击最多引几次风。按本趟计、不进存档：离岛再出击就是新的一趟。</summary>
        internal const int MaxPerRaid = 1;

        internal static bool IsEcho(string id)
        {
            return string.Equals(id, EncounterId, StringComparison.Ordinal);
        }

        /// <summary>
        /// 这份存档有没有资格引风：敲过钟、打过噬风。钟守与名册的回应台词只认这一条；
        /// 选项的完整判据（再加这一趟没引过、核在背包里、风晶够）在 <see cref="SkyIslandStoryRules.CanSummonStormEcho"/>。
        /// </summary>
        internal static bool UnlockedBySave(SkyIslandStoryData data)
        {
            return data != null && data.Has(SkyIslandStoryFlag.Ending) && data.StormResolved;
        }

        /// <summary>双航标门装置上的选项。</summary>
        internal static string ChoiceLabel
        {
            get
            {
                return L10n.T("引风 · 烧一块晴岚风晶，唤回噬风的回响",
                    "Call the wind · burn a Qinglan Windcrystal for the Windeater's echo");
            }
        }

        /// <summary>引风成功（面板随即收起，这句走字幕）。</summary>
        internal static string Opened
        {
            get
            {
                return L10n.T("风晶在装置里烧化了。云海那头，那阵风循着味道回来了。",
                    "The windcrystal burns away in the device. Out on the cloud sea, that wind follows the scent back.");
            }
        }

        internal static string SpentThisRaid
        {
            get { return L10n.T("这一趟已经引过一次风了，下次出击再来。", "You have already called the wind this raid. Come back next trip."); }
        }

        internal static string NotReady
        {
            get { return L10n.T("群岛还没就绪，风引不起来。", "The isles are not ready yet; the wind will not come."); }
        }

        internal static string ReserveFailed
        {
            get { return L10n.T("风晶在操作时发生了变化，再试一次。", "The windcrystal changed while you were using it. Try again."); }
        }

        internal static string StartFailed
        {
            get
            {
                return L10n.T("风没引起来：请站到鸣风栈道上，并等上一场战斗结束。风晶还在背包里。",
                    "The wind did not come: stand on Windsong Boardwalk and wait for the previous fight to end. The windcrystal is still in your pack.");
            }
        }
    }
}
