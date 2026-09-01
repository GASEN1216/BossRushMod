// ============================================================================
// AffixDefinitions.cs - 词缀锻造（AffixForge）的数值与文案单点
// ============================================================================
// 职责：
//   - 定义 12 条词缀（普通 5 / 稀有 4 / 诅咒 3）的**全部**静态数据：稳定 id、
//     稀有档、运行时钩子位、适用装备类型、三档数值、图标名、中英名称与描述。
//   - 提供按 id / 按档 / 按档位数值的只读查询，以及"已本地化"的名称与描述渲染。
//
// 硬约束：
//   1. **`Id_*` 字符串永久冻结**。它会写进装备的 `AFX_SLOT_n` KV 并随
//      `ItemTreeData.FromItem` 进存档；改名 = 玩家存档里的词缀集体失效。
//      新增词缀只能追加新 id，禁止改写或复用已发布的 id。
//   2. **行为参数不进 KV**。伤害、百分比、冷却全部按 (Id, Tier) 在本表查；
//      调平衡只改本文件，存档零迁移。
//   3. 全部可调数值集中在下方"常量区"，owner 一轮调参只看那一段。
//   4. 名称/描述文本是**唯一 source of truth**：`AffixForgeLocalization` 反过来
//      调本类的 GetDisplayName / GetDisplayDescription 把同一份文本注进本地化表，
//      避免"代码里一套、本地化表里另一套"。
//   5. 本文件零 Unity 依赖、零事件订阅、零 Harmony，可在任何时机安全访问。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace BossRush
{
    /// <summary>词缀稀有档。roll 时先抽档，再在档内按适用类型均匀抽 id。</summary>
    public enum AffixRarity
    {
        /// <summary>普通：纯增益，数值温和。</summary>
        Common = 0,
        /// <summary>稀有：强增益，出现率低。</summary>
        Rare = 1,
        /// <summary>诅咒：高收益 + 明确代价。</summary>
        Curse = 2
    }

    /// <summary>
    /// 词缀需要的运行时钩子（位标志）。运行时服务按 `_active` 里出现过的位
    /// **动态增删**静态事件订阅：一条都没激活时订阅数归零（AGENTS 4.12）。
    /// </summary>
    [Flags]
    public enum AffixHookMask
    {
        None = 0,
        /// <summary>Health.OnHurt，且受害者是敌人、来源是主角。</summary>
        OnPlayerHitEnemy = 1 << 0,
        /// <summary>Health.OnDead，且击杀者是主角。</summary>
        OnPlayerKill = 1 << 1,
        /// <summary>Health.OnHurt，且受害者是主角。</summary>
        OnPlayerHurt = 1 << 2,
        /// <summary>常驻 stat modifier，不订阅任何事件。</summary>
        Persistent = 1 << 3,
        /// <summary>需要逐帧 tick（死契的持续流失）。</summary>
        Tick = 1 << 4
    }

    /// <summary>
    /// 词缀适用的装备类型（位标志）。roll 时按承载装备的实际类型过滤，
    /// 避免"反弹伤害"落到枪上、"换弹加速"落到头盔上这类语义错位。
    /// </summary>
    [Flags]
    public enum AffixEquipMask
    {
        None = 0,
        Gun = 1 << 0,
        Melee = 1 << 1,
        Armor = 1 << 2,
        Helmet = 1 << 3,
        FaceMask = 1 << 4,

        AnyWeapon = Gun | Melee,
        AnyWear = Armor | Helmet | FaceMask,
        All = AnyWeapon | AnyWear
    }

    /// <summary>一条词缀的静态定义。实例全部来自 <see cref="AffixDefinitions"/> 的表，运行时只读。</summary>
    public sealed class AffixDefinition
    {
        /// <summary>KV 里存的稳定 id，**永久冻结**，禁止改名。</summary>
        public string Id;

        /// <summary>稀有档。</summary>
        public AffixRarity Rarity;

        /// <summary>需要的运行时钩子位。</summary>
        public AffixHookMask Hooks;

        /// <summary>可以落在哪些装备类型上。</summary>
        public AffixEquipMask AppliesTo;

        /// <summary>名称本地化键（= <see cref="AffixDefinitions.NameLocKeyPrefix"/> + Id）。</summary>
        public string NameLocKey;

        /// <summary>描述本地化键前缀（+ "_T1"/"_T2"/"_T3"）。</summary>
        public string DescLocKeyPrefix;

        /// <summary>图标文件名（含扩展名），目录见 <see cref="AffixDefinitions.IconRelativeDirectory"/>。</summary>
        public string IconFileName;

        /// <summary>主数值，长度 3（T1/T2/T3）。</summary>
        public float[] TierValues;

        /// <summary>副数值，长度 3；无副数值时为 null。</summary>
        public float[] TierValues2;

        /// <summary>触发冷却（秒）。&lt;= 0 表示不设 CD。</summary>
        public float ProcCooldownSeconds;

        /// <summary>true = 枪械归因时一次开火只触发一次（抗霰弹多弹丸重复 OnHurt）。</summary>
        public bool ConsumesShotGate;

        /// <summary>主数值是否按百分比展示。</summary>
        public bool Value1IsPercent = true;

        /// <summary>副数值是否按百分比展示。</summary>
        public bool Value2IsPercent = true;

        /// <summary>中文名。</summary>
        public string NameCN;

        /// <summary>英文名。</summary>
        public string NameEN;

        /// <summary>中文描述模板（{0} = 主数值，{1} = 副数值）。</summary>
        public string DescCN;

        /// <summary>英文描述模板（占位符同上）。</summary>
        public string DescEN;
    }

    /// <summary>词缀锻造的静态数据表。全部数值集中在常量区，便于一轮调参。</summary>
    public static class AffixDefinitions
    {
        // ====================================================================
        // 常量区 —— owner 调参只看这一段
        // ====================================================================

        #region 前缀与容量

        /// <summary>装备 KV 的统一前缀。与重铸的 `RF_` 天然互斥。</summary>
        public const string KvPrefix = "AFX_";

        /// <summary>词缀名称本地化键前缀。</summary>
        public const string NameLocKeyPrefix = "BossRush_AffixForge_Name_";

        /// <summary>词缀描述本地化键前缀。</summary>
        public const string DescLocKeyPrefix = "BossRush_AffixForge_Desc_";

        /// <summary>词缀图标目录（相对 Mod 根，走 ItemFactory.GetSpriteFromFile）。</summary>
        public const string IconRelativeDirectory = "Assets/ui/AffixForge";

        /// <summary>单件装备最多的词缀槽位数。</summary>
        public const int MaxSlots = 3;

        /// <summary>KV schema 版本。未来 schema 变更靠这个值分流，不加新前缀。</summary>
        public const int KvSchemaVersion = 1;

        #endregion

        #region 概率

        /// <summary>普通档权重（%）。三档之和必须为 100。</summary>
        public const float RarityWeightCommon = 62f;

        /// <summary>稀有档权重（%）。</summary>
        public const float RarityWeightRare = 30f;

        /// <summary>诅咒档权重（%）。</summary>
        public const float RarityWeightCurse = 8f;

        /// <summary>强度 T1 权重（%）。三档之和必须为 100。</summary>
        public const float TierWeightT1 = 60f;

        /// <summary>强度 T2 权重（%）。</summary>
        public const float TierWeightT2 = 30f;

        /// <summary>强度 T3 权重（%）。</summary>
        public const float TierWeightT3 = 10f;

        #endregion

        #region 槽位与经济

        /// <summary>Item.Quality &gt;= 该值 → 2 槽。</summary>
        public const int SlotQualityThreshold2 = 5;

        /// <summary>Item.Quality &gt;= 该值 → 3 槽。</summary>
        public const int SlotQualityThreshold3 = 7;

        /// <summary>一次重铸消耗的词缀熔石数量。</summary>
        public const int ForgeStoneCostPerRoll = 1;

        /// <summary>锁定一个槽消耗的词缀熔石数量（解锁免费）。</summary>
        public const int ForgeStoneCostPerLock = 2;

        /// <summary>
        /// Boss 击杀直掉词缀熔石的概率。游戏内 Wiki 写的是「Boss 掉落（概率不高）」。
        ///
        /// 取 0.08：一次重随机只花 1 颗（ForgeStoneCostPerRoll），比遗种蛋的 0.04 宽松一倍——
        /// 蛋是能开出随从的终局奖励，熔石只是消耗材料，卡材料比卡蛋难受得多。
        /// 标准竞技场一局十几只 Boss 期望掉 1 颗左右，配合哥布林商店（好感 2 级、库存 5）
        /// 构成「稳定买 + 运气捡」两条线，都不足以让玩家无限重随机。
        /// </summary>
        public const float ForgeStoneBossDropChance = 0.08f;

        /// <summary>单次 Boss 掉落的熔石数量。</summary>
        public const int ForgeStoneBossDropCount = 1;

        /// <summary>金钱费用下限，与重铸共用同一条底线。</summary>
        public const int MinMoneyCost = ReforgeSystem.MIN_REFORGE_COST;

        #endregion

        #region 词缀 id（**永久冻结**，进存档）

        public const string Id_Lifesteal = "lifesteal";
        public const string Id_Slaughter = "slaughter";
        public const string Id_Bulwark = "bulwark";
        public const string Id_SwiftHand = "swifthand";
        public const string Id_Thorns = "thorns";
        public const string Id_DeathBurst = "deathburst";
        public const string Id_Frenzy = "frenzy";
        public const string Id_HawkEye = "hawkeye";
        public const string Id_Overcharge = "overcharge";
        public const string Id_BloodRage = "bloodrage";
        public const string Id_GlassCannon = "glasscannon";
        public const string Id_DeathPact = "deathpact";

        #endregion

        #region 数值常量（按词缀分组，全部集中于此）

        // --- 普通档 ---
        /// <summary>汲血：命中回复的最大生命比例。</summary>
        private static readonly float[] Val_Lifesteal = { 0.006f, 0.010f, 0.015f };
        /// <summary>汲血：触发冷却（秒）。</summary>
        private const float CD_Lifesteal = 0.4f;

        /// <summary>屠戮：击杀回复的最大生命比例。</summary>
        private static readonly float[] Val_Slaughter = { 0.03f, 0.05f, 0.08f };

        /// <summary>磐石：受击后获得的护甲点数（每层）。</summary>
        private static readonly float[] Val_Bulwark = { 1f, 2f, 3f };
        /// <summary>磐石：触发冷却（秒）。</summary>
        private const float CD_Bulwark = 0.5f;

        /// <summary>迅手：击杀后的换弹速度增益（每层）。</summary>
        private static readonly float[] Val_SwiftHand = { 0.15f, 0.25f, 0.35f };

        /// <summary>荆棘：反弹所受实伤的比例。</summary>
        private static readonly float[] Val_Thorns = { 0.10f, 0.18f, 0.28f };
        /// <summary>荆棘：触发冷却（秒）。</summary>
        private const float CD_Thorns = 0.35f;

        // --- 稀有档 ---
        /// <summary>殉爆：爆炸伤害。</summary>
        private static readonly float[] Val_DeathBurstDamage = { 35f, 55f, 80f };
        /// <summary>殉爆：爆炸半径（米）。</summary>
        private static readonly float[] Val_DeathBurstRadius = { 2.5f, 3.0f, 3.5f };

        /// <summary>狂潮：击杀后的射速 / 机动性增益（每层）。</summary>
        private static readonly float[] Val_Frenzy = { 0.04f, 0.06f, 0.09f };

        /// <summary>鹰目：常驻暴击率加成。</summary>
        private static readonly float[] Val_HawkEye = { 0.04f, 0.07f, 0.11f };

        /// <summary>灌能：追加电系伤害占本次实伤的比例。</summary>
        private static readonly float[] Val_Overcharge = { 0.12f, 0.20f, 0.30f };
        /// <summary>灌能：触发冷却（秒）。</summary>
        private const float CD_Overcharge = 0.25f;

        // --- 诅咒档 ---
        /// <summary>狂血：武器伤害加成。</summary>
        private static readonly float[] Val_BloodRageDamage = { 0.10f, 0.16f, 0.24f };
        /// <summary>狂血：生命上限惩罚。</summary>
        private static readonly float[] Val_BloodRageHealth = { 0.12f, 0.18f, 0.25f };

        /// <summary>玻璃炮：武器伤害加成。</summary>
        private static readonly float[] Val_GlassCannonDamage = { 0.15f, 0.24f, 0.35f };
        /// <summary>玻璃炮：护甲惩罚（点数）。</summary>
        private static readonly float[] Val_GlassCannonArmor = { 1f, 2f, 3f };

        /// <summary>死契：击杀回复的最大生命比例。</summary>
        private static readonly float[] Val_DeathPactHeal = { 0.10f, 0.14f, 0.20f };
        /// <summary>死契：每秒流失的最大生命比例。</summary>
        private static readonly float[] Val_DeathPactDrain = { 0.008f, 0.012f, 0.018f };

        #endregion

        // ====================================================================
        // 词缀表
        // ====================================================================

        private static readonly AffixDefinition[] All =
        {
            // ---------------- 普通档（5 条） ----------------
            new AffixDefinition
            {
                Id = Id_Lifesteal,
                Rarity = AffixRarity.Common,
                Hooks = AffixHookMask.OnPlayerHitEnemy,
                AppliesTo = AffixEquipMask.AnyWeapon,
                IconFileName = "affix_" + Id_Lifesteal + ".png",
                TierValues = Val_Lifesteal,
                ProcCooldownSeconds = CD_Lifesteal,
                ConsumesShotGate = true,
                NameCN = "汲血", NameEN = "Lifesteal",
                DescCN = "命中敌人时回复最大生命的 {0}（冷却 0.4 秒）。",
                DescEN = "Restore {0} of max health when you hit an enemy (0.4s cooldown)."
            },
            new AffixDefinition
            {
                Id = Id_Slaughter,
                Rarity = AffixRarity.Common,
                Hooks = AffixHookMask.OnPlayerKill,
                AppliesTo = AffixEquipMask.All,
                IconFileName = "affix_" + Id_Slaughter + ".png",
                TierValues = Val_Slaughter,
                NameCN = "屠戮", NameEN = "Slaughter",
                DescCN = "击杀敌人时回复最大生命的 {0}。",
                DescEN = "Restore {0} of max health on kill."
            },
            new AffixDefinition
            {
                Id = Id_Bulwark,
                Rarity = AffixRarity.Common,
                Hooks = AffixHookMask.OnPlayerHurt,
                AppliesTo = AffixEquipMask.AnyWear,
                IconFileName = "affix_" + Id_Bulwark + ".png",
                TierValues = Val_Bulwark,
                ProcCooldownSeconds = CD_Bulwark,
                Value1IsPercent = false,
                NameCN = "磐石", NameEN = "Bulwark",
                DescCN = "受击后 4 秒内获得 {0} 点护甲，最多叠 3 层。",
                DescEN = "Gain {0} armor for 4s after being hit, up to 3 stacks."
            },
            new AffixDefinition
            {
                Id = Id_SwiftHand,
                Rarity = AffixRarity.Common,
                Hooks = AffixHookMask.OnPlayerKill,
                AppliesTo = AffixEquipMask.Gun,
                IconFileName = "affix_" + Id_SwiftHand + ".png",
                TierValues = Val_SwiftHand,
                NameCN = "迅手", NameEN = "Swift Hand",
                DescCN = "击杀后 5 秒内换弹速度提升 {0}，最多叠 2 层。",
                DescEN = "Reload speed +{0} for 5s after a kill, up to 2 stacks."
            },
            new AffixDefinition
            {
                Id = Id_Thorns,
                Rarity = AffixRarity.Common,
                Hooks = AffixHookMask.OnPlayerHurt,
                AppliesTo = AffixEquipMask.AnyWear,
                IconFileName = "affix_" + Id_Thorns + ".png",
                TierValues = Val_Thorns,
                ProcCooldownSeconds = CD_Thorns,
                NameCN = "荆棘", NameEN = "Thorns",
                DescCN = "受击时向攻击者反弹本次实际伤害的 {0}（冷却 0.35 秒）。",
                DescEN = "Reflect {0} of the damage taken back to the attacker (0.35s cooldown)."
            },

            // ---------------- 稀有档（4 条） ----------------
            new AffixDefinition
            {
                Id = Id_DeathBurst,
                Rarity = AffixRarity.Rare,
                Hooks = AffixHookMask.OnPlayerKill,
                AppliesTo = AffixEquipMask.All,
                IconFileName = "affix_" + Id_DeathBurst + ".png",
                TierValues = Val_DeathBurstDamage,
                TierValues2 = Val_DeathBurstRadius,
                Value1IsPercent = false,
                Value2IsPercent = false,
                NameCN = "殉爆", NameEN = "Death Burst",
                DescCN = "击杀敌人时引爆尸体，造成 {0} 点火焰伤害，半径 {1} 米，不伤及自身。",
                DescEN = "Kills detonate the corpse for {0} fire damage in a {1}m radius. Never harms you."
            },
            new AffixDefinition
            {
                Id = Id_Frenzy,
                Rarity = AffixRarity.Rare,
                Hooks = AffixHookMask.OnPlayerKill,
                AppliesTo = AffixEquipMask.All,
                IconFileName = "affix_" + Id_Frenzy + ".png",
                TierValues = Val_Frenzy,
                NameCN = "狂潮", NameEN = "Frenzy",
                DescCN = "击杀后 6 秒内射速与机动性各提升 {0}，最多叠 5 层。",
                DescEN = "Fire rate and mobility +{0} for 6s after a kill, up to 5 stacks."
            },
            new AffixDefinition
            {
                Id = Id_HawkEye,
                Rarity = AffixRarity.Rare,
                Hooks = AffixHookMask.Persistent,
                AppliesTo = AffixEquipMask.AnyWeapon,
                IconFileName = "affix_" + Id_HawkEye + ".png",
                TierValues = Val_HawkEye,
                NameCN = "鹰目", NameEN = "Hawk Eye",
                DescCN = "常驻：枪械与近战暴击率各 +{0}。",
                DescEN = "Passive: gun and melee crit chance +{0}."
            },
            new AffixDefinition
            {
                Id = Id_Overcharge,
                Rarity = AffixRarity.Rare,
                Hooks = AffixHookMask.OnPlayerHitEnemy,
                AppliesTo = AffixEquipMask.AnyWeapon,
                IconFileName = "affix_" + Id_Overcharge + ".png",
                TierValues = Val_Overcharge,
                ProcCooldownSeconds = CD_Overcharge,
                ConsumesShotGate = true,
                NameCN = "灌能", NameEN = "Overcharge",
                DescCN = "命中敌人时追加一次电击，伤害为本次实际伤害的 {0}（冷却 0.25 秒）。",
                DescEN = "Hits deal an extra electric strike for {0} of the damage dealt (0.25s cooldown)."
            },

            // ---------------- 诅咒档（3 条） ----------------
            new AffixDefinition
            {
                Id = Id_BloodRage,
                Rarity = AffixRarity.Curse,
                Hooks = AffixHookMask.Persistent,
                AppliesTo = AffixEquipMask.All,
                IconFileName = "affix_" + Id_BloodRage + ".png",
                TierValues = Val_BloodRageDamage,
                TierValues2 = Val_BloodRageHealth,
                NameCN = "狂血", NameEN = "Blood Rage",
                DescCN = "诅咒：全部武器伤害 +{0}，但生命上限 −{1}。",
                DescEN = "Curse: all weapon damage +{0}, but max health −{1}."
            },
            new AffixDefinition
            {
                Id = Id_GlassCannon,
                Rarity = AffixRarity.Curse,
                Hooks = AffixHookMask.Persistent,
                AppliesTo = AffixEquipMask.All,
                IconFileName = "affix_" + Id_GlassCannon + ".png",
                TierValues = Val_GlassCannonDamage,
                TierValues2 = Val_GlassCannonArmor,
                Value2IsPercent = false,
                NameCN = "玻璃炮", NameEN = "Glass Cannon",
                DescCN = "诅咒：全部武器伤害 +{0}，但护甲 −{1} 点。",
                DescEN = "Curse: all weapon damage +{0}, but armor −{1}."
            },
            new AffixDefinition
            {
                Id = Id_DeathPact,
                Rarity = AffixRarity.Curse,
                Hooks = AffixHookMask.OnPlayerKill | AffixHookMask.Tick,
                AppliesTo = AffixEquipMask.All,
                IconFileName = "affix_" + Id_DeathPact + ".png",
                TierValues = Val_DeathPactHeal,
                TierValues2 = Val_DeathPactDrain,
                NameCN = "死契", NameEN = "Death Pact",
                DescCN = "诅咒：击杀回复最大生命的 {0}，但每秒流失最大生命的 {1}。基地内不流失，且永远不会因此致死。",
                DescEN = "Curse: kills restore {0} of max health, but you lose {1} of max health per second. "
                    + "No drain in the base, and it can never kill you."
            }
        };

        /// <summary>id → 定义。懒建，<see cref="ResetStaticCaches"/> 清空。</summary>
        private static Dictionary<string, AffixDefinition> _byId;

        // ====================================================================
        // 查询
        // ====================================================================

        /// <summary>全部词缀定义（只读）。</summary>
        public static IReadOnlyList<AffixDefinition> GetAll()
        {
            EnsureKeysStamped();
            return All;
        }

        /// <summary>
        /// 按 id 查定义。**未知 id 返回 null（fail-open）**：读到未知词缀时调用方
        /// 应保留 KV 原样、不入 context，绝不清空——防止旧版/未来版本数据被抹掉。
        /// </summary>
        public static AffixDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                EnsureIndex();
                AffixDefinition def;
                return _byId.TryGetValue(id, out def) ? def : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>按稀有档取定义数组。无匹配返回长度 0 的数组（不返回 null）。</summary>
        public static AffixDefinition[] GetByRarity(AffixRarity rarity)
        {
            EnsureKeysStamped();
            int count = 0;
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Rarity == rarity) count++;
            }

            AffixDefinition[] result = new AffixDefinition[count];
            int w = 0;
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Rarity == rarity) result[w++] = All[i];
            }
            return result;
        }

        /// <summary>某条词缀是否可以落在给定装备类型上。mask 为 None 时一律不允许。</summary>
        public static bool IsApplicableTo(AffixDefinition def, AffixEquipMask mask)
        {
            if (def == null || mask == AffixEquipMask.None) return false;
            return (def.AppliesTo & mask) != AffixEquipMask.None;
        }

        /// <summary>
        /// 按物品稀有度算槽位数。Quality 语义见官方 Item.Quality（1..8）。
        /// 注意：真正生效的是首铸时冻结进 `AFX_CAP` 的值，本函数只用于"尚未锻造过"的推导。
        /// </summary>
        public static int GetSlotCountForQuality(int quality)
        {
            if (quality >= SlotQualityThreshold3) return 3;
            if (quality >= SlotQualityThreshold2) return 2;
            return 1;
        }

        /// <summary>取主数值。tier 1..3，越界 clamp；缺表返回 0。</summary>
        public static float GetTierValue(AffixDefinition def, int tier)
        {
            return ReadTier(def == null ? null : def.TierValues, tier);
        }

        /// <summary>取副数值。tier 1..3，越界 clamp；无副数值返回 0。</summary>
        public static float GetTierValue2(AffixDefinition def, int tier)
        {
            return ReadTier(def == null ? null : def.TierValues2, tier);
        }

        private static float ReadTier(float[] values, int tier)
        {
            if (values == null || values.Length == 0) return 0f;
            int index = tier - 1;
            if (index < 0) index = 0;
            if (index >= values.Length) index = values.Length - 1;
            return values[index];
        }

        // ====================================================================
        // 文案渲染（本地化的唯一 source of truth）
        // ====================================================================

        /// <summary>词缀显示名（已按当前语言渲染）。未知 id 返回"未知词缀"。</summary>
        public static string GetDisplayName(string id)
        {
            AffixDefinition def = Find(id);
            if (def == null)
            {
                return L10n.T("未知词缀", "Unknown Affix");
            }
            return L10n.T(def.NameCN, def.NameEN);
        }

        /// <summary>词缀显示描述（已按当前语言渲染并填入档位数值）。未知 id 返回兜底文案。</summary>
        public static string GetDisplayDescription(string id, int tier)
        {
            AffixDefinition def = Find(id);
            if (def == null)
            {
                return L10n.T("这条词缀来自其它版本，本版本无法解析它的效果。",
                    "This affix comes from another version and cannot be interpreted here.");
            }
            return RenderDescription(def, tier);
        }

        /// <summary>按定义与档位渲染描述。供本类与本地化注入共用，保证两边永远一致。</summary>
        public static string RenderDescription(AffixDefinition def, int tier)
        {
            if (def == null) return string.Empty;
            try
            {
                string template = L10n.T(def.DescCN, def.DescEN);
                string v1 = FormatValue(GetTierValue(def, tier), def.Value1IsPercent);
                string v2 = FormatValue(GetTierValue2(def, tier), def.Value2IsPercent);
                return string.Format(CultureInfo.InvariantCulture, template, v1, v2);
            }
            catch (Exception)
            {
                // 模板占位符与数值不匹配时不能拖崩 UI，退回未格式化文本
                return L10n.T(def.DescCN, def.DescEN);
            }
        }

        /// <summary>
        /// 1..3 的罗马数字（Ⅰ/Ⅱ/Ⅲ）。档位显示与槽位左列名共用这一份，
        /// 越界一律夹到 Ⅰ / Ⅲ。
        /// </summary>
        public static string GetRomanNumeral(int value)
        {
            if (value <= 1) return "Ⅰ";
            if (value == 2) return "Ⅱ";
            return "Ⅲ";
        }

        /// <summary>图标相对路径（供 ItemFactory.GetSpriteFromFile 使用）。</summary>
        public static string GetIconRelativePath(AffixDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.IconFileName)) return null;
            return IconRelativeDirectory + "/" + def.IconFileName;
        }

        private static string FormatValue(float value, bool isPercent)
        {
            if (isPercent)
            {
                return (value * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";
            }
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ====================================================================
        // 内部
        // ====================================================================

        private static void EnsureIndex()
        {
            if (_byId != null) return;
            EnsureKeysStamped();
            Dictionary<string, AffixDefinition> map =
                new Dictionary<string, AffixDefinition>(All.Length, StringComparer.Ordinal);
            for (int i = 0; i < All.Length; i++)
            {
                AffixDefinition def = All[i];
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;
                map[def.Id] = def;
            }
            _byId = map;
        }

        /// <summary>把 NameLocKey / DescLocKeyPrefix 按 id 补齐一次（表里不重复写前缀）。</summary>
        private static void EnsureKeysStamped()
        {
            for (int i = 0; i < All.Length; i++)
            {
                AffixDefinition def = All[i];
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;
                if (def.NameLocKey == null) def.NameLocKey = NameLocKeyPrefix + def.Id;
                if (def.DescLocKeyPrefix == null) def.DescLocKeyPrefix = DescLocKeyPrefix + def.Id;
            }
        }

        /// <summary>清空懒建索引。表本身是静态只读数据，不需要也不应该被清掉。</summary>
        public static void ResetStaticCaches()
        {
            _byId = null;
        }
    }
}
