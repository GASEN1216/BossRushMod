using System;
using System.Collections.Generic;
using Duckov.NoteIndexs;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的信、四页闸工日志与收尾 → 官方笔记图鉴的镜像（口径照 `SkyIslandNoteBridge` / `CampaignNoteBridge`）：
    /// 我们的剧情存档（<see cref="TidegateStory"/>）是唯一权威，这里只往官方 `NoteIndex` 写；
    /// 官方 `SetNoteDynamic` 只写查询字典，所以先 `Notes.Add` 再 `SetNoteDynamic`；标题与正文走本地化注入（`Note_{key}_Title/_Content`）。
    /// 拿不到 `NoteIndex.Instance` 时静默跳过，绝不挡剧情。
    /// </summary>
    internal static class TidegateNoteBridge
    {
        internal const string NoteKeyPrefix = "BossRushTidegate_";
        internal const string LetterId = "Letter";
        internal const string EndingId = "Ending";
        private const string LogPrefix = "[TidegateNote] ";
        private static bool syncPending = true, subscribed, warned;

        internal static string PageId(int index) { return "Page" + (index + 1); }

        private static IEnumerable<string> AllIds()
        {
            yield return LetterId;
            for (int i = 0; i < TidegateStory.PageCount; i++) yield return PageId(i);
            yield return EndingId;
        }

        private static string Key(string id) { return NoteKeyPrefix + id; }

        private static string Title(string id)
        {
            if (id == LetterId) return TidegateText.LetterTitle;
            if (id == EndingId) return TidegateText.EndingTitle;
            return TidegateText.PageTitle(PageIndex(id));
        }

        private static string Content(string id)
        {
            if (id == LetterId) return TidegateText.LetterBody;
            if (id == EndingId) return TidegateText.EndingBody;
            return TidegateText.PageBody(PageIndex(id));
        }

        private static int PageIndex(string id)
        {
            int number;
            return id != null && id.StartsWith("Page", StringComparison.Ordinal) && int.TryParse(id.Substring(4), out number) ? number - 1 : -1;
        }

        /// <summary>按存档判断这一条该不该点亮。</summary>
        private static bool Recorded(TidegateStoryData data, string id)
        {
            if (data == null) return false;
            if (id == LetterId) return data.LetterRead;
            if (id == EndingId) return data.WatchDelivered;
            int index = PageIndex(id);
            return index >= 0 && (data.Pages & (1 << index)) != 0;
        }

        /// <summary>启动与切语言时注入标题与正文（由 <see cref="TidegateRaidScene.InjectLocalization"/> 调）。</summary>
        internal static void InjectNoteKeys()
        {
            try
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string id in AllIds())
                {
                    map["Note_" + Key(id) + "_Title"] = Title(id);
                    map["Note_" + Key(id) + "_Content"] = Content(id);
                }
                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] 图鉴文案注入失败: " + e.Message); }
        }

        internal static void EnsureRuntime()
        {
            if (subscribed) return;
            SavesSystem.OnSetFile += RequestSync;
            SavesSystem.OnSaveDeleted += RequestSync;
            subscribed = true;
        }

        internal static void RequestSync() { syncPending = true; }

        /// <summary>
        /// 模块每帧调；只在需要同步时做事，平时 O(1)。剧情存档读不了（写屏障、解码失败、换槽中）时
        /// 读到的是全 false 的默认值，拿它对齐会把官方图鉴里的条目全收回，所以等存档恢复再同步，镜像保留原状态（口径同天空岛）。
        /// </summary>
        internal static void Tick()
        {
            if (!syncPending || !TidegateStory.Writable) return;
            EnsureRegistered(TidegateStory.Current);
        }

        /// <summary>幂等注册全部条目，并按存档把点亮状态双向对齐（我们的存档里没有、官方却点亮着就收回）。</summary>
        internal static bool EnsureRegistered(TidegateStoryData data)
        {
            try
            {
                NoteIndex index = NoteIndex.Instance;
                if (index == null || index.Notes == null || data == null) return false;
                List<Note> notes = index.Notes;
                foreach (string id in AllIds())
                {
                    string key = Key(id);
                    Note note = null;
                    for (int n = 0; n < notes.Count; n++)
                        if (notes[n] != null && string.Equals(notes[n].key, key, StringComparison.Ordinal)) { note = notes[n]; break; }
                    if (note == null)
                    {
                        note = new Note { key = key, image = null, hide = false };
                        notes.Add(note);
                    }
                    NoteIndex.SetNoteDynamic(note);
                    bool ours = Recorded(data, id), theirs = NoteIndex.GetNoteUnlocked(key);
                    if (ours && !theirs) NoteIndex.SetNoteUnlocked(key);
                    else if (!ours && theirs) Relock(index, key);
                }
                syncPending = false;
                warned = false;
                return true;
            }
            catch (Exception e)
            {
                syncPending = true;
                if (!warned) ModBehaviour.DevLog(LogPrefix + "[WARNING] 图鉴注册失败: " + e.Message);
                warned = true;
                return false;
            }
        }

        /// <summary>刚记进存档的一条：把官方图鉴那边点亮（权威副本已经写好）。</summary>
        internal static void Unlock(string id)
        {
            try
            {
                if (NoteIndex.Instance == null) { RequestSync(); return; }
                string key = Key(id);
                EnsureRegistered(TidegateStory.Current);
                if (!NoteIndex.GetNoteUnlocked(key)) NoteIndex.SetNoteUnlocked(key);
            }
            catch (Exception e)
            {
                RequestSync();
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 图鉴点亮失败 " + id + ": " + e.Message);
            }
        }

        private static void Relock(NoteIndex index, string key)
        {
            HashSet<string> unlocked = index.UnlockedNotes;
            if (unlocked == null || !unlocked.Remove(key)) return;
            Action<string> changed = NoteIndex.onNoteStatusChanged;
            if (changed != null) changed(key);
        }

        internal static void ResetStaticCaches()
        {
            if (subscribed)
            {
                SavesSystem.OnSetFile -= RequestSync;
                SavesSystem.OnSaveDeleted -= RequestSync;
                subscribed = false;
            }
            syncPending = true;
            warned = false;
        }
    }
}
