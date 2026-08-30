// ============================================================================
// AffixForgeLocalization.cs - 词缀锻造本地化的唯一注入口
// ============================================================================
// 形态照 Localization/PetNestLocalization.cs：独立文件而不是继续膨胀
// LocalizationInjector.cs。接线点是
// Integration/BossRushIntegration_StartAndScene.cs 的 InjectLocalization_Extra_Integration()。
//
// 冻结要求（AGENTS.md 4.4）：凡设置 DisplayNameRaw / _overrideInteractNameKey /
// 写进 KV 的本地化键，都必须在这里注入；缺失会在游戏内显示 *raw key*。
//
// 两个容易漏的点（漏了就会在物品详情面板露星号）：
//   1. `Var_AFX_NAME_1` / `_2` / `_3` —— 官方 CustomData.DisplayName 是
//      `("Var_" + key).ToPlainText()`，三个下标各是一条独立键，必须注三条。
//   2. `BossRush_AffixForge_Name_<id>` ×12 —— 装备 KV 里存的是这个**键**，
//      官方 CustomData.GetValueDisplayString 对 String 会再 ToPlainText() 一次，
//      所以值本身要能被翻译。缺一个就在词缀行上露 *BossRush_AffixForge_Name_xxx*。
//
// 名称与描述文本的 source of truth 在 AffixDefinitions（表里的 NameCN/NameEN/
// DescCN/DescEN）。本文件只是把同一份渲染结果搬进本地化表，
// 保证"代码里显示的"和"KV 翻出来的"永远是同一句话。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>词缀锻造全部本地化键的注入入口。</summary>
    public static class AffixForgeLocalization
    {
        /// <summary>UI 文案统一前缀。</summary>
        public const string Prefix = "BossRush_AffixForge_";

        /// <summary>哥布林"词缀锻造"子交互的名称键（_overrideInteractNameKey 用它）。</summary>
        public const string InteractNameKey = "BossRush_AffixForge";

        /// <summary>把全部词缀锻造键注入官方本地化表。</summary>
        public static void Inject()
        {
            try
            {
                Dictionary<string, string> map = new Dictionary<string, string>();
                AddAffixNamesAndDescs(map);
                AddUiStrings(map);
                AddKvDisplayNames(map);
                AddBuffStrings(map);
                LocalizationHelper.InjectLocalizations(map);

                // 词缀熔石物品自身的键由物品配置注入
                AffixForgeStoneConfig.InjectLocalization();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeLocalization] 注入词缀锻造本地化失败: " + e.Message);
            }
        }

        private static void Add(Dictionary<string, string> map, string suffix, string cn, string en)
        {
            map[Prefix + suffix] = L10n.T(cn, en);
        }

        #region 12 条词缀的名称与描述

        /// <summary>
        /// 注入 12 条名称键与 36 条描述键（每条词缀 3 档）。
        /// 文本全部由 AffixDefinitions 渲染，两处永不漂移。
        /// </summary>
        private static void AddAffixNamesAndDescs(Dictionary<string, string> map)
        {
            IReadOnlyList<AffixDefinition> all = AffixDefinitions.GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                AffixDefinition def = all[i];
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;

                map[AffixDefinitions.NameLocKeyPrefix + def.Id] = L10n.T(def.NameCN, def.NameEN);

                for (int tier = 1; tier <= 3; tier++)
                {
                    string key = AffixDefinitions.DescLocKeyPrefix + def.Id + "_T" + tier;
                    map[key] = AffixDefinitions.RenderDescription(def, tier);
                }
            }
        }

        #endregion

        #region KV 左列名

        /// <summary>
        /// 物品详情面板里词缀行的**左列**名。官方按 `"Var_" + key` 查表，
        /// 三个槽位各是一条独立键，缺任何一条就露 *Var_AFX_NAME_n*。
        /// </summary>
        private static void AddKvDisplayNames(Dictionary<string, string> map)
        {
            for (int slot = 1; slot <= AffixDefinitions.MaxSlots; slot++)
            {
                string key = "Var_" + AffixItemData.PREFIX_NAME + slot;
                string roman = AffixDefinitions.GetRomanNumeral(slot);
                map[key] = L10n.T("词缀·" + roman, "Affix " + roman);
            }
        }

        #endregion

        #region UI 文案

        private static void AddUiStrings(Dictionary<string, string> map)
        {
            // 哥布林子交互名（不带 Prefix 的独立键）
            map[InteractNameKey] = L10n.T("词缀锻造", "Affix Forging");

            Add(map, "Title", "词缀锻造", "Affix Forging");
            Add(map, "Subtitle",
                "把 Boss 的脾气刻进你的家伙。锁住想留的，重掷剩下的。",
                "Etch a boss's temper into your gear. Lock what you like, reroll the rest.");

            Add(map, "Forge", "锻造", "Forge");
            Add(map, "Cost", "费用", "Cost");
            Add(map, "StoneCount", "词缀熔石", "Affix Forge Stones");
            Add(map, "Lock", "锁定", "Lock");
            Add(map, "Unlock", "解锁", "Unlock");
            Add(map, "Locked", "已锁定", "Locked");
            Add(map, "EmptySlot", "空词缀槽", "Empty affix slot");
            Add(map, "SelectItem", "选择一件武器或护甲", "Pick a weapon or a piece of armor");
            Add(map, "NotForgeable", "该装备无法附加词缀。", "This gear cannot carry affixes.");
            Add(map, "NoStone", "词缀熔石不足。", "Not enough Affix Forge Stones.");
            Add(map, "NoMoney", "金钱不足。", "Not enough money.");
            Add(map, "AllLocked", "全部词缀槽都已锁定，没有可重铸的槽。",
                "Every affix slot is locked; there is nothing to reroll.");
            Add(map, "KeepOneUnlocked", "至少要留一个未锁定的词缀槽。",
                "At least one affix slot must stay unlocked.");
            Add(map, "Unknown", "未知词缀", "Unknown Affix");
            Add(map, "UnknownDesc", "这条词缀来自其它版本，本版本无法解析它的效果。",
                "This affix comes from another version and cannot be interpreted here.");

            Add(map, "SlotCountFormat", "词缀槽 {0}", "Affix slots: {0}");
            Add(map, "LockCostFormat", "锁定需要 {0} 块词缀熔石", "Locking costs {0} Affix Forge Stones");

            Add(map, "Rarity_Common", "普通", "Common");
            Add(map, "Rarity_Rare", "稀有", "Rare");
            Add(map, "Rarity_Curse", "诅咒", "Curse");

            Add(map, "Hint_Curse",
                "诅咒词缀强度更高，但一定带代价。要不要留，你自己掂量。",
                "Cursed affixes hit harder but always cost you something. Your call whether to keep one.");
        }

        #endregion

        #region 运行时 Buff 文案

        /// <summary>
        /// 三个运行时 Buff（磐石 / 迅手 / 狂潮）的名称与描述。
        /// 键名与 AffixBuffFactory 构造 Buff 时写进 displayName / description 的键一致。
        /// </summary>
        private static void AddBuffStrings(Dictionary<string, string> map)
        {
            map["Buff_BossRush_Affix_Bulwark_Name"] = L10n.T("磐石", "Bulwark");
            map["Buff_BossRush_Affix_Bulwark_Desc"] =
                L10n.T("受击后短暂获得额外护甲，可叠加。", "Extra armor for a short time after being hit. Stacks.");

            map["Buff_BossRush_Affix_SwiftHand_Name"] = L10n.T("迅手", "Swift Hand");
            map["Buff_BossRush_Affix_SwiftHand_Desc"] =
                L10n.T("击杀后短暂加快换弹，可叠加。", "Faster reloads for a short time after a kill. Stacks.");

            map["Buff_BossRush_Affix_Frenzy_Name"] = L10n.T("狂潮", "Frenzy");
            map["Buff_BossRush_Affix_Frenzy_Desc"] =
                L10n.T("击杀后短暂提升射速与机动性，可叠加。",
                    "Faster fire rate and movement for a short time after a kill. Stacks.");
        }

        #endregion
    }
}
