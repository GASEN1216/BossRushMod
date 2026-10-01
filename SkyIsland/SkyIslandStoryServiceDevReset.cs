#if BOSSRUSH_DEV
// ============================================================================
// SkyIslandStoryServiceDevReset.cs - F3「清空天空岛剧情 / 任务」的写入口（只在 Dev 构建里存在）
// ============================================================================
// owner 2026-10-01 要在 F3 里一键把当前存档槽的天空岛进度清回新档，方便从 Jeff 的序章重新走一遍。
//
// - 清的是 Mod 自己的天空岛剧情存档（SkyIslandStoryRules.StorageKey）：序章、航线、三条主线任务的接取 / 交付位、
//   航标、具名对手、手记、信件、灯、蛙卵、纪念品发放记录全部回到 CreateDefault。官方任务日志里的 590001、590011–590013
//   由投影核心按这份事实下一拍自动撤回（OfficialQuestProjection.Synchronize → ClearProjection），不直接改官方 QuestManager。
// - 不动背包、仓库里的物品（航徽、噬风之核、航向仪等照旧留着），不动好感 / 婚姻、其它模式与鸭王征程。
// - 只走共享 store 的写屏障与协调器的立即落盘（与 DevAutotestReplace / DevAutotestFlush 同一条路），
//   但不过自动验收的「专用测试档」门：这是 owner 在自己档上手动按的按钮，调用方先弹确认框。
// - 只许在岛外调用（岛上会话持有门面、门 / 遭遇 / HUD 都按开场时的剧情装配，半路清空会把这一趟弄乱）。
// - 整份文件包在 #if BOSSRUSH_DEV 里：正式构建里没有这个入口。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    internal sealed partial class SkyIslandStoryService
    {
        /// <summary>把当前槽的天空岛剧情整份清回新档并立即落盘。失败时 <paramref name="error"/> 给出原因，存档保持原样。</summary>
        internal bool DevResetToNewGame(out string error)
        {
            error = null;
            if (!CanWrite) { error = "story_cannot_write:" + (store.LastError ?? (IsCurrentSlot ? "write_barrier_or_fault" : "slot_changed")); return false; }
            SkyIslandStoryData fresh = SkyIslandStoryRules.CreateDefault();
            string encoded = SkyIslandStoryCodec.Encode(fresh);
            if (encoded == null || SkyIslandStoryCodec.Decode(encoded) == null) { error = "default_rejected_by_codec"; return false; }
            // 整份替换是权威数据：这一趟暂不入档的名单作废，否则旧的灯 / 手记会在下次编码时被并回去。
            raidHeldNotes.Clear();
            if (!store.Store(fresh)) { error = "store_rejected:" + (store.LastError ?? "unknown"); return false; }
            MarkPending(true);
            if (!coordinator.RequestFlush(out error, true))
            {
                error = error ?? coordinator.LastError ?? store.LastError ?? "flush_failed";
                return false;
            }
            lastSaveError = null;
            pendingSince = -1f;
            urgentPending = false;
            return true;
        }

        /// <summary>
        /// F3 按钮的整套流程：岛外才执行；基地里有常驻门面（序章打开的）就用它，免得它内存里的旧剧情下次存档时写回去；
        /// 没有时开一份临时门面（同一个 store / 协调器），用完关掉。成功后读回核对存档里确实是新档。
        /// </summary>
        internal static bool DevResetCurrentSlot(ModBehaviour host, out string detail)
        {
            bool onIsland;
            SkyIslandStoryService live = SkyIslandOfficialQuestStory.Resolve(host, out onIsland);
            if (onIsland || (host != null && host.GetComponent<SkyIslandSession>() != null)) { detail = "on_island"; return false; }
            if (SkyIslandStorySaveRecovery.IsPending()) { detail = "island_story_save_pending"; return false; }
            SkyIslandStoryService temporary = null;
            try
            {
                SkyIslandStoryService target = live;
                if (target == null)
                {
                    temporary = new SkyIslandStoryService();
                    temporary.DevAutotestOpen();
                    target = temporary;
                }
                string error;
                if (!target.DevResetToNewGame(out error)) { detail = "reset_failed:" + error; return false; }
                SkyIslandStoryData saved = SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey)
                    ? SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey))
                    : SkyIslandStoryRules.CreateDefault();
                if (saved == null || saved.flags != 0 || saved.discoveredNotes.Length != 0 || saved.clearedEncounters.Length != 0 || saved.visitedRegions != 0)
                { detail = "readback_not_default"; return false; }
                detail = (temporary == null ? "base_service" : "temporary_service") + ",slot=" + SavesSystem.CurrentSlot;
                return true;
            }
            catch (Exception e)
            {
                detail = "reset_threw:" + e.GetType().Name + ":" + e.Message;
                return false;
            }
            finally
            {
                if (temporary != null)
                {
                    try { temporary.Close(); }
                    catch (Exception e) { ModBehaviour.DevLog("[SkyIsland] F3 清空剧情的临时门面关闭失败: " + e.Message); }
                }
            }
        }
    }
}
#endif
