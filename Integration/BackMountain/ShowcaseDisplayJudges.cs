// ============================================================================
// ShowcaseDisplayJudges.cs - 官方陈列柜陈列加成的纯判据
// ============================================================================
// 2026-09-22 起自建「战利品登记簿」退役：加成改为按官方陈列柜 / 枪械展示架 / 假人里
// **实际摆放**的 Mod 战利品计算（ShowcaseDisplayScanner 采集，ShowcaseService 缓存与挂加成）。
// 本文件零 Unity 引用：筛选、归一、算加成、老档迁移判据全是纯函数，执行回归直接链接。
// 数值口径逐字不变：每高于 Q4 一级 +0.5%，8 件全满再 +5%，上限 +21%。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>陈列加成的纯判据。</summary>
    internal static class ShowcaseDisplayJudges
    {
        /// <summary>每高于 Q4 一级的最大生命加成。</summary>
        internal const float BonusPerQualityLevel = 0.005f;

        /// <summary>八格全满的额外加成。</summary>
        internal const float FullSetBonus = 0.05f;

        /// <summary>可陈列的最低品质。</summary>
        internal const int MinDisplayQuality = 5;

        /// <summary>存档 sourceVersion：1 = 老登记簿（登记不收走），2 = 官方柜实际陈列。</summary>
        internal const int LegacyLedgerSourceVersion = 1;
        internal const int OfficialDisplaySourceVersion = 2;

        /// <summary>哪些 Mod 物品算「战利品」：品质达标、不是后山自产（种子 / 餐食）、不在功能道具名单里、prefab 在位。</summary>
        internal static bool ShouldTagForShowcase(int typeId, int quality, bool isBackMountainItem, bool inDenyList, bool prefabLoaded)
        {
            return typeId > 0 && prefabLoaded && !isBackMountainItem && !inDenyList && quality >= MinDisplayQuality;
        }

        /// <summary>
        /// 把「官方柜里现在摆着的 TypeID」归一：去重、剔非法与低品质、按品质降序、截到 cap。
        /// 降序是有意的：玩家柜子里超过 cap 件时取最好的几件，不看摆放顺序。
        /// </summary>
        internal static int[] NormalizeDisplaySnapshot(int[] raw, Func<int, int> quality, int cap)
        {
            var kept = new List<int>();
            if (raw != null)
            {
                for (int i = 0; i < raw.Length; i++)
                {
                    int id = raw[i];
                    if (id <= 0 || kept.Contains(id)) continue;
                    if (quality(id) < MinDisplayQuality) continue;
                    kept.Add(id);
                }
            }
            kept.Sort((a, b) =>
            {
                int byQuality = quality(b).CompareTo(quality(a));
                return byQuality != 0 ? byQuality : a.CompareTo(b);
            });
            if (cap > 0 && kept.Count > cap) kept.RemoveRange(cap, kept.Count - cap);
            return kept.ToArray();
        }

        /// <summary>当前陈列提供的最大生命加成总量（0.05 = +5%）。</summary>
        internal static float CalculateBonusFrom(IList<int> typeIds, Func<int, int> quality, int cap)
        {
            float total = 0f;
            if (typeIds == null) return 0f;
            for (int i = 0; i < typeIds.Count; i++)
            {
                int q = quality(typeIds[i]);
                if (q <= 4) continue;
                total += (q - 4) * BonusPerQualityLevel;
            }
            if (cap > 0 && typeIds.Count >= cap) total += FullSetBonus;
            return total;
        }

        /// <summary>
        /// 老登记簿（sourceVersion 1）什么时候被官方柜实摆整体覆盖：只在基地且至少找到一个官方柜时。
        /// 非基地或一个柜都没有时保留老列表，老玩家不会在「进基地那一刻加成清零、又没地方摆」的空窗里掉血上限。
        /// </summary>
        internal static bool ShouldOverwriteLegacyLedger(int sourceVersion, bool inBaseScene, bool anyShowcaseFound)
        {
            if (sourceVersion >= OfficialDisplaySourceVersion) return true;
            return inBaseScene && anyShowcaseFound;
        }

        /// <summary>两份快照是否相同（顺序敏感：归一后顺序稳定）。</summary>
        internal static bool SameSnapshot(IList<int> a, IList<int> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
