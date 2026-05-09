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
using Duckov.Utilities;
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

        // 只清理明确确认过的负面 Buff ID，避免误删同 tag 下的增益/剧情 Buff。
        // 原版基础 Debuff 优先复用 GameplayDataSettings.Buffs 的官方引用，其余补充项来自原版/当前模组已知负面状态。
        private static readonly HashSet<int> SupplementalTreatableDebuffIds = new HashSet<int>
        {
            1,      // 脱水
            1001,   // 出血
            1002,   // 出血
            1003,   // 骨折
            1004,   // 创伤
            1016,   // 萎靡
            1022,   // 负重
            1023,   // 超重
            1042,   // 震慑
            1061,   // 中毒
            1071,   // 触电
            1081,   // 疼痛
            1111,   // 扰动
            1112,   // 扭曲
            1117,   // 碎裂
            1121,   // 点燃
            1122,   // 弱毒
            1123,   // 恶心
            1124,   // 害怕
            1125,   // 燃烧
            1126,   // 冰冻
            1127,   // 冻结
            1128,   // 纳米机器
            1129,   // 机器入侵
            1130,   // 食物中毒
            1305,   // 干枯
            1306,   // 干枯
            1401,   // 干枯
            1900,   // 图腾诅咒
            2101,   // 寒冷
            2102,   // 失温
            2201    // 冻伤
        };

        private static readonly HashSet<int> TreatableDebuffIds = new HashSet<int>(SupplementalTreatableDebuffIds);
        private static bool _vanillaTreatableDebuffIdsInitialized;

        private static NurseAffinityConfig cachedConfig => NurseAffinityConfig.Instance;

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

        private static void AddTreatableDebuffId(Buff buff)
        {
            if (buff != null && buff.ID > 0)
            {
                TreatableDebuffIds.Add(buff.ID);
            }
        }

        private static void EnsureTreatableDebuffIdsInitialized()
        {
            if (_vanillaTreatableDebuffIdsInitialized)
            {
                return;
            }

            try
            {
                GameplayDataSettings.BuffsData buffsData = GameplayDataSettings.Buffs;
                if (buffsData == null)
                {
                    return;
                }

                AddTreatableDebuffId(buffsData.BleedSBuff);
                AddTreatableDebuffId(buffsData.UnlimitBleedBuff);
                AddTreatableDebuffId(buffsData.BoneCrackBuff);
                AddTreatableDebuffId(buffsData.WoundBuff);
                AddTreatableDebuffId(buffsData.Weight_Light);
                AddTreatableDebuffId(buffsData.Weight_Heavy);
                AddTreatableDebuffId(buffsData.Weight_SuperHeavy);
                AddTreatableDebuffId(buffsData.Weight_Overweight);
                AddTreatableDebuffId(buffsData.Pain);
                AddTreatableDebuffId(buffsData.Starve);
                AddTreatableDebuffId(buffsData.Thirsty);
                AddTreatableDebuffId(buffsData.Burn);
                AddTreatableDebuffId(buffsData.Poison);
                AddTreatableDebuffId(buffsData.Electric);
                AddTreatableDebuffId(buffsData.Space);
                AddTreatableDebuffId(buffsData.Cold);
                AddTreatableDebuffId(buffsData.SuperCold);

                _vanillaTreatableDebuffIdsInitialized = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "初始化原版Debuff目录失败: " + e.Message);
            }
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

        public static bool HasDebuffs(CharacterMainControl player)
        {
            try
            {
                if (player == null)
                {
                    return false;
                }

                if (player == CharacterMainControl.Main)
                {
                    return HasDebuffs();
                }

                return HasDebuffsInternal(player);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_PREFIX + "检查指定角色 Debuff 失败: " + e.Message);
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
            if (buff == null)
            {
                return false;
            }

            EnsureTreatableDebuffIdsInitialized();
            return TreatableDebuffIds.Contains(buff.ID);
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
            return CanAffordHealing(cost, null);
        }

        public static bool CanAffordHealing(int cost, Component paymentContext)
        {
            try
            {
                if (cost <= 0) return true;
                if (paymentContext != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(paymentContext))
                {
                    return ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(paymentContext, cost);
                }
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
            return GetPlayerMoney(null);
        }

        public static long GetPlayerMoney(Component paymentContext)
        {
            try
            {
                if (paymentContext != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(paymentContext))
                {
                    return ModBehaviour.Instance.GetZombieModePurificationPointsForRealNpcUi(paymentContext);
                }

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
        /// 执行治疗（扣费后尝试回血，并在适用时清除可治疗 Debuff）
        /// </summary>
        /// <param name="cost">期望扣费金额（将以实时报价为准）</param>
        public enum HealingExecutionResult
        {
            /// <summary>没有产生有效治疗结果，必要时会尝试退款。</summary>
            Failure,
            /// <summary>治疗产生了部分效果，但仍有未解决的治疗目标。</summary>
            PartialSuccess,
            /// <summary>本次治疗目标已全部完成。</summary>
            Success
        }

        /// <returns>治疗执行结果</returns>
        public static HealingExecutionResult PerformHealing(int cost, Component paymentContext = null)
        {
            int finalCost = 0;
            bool paymentDeducted = false;
            bool treatmentApplied = false;
            try
            {
                HealingQuote quote;
                if (!TryBuildHealingQuote(out quote, true, paymentContext))
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 无法获取玩家状态");
                    return HealingExecutionResult.Failure;
                }

                if (quote.Status == HealingStatus.FullHealthNoDebuff)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗取消: 当前不需要治疗");
                    return HealingExecutionResult.Failure;
                }

                if (quote.Status == HealingStatus.InsufficientFunds)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 资金不足");
                    return HealingExecutionResult.Failure;
                }

                finalCost = quote.Cost;
                if (finalCost <= 0)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 非法费用 " + finalCost);
                    return HealingExecutionResult.Failure;
                }

                if (cost > 0 && cost != finalCost)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "费用发生变动，使用最新报价: " + cost + " -> " + finalCost);
                }

                CharacterMainControl targetPlayer = CharacterMainControl.Main;
                if (targetPlayer == null || targetPlayer.Health == null)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 扣费前玩家或Health组件为null");
                    return HealingExecutionResult.Failure;
                }

                if (!DeductMoney(finalCost, paymentContext))
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 扣费失败");
                    return HealingExecutionResult.Failure;
                }
                paymentDeducted = true;

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null || player != targetPlayer)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 扣费后玩家上下文发生变化，尝试退款");
                    TryRefundMoney(finalCost, "玩家上下文变化", paymentContext);
                    return HealingExecutionResult.Failure;
                }

                bool shouldClearDebuffs = quote.HasDebuffs;
                float oldHealth = player.Health.CurrentHealth;
                float maxHealth = player.Health.MaxHealth;

                player.Health.SetHealth(maxHealth);
                if (player.Health.CurrentHealth + HEALTH_EPSILON < maxHealth)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: 血量未成功恢复到上限，尝试退款");
                    if (paymentDeducted && finalCost > 0)
                    {
                        TryRefundMoney(finalCost, "治疗回血未生效", paymentContext);
                    }
                    return HealingExecutionResult.Failure;
                }

                int debuffsCleared = 0;
                bool debuffsRemain = false;
                if (shouldClearDebuffs)
                {
                    debuffsCleared = ClearAllDebuffs(player);
                    debuffsRemain = HasDebuffsInternal(player);
                }

                bool restoredHealth = maxHealth - oldHealth > HEALTH_EPSILON;
                if (shouldClearDebuffs && debuffsRemain)
                {
                    bool partialBenefitApplied = restoredHealth || debuffsCleared > 0;
                    if (!partialBenefitApplied)
                    {
                        ModBehaviour.DevLog(LOG_PREFIX + "治疗失败: Debuff 未清除干净且未产生有效治疗，尝试退款");
                        if (paymentDeducted && finalCost > 0)
                        {
                            TryRefundMoney(finalCost, "Debuff清除失败", paymentContext);
                        }
                        return HealingExecutionResult.Failure;
                    }

                    treatmentApplied = true;
                    ModBehaviour.DevLog(
                        LOG_PREFIX + "治疗部分成功! 费用: " + finalCost +
                        ", HP恢复: " + (maxHealth - oldHealth).ToString("F1") +
                        ", Debuff清除: " + debuffsCleared +
                        ", 剩余Debuff: true");
                    return HealingExecutionResult.PartialSuccess;
                }

                treatmentApplied = true;

                ModBehaviour.DevLog(
                    LOG_PREFIX + "治疗成功! 费用: " + finalCost +
                    ", HP恢复: " + (maxHealth - oldHealth).ToString("F1") +
                    ", Debuff清除: " + debuffsCleared);

                return HealingExecutionResult.Success;
            }
            catch (Exception e)
            {
                if (paymentDeducted && !treatmentApplied && finalCost > 0)
                {
                    TryRefundMoney(finalCost, "治疗执行异常", paymentContext);
                }
                NPCExceptionHandler.LogAndIgnore(e, "NurseHealingService.PerformHealing");
                return HealingExecutionResult.Failure;
            }
        }

        private static bool TryRefundMoney(int amount, string reason, Component paymentContext = null)
        {
            try
            {
                if (amount <= 0)
                {
                    return false;
                }

                if (paymentContext != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(paymentContext))
                {
                    ModBehaviour.Instance.RefundZombieModePurificationPointsForRealNpc(paymentContext, amount, true);
                    return true;
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
        private static bool DeductMoney(int amount, Component paymentContext = null)
        {
            try
            {
                if (amount <= 0)
                {
                    return false;
                }

                if (paymentContext != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(paymentContext))
                {
                    return ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(
                        paymentContext,
                        amount,
                        "ZombieModeTempNurseHeal");
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

        public static int ClearAllDebuffs(CharacterMainControl player)
        {
            return ClearAllDebuffsInternal(player);
        }

        private static int ClearAllDebuffsInternal(CharacterMainControl player)
        {
            int cleared = 0;
            try
            {
                if (player == null) return 0;
                cleared = ClearTreatableDebuffs(player);
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
        /// 逐个移除明确允许治疗的 Debuff，避免误删同 tag 下的增益 Buff。
        /// </summary>
        private static int ClearTreatableDebuffs(CharacterMainControl player)
        {
            if (player == null) return 0;

            CharacterBuffManager buffManager = player.GetBuffManager();
            if (buffManager == null || buffManager.Buffs == null || buffManager.Buffs.Count <= 0)
            {
                return 0;
            }

            int cleared = 0;
            List<Buff> buffsToRemove = new List<Buff>();
            var buffs = buffManager.Buffs;
            for (int i = 0; i < buffs.Count; i++)
            {
                Buff buff = buffs[i];
                if (IsTreatableDebuff(buff))
                {
                    buffsToRemove.Add(buff);
                }
            }

            for (int i = 0; i < buffsToRemove.Count; i++)
            {
                Buff buff = buffsToRemove[i];
                if (buff == null || !buffManager.Buffs.Contains(buff))
                {
                    continue;
                }

                try
                {
                    buffManager.RemoveBuff(buff, false);
                    cleared++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LOG_PREFIX + "按实例清除Debuff失败: " + buff.ID + ", " + e.Message);
                }
            }

            return cleared;
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

        private static bool TryBuildHealingQuote(out HealingQuote quote, bool includeAffordability, Component paymentContext = null)
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

            if (includeAffordability && !CanAffordHealing(quote.Cost, paymentContext))
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
            return GetHealingStatus(null, out ignoredCost);
        }

        /// <summary>
        /// 获取当前治疗状态并输出当前费用
        /// </summary>
        public static HealingStatus GetHealingStatus(out int cost)
        {
            return GetHealingStatus(null, out cost);
        }

        public static HealingStatus GetHealingStatus(Component paymentContext, out int cost)
        {
            cost = 0;
            try
            {
                HealingQuote quote;
                if (!TryBuildHealingQuote(out quote, true, paymentContext))
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
                    return NPCDialogueSystem.GetSpecialDialogue(NurseAffinityConfig.NPC_ID, "heal_full_hp", level);
                case HealingStatus.InsufficientFunds:
                    return NPCDialogueSystem.GetSpecialDialogue(NurseAffinityConfig.NPC_ID, "heal_no_money", level);
                case HealingStatus.DebuffOnly:
                    return NPCDialogueSystem.GetSpecialDialogue(NurseAffinityConfig.NPC_ID, "heal_debuff_only", level);
                default:
                    return null;
            }
        }
    }
}
