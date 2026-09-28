using System;
using UnityEngine;

namespace BossRush
{
    // 本趟纪念品交付与限频重试；保留原 WorldStory owner，由共享剧情保存门面持久化。
    internal sealed partial class SkyIslandWorldStory
    {
        /// <summary>
        /// 纪念品（<see cref="SkyIslandItemRules"/>）：条件满足、手记里还没有发放记录就发一件。
        /// 先准备物品实例，再记手记，最后转移物品；资源缺失不耗掉领取资格，写屏障下也不会发出可重复卖钱的东西。
        /// 旧存档第一次进岛同样补发（航徽、噬风之核按已有旗标，罗盘按已收到的信）。
        /// </summary>
        private void GrantKeepsakes()
        {
            keepsakesPending = false;
            nextKeepsakeAttempt = Time.unscaledTime + 1f;
            SkyIslandKeepsake[] all = SkyIslandItemRules.Keepsakes;
            for (int i = 0; i < all.Length; i++)
            {
                if (!SkyIslandItemRules.Due(story.Current, all[i])) continue;
                string snapshotError;
                if (!story.BeginKeepsakeDelivery(all[i].NoteId, out snapshotError))
                {
                    keepsakesPending = true;
                    WarnKeepsakePending(snapshotError);
                    continue;
                }
                bool delivered = false, buffered = false;
                try
                {
                    string message;
                    delivered = SkyIslandItems.TryGiveWithReceipt(all[i].TypeId, all[i].ToStorage,
                        delegate { return story.RecordNote(all[i].NoteId, out message); },
                        delegate { return story.RemoveNote(all[i].NoteId); }, out buffered);
                }
                finally { story.EndKeepsakeDelivery(all[i].NoteId, delivered, buffered); }
                if (delivered) session.Announce(all[i].Caption, false);
                else { keepsakesPending = true; WarnKeepsakePending(all[i].NoteId); }
            }
            if (!keepsakesPending) keepsakeWarning = false;
        }

        private bool keepsakesPending, keepsakeWarning;
        private float nextKeepsakeAttempt;
        private void WarnKeepsakePending(string reason)
        {
            if (keepsakeWarning) return;
            keepsakeWarning = true;
            Debug.LogWarning("[SkyIsland] 纪念品发放待重试：" + (reason ?? "unknown"));
        }

    }
}
