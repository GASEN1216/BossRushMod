// ============================================================================
// Integration.cs - 游戏系统集成
// ============================================================================
// 模块说明：
//   管理 BossRush 模组与游戏系统的集成，包括：
//   - 动态物品初始化（BossRush 船票）
//   - 本地化注入（中文显示名称和描述）
//   - 商店注入（将船票添加到商店）
//   - 场景加载事件处理
//   
// 主要功能：
//   - InitializeDynamicItems: 从 AssetBundle 加载船票物品
//   - InjectBossRushTicketLocalization: 注入船票的本地化文本
//   - InjectBossRushTicketIntoShops: 将船票添加到游戏商店
//   - OnSceneLoaded: 场景加载后的初始化处理
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 游戏系统集成模块
    /// </summary>
    public partial class ModBehaviour
    {
        // 物品 ID 105 购买计数器（用于检测进货行为）
        private int item105PurchaseCount = 0;
        /// <summary>
        /// 初始化动态物品（从 AssetBundle 加载 BossRush 船票）
        /// </summary>
        private void InitializeDynamicItems_Integration()
        {
            if (dynamicItemsInitialized)
            {
                return;
            }
            dynamicItemsInitialized = true;

            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "bossrush_ticket");

                if (!File.Exists(bundlePath))
                {
                    DevLog("[BossRush] 未找到 bossrush_ticket AssetBundle: " + bundlePath);
                    return;
                }

                // 通过反射加载 AssetBundle，避免编译期依赖 UnityEngine.AssetBundleModule
                Type assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule");
                if (assetBundleType == null)
                {
                    assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine");
                }
                if (assetBundleType == null)
                {
                    Debug.LogWarning("[BossRush] 无法找到 UnityEngine.AssetBundle 类型，跳过动态物品加载");
                    return;
                }

                MethodInfo loadFromFile = assetBundleType.GetMethod("LoadFromFile", new Type[] { typeof(string) });
                if (loadFromFile == null)
                {
                    Debug.LogWarning("[BossRush] 未找到 AssetBundle.LoadFromFile 方法，跳过动态物品加载");
                    return;
                }

                object bundle = loadFromFile.Invoke(null, new object[] { bundlePath });
                if (bundle == null)
                {
                    Debug.LogError("[BossRush] 反射调用 LoadFromFile 失败: " + bundlePath);
                    return;
                }

                MethodInfo loadAllAssets = assetBundleType.GetMethod("LoadAllAssets", new Type[] { typeof(Type) });
                if (loadAllAssets == null)
                {
                    Debug.LogWarning("[BossRush] 未找到 AssetBundle.LoadAllAssets(Type) 方法，跳过动态物品加载");
                    return;
                }

                // 加载所有资源，然后从 GameObject 中查找挂载的 Item 组件
                UnityEngine.Object[] assets = loadAllAssets.Invoke(bundle, new object[] { typeof(UnityEngine.Object) }) as UnityEngine.Object[];
                if (assets == null || assets.Length == 0)
                {
                    Debug.LogWarning("[BossRush] bossrush_ticket AssetBundle 中未找到任何资源");
                    return;
                }

                int itemCount = 0;
                foreach (UnityEngine.Object obj in assets)
                {
                    GameObject go = obj as GameObject;
                    if (go == null)
                    {
                        continue;
                    }

                    Item itemPrefab = go.GetComponent<Item>();
                    if (itemPrefab == null)
                    {
                        continue;
                    }

                    ItemAssetsCollection.AddDynamicEntry(itemPrefab);
                    itemCount++;

                    if (bossRushTicketTypeId <= 0 && itemPrefab.TypeID > 0)
                    {
                        bossRushTicketTypeId = itemPrefab.TypeID;
                    }
                }

                DevLog("[BossRush] 动态物品初始化完成：从 bossrush_ticket 加载 " + itemCount + " 个 Item，BossRushTicketTypeId=" + bossRushTicketTypeId);
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] InitializeDynamicItems 出错: " + e.Message);
            }
        }

        private static void InjectBossRushTicketLocalization_Integration()
        {
            try
            {
                string displayName = "Boss Rush船票";
                string descriptionText = "开启Boss Rush的凭证，九死一生，一旦倒在那，掉落的东西会被立马收走，一件不剩。但是裸体进入可白手起家！";

                // 通过反射查找 LocalizationManager 类型
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

                            bool injected = false;

                            Dictionary<string, string> dict = val as Dictionary<string, string>;
                            if (dict != null)
                            {
                                // 注入 Boss Rush船票 的名字和描述
                                if (!dict.ContainsKey(displayName))
                                {
                                    dict.Add(displayName, displayName);
                                }

                                if (!dict.ContainsKey("Boss Rush船票_Desc"))
                                {
                                    dict.Add("Boss Rush船票_Desc", descriptionText);
                                }

                                if (bossRushTicketTypeId > 0)
                                {
                                    string itemKey = "Item_" + bossRushTicketTypeId;
                                    string itemDescKey = itemKey + "_Desc";

                                    if (!dict.ContainsKey(itemKey))
                                    {
                                        dict.Add(itemKey, displayName);
                                    }

                                    if (!dict.ContainsKey(itemDescKey))
                                    {
                                        dict.Add(itemDescKey, descriptionText);
                                    }
                                }

                                injected = true;
                            }

                            if (!injected)
                            {
                                var dictObj = val as System.Collections.IDictionary;
                                if (dictObj != null)
                                {
                                    if (!dictObj.Contains(displayName))
                                    {
                                        dictObj.Add(displayName, displayName);
                                    }

                                    if (!dictObj.Contains("Boss Rush船票_Desc"))
                                    {
                                        dictObj.Add("Boss Rush船票_Desc", descriptionText);
                                    }

                                    if (bossRushTicketTypeId > 0)
                                    {
                                        string itemKey = "Item_" + bossRushTicketTypeId;
                                        string itemDescKey = itemKey + "_Desc";

                                        if (!dictObj.Contains(itemKey))
                                        {
                                            dictObj.Add(itemKey, displayName);
                                        }

                                        if (!dictObj.Contains(itemDescKey))
                                        {
                                            dictObj.Add(itemDescKey, descriptionText);
                                        }
                                    }

                                    injected = true;
                                }
                            }

                            if (injected)
                            {
                                DevLog("[BossRush] 成功注入 BossRush 船票本地化键值到字段: " + field.Name);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] 注入 BossRush 船票本地化失败: " + e.Message);
            }
        }

        private void InjectBossRushTicketIntoShops_Integration()
        {
            if (bossRushTicketTypeId <= 0)
            {
                Debug.LogWarning("[BossRush] BossRush 船票 TypeID 未初始化，跳过商店注入");
                return;
            }

            try
            {
                StockShop[] shops = UnityEngine.Object.FindObjectsOfType<StockShop>();
                if (shops == null || shops.Length == 0)
                {
                    DevLog("[BossRush] 未找到任何 StockShop，跳过商店扫描");
                    return;
                }

                int totalCount = 0;
                int npcShopCount = 0;
                int nonNpcShopCount = 0;
                int targetShopCount = 0;
                int addedCount = 0;
                foreach (StockShop shop in shops)
                {
                    if (shop == null)
                    {
                        continue;
                    }
                    totalCount++;

                    bool isNpcShop = false;
                    try
                    {
                        if (shop.GetComponentInParent<CharacterMainControl>() != null)
                        {
                            isNpcShop = true;
                        }
                    }
                    catch { }

                    if (isNpcShop)
                    {
                        npcShopCount++;
                    }
                    else
                    {
                        nonNpcShopCount++;
                    }

                    string sceneName = "";
                    string merchantId = "";
                    string goName = "";
                    string displayName = "";

                    try
                    {
                        sceneName = shop.gameObject != null ? shop.gameObject.scene.name : "<no-go>";
                    }
                    catch { }

                    try
                    {
                        goName = shop.gameObject != null ? shop.gameObject.name : "<no-go>";
                    }
                    catch { }

                    try
                    {
                        merchantId = shop.MerchantID;
                    }
                    catch { }

                    try
                    {
                        displayName = shop.DisplayName;
                    }
                    catch { }

                    bool isTargetShop = (!isNpcShop && merchantId == "Merchant_Normal" && sceneName == "Base_SceneV2");

                    if (isTargetShop)
                    {
                        targetShopCount++;

                        if (shop.entries != null)
                        {
                            bool alreadyExists = false;
                            foreach (StockShop.Entry entry in shop.entries)
                            {
                                if (entry != null && entry.ItemTypeID == bossRushTicketTypeId)
                                {
                                    alreadyExists = true;
                                    break;
                                }
                            }

                            if (!alreadyExists)
                            {
                                StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                                itemEntry.typeID = bossRushTicketTypeId;
                                itemEntry.maxStock = 10;
                                itemEntry.forceUnlock = true;
                                itemEntry.priceFactor = 1f;
                                itemEntry.possibility = 1f;
                                itemEntry.lockInDemo = false;

                                StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                                shop.entries.Add(wrapped);
                                addedCount++;
                            }
                        }
                    }

                    DevLog("[BossRush] ShopScan: scene=" + sceneName + ", isNpcShop=" + isNpcShop + ", merchantID=" + merchantId + ", goName=" + goName + ", displayName=" + displayName + ", isTargetShop=" + isTargetShop);
                }

                DevLog("[BossRush] ShopScan summary: total=" + totalCount + ", npcShops=" + npcShopCount + ", nonNpcShops=" + nonNpcShopCount + ", targetShops=" + targetShopCount + ", added=" + addedCount + ", TypeID=" + bossRushTicketTypeId);
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] InjectBossRushTicketIntoShops 出错: " + e.Message);
            }
        }

        private void InjectLocalization_Integration()
        {
            try
            {
                // 尝试查找 LocalizationManager 类型
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
                    // 查找所有静态字段，寻找可能是字典的字段
                    // 扩大搜索范围，包括私有静态字段
                    var fields = locType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    foreach (var field in fields)
                    {
                        try
                        {
                            var val = field.GetValue(null);
                            if (val == null) continue;

                            bool injected = false;

                            Dictionary<string, string> dict = val as Dictionary<string, string>;
                            if (dict != null)
                            {
                                // 注入新的本地化键：开始第一波
                                if (dict.ContainsKey("开始第一波"))
                                {
                                    dict["开始第一波"] = "开始第一波";
                                }
                                else
                                {
                                    dict.Add("开始第一波", "开始第一波");
                                }

                                // 注入新的本地化键：BossRush 入口
                                if (dict.ContainsKey("BossRush"))
                                {
                                    dict["BossRush"] = "Boss Rush";
                                }
                                else
                                {
                                    dict.Add("BossRush", "Boss Rush");
                                }

                                // 注入新的本地化键：难度选项 "弹指可灭" / "有点意思" / "无间炼狱"
                                if (dict.ContainsKey("弹指可灭"))
                                {
                                    dict["弹指可灭"] = "弹指可灭";
                                }
                                else
                                {
                                    dict.Add("弹指可灭", "弹指可灭");
                                }

                                if (dict.ContainsKey("有点意思"))
                                {
                                    dict["有点意思"] = "有点意思";
                                }
                                else
                                {
                                    dict.Add("有点意思", "有点意思");
                                }

                                if (dict.ContainsKey("无间炼狱"))
                                {
                                    dict["无间炼狱"] = "无间炼狱";
                                }
                                else
                                {
                                    dict.Add("无间炼狱", "无间炼狱");
                                }

                                // 注入新的本地化键：测试
                                if (dict.ContainsKey("测试"))
                                {
                                    dict["测试"] = "测试";
                                }
                                else
                                {
                                    dict.Add("测试", "测试");
                                }

                                injected = true;
                            }

                            if (!injected)
                            {
                                var dictObj = val as System.Collections.IDictionary;
                                if (dictObj != null)
                                {
                                    if (!dictObj.Contains("开始第一波"))
                                    {
                                        dictObj.Add("开始第一波", "开始第一波");
                                    }

                                    if (!dictObj.Contains("BossRush"))
                                    {
                                        dictObj.Add("BossRush", "Boss Rush");
                                    }

                                    if (!dictObj.Contains("弹指可灭"))
                                    {
                                        dictObj.Add("弹指可灭", "弹指可灭");
                                    }

                                    if (!dictObj.Contains("有点意思"))
                                    {
                                        dictObj.Add("有点意思", "有点意思");
                                    }

                                    if (!dictObj.Contains("无间炼狱"))
                                    {
                                        dictObj.Add("无间炼狱", "无间炼狱");
                                    }

                                    if (!dictObj.Contains("测试"))
                                    {
                                        dictObj.Add("测试", "测试");
                                    }

                                    injected = true;
                                }
                            }

                            if (injected)
                            {
                                DevLog("[BossRush] 成功注入 BossRush 本地化键值到字段: " + field.Name);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] 注入 BossRush 本地化失败: " + e.Message);
            }
        }

        private void InjectLocalization_Extra_Integration()
        {
            try
            {
                // 尝试查找 LocalizationManager 类型
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
                    // 查找所有静态字段，寻找可能是字典的字段
                    // 扩大搜索范围，包括私有静态字段
                    var fields = locType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    foreach (var field in fields)
                    {
                        try
                        {
                            var val = field.GetValue(null);
                            if (val == null) continue;

                            bool injected = false;

                            Dictionary<string, string> dict = val as Dictionary<string, string>;
                            if (dict != null)
                            {
                                // 注入新的本地化键：开始第一波
                                if (dict.ContainsKey("开始第一波"))
                                {
                                    dict["开始第一波"] = "开始第一波";
                                }
                                else
                                {
                                    dict.Add("开始第一波", "开始第一波");
                                }

                                // 注入新的本地化键：BossRush 入口
                                if (dict.ContainsKey("BossRush"))
                                {
                                    dict["BossRush"] = "Boss Rush";
                                }
                                else
                                {
                                    dict.Add("BossRush", "Boss Rush");
                                }

                                // 注入新的本地化键：难度选项 "弹指可灭" / "有点意思" / "无间炼狱"
                                if (dict.ContainsKey("弹指可灭"))
                                {
                                    dict["弹指可灭"] = "弹指可灭";
                                }
                                else
                                {
                                    dict.Add("弹指可灭", "弹指可灭");
                                }

                                if (dict.ContainsKey("有点意思"))
                                {
                                    dict["有点意思"] = "有点意思";
                                }
                                else
                                {
                                    dict.Add("有点意思", "有点意思");
                                }

                                if (dict.ContainsKey("无间炼狱"))
                                {
                                    dict["无间炼狱"] = "无间炼狱";
                                }
                                else
                                {
                                    dict.Add("无间炼狱", "无间炼狱");
                                }

                                // 注入新的本地化键：测试
                                if (dict.ContainsKey("测试"))
                                {
                                    dict["测试"] = "测试";
                                }
                                else
                                {
                                    dict.Add("测试", "测试");
                                }

                                // 注入新的本地化键：加油！（使用富文本金色）
                                if (dict.ContainsKey("加油！"))
                                {
                                    dict["加油！"] = "<color=#FFD700>加油！</color>";
                                }
                                else
                                {
                                    dict.Add("加油！", "<color=#FFD700>加油！</color>");
                                }

                                // 注入新的本地化键：路牌加油状态（BossRush_Sign_Cheer）
                                if (dict.ContainsKey("BossRush_Sign_Cheer"))
                                {
                                    dict["BossRush_Sign_Cheer"] = "<color=#FFD700>加油！！！</color>";
                                }
                                else
                                {
                                    dict.Add("BossRush_Sign_Cheer", "<color=#FFD700>加油！！！</color>");
                                }

                                // 注入新的本地化键：哎哟~你干嘛~（使用富文本金色）
                                if (dict.ContainsKey("哎哟~你干嘛~"))
                                {
                                    dict["哎哟~你干嘛~"] = "<color=#FFD700>哎哟~你干嘛~</color>";
                                }
                                else
                                {
                                    dict.Add("哎哟~你干嘛~", "<color=#FFD700>哎哟~你干嘛~</color>");
                                }

                                // 注入新的本地化键：冲！（下一波）（使用富文本金色）
                                if (dict.ContainsKey("冲！（下一波）"))
                                {
                                    dict["冲！（下一波）"] = "<color=#FFD700>冲！（下一波）</color>";
                                }
                                else
                                {
                                    dict.Add("冲！（下一波）", "<color=#FFD700>冲！（下一波）</color>");
                                }

                                // 注入新的本地化键：路牌最终胜利状态（BossRush_Sign_Victory）
                                if (dict.ContainsKey("BossRush_Sign_Victory"))
                                {
                                    dict["BossRush_Sign_Victory"] = "<color=#FFD700>君王凯旋归来，拿取属于王的荣耀！</color>";
                                }
                                else
                                {
                                    dict.Add("BossRush_Sign_Victory", "<color=#FFD700>君王凯旋归来，拿取属于王的荣耀！</color>");
                                }

                                // 注入 Boss 奖励箱搬起/放下本地化键
                                if (dict.ContainsKey("BossRush_Carry_Up"))
                                {
                                    dict["BossRush_Carry_Up"] = "搬起";
                                }
                                else
                                {
                                    dict.Add("BossRush_Carry_Up", "搬起");
                                }

                                if (dict.ContainsKey("BossRush_Carry_Down"))
                                {
                                    dict["BossRush_Carry_Down"] = "放下";
                                }
                                else
                                {
                                    dict.Add("BossRush_Carry_Down", "放下");
                                }

                                // 注入初始传送气泡的本地化键
                                if (dict.ContainsKey("BossRush_InitEntry"))
                                {
                                    dict["BossRush_InitEntry"] = "<color=#FFD700>哎哟~你干嘛~</color>";
                                }
                                else
                                {
                                    dict.Add("BossRush_InitEntry", "<color=#FFD700>哎哟~你干嘛~</color>");
                                }

                                // 注入救援传送气泡的本地化键
                                if (dict.ContainsKey("传送"))
                                {
                                    dict["传送"] = "<color=#00BFFF>传送</color>";
                                }
                                else
                                {
                                    dict.Add("传送", "<color=#00BFFF>传送</color>");
                                }

                                // 注入清理箱子选项本地化键
                                if (dict.ContainsKey("BossRush_ClearAllLootboxes"))
                                {
                                    dict["BossRush_ClearAllLootboxes"] = "清空所有箱子";
                                }
                                else
                                {
                                    dict.Add("BossRush_ClearAllLootboxes", "清空所有箱子");
                                }

                                if (dict.ContainsKey("BossRush_ClearEmptyLootboxes"))
                                {
                                    dict["BossRush_ClearEmptyLootboxes"] = "清空所有空箱子";
                                }
                                else
                                {
                                    dict.Add("BossRush_ClearEmptyLootboxes", "清空所有空箱子");
                                }

                                // Mode D: 冲下一波 选项本地化键
                                if (dict.ContainsKey("BossRush_ModeD_NextWave"))
                                {
                                    dict["BossRush_ModeD_NextWave"] = "冲！下一波";
                                }
                                else
                                {
                                    dict.Add("BossRush_ModeD_NextWave", "冲！下一波");
                                }

                                if (dict.ContainsKey("BossRush_AmmoRefill"))
                                {
                                    dict["BossRush_AmmoRefill"] = "加油站";
                                }
                                else
                                {
                                    dict.Add("BossRush_AmmoRefill", "加油站");
                                }

                                // 注入新的本地化键：维修选项
                                if (dict.ContainsKey("BossRush_Repair"))
                                {
                                    dict["BossRush_Repair"] = "维修";
                                }
                                else
                                {
                                    dict.Add("BossRush_Repair", "维修");
                                }

                                injected = true;
                            }
                            
                            if (!injected)
                            {
                                var dictObj = val as System.Collections.IDictionary;
                                if (dictObj != null)
                                {
                                    // 注入新的本地化键：开始第一波
                                    if (dictObj.Contains("开始第一波"))
                                    {
                                        dictObj["开始第一波"] = "开始第一波";
                                    }
                                    else
                                    {
                                        dictObj.Add("开始第一波", "开始第一波");
                                    }
                                    
                                    // 注入新的本地化键：BossRush 入口
                                    if (dictObj.Contains("BossRush"))
                                    {
                                        dictObj["BossRush"] = "Boss Rush";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush", "Boss Rush");
                                    }

                                    // 注入新的本地化键：难度选项 "弹指可灭" / "有点意思" / "无间炼狱"
                                    if (dictObj.Contains("弹指可灭"))
                                    {
                                        dictObj["弹指可灭"] = "弹指可灭";
                                    }
                                    else
                                    {
                                        dictObj.Add("弹指可灭", "弹指可灭");
                                    }

                                    if (dictObj.Contains("有点意思"))
                                    {
                                        dictObj["有点意思"] = "有点意思";
                                    }
                                    else
                                    {
                                        dictObj.Add("有点意思", "有点意思");
                                    }

                                    if (dictObj.Contains("无间炼狱"))
                                    {
                                        dictObj["无间炼狱"] = "无间炼狱";
                                    }
                                    else
                                    {
                                        dictObj.Add("无间炼狱", "无间炼狱");
                                    }

                                    // 注入 Boss 奖励箱搬起/放下本地化键
                                    if (dictObj.Contains("BossRush_Carry_Up"))
                                    {
                                        dictObj["BossRush_Carry_Up"] = "搬起";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush_Carry_Up", "搬起");
                                    }

                                    if (dictObj.Contains("BossRush_Carry_Down"))
                                    {
                                        dictObj["BossRush_Carry_Down"] = "放下";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush_Carry_Down", "放下");
                                    }

                                    if (dictObj.Contains("BossRush_AmmoRefill"))
                                    {
                                        dictObj["BossRush_AmmoRefill"] = "加油站";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush_AmmoRefill", "加油站");
                                    }

                                    // 注入新的本地化键：维修选项
                                    if (dictObj.Contains("BossRush_Repair"))
                                    {
                                        dictObj["BossRush_Repair"] = "维修";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush_Repair", "维修");
                                    }

                                    // 注入新的本地化键：测试
                                    if (dictObj.Contains("测试"))
                                    {
                                        dictObj["测试"] = "测试";
                                    }
                                    else
                                    {
                                        dictObj.Add("测试", "测试");
                                    }

                                    // 注入新的本地化键：加油！（使用富文本金色）
                                    if (dictObj.Contains("加油！"))
                                    {
                                        dictObj["加油！"] = "<color=#FFD700>加油！</color>";
                                    }
                                    else
                                    {
                                        dictObj.Add("加油！", "<color=#FFD700>加油！</color>");
                                    }

                                    // 注入新的本地化键：哎哟~你干嘛~（使用富文本金色）
                                    if (dictObj.Contains("哎哟~你干嘛~"))
                                    {
                                        dictObj["哎哟~你干嘛~"] = "<color=#FFD700>哎哟~你干嘛~</color>";
                                    }
                                    else
                                    {
                                        dictObj.Add("哎哟~你干嘛~", "<color=#FFD700>哎哟~你干嘛~</color>");
                                    }

                                    // 注入新的本地化键：冲！（下一波）（使用富文本金色）
                                    if (dictObj.Contains("冲！（下一波）"))
                                    {
                                        dictObj["冲！（下一波）"] = "<color=#FFD700>冲！（下一波）</color>";
                                    }
                                    else
                                    {
                                        dictObj.Add("冲！（下一波）", "<color=#FFD700>冲！（下一波）</color>");
                                    }

                                    // 注入初始传送气泡的本地化键
                                    if (dictObj.Contains("BossRush_InitEntry"))
                                    {
                                        dictObj["BossRush_InitEntry"] = "<color=#FFD700>哎哟~你干嘛~</color>";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush_InitEntry", "<color=#FFD700>哎哟~你干嘛~</color>");
                                    }

                                    // 注入救援传送气泡的本地化键
                                    if (dictObj.Contains("传送"))
                                    {
                                        dictObj["传送"] = "<color=#00BFFF>传送</color>";
                                    }
                                    else
                                    {
                                        dictObj.Add("传送", "<color=#00BFFF>传送</color>");
                                    }

                                    // 注入清理箱子选项本地化键
                                    if (dictObj.Contains("BossRush_ClearAllLootboxes"))
                                    {
                                        dictObj["BossRush_ClearAllLootboxes"] = "清空所有箱子";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush_ClearAllLootboxes", "清空所有箱子");
                                    }

                                    if (dictObj.Contains("BossRush_ClearEmptyLootboxes"))
                                    {
                                        dictObj["BossRush_ClearEmptyLootboxes"] = "清空所有空箱子";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush_ClearEmptyLootboxes", "清空所有空箱子");
                                    }

                                    // Mode D: 冲下一波 选项本地化键
                                    if (dictObj.Contains("BossRush_ModeD_NextWave"))
                                    {
                                        dictObj["BossRush_ModeD_NextWave"] = "冲！下一波";
                                    }
                                    else
                                    {
                                        dictObj.Add("BossRush_ModeD_NextWave", "冲！下一波");
                                    }
                                }
                            }
                        }
                        catch {}
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] 本地化注入失败: " + e.Message);
            }
        }

        void Start_Integration()
        {
            LoadConfigFromFile();
            Type modConfigType = FindModConfigType("ModConfig.ModBehaviour");
            if (modConfigType != null)
            {
                SetupModConfig();
                LoadConfigFromModConfig();
                SaveConfigToFile();
            }

            // 尝试注入本地化字典
            InjectLocalization();

            InitializeDynamicItems();
            InjectBossRushTicketLocalization();
            InjectBossRushTicketIntoShops();

            DevLog("[BossRush] ========================================");
            DevLog("[BossRush] Boss Rush Mod v1.0 已加载");
            DevLog("[BossRush] ========================================");
            DevLog("[BossRush] 使用方法:");
            DevLog("[BossRush]   1. 购买'Boss Rush船票'（在商店中）");
            // 延迟初始化，等待游戏系统完全加载
            
            // 注册场景加载事件，在进入新场景时注入交互
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // 注册商店购买事件，用于检测进货行为
            StockShop.OnItemPurchased += OnItemPurchased_Integration;
            
            // 如果当前已经在场景中，立即执行一次
            if (SceneManager.GetActiveScene().name != "MainMenu" && SceneManager.GetActiveScene().name != "LoadingScreen_Black")
            {
                StartCoroutine(FindInteractionTargets(5)); // 立即扫描5次
            }
        }
        
        void OnDestroy_Integration()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            StockShop.OnItemPurchased -= OnItemPurchased_Integration;
            Health.OnDead -= OnPlayerDeathInBossRush;
            Health.OnDead -= OnEnemyDiedWithDamageInfo;
            
            try
            {
                Type modBehaviourType = FindModConfigType("ModConfig.ModBehaviour");
                if (modBehaviourType != null)
                {
                    MethodInfo removeDelegateMethod = modBehaviourType.GetMethod("RemoveOnOptionsChangedDelegate", BindingFlags.Public | BindingFlags.Static);
                    if (removeDelegateMethod != null)
                    {
                        Action<string> handler = new Action<string>(OnModConfigOptionsChanged);
                        removeDelegateMethod.Invoke(null, new object[] { handler });
                        DevLog("[BossRush] 已移除配置变更事件监听");
                    }
                }
            }
            catch
            {
            }
            
            if (arenaStartPoint != null) UnityEngine.Object.Destroy(arenaStartPoint);
            
            DevLog("[BossRush] Boss Rush Mod已卸载");
        }

        /// <summary>
        /// 商店购买事件处理：检测玩家是否大量购买 ID 105 物品（进货行为）
        /// 仅在 BossRush 弹药商店（ammoShop）中生效
        /// </summary>
        private void OnItemPurchased_Integration(StockShop shop, Item item)
        {
            try
            {
                if (shop == null || item == null) return;
                
                // 仅在 BossRush 弹药商店中检测
                if (ammoShop == null || shop != ammoShop) return;
                
                // 检测是否购买了 ID 105 的物品
                if (item.TypeID == 105)
                {
                    item105PurchaseCount++;
                    
                    // 达到 10 个时显示横幅提示
                    if (item105PurchaseCount == 10)
                    {
                        ShowBigBanner("喂喂，你这家伙来这进货了是吗(*´･д･)?");
                    }
                }
            }
            catch {}
        }

        private void OnSceneLoaded_Integration(Scene scene, LoadSceneMode mode)
        {
            DevLog("[BossRush] 场景加载: " + scene.name);

            try
            {
                InjectBossRushTicketIntoShops();

                if (scene.name == BossRushArenaSceneName)
                {
                    // 只有在通过 BossRush 启动的 DEMO 挑战时才执行竞技场逻辑
                    if (bossRushArenaPlanned)
                    {
                        InitializeEnemyPresets();
                        bossRushArenaActive = true;
                        bossRushArenaPlanned = false;

                        // Level_DemoChallenge_1 场景：设置BossRush竞技场
                        demoChallengeStartPosition = Vector3.zero;
                        // 延迟到地图完全加载后再执行传送和创建交互点，避免被游戏自身的出生点逻辑覆盖
                        StartCoroutine(WaitForLevelInitializedThenSetup(scene));
                    }
                }
                else
                {
                    // Bug #1 修复：从竞技场撤离到其他场景时重置状态
                    if (IsActive || bossRushArenaActive)
                    {
                        DevLog("[BossRush] 检测到场景切换（离开竞技场），重置 BossRush 状态");
                        // 优先清空 BossRush 内部的下一波倒计时/提示状态和 NotificationText 队列，防止在基地继续播报 "下一波将在 X 秒后开始"
                        waitingForNextWave = false;
                        waveCountdown = 0f;
                        lastWaveCountdownSeconds = -1;
                        statusMessage = string.Empty;
                        messageTimer = 0f;

                        // 清理 NotificationText.pendingTexts 队列
                        try
                        {
                            System.Type notifType = typeof(NotificationText);
                            System.Reflection.FieldInfo pendingField = notifType.GetField("pendingTexts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                            if (pendingField != null)
                            {
                                System.Collections.Generic.Queue<string> q = pendingField.GetValue(null) as System.Collections.Generic.Queue<string>;
                                if (q != null)
                                {
                                    q.Clear();
                                }
                            }
                        }
                        catch { }

                        // 最后再重置 BossRush 状态标志，避免因为 IsActive 过早被置为 false 导致某些清理逻辑被跳过
                        SetBossRushRuntimeActive(false);
                        bossRushArenaActive = false;
                        bossRushArenaPlanned = false;
                        currentBoss = null;
                        try
                        {
                            if (ammoShop != null)
                            {
                                try
                                {
                                    if (ammoShop.gameObject != null)
                                    {
                                        UnityEngine.Object.Destroy(ammoShop.gameObject);
                                    }
                                }
                                catch {}
                                ammoShop = null;
                            }
                        }
                        catch {}
                        
                        // 取消敌人死亡监听
                        Health.OnDead -= OnEnemyDiedWithDamageInfo;

                        // 如果是 Mode D 模式，结束 Mode D
                        if (modeDActive)
                        {
                            EndModeD();
                        }
                    }
                    
                    // 其他场景：注入传送到竞技场的交互选项
                    StartCoroutine(FindInteractionTargets(10));
                }
            }
            catch { }
        }

        private System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)
        {
            DevLog("[BossRush] WaitForLevelInitializedThenSetup: 开始等待地图完全初始化...");

            // 等待条件：场景已加载、SceneLoader 不在加载中、CharacterMainControl.Main 和 GameCamera.Instance 均已存在
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;
            int attempt = 0;

            while (elapsed < maxWait)
            {
                attempt++;
                bool sceneLoaded = scene.isLoaded;
                bool sceneLoaderDone = true;
                bool mainExists = false;
                bool cameraExists = false;
                bool levelInited = false;

                try
                {
                    sceneLoaderDone = !SceneLoader.IsSceneLoading;
                }
                catch { }

                try
                {
                    mainExists = CharacterMainControl.Main != null;
                }
                catch { }

                try
                {
                    cameraExists = GameCamera.Instance != null;
                }
                catch { }

                try
                {
                    levelInited = LevelManager.LevelInited;
                }
                catch { }

                if (sceneLoaded && sceneLoaderDone && mainExists && cameraExists && levelInited)
                {
                    DevLog("[BossRush] WaitForLevelInitializedThenSetup: 地图初始化完成，第 " + attempt + " 次检查，elapsed=" + elapsed + "s");
                    break;
                }

                if (attempt % 10 == 0)
                {
                    DevLog("[BossRush] WaitForLevelInitializedThenSetup: 第 " + attempt + " 次检查, sceneLoaded=" + sceneLoaded + ", sceneLoaderDone=" + sceneLoaderDone + ", mainExists=" + mainExists + ", cameraExists=" + cameraExists + ", levelInited=" + levelInited + ", elapsed=" + elapsed + "s");
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            DevLog("[BossRush] WaitForLevelInitializedThenSetup: 结束等待, elapsed=" + elapsed + "s");

            // 执行原来的设置逻辑
            StartCoroutine(SetupBossRushInDemoChallenge(scene));
        }
    }
}
