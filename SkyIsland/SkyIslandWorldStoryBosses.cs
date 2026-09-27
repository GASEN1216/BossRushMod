// ============================================================================
// SkyIslandWorldStoryBosses.cs - 头目 / 岛主倒下之后的剧情接线（R1：残星匠首、瞭台观星手）
// ============================================================================
// 从 SkyIslandWorldStory.cs 拆出来单独放：主文件只在构造与 Dispose 各多一句。
//
// 纪律：
// - 订阅静态事件 SkyIslandBossForge.Defeated 要幂等、可退订（根 AGENTS §4.6），退订在 Dispose；
//   模块销毁时 SkyIslandBossForge.ResetStaticCaches 再兜一次。
// - 首杀只记一次手记（discoveredNotes 的 Lord_ / Chief_ 条目；剧情旗标 16 位已满，走见闻数组）。
//   之后每趟照样刷、照样掉专属装备，只是不再播首杀字幕。
// - 掉落与手记无关：掉落在官方建箱前由 SkyIslandBossLoot 定下，这里记不上手记也不补发任何东西。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandWorldStory
    {
        private bool bossEventsAttached, mailbagLetterUsed;

        /// <summary>
        /// 原口径（有前置的信同趟连送，SkyIslandLetters.NextSameRaidFor）不给下一封时的补位：主角背着截信人的旧邮包、
        /// 这一趟还没用过，就再放一封无前置的信（每趟一次，头目 R2）。背包由局内 owner 按采样节拍读（SkyIslandBossGearWorn）。
        /// </summary>
        private SkyIslandLetter MailbagLetterThisRaid()
        {
            if (story == null || mailbagLetterUsed || !SkyIslandBossGearWorn.Mailbag) return null;
            SkyIslandLetter next = SkyIslandLetters.NextSameRaidFor(story.Current, true);
            if (next == null) return null;
            mailbagLetterUsed = true;
            session.Announce(L10n.T("旧邮包里还压着一封没送出去的信：信鸽会再飞一趟。",
                "One more undelivered letter is still pressed inside the old mailbag: the pigeon will fly again."), false);
            return next;
        }

        private void AttachBossEvents()
        {
            if (bossEventsAttached) return;
            SkyIslandBossForge.Defeated += OnBossDefeated;
            bossEventsAttached = true;
        }

        private void DetachBossEvents()
        {
            if (!bossEventsAttached) return;
            SkyIslandBossForge.Defeated -= OnBossDefeated;
            bossEventsAttached = false;
        }

        private void OnBossDefeated(SkyIslandBossProfile profile, Vector3 position)
        {
            if (disposed || profile == null || story == null) return;
            if (SkyIslandBossRules.Defeated(story.Current, profile)) return;
            string message;
            if (!story.RecordNote(profile.NoteId, out message))
            {
                Debug.LogWarning("[SkyIslandBoss] 首杀手记没记上：" + profile.NoteId + " " + message);
                return;
            }
            story.LogTiming("boss", profile.Id);
            session.Announce(SkyIslandBossRules.FirstKillCaption(profile), false);
        }
    }
}
