// ============================================================================
// ReforgeSystem_Pity.cs - 重铸顶格保底（按投入累计）
// ============================================================================
// 模块说明：
//   - 每条可重铸属性单独记一份保底进度（0~100），越高越好记 Max，后坐力这类记 Min
//   - 进度按本次投入的幅度金额推进：累计投满物品价值的 PITY_GUARANTEE_VALUE_MULTIPLIER 倍必中，
//     单次最多推进 PITY_MAX_STEP（至少洗 4 次）。只投最低价几乎不涨，顶格主要靠运气；钱花够了就刷得出
//   - 推进后进度为 x 时，额外的保底概率为 (x / 100)^2：前期涨得慢，到 100 必中
//   - 保底命中直接取有利边界，不看倾向滑块；自然顶格或保底顶格都清零重计
//   - 被冷淬液固定的属性不重抽，进度原样冻结
//   - 进度存在物品 Variables 上（RF_PITY_ 前缀、Display=false），随物品存档、交易与入库
//
// 旧存档兼容：
//   - 旧物品没有进度条目，读到默认 0，从头开始算；已有的重铸差值、锁定与 Max/Min 值都不动
//   - 2026-11-27 首版按次数记的整数计数与进度同一刻度（100 必中），直接当进度读
//   - RF_ 前缀已被 IsRuntimeTrackingVariableKey 挡在重铸池和锁定列表之外
//   - RF_PITY_ 不匹配 RF_MOD_ / RF_STAT_ / RF_VAR_ 差值解析与 RF_LOCK_ 锁定解析，
//     旧版本读到带进度的存档同样忽略
//   - 属性从没被记过进度时顶格不新建条目，旧物品不会凭空多出空键
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    public static partial class ReforgeSystem
    {
        /// <summary>保底进度满值：推进到这里本次必定顶格。</summary>
        public const float PITY_GUARANTEE_PROGRESS = 100f;

        /// <summary>
        /// 同一条属性累计投入满物品价值的这么多倍，保底必中（只算幅度投入，不含倾向费用）。
        /// 2026-11-27 由 20 上调：20 倍时价值 4 万的武器约 76 万就能 6 条全满，owner 觉得太便宜；
        /// 200 倍时每次投 1 倍价值约 61 次、370 万，投 10 倍约 13 次、550 万（对比见 FIX_TRACKER 同日一节）。
        /// </summary>
        public const float PITY_GUARANTEE_VALUE_MULTIPLIER = 200f;

        /// <summary>单次重铸最多推进的进度：投得再多也至少洗 4 次才必中，保留抽奖的节奏。</summary>
        public const float PITY_MAX_STEP = 25f;

        /// <summary>存档里进度的上限，留在满值之下。</summary>
        private const float PITY_STORED_CAP = 99.99f;

        public const string PITY_MOD_PREFIX = "RF_PITY_MOD_";
        public const string PITY_STAT_PREFIX = "RF_PITY_STAT_";
        public const string PITY_VAR_PREFIX = "RF_PITY_VAR_";

        /// <summary>属性对应的保底进度键。</summary>
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
        /// 已累计的保底进度，范围 0 ~ 99.99。
        /// 没有条目（旧存档）、读取失败或脏值一律按 0。
        /// </summary>
        public static float GetPityProgress(Item item, string pityKey)
        {
            if (item == null || string.IsNullOrEmpty(pityKey)) return 0f;
            try
            {
                float raw = item.GetFloat(pityKey, 0f);
                if (float.IsNaN(raw) || float.IsInfinity(raw) || raw <= 0f) return 0f;
                return Math.Min(raw, PITY_STORED_CAP);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// 本次投入推进的进度：100 × 投入 ÷ (物品价值 × 20)，封顶 25。
        /// 最低价（物品价值的 1/100）只推进 0.05，投 1 倍物品价值推进 5，投 5 倍及以上推进 25。
        /// </summary>
        public static float GetPityStep(int moneyInvested, float itemValue)
        {
            if (moneyInvested <= 0 || itemValue <= 0f || float.IsNaN(itemValue) || float.IsInfinity(itemValue)) return 0f;
            float step = PITY_GUARANTEE_PROGRESS * moneyInvested / (PITY_GUARANTEE_VALUE_MULTIPLIER * itemValue);
            if (float.IsNaN(step) || step <= 0f) return 0f;
            return Math.Min(step, PITY_MAX_STEP);
        }

        /// <summary>
        /// 推进后进度为 progressAfter 时保底直接顶格的概率：(progressAfter / 100)^2，到 100 为 1。
        /// </summary>
        public static float GetPityChance(float progressAfter)
        {
            if (float.IsNaN(progressAfter) || progressAfter <= 0f) return 0f;
            float ratio = Math.Min(progressAfter, PITY_GUARANTEE_PROGRESS) / PITY_GUARANTEE_PROGRESS;
            return ratio * ratio;
        }

        /// <summary>
        /// 抽一次保底。每条属性固定消耗一次随机数，进度到 100 时概率为 1（NextDouble 恒小于 1）。
        /// </summary>
        public static bool RollPity(float progress, float step, System.Random random)
        {
            return random.NextDouble() < GetPityChance(progress + step);
        }

        /// <summary>
        /// 记录本次结果：顶到有利边界清零，否则累加本次推进（封顶 99.99）。
        /// </summary>
        public static void RecordPityResult(Item item, string pityKey, float previousProgress, float step, bool reachedBest)
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

                if (step <= 0f) return;
                float next = Math.Min(Math.Max(0f, previousProgress) + step, PITY_STORED_CAP);
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
                ModBehaviour.DevLog("[ReforgeSystem] 记录保底进度失败: " + pityKey + " " + e.Message);
            }
        }
    }
}
