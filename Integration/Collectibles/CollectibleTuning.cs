// ============================================================================
// CollectibleTuning.cs - 收集品掉落、档次、奖励的玩法常量（AGENTS.md §4.8 第 2 层）
// ============================================================================
// owner 2026-11-19 拍板：
//   - 收集品单独一个池子，不进官方搜箱池；第一次开箱时判一次，中了就额外多一格。
//   - 普通箱 4%，高价值箱 12%；每趟出击最多 3 件。
//   - 价值越高越难出：8 档只在高价值箱出，每箱 0.006%（owner 给的皇冠爆率 0.03% 的五分之一）；
//     7 档高价值箱每箱 0.03%、普通箱 0.006%。7、8 档都不限量。
//   - 自研图守住旧承诺：天空岛单件不超过 10 万（收集品最高 6 档）；潮闸镇水下箱的 5–6 档大奖预算不被打乱（最高 4 档）。
// 出现倍率没有做成 ModConfig 旋钮：ModBehaviour partial 行数预算（§4.15）放不下，改由 F3 Dev 菜单临时调倍率。
// 2026-11-19 审核后（CR-2026-11-19-007，owner「全部修复」授权按好玩优先定）：
//   - 集齐奖金不低于整套单卖价：登记会收走物品，奖金比卖掉还少就成了陷阱。地图系列整套 1.9–2.6 万 → 奖金 3 万；
//     全图系列整套 9.7 / 12.2 万 → 奖金 15 万。
//   - 新件优先：抽中品质之后，一半的机会只在还没登记的件里挑。出现率、品质分布与 7、8 档每箱概率都不变，
//     凑齐全图系列所需件数的中位数从约 400 降到约 250，同时还会掉重复件给玩家卖或送。
//   - 主角枪械只有爆头才暴击（官方 ItemAgent_Gun 对主角把暴击率改成「瞄头 = 1，否则 = 0」），
//     枪械暴击率加成对玩家无效；竞技场系列改加爆头伤害。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>箱子档次：决定出现率与品质权重。</summary>
    internal enum CollectibleBoxTier
    {
        Normal = 0,
        High = 1,
    }

    internal static class CollectibleTuning
    {
        internal const string LogPrefix = "[Collectibles] ";

        // ---------------- 掉落 ----------------

        internal const float NormalBoxChance = 0.04f;
        internal const float HighBoxChance = 0.12f;
        internal const int PerRaidCap = 3;

        /// <summary>本地系列与全图系列同档都有候选时，本地系列的份额。</summary>
        internal const float LocalSeriesShare = 0.6f;

        /// <summary>新件优先：抽中品质后，这么大的机会只在还没登记的件里挑（这一档全登记过就照常挑）。</summary>
        internal const float NewItemPreference = 0.5f;

        /// <summary>品质 1..8 的权重（下标 0 不用），合计 100。</summary>
        internal static readonly float[] NormalQualityWeights = { 0f, 32.85f, 27f, 20f, 12f, 6f, 2f, 0.15f, 0f };
        internal static readonly float[] HighQualityWeights = { 0f, 20.5f, 21f, 22f, 20f, 12f, 4.2f, 0.25f, 0.05f };

        internal const int MaxQuality = 8;

        /// <summary>
        /// 物品格光晕（官方 DisplayQuality）跟着数值品质走：官方枚举 None、White…Red、Q7、Q8 与 1–8 档一一对应。
        /// 收集品克隆自便携安全区装置一系，不写就沿用克隆源的红光晕，1 档和 8 档看起来一样（CR-2026-11-19-001）。
        /// </summary>
        internal static int DisplayQualityOf(int quality)
        {
            if (quality < 1) return 1;
            return quality > MaxQuality ? MaxQuality : quality;
        }

        /// <summary>天空岛：岛上单件不超过 10 万（官方 6 档奢侈品最高约 5.6 万）。</summary>
        internal const int SkyIslandQualityCap = 6;
        /// <summary>潮闸镇水下箱：已有「八成出击恰好一件 5–6 档」的大奖预算，收集品不超过 4 档。</summary>
        internal const int TidegateUnderwaterQualityCap = 4;

        // ---------------- 官方箱子档次（官方本地化 UI_LootBox_*，2026-11-19 从游戏 Localization CSV 核对） ----------------

        private static readonly HashSet<string> HighValueBoxKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "UI_LootBox_Lux",           // 保险箱
            "UI_LootBox_WeaponEquip",   // 战术箱
            "UI_LootBox_WeaponEquip_2", // 武器箱
            "UI_LootBox_Tec",           // 战术物资箱
            "UI_LootBox_Console",       // 机箱
            "UI_LootBox_JLabBox",       // J-Lab 箱
            "UI_LootBox_Chest",         // 宝箱
        };

        /// <summary>不参与的官方箱子：宠物背包、快递箱、新手爱心箱、任务箱。</summary>
        private static readonly string[] ExcludedBoxKeyPrefixes =
        {
            "UI_LootBox_Safe",      // 宠物背包（官方键名如此）
            "UI_LootBox_Deliver",   // 快递箱（含 DeliverBox）
            "UI_LootBox_Starter",   // 爱心箱
            "UI_LootBox_Quest",     // 任务箱
            "UI_LootBox_GunFire",   // 任务箱
        };

        /// <summary>官方箱子名键 → 档次；排除的返回 false。未知键按普通箱。</summary>
        internal static bool TryResolveOfficialBoxTier(string displayNameKey, out CollectibleBoxTier tier)
        {
            tier = CollectibleBoxTier.Normal;
            if (string.IsNullOrEmpty(displayNameKey)) return true;
            for (int i = 0; i < ExcludedBoxKeyPrefixes.Length; i++)
            {
                if (displayNameKey.StartsWith(ExcludedBoxKeyPrefixes[i], StringComparison.Ordinal)) return false;
            }
            if (HighValueBoxKeys.Contains(displayNameKey)) tier = CollectibleBoxTier.High;
            return true;
        }

        /// <summary>官方出击图主场景 → 本地系列（37 号实验区的主场景是雪地军事基地）。</summary>
        internal static bool TryResolveOfficialSeries(string mainSceneId, out CollectibleSeries series)
        {
            series = CollectibleSeries.GroundZero;
            switch (mainSceneId)
            {
                case "Level_GroundZero_Main": series = CollectibleSeries.GroundZero; return true;
                case "Level_HiddenWarehouse_Main": series = CollectibleSeries.Warehouse; return true;
                case "Level_Farm_Main": series = CollectibleSeries.Farm; return true;
                case "Level_JLab_Main": series = CollectibleSeries.JLab; return true;
                case "Level_StormZone_Main": series = CollectibleSeries.StormZone; return true;
                case "Level_SnowMilitaryBase_Main": series = CollectibleSeries.TestArea37; return true;
                default: return false;
            }
        }

        // ---------------- 集齐奖励 ----------------

        /// <summary>集齐奖金不低于整套单卖价（地图系列整套 1.9–2.6 万，全图系列 9.7 / 12.2 万）。</summary>
        internal const int LocalSeriesCash = 30000;
        internal const int GlobalSeriesCash = 150000;
        internal const int AllSeriesCash = 500_000;

        internal static int SeriesCash(CollectibleSeries series)
        {
            CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(series);
            if (info == null || !info.CountsForSet) return 0;
            return info.Local ? LocalSeriesCash : GlobalSeriesCash;
        }

        /// <summary>系列永久小加成：属性键核对自官方 CharacterMainControl 的 stat hash。</summary>
        internal sealed class SeriesBonus
        {
            internal readonly string Stat;
            internal readonly float Amount;
            internal readonly bool Percent;
            internal readonly string TextCN, TextEN;
            internal SeriesBonus(string stat, float amount, bool percent, string textCN, string textEN)
            {
                Stat = stat; Amount = amount; Percent = percent; TextCN = textCN; TextEN = textEN;
            }
            internal string Text { get { return L10n.T(TextCN, TextEN); } }
        }

        // 风暴防护、寒冷防护是「过阈值就整段免疫」的属性，耐热官方没有用到：都不拿来做加成。
        // 枪械暴击率对主角无效（主角只有爆头算暴击），竞技场系列用爆头伤害 GunCritDamageGain：乘在暴击倍率上。
        private static readonly SeriesBonus[] Bonuses =
        {
            new SeriesBonus("Stamina", 5f, false, "体力上限 +5", "Max stamina +5"),
            new SeriesBonus("MaxWeight", 1f, false, "负重上限 +1 kg", "Carry weight +1 kg"),
            new SeriesBonus("FoodGain", 0.05f, false, "食物与饮水效果 +5%", "Food and drink effects +5%"),
            new SeriesBonus("HealGain", 0.05f, false, "治疗效果 +5%", "Healing received +5%"),
            new SeriesBonus("RunSpeed", 0.02f, true, "奔跑速度 +2%", "Run speed +2%"),
            new SeriesBonus("ReloadSpeedGain", 0.03f, false, "换弹速度 +3%", "Reload speed +3%"),
            new SeriesBonus("StaminaRecoverRate", 0.05f, true, "体力恢复 +5%", "Stamina recovery +5%"),
            new SeriesBonus("MaxWater", 5f, false, "水分上限 +5", "Max hydration +5"),
            new SeriesBonus("HearingAbility", 0.05f, false, "听觉 +5%", "Hearing +5%"),
            new SeriesBonus("GunCritDamageGain", 0.05f, false, "爆头伤害 +5%", "Headshot damage +5%"),
            new SeriesBonus("InventoryCapacity", 1f, false, "背包格数 +1", "Backpack slots +1"),
        };

        internal static SeriesBonus BonusOf(CollectibleSeries series)
        {
            int index = (int)series;
            return index >= 0 && index < Bonuses.Length ? Bonuses[index] : null;
        }

        // ---------------- 送礼 ----------------

        /// <summary>喜欢的收集品按品质给好感（钻石 +80、生日蛋糕 +150、皇冠 +500 作参照）。</summary>
        private static readonly int[] GiftAffinityByQuality = { 0, 60, 60, 80, 100, 120, 160, 250, 500 };

        internal static int GiftAffinity(int quality)
        {
            if (quality < 1) quality = 1;
            if (quality > MaxQuality) quality = MaxQuality;
            return GiftAffinityByQuality[quality];
        }

        /// <summary>
        /// 这位 NPC 是不是喜欢这件收集品：喜欢就按品质给好感（CollectibleCatalog 的 LikedBy），否则 0。
        /// NPCGiftSystem 在各 NPC 自己的 PositiveItems 之后查它，已有的礼物偏好优先。
        /// </summary>
        internal static int LikedGiftAffinity(string npcId, int typeId)
        {
            if (string.IsNullOrEmpty(npcId)) return 0;
            CollectibleDefinition def = CollectibleCatalog.Get(typeId);
            if (def == null) return 0;
            for (int i = 0; i < def.LikedBy.Length; i++)
            {
                if (string.Equals(def.LikedBy[i], npcId, StringComparison.Ordinal)) return GiftAffinity(def.Quality);
            }
            return 0;
        }

        // ---------------- 收藏册 ----------------

        internal const int BookShopMaxStock = 1;
    }
}
