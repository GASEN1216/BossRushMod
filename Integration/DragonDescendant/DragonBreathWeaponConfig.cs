// ============================================================================
// DragonBreathWeaponConfig.cs - 龙息武器完整配置
// ============================================================================
// 模块说明：
//   配置龙息武器的配件槽位、弹药类型、耐久度、标签和属性
//   属性值比MCX Super略强，作为龙裔遗族Boss专属掉落
//   支持运行时配置（当玩家装备武器时自动应用）
// ============================================================================

using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 龙息武器完整配置
    /// </summary>
    public static partial class DragonBreathWeaponConfig
    {
        // ========== 武器配置常量 ==========

        /// <summary>
        /// 龙息武器TypeID（来自DragonBreathConfig）
        /// </summary>
        public const int WEAPON_TYPE_ID = DragonBreathConfig.WEAPON_TYPE_ID;

        /// <summary>
        /// 弹药类型（BR = 战斗步枪弹药，与MCX Spear相同）
        /// 注意：MCX Spear实际使用BR口径，不是L口径
        /// </summary>
        public const string CALIBER = "BR";

        /// <summary>
        /// 最大耐久度（与MCX Spear相同）
        /// </summary>
        public const float MAX_DURABILITY = 100f;

        /// <summary>
        /// 维修损耗比例（与MCX Spear相同）
        /// </summary>
        public const float REPAIR_LOSS_RATIO = 0.2f;

        /// <summary>
        /// 开枪声音Key（与MCX Spear相同：rifle_heavy）
        /// </summary>
        public const string SHOOT_KEY = "rifle_heavy";

        // ========== 缓存字段（避免反射开销）==========
        // Stat类的baseValue和display字段缓存
        private static FieldInfo cachedStatBaseValueField = null;
        private static FieldInfo cachedStatDisplayField = null;
        private static bool statFieldsCached = false;

        // 子弹和枪口特效预制体缓存
        private static Projectile cachedBurnBullet = null;
        private static GameObject cachedFireMuzzle = null;
        private static bool bulletPrefabSearched = false;
        private static bool muzzlePrefabSearched = false;

        // ========== 配件槽位配置 ==========
        // 槽位Key -> 对应的Tag名称
        // 注意：Tag名称必须与GameplayDataSettings.Tags.AllTags中的name完全匹配

        // 槽位Key到Tag名称的映射
        // 重要：槽位Key和Tag名称不一定相同！
        // 例如：槽位Key是"Tec"，但对应的Tag名称是"TecEquip"
        private static readonly Dictionary<string, string> SLOT_KEY_TO_TAG = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Scope", "Scope" },           // 瞄具 - Key和Tag名称相同
            { "Muzzle", "Muzzle" },         // 枪口 - Key和Tag名称相同
            { "Grip", "Grip" },             // 握把 - Key和Tag名称相同
            { "Stock", "Stock" },           // 枪托 - Key和Tag名称相同
            { "Tec", "TecEquip" },          // 战术 - Key是Tec，Tag名称是TecEquip
            { "Mag", "Magazine" },          // 弹夹 - Key是Mag，Tag名称是Magazine
            { "Magazine", "Magazine" },     // 弹夹（备用Key）
            { "Special", "TecEquip" }       // 战术（备用Key）
        };

        // ========== 武器Stats属性（与MCX Spear完全相同）==========
        // 需要在UI中显示的属性（与MCX Spear显示一致）
        private static readonly HashSet<string> DISPLAY_STATS = new HashSet<string>
        {
            "Damage", "ShootSpeed", "Capacity", "ReloadTime", "BulletSpeed", "BulletDistance",
            "CritDamageFactor", "SoundRange", "ADSTime", "MoveSpeedMultiplier",
            "AdsWalkSpeedMultiplier", "ExplosionDamageMultiplier", "ScatterFactor",
            "ScatterFactorADS", "RecoilScaleV", "RecoilScaleH"
        };

        // 龙息武器属性（比MCX Super略强，作为Boss专属掉落）
        private static readonly Dictionary<string, float> WEAPON_STATS = new Dictionary<string, float>
        {
            { "Damage", 23f },              // 伤害：21 -> 23
            { "ShootSpeed", 13f },          // 射速：12.5 -> 13
            { "ShootSpeedGainEachShoot", 0f },
            { "ShootSpeedGainByShootMax", 0f },
            { "Capacity", 20f },
            { "ReloadTime", 3f },
            { "BurstCount", 1f },
            { "BulletSpeed", 122f },        // 子弹速度：118 -> 122
            { "BulletDistance", 28f },      // 射程：26.2 -> 28
            { "Penetrate", 0f },
            { "TraceAbility", 0f },
            { "CritRate", 0.25f },          // 暴击率：0.23 -> 0.25
            { "CritDamageFactor", 1.5f },
            { "ArmorPiercing", 0f },
            { "ArmorBreak", 0f },
            { "ShotCount", 1f },
            { "ShotAngle", 0f },
            { "SoundRange", 30.5f },
            { "ADSAimDistanceFactor", 1f },
            { "ADSTime", 0.65f },
            { "MoveSpeedMultiplier", 0.82f },
            { "AdsWalkSpeedMultiplier", 0.45f },
            { "ExplosionDamageMultiplier", 1f },
            { "BuffChance", 0.5f },
            { "DefaultScatter", 0.289f },
            { "MaxScatter", 0.963f },
            { "ScatterGrow", 0.252f },
            { "ScatterRecover", 0.28f },
            { "DefaultScatterADS", 0.332f },
            { "MaxScatterADS", 0.919f },
            { "ScatterGrowADS", 0.251f },
            { "ScatterRecoverADS", 0.38f },
            { "ScatterFactor", 27f },       // 散布：29.8 -> 27（更精准）
            { "ScatterFactorADS", 8f },     // ADS散布：9.03 -> 8（更精准）
            { "RecoilVMin", 0.884f },
            { "RecoilVMax", 1.116f },
            { "RecoilHMin", -0.4f },
            { "RecoilHMax", 0.6f },
            { "RecoilTime", 0.075f },
            { "RecoilScaleV", 35f },        // 垂直后坐力：38.7 -> 35
            { "RecoilScaleH", 40f },        // 水平后坐力：45 -> 40
            { "RecoilRecoverTime", 0.12f },
            { "RecoilRecover", 500f },
            { "FlashLight", 0f },
            { "OverrideTriggerMode", 0f }
        };

        /// <summary>
        /// 尝试配置龙息武器（在EquipmentFactory加载后调用，配置预制体）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null) return false;

            bool isDragonBreath = item.TypeID == WEAPON_TYPE_ID ||
                                  baseName.Equals("dragon_Gun", StringComparison.OrdinalIgnoreCase) ||
                                  baseName.Equals("Dragon_Gun", StringComparison.OrdinalIgnoreCase);

            if (!isDragonBreath) return false;

            ConfigureWeapon(item);
            return true;
        }

        /// <summary>
        /// 配置龙息武器的所有属性
        /// </summary>
        public static void ConfigureWeapon(Item item)
        {
            if (item == null) return;

            try
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 开始配置龙息武器...");

                ConfigureSharedGunFoundation(item, CALIBER, MAX_DURABILITY, REPAIR_LOSS_RATIO);
                SetWeaponStats(item);
                AddWeaponTags(item);
                SetBulletCountDisplay(item);  // 设置BulletCount显示
                ConfigureGunSettings(item);   // 配置枪械音效和特效
                SyncItemValueFromRawValue(item);

                ModBehaviour.DevLog("[DragonBreathWeapon] 配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 配置出错: " + e.Message);
            }
        }

        private static void SyncItemValueFromRawValue(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                int rawValue = item.GetTotalRawValue();
                if (rawValue > 0 && item.Value < rawValue)
                {
                    item.Value = rawValue;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 同步物品价值失败: " + e.Message);
            }
        }

        public static void ConfigureSharedGunFoundation(Item item, string caliber, float maxDurability, float repairLossRatio)
        {
            if (item == null)
            {
                return;
            }

            EnsureInventoryComponent(item);
            ConfigureSlotTags(item, false);
            SetCaliber(item, caliber);
            SetDurability(item, maxDurability);
            SetRepairLossRatio(item, repairLossRatio);
            SetBulletCountDisplay(item);
        }

        public static void ConfigureSharedFireGunSettings(Item item, bool useBurnBullet)
        {
            if (useBurnBullet)
            {
                ConfigureGunSettings(item);
                return;
            }

            try
            {
                ItemSetting_Gun gunSetting = item != null ? item.GetComponent<ItemSetting_Gun>() : null;
                if (gunSetting == null)
                {
                    return;
                }

                gunSetting.shootKey = SHOOT_KEY;
                gunSetting.reloadKey = RELOAD_KEY;

                if (gunSetting.muzzleFxPfb == null || gunSetting.muzzleFxPfb.name != "MuzzleFlash_Fire")
                {
                    GameObject fireMuzzle = FindMuzzleFxPrefab("MuzzleFlash_Fire");
                    if (fireMuzzle != null)
                    {
                        gunSetting.muzzleFxPfb = fireMuzzle;
                    }
                }

                EnsureGunSettingStatsReference(gunSetting, item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] ConfigureSharedFireGunSettings寮傚父: " + e.Message);
            }
        }

        private static void EnsureInventoryComponent(Item item)
        {
            try
            {
                // 检查当前Inventory状态
                bool hasInventory = item.Inventory != null;
                ModBehaviour.DevLog("[DragonBreathWeapon] 检查Inventory: " + (hasInventory ? "已存在" : "不存在"));

                if (hasInventory) return;

                // 尝试创建Inventory组件
                ModBehaviour.DevLog("[DragonBreathWeapon] 尝试创建Inventory组件...");
                item.CreateInventoryComponent();

                // 验证创建结果
                bool created = item.Inventory != null;
                ModBehaviour.DevLog("[DragonBreathWeapon] Inventory创建结果: " + (created ? "成功" : "失败"));

                if (!created)
                {
                    // 尝试手动添加Inventory组件（使用反射设置AttachedToItem）
                    ModBehaviour.DevLog("[DragonBreathWeapon] 尝试手动添加Inventory组件...");
                    var inventory = item.gameObject.AddComponent<Inventory>();
                    if (inventory != null)
                    {
                        // 使用反射设置私有字段
                        var field = typeof(Item).GetField("inventory",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(item, inventory);
                            ModBehaviour.DevLog("[DragonBreathWeapon] 手动添加Inventory成功");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] EnsureInventoryComponent异常: " + e.Message);
                ModBehaviour.DevLog("[DragonBreathWeapon] 堆栈: " + e.StackTrace);
            }
        }

        private static void ConfigureSlotTags(Item item, bool logAvailableTags = true)
        {
            if (item.Slots == null || item.Slots.Count == 0)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 武器没有槽位");
                return;
            }

            int slotCount = item.Slots.Count;
            ModBehaviour.DevLog("[DragonBreathWeapon] 开始配置 " + slotCount + " 个槽位的Tags");

            // 打印所有可用的Tag名称（仅首次）
            if (logAvailableTags)
            {
                PrintAllAvailableTags();
            }

            for (int i = 0; i < slotCount; i++)
            {
                Slot slot = item.Slots.GetSlotByIndex(i);
                if (slot == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 槽位#" + (i+1) + " 为null");
                    continue;
                }

                string slotKey = slot.Key;
                string tagName = slotKey; // 默认使用槽位Key作为Tag名称

                // 查找映射：槽位Key -> Tag名称
                // 使用TryGetValue确保正确获取映射值
                string mappedTagName;
                if (SLOT_KEY_TO_TAG.TryGetValue(slotKey, out mappedTagName))
                {
                    tagName = mappedTagName;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 槽位#" + (i+1) + " Key='" + slotKey + "' 映射到Tag='" + tagName + "'");
                }
                else
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 槽位#" + (i+1) + " Key='" + slotKey + "' 无映射，使用原始Key作为Tag名称");
                }

                Tag targetTag = GetTagByName(tagName);
                if (targetTag != null)
                {
                    slot.requireTags.Clear();
                    slot.requireTags.Add(targetTag);
                    ModBehaviour.DevLog("[DragonBreathWeapon] 槽位#" + (i+1) + " 设置Tag成功: " + targetTag.DisplayName);
                }
                else
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 槽位#" + (i+1) + " 找不到Tag: '" + tagName + "'");
                }
            }
        }

        private static bool tagsPrinted = false;

        /// <summary>
        /// 打印所有可用的Tag名称（用于调试）
        /// </summary>
        private static void PrintAllAvailableTags()
        {
            if (tagsPrinted) return;
            tagsPrinted = true;

            try
            {
                var allTags = GameplayDataSettings.Tags.AllTags;
                if (allTags == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] AllTags为null");
                    return;
                }

                ModBehaviour.DevLog("[DragonBreathWeapon] === 所有可用Tag (" + allTags.Count + "个) ===");
                foreach (Tag tag in allTags)
                {
                    if (tag != null)
                    {
                        ModBehaviour.DevLog("[DragonBreathWeapon] Tag: name='" + tag.name + "', DisplayName='" + tag.DisplayName + "'");
                    }
                }
                ModBehaviour.DevLog("[DragonBreathWeapon] === Tag列表结束 ===");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 打印Tag列表失败: " + e.Message);
            }
        }

        // ========== Tag查找缓存 ==========
        private static MethodInfo cachedTagsGetMethod = null;
        private static bool tagsGetMethodCached = false;
        private static Dictionary<string, Tag> tagCache = new Dictionary<string, Tag>();

        private static Tag GetTagByName(string tagName)
        {
            // 先检查缓存
            Tag cachedTag;
            if (tagCache.TryGetValue(tagName, out cachedTag))
            {
                return cachedTag;
            }

            try
            {
                var tagsData = GameplayDataSettings.Tags;
                if (tagsData != null)
                {
                    // 缓存Get方法（只执行一次）
                    if (!tagsGetMethodCached)
                    {
                        cachedTagsGetMethod = tagsData.GetType().GetMethod("Get",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        tagsGetMethodCached = true;
                    }

                    if (cachedTagsGetMethod != null)
                    {
                        Tag result = cachedTagsGetMethod.Invoke(tagsData, new object[] { tagName }) as Tag;
                        if (result != null)
                        {
                            tagCache[tagName] = result;
                            return result;
                        }
                    }
                }

                var allTags = GameplayDataSettings.Tags.AllTags;
                if (allTags != null)
                {
                    foreach (Tag tag in allTags)
                    {
                        if (tag != null && tag.name == tagName)
                        {
                            tagCache[tagName] = tag;
                            return tag;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void SetCaliber(Item item, string caliber)
        {
            try
            {
                var constants = item.Constants;
                if (constants == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] Constants为null，无法设置Caliber");
                    return;
                }

                // 设置Caliber值并启用显示
                var caliberEntry = constants.GetEntry("Caliber");
                if (caliberEntry != null)
                {
                    caliberEntry.SetString(caliber);
                    caliberEntry.Display = true;  // 启用在详细页显示
                    ModBehaviour.DevLog("[DragonBreathWeapon] 设置Caliber为: '" + caliber + "', Display=true");
                }
                else
                {
                    // 如果没有现有条目，创建新的
                    constants.SetString("Caliber", caliber, true);
                    // 再次获取并设置Display
                    caliberEntry = constants.GetEntry("Caliber");
                    if (caliberEntry != null)
                    {
                        caliberEntry.Display = true;
                    }
                    ModBehaviour.DevLog("[DragonBreathWeapon] 创建Caliber为: '" + caliber + "', Display=true");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] SetCaliber异常: " + e.Message);
            }
        }

        /// <summary>
        /// 设置BulletCount变量的Display属性为true，使其在详细页显示
        /// 关键：ItemSetting_Gun.Start()会在其Start方法中设置BulletCount变量
        /// 我们需要确保在那之后设置Display=true，或者手动触发相同的逻辑
        /// </summary>
        private static void SetBulletCountDisplay(Item item)
        {
            try
            {
                if (item.Variables == null) return;

                var gunSetting = item.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 没有ItemSetting_Gun组件，跳过BulletCount设置");
                    return;
                }

                int bulletCountHash = "BulletCount".GetHashCode();

                // 首先尝试获取现有的BulletCount变量
                var bulletCountEntry = item.Variables.GetEntry(bulletCountHash);

                if (bulletCountEntry != null)
                {
                    // 变量已存在，直接设置Display
                    bulletCountEntry.Display = true;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 设置现有BulletCount Display=true");
                }
                else
                {
                    // 变量不存在，使用默认值0创建（避免调用gunSetting.BulletCount触发错误）
                    int defaultBulletCount = 0;

                    // 使用字符串key版本的SetInt来创建变量
                    item.Variables.SetInt("BulletCount", defaultBulletCount, true);

                    // 再次获取并设置Display
                    bulletCountEntry = item.Variables.GetEntry(bulletCountHash);
                    if (bulletCountEntry != null)
                    {
                        bulletCountEntry.Display = true;
                        ModBehaviour.DevLog("[DragonBreathWeapon] 创建BulletCount=" + defaultBulletCount + ", Display=true");
                    }
                    else
                    {
                        ModBehaviour.DevLog("[DragonBreathWeapon] 警告：无法创建BulletCount变量");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] SetBulletCountDisplay异常: " + e.Message);
            }
        }

        private static void SetDurability(Item item, float maxDurability)
        {
            try
            {
                EquipmentHelper.SetItemConstant(item, "MaxDurability", maxDurability);
                item.Durability = maxDurability;
            }
            catch { }
        }

        private static void SetRepairLossRatio(Item item, float ratio)
        {
            try
            {
                EquipmentHelper.SetItemConstant(item, "RepairLossRatio", ratio);
            }
            catch { }
        }

        private static void SetWeaponStats(Item item)
        {
            try
            {
                // 确保 Stats 组件存在
                if (item.Stats == null)
                {
                    item.CreateStatsComponent();
                    if (item.Stats == null)
                    {
                        ModBehaviour.DevLog("[DragonBreathWeapon] 错误：无法创建 Stats 组件");
                        return;
                    }
                }

                int addedCount = 0;
                int updatedCount = 0;

                foreach (var kvp in WEAPON_STATS)
                {
                    // 只有在DISPLAY_STATS中的属性才显示
                    bool shouldDisplay = DISPLAY_STATS.Contains(kvp.Key);

                    Stat existingStat = item.Stats.GetStat(kvp.Key);
                    if (existingStat != null)
                    {
                        SetStatBaseValue(existingStat, kvp.Value);
                        SetStatDisplay(existingStat, shouldDisplay);
                        updatedCount++;
                    }
                    else
                    {
                        Stat newStat = new Stat(kvp.Key, kvp.Value, shouldDisplay);
                        item.Stats.Add(newStat);
                        addedCount++;
                    }
                }

                // 验证关键 Stats 是否正确设置（用于调试）
                VerifyCriticalStats(item);

                int removedLegacyModifiers = RemoveLegacyTrackingModifiers(item);
                ModBehaviour.DevLog("[DragonBreathWeapon] Stats设置完成: 新增=" + addedCount + ", 更新=" + updatedCount +
                    ", 清理遗留追踪Modifier=" + removedLegacyModifiers);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] SetWeaponStats异常: " + e.Message);
                ModBehaviour.DevLog("[DragonBreathWeapon] 堆栈: " + e.StackTrace);
            }
        }

        /// <summary>
        /// 验证关键 Stats 是否正确设置（用于调试 MaxScatter 空引用问题）
        /// </summary>
        private static void VerifyCriticalStats(Item item)
        {
            try
            {
                // 验证 ItemAgent_Gun.MaxScatter 依赖的关键 Stats
                string[] criticalStatKeys = { "MaxScatter", "MaxScatterADS", "ScatterFactor", "ScatterFactorADS" };

                foreach (string statKey in criticalStatKeys)
                {
                    int statHash = statKey.GetHashCode();
                    float value = item.GetStatValue(statHash);
                    if (value == 0f)
                    {
                        ModBehaviour.DevLog("[DragonBreathWeapon] 警告: 关键Stat '" + statKey + "' 值为0");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] VerifyCriticalStats异常: " + e.Message);
            }
        }

        /// <summary>
        /// 清理旧版本为了重铸追踪而额外添加的隐藏 Modifier。
        /// 现在龙息枪与龙皇铳一样，直接以 Stat/Variable 作为重铸来源，
        /// 避免同名隐藏 Modifier 抢占属性栏差异对比。
        /// </summary>
        private static int RemoveLegacyTrackingModifiers(Item item)
        {
            if (item == null || item.Modifiers == null)
            {
                return 0;
            }

            try
            {
                List<ModifierDescription> toRemove = new List<ModifierDescription>();
                foreach (ModifierDescription mod in item.Modifiers)
                {
                    if (mod == null || mod.Display)
                    {
                        continue;
                    }

                    if (!DISPLAY_STATS.Contains(mod.Key))
                    {
                        continue;
                    }

                    if (Mathf.Abs(mod.Value) > 0.001f)
                    {
                        continue;
                    }

                    toRemove.Add(mod);
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    item.Modifiers.Remove(toRemove[i]);
                }

                return toRemove.Count;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 清理遗留追踪Modifier异常: " + e.Message);
                return 0;
            }
        }

        private static void SetStatDisplay(Stat stat, bool display)
        {
            try
            {
                // 缓存反射字段，避免每次调用都查找
                if (!statFieldsCached)
                {
                    CacheStatFields();
                }

                if (cachedStatDisplayField != null)
                    cachedStatDisplayField.SetValue(stat, display);
            }
            catch { }
        }

        private static void SetStatBaseValue(Stat stat, float value)
        {
            try
            {
                // 缓存反射字段，避免每次调用都查找
                if (!statFieldsCached)
                {
                    CacheStatFields();
                }

                if (cachedStatBaseValueField != null)
                    cachedStatBaseValueField.SetValue(stat, value);
            }
            catch { }
        }

        /// <summary>
        /// 缓存Stat类的反射字段（只执行一次）
        /// </summary>
        private static void CacheStatFields()
        {
            if (statFieldsCached) return;

            try
            {
                cachedStatBaseValueField = typeof(Stat).GetField("baseValue",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                cachedStatDisplayField = typeof(Stat).GetField("display",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch { }

            statFieldsCached = true;
        }

        private static void AddWeaponTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Weapon");
            EquipmentHelper.AddTagToItem(item, "Gun");
            EquipmentHelper.AddTagToItem(item, "GunType_BR");
            EquipmentHelper.AddTagToItem(item, "Western");
            EquipmentHelper.AddTagToItem(item, "Special");
            EquipmentHelper.AddRepairableTag(item);
        }

        /// <summary>
        /// 换弹声音Key（与火AK相同：mag_rifle）
        /// </summary>
        public const string RELOAD_KEY = "mag_rifle";

        /// <summary>
        /// 配置枪械的音效（shootKey, reloadKey）和子弹预制体
        /// 静默执行，仅在实际修改时输出日志
        /// </summary>
        private static void ConfigureGunSettings(Item item)
        {
            try
            {
                ItemSetting_Gun gunSetting = item.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null) return;

                // 设置开枪声音Key（与MCX Spear相同）
                if (gunSetting.shootKey != SHOOT_KEY)
                {
                    string oldShootKey = gunSetting.shootKey;
                    gunSetting.shootKey = SHOOT_KEY;
                    ModBehaviour.DevLog("[DragonBreathWeapon] shootKey: '" + oldShootKey + "' -> '" + SHOOT_KEY + "'");
                }

                // 设置换弹声音Key（与MCX Spear相同，避免使用无效的默认值）
                if (gunSetting.reloadKey != RELOAD_KEY)
                {
                    string oldReloadKey = gunSetting.reloadKey;
                    gunSetting.reloadKey = RELOAD_KEY;
                    ModBehaviour.DevLog("[DragonBreathWeapon] reloadKey: '" + oldReloadKey + "' -> '" + RELOAD_KEY + "'");
                }

                // 设置子弹预制体（使用燃烧子弹 BulletNormal_Burn，与带火AK-47相同）
                if (gunSetting.bulletPfb == null || gunSetting.bulletPfb.name != "BulletNormal_Burn")
                {
                    try
                    {
                        // 尝试从游戏预制体中获取 BulletNormal_Burn
                        var burnBullet = FindProjectilePrefab("BulletNormal_Burn");
                        if (burnBullet != null)
                        {
                            gunSetting.bulletPfb = burnBullet;
                            ModBehaviour.DevLog("[DragonBreathWeapon] bulletPfb -> BulletNormal_Burn");
                        }
                        else
                        {
                            // 回退到默认子弹
                            var defaultBullet = GameplayDataSettings.Prefabs.DefaultBullet;
                            if (defaultBullet != null && gunSetting.bulletPfb == null)
                            {
                                gunSetting.bulletPfb = defaultBullet;
                                ModBehaviour.DevLog("[DragonBreathWeapon] bulletPfb -> DefaultBullet (回退)");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[DragonBreathWeapon] 设置子弹预制体失败: " + e.Message);
                    }
                }

                // 设置枪口火焰（使用火焰枪口 MuzzleFlash_Fire，与带火AK-47相同）
                if (gunSetting.muzzleFxPfb == null || gunSetting.muzzleFxPfb.name != "MuzzleFlash_Fire")
                {
                    try
                    {
                        var fireMuzzle = FindMuzzleFxPrefab("MuzzleFlash_Fire");
                        if (fireMuzzle != null)
                        {
                            gunSetting.muzzleFxPfb = fireMuzzle;
                            ModBehaviour.DevLog("[DragonBreathWeapon] muzzleFxPfb -> MuzzleFlash_Fire");
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[DragonBreathWeapon] 设置枪口火焰失败: " + e.Message);
                    }
                }

                // 确保 ItemSetting_Gun 的 Stats 引用正确（修复 MaxScatter 空引用问题）
                // ItemSetting_Gun 需要引用 Item.Stats 来计算散布等属性
                EnsureGunSettingStatsReference(gunSetting, item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] ConfigureGunSettings异常: " + e.Message);
            }
        }

        /// <summary>
        /// 确保 ItemSetting_Gun 正确引用 Item.Stats（修复 MaxScatter 空引用问题）
        /// ItemSetting_Gun.MaxScatter 等属性需要从 Item.Stats 读取
        /// </summary>
        private static void EnsureGunSettingStatsReference(ItemSetting_Gun gunSetting, Item item)
        {
            try
            {
                // 使用反射检查 ItemSetting_Gun 的 stats 字段
                FieldInfo statsField = typeof(ItemSetting_Gun).GetField("stats",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (statsField != null)
                {
                    var currentStats = statsField.GetValue(gunSetting);
                    if (currentStats == null && item.Stats != null)
                    {
                        // 如果 gunSetting.stats 为 null，设置为 item.Stats
                        statsField.SetValue(gunSetting, item.Stats);
                        ModBehaviour.DevLog("[DragonBreathWeapon] 已设置 ItemSetting_Gun.stats 引用");
                    }
                }

                // 同时检查 item 字段
                FieldInfo itemField = typeof(ItemSetting_Gun).GetField("item",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (itemField != null)
                {
                    var currentItem = itemField.GetValue(gunSetting);
                    if (currentItem == null)
                    {
                        itemField.SetValue(gunSetting, item);
                        ModBehaviour.DevLog("[DragonBreathWeapon] 已设置 ItemSetting_Gun.item 引用");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] EnsureGunSettingStatsReference异常: " + e.Message);
            }
        }

        /// <summary>
        /// 从游戏中查找子弹预制体（带缓存）
        /// </summary>
        private static Projectile FindProjectilePrefab(string name)
        {
            // 使用缓存
            if (name == "BulletNormal_Burn")
            {
                if (cachedBurnBullet != null) return cachedBurnBullet;
                if (bulletPrefabSearched) return null;
                bulletPrefabSearched = true;
            }

            try
            {
                // 从已加载的武器中查找（遍历entries）
                var instance = ItemAssetsCollection.Instance;
                if (instance != null && instance.entries != null)
                {
                    foreach (var entry in instance.entries)
                    {
                        if (entry == null || entry.prefab == null) continue;
                        var gunSetting = entry.prefab.GetComponent<ItemSetting_Gun>();
                        if (gunSetting != null && gunSetting.bulletPfb != null && gunSetting.bulletPfb.name == name)
                        {
                            // 缓存结果
                            if (name == "BulletNormal_Burn")
                            {
                                cachedBurnBullet = gunSetting.bulletPfb;
                            }
                            return gunSetting.bulletPfb;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 从游戏中查找枪口火焰预制体（带缓存）
        /// </summary>
        private static GameObject FindMuzzleFxPrefab(string name)
        {
            // 使用缓存
            if (name == "MuzzleFlash_Fire")
            {
                if (cachedFireMuzzle != null) return cachedFireMuzzle;
                if (muzzlePrefabSearched) return null;
                muzzlePrefabSearched = true;
            }

            try
            {
                // 从已加载的武器中查找
                var instance = ItemAssetsCollection.Instance;
                if (instance != null && instance.entries != null)
                {
                    foreach (var entry in instance.entries)
                    {
                        if (entry == null || entry.prefab == null) continue;
                        var gunSetting = entry.prefab.GetComponent<ItemSetting_Gun>();
                        if (gunSetting != null && gunSetting.muzzleFxPfb != null && gunSetting.muzzleFxPfb.name == name)
                        {
                            // 缓存结果
                            if (name == "MuzzleFlash_Fire")
                            {
                                cachedFireMuzzle = gunSetting.muzzleFxPfb;
                            }
                            return gunSetting.muzzleFxPfb;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // ========== 火焰特效复制 ==========

        // 带火AK-47的TypeID
        private const int FIRE_AK47_TYPE_ID = 862;

        // 已添加特效的ItemAgent实例ID集合
        private static HashSet<int> effectsAddedAgents = new HashSet<int>();

        // 缓存的火焰特效源ItemAgent
        private static GameObject cachedFireAK47Model = null;

        // 标记是否已尝试过查找火AK-47模型（避免重复查找和日志输出）
        private static bool fireAK47ModelSearched = false;

        /// <summary>
        /// 清理所有静态缓存（场景切换时调用，防止持有已销毁对象引用）
        /// </summary>
        public static void ClearStaticCache()
        {
            // 清理特效追踪集合
            effectsAddedAgents.Clear();

            // 清理预制体缓存（可能指向已卸载的资源）
            cachedFireAK47Model = null;
            fireAK47ModelSearched = false;
            cachedBurnBullet = null;
            bulletPrefabSearched = false;
            cachedFireMuzzle = null;
            muzzlePrefabSearched = false;

            // 清理Tag缓存
            tagCache.Clear();
            cachedTagsGetMethod = null;
            tagsGetMethodCached = false;

            // 清理Stat反射缓存（类型信息不需要清理，但重置标记以便重新验证）
            statFieldsCached = false;

            // 清理ParticleSystem反射缓存
            psReflectionCached = false;

            // 清理SodaPointLight反射缓存
            sodaLightReflectionCached = false;

            // 重置Tag打印标记
            tagsPrinted = false;
        }

    }
}
