// ============================================================================
// CodexModels.cs - 鸭皇图鉴存档 DTO
// ============================================================================
// 硬约束：
//   - 纯数据类，零 Unity 依赖、零副作用，方便 Codec 与 Persistence 单独推理；
//   - `Entries` 是唯一真值，`_index` 只是查询加速，任何直接改动 Entries 的路径
//     都必须自己调 RebuildIndex()（Decode 与 GetOrCreate 已内建）；
//   - 条目上限 fail-closed：达到 CodexTuning.MaxEntries 后 GetOrCreate 返回 null，
//     调用方静默丢弃这次记录，绝不静默挤掉老条目（老条目是玩家收藏进度）；
//   - 字段语义与存档 schema 一一对应，字段含义见 Integration/Codex/CodexCodec.cs 头注释。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>一条图鉴条目（存档 DTO）。</summary>
    internal sealed class CodexEntry
    {
        /// <summary>身份 key（官方 preset nameKey / 自定义 Boss 常量 / zombie_boss_*）。</summary>
        internal string Key;

        /// <summary>落档快照名。目录查不到该 key 时（筛选禁用/官方改名）的显示回落。</summary>
        internal string DisplayName;

        /// <summary>累计击杀次数。</summary>
        internal int Kills;

        /// <summary>初见 DateTime.UtcNow.Ticks；0 = 未解锁。</summary>
        internal long FirstKillTicks;

        /// <summary>首杀模式 id（CodexTuning.ModeId*）。</summary>
        internal string FirstMode;

        /// <summary>最快击杀秒数；&lt;= 0 = 未记录。</summary>
        internal float FastestKillSeconds;
    }

    /// <summary>图鉴存档根 DTO。Entries 是唯一真值，_index 只是查询加速。</summary>
    internal sealed class CodexData
    {
        /// <summary>全部条目（存档顺序 = 首次解锁顺序）。</summary>
        internal readonly List<CodexEntry> Entries = new List<CodexEntry>(64);

        /// <summary>最后写入时间（UTC Ticks），由 CodexPersistence.Store 统一盖。</summary>
        internal long LastUpdatedTicks;

        /// <summary>key -&gt; 条目的查询索引。允许为 null（惰性重建）。</summary>
        private Dictionary<string, CodexEntry> _index;

        /// <summary>
        /// 重建查询索引。Decode 完成后、以及任何绕过 GetOrCreate 直接改 Entries 之后必须调。
        /// 重复 key 走「后者覆盖」而不是抛异常：脏档不得让整个图鉴读不出来。
        /// </summary>
        internal void RebuildIndex()
        {
            Dictionary<string, CodexEntry> index =
                new Dictionary<string, CodexEntry>(Entries.Count + 8, StringComparer.Ordinal);
            for (int i = 0; i < Entries.Count; i++)
            {
                CodexEntry entry = Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;
                index[entry.Key] = entry;
            }
            _index = index;
        }

        /// <summary>按 key 查条目；查不到返回 null。</summary>
        internal CodexEntry Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_index == null) RebuildIndex();

            CodexEntry entry;
            return _index.TryGetValue(key, out entry) ? entry : null;
        }

        /// <summary>
        /// 按 key 取或建条目。达到 CodexTuning.MaxEntries 时返回 null（fail-closed，不新增）。
        ///
        /// displayName 只在两种情况下写进已有条目：快照为空，或快照就是裸 key
        /// （说明落档时目录还查不到名字）。否则一律保留老快照——调用方传进来的
        /// 回落名可能就是 key 本身，不能拿它把一个好名字覆盖掉。
        /// </summary>
        internal CodexEntry GetOrCreate(string key, string displayName)
        {
            if (string.IsNullOrEmpty(key)) return null;

            CodexEntry existing = Find(key);
            if (existing != null)
            {
                if (!string.IsNullOrEmpty(displayName)
                    && !string.Equals(displayName, key, StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(existing.DisplayName)
                        || string.Equals(existing.DisplayName, key, StringComparison.Ordinal)))
                {
                    existing.DisplayName = displayName;
                }
                return existing;
            }

            // fail-closed：条目上限到顶就不再新增，已有条目照常累计
            if (Entries.Count >= CodexTuning.MaxEntries) return null;

            CodexEntry created = new CodexEntry();
            created.Key = key;
            created.DisplayName = string.IsNullOrEmpty(displayName) ? key : displayName;
            created.Kills = 0;
            created.FirstKillTicks = 0L;
            created.FirstMode = string.Empty;
            created.FastestKillSeconds = 0f;

            Entries.Add(created);
            if (_index == null) RebuildIndex();
            else _index[key] = created;
            return created;
        }

        /// <summary>已解锁条目数（Kills &gt; 0）。面板进度与里程碑判定的唯一口径。</summary>
        internal int UnlockedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    CodexEntry entry = Entries[i];
                    if (entry != null && entry.Kills > 0) count++;
                }
                return count;
            }
        }
    }
}
