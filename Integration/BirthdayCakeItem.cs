// ============================================================================
// BirthdayCakeItem.cs - 生日蛋糕物品
// ============================================================================
// 模块说明：
//   管理生日蛋糕物品的加载、配置和注册，包括：
//   - 从 AssetBundle 加载生日蛋糕预制体
//   - 动态添加食物功能（FoodDrink）
//   - 动态添加 Buff 效果（高兴）
//   - 动态添加 Tag（食物、收藏品）
//   - 本地化注入
//   - 商店注入
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Duckov.ItemUsage;
using Duckov.Buffs;
using Duckov.Economy;
using Duckov.Utilities;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 生日蛋糕物品模块
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 生日蛋糕配置
        // ============================================================================
        
        /// <summary>
        /// 生日蛋糕物品 TypeID（需要与 Unity 预制体中的 typeID 一致）
        /// </summary>
        private const int BIRTHDAY_CAKE_TYPE_ID = 500002;
        
        /// <summary>
        /// 生日蛋糕显示名称（中文）
        /// </summary>
        private const string BIRTHDAY_CAKE_DISPLAY_NAME_CN = "生日蛋糕";
        
        /// <summary>
        /// 生日蛋糕显示名称（英文）
        /// </summary>
        private const string BIRTHDAY_CAKE_DISPLAY_NAME_EN = "Birthday Cake";
        
        /// <summary>
        /// 生日蛋糕描述（中文）
        /// </summary>
        private const string BIRTHDAY_CAKE_DESCRIPTION_CN = "祝你永远开开心心快快乐乐！----来自小猪鲨的祝福";
        
        /// <summary>
        /// 生日蛋糕描述（英文）
        /// </summary>
        private const string BIRTHDAY_CAKE_DESCRIPTION_EN = "May you always be happy! ----Blessings from Little Pig Shark";
        
        /// <summary>
        /// 获取本地化的生日蛋糕名称
        /// </summary>
        private static string BIRTHDAY_CAKE_DISPLAY_NAME { get { return L10n.T(BIRTHDAY_CAKE_DISPLAY_NAME_CN, BIRTHDAY_CAKE_DISPLAY_NAME_EN); } }
        
        /// <summary>
        /// 获取本地化的生日蛋糕描述
        /// </summary>
        private static string BIRTHDAY_CAKE_DESCRIPTION { get { return L10n.T(BIRTHDAY_CAKE_DESCRIPTION_CN, BIRTHDAY_CAKE_DESCRIPTION_EN); } }
        
        /// <summary>
        /// 生日蛋糕 AssetBundle 文件名
        /// </summary>
        private const string BIRTHDAY_CAKE_BUNDLE_NAME = "birthday_cake";
        
        /// <summary>
        /// 高兴 Buff 持续时间（秒）
        /// </summary>
        private const float HAPPY_BUFF_DURATION = 90f;
        
        /// <summary>
        /// 饱食度恢复值（100% = 100）
        /// </summary>
        private const float ENERGY_RESTORE_VALUE = 100f;
        
        // 生日蛋糕物品是否已初始化
        private bool birthdayCakeInitialized = false;
        
        // 生日蛋糕物品 TypeID（运行时确认）
        private int birthdayCakeTypeId = BIRTHDAY_CAKE_TYPE_ID;
        
        // 缓存的"高兴"Buff 预制体
        private Buff happyBuffPrefab = null;
        
        // ============================================================================
        // 初始化方法
        // ============================================================================
        
        /// <summary>
        /// 初始化生日蛋糕物品（从 AssetBundle 加载）
        /// </summary>
        private void InitializeBirthdayCakeItem()
        {
            if (birthdayCakeInitialized)
            {
                return;
            }
            birthdayCakeInitialized = true;
            
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", BIRTHDAY_CAKE_BUNDLE_NAME);
                
                if (!File.Exists(bundlePath))
                {
                    DevLog("[BirthdayCake] 未找到 AssetBundle: " + bundlePath);
                    return;
                }
                
                // 通过反射加载 AssetBundle
                Type assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule");
                if (assetBundleType == null)
                {
                    assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine");
                }
                if (assetBundleType == null)
                {
                    Debug.LogWarning("[BirthdayCake] 无法找到 AssetBundle 类型");
                    return;
                }
                
                MethodInfo loadFromFile = assetBundleType.GetMethod("LoadFromFile", new Type[] { typeof(string) });
                if (loadFromFile == null)
                {
                    Debug.LogWarning("[BirthdayCake] 未找到 LoadFromFile 方法");
                    return;
                }
                
                object bundle = loadFromFile.Invoke(null, new object[] { bundlePath });
                if (bundle == null)
                {
                    Debug.LogError("[BirthdayCake] 加载 AssetBundle 失败: " + bundlePath);
                    return;
                }
                
                MethodInfo loadAllAssets = assetBundleType.GetMethod("LoadAllAssets", new Type[] { typeof(Type) });
                if (loadAllAssets == null)
                {
                    Debug.LogWarning("[BirthdayCake] 未找到 LoadAllAssets 方法");
                    return;
                }
                
                UnityEngine.Object[] assets = loadAllAssets.Invoke(bundle, new object[] { typeof(UnityEngine.Object) }) as UnityEngine.Object[];
                if (assets == null || assets.Length == 0)
                {
                    Debug.LogWarning("[BirthdayCake] AssetBundle 中未找到任何资源");
                    return;
                }
                
                int itemCount = 0;
                foreach (UnityEngine.Object obj in assets)
                {
                    GameObject go = obj as GameObject;
                    if (go == null) continue;
                    
                    Item itemPrefab = go.GetComponent<Item>();
                    if (itemPrefab == null) continue;
                    
                    // 配置生日蛋糕物品
                    ConfigureBirthdayCakeItem(itemPrefab);
                    
                    // 注册到物品系统
                    ItemAssetsCollection.AddDynamicEntry(itemPrefab);
                    itemCount++;
                    
                    if (birthdayCakeTypeId <= 0 && itemPrefab.TypeID > 0)
                    {
                        birthdayCakeTypeId = itemPrefab.TypeID;
                    }
                    
                    DevLog("[BirthdayCake] 成功加载生日蛋糕物品: TypeID=" + itemPrefab.TypeID);
                }
                
                DevLog("[BirthdayCake] 初始化完成，加载了 " + itemCount + " 个物品");
            }
            catch (Exception e)
            {
                Debug.LogError("[BirthdayCake] 初始化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 配置生日蛋糕物品（添加食物功能、Buff、Tag）
        /// </summary>
        private void ConfigureBirthdayCakeItem(Item itemPrefab)
        {
            if (itemPrefab == null) return;
            
            try
            {
                // 1. 添加 UsageUtilities 组件
                UsageUtilities usageUtils = itemPrefab.GetComponent<UsageUtilities>();
                if (usageUtils == null)
                {
                    usageUtils = itemPrefab.gameObject.AddComponent<UsageUtilities>();
                }
                
                // 确保 behaviors 列表存在
                if (usageUtils.behaviors == null)
                {
                    usageUtils.behaviors = new List<UsageBehavior>();
                }
                
                // 1.5 配置音效（使用食物音效）
                usageUtils.hasSound = true;
                usageUtils.actionSound = "SFX/Item/pickup_food";  // 开始吃的音效
                usageUtils.useSound = "SFX/Item/put_food";        // 吃完的音效
                DevLog("[BirthdayCake] 已配置音效: actionSound=" + usageUtils.actionSound + ", useSound=" + usageUtils.useSound);
                
                // 2. 添加 FoodDrink 行为（恢复100%饱食度）
                FoodDrink foodBehavior = itemPrefab.gameObject.AddComponent<FoodDrink>();
                foodBehavior.energyValue = ENERGY_RESTORE_VALUE;
                foodBehavior.waterValue = 0f;  // 不恢复水分
                foodBehavior.UseDurability = 0f;  // 一次性消耗品
                usageUtils.behaviors.Add(foodBehavior);
                DevLog("[BirthdayCake] 已添加食物功能: 饱食度+" + ENERGY_RESTORE_VALUE);
                
                // 3. 添加 AddBuff 行为（高兴 Buff）
                Buff happyBuff = FindHappyBuff();
                if (happyBuff != null)
                {
                    AddBuff buffBehavior = itemPrefab.gameObject.AddComponent<AddBuff>();
                    
                    // 设置 buffPrefab 字段
                    var buffPrefabField = typeof(AddBuff).GetField("buffPrefab", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (buffPrefabField != null)
                    {
                        buffPrefabField.SetValue(buffBehavior, happyBuff);
                    }
                    
                    // 设置 chance 为 100%
                    var chanceField = typeof(AddBuff).GetField("chance", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (chanceField != null)
                    {
                        chanceField.SetValue(buffBehavior, 1f);
                    }
                    
                    usageUtils.behaviors.Add(buffBehavior);
                    DevLog("[BirthdayCake] 已添加高兴Buff: " + happyBuff.DisplayName + " " + happyBuff.TotalLifeTime + "s");
                }
                else
                {
                    DevLog("[BirthdayCake] 警告：未找到高兴Buff，跳过Buff添加");
                }
                
                // 4. 关联 UsageUtilities 到 Item
                var usageField = typeof(Item).GetField("usageUtilities", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (usageField != null)
                {
                    usageField.SetValue(itemPrefab, usageUtils);
                }
                
                // 5. 添加 Tag（食物）
                AddTagsToItem(itemPrefab, new string[] { "Food", "食物" });
                
                DevLog("[BirthdayCake] 物品配置完成");
            }
            catch (Exception e)
            {
                Debug.LogError("[BirthdayCake] 配置物品失败: " + e.Message);
            }
        }

        
        /// <summary>
        /// 查找"高兴"Buff（从游戏的 allBuffs 列表中查找）
        /// </summary>
        private Buff FindHappyBuff()
        {
            if (happyBuffPrefab != null)
            {
                return happyBuffPrefab;
            }
            
            try
            {
                // 尝试从 GameplayDataSettings.Buffs 获取 allBuffs 列表
                var buffsData = GameplayDataSettings.Buffs;
                if (buffsData == null)
                {
                    DevLog("[BirthdayCake] GameplayDataSettings.Buffs 为空");
                    return null;
                }
                
                // 通过反射获取 allBuffs 字段
                var allBuffsField = buffsData.GetType().GetField("allBuffs", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (allBuffsField == null)
                {
                    DevLog("[BirthdayCake] 未找到 allBuffs 字段");
                    return null;
                }
                
                var allBuffs = allBuffsField.GetValue(buffsData) as List<Buff>;
                if (allBuffs == null || allBuffs.Count == 0)
                {
                    DevLog("[BirthdayCake] allBuffs 列表为空");
                    return null;
                }
                
                // 搜索"高兴"相关的 Buff（尝试多种可能的名称）
                string[] happyBuffNames = new string[] 
                { 
                    "高兴", "Happy", "happy", "Happiness", "Joy", "joy",
                    "开心", "愉快", "满足", "Satisfied", "Well", "Good"
                };
                
                foreach (Buff buff in allBuffs)
                {
                    if (buff == null) continue;
                    
                    string buffName = buff.name ?? "";
                    string displayName = buff.DisplayName ?? "";
                    string displayNameKey = buff.DisplayNameKey ?? "";
                    
                    // 检查是否匹配任何高兴相关的名称
                    foreach (string name in happyBuffNames)
                    {
                        if (buffName.Contains(name) || 
                            displayName.Contains(name) || 
                            displayNameKey.Contains(name))
                        {
                            DevLog("[BirthdayCake] 找到高兴Buff: " + buffName + " (DisplayName: " + displayName + ")");
                            happyBuffPrefab = buff;
                            return buff;
                        }
                    }
                }
                
                // 如果没找到，打印所有可用的 Buff 供调试
                DevLog("[BirthdayCake] 未找到高兴Buff，可用的Buff列表：");
                foreach (Buff buff in allBuffs)
                {
                    if (buff != null)
                    {
                        DevLog("  - " + buff.name + " (ID:" + buff.ID + ", DisplayName:" + buff.DisplayName + ")");
                    }
                }
                
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError("[BirthdayCake] 查找高兴Buff失败: " + e.Message);
                return null;
            }
        }
        
        /// <summary>
        /// 为物品添加 Tag
        /// </summary>
        private void AddTagsToItem(Item itemPrefab, string[] tagNames)
        {
            try
            {
                var allTags = GameplayDataSettings.Tags.AllTags;
                if (allTags == null)
                {
                    DevLog("[BirthdayCake] AllTags 为空");
                    return;
                }
                
                // 获取 Item 的 tags 字段
                var tagsField = typeof(Item).GetField("tags", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (tagsField == null)
                {
                    DevLog("[BirthdayCake] 未找到 tags 字段");
                    return;
                }
                
                var tagCollection = tagsField.GetValue(itemPrefab);
                if (tagCollection == null)
                {
                    DevLog("[BirthdayCake] tagCollection 为空");
                    return;
                }
                
                // 获取 TagCollection 的 list 字段
                var listField = tagCollection.GetType().GetField("list", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (listField == null)
                {
                    DevLog("[BirthdayCake] 未找到 list 字段");
                    return;
                }
                
                var tagList = listField.GetValue(tagCollection) as List<Tag>;
                if (tagList == null)
                {
                    tagList = new List<Tag>();
                    listField.SetValue(tagCollection, tagList);
                }
                
                // 添加匹配的 Tag
                foreach (string tagName in tagNames)
                {
                    foreach (Tag tag in allTags)
                    {
                        if (tag != null && (tag.name == tagName || tag.name.Contains(tagName)))
                        {
                            if (!tagList.Contains(tag))
                            {
                                tagList.Add(tag);
                                DevLog("[BirthdayCake] 已添加 Tag: " + tag.name);
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BirthdayCake] 添加 Tag 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入生日蛋糕本地化
        /// </summary>
        private void InjectBirthdayCakeLocalization()
        {
            try
            {
                // 获取当前语言的本地化文本
                string displayName = BIRTHDAY_CAKE_DISPLAY_NAME;
                string description = BIRTHDAY_CAKE_DESCRIPTION;
                
                var types = new string[]
                {
                    "SodaCraft.Localizations.LocalizationManager, SodaLocalization",
                    "SodaCraft.Localizations.LocalizationManager, TeamSoda.Duckov.Core",
                    "SodaCraft.Localizations.LocalizationManager, Assembly-CSharp",
                    "LocalizationManager, Assembly-CSharp"
                };
                
                Type locType = null;
                foreach (var t in types)
                {
                    locType = Type.GetType(t);
                    if (locType != null) break;
                }
                
                if (locType != null)
                {
                    var fields = locType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    foreach (var field in fields)
                    {
                        try
                        {
                            var val = field.GetValue(null);
                            if (val == null) continue;
                            
                            Dictionary<string, string> dict = val as Dictionary<string, string>;
                            if (dict != null)
                            {
                                // 注入生日蛋糕名称（中英文键都注入）
                                InjectLocalizedKey(dict, BIRTHDAY_CAKE_DISPLAY_NAME_CN, displayName);
                                InjectLocalizedKey(dict, BIRTHDAY_CAKE_DISPLAY_NAME_EN, displayName);
                                InjectLocalizedKey(dict, "BossRush_BirthdayCake", displayName);
                                
                                // 注入生日蛋糕描述（中英文键都注入）
                                InjectLocalizedKey(dict, BIRTHDAY_CAKE_DISPLAY_NAME_CN + "_Desc", description);
                                InjectLocalizedKey(dict, BIRTHDAY_CAKE_DISPLAY_NAME_EN + "_Desc", description);
                                InjectLocalizedKey(dict, "BossRush_BirthdayCake_Desc", description);
                                
                                // 注入物品 ID 键（这是游戏系统查找物品名称的标准方式）
                                if (birthdayCakeTypeId > 0)
                                {
                                    string itemKey = "Item_" + birthdayCakeTypeId;
                                    string itemDescKey = itemKey + "_Desc";
                                    
                                    InjectLocalizedKey(dict, itemKey, displayName);
                                    InjectLocalizedKey(dict, itemDescKey, description);
                                }
                                
                                DevLog("[BirthdayCake] 本地化注入成功: " + displayName);
                                return;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BirthdayCake] 本地化注入失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 将生日蛋糕注入商店
        /// </summary>
        private void InjectBirthdayCakeIntoShops()
        {
            if (birthdayCakeTypeId <= 0)
            {
                DevLog("[BirthdayCake] TypeID 未初始化，跳过商店注入");
                return;
            }
            
            try
            {
                StockShop[] shops = UnityEngine.Object.FindObjectsOfType<StockShop>();
                if (shops == null || shops.Length == 0)
                {
                    DevLog("[BirthdayCake] 未找到任何商店");
                    return;
                }
                
                int addedCount = 0;
                foreach (StockShop shop in shops)
                {
                    if (shop == null) continue;
                    
                    // 只注入到基地的普通商店
                    string sceneName = "";
                    string merchantId = "";
                    try { sceneName = shop.gameObject.scene.name; } catch { }
                    try { merchantId = shop.MerchantID; } catch { }
                    
                    bool isNpcShop = false;
                    try { isNpcShop = shop.GetComponentInParent<CharacterMainControl>() != null; } catch { }
                    
                    if (!isNpcShop && merchantId == "Merchant_Normal" && sceneName == "Base_SceneV2")
                    {
                        if (shop.entries != null)
                        {
                            bool alreadyExists = false;
                            foreach (StockShop.Entry entry in shop.entries)
                            {
                                if (entry != null && entry.ItemTypeID == birthdayCakeTypeId)
                                {
                                    alreadyExists = true;
                                    break;
                                }
                            }
                            
                            if (!alreadyExists)
                            {
                                StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                                itemEntry.typeID = birthdayCakeTypeId;
                                itemEntry.maxStock = 5;
                                itemEntry.forceUnlock = true;
                                itemEntry.priceFactor = 1f;
                                itemEntry.possibility = 1f;
                                itemEntry.lockInDemo = false;
                                
                                StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                                wrapped.CurrentStock = 5;
                                wrapped.Show = true;
                                
                                shop.entries.Add(wrapped);
                                addedCount++;
                            }
                        }
                    }
                }
                
                DevLog("[BirthdayCake] 商店注入完成，添加到 " + addedCount + " 个商店");
            }
            catch (Exception e)
            {
                Debug.LogError("[BirthdayCake] 商店注入失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 12月份自动赠送生日蛋糕
        // ============================================================================
        
        /// <summary>
        /// 存档键：是否已赠送过生日蛋糕
        /// </summary>
        private const string BIRTHDAY_CAKE_GIVEN_KEY = "BossRush_BirthdayCakeGiven_2024";
        
        /// <summary>
        /// 延迟赠送生日蛋糕（等待场景完全加载）
        /// </summary>
        private System.Collections.IEnumerator DelayedBirthdayCakeGift()
        {
            yield return new WaitForSeconds(2f);  // 等待2秒确保玩家角色和背包已加载
            CheckAndGiveDecemberBirthdayCake();
        }
        
        /// <summary>
        /// 检查并赠送12月份生日蛋糕（只赠送一次）
        /// </summary>
        private void CheckAndGiveDecemberBirthdayCake()
        {
            try
            {
                // 检查是否是12月份
                if (DateTime.Now.Month != 12)
                {
                    return;
                }
                
                // 检查是否已经赠送过
                if (Saves.SavesSystem.KeyExisits(BIRTHDAY_CAKE_GIVEN_KEY))
                {
                    bool alreadyGiven = Saves.SavesSystem.Load<bool>(BIRTHDAY_CAKE_GIVEN_KEY);
                    if (alreadyGiven)
                    {
                        DevLog("[BirthdayCake] 今年12月已赠送过生日蛋糕，跳过");
                        return;
                    }
                }
                
                // 检查物品是否已初始化
                if (birthdayCakeTypeId <= 0)
                {
                    DevLog("[BirthdayCake] 物品未初始化，无法赠送");
                    return;
                }
                
                // 获取玩家角色
                CharacterMainControl player = null;
                try
                {
                    player = CharacterMainControl.Main;
                }
                catch { }
                
                if (player == null)
                {
                    try
                    {
                        player = UnityEngine.Object.FindObjectOfType<CharacterMainControl>();
                    }
                    catch { }
                }
                
                if (player == null)
                {
                    DevLog("[BirthdayCake] 未找到玩家角色，无法赠送");
                    return;
                }
                
                // 获取玩家背包
                Item characterItem = player.CharacterItem;
                if (characterItem == null)
                {
                    DevLog("[BirthdayCake] 未找到玩家 CharacterItem，无法赠送");
                    return;
                }
                
                Inventory inventory = characterItem.Inventory;
                if (inventory == null)
                {
                    DevLog("[BirthdayCake] 未找到玩家背包，无法赠送");
                    return;
                }
                
                // 创建生日蛋糕物品
                Item cakeItem = ItemAssetsCollection.InstantiateSync(birthdayCakeTypeId);
                if (cakeItem == null)
                {
                    DevLog("[BirthdayCake] 创建物品失败");
                    return;
                }
                
                // 尝试添加到背包
                bool added = inventory.AddAndMerge(cakeItem, 0);
                if (added)
                {
                    // 标记已赠送
                    Saves.SavesSystem.Save<bool>(BIRTHDAY_CAKE_GIVEN_KEY, true);
                    DevLog("[BirthdayCake] 12月份生日蛋糕已赠送！");
                    
                    // 显示大横幅祝福语
                    ShowBirthdayBanner();
                }
                else
                {
                    // 背包满了，销毁物品
                    UnityEngine.Object.Destroy(cakeItem.gameObject);
                    DevLog("[BirthdayCake] 背包已满，无法赠送生日蛋糕");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BirthdayCake] 赠送生日蛋糕失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示生日祝福大横幅
        /// </summary>
        private void ShowBirthdayBanner()
        {
            try
            {
                // 使用 NotificationText 显示祝福语（会在屏幕中央显示）
                Duckov.UI.NotificationText.Push(BIRTHDAY_CAKE_DESCRIPTION);
                
                DevLog("[BirthdayCake] 已显示祝福横幅");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BirthdayCake] 显示横幅失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 调试功能：给予玩家一个生日蛋糕并显示横幅（F11 调用）
        /// </summary>
        public void DebugGiveBirthdayCake()
        {
            try
            {
                // 检查物品是否已初始化
                if (birthdayCakeTypeId <= 0)
                {
                    DevLog("[BirthdayCake] F11: 物品未初始化，无法给予");
                    return;
                }
                
                // 获取玩家角色
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    player = UnityEngine.Object.FindObjectOfType<CharacterMainControl>();
                }
                
                if (player == null)
                {
                    DevLog("[BirthdayCake] F11: 未找到玩家角色");
                    return;
                }
                
                // 获取玩家背包
                Item characterItem = player.CharacterItem;
                if (characterItem == null)
                {
                    DevLog("[BirthdayCake] F11: 未找到玩家 CharacterItem");
                    return;
                }
                
                Inventory inventory = characterItem.Inventory;
                if (inventory == null)
                {
                    DevLog("[BirthdayCake] F11: 未找到玩家背包");
                    return;
                }
                
                // 创建生日蛋糕物品
                Item cakeItem = ItemAssetsCollection.InstantiateSync(birthdayCakeTypeId);
                if (cakeItem == null)
                {
                    DevLog("[BirthdayCake] F11: 创建物品失败");
                    return;
                }
                
                // 尝试添加到背包
                bool added = inventory.AddAndMerge(cakeItem, 0);
                if (added)
                {
                    DevLog("[BirthdayCake] F11: 已给予生日蛋糕！");
                    
                    // 显示大横幅祝福语
                    ShowBirthdayBanner();
                }
                else
                {
                    // 背包满了，销毁物品
                    UnityEngine.Object.Destroy(cakeItem.gameObject);
                    DevLog("[BirthdayCake] F11: 背包已满，无法给予");
                    Duckov.UI.NotificationText.Push("背包已满，无法给予生日蛋糕");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BirthdayCake] F11 给予失败: " + e.Message);
            }
        }
    }
}
