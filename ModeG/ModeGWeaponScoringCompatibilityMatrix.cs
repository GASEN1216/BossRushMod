using System;

namespace BossRush
{
    /// <summary>
    /// Mode G 武器计分兼容矩阵条目（规格 §20 第 9 条）。
    /// 逐稳定 TypeID/能力 key 冻结：普通攻击 family/计分、被动计分、terminal credit、
    /// 是否真实消费 Gun/Melee Stat、exact suppression scope 与 verification revision。
    /// </summary>
    internal struct ModeGWeaponScoringEntry
    {
        /// <summary>稳定能力 key（归因/遥测用；不参与背包扫描）</summary>
        public readonly string StableKey;
        /// <summary>稳定 TypeID（0 = 纯套装/图腾能力 key）</summary>
        public readonly int WeaponTypeId;
        /// <summary>普通攻击 family（None = 无普通攻击输出，如套装/图腾被动）</summary>
        public readonly WeaponFamily NormalAttackFamily;
        /// <summary>普通攻击是否计分</summary>
        public readonly bool NormalAttackScoreable;
        /// <summary>被动/附伤是否计分（明确登记的被动不计分仍造成原伤害并可推进）</summary>
        public readonly bool PassiveScoreable;
        /// <summary>terminal credit 是否允许（Last Stand 处决归因）</summary>
        public readonly bool TerminalCreditAllowed;
        /// <summary>是否真实消费 GunDamageMultiplier</summary>
        public readonly bool ConsumesGunStat;
        /// <summary>是否真实消费 MeleeDamageMultiplier</summary>
        public readonly bool ConsumesMeleeStat;
        /// <summary>exact suppression scope（空串 = 无 suppression）</summary>
        public readonly string SuppressionScope;
        /// <summary>本条目验证时绑定的 verification revision（过期 fail-closed）</summary>
        public readonly string VerificationRevision;

        public ModeGWeaponScoringEntry(
            string stableKey, int weaponTypeId,
            WeaponFamily normalAttackFamily, bool normalAttackScoreable,
            bool passiveScoreable, bool terminalCreditAllowed,
            bool consumesGunStat, bool consumesMeleeStat,
            string suppressionScope, string verificationRevision)
        {
            StableKey = stableKey;
            WeaponTypeId = weaponTypeId;
            NormalAttackFamily = normalAttackFamily;
            NormalAttackScoreable = normalAttackScoreable;
            PassiveScoreable = passiveScoreable;
            TerminalCreditAllowed = terminalCreditAllowed;
            ConsumesGunStat = consumesGunStat;
            ConsumesMeleeStat = consumesMeleeStat;
            SuppressionScope = suppressionScope ?? string.Empty;
            VerificationRevision = verificationRevision;
        }
    }

    /// <summary>
    /// Mode G 武器计分兼容矩阵（规格 §20 第 9 条；人工签署冻结结构）。
    ///
    /// 硬约束：
    /// - 矩阵不改变分类器（ModeGDirectDamageClassifier）、不扫描玩家背包；
    /// - revision 过期、普通攻击预期不符（Gun/Melee 普通攻击不可计分）时正式入口 fail-closed；
    /// - 明确登记的套装/图腾/被动附伤不计分仍造成原伤害并可推进（豁免「主要输出全部不可计分」规则）；
    /// - 条目绑定 verification revision，游戏更新后必须重新验证并整体递增 revision。
    ///
    /// TypeID 依据：docs/Bossrush使用物品ID表.md（500005 龙息、500034 焚皇断界戟、
    /// 500041 霜之哀伤、500044 噬魂挽歌、500048 毒蛇匕首、500052 雷电戒指、500013 逆鳞；
    /// DragonSet = 500003+500004 套装被动，ThunderSet = 500055+500056 套装被动）。
    /// </summary>
    internal static class ModeGWeaponScoringCompatibilityMatrix
    {
        /// <summary>
        /// 矩阵绑定的 verification revision（过期 fail-closed；与 ModeGAvailability 冻结值一致，
        /// 游戏更新后必须重新人工验证全部条目并同步递增）。
        /// </summary>
        public const string RequiredVerificationRevision = "2026-08-10.v1";

