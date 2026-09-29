// ============================================================================
// ModeHKitPreferenceLedger.cs - 鸭王杯「调整」页里玩家给选手穿上的装备（2026-09-29，SCHEMA+）
// ============================================================================
// 玩家在调整页给选手换上的整备套装原本只记在本场阵容（matchRoster）里：技术重试 / 退游戏重进后的
// 同场重开会把阵容整份清掉，新赛季更是从空白开始，于是「每次穿上，下一把回来又全是没穿上」。
// 赛季 DTO 进 canonical digest（反射全部公有字段），不能加字段，所以照 ModeHDraftRefreshLedger 的做法
// 记在本槽独立 key 里，走共享 BossRushSlotJsonStore（写屏障 / 回读核对 / 换槽复位）。
//
// 口径：按选手身份（stableKey = 官方 preset nameKey）记「这位选手上次穿上了哪几件整备套装」，
// 空列表也记（= 玩家主动换回了全套基础装备）。同一赛季换场、重打、退游戏重进都沿用；
// 下个赛季再抽到同一位选手也沿用，但只取这一季已解锁、可用、与选手兼容、槽位不冲突的那几件
// （过滤在 ModeHRuntimeModule.LoadKitPreference）。基础装备按赛季种子重随，本来就是全套穿着，不用记。
// 旧档没有这个 key：读出空表，行为与改动前一致。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BossRush
{
    internal static class ModeHKitPreferenceLedger
    {
        internal const string StorageKey = "BossRush_ModeHKitPreference_v1";
        private const int SchemaVersion = 1;
        /// <summary>最多记多少位选手（按最近一次改动排序，超出丢最旧的）。</summary>
        private const int MaxEntries = 64;

        private sealed class Entry
        {
            public string StableKey = string.Empty;
            public List<string> KitIds = new List<string>();
        }

        private sealed class Data
        {
            /// <summary>最近改动的排在最后。</summary>
            public List<Entry> Entries = new List<Entry>();
        }

        private static readonly BossRushSlotJsonStore<Data> _store =
            new BossRushSlotJsonStore<Data>(new BossRushSlotJsonStoreSpec<Data>
            {
                StorageKey = StorageKey,
                SchemaVersion = SchemaVersion,
                LogPrefix = "[ModeH] ",
                DisplayName = "鸭王杯选手配装",
                Encode = Encode,
                ReadSchemaVersion = ReadSchema,
                Decode = Decode,
                CreateDefault = () => new Data(),
                BeforeStore = null,
                NotifySlotChanged = null,
                BeforeCollectSaveData = null,
            });

        /// <summary>这位选手上次穿上的整备套装；没记过返回 null（与「记过、是空的」区分开）。</summary>
        internal static List<string> Find(string stableKey)
        {
            if (string.IsNullOrEmpty(stableKey)) return null;
            try
            {
                _store.EnsureSubscribed();
                Data data = _store.Current;
                if (data == null || data.Entries == null) return null;
                for (int i = data.Entries.Count - 1; i >= 0; i--)
                {
                    Entry entry = data.Entries[i];
                    if (entry != null && string.Equals(entry.StableKey, stableKey, StringComparison.Ordinal))
                        return new List<string>(entry.KitIds ?? new List<string>());
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 读选手配装失败: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 记下这位选手现在穿上的整备套装，并立即写进官方存档缓存；随后赛季落盘的 SaveFile 一并写盘。
        /// 内容没变不写。写屏障 / 存档忙时返回 false（最坏是下次打开少沿用一次，不影响本场）。
        /// </summary>
        internal static bool Record(string stableKey, IList<string> kitIds)
        {
            if (string.IsNullOrEmpty(stableKey)) return false;
            try
            {
                _store.EnsureSubscribed();
                Data current = _store.Current;
                List<string> kits = new List<string>();
                for (int i = 0; kitIds != null && i < kitIds.Count; i++)
                {
                    string id = Sanitize(kitIds[i]);
                    if (!string.IsNullOrEmpty(id) && !kits.Contains(id)) kits.Add(id);
                }
                Data next = new Data();
                if (current != null && current.Entries != null)
                {
                    for (int i = 0; i < current.Entries.Count; i++)
                    {
                        Entry entry = current.Entries[i];
                        if (entry == null || string.Equals(entry.StableKey, stableKey, StringComparison.Ordinal)) continue;
                        next.Entries.Add(new Entry { StableKey = entry.StableKey, KitIds = new List<string>(entry.KitIds) });
                    }
                    List<string> old = Find(stableKey);
                    if (old != null && SameList(old, kits)) return true;
                }
                next.Entries.Add(new Entry { StableKey = Sanitize(stableKey), KitIds = kits });
                while (next.Entries.Count > MaxEntries) next.Entries.RemoveAt(0);
                if (!_store.Store(next)) return false;
                return _store.FlushPending();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 记选手配装失败: " + e.Message);
                return false;
            }
        }

        /// <summary>模块销毁：退订存档事件并丢缓存（AGENTS §4.6）。</summary>
        internal static void ResetStaticCaches()
        {
            _store.ResetAll();
        }

        private static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>编码用到的分隔符（| ; = ,）不得出现在键里；stableKey 与 kitId 本来就不含它们。</summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("|", string.Empty).Replace(";", string.Empty)
                .Replace("=", string.Empty).Replace(",", string.Empty).Trim();
        }

        // 「1|stableKey=kitA,kitB;stableKey2=」
        private static string Encode(Data data)
        {
            if (data == null) return null;
            StringBuilder sb = new StringBuilder();
            sb.Append(SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append('|');
            for (int i = 0; data.Entries != null && i < data.Entries.Count; i++)
            {
                Entry entry = data.Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.StableKey)) continue;
                if (sb[sb.Length - 1] != '|') sb.Append(';');
                sb.Append(Sanitize(entry.StableKey)).Append('=');
                for (int k = 0; entry.KitIds != null && k < entry.KitIds.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append(Sanitize(entry.KitIds[k]));
                }
            }
            return sb.ToString();
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
            int bar = raw.IndexOf('|');
            if (bar <= 0) return null;
            Data data = new Data();
            string body = raw.Substring(bar + 1);
            if (body.Length == 0) return data;
            string[] entries = body.Split(';');
            for (int i = 0; i < entries.Length; i++)
            {
                int eq = entries[i].IndexOf('=');
                if (eq <= 0) continue;
                Entry entry = new Entry { StableKey = entries[i].Substring(0, eq) };
                string kits = entries[i].Substring(eq + 1);
                if (kits.Length > 0)
                {
                    string[] ids = kits.Split(',');
                    for (int k = 0; k < ids.Length; k++)
                        if (ids[k].Length > 0 && !entry.KitIds.Contains(ids[k])) entry.KitIds.Add(ids[k]);
                }
                data.Entries.Add(entry);
            }
            return data;
        }
    }
}
