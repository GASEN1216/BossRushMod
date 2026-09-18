// ============================================================================
// NewWeaponConfiguratorCore.cs - 五把新武器的共享配置流程
// ============================================================================
// 为什么需要本文件：
//   五个 XxxWeaponConfig.TryConfigure 此前各自完整写了一遍同一套流程：
//     近战三把（毒蛇匕首 / 冰霜长矛 / 召唤法杖）
//       —— ConfigureStats / ConfigureMeleeAgent / ConfigureTags / InjectLocalization
//          四个方法逐字相同，连 soundKey 的反射写法和 DISPLAY_STATS 的集合都一模一样；
//     图腾两件（能量盾 / 雷电戒指）
//       —— ConfigureTags / TryBindLoadedModel / InjectLocalization 同样逐字相同。
//   合计约 250 行纯复制。后果不只是难看：改一处口径（例如给近战补一条标签）要记得改三遍，
//   漏掉的那一把不会报错、也不会被守卫发现，只会在游戏里表现成「这把刀少点东西」。
//
// 做法：
//   把「流程」放本文件，把「差异」放各武器的 Spec（静态只读实例）。加第六把武器时
//   只需要写一个 Spec + 一行 TryConfigure，不再复制流程。
//
// 幂等：
//   本文件所有写入都可重复执行（Stat 存在就改 BaseValue、组件存在就复用、
//   Modifier 走 EquipmentHelper.EnsureModifierOnItem）。ItemFactory 配置器路径、
//   占位符路径与延迟 bootstrap 路径会重复调用，结果必须一致。
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
    /// <summary>近战武器要挂的官方 buff。用枚举而不是委托：避免每次配置都分配一个闭包。</summary>
    internal enum NewWeaponMeleeBuffKind
    {
        None = 0,
        Poison = 1,
        Cold = 2
    }

    /// <summary>一把新近战武器的差异项。实例是各 XxxWeaponConfig 里的静态只读字段。</summary>
    internal sealed class NewWeaponMeleeSpec
    {
        public int TypeId;
        /// <summary>占位符 / ConfigureNewWeaponsAfterLoad 直接传入的名字。</summary>
        public string BaseName;
        /// <summary>EquipmentFactory 从 prefab 名（Xxx_Melee_Item）提取出来的名字。</summary>
        public string ModelBaseName;
        public string LogPrefix;
        public string DisplayLabelCN;

        /// <summary>近战面板。键是官方 Stat key，值是基础值。</summary>
        public Dictionary<string, float> Stats;

        public ElementTypes Element;
        public NewWeaponMeleeBuffKind BuffKind;
        public float BuffChance;

        /// <summary>可选的常驻 modifier（冰霜长矛的 ColdProtection）。Key 为空表示不挂。</summary>
        public string ModifierKey;
        public float ModifierValue;

        public string DisplayNameCN;
        public string DisplayNameEN;
        public string DescriptionCN;
        public string DescriptionEN;
    }

    /// <summary>一件新图腾装备的差异项。</summary>
    internal sealed class NewWeaponTotemSpec
    {
        public int TypeId;
        public string BaseName;
        public string ModelBaseName;
        public string LogPrefix;
        public string DisplayLabelCN;

        /// <summary>可选的常驻 modifier（能量盾的 BodyArmor）。Key 为空表示不挂。</summary>
        public string ModifierKey;
        public float ModifierValue;

        public string DisplayNameCN;
        public string DisplayNameEN;
        public string DescriptionCN;
        public string DescriptionEN;
    }

    /// <summary>五把新武器的共享配置流程。</summary>
    internal static class NewWeaponConfiguratorCore
    {
        /// <summary>
        /// 会出现在物品详情面板里的 Stat。三把近战共用同一份——它们的面板项本来就一样，
        /// 差的只是数值。
        /// </summary>
        private static readonly HashSet<string> DisplayStats = new HashSet<string>
        {
            "Damage", "MoveSpeedMultiplier", "CritRate", "CritDamageFactor",
            "ArmorPiercing", "AttackSpeed", "AttackRange", "StaminaCost"
        };

        // ====================================================================
        // 入口
        // ====================================================================

        /// <summary>配置一把新近战武器。baseName 不匹配时返回 false 且不做任何写入。</summary>
        internal static bool ConfigureMelee(Item item, string baseName, NewWeaponMeleeSpec spec)
        {
            if (item == null || spec == null || string.IsNullOrEmpty(baseName)) return false;
            if (!MatchesBaseName(baseName, spec.BaseName, spec.ModelBaseName)) return false;

            try
            {
                ModBehaviour.DevLog(spec.LogPrefix + " 开始配置" + spec.DisplayLabelCN + "...");

                ItemAgent modelAgent = null;
                EquipmentFactory.TryGetLoadedModel(spec.ModelBaseName, out modelAgent);

                ApplyStats(item, spec.Stats);
                ApplyMeleeAgent(item, modelAgent);
                ApplyMeleeSetting(item, spec);
                ApplyMeleeTags(item);
                ApplyModifier(item, spec.ModifierKey, spec.ModifierValue, spec.LogPrefix);
                // 品质 / 售价 / 耐久 / 可维修标签（与占位符路径共用同一张表）
                NewWeaponItemAttributes.Apply(item, spec.TypeId);

                if (modelAgent != null)
                {
                    EquipmentFactory.TryBindLoadedMeleeModel(item, spec.ModelBaseName, spec.BaseName);
                }

                InjectItemLocalization(item, spec.DisplayNameCN, spec.DisplayNameEN,
                    spec.DescriptionCN, spec.DescriptionEN, spec.LogPrefix);

                ModBehaviour.DevLog(spec.LogPrefix + " 配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(spec.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        /// <summary>配置一件新图腾装备。baseName 不匹配时返回 false 且不做任何写入。</summary>
        internal static bool ConfigureTotem(Item item, string baseName, NewWeaponTotemSpec spec)
        {
            if (item == null || spec == null || string.IsNullOrEmpty(baseName)) return false;
            if (!MatchesBaseName(baseName, spec.BaseName, spec.ModelBaseName)) return false;

            try
            {
                ModBehaviour.DevLog(spec.LogPrefix + " 开始配置" + spec.DisplayLabelCN + "...");

                ApplyTotemTags(item);
                // 品质 / 售价（图腾不写耐久与可维修标签，理由见 NewWeaponItemAttributes 文件头）
                NewWeaponItemAttributes.Apply(item, spec.TypeId);

                TryBindTotemModel(item, spec);
                ApplyModifier(item, spec.ModifierKey, spec.ModifierValue, spec.LogPrefix);

                InjectItemLocalization(item, spec.DisplayNameCN, spec.DisplayNameEN,
                    spec.DescriptionCN, spec.DescriptionEN, spec.LogPrefix);

                ModBehaviour.DevLog(spec.LogPrefix + " 配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(spec.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        // ====================================================================
        // 流程各段
        // ====================================================================

        /// <summary>
        /// 两种 baseName 都认：
        ///   - "ViperDagger"（占位符 / ConfigureNewWeaponsAfterLoad 直接传入）
        ///   - "ViperDagger_Melee"（EquipmentFactory.LoadBundleInternal 从 prefab 名提取）
        /// </summary>
        private static bool MatchesBaseName(string baseName, string primary, string model)
        {
            if (!string.IsNullOrEmpty(primary) &&
                baseName.Equals(primary, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return !string.IsNullOrEmpty(model) &&
                   baseName.Equals(model, StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyStats(Item item, Dictionary<string, float> stats)
        {
            if (stats == null) return;

            StatCollection collection = item.Stats;
            if (collection == null)
            {
                item.CreateStatsComponent();
                collection = item.Stats;
            }
            if (collection == null) return;

            foreach (KeyValuePair<string, float> kvp in stats)
            {
                Stat existingStat = collection.GetStat(kvp.Key);
                if (existingStat != null)
                {
                    existingStat.BaseValue = kvp.Value;
                }
                else
                {
                    collection.Add(new Stat(kvp.Key, kvp.Value, DisplayStats.Contains(kvp.Key)));
                }
            }
        }

        private static void ApplyMeleeAgent(Item item, ItemAgent modelAgent)
        {
            ItemAgent_MeleeWeapon meleeAgent = item.GetComponent<ItemAgent_MeleeWeapon>();
            if (meleeAgent == null)
            {
                meleeAgent = item.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
            }

            ConfigureMeleeAgentFields(meleeAgent);

            // 为模型也配置一份：手上拿的是模型 prefab，不配的话挥砍动画与特效挂不上
            if (modelAgent != null)
            {
                ItemAgent_MeleeWeapon modelMeleeAgent = modelAgent.gameObject.GetComponent<ItemAgent_MeleeWeapon>();
                if (modelMeleeAgent == null)
                {
                    modelMeleeAgent = modelAgent.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
                }
                ConfigureMeleeAgentFields(modelMeleeAgent);
                NewWeaponMeleeFx.EnsureMeleeAttackFx(modelMeleeAgent);
            }

            // slashFx / hitFx 回退走共享的 MeleeWeaponFxPolicy（三把新近战共用一处实现）
            NewWeaponMeleeFx.EnsureMeleeAttackFx(meleeAgent);
        }

        private static void ConfigureMeleeAgentFields(ItemAgent_MeleeWeapon agent)
        {
            agent.handheldSocket = HandheldSocketTypes.normalHandheld;
            agent.handAnimationType = HandheldAnimationType.meleeWeapon;

            // soundKey 是官方私有字段，只能反射写；写不进去就用 prefab 自带值，不影响玩法
            try
            {
                FieldInfo soundKeyField = typeof(ItemAgent_MeleeWeapon).GetField("soundKey",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (soundKeyField != null)
                {
                    soundKeyField.SetValue(agent, "Default");
                }
            }
            catch  { /* best-effort fallback intentionally ignored */ }
        }

        private static void ApplyMeleeSetting(Item item, NewWeaponMeleeSpec spec)
        {
            ItemSetting_MeleeWeapon meleeSetting = item.GetComponent<ItemSetting_MeleeWeapon>();
            if (meleeSetting == null)
            {
                meleeSetting = item.gameObject.AddComponent<ItemSetting_MeleeWeapon>();
            }

            meleeSetting.element = spec.Element;
            meleeSetting.dealExplosionDamage = false;

            if (spec.BuffKind == NewWeaponMeleeBuffKind.None)
            {
                meleeSetting.buffChance = 0f;
                return;
            }

            try
            {
                Duckov.Buffs.Buff buff = ResolveBuff(spec.BuffKind);
                if (buff != null)
                {
                    meleeSetting.buff = buff;
                    meleeSetting.buffChance = spec.BuffChance;
                }
                else
                {
                    meleeSetting.buffChance = 0f;
                }
            }
            catch (Exception e)
            {
                meleeSetting.buffChance = 0f;
                ModBehaviour.DevLog(spec.LogPrefix + " 设置官方 buff 失败: " + e.Message);
            }
        }

        private static Duckov.Buffs.Buff ResolveBuff(NewWeaponMeleeBuffKind kind)
        {
            Duckov.Utilities.GameplayDataSettings.BuffsData buffs =
                Duckov.Utilities.GameplayDataSettings.Buffs;
            if (buffs == null) return null;

            if (kind == NewWeaponMeleeBuffKind.Poison) return buffs.Poison;
            if (kind == NewWeaponMeleeBuffKind.Cold) return buffs.Cold;
            return null;
        }

        private static void ApplyMeleeTags(Item item)
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

        private static void ApplyTotemTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Totem");
            EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");
            EquipmentHelper.AddTagToItem(item, "Special");
        }

        private static void TryBindTotemModel(Item item, NewWeaponTotemSpec spec)
        {
            if (string.IsNullOrEmpty(spec.ModelBaseName)) return;

            try
            {
                EquipmentFactory.TryBindLoadedEquipmentModel(item, spec.ModelBaseName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(spec.LogPrefix + " 绑定模型失败: " + e.Message);
            }
        }

        private static void ApplyModifier(Item item, string key, float value, string logPrefix)
        {
            if (string.IsNullOrEmpty(key)) return;

            try
            {
                // 幂等：配置器可能被重复调用，EnsureModifierOnItem 不会叠加
                EquipmentHelper.EnsureModifierOnItem(item, key, ModifierType.Add, value, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " 添加 modifier 失败: " + e.Message);
            }
        }

        private static void InjectItemLocalization(
            Item item, string nameCN, string nameEN, string descCN, string descEN, string logPrefix)
        {
            try
            {
                // 语言在取用时解析：玩家能在游戏里切语言，ModBehaviour 会重跑注入
                string displayName = L10n.T(nameCN, nameEN);
                string description = L10n.T(descCN, descEN);

                string itemKey = "Item_" + item.TypeID;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " 本地化注入失败: " + e.Message);
            }
        }
    }
}
