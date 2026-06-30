// ============================================================================
// FrostSpearWeaponConfig.cs - 冰霜长矛装备工厂配置器
// ============================================================================
// 模块说明：
//   在 EquipmentFactory 加载 AssetBundle 后，自动为冰霜长矛 Prefab 配置：
//   - ItemAgent_MeleeWeapon 组件
//   - ItemSetting_MeleeWeapon 组件（冰属性，100%冰冻）
//   - 近战 Stats
//   - ColdProtection +1 modifier
//   - 物品标签
//   - 本地化注入
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>
    /// 冰霜长矛装备工厂配置器
    /// </summary>
    public static class FrostSpearWeaponConfig
    {
        private static readonly Dictionary<string, float> WEAPON_STATS = new Dictionary<string, float>
        {
            { "Damage", FrostSpearConfig.Damage },
            { "MoveSpeedMultiplier", FrostSpearConfig.MoveSpeedMultiplier },
            { "BlockBullet", FrostSpearConfig.BlockBullet },
            { "CritRate", FrostSpearConfig.CritRate },
            { "CritDamageFactor", FrostSpearConfig.CritDamageFactor },
            { "ArmorPiercing", FrostSpearConfig.ArmorPiercing },
            { "AttackSpeed", FrostSpearConfig.AttackSpeed },
            { "AttackRange", FrostSpearConfig.AttackRange },
            { "DealDamageTime", FrostSpearConfig.DealDamageTime },
            { "StaminaCost", FrostSpearConfig.StaminaCost },
            { "BleedChance", FrostSpearConfig.BleedChance }
        };

        private static readonly HashSet<string> DISPLAY_STATS = new HashSet<string>
        {
            "Damage", "MoveSpeedMultiplier", "CritRate", "CritDamageFactor",
            "ArmorPiercing", "AttackSpeed", "AttackRange", "StaminaCost"
        };

        /// <summary>
        /// 尝试配置冰霜长矛
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;
            if (!baseName.Equals(NewWeaponIds.FrostSpearBaseName, StringComparison.OrdinalIgnoreCase) &&
                !baseName.Equals(NewWeaponIds.FrostSpearModelBaseName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 开始配置冰霜长矛...");

                ItemAgent modelAgent = null;
                EquipmentFactory.TryGetLoadedModel(NewWeaponIds.FrostSpearModelBaseName, out modelAgent);

                ConfigureStats(item);
                ConfigureMeleeAgent(item, modelAgent);
                ConfigureMeleeSetting(item);
                ConfigureTags(item);
                ConfigureModifiers(item);

                if (modelAgent != null)
                {
                    EquipmentFactory.TryBindLoadedMeleeModel(item, NewWeaponIds.FrostSpearModelBaseName, NewWeaponIds.FrostSpearBaseName);
                }

                InjectLocalization(item);

                ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        private static void ConfigureStats(Item item)
        {
            StatCollection stats = item.Stats;
            if (stats == null)
            {
                item.CreateStatsComponent();
                stats = item.Stats;
            }
            if (stats == null) return;

            foreach (KeyValuePair<string, float> kvp in WEAPON_STATS)
            {
                bool shouldDisplay = DISPLAY_STATS.Contains(kvp.Key);
                Stat existingStat = stats.GetStat(kvp.Key);
                if (existingStat != null)
                {
                    existingStat.BaseValue = kvp.Value;
                }
                else
                {
                    stats.Add(new Stat(kvp.Key, kvp.Value, shouldDisplay));
                }
            }
        }

        private static void ConfigureMeleeAgent(Item item, ItemAgent modelAgent)
        {
            ItemAgent_MeleeWeapon meleeAgent = item.GetComponent<ItemAgent_MeleeWeapon>();
            if (meleeAgent == null)
            {
                meleeAgent = item.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
            }

            meleeAgent.handheldSocket = HandheldSocketTypes.normalHandheld;
            meleeAgent.handAnimationType = HandheldAnimationType.meleeWeapon;

            try
            {
                FieldInfo soundKeyField = typeof(ItemAgent_MeleeWeapon).GetField("soundKey",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (soundKeyField != null)
                {
                    soundKeyField.SetValue(meleeAgent, "Default");
                }
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            if (modelAgent != null)
            {
                ItemAgent_MeleeWeapon modelMeleeAgent = modelAgent.gameObject.GetComponent<ItemAgent_MeleeWeapon>();
                if (modelMeleeAgent == null)
                {
                    modelMeleeAgent = modelAgent.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
                }
                modelMeleeAgent.handheldSocket = HandheldSocketTypes.normalHandheld;
                modelMeleeAgent.handAnimationType = HandheldAnimationType.meleeWeapon;
            }
        }

        private static void ConfigureMeleeSetting(Item item)
        {
            ItemSetting_MeleeWeapon meleeSetting = item.GetComponent<ItemSetting_MeleeWeapon>();
            if (meleeSetting == null)
            {
                meleeSetting = item.gameObject.AddComponent<ItemSetting_MeleeWeapon>();
            }

            // 冰属性
            meleeSetting.element = ElementTypes.ice;
            meleeSetting.dealExplosionDamage = false;

            // 设置冰冻 buff（100% 触发）
            try
            {
                Duckov.Buffs.Buff coldBuff = Duckov.Utilities.GameplayDataSettings.Buffs.Cold;
                if (coldBuff != null)
                {
                    meleeSetting.buff = coldBuff;
                    meleeSetting.buffChance = FrostSpearConfig.FreezeChance;
                }
                else
                {
                    meleeSetting.buffChance = 0f;
                }
            }
            catch (Exception e)
            {
                meleeSetting.buffChance = 0f;
                ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 设置冰冻 buff 失败: " + e.Message);
            }
        }

        private static void ConfigureTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Weapon");
            EquipmentHelper.AddTagToItem(item, "MeleeWeapon");
            EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");
            EquipmentHelper.AddTagToItem(item, "Special");

            try
            {
                item.SetBool("IsMeleeWeapon", true, true);
            }
            catch  { /* best-effort fallback intentionally ignored */ }
        }

        private static void ConfigureModifiers(Item item)
        {
            try
            {
                EquipmentHelper.AddModifierToItem(item, "ColdProtection", ModifierType.Add, FrostSpearConfig.ColdProtectionBonus, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 添加 modifier 失败: " + e.Message);
            }
        }

        private static void InjectLocalization(Item item)
        {
            try
            {
                string displayName = L10n.T(FrostSpearConfig.DisplayNameCN, FrostSpearConfig.DisplayNameEN);
                string description = L10n.T(FrostSpearConfig.DescriptionCN, FrostSpearConfig.DescriptionEN);

                string itemKey = "Item_" + item.TypeID;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 本地化注入失败: " + e.Message);
            }
        }

        public static void ResetStaticCaches()
        {
            // 当前无需清理的静态缓存
        }
    }
}
