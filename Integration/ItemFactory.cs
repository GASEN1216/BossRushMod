// ============================================================================
// ItemFactory.cs - 通用物品工厂
// ============================================================================
// 模块说明：
//   提供简单的 API 从 AssetBundle 加载消耗品等非装备类物品
//   支持自动扫描目录加载所有资源包
// 
// ============================================================================
// 资源文件要求（放在 Assets/Items/ 目录下）：
// ============================================================================
//
//   BossRush/Assets/Items/
//   ├── cold_quench_fluid    # 冷淬液物品 AssetBundle（无扩展名）
//   └── ...
//
// ============================================================================
// Prefab 命名规范：
// ============================================================================
//
//   格式：{物品名}
//   示例：ColdQuenchFluid
//
// ============================================================================
// 使用方式：
// ============================================================================
//
//   方式一：自动加载（推荐）
//   ItemFactory.LoadAllItems();  // 自动扫描并加载所有 bundle
//
//   方式二：手动加载单个 bundle
//   ItemFactory.LoadBundle("cold_quench_fluid");
//
//   方式三：获取物品数量
//   int count = ItemFactory.GetItemCountInInventory(500014);
//
//   方式四：消耗物品
//   bool success = ItemFactory.ConsumeItem(500014, 1);
//
// ============================================================================

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 通用物品工厂 - 从 AssetBundle 加载消耗品等非装备类物品
    /// </summary>
    public static class ItemFactory
    {
        // ========== 缓存字典 ==========
        
        // 已加载的物品缓存（TypeID -> Item预制体）
        private static Dictionary<int, Item> loadedItems = new Dictionary<int, Item>();
        
        // 已加载的 bundle 列表（避免重复加载）
        private static HashSet<string> loadedBundles = new HashSet<string>();
        
        // 已加载的 AssetBundle 缓存（用于加载图标等资源）
        private static Dictionary<string, AssetBundle> loadedAssetBundles = new Dictionary<string, AssetBundle>();
        
        // 已加载的图标缓存
        private static Dictionary<string, Sprite> loadedSprites = new Dictionary<string, Sprite>();
        
        // 物品配置回调（TypeID -> 配置方法）
        private static Dictionary<int, Action<Item>> itemConfigurators = new Dictionary<int, Action<Item>>();
        
        // Mod 目录路径
        private static string modDirectory = null;

        // 物品资源目录（固定路径）
        private const string ITEMS_PATH = "Assets/Items";

        // ========== 公开 API ==========

        /// <summary>
        /// 注册物品配置器（在物品加载后调用）
        /// </summary>
        /// <param name="typeId">物品 TypeID</param>
        /// <param name="configurator">配置方法</param>
        public static void RegisterConfigurator(int typeId, Action<Item> configurator)
        {
            itemConfigurators[typeId] = configurator;
        }

        /// <summary>
        /// 自动加载 Assets/Items/ 目录下所有 AssetBundle（推荐）
        /// </summary>
        /// <returns>加载的物品总数</returns>
        public static int LoadAllItems()
        {
            if (modDirectory == null)
            {
                modDirectory = Path.GetDirectoryName(typeof(ItemFactory).Assembly.Location);
            }

            string itemsDir = Path.Combine(modDirectory, ITEMS_PATH);
            
            if (!Directory.Exists(itemsDir))
            {
                ModBehaviour.DevLog("[ItemFactory] 物品目录不存在，跳过自动加载: " + itemsDir);
                return 0;
            }

            int totalCount = 0;
            string[] files = Directory.GetFiles(itemsDir);
            
            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                
                // 跳过 .manifest 文件和其他非 bundle 文件
                if (fileName.Contains(".")) continue;
                
                // 跳过已加载的 bundle
                if (loadedBundles.Contains(fileName)) continue;
                
                ModBehaviour.DevLog("[ItemFactory] 自动加载 bundle: " + fileName);
                int count = LoadBundle(fileName);
                totalCount += count;
            }
            
            ModBehaviour.DevLog("[ItemFactory] 自动加载完成，共 " + totalCount + " 个物品");
            return totalCount;
        }

        /// <summary>
        /// 从 AssetBundle 加载物品
        /// </summary>
        public static int LoadBundle(string bundleName)
        {
            if (loadedBundles.Contains(bundleName))
            {
                ModBehaviour.DevLog("[ItemFactory] Bundle 已加载，跳过: " + bundleName);
                return 0;
            }

            if (modDirectory == null)
            {
                modDirectory = Path.GetDirectoryName(typeof(ItemFactory).Assembly.Location);
            }

            string bundlePath = Path.Combine(modDirectory, ITEMS_PATH, bundleName);
            
            if (!File.Exists(bundlePath))
            {
                ModBehaviour.DevLog("[ItemFactory] 未找到 AssetBundle: " + bundlePath);
                return 0;
            }

            int count = LoadBundleInternal(bundlePath, bundleName);
            
            if (count > 0)
            {
                loadedBundles.Add(bundleName);
            }
            
            return count;
        }

        /// <summary>
        /// 获取已加载的物品预制体
        /// </summary>
        public static Item GetLoadedItem(int typeId)
        {
            Item item;
            if (loadedItems.TryGetValue(typeId, out item))
            {
                return item;
            }
            return null;
        }

        /// <summary>
        /// 从 AssetBundle 获取图标 Sprite
        /// </summary>
        /// <param name="bundleName">AssetBundle 名称</param>
        /// <param name="spriteName">Sprite 资源名称（不含扩展名）</param>
        /// <returns>Sprite 对象，如果未找到则返回 null</returns>
        public static Sprite GetSprite(string bundleName, string spriteName)
        {
            // 检查缓存
            string cacheKey = bundleName + "/" + spriteName;
            if (loadedSprites.TryGetValue(cacheKey, out Sprite cachedSprite))
            {
                return cachedSprite;
            }
            
            try
            {
                // 确保 bundle 已加载
                if (!loadedAssetBundles.ContainsKey(bundleName))
                {
                    // 尝试加载 bundle
                    if (modDirectory == null)
                    {
                        modDirectory = Path.GetDirectoryName(typeof(ItemFactory).Assembly.Location);
                    }
                    
                    string bundlePath = Path.Combine(modDirectory, ITEMS_PATH, bundleName);
                    if (!File.Exists(bundlePath))
                    {
                        ModBehaviour.DevLog("[ItemFactory] 未找到 AssetBundle: " + bundlePath);
                        return null;
                    }
                    
                    AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                    if (bundle == null)
                    {
                        ModBehaviour.DevLog("[ItemFactory] 加载 AssetBundle 失败: " + bundlePath);
                        return null;
                    }
                    
                    loadedAssetBundles[bundleName] = bundle;
                }
                
                // 从 bundle 加载 Sprite
                AssetBundle assetBundle = loadedAssetBundles[bundleName];
                
                // 尝试多种加载方式
                Sprite sprite = assetBundle.LoadAsset<Sprite>(spriteName);
                if (sprite == null)
                {
                    sprite = assetBundle.LoadAsset<Sprite>(spriteName + ".png");
                }
                if (sprite == null)
                {
                    // 尝试从 Texture2D 创建 Sprite
                    Texture2D texture = assetBundle.LoadAsset<Texture2D>(spriteName);
                    if (texture == null)
                    {
                        texture = assetBundle.LoadAsset<Texture2D>(spriteName + ".png");
                    }
                    if (texture != null)
                    {
                        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    }
                }
                
                if (sprite != null)
                {
                    loadedSprites[cacheKey] = sprite;
                    ModBehaviour.DevLog("[ItemFactory] 成功加载 Sprite: " + cacheKey);
                }
                else
                {
                    ModBehaviour.DevLog("[ItemFactory] 未找到 Sprite: " + cacheKey);
                }
                
                return sprite;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ItemFactory] 加载 Sprite 失败: " + cacheKey + " - " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 检查 TypeID 是否是已加载的自定义物品
        /// </summary>
        public static bool IsCustomItem(int typeId)
        {
            return loadedItems.ContainsKey(typeId);
        }

        /// <summary>
        /// 获取玩家背包中指定物品的数量
        /// </summary>
        /// <param name="typeId">物品 TypeID</param>
        /// <returns>物品数量，如果没有则返回0</returns>
        public static int GetItemCountInInventory(int typeId)
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null || player.CharacterItem == null) return 0;
                
                var inventory = player.CharacterItem.Inventory;
                if (inventory == null || inventory.Content == null) return 0;
                
                int totalCount = 0;
                foreach (var item in inventory.Content)
                {
                    if (item == null) continue;
                    if (item.TypeID == typeId)
                    {
                        // 检查是否是可堆叠物品
                        if (item.Stackable)
                        {
                            totalCount += item.StackCount;
                        }
                        else
                        {
                            totalCount += 1;
                        }
                    }
                }
                
                return totalCount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ItemFactory] 获取物品数量失败: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 从玩家背包中消耗指定数量的物品
        /// </summary>
        /// <param name="typeId">物品 TypeID</param>
        /// <param name="count">消耗数量</param>
        /// <returns>是否成功消耗</returns>
        public static bool ConsumeItem(int typeId, int count = 1)
        {
            try
            {
                var player = CharacterMainControl.Main;
                if (player == null || player.CharacterItem == null) return false;
                
                var inventory = player.CharacterItem.Inventory;
                if (inventory == null || inventory.Content == null) return false;
                
                // 先检查数量是否足够
                int available = GetItemCountInInventory(typeId);
                if (available < count)
                {
                    ModBehaviour.DevLog("[ItemFactory] 物品数量不足: 需要 " + count + ", 拥有 " + available);
                    return false;
                }
                
                int remaining = count;
                
                // 遍历背包，消耗物品
                for (int i = inventory.Content.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    var item = inventory.Content[i];
                    if (item == null || item.TypeID != typeId) continue;
                    
                    if (item.Stackable)
                    {
                        int stackCount = item.StackCount;
                        if (stackCount <= remaining)
                        {
                            // 整个堆叠都消耗掉
                            remaining -= stackCount;
                            inventory.RemoveItem(item);
                            ModBehaviour.DevLog("[ItemFactory] 消耗整个堆叠: " + stackCount);
                        }
                        else
                        {
                            // 只消耗部分
                            item.StackCount -= remaining;
                            ModBehaviour.DevLog("[ItemFactory] 消耗部分堆叠: " + remaining + ", 剩余: " + item.StackCount);
                            remaining = 0;
                        }
                    }
                    else
                    {
                        // 非堆叠物品，直接移除
                        inventory.RemoveItem(item);
                        remaining -= 1;
                        ModBehaviour.DevLog("[ItemFactory] 消耗非堆叠物品");
                    }
                }
                
                return remaining == 0;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ItemFactory] 消耗物品失败: " + e.Message);
                return false;
            }
        }

        // ========== 核心加载逻辑 ==========

        /// <summary>
        /// 加载 AssetBundle 并注册所有物品
        /// </summary>
        private static int LoadBundleInternal(string bundlePath, string bundleName)
        {
            AssetBundle bundle = null;
            
            try
            {
                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[ItemFactory] 加载 AssetBundle 失败: " + bundlePath);
                    return 0;
                }

                var assets = bundle.LoadAllAssets<GameObject>();
                if (assets == null || assets.Length == 0)
                {
                    ModBehaviour.DevLog("[ItemFactory] AssetBundle 中未找到任何资源: " + bundleName);
                    return 0;
                }

                int loadedCount = 0;

                foreach (var go in assets)
                {
                    // 检查是否是 Item 预制体
                    Item item = go.GetComponent<Item>();
                    if (item == null) continue;
                    
                    string goName = go.name;
                    
                    try
                    {
                        // 缓存物品
                        loadedItems[item.TypeID] = item;
                        
                        // 调用配置器（如果有）
                        Action<Item> configurator;
                        if (itemConfigurators.TryGetValue(item.TypeID, out configurator))
                        {
                            configurator(item);
                            ModBehaviour.DevLog("[ItemFactory] 已应用配置器: TypeID=" + item.TypeID);
                        }
                        
                        // 注册到游戏物品系统
                        ItemAssetsCollection.AddDynamicEntry(item);
                        loadedCount++;

                        ModBehaviour.DevLog("[ItemFactory] 成功加载物品: " + goName + 
                            " (TypeID=" + item.TypeID + ")");
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[ItemFactory] 处理物品失败: " + goName + " - " + e.Message);
                    }
                }

                ModBehaviour.DevLog("[ItemFactory] Bundle '" + bundleName + "' 加载完成，共 " + loadedCount + " 个物品");
                return loadedCount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ItemFactory] LoadBundleInternal 出错: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
            // 注意：不要 Unload bundle，因为资源还在使用
        }

        /// <summary>
        /// 清理所有缓存（Mod卸载时调用）
        /// </summary>
        public static void Shutdown()
        {
            loadedItems.Clear();
            loadedBundles.Clear();
            loadedSprites.Clear();
            
            // 卸载 AssetBundle（但不卸载已加载的资源）
            foreach (var bundle in loadedAssetBundles.Values)
            {
                if (bundle != null)
                {
                    bundle.Unload(false);
                }
            }
            loadedAssetBundles.Clear();
            
            ModBehaviour.DevLog("[ItemFactory] 已清理所有缓存");
        }
    }
}
