using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

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
        /// <summary>破解门槛：分类器接受的 Gun/Melee 伤害占比 >=35%（owner tunable）</summary>
        public const float DistanceBreakDamageShare = 0.35f;
        /// <summary>破解门槛：>=20% combatStartAggregatePrimaryMaxHealth 贡献（owner tunable，待确认）</summary>
        public const float DistanceBreakHealthContribution = 0.20f;

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

        #endregion

        #region Private Modifier Sources（exact source 身份，移除时精确匹配）

        private static readonly object AttributeLockSource = new object();
        private static readonly object AdaptationSource = new object();
        private static readonly object RevengeSource = new object();
        private static readonly object NemesisSource = new object();

        #endregion

        #region State

        private readonly ModeGRunState _state;
        private readonly List<ZombieModeAttributeModifierRecord> _modifierRecords
            = new List<ZombieModeAttributeModifierRecord>();

        private int _resolveDistance;
        private int _resolveAmmo;
        private int _resolveAttribute;
        private int _resolveLastStand;

        public ModeGAdaptiveCombat(ModeGRunState state)
        {
            _state = state;
        }

        public int TotalResolve { get { return _resolveDistance + _resolveAmmo + _resolveAttribute + _resolveLastStand; } }
        public int ResolveDistance { get { return _resolveDistance; } }
        public int ResolveAmmo { get { return _resolveAmmo; } }
        public int ResolveAttribute { get { return _resolveAttribute; } }
        public int ResolveLastStand { get { return _resolveLastStand; } }

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
        /// 距离轴判定：极端带占比显著时给出 Close/Far 适应指令。
        /// </summary>
        public static ModeGDistanceVerdict EvaluateDistanceAxis(ModeGCombatTelemetry telemetry)
        {
            if (telemetry == null) return ModeGDistanceVerdict.None;
            if (telemetry.CloseExtremeShare >= DistanceBreakDamageShare) return ModeGDistanceVerdict.Close;
            if (telemetry.FarExtremeShare >= DistanceBreakDamageShare) return ModeGDistanceVerdict.Far;
            return ModeGDistanceVerdict.None;
        }

        /// <summary>
        /// 距离轴破解判定（双门槛）：分类器伤害占比 >=35% 且对聚合主 Boss 血量贡献 >=20%。
        /// </summary>
        public static bool IsDistanceAxisBroken(ModeGCombatTelemetry telemetry, ModeGDistanceVerdict verdict)
        {
            if (telemetry == null || verdict == ModeGDistanceVerdict.None) return false;
            float share = (verdict == ModeGDistanceVerdict.Close)
                ? telemetry.MeleeDamageShare   // Close 适应的破解方：近战直伤占比
                : telemetry.GunDamageShare;    // Far 适应的破解方：枪械直伤占比
            if (share < DistanceBreakDamageShare) return false;
            // 血量贡献以总直伤/聚合血量近似（贡献门槛 owner tunable）
            if (telemetry.CombatStartAggregatePrimaryMaxHealth <= 0f) return false;
            float contribution = telemetry.TotalDirectDamage / telemetry.CombatStartAggregatePrimaryMaxHealth;
            return contribution >= DistanceBreakHealthContribution;
        }

        /// <summary>
        /// Close 适应：Boss 近战伤害 +15%、最大生命 +10%（Health.AddHealth 同步抬高当前/上限）。
        /// </summary>
        public void ApplyCloseAdaptation(CharacterMainControl boss)
        {
            if (boss == null) return;
            TryAddStatModifier(boss, StatMeleeDamage, CloseAdaptationMeleeDamageBonus, AdaptationSource);
            try
            {
                if (boss.Health != null)
                {
                    boss.Health.AddHealth(boss.Health.MaxHealth * CloseAdaptationMaxHealthBonus);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] Close 适应生命加成失败: " + e.Message);
            }
        }

        /// <summary>
        /// Far 适应：Boss Walk/Run +15%、GunShootSpeed +10%。
        /// </summary>
        public void ApplyFarAdaptation(CharacterMainControl boss)
        {
            if (boss == null) return;
            TryAddStatModifier(boss, StatWalkSpeed, FarAdaptationMoveSpeedBonus, AdaptationSource);
            TryAddStatModifier(boss, StatRunSpeed, FarAdaptationMoveSpeedBonus, AdaptationSource);
            TryAddStatModifier(boss, StatGunShootSpeed, FarAdaptationShootSpeedBonus, AdaptationSource);
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
                if (telemetry.TotalAmmoSamples < AmmoAxisMinSamples) return -1;

                IReadOnlyDictionary<int, double> threat = telemetry.AmmoThreatTable;
                IReadOnlyDictionary<int, int> shots = telemetry.AmmoShotCountTable;
                if (threat == null || threat.Count == 0) return -1;

                // 候选：样本 >=2 发且威胁份额 >=15%（规格 §4.3 候选条件）；排除已点名
                List<int> candidates = new List<int>();
                List<int> weights = new List<int>();
                double totalThreat = 0.0;
                foreach (KeyValuePair<int, double> kv in threat) totalThreat += kv.Value;
                if (totalThreat <= 0.0) return -1;

                foreach (KeyValuePair<int, double> kv in threat)
                {
                    int ammoId = kv.Key;
                    int shotCount;
                    if (!shots.TryGetValue(ammoId, out shotCount) || shotCount < 2) continue;
                    double share = kv.Value / totalThreat;
                    if (share < 0.15) continue;
                    candidates.Add(ammoId);
                    weights.Add(ModeGDeterministicRandom.QuantizeThreatWeight(share));
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
                    weights.RemoveRange(2, weights.Count - 2);
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

        /// <summary>
        /// 单次违规伤害 clamp：10% 聚合血量（禁弹违规不直接扣血，只钳制计分上限）。
        /// </summary>
        public static float ClampAmmoViolationDamage(float damage, float aggregateMaxHealth)
        {
            float cap = aggregateMaxHealth * AmmoBanClampShare;
            return damage > cap ? cap : damage;
        }

        #endregion

        #region Attribute Axis

        /// <summary>
        /// 属性封锁：封锁玩家占比更高的一侧（Gun/Melee），PercentageMultiply ×0.75，order 200，私有 source。
        /// </summary>
        public bool ApplyAttributeLock(CharacterMainControl player, ModeGCombatTelemetry telemetry)
        {
            if (player == null || telemetry == null) return false;
            string statName = telemetry.GunDirectDamage >= telemetry.MeleeDirectDamage
                ? StatGunDamage
                : StatMeleeDamage;
            return TryAddExactStatModifier(player, statName, AttributeLockValue, AttributeLockSource);
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

        #endregion

        #region Nemesis Rank（宿敌倍率）

        /// <summary>
        /// 宿敌 Rank 强化（R1 生命 +15%；R2 +25%+WalkRun +10%；R3 +40%+WalkRun +15%+枪/近战 +10%；owner tunable）。
        /// </summary>
        public void ApplyNemesisRank(CharacterMainControl nemesis, int rank)
        {
            if (nemesis == null || rank < 1) return;
            if (rank > ModeGNemesisPersistence.MaxRank) rank = ModeGNemesisPersistence.MaxRank;

            try
            {
                if (nemesis.Health != null && RankHealthBonus[rank] > 0f)
                {
                    nemesis.Health.AddHealth(nemesis.Health.MaxHealth * RankHealthBonus[rank]);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] 宿敌 Rank 生命加成失败: " + e.Message);
            }

            if (RankMoveBonus[rank] > 0f)
            {
                TryAddStatModifier(nemesis, StatWalkSpeed, RankMoveBonus[rank], NemesisSource);
                TryAddStatModifier(nemesis, StatRunSpeed, RankMoveBonus[rank], NemesisSource);
            }
            if (RankDamageBonus[rank] > 0f)
            {
                TryAddStatModifier(nemesis, StatGunDamage, RankDamageBonus[rank], NemesisSource);
                TryAddStatModifier(nemesis, StatMeleeDamage, RankDamageBonus[rank], NemesisSource);
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
        }

        #endregion
    }
}
