using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛小兵、精英与巡守用官方 Boss 预设（<see cref="SkyIslandEnemySources"/>）时，把「掉落与经验」换回拾荒者口径。
    /// owner 2026-10-01 拍板：外形、AI、技能、随身行头都是 Boss 的，数值是 Boss 原版乘统一倍率；
    /// 但岛上同时有两百多个，每个都按 Boss 掉（800–1500 经验、专属战利品）会把经济与等级冲垮，所以小兵掉拾荒者那一份。
    /// 头目 / 岛主 / 具名对手 / 噬风不走这里，保留各自 Boss 的掉落与经验。
    ///
    /// 只改**本次生成的克隆 preset**，在官方 CreateCharacterAsync 之前：
    /// - 官方 Boss 的 `itemsToGenerate` 一张表里混着行头（武器、护甲、头盔、背包……）与杂项战利品。逐条判：
    ///   条目里每一件物品都插得进角色装备槽的算行头，留下；其余丢掉，再接上拾荒者那张表里不是行头的条目。
    /// - 经验、现金概率与区间照拾荒者；`isBoss` 关掉——官方据此置 `isBossCharacter`，战役「击杀 Boss」目标、
    ///   日报 Boss 击杀与 Rogue 结算都按它计数，两百多个小兵不该都算 Boss。
    /// 任一步失败保留官方原样（可失败的装饰，口径同 SkyIslandEnemyArmory），小兵照常参战。
    /// </summary>
    internal static class SkyIslandMinionKit
    {
        private static readonly FieldInfo ItemsField =
            typeof(CharacterRandomPreset).GetField("itemsToGenerate", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly string[] GearSlots =
            { "PrimaryWeapon", "SecondaryWeapon", "MeleeWeapon", "Armor", "Helmat", "FaceMask", "Backpack", "Headset" };
        private static readonly HashSet<string> GearTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Gun", "MeleeWeapon", "Armor", "Helmat", "FaceMask", "Mask", "Backpack", "Headset" };
        private static bool warned;

        internal static void UseScavLoot(CharacterRandomPreset clone, CharacterRandomPreset scav)
        {
            if (clone == null) return;
            clone.isBoss = false;
            if (scav == null) return;
            clone.exp = scav.exp;
            clone.hasCashChance = scav.hasCashChance;
            clone.cashRange = scav.cashRange;
            try
            {
                if (ItemsField == null) throw new MissingFieldException("CharacterRandomPreset.itemsToGenerate");
                var own = ItemsField.GetValue(clone) as List<RandomItemGenerateDescription>;
                var scavItems = ItemsField.GetValue(scav) as List<RandomItemGenerateDescription>;
                if (own == null || scavItems == null) return;
                List<Slot> slots = TemplateSlots();
                var merged = new List<RandomItemGenerateDescription>(own.Count + scavItems.Count);
                for (int i = 0; i < own.Count; i++) if (IsGear(own[i], slots)) merged.Add(own[i]);
                for (int i = 0; i < scavItems.Count; i++) if (!IsGear(scavItems[i], slots)) merged.Add(scavItems[i]);
                // 克隆出来的 preset 自带一份新的列表；这里再换成新表，绝不写回官方资源。
                ItemsField.SetValue(clone, merged);
            }
            catch (Exception e)
            {
                if (!warned) { warned = true; Debug.LogWarning("[SkyIslandEnemy] 小兵掉落换拾荒者口径失败（保留 Boss 原掉落）：" + e.Message); }
            }
        }

        /// <summary>这一条生成的是不是行头：按物品池时每一件都插得进角色装备槽；按标签时每个标签都是行头标签。</summary>
        private static bool IsGear(RandomItemGenerateDescription entry, List<Slot> slots)
        {
            if (entry.randomFromPool)
            {
                if (entry.itemPool == null || entry.itemPool.entries == null || entry.itemPool.entries.Count == 0) return false;
                for (int i = 0; i < entry.itemPool.entries.Count; i++)
                    if (!Pluggable(entry.itemPool.entries[i].value.itemTypeID, slots)) return false;
                return true;
            }
            if (entry.tags == null || entry.tags.entries == null || entry.tags.entries.Count == 0) return false;
            for (int i = 0; i < entry.tags.entries.Count; i++)
            {
                Tag tag = entry.tags.entries[i].value;
                if (tag == null || !GearTags.Contains(tag.name)) return false;
            }
            return true;
        }

        private static bool Pluggable(int typeId, List<Slot> slots)
        {
            Item prefab = typeId > 0 ? ItemAssetsCollection.GetPrefab(typeId) : null;
            if (prefab == null) return false;
            for (int i = 0; i < slots.Count; i++) if (slots[i].CanPlug(prefab)) return true;
            return false;
        }

        private static List<Slot> TemplateSlots()
        {
            var result = new List<Slot>();
            Item template = ItemAssetsCollection.GetPrefab(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
            for (int i = 0; i < GearSlots.Length; i++)
            {
                Slot slot = SkyIslandBossForge.FindSlot(template, GearSlots[i]);
                if (slot != null) result.Add(slot);
            }
            if (result.Count == 0) throw new InvalidOperationException("角色装备槽模板缺失");
            return result;
        }

        internal static void ResetStaticCaches() { warned = false; }
    }
}
