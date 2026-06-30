// ============================================================================
// ViperDaggerWeaponConfig.cs - 毒蛇匕首装备工厂配置器
// ============================================================================
// 模块说明：
//   在 EquipmentFactory 加载 AssetBundle 后，自动为毒蛇匕首 Prefab 配置：
//   - ItemAgent_MeleeWeapon 组件
//   - ItemSetting_MeleeWeapon 组件（毒属性）
//   - 近战 Stats
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
    /// 毒蛇匕首装备工厂配置器
    /// </summary>
    public static class ViperDaggerWeaponConfig
    {
        private static readonly Dictionary<string, float> WEAPON_STATS = new Dictionary<string, float>
        {
            { "Damage", ViperDaggerConfig.Damage },
            { "MoveSpeedMultiplier", ViperDaggerConfig.MoveSpeedMultiplier },
            { "BlockBullet", ViperDaggerConfig.BlockBullet },
            { "CritRate", ViperDaggerConfig.CritRate },
            { "CritDamageFactor", ViperDaggerConfig.CritDamageFactor },
            { "ArmorPiercing", ViperDaggerConfig.ArmorPiercing },
            { "AttackSpeed", ViperDaggerConfig.AttackSpeed },
            { "AttackRange", ViperDaggerConfig.AttackRange },
            { "DealDamageTime", ViperDaggerConfig.DealDamageTime },
            { "StaminaCost", ViperDaggerConfig.StaminaCost },
            { "BleedChance", ViperDaggerConfig.BleedChance }
        };

        private static readonly HashSet<string> DISPLAY_STATS = new HashSet<string>
        {
            "Damage", "MoveSpeedMultiplier", "CritRate", "CritDamageFactor",
            "ArmorPiercing", "AttackSpeed", "AttackRange", "StaminaCost"
        };

        /// <summary>
        /// 尝试配置毒蛇匕首（由 EquipmentFactory 加载后调用）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;
            // 接受两种 baseName：
            //   - "ViperDagger"（占位符 / ConfigureNewWeaponsAfterLoad 直接传入）
            //   - "ViperDagger_Melee"（EquipmentFactory.LoadBundleInternal 从 prefab 名 ViperDagger_Melee_Item 提取）
            if (!baseName.Equals(NewWeaponIds.ViperDaggerBaseName, StringComparison.OrdinalIgnoreCase) &&
                !baseName.Equals(NewWeaponIds.ViperDaggerModelBaseName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 开始配置毒蛇匕首...");

                // 获取 3D 模型
                ItemAgent modelAgent = null;
                EquipmentFactory.TryGetLoadedModel(NewWeaponIds.ViperDaggerModelBaseName, out modelAgent);

                // 1. 配置 Stats
                ConfigureStats(item);

                // 2. 配置 MeleeAgent
                ConfigureMeleeAgent(item, modelAgent);

                // 3. 配置 MeleeSetting（毒属性）
                ConfigureMeleeSetting(item);

                // 4. 配置标签
                ConfigureTags(item);

                // 5. 绑定模型
                if (modelAgent != null)
                {
                    EquipmentFactory.TryBindLoadedMeleeModel(item, NewWeaponIds.ViperDaggerModelBaseName, NewWeaponIds.ViperDaggerBaseName);
                }

                // 6. 注入本地化
                InjectLocalization(item);

                ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 配置失败: " + e.Message);
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

            // 设置音效键
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

            // 为模型也配置
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

            // 毒属性
            meleeSetting.element = ElementTypes.poison;
            meleeSetting.dealExplosionDamage = false;

            // 设置原版 Poison buff（基础触发，运行时会额外叠层）
            try
            {
                Duckov.Buffs.Buff poisonBuff = Duckov.Utilities.GameplayDataSettings.Buffs.Poison;
                if (poisonBuff != null)
                {
                    meleeSetting.buff = poisonBuff;
                    meleeSetting.buffChance = 1f; // 100% 触发中毒
                }
            }
            catch (Exception e)
            {
                meleeSetting.buffChance = 0f;
                ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 设置毒素 buff 失败: " + e.Message);
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

        private static void InjectLocalization(Item item)
        {
            try
            {
                string displayName = L10n.T(ViperDaggerConfig.DisplayNameCN, ViperDaggerConfig.DisplayNameEN);
                string description = L10n.T(ViperDaggerConfig.DescriptionCN, ViperDaggerConfig.DescriptionEN);

                string itemKey = "Item_" + item.TypeID;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 本地化注入失败: " + e.Message);
            }
        }

        public static void ResetStaticCaches()
        {
            // 当前无需清理的静态缓存
        }
    }
}
