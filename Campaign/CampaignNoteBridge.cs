// ============================================================================
// CampaignNoteBridge.cs - 线索接入官方笔记图鉴
// ============================================================================
// 线索碎片走官方 NoteIndex 而不是 mod 自有的 WikiContent：
// WikiContent 是无解锁状态的静态知识库，而线索的意义正在于「逐步揭露」，
// 官方笔记图鉴自带解锁状态、按槽持久化和现成 UI，是天然合适的载体。
//
// 【本次调查的关键发现，务必别踩】
//   官方 `NoteIndex.SetNoteDynamic(note)` 只调用 `MSetEntryDynamic`，
//   而后者只写查询字典 `MDic`，**不写 `notes` 列表**。
//   而图鉴界面列条目走的是 `GetAllNotes()` → 遍历 `notes` 列表。
//   所以只调 SetNoteDynamic 的话：按 key 查得到，界面里一条也看不见。
//   正确做法是**两边都写**——加进 Notes 列表让界面能列，
//   再 SetNoteDynamic 保证字典查询也命中（RebuildDic 只在 _dic 为 null 时跑，
//   直接往列表里加不会自动刷新字典）。
//
// 【fail-open】NoteIndex 在基地之外可能为 null，注册失败一律静默跳过：
// 线索的权威副本在战役存档里；NoteIndex 暂不可用时由后续基地刷新重试注册。
// 官方图鉴接入失败不阻塞契约交付，但不能假称公告板另有线索页签。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.NoteIndexs;

namespace BossRush
{
    /// <summary>线索 → 官方笔记图鉴的桥。</summary>
    internal static class CampaignNoteBridge
    {
        #region 状态

        /// <summary>本会话已注册进官方图鉴的 note key，避免重复注册。</summary>
        private static readonly HashSet<string> _registeredKeys = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        /// <summary>线索 ID → 官方 note key。</summary>
        internal static string BuildNoteKey(string clueId)
        {
            if (string.IsNullOrEmpty(clueId)) return string.Empty;
            return CampaignTuning.NoteKeyPrefix + clueId;
        }

        /// <summary>
        /// 幂等注册全部线索条目到官方图鉴，并把已解锁的标成已解锁。
        /// 场景重载后 NoteIndex 实例会换，因此每次进基地都该调一次（注册本身幂等）。
        /// </summary>
        internal static void EnsureNotesRegistered()
        {
            try
            {
                NoteIndex index = NoteIndex.Instance;
                if (index == null) return;

                IList<CampaignChapterDef> chapters = CampaignContentCatalog.Chapters;
                if (chapters == null) return;

                for (int i = 0; i < chapters.Count; i++)
                {
                    CampaignChapterDef def = chapters[i];
                    if (def == null || string.IsNullOrEmpty(def.ClueId)) continue;
                    RegisterNote(index, def);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 线索注册失败: " + e.Message);
            }
        }

        private static void RegisterNote(NoteIndex index, CampaignChapterDef def)
        {
            try
            {
                string key = BuildNoteKey(def.ClueId);
                if (string.IsNullOrEmpty(key)) return;

                List<Note> notes = index.Notes;
                if (notes == null) return;

                // 场景重载后官方实例会重建，_registeredKeys 里的记录就过期了——
                // 因此判重必须看真实列表，不能只信本地集合。
                bool present = false;
                for (int i = 0; i < notes.Count; i++)
                {
                    Note existing = notes[i];
                    if (existing == null) continue;
                    if (string.Equals(existing.key, key, StringComparison.Ordinal))
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    Note note = new Note();
                    note.key = key;
                    note.image = CampaignAssetCache.GetChapterPoster(def.Order);
                    note.hide = false;

                    // 两边都写：列表决定界面能不能列出来，字典决定按 key 查得到
                    notes.Add(note);
                    NoteIndex.SetNoteDynamic(note);
                    _registeredKeys.Add(key);
                }

                // 已在战役存档里解锁的线索，同步标进官方解锁集（官方按槽自持久化）
                if (CampaignProgressService.IsClueUnlocked(def.ClueId)
                    && !NoteIndex.GetNoteUnlocked(key))
                {
                    NoteIndex.SetNoteUnlocked(key);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 单条线索注册失败 "
                    + def.ClueId + ": " + e.Message);
            }
        }

        /// <summary>
        /// 解锁一条线索。战役存档是权威副本（由 ProgressService 在交付时写入），
        /// 这里只做官方图鉴侧的镜像 + 玩家提示。
        /// </summary>
        internal static void UnlockClue(string clueId)
        {
            if (string.IsNullOrEmpty(clueId)) return;
            try
            {
                EnsureNotesRegistered();

                string key = BuildNoteKey(clueId);
                if (!string.IsNullOrEmpty(key) && !NoteIndex.GetNoteUnlocked(key))
                {
                    NoteIndex.SetNoteUnlocked(key);
                }

                ModBehaviour.Instance?.ShowMessage(
                    L10n.T("已获得新线索，可在笔记中查看", "New clue acquired — check your notes"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 线索解锁失败 "
                    + clueId + ": " + e.Message);
            }
        }

        internal static void ResetStaticCaches()
        {
            _registeredKeys.Clear();
        }
    }
}
