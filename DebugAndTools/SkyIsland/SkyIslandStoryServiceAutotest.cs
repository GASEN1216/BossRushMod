#if BOSSRUSH_DEV
// ============================================================================
// SkyIslandStoryServiceAutotest.cs - 全自动实机验收给剧情存档的快照 / 清空 / 还原入口（只在 Dev 构建里存在）
// ============================================================================
// 全自动实机验收（DebugAndTools/F3GameplayValidationAutotest*.cs）要从「新档」按阶段推进剧情，跑完再把测试档还原成开跑前的样子。
// 阶段推进只走生产的合法入口（TryApply / RecordEncounterCleared / RecordNote）；只有「整份清空」与「整份还原」没有生产入口，收在这里：
//
// - 只走共享 store：写屏障、槽位烙印、编码失败一律拒绝，与生产写入同一道门；不直接 SavesSystem.Save。
// - 任何写入之前先过 F3GameplayValidationRunner.AutotestWriteAllowed：Dev 构建 + 专用测试档 + 自动验收正在跑（或正在做崩溃恢复）。
// - 立即落盘走协调器的 bypass 分支（与离岛 TryClose 同一条），读回核对由调用方做。
// - 整份文件包在 #if BOSSRUSH_DEV 里：正式构建里没有这些入口（tests/F3AutotestOrchestratorGuard.py 守着）。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    internal sealed partial class SkyIslandStoryService
    {
        /// <summary>
        /// 不开会话、只为还原打开存档门面：与 <see cref="Open"/> 同样烙印槽位、订阅并读档，但不开分段计时
        /// （否则基地里会多出一行 SKY_TIMING raid_open，混进 owner 回填时长模型的日志）。
        /// </summary>
        internal void DevAutotestOpen()
        {
            if (opened) return;
            entrySlot = SavesSystem.CurrentSlot;
            store.EnsureSubscribed();
            store.LoadOrInit();
            slotChanged = false;
            opened = true;
        }

        /// <summary>
        /// 整份替换剧情记录（清空成新档、或还原快照）。<paramref name="flushNow"/> 为真时立即物理落盘。
        /// 失败时 <paramref name="error"/> 给出原因，存档保持原样。
        /// </summary>
        internal bool DevAutotestReplace(SkyIslandStoryData data, bool flushNow, out string error)
        {
            if (!F3GameplayValidationRunner.AutotestWriteAllowed(out error)) return false;
            if (!CanWrite) { error = "story_cannot_write:" + (store.LastError ?? (IsCurrentSlot ? "write_barrier_or_fault" : "slot_changed")); return false; }
            if (data == null) { error = "replacement_null"; return false; }
            SkyIslandStoryData candidate = data.Copy();
            string encoded = SkyIslandStoryCodec.Encode(candidate);
            if (encoded == null || SkyIslandStoryCodec.Decode(encoded) == null) { error = "replacement_rejected_by_codec"; return false; }
            if (!store.Store(candidate)) { error = "store_rejected:" + (store.LastError ?? "unknown"); return false; }
            MarkPending(true);
            return !flushNow || DevAutotestFlush(out error);
        }

        /// <summary>立即把待写批次物理落盘（绕过战斗门与每帧闸，与离岛兜底同一条）。</summary>
        internal bool DevAutotestFlush(out string error)
        {
            if (!F3GameplayValidationRunner.AutotestWriteAllowed(out error)) return false;
            if (!IsCurrentSlot) { error = "slot_changed"; return false; }
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
    }
}
#endif
