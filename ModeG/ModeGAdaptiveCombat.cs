using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 距离轴判定结果。
    /// </summary>
    public enum ModeGDistanceVerdict
    {
        /// <summary>无极端倾向</summary>
        None,
        /// <summary>玩家持续近距（<=8m）：触发 Close 适应</summary>
        Close,
        /// <summary>玩家持续远距（>=18m）：触发 Far 适应</summary>
        Far
    }

    /// <summary>
    /// 三轴双门槛破解进度（规格 §4.2/§4.4 门槛，§15 HUD 呈现）。
    ///
    /// 唯一来源：破解判定与 HUD 进度必须共用本结构，避免 HUD 显示与实际结算口径漂移
    /// （§15「不显示仍可得分的假进度」）。全部为 0..1 比例，呈现层负责取整成百分比。
    /// </summary>
    public struct ModeGAxisProgress
    {
        /// <summary>目标侧占本波全部可计分直伤的比例（0..1）</summary>
        public readonly float share;
        /// <summary>占比门槛（AxisBreakDamageShare）</summary>
        public readonly float shareTarget;
        /// <summary>目标侧累计直伤占冻结聚合主 Boss 最大血量的比例（0..1+）</summary>
        public readonly float contribution;
        /// <summary>贡献门槛（AxisBreakHealthContribution）</summary>
        public readonly float contributionTarget;
        /// <summary>本轴是否有有效目标（无反制/样本不可信时为 false）</summary>
        public readonly bool active;

        public ModeGAxisProgress(float share, float contribution, bool active)
        {
            this.share = share;
            this.contribution = contribution;
            this.active = active;
            this.shareTarget = ModeGAdaptiveCombat.AxisBreakDamageShare;
            this.contributionTarget = ModeGAdaptiveCombat.AxisBreakHealthContribution;
        }

        /// <summary>双门槛是否同时达标。</summary>
        public bool ThresholdsMet
        {
            get { return active && share >= shareTarget && contribution >= contributionTarget; }
        }
    }

    /// <summary>
    /// Mode G 三轴自适应战斗（规格 §4/§5/§6/§7/§8 重写版）。
    ///
    /// 轴波次固定（九波三幕循环）：距离轴波 2/5/8、弹药轴波 3/6/9、属性轴波 4/7。
    /// - 距离轴：13m 分界、极端带 >=18m/<=8m、破解双门槛 35% 占比 + 20% 总血量贡献；
    /// - 弹药轴：口径禁/促、样本 >=5 发、单次 clamp 10% 聚合血量；
    /// - 属性轴：GunDamageMultiplier 或 MeleeDamageMultiplier PercentageMultiply ×0.75（order 200 私有 source）；
    /// - Close 适应：Boss 近战 +15%/最大生命 +10%；Far 适应：Walk/Run +15%/GunShootSpeed +10%；
    /// - Last Stand 12 秒（owner tunable），超时复仇 buff：回血 20%（Health.AddHealth）+ Walk/Run +15% + 枪/近战 +10%；
    /// - 幕倍率：I +0/+0、II +20%/+10%、III +45%/+20%（owner tunable）；
    /// - 宿敌 Rank：R1 生命 +15%、R2 +25%+WalkRun +10%、R3 +40%+WalkRun +15%+枪/近战 +10%（owner tunable）；
    /// - Resolve 上限 11 = 距离 3 + 弹药 3 + 属性 2 + Last Stand 3。
    /// </summary>
    public sealed class ModeGAdaptiveCombat
    {
        #region Tunables（全部为规格候选值，owner tunable）

        // ---- 距离轴（§4.2）----
        /// <summary>距离分界 13m（冻结）</summary>
        public const float DistanceBoundaryMeters = 13f;
        /// <summary>极端远距 >=18m（冻结）</summary>
        public const float ExtremeFarMeters = 18f;
        /// <summary>极端近距 <=8m（冻结）</summary>
        public const float ExtremeCloseMeters = 8f;
        // 距离轴（§4.2）与属性轴（§4.4）共用同一对破解门槛，因此使用轴中立命名：
        // 单独调整某一轴时必须新增独立常量，不得就地改写共用值。
        /// <summary>破解门槛：分类器接受的 Gun/Melee 伤害占比 >=35%（owner tunable，距离/属性轴共用）</summary>
        public const float AxisBreakDamageShare = 0.35f;
        /// <summary>破解门槛：>=20% combatStartAggregatePrimaryMaxHealth 贡献（owner tunable，距离/属性轴共用）</summary>
        public const float AxisBreakHealthContribution = 0.20f;

        // ---- Close/Far 适应（§4.2 冻结）----
        public const float CloseAdaptationMeleeDamageBonus = 0.15f;   // 近战 +15%
        public const float CloseAdaptationMaxHealthBonus = 0.10f;     // 最大生命 +10%
        public const float FarAdaptationMoveSpeedBonus = 0.15f;       // Walk/Run +15%
        public const float FarAdaptationShootSpeedBonus = 0.10f;      // GunShootSpeed +10%

        // ---- 弹药轴（§4.3）----
        /// <summary>样本门槛：总样本 >=5 发（冻结）</summary>
        public const int AmmoAxisMinSamples = 5;
        /// <summary>单次 clamp：10% 聚合血量（冻结）</summary>
        public const float AmmoBanClampShare = 0.10f;
        /// <summary>CalmGate 停火时长（owner tunable，候选 2 秒）</summary>
        public const float CalmGateSeconds = 2f;

        // ---- 属性轴（§4.4）----
        /// <summary>属性封锁：PercentageMultiply -25%（即 ×0.75，order 200）（冻结）</summary>
        public const float AttributeLockValue = -0.25f;

        // ---- Last Stand（§5）----
        /// <summary>Last Stand 时长 12 秒（owner tunable，替换脚手架 10f）</summary>
        public const float LastStandDurationSeconds = 12f;
        /// <summary>复仇 buff 回血 20%（冻结）</summary>
        public const float RevengeHealShare = 0.20f;
        /// <summary>复仇 buff Walk/Run +15%（冻结）</summary>
        public const float RevengeMoveSpeedBonus = 0.15f;
        /// <summary>复仇 buff 枪/近战 +10%（冻结）</summary>
        public const float RevengeDamageBonus = 0.10f;

        // ---- 幕倍率（§8，owner tunable）----
        private static readonly float[] ActDamageBonus = { 0.00f, 0.20f, 0.45f };
        private static readonly float[] ActHealthBonus = { 0.00f, 0.10f, 0.20f };

        // ---- 宿敌 Rank（§6，owner tunable）----
        private static readonly float[] RankHealthBonus = { 0f, 0.15f, 0.25f, 0.40f };
        private static readonly float[] RankMoveBonus = { 0f, 0f, 0.10f, 0.15f };
        private static readonly float[] RankDamageBonus = { 0f, 0f, 0f, 0.10f };

        // ---- 宿敌性格（保守首发值；只使用已验证 Stat）----
        public const float HunterMoveSpeedBonus = 0.08f;
        public const float SuppressorDamageBonus = 0.08f;
        public const float BulwarkMaxHealthBonus = 0.12f;

        // ---- Resolve 上限（§7 冻结：3+3+2+3=11）----
        public const int MaxResolveDistance = 3;
        public const int MaxResolveAmmo = 3;
        public const int MaxResolveAttribute = 2;
        public const int MaxResolveLastStand = 3;
        public const int MaxResolveTotal = 11;

        // ---- Stat 名（官方真实 key）----
        private const string StatGunDamage = "GunDamageMultiplier";
        private const string StatMeleeDamage = "MeleeDamageMultiplier";
        private const string StatGunShootSpeed = "GunShootSpeedMultiplier";
        private const string StatWalkSpeed = "WalkSpeed";
        private const string StatRunSpeed = "RunSpeed";
        private const string StatMaxHealth = "MaxHealth";

        #endregion

        #region Private Modifier Sources（exact source 身份，移除时精确匹配）

        private static readonly object AttributeLockSource = new object();
        private static readonly object AdaptationSource = new object();
        private static readonly object RevengeSource = new object();
        private static readonly object NemesisSource = new object();
        private static readonly object ActHealthSource = new object();
        private static readonly object ActDamageSource = new object();
        private static readonly object TemperamentSource = new object();

        #endregion

        #region State

        private readonly ModeGRunState _state;
        private readonly List<ZombieModeAttributeModifierRecord> _modifierRecords
            = new List<ZombieModeAttributeModifierRecord>();

        private int _resolveDistance;
        private int _resolveAmmo;
        private int _resolveAttribute;
        private int _resolveLastStand;
        private ModeGDirectDamageClass _activeAttributeLockedFamily;

        public ModeGAdaptiveCombat(ModeGRunState state)
        {
            _state = state;
        }

        public int TotalResolve { get { return _resolveDistance + _resolveAmmo + _resolveAttribute + _resolveLastStand; } }
        public int ResolveDistance { get { return _resolveDistance; } }
        public int ResolveAmmo { get { return _resolveAmmo; } }
        public int ResolveAttribute { get { return _resolveAttribute; } }
        public int ResolveLastStand { get { return _resolveLastStand; } }
        public ModeGDirectDamageClass ActiveAttributeLockedFamily { get { return _activeAttributeLockedFamily; } }

        #endregion

        #region Axis Schedule（九波固定三幕循环）

        /// <summary>
        /// 波次（0-based）对应的反制轴：距离 1/4/7、弹药 2/5/8、属性 3/6；其余 None。
        /// </summary>
        public static ModeGCounterAxis GetAxisForWave(int waveIndex0)
        {
            switch (waveIndex0)
            {
                case 1:
                case 4:
                case 7:
                    return ModeGCounterAxis.Distance;
                case 2:
                case 5:
                case 8:
                    return ModeGCounterAxis.Ammo;
                case 3:
                case 6:
                    return ModeGCounterAxis.Attribute;
                default:
                    return ModeGCounterAxis.None;
            }
        }

        #endregion

        #region Distance Axis

        /// <summary>
        /// 距离轴目标极端带：上一署名波 Close -> 本波去 Far 破解；Far -> 去 Close 破解。
        /// 距离方向只来自上一署名波最后一名 Boss 的可信终结距离（规格 §4.2），
        /// 禁止改用整波命中分布推断。
        /// </summary>
        public static ModeGDistanceVerdict GetDistanceTargetBand(ModeGDistanceVerdict verdict)
        {
            if (verdict == ModeGDistanceVerdict.Close) return ModeGDistanceVerdict.Far;
            if (verdict == ModeGDistanceVerdict.Far) return ModeGDistanceVerdict.Close;
            return ModeGDistanceVerdict.None;
        }

        /// <summary>
        /// 距离轴双门槛进度（§4.2）：目标极端带占本波可计分直伤 >=35%，
        /// 且累计直伤 >=20% 冻结聚合主 Boss 最大血量。破解判定与 HUD 共用本方法。
        /// </summary>
        public static ModeGAxisProgress EvaluateDistanceProgress(
            ModeGCombatTelemetry telemetry, ModeGDistanceVerdict verdict)
        {
            ModeGDistanceVerdict target = GetDistanceTargetBand(verdict);
            if (telemetry == null || target == ModeGDistanceVerdict.None)
                return new ModeGAxisProgress(0f, 0f, false);

            float aggregate = telemetry.CombatStartAggregatePrimaryMaxHealth;
            if (aggregate <= 0f) return new ModeGAxisProgress(0f, 0f, false);

            float share = target == ModeGDistanceVerdict.Far
                ? telemetry.FarExtremeDamageShare
                : telemetry.CloseExtremeDamageShare;
            float targetDamage = target == ModeGDistanceVerdict.Far
                ? telemetry.FarExtremeDirectDamage
                : telemetry.CloseExtremeDirectDamage;
            return new ModeGAxisProgress(share, targetDamage / aggregate, true);
        }

        /// <summary>
        /// 距离轴破解判定（双门槛）：与 <see cref="EvaluateDistanceProgress"/> 同一口径。
        /// 距离分在本波可信完成时结算，不要求末击来源或末击位置（§4.2）。
        /// </summary>
        public static bool IsDistanceAxisBroken(ModeGCombatTelemetry telemetry, ModeGDistanceVerdict verdict)
        {
            return EvaluateDistanceProgress(telemetry, verdict).ThresholdsMet;
        }

        /// <summary>
        /// Close 适应：Boss 近战伤害 +15%、最大生命 +10%。
        /// </summary>
        public bool ApplyCloseAdaptation(CharacterMainControl boss)
        {
            if (boss == null) return false;
            int checkpoint = _modifierRecords.Count;
            bool applied = TryAddVerifiedStatModifier(
                boss, StatMeleeDamage, CloseAdaptationMeleeDamageBonus, AdaptationSource)
                && TryApplyMaxHealthBonus(boss, CloseAdaptationMaxHealthBonus, AdaptationSource,
                    "Close 适应");
            if (!applied)
            {
                RollbackModifiersFrom(checkpoint);
                ModBehaviour.DevLog("[ModeG] Close 适应 Stat 验证失败，本次不保留部分强化");
            }
            return applied;
        }

        /// <summary>
        /// Far 适应：Boss Walk/Run +15%、GunShootSpeed +10%。
        /// </summary>
        public bool ApplyFarAdaptation(CharacterMainControl boss)
        {
            if (boss == null) return false;
            int checkpoint = _modifierRecords.Count;
            bool applied = TryAddVerifiedStatModifier(
                boss, StatWalkSpeed, FarAdaptationMoveSpeedBonus, AdaptationSource)
                && TryAddVerifiedStatModifier(
                    boss, StatRunSpeed, FarAdaptationMoveSpeedBonus, AdaptationSource)
                && TryAddVerifiedStatModifier(
                    boss, StatGunShootSpeed, FarAdaptationShootSpeedBonus, AdaptationSource);
            if (!applied)
            {
                RollbackModifiersFrom(checkpoint);
                ModBehaviour.DevLog("[ModeG] Far 适应 Stat 验证失败，本次不保留部分强化");
            }
            return applied;
        }

        #endregion

        #region Ammo Axis

        /// <summary>
        /// 弹药禁令选择：总样本 >=5 发；稳定 top2 威胁 + 独立 seed 威胁加权抽取；排除已点名。
        /// 返回被禁弹药 TypeID（-1 = 样本不足/无候选）。
        /// </summary>
        public static int SelectAmmoBan(ulong runSeed, int waveEpoch, ModeGCombatTelemetry telemetry)
        {
            try
            {
                if (telemetry == null) return -1;
                if (!telemetry.IsAmmoSampleValid) return -1;
                if (telemetry.TotalAmmoSamples < AmmoAxisMinSamples) return -1;

                IReadOnlyDictionary<int, double> threat = telemetry.AmmoThreatTable;
                IReadOnlyDictionary<int, int> shots = telemetry.AmmoShotCountTable;
                if (threat == null || threat.Count == 0) return -1;

                // 候选：>=2 发且威胁份额 >=15%，或单发份额 >=35%；排除已点名。
                List<int> candidates = new List<int>();
                double totalThreat = 0.0;
                foreach (KeyValuePair<int, double> kv in threat) totalThreat += kv.Value;
                if (totalThreat <= 0.0) return -1;

                foreach (KeyValuePair<int, double> kv in threat)
                {
                    int ammoId = kv.Key;
                    if (telemetry.WasAmmoNamed(ammoId)) continue;
                    int shotCount;
                    if (!shots.TryGetValue(ammoId, out shotCount) || shotCount <= 0) continue;
                    double share = kv.Value / totalThreat;
                    if (shotCount >= 2 ? share < 0.15 : share < 0.35) continue;
                    candidates.Add(ammoId);
                }
                if (candidates.Count == 0) return -1;

                // 稳定 top2（按威胁降序、TypeID 升序破平）后威胁加权抽取
                candidates.Sort((a, b) =>
                {
                    double ta, tb;
                    threat.TryGetValue(a, out ta);
                    threat.TryGetValue(b, out tb);
                    int cmp = tb.CompareTo(ta);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });
                if (candidates.Count > 2)
                {
                    candidates.RemoveRange(2, candidates.Count - 2);
                }

                List<int> weights = new List<int>(candidates.Count);
                for (int i = 0; i < candidates.Count; i++)
                {
                    weights.Add(ModeGDeterministicRandom.QuantizeThreatWeight(
                        threat[candidates[i]] / totalThreat));
                }

                ulong state = ModeGDeterministicRandom.SeedDomain(runSeed,
                    ModeGDeterministicRandom.DomainConstants.AmmoBan, string.Empty, waveEpoch);
                int idx = ModeGDeterministicRandom.WeightedSelect(ref state, weights);
                return candidates[idx];
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] SelectAmmoBan 异常: " + e.Message);
                return -1;
            }
        }

        #endregion

        #region Attribute Axis

        /// <summary>
        /// 属性封锁：封锁玩家占比更高的一侧（Gun/Melee），PercentageMultiply ×0.75，order 200，私有 source。
        /// </summary>
        /// <summary>
        /// 预测/判定将被封锁的武器系：上一宿敌波玩家直接伤害的主导侧（§4.4）。
        /// 两侧相等或都为 0 时返回 NotScoreable（不封锁、该分不可得）。
        /// 唯一来源：ApplyAttributeLock 与休整期 HUD 预告必须共用本方法，避免预告与实际封锁不一致。
        /// </summary>
        public static ModeGDirectDamageClass PredictAttributeLockFamily(ModeGCombatTelemetry telemetry)
        {
            if (telemetry == null || telemetry.TotalDirectDamage <= 0f) return ModeGDirectDamageClass.NotScoreable;
            if (Math.Abs(telemetry.GunDirectDamage - telemetry.MeleeDirectDamage) < 0.0001f)
                return ModeGDirectDamageClass.NotScoreable;
            return telemetry.GunDirectDamage > telemetry.MeleeDirectDamage
                ? ModeGDirectDamageClass.Gun
                : ModeGDirectDamageClass.Melee;
        }

        public bool ApplyAttributeLock(CharacterMainControl player, ModeGCombatTelemetry telemetry)
        {
            if (player == null || telemetry == null) return false;
            ClearAttributeLock();

            ModeGDirectDamageClass family = PredictAttributeLockFamily(telemetry);
            if (family == ModeGDirectDamageClass.NotScoreable) return false;
            string statName = family == ModeGDirectDamageClass.Gun ? StatGunDamage : StatMeleeDamage;
            if (player.CharacterItem == null) return false;

            try
            {
                Stat stat = player.CharacterItem.GetStat(statName);
                if (stat == null) return false;
                float before = stat.Value;
                if (float.IsNaN(before) || float.IsInfinity(before)) return false;

                Modifier modifier = new Modifier(
                    ModifierType.PercentageMultiply, AttributeLockValue, true, 200, AttributeLockSource);
                stat.AddModifier(modifier);
                float after = stat.Value;
                float expected = before * (1f + AttributeLockValue);
                float tolerance = Math.Max(0.001f, Math.Abs(expected) * 0.01f);
                if (float.IsNaN(after) || float.IsInfinity(after) || Math.Abs(after - expected) > tolerance)
                {
                    stat.RemoveModifier(modifier);
                    ModBehaviour.DevLog("[ModeG] 属性封锁回读验证失败，已精确撤销: " + statName);
                    return false;
                }

                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = player.CharacterItem;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                _modifierRecords.Add(record);
                _activeAttributeLockedFamily = family;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] 属性封锁添加失败: " + statName + ", " + e.Message);
                ClearAttributeLock();
                return false;
            }
        }

        /// <summary>
        /// 被封锁侧的相反武器系（即破解所需的一侧）。未封锁时返回 NotScoreable。
        /// </summary>
        public ModeGDirectDamageClass AttributeBreakFamily
        {
            get
            {
                if (_activeAttributeLockedFamily == ModeGDirectDamageClass.Gun)
                    return ModeGDirectDamageClass.Melee;
                if (_activeAttributeLockedFamily == ModeGDirectDamageClass.Melee)
                    return ModeGDirectDamageClass.Gun;
                return ModeGDirectDamageClass.NotScoreable;
            }
        }

        /// <summary>
        /// 属性轴双门槛进度（§4.4）：相反武器系占本波 Gun+Melee 直伤 >=35%，
        /// 且累计直伤 >=20% 冻结聚合主 Boss 最大血量。破解判定与 HUD 共用本方法。
        /// 相反系直接终结是额外条件，不并入本进度（由 IsAttributeAxisBroken 单独校验）。
        /// </summary>
        public ModeGAxisProgress EvaluateAttributeProgress(ModeGCombatTelemetry telemetry)
        {
            ModeGDirectDamageClass opposite = AttributeBreakFamily;
            if (telemetry == null || opposite == ModeGDirectDamageClass.NotScoreable)
                return new ModeGAxisProgress(0f, 0f, false);

            float aggregate = telemetry.CombatStartAggregatePrimaryMaxHealth;
            if (aggregate <= 0f) return new ModeGAxisProgress(0f, 0f, false);

            bool gun = opposite == ModeGDirectDamageClass.Gun;
            float damage = gun ? telemetry.GunDirectDamage : telemetry.MeleeDirectDamage;
            float share = gun ? telemetry.GunDamageShare : telemetry.MeleeDamageShare;
            return new ModeGAxisProgress(share, damage / aggregate, true);
        }

        /// <summary>
        /// 属性轴破解判定：双门槛达标且由相反武器系可计分直伤终结（§4.4）。
        /// </summary>
        public bool IsAttributeAxisBroken(
            ModeGCombatTelemetry telemetry, ModeGDirectDamageClass terminalFamily)
        {
            ModeGDirectDamageClass opposite = AttributeBreakFamily;
            if (opposite == ModeGDirectDamageClass.NotScoreable || terminalFamily != opposite) return false;
            return EvaluateAttributeProgress(telemetry).ThresholdsMet;
        }

        /// <summary>只移除属性封锁 source，保留 Boss 适应、宿敌和复仇 Modifier。</summary>
        public void ClearAttributeLock()
        {
            RemoveModifiersFromSource(AttributeLockSource);
            _activeAttributeLockedFamily = ModeGDirectDamageClass.NotScoreable;
        }

        #endregion

        #region Act Multipliers（幕倍率）

        /// <summary>
        /// 幕 Boss 伤害加成（I +0、II +20%、III +45%；owner tunable）。
        /// </summary>
        public static float GetActDamageBonus(int actIndex)
        {
            if (actIndex < 0) return ActDamageBonus[0];
            if (actIndex >= ActDamageBonus.Length) return ActDamageBonus[ActDamageBonus.Length - 1];
            return ActDamageBonus[actIndex];
        }

        /// <summary>
        /// 幕 Boss 生命加成（I +0、II +10%、III +20%；owner tunable）。
        /// </summary>
        public static float GetActHealthBonus(int actIndex)
        {
            if (actIndex < 0) return ActHealthBonus[0];
            if (actIndex >= ActHealthBonus.Length) return ActHealthBonus[ActHealthBonus.Length - 1];
            return ActHealthBonus[actIndex];
        }

        public bool ApplyActHealthBonus(CharacterMainControl boss, int actIndex)
        {
            float bonus = GetActHealthBonus(actIndex);
            return bonus <= 0f || TryApplyMaxHealthBonus(boss, bonus, ActHealthSource, "幕次生命");
        }

        public bool ApplyActDamageBonus(CharacterMainControl boss, int actIndex)
        {
            float bonus = GetActDamageBonus(actIndex);
            if (bonus <= 0f) return true;
            int checkpoint = _modifierRecords.Count;
            bool applied = TryAddVerifiedStatModifier(
                boss, StatGunDamage, bonus, ActDamageSource)
                && TryAddVerifiedStatModifier(
                    boss, StatMeleeDamage, bonus, ActDamageSource);
            if (!applied)
            {
                RollbackModifiersFrom(checkpoint);
                ModBehaviour.DevLog("[ModeG] 幕次伤害 Stat 验证失败，本次不保留部分强化");
            }
            return applied;
        }

        #endregion

        #region Nemesis Rank（宿敌倍率）

        /// <summary>
        /// 宿敌 Rank 强化（R1 生命 +15%；R2 +25%+WalkRun +10%；R3 +40%+WalkRun +15%+枪/近战 +10%；owner tunable）。
        /// </summary>
        public bool ApplyNemesisRank(CharacterMainControl nemesis, int rank)
        {
            if (nemesis == null || rank < 1) return false;
            if (rank > ModeGNemesisPersistence.MaxRank) rank = ModeGNemesisPersistence.MaxRank;

            int checkpoint = _modifierRecords.Count;
            bool applied = true;
            if (RankHealthBonus[rank] > 0f)
                applied = TryApplyMaxHealthBonus(
                    nemesis, RankHealthBonus[rank], NemesisSource, "宿敌 Rank");

            if (applied && RankMoveBonus[rank] > 0f)
            {
                applied = TryAddVerifiedStatModifier(
                    nemesis, StatWalkSpeed, RankMoveBonus[rank], NemesisSource)
                    && TryAddVerifiedStatModifier(
                        nemesis, StatRunSpeed, RankMoveBonus[rank], NemesisSource);
            }
            if (applied && RankDamageBonus[rank] > 0f)
            {
                applied = TryAddVerifiedStatModifier(
                    nemesis, StatGunDamage, RankDamageBonus[rank], NemesisSource)
                    && TryAddVerifiedStatModifier(
                        nemesis, StatMeleeDamage, RankDamageBonus[rank], NemesisSource);
            }
            if (!applied)
            {
                RollbackModifiersFrom(checkpoint);
                ModBehaviour.DevLog("[ModeG] 宿敌 Rank Stat 验证失败，本次不保留部分强化: R" + rank);
            }
            return applied;
        }

        public static ModeGNemesisTemperament SelectNemesisTemperament(ulong runSeed)
        {
            ulong state = ModeGDeterministicRandom.SeedDomain(runSeed,
                ModeGDeterministicRandom.DomainConstants.Temperament, 0);
            return (ModeGNemesisTemperament)(1 + ModeGDeterministicRandom.NextInt(ref state, 3));
        }

        public bool ApplyNemesisTemperament(CharacterMainControl nemesis,
            ModeGNemesisTemperament temperament)
        {
            if (nemesis == null || temperament == ModeGNemesisTemperament.None) return true;
            int checkpoint = _modifierRecords.Count;
            bool applied;
            if (temperament == ModeGNemesisTemperament.Hunter)
            {
                applied = TryAddVerifiedStatModifier(nemesis, StatWalkSpeed,
                    HunterMoveSpeedBonus, TemperamentSource)
                    && TryAddVerifiedStatModifier(nemesis, StatRunSpeed,
                        HunterMoveSpeedBonus, TemperamentSource);
            }
            else if (temperament == ModeGNemesisTemperament.Suppressor)
            {
                applied = TryAddVerifiedStatModifier(nemesis, StatGunDamage,
                    SuppressorDamageBonus, TemperamentSource)
                    && TryAddVerifiedStatModifier(nemesis, StatMeleeDamage,
                        SuppressorDamageBonus, TemperamentSource);
            }
            else
            {
                applied = TryApplyMaxHealthBonus(nemesis, BulwarkMaxHealthBonus,
                    TemperamentSource, "堡垒性格");
            }

            if (!applied)
            {
                RollbackModifiersFrom(checkpoint);
                ModBehaviour.DevLog("[ModeG] 宿敌性格 Stat 验证失败，本次仅关闭性格强化: " + temperament);
            }
            return applied;
        }

        public static string GetTemperamentDisplayName(ModeGNemesisTemperament temperament)
        {
            switch (temperament)
            {
                case ModeGNemesisTemperament.Hunter: return L10n.T("追猎者", "Hunter");
                case ModeGNemesisTemperament.Suppressor: return L10n.T("压制者", "Suppressor");
                case ModeGNemesisTemperament.Bulwark: return L10n.T("堡垒", "Bulwark");
                default: return string.Empty;
            }
        }

        #endregion

        #region Last Stand / Revenge Buff（§5）

        /// <summary>
        /// 复仇 buff（Last Stand 超时后幸存者）：
        /// 回血 20%（Health.AddHealth(MaxHealth*0.2f)，禁用 CharacterMainControl.AddHealth——后者乘 HealGain）
        /// + Walk/Run +15% + 枪/近战 +10%。
        /// </summary>
        public void ApplyRevengeBuff(CharacterMainControl survivor)
        {
            if (survivor == null) return;
            try
            {
                if (survivor.Health != null)
                {
                    survivor.Health.AddHealth(survivor.Health.MaxHealth * RevengeHealShare);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] 复仇 buff 回血失败: " + e.Message);
            }
            TryAddStatModifier(survivor, StatWalkSpeed, RevengeMoveSpeedBonus, RevengeSource);
            TryAddStatModifier(survivor, StatRunSpeed, RevengeMoveSpeedBonus, RevengeSource);
            TryAddStatModifier(survivor, StatGunDamage, RevengeDamageBonus, RevengeSource);
            TryAddStatModifier(survivor, StatMeleeDamage, RevengeDamageBonus, RevengeSource);
        }

        #endregion

        #region Resolve Accounting（上限 11 = 3+3+2+3）

        /// <summary>
        /// 记录一次 Resolve（按轴钳制上限；总上限 11）。
        /// </summary>
        public bool RecordResolve(ModeGCounterAxis axis)
        {
            if (TotalResolve >= MaxResolveTotal) return false;
            switch (axis)
            {
                case ModeGCounterAxis.Distance:
                    if (_resolveDistance >= MaxResolveDistance) return false;
                    _resolveDistance++;
                    return true;
                case ModeGCounterAxis.Ammo:
                    if (_resolveAmmo >= MaxResolveAmmo) return false;
                    _resolveAmmo++;
                    return true;
                case ModeGCounterAxis.Attribute:
                    if (_resolveAttribute >= MaxResolveAttribute) return false;
                    _resolveAttribute++;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 记录一次 Last Stand Resolve（独立通道，上限 3）。
        /// </summary>
        public bool RecordLastStandResolve()
        {
            if (TotalResolve >= MaxResolveTotal) return false;
            if (_resolveLastStand >= MaxResolveLastStand) return false;
            _resolveLastStand++;
            return true;
        }

        #endregion

        #region Modifier Machinery（原值恢复）

        private bool TryAddStatModifier(CharacterMainControl character, string statName, float percent, object source)
        {
            return TryAddExactStatModifier(character, statName, percent, source);
        }

        private bool TryAddVerifiedStatModifier(
            CharacterMainControl character, string statName, float value, object source)
        {
            if (character == null || character.CharacterItem == null) return false;
            Stat stat = character.CharacterItem.GetStat(statName);
            if (stat == null) return false;
            float before = stat.Value;
            if (float.IsNaN(before) || float.IsInfinity(before)) return false;
            int checkpoint = _modifierRecords.Count;
            if (!TryAddExactStatModifier(character, statName, value, source)) return false;
            float after = stat.Value;
            bool applied = !float.IsNaN(after) && !float.IsInfinity(after) && after > before;
            if (!applied) RollbackModifiersFrom(checkpoint);
            return applied;
        }

        private bool TryApplyMaxHealthBonus(
            CharacterMainControl character, float bonus, object source, string label)
        {
            if (character == null || character.CharacterItem == null || character.Health == null
                || bonus <= 0f) return false;
            int checkpoint = _modifierRecords.Count;
            try
            {
                Stat stat = character.CharacterItem.GetStat(StatMaxHealth);
                if (stat == null) return false;
                float beforeMax = character.Health.MaxHealth;
                float beforeCurrent = character.Health.CurrentHealth;
                if (beforeMax <= 0f || float.IsNaN(beforeMax) || float.IsInfinity(beforeMax)) return false;

                Modifier modifier = new Modifier(ModifierType.PercentageAdd, bonus, source);
                stat.AddModifier(modifier);
                float afterMax = character.Health.MaxHealth;
                if (float.IsNaN(afterMax) || float.IsInfinity(afterMax) || afterMax <= beforeMax)
                {
                    stat.RemoveModifier(modifier);
                    return false;
                }

                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = character.CharacterItem;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = StatMaxHealth;
                _modifierRecords.Add(record);

                float ratio = Mathf.Clamp01(beforeCurrent / beforeMax);
                character.Health.SetHealth(afterMax * ratio);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] " + label + "最大生命 Modifier 失败: " + e.Message);
                RollbackModifiersFrom(checkpoint);
                return false;
            }
        }

        /// <summary>
        /// 加运行时 Modifier（PercentageAdd 用于加成；属性封锁用 PercentageMultiply）。
        /// </summary>
        private bool TryAddExactStatModifier(CharacterMainControl character, string statName, float value, object source)
        {
            if (character == null || character.CharacterItem == null || string.IsNullOrEmpty(statName)) return false;
            try
            {
                Stat stat = character.CharacterItem.GetStat(statName);
                if (stat == null) return false;

                // 属性封锁：PercentageMultiply（order 200）；其余加成：PercentageAdd
                ModifierType type = (source == AttributeLockSource)
                    ? ModifierType.PercentageMultiply
                    : ModifierType.PercentageAdd;
                Modifier modifier = new Modifier(type, value, source);
                stat.AddModifier(modifier);

                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = character.CharacterItem;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                _modifierRecords.Add(record);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] Modifier 添加失败: " + statName + ", " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 移除全部本模块添加的 Modifier（End/清理路径；原值恢复）。
        /// </summary>
        public void RestoreAllModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(_modifierRecords, "[ModeG] AdaptiveCombat");
            _activeAttributeLockedFamily = ModeGDirectDamageClass.NotScoreable;
        }

        internal int CreateModifierCheckpoint()
        {
            return _modifierRecords.Count;
        }

        internal void RollbackToCheckpoint(int checkpoint)
        {
            RollbackModifiersFrom(checkpoint);
        }

        private void RemoveModifiersFromSource(object source)
        {
            for (int i = _modifierRecords.Count - 1; i >= 0; i--)
            {
                ZombieModeAttributeModifierRecord record = _modifierRecords[i];
                if (record == null || record.Modifier == null
                    || !ReferenceEquals(record.Modifier.Source, source)) continue;
                try
                {
                    Stat stat = record.Stat;
                    if (stat == null && record.CharacterItem != null)
                        stat = record.CharacterItem.GetStat(record.StatName);
                    if (stat != null) stat.RemoveModifier(record.Modifier);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeG] Modifier 回滚失败: " + e.Message);
                }
                _modifierRecords.RemoveAt(i);
            }
        }

        private void RollbackModifiersFrom(int startIndex)
        {
            if (startIndex < 0) startIndex = 0;
            for (int i = _modifierRecords.Count - 1; i >= startIndex; i--)
            {
                ZombieModeAttributeModifierRecord record = _modifierRecords[i];
                try
                {
                    Stat stat = record != null ? record.Stat : null;
                    if (stat == null && record != null && record.CharacterItem != null)
                        stat = record.CharacterItem.GetStat(record.StatName);
                    if (stat != null && record != null && record.Modifier != null)
                        stat.RemoveModifier(record.Modifier);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeG] Modifier 清理失败: " + e.Message);
                }
                _modifierRecords.RemoveAt(i);
            }
        }

        #endregion
    }
}
