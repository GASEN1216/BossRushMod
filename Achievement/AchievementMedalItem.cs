// ============================================================================
// AchievementMedalItem.cs - 成就勋章商店注入
// ============================================================================
// 模块说明：
//   管理成就勋章物品的商店注入和库存持久化
//   物品加载由 ItemFactory 统一管理，本模块只负责：
//   - 注入到基地售货机（排在最前面）
//   - 库存持久化（存档/读档）
// ============================================================================

using System;
using UnityEngine;
using Duckov.Economy;
using ItemStatsSystem;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 成就勋章商店注入模块
    /// </summary>
    public partial class ModBehaviour
    {
        // 缓存注入的成就勋章条目引用
        private static StockShop.Entry injectedMedalEntry = null;
        
        // 成就勋章库存缓存
        private static int cachedMedalStock = -1;
        
        // ============================================================================
        // 初始化方法（已废弃，配置器在 BossRushIntegration 中统一注册）
        // ============================================================================
        
        /// <summary>
        /// 初始化成就勋章物品（已废弃，保留空方法以兼容旧调用）
        /// 配置器现在在 BossRushIntegration.cs 的 ItemFactory.LoadAllItems() 之前注册
        /// </summary>
        private void InitializeAchievementMedalItem()
        {
            // 配置器已移至 BossRushIntegration.cs 中统一注册
            // 此方法保留为空以兼容可能的旧调用
        }
        
        /// <summary>
        /// 注入成就勋章本地化
        /// </summary>
        private void InjectAchievementMedalLocalization()
        {
            AchievementMedalConfig.InjectLocalization();
        }
        
        /// <summary>
        /// 将成就勋章注入到商店（排在最前面，与冒险家日志相同位置）
        /// </summary>
        private void InjectAchievementMedalIntoShops(string targetSceneName = null)
        {
            // 如果不在基地场景，跳过扫描
            string currentScene = targetSceneName;
            if (string.IsNullOrEmpty(currentScene))
            {
                try { currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch {}
            }
            if (currentScene != "Base_SceneV2")
            {
                return;
            }
            
            try
            {
                StockShop[] shops = UnityEngine.Object.FindObjectsOfType<StockShop>();
                if (shops == null || shops.Length == 0)
                {
                    return;
                }

                int addedCount = 0;
                foreach (StockShop shop in shops)
                {
                    if (shop == null) continue;

                    bool isNpcShop = false;
                    try
                    {
                        if (shop.GetComponentInParent<CharacterMainControl>() != null)
                        {
                            isNpcShop = true;
                        }
                    }
                    catch { }

                    string sceneName = "";
                    string merchantId = "";
                    try { sceneName = shop.gameObject != null ? shop.gameObject.scene.name : ""; } catch { }
                    try { merchantId = shop.MerchantID; } catch { }

                    // 只注入到基地的普通售货机
                    bool isTargetShop = (!isNpcShop && merchantId == "Merchant_Normal" && sceneName == "Base_SceneV2");

                    if (isTargetShop && shop.entries != null)
                    {
                        bool alreadyExists = false;
                        foreach (StockShop.Entry entry in shop.entries)
                        {
                            if (entry != null && entry.ItemTypeID == AchievementMedalConfig.TYPE_ID)
                            {
                                alreadyExists = true;
                                injectedMedalEntry = entry;
                                break;
                            }
                        }

                        if (!alreadyExists)
                        {
                            // 计算 priceFactor 使价格为1块钱
                            float priceFactor = 1f;
                            try
                            {
                                Item itemPrefab = ItemAssetsCollection.GetPrefab(AchievementMedalConfig.TYPE_ID);
                                if (itemPrefab != null)
                                {
                                    int rawValue = itemPrefab.GetTotalRawValue();
                                    if (rawValue > 0)
                                    {
                                        priceFactor = 1f / rawValue;
                                    }
                                }
                            }
                            catch { }
                            
                            StockShopDatabase.ItemEntry itemEntry = new StockShopDatabase.ItemEntry();
                            itemEntry.typeID = AchievementMedalConfig.TYPE_ID;
                            itemEntry.maxStock = AchievementMedalConfig.DEFAULT_MAX_STOCK;
                            itemEntry.forceUnlock = true;
                            itemEntry.priceFactor = priceFactor;
                            itemEntry.possibility = 1f;
                            itemEntry.lockInDemo = false;

                            StockShop.Entry wrapped = new StockShop.Entry(itemEntry);
                            
                            // 从存档读取库存
                            int stockToSet = LoadMedalStockFromSave();
                            wrapped.CurrentStock = stockToSet;
                            wrapped.Show = true;
                            
                            injectedMedalEntry = wrapped;
                            // 插入到列表开头，排在冒险家日志前面
                            shop.entries.Insert(0, wrapped);
                            addedCount++;
                            
                            DevLog("[AchievementMedal] 成就勋章注入成功，库存设置为: " + stockToSet + ", priceFactor=" + priceFactor);
                        }
                    }
                }

                if (addedCount > 0)
                {
                    DevLog("[AchievementMedal] 成就勋章商店注入完成，新增: " + addedCount);
                }
            }
            catch (Exception e)
            {
                DevLog("[AchievementMedal] InjectAchievementMedalIntoShops 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 从存档读取成就勋章库存
        /// </summary>
        private int LoadMedalStockFromSave()
        {
            try
            {
                if (cachedMedalStock >= 0)
                {
                    return cachedMedalStock;
                }
                
                if (SavesSystem.KeyExisits(AchievementMedalConfig.STOCK_SAVE_KEY))
                {
                    cachedMedalStock = SavesSystem.Load<int>(AchievementMedalConfig.STOCK_SAVE_KEY);
                    DevLog("[AchievementMedal] 从存档读取成就勋章库存: " + cachedMedalStock);
                    return cachedMedalStock;
                }
                
                cachedMedalStock = AchievementMedalConfig.DEFAULT_MAX_STOCK;
                return cachedMedalStock;
            }
            catch (Exception e)
            {
                DevLog("[AchievementMedal] 读取成就勋章库存失败: " + e.Message);
                cachedMedalStock = AchievementMedalConfig.DEFAULT_MAX_STOCK;
                return cachedMedalStock;
            }
        }
        
        /// <summary>
        /// 存档时保存成就勋章库存
        /// </summary>
        private void OnCollectSaveData_MedalStock()
        {
            try
            {
                int stockToSave = AchievementMedalConfig.DEFAULT_MAX_STOCK;
                
                if (injectedMedalEntry != null)
                {
                    stockToSave = injectedMedalEntry.CurrentStock;
                }
                
                SavesSystem.Save<int>(AchievementMedalConfig.STOCK_SAVE_KEY, stockToSave);
                cachedMedalStock = stockToSave;
                DevLog("[AchievementMedal] 保存成就勋章库存: " + stockToSave);
            }
            catch (Exception e)
            {
                DevLog("[AchievementMedal] 保存成就勋章库存失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 读档时重置成就勋章缓存
        /// </summary>
        private void OnSetFile_MedalStock()
        {
            cachedMedalStock = -1;
            injectedMedalEntry = null;
            DevLog("[AchievementMedal] 检测到读档，重置成就勋章库存缓存");
        }
    }
}
