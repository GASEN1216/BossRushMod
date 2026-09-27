// ============================================================================
// ModeHDraftRefreshLedger.cs - 鸭王杯选人页「刷新候选」已用次数（2026-09-25，SCHEMA+）
// ============================================================================
// 每季最多刷新 3 次。次数原本只在内存里，退出重进走恢复后又给满 3 次，而候选名单已落盘，
// 等于可以无限重抽。赛季 DTO 进 canonical digest（反射全部公有字段），不能加字段，
// 所以按 runId 记在本槽独立 key 里，走共享 BossRushSlotJsonStore（写屏障 / 回读核对 / 换槽复位）。
// 旧档没有这个 key：读出 0 次，行为与改动前一致。
// 只记「这一季用了几次」，一季一条；新赛季 runId 不同，自然从 0 开始。
// ============================================================================

using System;
using System.Globalization;

namespace BossRush
{
    internal static class ModeHDraftRefreshLedger
    {
        internal const string StorageKey = "BossRush_ModeHDraftRefresh_v1";
        private const int SchemaVersion = 1;

        private sealed class Data
        {
            public string RunId = string.Empty;
            public int Used;
        }

        private static readonly BossRushSlotJsonStore<Data> _store =
            new BossRushSlotJsonStore<Data>(new BossRushSlotJsonStoreSpec<Data>
            {
                StorageKey = StorageKey,
                SchemaVersion = SchemaVersion,
                LogPrefix = "[ModeH] ",
                DisplayName = "鸭王杯刷新次数",
                Encode = Encode,
                ReadSchemaVersion = ReadSchema,
                Decode = Decode,
                CreateDefault = () => new Data(),
                BeforeStore = null,
                NotifySlotChanged = null,
                BeforeCollectSaveData = null,
            });

        /// <summary>这一季已经用掉的刷新次数；读不出来时按 0（与旧档一致）。</summary>
        internal static int UsedFor(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return 0;
            try
            {
                _store.EnsureSubscribed();
                Data data = _store.Current;
                return data != null && string.Equals(data.RunId, runId, StringComparison.Ordinal)
                    ? Math.Max(0, data.Used) : 0;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 读刷新次数失败: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 记下这一季用了几次，并立即写进官方存档缓存；随后赛季落盘（TryPersistSeason）的 SaveFile 一并写盘。
        /// 写屏障 / 存档忙时返回 false，调用方照常刷新（最坏是崩溃后多给一次，不会少给）。
        /// </summary>
        internal static bool Record(string runId, int used)
        {
            if (string.IsNullOrEmpty(runId)) return false;
            try
            {
                _store.EnsureSubscribed();
                if (!_store.Store(new Data { RunId = runId, Used = Math.Max(0, used) })) return false;
                return _store.FlushPending();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 记刷新次数失败: " + e.Message);
                return false;
            }
        }

        /// <summary>模块销毁：退订存档事件并丢缓存（AGENTS §4.6）。</summary>
        internal static void ResetStaticCaches()
        {
            _store.ResetAll();
        }

        private static string Encode(Data data)
        {
            if (data == null) return null;
            string runId = (data.RunId ?? string.Empty).Replace("|", string.Empty);
            return SchemaVersion.ToString(CultureInfo.InvariantCulture) + "|"
                + data.Used.ToString(CultureInfo.InvariantCulture) + "|" + runId;
        }

        private static int ReadSchema(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return -1;
            int bar = raw.IndexOf('|');
            int version;
            return bar > 0 && int.TryParse(raw.Substring(0, bar), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out version) ? version : -1;
        }

        private static Data Decode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string[] parts = raw.Split(new[] { '|' }, 3);
            int used;
            if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out used))
                return null;
            return new Data { RunId = parts[2], Used = Math.Max(0, used) };
        }
    }
}
