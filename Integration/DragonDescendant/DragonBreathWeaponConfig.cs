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
using System.Linq;
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
    public static class DragonBreathWeaponConfig
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
        
        /// <summary>
        /// 换弹声音Key（与MCX Spear相同：rifle_heavy）
        /// </summary>
        public const string RELOAD_KEY = "rifle_heavy";
        
        // ========== 已配置的实例跟踪 ==========
        // 用于避免重复配置同一个Item实例
        private static HashSet<int> configuredInstances = new HashSet<int>();
        
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
        /// 检查并配置龙息武器（运行时调用，用于配置玩家装备的武器实例）
        /// 每次都强制配置音效和子弹，确保属性正确
        /// </summary>
        public static void TryConfigureRuntime(Item item)
        {
            if (item == null) return;
            if (item.TypeID != WEAPON_TYPE_ID) return;
            
            int instanceId = item.GetInstanceID();
            bool isFirstConfig = !configuredInstances.Contains(instanceId);
            
            // 每次都强制配置音效和子弹（解决实例化后默认值覆盖的问题）
            ConfigureGunSettings(item);
            
            // 首次配置时执行完整配置
            if (isFirstConfig)
            {
                // 检查Caliber当前值（用于调试）
                string currentCaliber = null;
                try
                {
                    if (item.Constants != null)
                        currentCaliber = item.Constants.GetString("Caliber");
                }
                catch { }
                
                ModBehaviour.DevLog("[DragonBreathWeapon] 检测到龙息武器，开始运行时配置 (InstanceID=" + instanceId + ", Caliber='" + (currentCaliber ?? "null") + "')");
                ConfigureWeapon(item);
                configuredInstances.Add(instanceId);
                ModBehaviour.DevLog("[DragonBreathWeapon] 运行时配置完成 (InstanceID=" + instanceId + ")");
            }
        }
        
        /// <summary>
        /// 尝试配置龙息武器（在EquipmentFactory加载后调用）
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
                
                EnsureInventoryComponent(item);
                ConfigureSlotTags(item);
                SetCaliber(item, CALIBER);
                SetDurability(item, MAX_DURABILITY);
                SetRepairLossRatio(item, REPAIR_LOSS_RATIO);
                SetWeaponStats(item);
                AddWeaponTags(item);
                SetBulletCountDisplay(item);  // 设置BulletCount显示
                ConfigureGunSettings(item);   // 配置枪械音效和特效
                
                ModBehaviour.DevLog("[DragonBreathWeapon] 配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 配置出错: " + e.Message);
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
        
        private static void ConfigureSlotTags(Item item)
        {
            if (item.Slots == null || item.Slots.Count == 0)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 武器没有槽位");
                return;
            }
            
            int slotCount = item.Slots.Count;
            ModBehaviour.DevLog("[DragonBreathWeapon] 开始配置 " + slotCount + " 个槽位的Tags");
            
            // 打印所有可用的Tag名称（仅首次）
            PrintAllAvailableTags();
            
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
        
        private static Tag GetTagByName(string tagName)
        {
            try
            {
                var tagsData = GameplayDataSettings.Tags;
                if (tagsData != null)
                {
                    var getMethod = tagsData.GetType().GetMethod("Get", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (getMethod != null)
                    {
                        Tag result = getMethod.Invoke(tagsData, new object[] { tagName }) as Tag;
                        if (result != null) return result;
                    }
                }
                
                var allTags = GameplayDataSettings.Tags.AllTags;
                if (allTags != null)
                {
                    foreach (Tag tag in allTags)
                    {
                        if (tag != null && tag.name == tagName)
                            return tag;
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
                    // 变量不存在，使用字符串版本创建（hash版本不会创建新变量）
                    int currentBulletCount = gunSetting.BulletCount;
                    if (currentBulletCount < 0) currentBulletCount = 0;
                    
                    // 使用字符串key版本的SetInt来创建变量
                    item.Variables.SetInt("BulletCount", currentBulletCount, true);
                    
                    // 再次获取并设置Display
                    bulletCountEntry = item.Variables.GetEntry(bulletCountHash);
                    if (bulletCountEntry != null)
                    {
                        bulletCountEntry.Display = true;
                        ModBehaviour.DevLog("[DragonBreathWeapon] 创建BulletCount=" + currentBulletCount + ", Display=true");
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
                if (item.Stats == null) item.CreateStatsComponent();
                
                foreach (var kvp in WEAPON_STATS)
                {
                    // 只有在DISPLAY_STATS中的属性才显示
                    bool shouldDisplay = DISPLAY_STATS.Contains(kvp.Key);
                    
                    Stat existingStat = item.Stats.GetStat(kvp.Key);
                    if (existingStat != null)
                    {
                        SetStatBaseValue(existingStat, kvp.Value);
                        SetStatDisplay(existingStat, shouldDisplay);
                    }
                    else
                    {
                        item.Stats.Add(new Stat(kvp.Key, kvp.Value, shouldDisplay));
                    }
                }
                ModBehaviour.DevLog("[DragonBreathWeapon] 已设置 " + WEAPON_STATS.Count + " 个武器Stats，显示 " + DISPLAY_STATS.Count + " 个");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] SetWeaponStats异常: " + e.Message);
            }
        }
        
        private static void SetStatDisplay(Stat stat, bool display)
        {
            try
            {
                // 尝试设置Stat的display属性
                FieldInfo displayField = typeof(Stat).GetField("display", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (displayField != null)
                    displayField.SetValue(stat, display);
            }
            catch { }
        }
        
        private static void SetStatBaseValue(Stat stat, float value)
        {
            try
            {
                FieldInfo baseValueField = typeof(Stat).GetField("baseValue", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (baseValueField != null)
                    baseValueField.SetValue(stat, value);
            }
            catch { }
        }
        
        private static void AddWeaponTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Weapon");
            EquipmentHelper.AddTagToItem(item, "Gun");
            EquipmentHelper.AddTagToItem(item, "GunType_BR");
            EquipmentHelper.AddTagToItem(item, "Western");
            EquipmentHelper.AddRepairableTag(item);
        }
        
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
                
                // 设置换弹声音Key（与MCX Spear相同）
                if (gunSetting.reloadKey != RELOAD_KEY)
                {
                    gunSetting.reloadKey = RELOAD_KEY;
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
                            }
                        }
                    }
                    catch { }
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
                    catch { }
                }
            }
            catch { }
        }
        
        /// <summary>
        /// 从游戏中查找子弹预制体
        /// </summary>
        private static Projectile FindProjectilePrefab(string name)
        {
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
                            return gunSetting.bulletPfb;
                        }
                    }
                }
            }
            catch { }
            return null;
        }
        
        /// <summary>
        /// 从游戏中查找枪口火焰预制体
        /// </summary>
        private static GameObject FindMuzzleFxPrefab(string name)
        {
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
        /// 为龙息武器的ItemAgent添加火焰特效（从带火AK-47复制）
        /// 在玩家手持龙息武器时调用
        /// </summary>
        public static void TryAddFireEffectsToAgent(ItemAgent_Gun gunAgent)
        {
            if (gunAgent == null) return;
            if (gunAgent.Item == null) return;
            if (gunAgent.Item.TypeID != WEAPON_TYPE_ID) return;
            
            int agentInstanceId = gunAgent.GetInstanceID();
            
            // 检查是否已添加过特效
            if (effectsAddedAgents.Contains(agentInstanceId)) return;
            
            try
            {
                // 从预制体获取带火AK-47的模型
                GameObject sourceModel = GetFireAK47Model();
                
                // 如果找不到源模型，静默返回（日志已在GetFireAK47Model中输出一次）
                if (sourceModel == null) return;
                
                // 复制特效
                CopyFireEffects(sourceModel, gunAgent.gameObject);
                effectsAddedAgents.Add(agentInstanceId);
                ModBehaviour.DevLog("[DragonBreathWeapon] 已为龙息武器添加火焰特效");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 添加火焰特效失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取带火AK-47的模型（从预制体，带缓存）
        /// </summary>
        private static GameObject GetFireAK47Model()
        {
            // 使用缓存
            if (cachedFireAK47Model != null) return cachedFireAK47Model;
            
            // 如果已经尝试过查找但失败了，直接返回null（避免重复查找）
            if (fireAK47ModelSearched) return null;
            
            // 标记已尝试查找
            fireAK47ModelSearched = true;
            
            try
            {
                // 从ItemAssetsCollection获取带火AK-47的预制体
                var prefab = ItemAssetsCollection.GetPrefab(FIRE_AK47_TYPE_ID);
                if (prefab == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 未找到带火AK-47预制体 (TypeID=" + FIRE_AK47_TYPE_ID + ")");
                    return null;
                }
                
                // 使用正确的API获取EquipmentModel预制体
                var agentUtilities = prefab.AgentUtilities;
                if (agentUtilities == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 带火AK-47的AgentUtilities为null");
                    return null;
                }
                
                // 使用GetPrefab方法获取EquipmentModel（与游戏源码一致）
                ItemAgent equipmentModelAgent = agentUtilities.GetPrefab("EquipmentModel");
                if (equipmentModelAgent != null)
                {
                    cachedFireAK47Model = equipmentModelAgent.gameObject;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 找到带火AK-47的EquipmentModel: " + cachedFireAK47Model.name);
                    return cachedFireAK47Model;
                }
                
                // 如果没有EquipmentModel，尝试获取Handheld模型
                ItemAgent handheldAgent = agentUtilities.GetPrefab("Handheld");
                if (handheldAgent != null)
                {
                    cachedFireAK47Model = handheldAgent.gameObject;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 找到带火AK-47的Handheld模型: " + cachedFireAK47Model.name);
                    return cachedFireAK47Model;
                }
                
                ModBehaviour.DevLog("[DragonBreathWeapon] 带火AK-47预制体中未找到EquipmentModel或Handheld");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 获取带火AK-47模型失败: " + e.Message);
            }
            return null;
        }
        
        /// <summary>
        /// 从源对象复制火焰特效到目标对象
        /// 烟雾和火花放在枪身（根节点），发光特效放在Muzzle
        /// </summary>
        private static void CopyFireEffects(GameObject source, GameObject target)
        {
            if (source == null || target == null) return;
            
            ModBehaviour.DevLog("[DragonBreathWeapon] === 开始复制火焰特效 ===");
            ModBehaviour.DevLog("[DragonBreathWeapon] 源对象: " + source.name);
            ModBehaviour.DevLog("[DragonBreathWeapon] 目标对象: " + target.name);
            
            // 打印源对象的子对象结构（用于调试）
            PrintChildHierarchy(source.transform, "[源]", 0);
            PrintChildHierarchy(target.transform, "[目标]", 0);
            
            Transform sourceRoot = source.transform;
            Transform targetRoot = target.transform;
            
            // 查找Muzzle位置（用于发光特效）
            Transform muzzleTransform = FindChildRecursive(targetRoot, "Muzzle");
            if (muzzleTransform == null)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 警告：未找到Muzzle");
            }
            
            // 烟雾和火花放在枪身根节点上（平行于枪身）
            CopyParticleSystemToBody(sourceRoot, targetRoot, "Smoke");
            CopyParticleSystemToBody(sourceRoot, targetRoot, "Spark");
            
            // 发光特效放在Muzzle（如果有的话）
            Transform lightParent = muzzleTransform != null ? muzzleTransform : targetRoot;
            CopySodaPointLights(sourceRoot, lightParent);
            
            ModBehaviour.DevLog("[DragonBreathWeapon] === 火焰特效复制完成 ===");
        }
        
        /// <summary>
        /// 复制粒子系统到枪身（根节点），调整位置使其在枪身中心
        /// </summary>
        private static void CopyParticleSystemToBody(Transform source, Transform targetRoot, string name)
        {
            try
            {
                Transform sourcePS = source.Find(name);
                if (sourcePS == null)
                {
                    sourcePS = FindChildRecursive(source, name);
                }
                
                if (sourcePS == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 未找到粒子系统: " + name);
                    return;
                }
                
                // 检查目标是否已有同名对象
                Transform existingPS = FindChildRecursive(targetRoot, name);
                if (existingPS != null)
                {
                    EnsureParticleSystemPlaying(existingPS.gameObject);
                    ModBehaviour.DevLog("[DragonBreathWeapon] 跳过已存在的粒子系统: " + name);
                    return;
                }
                
                // 复制粒子系统到根节点
                GameObject copy = UnityEngine.Object.Instantiate(sourcePS.gameObject, targetRoot);
                copy.name = name;
                
                // 放在枪身中心位置（根据龙息武器模型调整）
                // 枪身大约在 Y=0.1~0.2, Z=0.2~0.5 的范围
                copy.transform.localPosition = new Vector3(0f, 0.15f, 0.35f);
                copy.transform.localRotation = Quaternion.identity;  // 不旋转，平行于枪身
                copy.transform.localScale = sourcePS.localScale;
                copy.SetActive(true);
                
                EnsureParticleSystemPlaying(copy);
                
                ModBehaviour.DevLog("[DragonBreathWeapon] 复制粒子系统: " + name + 
                    " 到枪身 localPos=" + copy.transform.localPosition);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 复制粒子系统失败 (" + name + "): " + e.Message);
            }
        }
        
        /// <summary>
        /// 打印子对象层级结构（调试用，最多2层）
        /// </summary>
        private static void PrintChildHierarchy(Transform parent, string prefix, int depth)
        {
            if (depth > 2) return;
            
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                string indent = new string(' ', depth * 2);
                
                // 获取组件信息
                var components = child.GetComponents<Component>();
                string compNames = string.Join(", ", components
                    .Where(c => c != null && !(c is Transform))
                    .Select(c => c.GetType().Name)
                    .Take(3));
                
                ModBehaviour.DevLog(prefix + indent + "├─ " + child.name + 
                    (string.IsNullOrEmpty(compNames) ? "" : " (" + compNames + ")") +
                    " scale=" + child.localScale);
                
                PrintChildHierarchy(child, prefix, depth + 1);
            }
        }
        
        /// <summary>
        /// 确保粒子系统正在播放（使用反射避免直接引用ParticleSystem类型）
        /// </summary>
        private static void EnsureParticleSystemPlaying(GameObject obj)
        {
            if (obj == null) return;
            
            try
            {
                // 使用反射获取ParticleSystem组件并调用Play方法
                var psType = typeof(Component).Assembly.GetType("UnityEngine.ParticleSystem");
                if (psType == null)
                {
                    // 尝试从UnityEngine.ParticleSystemModule程序集获取
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        psType = asm.GetType("UnityEngine.ParticleSystem");
                        if (psType != null) break;
                    }
                }
                
                if (psType == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 无法找到ParticleSystem类型");
                    return;
                }
                
                // 获取组件
                var ps = obj.GetComponent(psType);
                if (ps == null) return;
                
                // 检查是否正在播放
                var isPlayingProp = psType.GetProperty("isPlaying", BindingFlags.Public | BindingFlags.Instance);
                bool isPlaying = isPlayingProp != null && (bool)isPlayingProp.GetValue(ps, null);
                
                if (!isPlaying)
                {
                    // 调用Play方法
                    var playMethod = psType.GetMethod("Play", new Type[] { typeof(bool) });
                    if (playMethod != null)
                    {
                        playMethod.Invoke(ps, new object[] { true });  // withChildren = true
                        ModBehaviour.DevLog("[DragonBreathWeapon] 启动粒子系统: " + obj.name);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] EnsureParticleSystemPlaying异常: " + e.Message);
            }
        }
        
        /// <summary>
        /// 复制SodaPointLight组件到目标父对象下
        /// </summary>
        private static void CopySodaPointLights(Transform source, Transform targetParent)
        {
            try
            {
                // 查找所有SodaPointLight
                var sodaLights = source.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && c.GetType().Name == "SodaPointLight")
                    .ToArray();
                
                if (sodaLights.Length == 0)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 源对象中未找到SodaPointLight");
                    return;
                }
                
                ModBehaviour.DevLog("[DragonBreathWeapon] 找到 " + sodaLights.Length + " 个SodaPointLight");
                ModBehaviour.DevLog("[DragonBreathWeapon] 发光点父对象: " + targetParent.name);
                
                int copyCount = 0;
                foreach (var light in sodaLights)
                {
                    string lightName = light.gameObject.name;
                    string newName = "FireLight_" + copyCount;
                    
                    // 检查目标是否已有同名对象
                    if (FindChildRecursive(targetParent, newName) != null)
                    {
                        ModBehaviour.DevLog("[DragonBreathWeapon] 跳过已存在的: " + newName);
                        copyCount++;
                        continue;
                    }
                    
                    // 记录源对象的缩放信息
                    Vector3 srcLocalScale = light.transform.localScale;
                    Vector3 srcLossyScale = light.transform.lossyScale;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 源 " + lightName + 
                        " localScale=" + srcLocalScale + " lossyScale=" + srcLossyScale);
                    
                    // 复制发光点对象到目标父对象
                    GameObject copy = UnityEngine.Object.Instantiate(light.gameObject, targetParent);
                    copy.name = newName;
                    
                    // 将发光点放在原点
                    copy.transform.localPosition = Vector3.zero;
                    copy.transform.localRotation = Quaternion.identity;
                    
                    // 使用和火AK一样的scale（直接使用源对象的scale）
                    copy.transform.localScale = srcLocalScale;
                    copy.SetActive(true);
                    
                    // 调用SodaPointLight的SyncToLight方法来初始化材质属性
                    var sodaLightComponent = copy.GetComponent<Component>();
                    if (sodaLightComponent != null && sodaLightComponent.GetType().Name == "SodaPointLight")
                    {
                        // 使用反射调用私有方法SyncToLight
                        var syncMethod = sodaLightComponent.GetType().GetMethod("SyncToLight", 
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (syncMethod != null)
                        {
                            syncMethod.Invoke(sodaLightComponent, null);
                            ModBehaviour.DevLog("[DragonBreathWeapon] 已调用SyncToLight");
                        }
                    }
                    
                    // 记录复制后的缩放信息
                    Vector3 dstLossyScale = copy.transform.lossyScale;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 复制 " + copy.name + 
                        " localScale=" + copy.transform.localScale + " lossyScale=" + dstLossyScale);
                    
                    copyCount++;
                }
                
                if (copyCount > 0)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 复制发光点: " + copyCount + "个");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 复制发光点失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 按路径查找子对象
        /// </summary>
        private static Transform FindChildByPath(Transform parent, string path)
        {
            if (parent == null || string.IsNullOrEmpty(path)) return null;
            
            string[] parts = path.Split('/');
            Transform current = parent;
            
            foreach (string part in parts)
            {
                current = current.Find(part);
                if (current == null) return null;
            }
            
            return current;
        }
        
        /// <summary>
        /// 递归查找子对象
        /// </summary>
        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null) return null;
            
            Transform found = parent.Find(name);
            if (found != null) return found;
            
            for (int i = 0; i < parent.childCount; i++)
            {
                found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            
            return null;
        }
    }
}