        /// <summary>首发九件武器/能力 key 的冻结条目（只追加，不重排）。</summary>
        private static readonly ModeGWeaponScoringEntry[] Entries =
        {
            // 龙息：枪械直射计分；龙焰灼烧 Buff(500006) DoT 为登记被动不计分
            new ModeGWeaponScoringEntry(
                "DragonBreath", 500005,
                WeaponFamily.Gun, true, false, true,
                true, false,
                "DragonFlameBurn_Buff_DoT", RequiredVerificationRevision),

            // 焚皇断界戟：近战连招直伤计分；连招冲击波属 buff/effect 通道不计分
            new ModeGWeaponScoringEntry(
                "FenHuangHalberd", 500034,
                WeaponFamily.Melee, true, false, true,
                false, true,
                string.Empty, RequiredVerificationRevision),

            // 霜之哀伤：近战直伤计分；右键亡灵召唤为召唤物输出不计分
            new ModeGWeaponScoringEntry(
                "Frostmourne", 500041,
                WeaponFamily.Melee, true, false, true,
                false, true,
                "Frostmourne_UndeadSummon_Minion", RequiredVerificationRevision),

            // 噬魂挽歌（幽灵女巫镰刀）：普通挥击 Melee 计分且受属性封锁；
            // 领域 tick 数值不变，仅在 exact receiver.Hurt suppression scope 内排除 Boss telemetry
            new ModeGWeaponScoringEntry(
                "PhantomWitchScythe", 500044,
                WeaponFamily.Melee, true, false, true,
                false, true,
                "CurseRealm_Tick_receiver.Hurt_ModeGTelemetryScope", RequiredVerificationRevision),

            // 毒蛇匕首：近战直伤计分；满层毒爆发为 buff/effect 通道不计分
            new ModeGWeaponScoringEntry(
                "ViperDagger", 500048,
                WeaponFamily.Melee, true, false, true,
                false, true,
                "ViperDagger_PoisonBurst_BuffEffect", RequiredVerificationRevision),

            // 雷电戒指：图腾被动释放（new DamageInfo(Main)、TypeID 0），
            // 登记不计分但伤害/推进不变；无普通攻击输出
            new ModeGWeaponScoringEntry(
                "ThunderRing", 500052,
                WeaponFamily.None, false, false, false,
                false, false,
                "ThunderRing_Release_DamageInfoMain_TypeId0", RequiredVerificationRevision),

            // 逆鳞：图腾被动反制（new DamageInfo(Main)、TypeID 0），登记不计分；无普通攻击输出
            new ModeGWeaponScoringEntry(
                "ReverseScale", 500013,
                WeaponFamily.None, false, false, false,
                false, false,
                "ReverseScale_Retaliation_DamageInfoMain_TypeId0", RequiredVerificationRevision),

            // 龙裔套装（赤龙首+焰鳞甲）：套装燃烧被动不计分，伤害/推进不变；无普通攻击输出
            new ModeGWeaponScoringEntry(
                "DragonSet", 0,
                WeaponFamily.None, false, false, false,
                false, false,
                "DragonSet_BurnPassive_BuffEffect", RequiredVerificationRevision),

            // 雷霆套装（雷神之角+雷霆战甲）：受击电击 AOE 被动不计分，伤害/推进不变；无普通攻击输出
            new ModeGWeaponScoringEntry(
                "ThunderSet", 0,
                WeaponFamily.None, false, false, false,
                false, false,
                "ThunderSet_ShockAoE_BuffEffect", RequiredVerificationRevision),
        };

        /// <summary>冻结条目只读访问（索引序即登记序）。</summary>
        public static int EntryCount { get { return Entries.Length; } }

        /// <summary>
        /// 按稳定 TypeID 查询条目（0 = 无 TypeID 条目，按 key 查询）。no-throw。
        /// </summary>
        public static bool TryGetEntryByTypeId(int weaponTypeId, out ModeGWeaponScoringEntry entry)
        {
            entry = default(ModeGWeaponScoringEntry);
            if (weaponTypeId <= 0) return false;
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].WeaponTypeId == weaponTypeId)
                {
                    entry = Entries[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 按稳定 key 查询条目（Ordinal 精确匹配）。no-throw。
        /// </summary>
        public static bool TryGetEntryByKey(string stableKey, out ModeGWeaponScoringEntry entry)
        {
            entry = default(ModeGWeaponScoringEntry);
            if (string.IsNullOrEmpty(stableKey)) return false;
            for (int i = 0; i < Entries.Length; i++)
            {
                if (string.Equals(Entries[i].StableKey, stableKey, StringComparison.Ordinal))
                {
                    entry = Entries[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 矩阵完整性验证（正式入口 fail-closed 用）。no-throw；异常视为失败。
        /// 失败条件：
        /// 1. 任一条目 verification revision 与 RequiredVerificationRevision 不一致（过期 fail-closed）；
        /// 2. 任一条目 StableKey 为空（登记损坏）；
        /// 3. Gun/Melee 普通攻击武器的普通攻击不可计分（普通攻击预期不符）。
        /// 套装/图腾被动登记不计分属豁免项（明确登记的被动不计分仍允许）。
        /// </summary>
        public static bool IsMatrixValid(out string failReason)
        {
            failReason = null;
            try
            {
                if (string.IsNullOrEmpty(RequiredVerificationRevision))
                {
                    failReason = "RequiredVerificationRevision 为空";
                    return false;
                }

                for (int i = 0; i < Entries.Length; i++)
                {
                    ModeGWeaponScoringEntry e = Entries[i];
                    if (string.IsNullOrEmpty(e.StableKey))
                    {
                        failReason = "条目 " + i + " StableKey 为空";
                        return false;
                    }
                    if (!string.Equals(e.VerificationRevision, RequiredVerificationRevision,
                        StringComparison.Ordinal))
                    {
                        failReason = "条目 " + e.StableKey + " revision 过期: " + e.VerificationRevision;
                        return false;
                    }
                    if (e.NormalAttackFamily != WeaponFamily.None && !e.NormalAttackScoreable)
                    {
                        failReason = "条目 " + e.StableKey + " 普通攻击预期不符（" + e.NormalAttackFamily + " 不可计分）";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                failReason = "矩阵验证异常: " + ex.Message;
                return false;
            }
        }
    }
}
