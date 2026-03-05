// ============================================================================
// NurseHealingService.cs - 护士治疗服务
// ============================================================================
// 模块说明：
//   护士NPC"羽织"的治疗服务，包括：
//   - 费用计算：(最大血量 - 当前血量) × 玩家等级 × 10
//   - Debuff治疗费用：每次治疗额外 + 玩家等级 × 50（有Debuff就计入）
//   - 好感度折扣应用
//   - 恢复满血
//   - 清除可治疗的负面状态（Debuff）
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Utils;
using Duckov;
using Duckov.Buffs;
using Duckov.Economy;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 护士治疗服务 - 处理治疗费用计算、回血和Debuff清除
    /// </summary>
    public static class NurseHealingService
    {
        // ============================================================================
        // 常量
        // ============================================================================

        /// <summary>费用倍率基础值</summary>
        private const int COST_MULTIPLIER = 10;

        /// <summary>Debuff治疗费用倍率（等级 × 此值）</summary>
        private const int DEBUFF_ONLY_COST_MULTIPLIER = 50;

        /// <summary>日志前缀</summary>
        private const string LOG_PREFIX = "[NurseNPC] [HealingService] ";
        private const float HEALTH_EPSILON = 0.01f;
        private const float DEBUFF_CACHE_INTERVAL = 0.2f;

        // 仅清理确定属于负面状态的标签，避免误删正面/剧情Buff。
        private static readonly HashSet<Buff.BuffExclusiveTags> TreatableDebuffTags = new HashSet<Buff.BuffExclusiveTags>
        {
            Buff.BuffExclusiveTags.Bleeding,
            Buff.BuffExclusiveTags.Starve,
            Buff.BuffExclusiveTags.Thirsty,
            Buff.BuffExclusiveTags.Weight,
            Buff.BuffExclusiveTags.Poison,
            Buff.BuffExclusiveTags.Pain,
            Buff.BuffExclusiveTags.Electric,
            Buff.BuffExclusiveTags.Burning,
            Buff.BuffExclusiveTags.Nauseous,
            Buff.BuffExclusiveTags.Stun,
            Buff.BuffExclusiveTags.Freeze
        };

        private static NurseAffinityConfig _cachedConfig;
        private static NurseAffinityConfig cachedConfig
        {
            get
            {
                if (_cachedConfig == null) _cachedConfig = new NurseAffinityConfig();
                return _cachedConfig;
            }
        }

        private static float _nextDebuffRefreshTime = -1f;
        private static bool _cachedHasDebuffs;
        private static CharacterMainControl _cachedDebuffOwner;

        private struct HealingQuote
        {
            public float HpDeficit;
            public bool HasDebuffs;
            public int PlayerLevel;
            public int Cost;
            public HealingStatus Status;
        }

        private static float GetRealtimeSinceStartup()
        {
            try
            {
                return Time.realtimeSinceStartup;
            }
            catch
            {
                return 0f;
            }
        }

        private static void InvalidateDebuffCache()
        {
            _cachedDebuffOwner = null;
            _cachedHasDebuffs = false;
            _nextDebuffRefreshTime = -1f;
        }

        public static void NotifyDebuffStateChanged()
        {
            InvalidateDebuffCache();
        }

        private static bool TryReadPlayerHealthSnapshot(out CharacterMainControl player, out float hpDeficit)
        {
            player = CharacterMainControl.Main;
            hpDeficit = 0f;
            if (player == null || player.Health == null)
            {
                return false;
            }

            hpDeficit = Mathf.Max(0f, player.Health.MaxHealth - player.Health.CurrentHealth);
            return true;
        }

        // ============================================================================
        // 玩家状态检查
        // ============================================================================

        /// <summary>
        /// 检查玩家是否需要治疗（HP不满或有Debuff）
        /// </summary>
        public static bool NeedsHealing()
        {
            try
            {
                HealingQuote quote;
                if (!TryBuildHealingQuote(out quote, false))
                {
                    return false;
                }

                return quote.Status == HealingStatus.DebuffOnly || quote.Status == HealingStatus.NeedsHealing;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "检查治疗需求失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 检查玩家是否满血
        /// </summary>
        public static bool IsFullHP()
        {
            try
            {
                CharacterMainControl player;
                float hpDeficit;
                if (!TryReadPlayerHealthSnapshot(out player, out hpDeficit)) return true;
                return hpDeficit <= HEALTH_EPSILON;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "检查满血状态失败: " + e.Message);
                return true;
            }
        }

        /// <summary>
        /// 获取玩家HP差值（最大血量 - 当前血量）
        /// </summary>
        public static float GetHPDeficit()
        {
            try
            {
                CharacterMainControl player;
                float hpDeficit;
                if (!TryReadPlayerHealthSnapshot(out player, out hpDeficit)) return 0f;
                return hpDeficit;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "获取HP差值失败: " + e.Message);
                return 0f;
            }
        }

        /// <summary>
        /// 检查玩家是否有可治疗的负面状态
        /// </summary>
        public static bool HasDebuffs()
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null)
                {
                    InvalidateDebuffCache();
                    return false;
                }

                float now = GetRealtimeSinceStartup();
                if (_cachedDebuffOwner == player && now < _nextDebuffRefreshTime)
                {
                    return _cachedHasDebuffs;
                }

                _cachedDebuffOwner = player;
                _cachedHasDebuffs = HasDebuffsInternal(player);
                _nextDebuffRefreshTime = now + DEBUFF_CACHE_INTERVAL;
                return _cachedHasDebuffs;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "检查Debuff失败: " + e.Message);
                return false;
            }
        }

        private static bool HasDebuffsInternal(CharacterMainControl player)
        {
            if (player == null) return false;

            CharacterBuffManager buffManager = player.GetBuffManager();
            if (buffManager == null || buffManager.Buffs == null || buffManager.Buffs.Count <= 0)
            {
                return false;
            }

            var buffs = buffManager.Buffs;
            for (int i = 0; i < buffs.Count; i++)
            {
                if (IsTreatableDebuff(buffs[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTreatableDebuff(Buff buff)
        {
            return buff != null && TreatableDebuffTags.Contains(buff.ExclusiveTag);
        }

        // ============================================================================
        // 费用计算
        // ============================================================================

        /// <summary>
        /// 获取玩家等级（官方入口：EXPManager.Level）
        /// </summary>
        public static int GetPlayerLevel()
        {
            try
            {
                return Mathf.Max(1, EXPManager.Level);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "获取玩家等级失败: " + e.Message);
                return 1;
            }
        }

        /// <summary>
        /// 计算治疗费用（不含是否足够支付判断）
        /// </summary>
        private static int CalculateHealCostInternal(float hpDeficit, bool hasDebuffs, int playerLevel)
        {
            if (hpDeficit <= HEALTH_EPSILON && !hasDebuffs)
            {
                return 0;
            }

            int baseCost = 0;

            // 回血费用：按缺失血量计算
            if (hpDeficit > HEALTH_EPSILON)
            {
                baseCost += Mathf.CeilToInt(hpDeficit * playerLevel * COST_MULTIPLIER);
            }

            // Debuff 治疗费用：有可治疗 Debuff 就计入一次
            if (hasDebuffs)
            {
                baseCost += playerLevel * DEBUFF_ONLY_COST_MULTIPLIER;
            }

            if (baseCost <= 0)
            {
                return 0;
            }

            float discount = GetCurrentDiscount();
            int finalCost = Mathf.CeilToInt(baseCost * (1f - discount));
            return Mathf.Max(1, finalCost);
        }

        public static int CalculateHealCost()
        {
            try
            {
                HealingQuote quote;
                if (!TryBuildHealingQuote(out quote, false))
                {
                    return 0;
                }

                return quote.Cost;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "计算治疗费用失败: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 获取当前好感度对应的折扣率
        /// </summary>
        public static float GetCurrentDiscount()
        {
            try
            {
                int level = AffinityManager.GetLevel(NurseAffinityConfig.NPC_ID);
                return NurseAffinityConfig.GetHealingDiscountForLevel(level);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// 检查玩家资金是否足够支付治疗费用（账户余额 + 现金）
        /// </summary>
        public static bool CanAffordHealing(int cost)
        {
            try
            {
                if (cost <= 0) return true;
                return EconomyManager.IsEnough(new Cost((long)cost), true, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "检查资金失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 获取玩家可用资金（账户余额 + 现金）
        /// </summary>
        public static long GetPlayerMoney()
        {
            try
            {
                long accountMoney = EconomyManager.Money;
                long cashMoney = EconomyManager.Cash;
                return accountMoney + cashMoney;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "获取资金失败: " + e.Message);
                return 0;
            }
        }

        // ============================================================================
        // 治疗执行
        // ============================================================================

        /// <summary>
        /// 执行治疗（扣费 + 回满血 + 清除Debuff）
        /// </summary>
        /// <param name="cost">期望扣费金额（将以实时报价为准）</param>
        /// <returns>治疗是否成功</returns>
        public static bool PerformHealing(int cost)
        {
            int finalCost = 0;
            bool paymentDeducted = false;
            bool treatmentApplied = false;
            try
            {
                HealingQuote quote;
                if (!TryBuildHealingQuote(out quote, true))
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 无法获取玩家状态");
                    return false;
                }

                if (quote.Status == HealingStatus.FullHealthNoDebuff)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗取消: 当前不需要治疗");
                    return false;
                }

                if (quote.Status == HealingStatus.InsufficientFunds)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 资金不足");
                    return false;
                }

                finalCost = quote.Cost;
                if (finalCost <= 0)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 非法费用 " + finalCost);
                    return false;
                }

                if (cost > 0 && cost != finalCost)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "费用发生变动，使用最新报价: " + cost + " -> " + finalCost);
                }

                CharacterMainControl targetPlayer = CharacterMainControl.Main;
                if (targetPlayer == null || targetPlayer.Health == null)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 扣费前玩家或Health组件为null");
                    return false;
                }

                if (!DeductMoney(finalCost))
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 扣费失败");
                    return false;
                }
                paymentDeducted = true;

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null || player != targetPlayer)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 扣费后玩家上下文发生变化，尝试退款");
                    TryRefundMoney(finalCost, "玩家上下文变化");
                    return false;
                }

                float oldHealth = player.Health.CurrentHealth;
                float maxHealth = player.Health.MaxHealth;
                player.Health.SetHealth(maxHealth);
                treatmentApplied = true;

                int debuffsCleared = ClearAllDebuffs(player);

                ModBehaviour.DevLog(
                    LOG_PREFIX + "治疗成功! 费用: " + finalCost +
                    ", HP恢复: " + (maxHealth - oldHealth).ToString("F1") +
                    ", Debuff清除: " + debuffsCleared);

                return true;
            }
            catch (Exception e)
            {
                if (paymentDeducted && !treatmentApplied && finalCost > 0)
                {
                    TryRefundMoney(finalCost, "治疗执行异常");
                }
                NPCExceptionHandler.LogAndIgnore(e, "NurseHealingService.PerformHealing");
                return false;
            }
        }

        private static bool TryRefundMoney(int amount, string reason)
        {
            try
            {
                if (amount <= 0)
                {
                    return false;
                }

                if (!EconomyManager.Add(amount))
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "退款失败: EconomyManager.Add 返回 false, amount=" + amount + ", reason=" + reason);
                    return false;
                }

                ModBehaviour.DevLog(LOG_PREFIX + "退款成功: " + amount + ", reason=" + reason);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "退款异常: " + e.Message + ", amount=" + amount + ", reason=" + reason);
                return false;
            }
        }

        /// <summary>
        /// 通过官方经济系统扣费
        /// </summary>
        private static bool DeductMoney(int amount)
        {
            try
            {
                if (amount <= 0)
                {
                    return false;
                }

                Cost payment = new Cost((long)amount);
                if (!EconomyManager.IsEnough(payment, true, true))
                {
                    return false;
                }

                if (!EconomyManager.Pay(payment, true, true))
                {
                    return false;
                }

                ModBehaviour.DevLog(LOG_PREFIX + "扣费成功: " + amount);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "扣费失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 清除玩家身上所有可治疗的负面状态
        /// </summary>
        /// <returns>清除的Debuff数量</returns>
        public static int ClearAllDebuffs()
        {
            return ClearAllDebuffs(CharacterMainControl.Main);
        }

        private static int ClearAllDebuffs(CharacterMainControl player)
        {
            int cleared = 0;
            try
            {
                if (player == null) return 0;
                cleared = ClearDebuffsByTags(player, TreatableDebuffTags);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "清除Debuff失败: " + e.Message);
            }
            finally
            {
                InvalidateDebuffCache();
            }

            return cleared;
        }

        /// <summary>
        /// 按标签分别清除 Debuff（原版支持按 tag 移除）
        /// </summary>
        private static int ClearDebuffsByTags(CharacterMainControl player, IEnumerable<Buff.BuffExclusiveTags> tags)
        {
            if (player == null || tags == null) return 0;

            CharacterBuffManager buffManager = player.GetBuffManager();
            if (buffManager == null || buffManager.Buffs == null || buffManager.Buffs.Count <= 0)
            {
                return 0;
            }

            int cleared = 0;
            HashSet<Buff.BuffExclusiveTags> processed = new HashSet<Buff.BuffExclusiveTags>();
            foreach (Buff.BuffExclusiveTags tag in tags)
            {
                if (tag == Buff.BuffExclusiveTags.NotExclusive) continue;
                if (!processed.Add(tag)) continue;

                int before = CountBuffsByTag(buffManager, tag);
                if (before <= 0) continue;

                try
                {
                    buffManager.RemoveBuffsByTag(tag, false);
                    int after = CountBuffsByTag(buffManager, tag);
                    cleared += Mathf.Max(0, before - after);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "按Tag清除Debuff失败: " + tag + ", " + e.Message);
                }
            }

            return cleared;
        }

        private static int CountBuffsByTag(CharacterBuffManager buffManager, Buff.BuffExclusiveTags tag)
        {
            if (buffManager == null || buffManager.Buffs == null || buffManager.Buffs.Count <= 0)
            {
                return 0;
            }

            int count = 0;
            var buffs = buffManager.Buffs;
            for (int i = 0; i < buffs.Count; i++)
            {
                Buff buff = buffs[i];
                if (buff != null && buff.ExclusiveTag == tag)
                {
                    count++;
                }
            }

            return count;
        }

        // ============================================================================
        // 治疗状态判断（用于UI显示）
        // ============================================================================

        /// <summary>
        /// 获取治疗状态类型
        /// </summary>
        public enum HealingStatus
        {
            /// <summary>满血且无Debuff</summary>
            FullHealthNoDebuff,
            /// <summary>仅有Debuff无HP差</summary>
            DebuffOnly,
            /// <summary>需要治疗（HP不满，可能也有Debuff）</summary>
            NeedsHealing,
            /// <summary>资金不足</summary>
            InsufficientFunds
        }

        private static bool TryBuildHealingQuote(out HealingQuote quote, bool includeAffordability)
        {
            quote = default(HealingQuote);

            CharacterMainControl player;
            float hpDeficit;
            if (!TryReadPlayerHealthSnapshot(out player, out hpDeficit))
            {
                quote.Status = HealingStatus.FullHealthNoDebuff;
                return false;
            }

            quote.HpDeficit = hpDeficit;
            quote.HasDebuffs = HasDebuffs();
            quote.PlayerLevel = GetPlayerLevel();
            quote.Cost = CalculateHealCostInternal(quote.HpDeficit, quote.HasDebuffs, quote.PlayerLevel);

            if (quote.HpDeficit <= HEALTH_EPSILON && !quote.HasDebuffs)
            {
                quote.Status = HealingStatus.FullHealthNoDebuff;
                quote.Cost = 0;
                return true;
            }

            if (quote.Cost <= 0)
            {
                quote.Status = HealingStatus.FullHealthNoDebuff;
                quote.Cost = 0;
                return true;
            }

            if (includeAffordability && !CanAffordHealing(quote.Cost))
            {
                quote.Status = HealingStatus.InsufficientFunds;
                return true;
            }

            quote.Status = quote.HpDeficit <= HEALTH_EPSILON
                ? HealingStatus.DebuffOnly
                : HealingStatus.NeedsHealing;
            return true;
        }

        /// <summary>
        /// 获取当前治疗状态
        /// </summary>
        public static HealingStatus GetHealingStatus()
        {
            int ignoredCost;
            return GetHealingStatus(out ignoredCost);
        }

        /// <summary>
        /// 获取当前治疗状态并输出当前费用
        /// </summary>
        public static HealingStatus GetHealingStatus(out int cost)
        {
            cost = 0;
            try
            {
                HealingQuote quote;
                if (!TryBuildHealingQuote(out quote, true))
                {
                    return HealingStatus.FullHealthNoDebuff;
                }

                cost = quote.Cost;
                return quote.Status;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "获取治疗状态失败: " + e.Message);
                return HealingStatus.FullHealthNoDebuff;
            }
        }

        /// <summary>
        /// 获取治疗状态对应的对话
        /// </summary>
        public static string GetHealingDialogue(HealingStatus status)
        {
            var config = cachedConfig;
            int level = NPCExceptionHandler.TryExecute(
                () => AffinityManager.GetLevel(NurseAffinityConfig.NPC_ID),
                "NurseHealingService.GetHealingDialogue.GetAffinityLevel",
                1,
                false);

            switch (status)
            {
                case HealingStatus.FullHealthNoDebuff:
                    return config.GetSpecialDialogue("heal_full_hp", level);
                case HealingStatus.InsufficientFunds:
                    return config.GetSpecialDialogue("heal_no_money", level);
                case HealingStatus.DebuffOnly:
                    return config.GetSpecialDialogue("heal_debuff_only", level);
                default:
                    return null;
            }
        }
    }
}
