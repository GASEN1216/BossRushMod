// ============================================================================
// EquipmentFactory.cs - 自定义装备工厂
// ============================================================================
// 模块说明：
//   提供简单的 API 从 AssetBundle 加载自定义装备
//   支持自动扫描目录加载所有装备包
// 
// ============================================================================
// 资源文件要求（放在 Assets/Equipment/ 目录下）：
// ============================================================================
//
//   BossRush/Assets/Equipment/
//   ├── my_equipment          # 可包含多个装备的 AssetBundle（无扩展名）
//   └── ...
//
// ============================================================================
// Prefab 命名规范（重要！）：
// ============================================================================
//
//   格式：{自定义名}_{类型}_{Model/Item}
//
//   | Prefab 类型 | 命名格式                | 示例                    |
//   |-------------|-------------------------|-------------------------|
//   | 头盔物品    | {名称}_Helmet_Item      | MyGear_Helmet_Item      |
//   | 头盔模型    | {名称}_Helmet_Model     | MyGear_Helmet_Model     |
//   | 护甲物品    | {名称}_Armor_Item       | MyGear_Armor_Item       |
//   | 护甲模型    | {名称}_Armor_Model      | MyGear_Armor_Model      |
//   | 背包物品    | {名称}_Backpack_Item    | MyGear_Backpack_Item    |
//   | 背包模型    | {名称}_Backpack_Model   | MyGear_Backpack_Model   |
//   | 面罩物品    | {名称}_FaceMask_Item    | MyGear_FaceMask_Item    |
//   | 面罩模型    | {名称}_FaceMask_Model   | MyGear_FaceMask_Model   |
//   | 耳机物品    | {名称}_Headset_Item     | MyGear_Headset_Item     |
//   | 耳机模型    | {名称}_Headset_Model    | MyGear_Headset_Model    |
//
//   自动匹配规则：MyGear_Helmet_Item 自动匹配 MyGear_Helmet_Model
//
// ============================================================================
// Unity 中 Prefab 配置要求：
// ============================================================================
//
// Item Prefab 必须配置：
//   - Item 组件
//   - typeID：唯一ID（建议 600000+）
//   - displayName：显示名称
//   - agentUtilities.agents：添加 key="EquipmentModel" 指向 Model Prefab
//   - ⚠️ 不需要配置 tags，代码会自动添加！
//
// Model Prefab 必须配置：
//   - DuckovItemAgent 组件（在父物体上）
//   - 子物体 Layer = Character (9)
//   - 子物体 Scale 调整到合适大小
//
// ============================================================================
// 使用方式：
// ============================================================================
//
//   方式一：自动加载（推荐）
//   // 在 InitializeCustomEquipment() 中调用：
//   EquipmentFactory.LoadAllEquipment();  // 自动扫描并加载 Assets/Equipment/ 下所有 bundle
//
//   方式二：手动加载单个 bundle
//   EquipmentFactory.LoadBundle("my_equipment");  // 自动识别装备类型
//
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 装备类型枚举
    /// </summary>
    public enum EquipmentType
    {
        Helmet,     // 头盔 - 使用 "Helmat" Tag（游戏原版拼写）
        Armor,      // 护甲 - 使用 "Armor" Tag
        Backpack,   // 背包 - 使用 "Backpack" Tag
        FaceMask,   // 面罩 - 使用 "FaceMask" Tag
        Headset     // 耳机 - 使用 "Headset" Tag
    }

    /// <summary>
    /// 自定义装备工厂 - 从 AssetBundle 加载装备
    /// </summary>
    public static class EquipmentFactory
    {
        // 已加载的模型缓存（TypeID -> ItemAgent）
        private static Dictionary<int, ItemAgent> loadedModels = new Dictionary<int, ItemAgent>();
        
        // 已加载的 bundle 列表（避免重复加载）
        private static HashSet<string> loadedBundles = new HashSet<string>();
        
        // Mod 目录路径
        private static string modDirectory = null;

        // 装备资源目录（固定路径）
        private const string EQUIPMENT_PATH = "Assets/Equipment";

        // ========== 公开 API ==========

        /// <summary>
        /// 自动加载 Assets/Equipment/ 目录下所有 AssetBundle（推荐）
        /// </summary>
        /// <returns>加载的装备总数</returns>
        public static int LoadAllEquipment()
        {
            // 获取 Mod 目录
            if (modDirectory == null)
            {
                modDirectory = Path.GetDirectoryName(typeof(EquipmentFactory).Assembly.Location);
            }

            string equipmentDir = Path.Combine(modDirectory, EQUIPMENT_PATH);
            
            if (!Directory.Exists(equipmentDir))
            {
                Debug.Log("[EquipmentFactory] 装备目录不存在，跳过自动加载: " + equipmentDir);
                return 0;
            }

            int totalCount = 0;
            string[] files = Directory.GetFiles(equipmentDir);
            
            foreach (string filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                
                // 跳过 .manifest 文件和其他非 bundle 文件
                if (fileName.Contains(".")) continue;
                
                // 跳过已加载的 bundle
                if (loadedBundles.Contains(fileName)) continue;
                
                Debug.Log("[EquipmentFactory] 自动加载 bundle: " + fileName);
                int count = LoadBundle(fileName);
                totalCount += count;
            }
            
            Debug.Log("[EquipmentFactory] 自动加载完成，共 " + totalCount + " 个装备");
            return totalCount;
        }

        /// <summary>
        /// 从 AssetBundle 加载装备（自动识别装备类型）
        /// </summary>
        /// <param name="bundleName">AssetBundle 文件名（放在 Assets/Equipment/ 下）</param>
        /// <returns>加载的装备数量</returns>
        public static int LoadBundle(string bundleName)
        {
            // 避免重复加载
            if (loadedBundles.Contains(bundleName))
            {
                Debug.Log("[EquipmentFactory] Bundle 已加载，跳过: " + bundleName);
                return 0;
            }

            // 获取 Mod 目录
            if (modDirectory == null)
            {
                modDirectory = Path.GetDirectoryName(typeof(EquipmentFactory).Assembly.Location);
            }

            string bundlePath = Path.Combine(modDirectory, EQUIPMENT_PATH, bundleName);
            
            if (!File.Exists(bundlePath))
            {
                Debug.LogWarning("[EquipmentFactory] 未找到 AssetBundle: " + bundlePath);
                return 0;
            }

            int count = LoadBundleAutoDetect(bundlePath, bundleName);
            
            if (count > 0)
            {
                loadedBundles.Add(bundleName);
            }
            
            return count;
        }

        /// <summary>
        /// 获取已加载的模型缓存（用于运行时修复 Layer/Shader）
        /// </summary>
        public static Dictionary<int, ItemAgent> GetLoadedModels()
        {
            return loadedModels;
        }

        /// <summary>
        /// 检查 TypeID 是否是已加载的自定义装备
        /// </summary>
        public static bool IsCustomEquipment(int typeId)
        {
            return loadedModels.ContainsKey(typeId);
        }

        /// <summary>
        /// 获取装备类型对应的 Tag 名称
        /// </summary>
        private static string GetTagName(EquipmentType type)
        {
            switch (type)
            {
                case EquipmentType.Helmet: return "Helmat";  // 注意：游戏原版拼写
                case EquipmentType.Armor: return "Armor";
                case EquipmentType.Backpack: return "Backpack";
                case EquipmentType.FaceMask: return "FaceMask";
                case EquipmentType.Headset: return "Headset";
                default: return "Helmat";
            }
        }

        /// <summary>
        /// 从 Prefab 名称中解析装备类型
        /// 命名格式：{自定义名}_{类型}_{Model/Item}
        /// 例如：MyGear_Helmet_Item, MyGear_Armor_Model
        /// </summary>
        private static EquipmentType? ParseEquipmentTypeFromName(string prefabName)
        {
            string nameLower = prefabName.ToLower();
            
            // 检查是否包含类型关键字
            if (nameLower.Contains("_helmet_")) return EquipmentType.Helmet;
            if (nameLower.Contains("_armor_")) return EquipmentType.Armor;
            if (nameLower.Contains("_platecarrier_")) return EquipmentType.Armor;
            if (nameLower.Contains("_backpack_")) return EquipmentType.Backpack;
            if (nameLower.Contains("_bag_")) return EquipmentType.Backpack;
            if (nameLower.Contains("_facemask_")) return EquipmentType.FaceMask;
            if (nameLower.Contains("_mask_")) return EquipmentType.FaceMask;
            if (nameLower.Contains("_headset_")) return EquipmentType.Headset;
            
            return null;
        }

        /// <summary>
        /// 从 Prefab 名称中提取装备基础名（去掉 _Model 或 _Item 后缀）
        /// 例如：MyGear_Helmet_Item -> MyGear_Helmet
        /// </summary>
        private static string ExtractEquipmentBaseName(string prefabName)
        {
            string name = prefabName;
            
            // 去掉 _Item 或 _Model 后缀
            if (name.EndsWith("_Item", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 5);
            }
            else if (name.EndsWith("_Model", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 6);
            }
            
            return name;
        }

        // ========== 内部方法 ==========

        /// <summary>
        /// 加载 AssetBundle 并自动检测装备类型
        /// </summary>
        private static int LoadBundleAutoDetect(string bundlePath, string bundleName)
        {
            AssetBundle bundle = null;
            
            try
            {
                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    Debug.LogError("[EquipmentFactory] 加载 AssetBundle 失败: " + bundlePath);
                    return 0;
                }

                // 加载所有 GameObject
                var assets = bundle.LoadAllAssets<GameObject>();
                if (assets == null || assets.Length == 0)
                {
                    Debug.LogWarning("[EquipmentFactory] AssetBundle 中未找到任何资源: " + bundleName);
                    return 0;
                }

                // 分类收集 Item 和 Model（按装备基础名分组）
                // Key: 装备基础名（如 MyGear_Helmet）, Value: (Item, Model, Type)
                Dictionary<string, Item> itemsByBaseName = new Dictionary<string, Item>();
                Dictionary<string, ItemAgent> modelsByBaseName = new Dictionary<string, ItemAgent>();
                Dictionary<string, EquipmentType> typesByBaseName = new Dictionary<string, EquipmentType>();

                foreach (var go in assets)
                {
                    string goName = go.name;
                    string baseName = ExtractEquipmentBaseName(goName);
                    EquipmentType? detectedType = ParseEquipmentTypeFromName(goName);

                    // 检查是否是 Item Prefab
                    Item item = go.GetComponent<Item>();
                    if (item != null)
                    {
                        itemsByBaseName[baseName] = item;
                        if (detectedType.HasValue)
                        {
                            typesByBaseName[baseName] = detectedType.Value;
                        }
                        Debug.Log("[EquipmentFactory] 发现 Item: " + goName + " (TypeID=" + item.TypeID + ", BaseName=" + baseName + ", Type=" + (detectedType.HasValue ? detectedType.Value.ToString() : "未知") + ")");
                        continue;
                    }

                    // 检查是否是 Model Prefab
                    ItemAgent agent = go.GetComponent<ItemAgent>();
                    bool isModel = agent != null || goName.ToLower().EndsWith("_model");
                    
                    if (isModel)
                    {
                        // 如果没有 ItemAgent，添加 DuckovItemAgent
                        if (agent == null)
                        {
                            agent = go.AddComponent<DuckovItemAgent>();
                        }
                        
                        modelsByBaseName[baseName] = agent;
                        if (detectedType.HasValue && !typesByBaseName.ContainsKey(baseName))
                        {
                            typesByBaseName[baseName] = detectedType.Value;
                        }
                        Debug.Log("[EquipmentFactory] 发现 Model: " + goName + " -> BaseName=" + baseName);
                    }
                }

                // 处理每个 Item
                int loadedCount = 0;
                
                foreach (var kvp in itemsByBaseName)
                {
                    string baseName = kvp.Key;
                    Item itemPrefab = kvp.Value;
                    
                    try
                    {
                        // 获取装备类型
                        EquipmentType equipType = EquipmentType.Helmet; // 默认
                        if (typesByBaseName.ContainsKey(baseName))
                        {
                            equipType = typesByBaseName[baseName];
                        }
                        
                        string tagName = GetTagName(equipType);
                        
                        // 查找匹配的 Model
                        ItemAgent modelAgent = null;
                        if (modelsByBaseName.ContainsKey(baseName))
                        {
                            modelAgent = modelsByBaseName[baseName];
                        }

                        // 添加装备 Tag
                        AddTagToItem(itemPrefab, tagName);

                        // 注入 EquipmentModel（如果 Unity 中未配置）
                        if (modelAgent != null && !HasEquipmentModel(itemPrefab))
                        {
                            InjectEquipmentModel(itemPrefab, modelAgent);
                        }

                        // 缓存模型
                        if (modelAgent != null)
                        {
                            loadedModels[itemPrefab.TypeID] = modelAgent;
                        }

                        // 注册到游戏物品系统
                        ItemAssetsCollection.AddDynamicEntry(itemPrefab);
                        loadedCount++;

                        Debug.Log("[EquipmentFactory] 成功加载: " + itemPrefab.gameObject.name + 
                            " (TypeID=" + itemPrefab.TypeID + ", Type=" + equipType + ", Model=" + (modelAgent != null ? modelAgent.name : "无") + ")");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[EquipmentFactory] 处理 Item 失败: " + itemPrefab.gameObject.name + " - " + e.Message);
                    }
                }

                Debug.Log("[EquipmentFactory] Bundle '" + bundleName + "' 加载完成，共 " + loadedCount + " 个装备");
                return loadedCount;
            }
            catch (Exception e)
            {
                Debug.LogError("[EquipmentFactory] LoadBundleAutoDetect 出错: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
            // 注意：不要 Unload bundle，因为资源还在使用
        }

        /// <summary>
        /// 检查 Item 是否已配置 EquipmentModel
        /// </summary>
        private static bool HasEquipmentModel(Item item)
        {
            try
            {
                var agentUtilitiesField = typeof(Item).GetField("agentUtilities", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentUtilitiesField == null) return false;

                var agentUtilities = agentUtilitiesField.GetValue(item);
                if (agentUtilities == null) return false;

                var agentsField = agentUtilities.GetType().GetField("agents", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentsField == null) return false;

                var agentsList = agentsField.GetValue(agentUtilities);
                if (agentsList == null) return false;

                var countProp = agentsList.GetType().GetProperty("Count");
                var itemProp = agentsList.GetType().GetProperty("Item");
                int count = (int)countProp.GetValue(agentsList);

                for (int i = 0; i < count; i++)
                {
                    var entry = itemProp.GetValue(agentsList, new object[] { i });
                    var keyField = entry.GetType().GetField("key", BindingFlags.Public | BindingFlags.Instance);
                    var prefabField = entry.GetType().GetField("agentPrefab", BindingFlags.Public | BindingFlags.Instance);
                    
                    string key = keyField != null ? keyField.GetValue(entry) as string : null;
                    var prefab = prefabField != null ? prefabField.GetValue(entry) as ItemAgent : null;
                    
                    if (key == "EquipmentModel" && prefab != null)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 为物品添加 Tag
        /// </summary>
        private static void AddTagToItem(Item item, string tagName)
        {
            try
            {
                var allTags = GameplayDataSettings.Tags.AllTags;
                if (allTags == null) return;

                Tag targetTag = null;
                foreach (Tag tag in allTags)
                {
                    if (tag != null && tag.name == tagName)
                    {
                        targetTag = tag;
                        break;
                    }
                }

                if (targetTag == null)
                {
                    Debug.LogWarning("[EquipmentFactory] 未找到 Tag: " + tagName);
                    return;
                }

                var tagsField = typeof(Item).GetField("tags", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (tagsField == null) return;

                var tagCollection = tagsField.GetValue(item);
                if (tagCollection == null) return;

                var listField = tagCollection.GetType().GetField("list", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (listField == null) return;

                var tagList = listField.GetValue(tagCollection) as List<Tag>;
                if (tagList == null)
                {
                    tagList = new List<Tag>();
                    listField.SetValue(tagCollection, tagList);
                }

                // 检查是否已存在
                foreach (Tag t in tagList)
                {
                    if (t != null && t.name == tagName) return;
                }

                tagList.Add(targetTag);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EquipmentFactory] 添加 Tag 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注入 EquipmentModel 到物品
        /// </summary>
        private static void InjectEquipmentModel(Item item, ItemAgent modelAgent)
        {
            try
            {
                var agentUtilitiesField = typeof(Item).GetField("agentUtilities", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentUtilitiesField == null) return;

                var agentUtilities = agentUtilitiesField.GetValue(item);
                if (agentUtilities == null) return;

                var agentsField = agentUtilities.GetType().GetField("agents", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentsField == null) return;

                // 获取 AgentKeyPair 类型
                Type agentKeyPairType = null;
                var nestedTypes = agentUtilities.GetType().GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var nt in nestedTypes)
                {
                    if (nt.Name == "AgentKeyPair")
                    {
                        agentKeyPairType = nt;
                        break;
                    }
                }

                if (agentKeyPairType == null)
                {
                    agentKeyPairType = Type.GetType("ItemStatsSystem.ItemAgentUtilities+AgentKeyPair, ItemStatsSystem");
                }

                if (agentKeyPairType == null) return;

                var agentsList = agentsField.GetValue(agentUtilities);
                if (agentsList == null)
                {
                    var listType = typeof(List<>).MakeGenericType(agentKeyPairType);
                    agentsList = Activator.CreateInstance(listType);
                    agentsField.SetValue(agentUtilities, agentsList);
                }

                var keyField = agentKeyPairType.GetField("key", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var agentPrefabField = agentKeyPairType.GetField("agentPrefab", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                // 创建新条目
                object newEntry = Activator.CreateInstance(agentKeyPairType);
                if (keyField != null)
                {
                    keyField.SetValue(newEntry, "EquipmentModel");
                }
                if (agentPrefabField != null)
                {
                    agentPrefabField.SetValue(newEntry, modelAgent);
                }

                var addMethod = agentsList.GetType().GetMethod("Add");
                if (addMethod != null)
                {
                    addMethod.Invoke(agentsList, new object[] { newEntry });
                }

                // 清除缓存
                var cacheField = agentUtilities.GetType().GetField("hashedAgentsCache", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (cacheField != null)
                {
                    cacheField.SetValue(agentUtilities, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[EquipmentFactory] 注入 EquipmentModel 失败: " + e.Message);
            }
        }
    }
}
