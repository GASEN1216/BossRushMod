// ============================================================================
// ReforgeSystem_Pity.cs - 重铸顶格保底
// ============================================================================
// 模块说明：
//   - 每条可重铸属性单独记「连续几次没顶到有利边界」（越高越好记 Max，后坐力这类记 Min）
//   - 本次是第 k 次（k = 计数 + 1），额外的保底概率为 (k / 100)^2：前期涨得慢，第 100 次必中
//   - 保底命中直接取有利边界，不看倾向滑块；自然顶格或保底顶格都清零重计
//   - 被冷淬液固定的属性不重抽，计数原样冻结
//   - 计数存在物品 Variables 上（RF_PITY_ 前缀、Display=false），随物品存档、交易与入库
//
// 旧存档兼容：
//   - 旧物品没有计数条目，读到默认 0，从头开始算；已有的重铸差值、锁定与 Max/Min 值都不动
//   - RF_ 前缀已被 IsRuntimeTrackingVariableKey 挡在重铸池和锁定列表之外
//   - RF_PITY_ 不匹配 RF_MOD_ / RF_STAT_ / RF_VAR_ 差值解析与 RF_LOCK_ 锁定解析，
//     旧版本读到带计数的存档同样忽略
//   - 属性从没被记过数时顶格不新建条目，旧物品不会凭空多出空键
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    public static partial class ReforgeSystem
    {
        /// <summary>保底次数：连续这么多次没顶格，这一次必定顶格。</summary>
        public const int PITY_GUARANTEE_REFORGES = 100;

        public const string PITY_MOD_PREFIX = "RF_PITY_MOD_";
        public const string PITY_STAT_PREFIX = "RF_PITY_STAT_";
        public const string PITY_VAR_PREFIX = "RF_PITY_VAR_";

        /// <summary>属性对应的保底计数键。</summary>
        public static string GetPityKey(string propertyKey, PropertyType type)
        {
            switch (type)
            {
                case PropertyType.Stat:
                    return PITY_STAT_PREFIX + propertyKey;
                case PropertyType.Variable:
                    return PITY_VAR_PREFIX + propertyKey;
                default:
                    return PITY_MOD_PREFIX + propertyKey;
            }
        }

        /// <summary>
        /// 连续未顶格次数，范围 0 ~ PITY_GUARANTEE_REFORGES-1。
        /// 没有条目（旧存档）、读取失败或脏值一律按 0。
        /// </summary>
        public static int GetPityCount(Item item, string pityKey)
        {
            if (item == null || string.IsNullOrEmpty(pityKey)) return 0;
            try
            {
                float raw = item.GetFloat(pityKey, 0f);
                if (float.IsNaN(raw) || float.IsInfinity(raw) || raw <= 0f) return 0;
                int count = (int)Math.Round(raw);
                return Math.Max(0, Math.Min(count, PITY_GUARANTEE_REFORGES - 1));
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 本次（第 count+1 次）保底直接顶格的概率：((count+1) / 100)^2，第 100 次为 1。
        /// </summary>
        public static float GetPityChance(int count)
        {
            int k = Math.Max(1, Math.Min(count + 1, PITY_GUARANTEE_REFORGES));
            float ratio = (float)k / PITY_GUARANTEE_REFORGES;
            return ratio * ratio;
        }

        /// <summary>
        /// 抽一次保底。每条属性固定消耗一次随机数，第 100 次概率为 1（NextDouble 恒小于 1）。
        /// </summary>
        public static bool RollPity(int count, System.Random random)
        {
            return random.NextDouble() < GetPityChance(count);
        }

        /// <summary>
        /// 记录本次结果：顶到有利边界清零，否则 +1（封顶 PITY_GUARANTEE_REFORGES-1，下一次必中）。
        /// </summary>
        public static void RecordPityResult(Item item, string pityKey, int previousCount, bool reachedBest)
        {
            if (item == null || item.Variables == null || string.IsNullOrEmpty(pityKey)) return;
            try
            {
                if (reachedBest)
                {
                    if (item.Variables.GetEntry(pityKey) != null)
                    {
                        item.SetFloat(pityKey, 0f, false);
                    }
                    return;
                }

                int next = Math.Min(Math.Max(0, previousCount) + 1, PITY_GUARANTEE_REFORGES - 1);
                item.SetFloat(pityKey, next, true);

                // 不在物品详情里显示
                var entry = item.Variables.GetEntry(pityKey);
                if (entry != null)
                {
                    entry.Display = false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 记录保底计数失败: " + pityKey + " " + e.Message);
            }
        }
    }
}
