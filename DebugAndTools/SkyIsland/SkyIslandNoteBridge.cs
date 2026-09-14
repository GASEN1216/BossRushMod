// ============================================================================
// SkyIslandNoteBridge.cs - 20 处见闻接进官方笔记图鉴
// ============================================================================
// 【为什么要接】同一个仓库里本来就有现成范例：`Campaign/CampaignNoteBridge.cs` 把征程线索
//   接进了官方 `Duckov.NoteIndexs.NoteIndex`，而天空岛却另建了一套手记
//   （`SkyIslandJournal` 的四个见闻章节）。同域两套图鉴是全项目最站不住脚的一处重复：
//   - 官方图鉴**自带解锁状态、按槽持久化与现成 UI**，回基地也翻得到；
//   - 自建手记只在岛上的剧情面板里翻得到，一趟结束就看不见了；
//   - 手记里 20 行 `□ …（尚未收录）` 的空占位，正是「选项/正文一上来就一大堆」的来源之一。
//
// 【口径：我们的存档是权威，官方图鉴只做镜像】
//   与征程完全一致。见闻是否收录的唯一事实源仍是 `SkyIslandStoryData.discoveredNotes`
//   （`SkyIslandJournal.Recorded`）；这里只把它同步到官方那边点亮。
//   反过来读官方图鉴当事实源是不行的——它是官方存档，我们不拥有它。
//
// 【官方 API 的两个坑，抄自征程踩过的】
//   1. `NoteIndex.SetNoteDynamic(note)` 只写查询字典，**不写 `notes` 列表**，
//      而图鉴界面列条目走的是 `GetAllNotes()` → 遍历 `notes`。
//      所以必须**两边都写**：先 `Notes.Add`，再 `SetNoteDynamic`。
//   2. `Note.titleKey` / `contentKey` 是**只读派生属性**（`"Note_" + key + "_Title"`），
//      赋值无效。文案必须走本地化注入，见 `InjectNoteKeys`。
//
// 【为什么条目不带图】`Note.image` 指向的 Sprite 必须活得比 NoteIndex 久，而天空岛的场景插图
//   由 `SkyIslandUiArt` 持有、在 `SkyIslandRuntimeModule.OnDestroy` 里**显式销毁**。
//   把它塞进 Note，出击一结束官方图鉴里留的就是一个已销毁的 Sprite 引用（回基地翻图鉴看到破图）。
//   另外 12 张区域横幅全读进来约 13.5 MB，为一个主要在基地翻的图鉴前载这些也不划算。
//   所以见闻条目是纯文本的——而这正好是它的价值：**长文案从面板搬到这里**（见 §文案分层）。
//
// 【fail-open】`NoteIndex.Instance` 走 `GameManager.Instance.noteIndex`，是全局单例，
//   出击中也拿得到；但拿不到时一律静默跳过，绝不能让图鉴注册挡住剧情或撤离。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.NoteIndexs;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>天空岛见闻 → 官方笔记图鉴的桥。全静态，只读我们的存档、只写官方图鉴。</summary>
    internal static class SkyIslandNoteBridge
    {
        /// <summary>
        /// 见闻在官方 NoteIndex 里的键前缀。官方按 `Note_{key}_Title/_Content` 取本地化，
        /// 因此这个前缀同时进本地化键，**双重冻结**（口径同 `CampaignTuning.NoteKeyPrefix`）。
        /// </summary>
        internal const string NoteKeyPrefix = "BossRushSkyIsland_";

        private const string LogPrefix = "[SkyIslandNote] ";

        /// <summary>本会话已注册过的 note key。场景重载后官方实例会换，所以判重仍以真实列表为准。</summary>
        private static readonly HashSet<string> registered = new HashSet<string>(StringComparer.Ordinal);

        private static bool runtimeSubscribed, syncPending = true, syncWarning;
        private static float nextSync;
        private static NoteIndex mirroredIndex;

        internal static void EnsureRuntime()
        {
            if (runtimeSubscribed) return;
            SavesSystem.OnSetFile += RequestSync;
            SavesSystem.OnSaveDeleted += RequestSync;
            runtimeSubscribed = true;
            RequestSync();
        }

        internal static void RequestSync()
        {
            syncPending = true;
            syncWarning = false;
            nextSync = 0f;
        }

        // 读档事件只置脏；延到下一帧，等官方 NoteIndex 完成同一个事件里的槽位切换。
        // 岛上优先镜像尚未落盘的当前剧情；基地只读缓存，不初始化或改写剧情存档。
        internal static void Tick(SkyIslandStoryData current = null)
        {
            if (Time.unscaledTime < nextSync) return;
            try
            {
                NoteIndex index = NoteIndex.Instance;
                if (!syncPending && ReferenceEquals(index, mirroredIndex)) return;
                nextSync = Time.unscaledTime + 1f;
                if (index == null || SavesSystem.IsSaving) return;
                SkyIslandStoryData data = current;
                if (data == null)
                {
                    data = SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey)
                        ? SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey))
                        : SkyIslandStoryRules.CreateDefault();
                    if (data == null) throw new InvalidOperationException("群岛记录无法解码，图鉴镜像保留原状态");
                }
                EnsureRegistered(data);
            }
            catch (Exception e)
            {
                nextSync = Time.unscaledTime + 1f;
                if (!syncWarning) ModBehaviour.DevLog(LogPrefix + "[WARNING] 图鉴同步等待重试: " + e.Message);
                syncWarning = true;
            }
        }

        /// <summary>见闻 id（`Search_A` 一类）→ 官方 note key。</summary>
        internal static string BuildNoteKey(string searchId)
        {
            return string.IsNullOrEmpty(searchId) ? string.Empty : NoteKeyPrefix + searchId;
        }

        /// <summary>
        /// 注入 20 条见闻在官方图鉴里的标题与正文。
        /// 官方按 `Note_{key}_Title` / `Note_{key}_Content` 查表，缺了会显示裸 key。
        ///
        /// 标题与正文的唯一来源仍是 `SkyIslandPointText.Name / Lore`——
        /// **不在这里写第二份文案**，否则图鉴与岛上的说法会分叉。
        /// 启动时调一次（`BossRushIntegration_StartAndScene`），与其它模块的注入同一处。
        /// </summary>
        internal static void InjectNoteKeys()
        {
            try
            {
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
                string[][] chapters = SkyIslandJournal.Chapters;
                for (int c = 0; c < chapters.Length; c++)
                {
                    for (int i = 0; i < chapters[c].Length; i++)
                    {
                        string id = chapters[c][i];
                        string key = BuildNoteKey(id);
                        if (string.IsNullOrEmpty(key)) continue;
                        map["Note_" + key + "_Title"] = SkyIslandPointText.Name(id);
                        map["Note_" + key + "_Content"] = SkyIslandPointText.Lore(id);
                    }
                }
                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 见闻图鉴文案注入失败: " + e.Message);
            }
        }

        /// <summary>
        /// 幂等注册 20 条见闻条目，并把**存档里已经收录的**同步成已解锁。
        /// 启动、读档、换槽和官方索引重建后由 runtime 重试；进岛时镜像当前内存进度。
        /// </summary>
        internal static bool EnsureRegistered(SkyIslandStoryData data)
        {
            try
            {
                NoteIndex index = NoteIndex.Instance;
                if (index == null) return false;
                List<Note> notes = index.Notes;
                if (notes == null) return false;

                string[][] chapters = SkyIslandJournal.Chapters;
                for (int c = 0; c < chapters.Length; c++)
                {
                    for (int i = 0; i < chapters[c].Length; i++)
                    {
                        string id = chapters[c][i];
                        string key = BuildNoteKey(id);
                        if (string.IsNullOrEmpty(key)) continue;

                        // 判重看真实列表：场景重载后官方实例会重建，registered 里的记录就过期了。
                        Note note = null;
                        for (int n = 0; n < notes.Count; n++)
                        {
                            Note existing = notes[n];
                            if (existing != null && string.Equals(existing.key, key, StringComparison.Ordinal))
                            { note = existing; break; }
                        }
                        if (note == null)
                        {
                            note = new Note();
                            note.key = key;
                            // 条目不带图，理由见文件头：插图由 SkyIslandUiArt 持有并在出击结束时销毁，
                            // 塞进 Note 会在回基地时变成一个已销毁的 Sprite 引用。
                            note.image = null;
                            note.hide = false;
                            // 两边都写：列表决定界面列不列得出来，字典决定按 key 查不查得到。
                            notes.Add(note);
                        }
                        // 若前次在 Add 后失败，已有列表项也必须补入查询字典。
                        NoteIndex.SetNoteDynamic(note);
                        registered.Add(key);

                        if (SkyIslandJournal.Recorded(data, id) && !NoteIndex.GetNoteUnlocked(key))
                            NoteIndex.SetNoteUnlocked(key);
                    }
                }
                mirroredIndex = index;
                syncPending = false;
                syncWarning = false;
                return true;
            }
            catch (Exception e)
            {
                syncPending = true;
                if (!syncWarning) ModBehaviour.DevLog(LogPrefix + "[WARNING] 见闻图鉴注册失败: " + e.Message);
                syncWarning = true;
                return false;
            }
        }

        /// <summary>
        /// 刚收录了一条见闻：把官方图鉴那边点亮。
        /// 权威副本已经由 `SkyIslandStoryService.RecordSearch` 写进我们自己的存档，这里只做镜像。
        /// </summary>
        internal static void Unlock(string searchId)
        {
            try
            {
                string key = BuildNoteKey(searchId);
                if (string.IsNullOrEmpty(key)) return;
                if (NoteIndex.Instance == null) { RequestSync(); return; }
                if (!NoteIndex.GetNoteUnlocked(key)) NoteIndex.SetNoteUnlocked(key);
            }
            catch (Exception e)
            {
                RequestSync();
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 见闻解锁镜像失败 " + searchId + ": " + e.Message);
            }
        }

        internal static void ResetStaticCaches()
        {
            if (runtimeSubscribed)
            {
                SavesSystem.OnSetFile -= RequestSync;
                SavesSystem.OnSaveDeleted -= RequestSync;
                runtimeSubscribed = false;
            }
            mirroredIndex = null;
            RequestSync();
            registered.Clear();
        }
    }
}
