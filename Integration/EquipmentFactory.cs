// ============================================================================
// EquipmentFactory.cs - 自定义装备/武器工厂
// ============================================================================
// 模块说明：
//   提供简单的 API 从 AssetBundle 加载自定义装备、武器和Buff
//   支持自动扫描目录加载所有资源包
// 
// ============================================================================
// 资源文件要求（放在 Assets/Equipment/ 目录下）：
// ============================================================================
//
//   BossRush/Assets/Equipment/
//   ├── my_equipment          # 可包含多个装备/武器的 AssetBundle（无扩展名）
//   └── ...
//
// ============================================================================
// Prefab 命名规范（重要！）：
// ============================================================================
//
//   格式：{自定义名}_{类型}_{后缀}
//
//   【装备类】
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
//   | 图腾物品    | {名称}_Totem_Item       | FlightTotem_Lv1_Item    |
//   | 图腾模型    | {名称}_Totem_Model      | FlightTotem_Lv1_Model   |
//
//   【武器类】
//   | Prefab 类型 | 命名格式                | 示例                    |
//   |-------------|-------------------------|-------------------------|
//   | 枪械物品    | {名称}_Gun_Item         | Dragon_Gun_Item         |
//   | 枪械模型    | {名称}_Gun_Model        | Dragon_Gun_Model（可选）|
//   | 子弹预制体  | {名称}_Bullet           | Dragon_Bullet           |
//   | Buff预制体  | {名称}_Buff             | Dragon_Buff             |
//
//   自动匹配规则：
//   - Dragon_Gun_Item 自动匹配 Dragon_Gun_Model、Dragon_Bullet、Dragon_Buff
//   - 武器的 Buff 和 Bullet 会自动注入到 ItemSetting_Gun 组件
//
// ============================================================================
// Unity 中 Prefab 配置要求：
// ============================================================================
//
// 【装备 Item Prefab】必须配置：
//   - Item 组件
//   - typeID：唯一ID（建议 600000+）
//   - ⚠️ 不需要配置 tags，代码会自动添加！
//
// 【图腾 Item Prefab】必须配置：
//   - Item 组件
//   - typeID：唯一ID（建议 600100+）
//   - displayName：本地化键名（如 "BossRush_FlightTotem"）
//   - quality：品质等级（1-8）
//   - ⚠️ 不需要配置 tags，代码会自动添加 Totem 标签
//
// 【武器 Item Prefab】必须配置：
//   - Item 组件 + ItemSetting_Gun 组件
//   - typeID：唯一ID
//   - ItemSetting_Gun 的基础属性（triggerMode, reloadMode 等）
//   - ⚠️ bulletPfb 和 buff 可以不配置，代码会自动关联同名资源
//
// 【Buff Prefab】必须配置：
//   - Buff 组件（来自 Duckov.Buffs 命名空间）
//   - id：唯一Buff ID
//   - maxLayers：最大叠加层数
//   - displayName/description：本地化键名
//   - limitedLifeTime/totalLifeTime：持续时间
//   - effects：Effect列表（可选）
//
// 【Bullet Prefab】必须配置：
//   - Projectile 组件
//   - radius：碰撞半径
//   - hitFx：命中特效（可选）
//
// ============================================================================
// 使用方式：
// ============================================================================
//
//   方式一：自动加载（推荐）
//   EquipmentFactory.LoadAllEquipment();  // 自动扫描并加载所有 bundle
//
//   方式二：手动加载单个 bundle
//   EquipmentFactory.LoadBundle("my_equipment");
//
//   方式三：获取已加载的Buff（用于代码中手动应用）
//   Buff dragonBuff = EquipmentFactory.GetLoadedBuff("Dragon");
//   character.AddBuff(dragonBuff, fromWho);
//
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using Duckov.Utilities;
using Duckov.Buffs;

namespace BossRush
{
    /// <summary>
    /// 物品类型枚举（扩展支持武器和图腾）
    /// </summary>
    public enum EquipmentType
    {
        Helmet,     // 头盔 - 使用 "Helmat" Tag（游戏原版拼写）
        Armor,      // 护甲 - 使用 "Armor" Tag
        Backpack,   // 背包 - 使用 "Backpack" Tag
        FaceMask,   // 面罩 - 使用 "FaceMask" Tag
        Headset,    // 耳机 - 使用 "Headset" Tag
        Gun,        // 枪械 - 使用 "Gun" Tag
        Totem       // 图腾 - 使用 "Totem" Tag（新增）
    }

    /// <summary>
    /// 自定义装备/武器工厂 - 从 AssetBundle 加载装备、武器和Buff
    /// </summary>
    public static class EquipmentFactory
    {
        // ========== 缓存字典 ==========
        
        // 已加载的模型缓存（TypeID -> ItemAgent）
        private static Dictionary<int, ItemAgent> loadedModels = new Dictionary<int, ItemAgent>();
        
        // 已加载的Buff缓存（基础名 -> Buff预制体）
        private static Dictionary<string, Buff> loadedBuffs = new Dictionary<string, Buff>();
        
        // 已加载的子弹缓存（基础名 -> Projectile预制体）
        private static Dictionary<string, Projectile> loadedBullets = new Dictionary<string, Projectile>();
        
        // 已加载的武器缓存（TypeID -> Item）
        private static Dictionary<int, Item> loadedGuns = new Dictionary<int, Item>();
        
        // 已加载的 bundle 列表（避免重复加载）
        private static HashSet<string> loadedBundles = new HashSet<string>();
        
        // Mod 目录路径
        private static string modDirectory = null;

        // 装备资源目录（固定路径）
        private const string EQUIPMENT_PATH = "Assets/Equipment";
        
        // Character Layer 常量（游戏中 Character 层为 9）
        private const int CHARACTER_LAYER = 9;
        
        // 游戏使用的 Shader 名称
        private const string GAME_SHADER_NAME = "SodaCraft/SodaCharacter";
        
        // 缓存的游戏 Shader
        private static Shader gameShader = null;

        // ========== 公开 API ==========

        /// <summary>
        /// 自动加载 Assets/Equipment/ 目录下所有 AssetBundle（推荐）
        /// </summary>
        /// <returns>加载的物品总数</returns>
        public static int LoadAllEquipment()
        {
            if (modDirectory == null)
            {
                modDirectory = Path.GetDirectoryName(typeof(EquipmentFactory).Assembly.Location);
            }

            string equipmentDir = Path.Combine(modDirectory, EQUIPMENT_PATH);
            
            if (!Directory.Exists(equipmentDir))
            {
                ModBehaviour.DevLog("[EquipmentFactory] 装备目录不存在，跳过自动加载: " + equipmentDir);
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
                
                ModBehaviour.DevLog("[EquipmentFactory] 自动加载 bundle: " + fileName);
                int count = LoadBundle(fileName);
                totalCount += count;
            }
            
            ModBehaviour.DevLog("[EquipmentFactory] 自动加载完成，共 " + totalCount + " 个物品");
            return totalCount;
        }

        /// <summary>
        /// 从 AssetBundle 加载装备/武器（自动识别类型）
        /// </summary>
        public static int LoadBundle(string bundleName)
        {
            if (loadedBundles.Contains(bundleName))
            {
                ModBehaviour.DevLog("[EquipmentFactory] Bundle 已加载，跳过: " + bundleName);
                return 0;
            }

            if (modDirectory == null)
            {
                modDirectory = Path.GetDirectoryName(typeof(EquipmentFactory).Assembly.Location);
            }

            string bundlePath = Path.Combine(modDirectory, EQUIPMENT_PATH, bundleName);
            
            if (!File.Exists(bundlePath))
            {
                ModBehaviour.DevLog("[EquipmentFactory] 未找到 AssetBundle: " + bundlePath);
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
        /// 获取已加载的Buff预制体（用于代码中手动应用Buff）
        /// </summary>
        /// <param name="baseName">Buff基础名（如 "Dragon" 对应 Dragon_Buff）</param>
        public static Buff GetLoadedBuff(string baseName)
        {
            Buff buff;
            if (loadedBuffs.TryGetValue(baseName, out buff))
            {
                return buff;
            }
            return null;
        }
        
        /// <summary>
        /// 获取已加载的Buff预制体（通过Buff ID）
        /// </summary>
        public static Buff GetLoadedBuffById(int buffId)
        {
            foreach (var kvp in loadedBuffs)
            {
                if (kvp.Value != null && kvp.Value.ID == buffId)
                {
                    return kvp.Value;
                }
            }
            return null;
        }
        
        /// <summary>
        /// 获取已加载的子弹预制体
        /// </summary>
        public static Projectile GetLoadedBullet(string baseName)
        {
            Projectile bullet;
            if (loadedBullets.TryGetValue(baseName, out bullet))
            {
                return bullet;
            }
            return null;
        }
        
        /// <summary>
        /// 获取已加载的武器（通过TypeID）
        /// </summary>
        public static Item GetLoadedGun(int typeId)
        {
            Item gun;
            if (loadedGuns.TryGetValue(typeId, out gun))
            {
                return gun;
            }
            return null;
        }

        /// <summary>
        /// 获取已加载的模型缓存
        /// </summary>
        public static Dictionary<int, ItemAgent> GetLoadedModels()
        {
            return loadedModels;
        }

        /// <summary>
        /// 检查 TypeID 是否是已加载的自定义物品
        /// </summary>
        public static bool IsCustomEquipment(int typeId)
        {
            return loadedModels.ContainsKey(typeId) || loadedGuns.ContainsKey(typeId);
        }
        
        /// <summary>
        /// 获取所有已加载的Buff（只读）
        /// </summary>
        public static IReadOnlyDictionary<string, Buff> GetAllLoadedBuffs()
        {
            return loadedBuffs;
        }

        // ========== 类型解析方法 ==========

        /// <summary>
        /// 获取物品类型对应的 Tag 名称
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
                case EquipmentType.Gun: return "Gun";
                case EquipmentType.Totem: return "Totem";
                default: return "Helmat";
            }
        }

        /// <summary>
        /// 从 Prefab 名称中解析物品类型
        /// 命名格式：{自定义名}_{类型}_{Model/Item}
        /// </summary>
        private static EquipmentType? ParseEquipmentTypeFromName(string prefabName)
        {
            string nameLower = prefabName.ToLower();
            
            // 武器类型（优先检测）
            if (nameLower.Contains("_gun_")) return EquipmentType.Gun;
            if (nameLower.Contains("_weapon_")) return EquipmentType.Gun;
            if (nameLower.Contains("_rifle_")) return EquipmentType.Gun;
            if (nameLower.Contains("_pistol_")) return EquipmentType.Gun;
            if (nameLower.Contains("_shotgun_")) return EquipmentType.Gun;
            if (nameLower.Contains("_smg_")) return EquipmentType.Gun;
            
            // 图腾类型（新增）
            if (nameLower.Contains("_totem_")) return EquipmentType.Totem;
            if (nameLower.Contains("totem")) return EquipmentType.Totem;
            
            // 装备类型
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
        /// 从 Prefab 名称中提取基础名（去掉后缀）
        /// 例如：Dragon_Gun_Item -> Dragon_Gun, Dragon_Bullet -> Dragon
        /// </summary>
        private static string ExtractBaseName(string prefabName)
        {
            string name = prefabName;
            
            // 去掉常见后缀
            string[] suffixes = { "_Item", "_Model", "_Bullet", "_Buff", "_FX", "_Effect" };
            foreach (string suffix in suffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                    break;
                }
            }
            
            return name;
        }
        
        /// <summary>
        /// 从武器基础名中提取Buff/Bullet匹配名
        /// 例如：Dragon_Gun -> Dragon（用于匹配 Dragon_Bullet, Dragon_Buff）
        /// </summary>
        private static string ExtractWeaponPrefix(string gunBaseName)
        {
            // 去掉 _Gun, _Weapon 等后缀
            string[] weaponSuffixes = { "_Gun", "_Weapon", "_Rifle", "_Pistol", "_Shotgun", "_SMG" };
            foreach (string suffix in weaponSuffixes)
            {
                if (gunBaseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return gunBaseName.Substring(0, gunBaseName.Length - suffix.Length);
                }
            }
            return gunBaseName;
        }

        // ========== 核心加载逻辑 ==========

        /// <summary>
        /// 加载 AssetBundle 并自动检测所有资源类型
        /// </summary>
        private static int LoadBundleInternal(string bundlePath, string bundleName)
        {
            AssetBundle bundle = null;
            
            try
            {
                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[EquipmentFactory] 加载 AssetBundle 失败: " + bundlePath);
                    return 0;
                }

                var assets = bundle.LoadAllAssets<GameObject>();
                if (assets == null || assets.Length == 0)
                {
                    ModBehaviour.DevLog("[EquipmentFactory] AssetBundle 中未找到任何资源: " + bundleName);
                    return 0;
                }

                // 分类收集所有资源
                Dictionary<string, Item> itemsByBaseName = new Dictionary<string, Item>();
                Dictionary<string, ItemAgent> modelsByBaseName = new Dictionary<string, ItemAgent>();
                Dictionary<string, EquipmentType> typesByBaseName = new Dictionary<string, EquipmentType>();
                Dictionary<string, Buff> buffsByPrefix = new Dictionary<string, Buff>();
                Dictionary<string, Projectile> bulletsByPrefix = new Dictionary<string, Projectile>();
                Dictionary<string, ItemSetting_Gun> gunSettingsByBaseName = new Dictionary<string, ItemSetting_Gun>();

                // 第一遍：分类所有资源
                foreach (var go in assets)
                {
                    string goName = go.name;
                    string baseName = ExtractBaseName(goName);
                    
                    // 检查是否是 Buff 预制体
                    Buff buff = go.GetComponent<Buff>();
                    if (buff != null)
                    {
                        string prefix = baseName; // Dragon_Buff -> Dragon
                        buffsByPrefix[prefix] = buff;
                        loadedBuffs[prefix] = buff;
                        ModBehaviour.DevLog("[EquipmentFactory] 发现 Buff: " + goName + " (ID=" + buff.ID + ", Prefix=" + prefix + ")");
                        continue;
                    }
                    
                    // 检查是否是 Projectile（子弹）预制体
                    Projectile projectile = go.GetComponent<Projectile>();
                    if (projectile != null)
                    {
                        string prefix = baseName; // Dragon_Bullet -> Dragon
                        bulletsByPrefix[prefix] = projectile;
                        loadedBullets[prefix] = projectile;
                        ModBehaviour.DevLog("[EquipmentFactory] 发现 Bullet: " + goName + " (Prefix=" + prefix + ")");
                        continue;
                    }

                    // 检查是否是 Item 预制体
                    Item item = go.GetComponent<Item>();
                    if (item != null)
                    {
                        EquipmentType? detectedType = ParseEquipmentTypeFromName(goName);
                        
                        // 检查是否是武器（有 ItemSetting_Gun 组件）
                        ItemSetting_Gun gunSetting = go.GetComponent<ItemSetting_Gun>();
                        if (gunSetting != null)
                        {
                            detectedType = EquipmentType.Gun;
                            gunSettingsByBaseName[baseName] = gunSetting;
                        }
                        
                        itemsByBaseName[baseName] = item;
                        if (detectedType.HasValue)
                        {
                            typesByBaseName[baseName] = detectedType.Value;
                        }
                        
                        string typeStr = detectedType.HasValue ? detectedType.Value.ToString() : "未知";
                        ModBehaviour.DevLog("[EquipmentFactory] 发现 Item: " + goName + " (TypeID=" + item.TypeID + ", Type=" + typeStr + ")");
                        continue;
                    }

                    // 检查是否是 Model 预制体
                    ItemAgent agent = go.GetComponent<ItemAgent>();
                    bool isModel = agent != null || goName.ToLower().EndsWith("_model");
                    
                    if (isModel)
                    {
                        if (agent == null)
                        {
                            // AssetBundle 中缺少 DuckovItemAgent 组件
                            // 创建一个运行时包装器 prefab，而不是直接修改原始对象
                            agent = CreateModelWrapper(go, goName);
                            if (agent == null)
                            {
                                ModBehaviour.DevLog("[EquipmentFactory] 无法为 Model 创建包装器: " + goName);
                                continue;
                            }
                            ModBehaviour.DevLog("[EquipmentFactory] 为 Model 创建了运行时包装器: " + goName);
                        }
                        FixModelLayerAndShader(agent.gameObject);
                        modelsByBaseName[baseName] = agent;
                        
                        EquipmentType? detectedType = ParseEquipmentTypeFromName(goName);
                        if (detectedType.HasValue && !typesByBaseName.ContainsKey(baseName))
                        {
                            typesByBaseName[baseName] = detectedType.Value;
                        }
                        ModBehaviour.DevLog("[EquipmentFactory] 发现 Model: " + goName + " -> BaseName=" + baseName);
                    }
                }

                // 第二遍：处理每个 Item，关联相关资源
                int loadedCount = 0;
                
                foreach (var kvp in itemsByBaseName)
                {
                    string baseName = kvp.Key;
                    Item itemPrefab = kvp.Value;
                    
                    try
                    {
                        // 获取物品类型
                        EquipmentType equipType = EquipmentType.Helmet; // 默认
                        if (typesByBaseName.ContainsKey(baseName))
                        {
                            equipType = typesByBaseName[baseName];
                        }
                        
                        // 查找匹配的 Model
                        ItemAgent modelAgent = null;
                        if (modelsByBaseName.ContainsKey(baseName))
                        {
                            modelAgent = modelsByBaseName[baseName];
                        }

                        // 根据类型处理
                        if (equipType == EquipmentType.Gun)
                        {
                            // 武器处理
                            ProcessGunItem(itemPrefab, baseName, modelAgent, gunSettingsByBaseName, buffsByPrefix, bulletsByPrefix);
                            loadedGuns[itemPrefab.TypeID] = itemPrefab;
                        }
                        else
                        {
                            // 装备处理
                            ProcessEquipmentItem(itemPrefab, baseName, equipType, modelAgent);
                        }

                        // 缓存模型
                        if (modelAgent != null)
                        {
                            loadedModels[itemPrefab.TypeID] = modelAgent;
                        }
                        
                        // 配置龙套装（设置本地化键和属性）
                        DragonSetConfig.TryConfigure(itemPrefab, baseName);

                        // 配置龙王套装（设置本地化键和属性）
                        DragonKingSetConfig.TryConfigure(itemPrefab, baseName);

                        // 配置飞行图腾（设置本地化键和属性）
                        FlightTotemConfig.TryConfigure(itemPrefab, baseName);

                        // 配置逆鳞图腾（设置本地化键和属性）
                        ModBehaviour.TryConfigureReverseScale(itemPrefab, baseName);

                        // 配置龙息武器（配件槽位、弹药类型、耐久度、标签）
                        DragonBreathWeaponConfig.TryConfigure(itemPrefab, baseName);

                        // 注册到游戏物品系统
                        ItemAssetsCollection.AddDynamicEntry(itemPrefab);
                        loadedCount++;

                        ModBehaviour.DevLog("[EquipmentFactory] 成功加载: " + itemPrefab.gameObject.name + 
                            " (TypeID=" + itemPrefab.TypeID + ", Type=" + equipType + ")");
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[EquipmentFactory] 处理 Item 失败: " + itemPrefab.gameObject.name + " - " + e.Message);
                    }
                }

                ModBehaviour.DevLog("[EquipmentFactory] Bundle '" + bundleName + "' 加载完成，共 " + loadedCount + " 个物品");
                return loadedCount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] LoadBundleInternal 出错: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
            // 注意：不要 Unload bundle，因为资源还在使用
        }

        // ========== 装备处理 ==========

        /// <summary>
        /// 处理装备类型物品（头盔、护甲、背包等）
        /// </summary>
        private static void ProcessEquipmentItem(Item itemPrefab, string baseName, EquipmentType equipType, ItemAgent modelAgent)
        {
            string tagName = GetTagName(equipType);
            
            // 添加装备 Tag
            EquipmentHelper.AddTagToItem(itemPrefab, tagName);
            
            // 图腾类型需要额外添加 DontDropOnDeadInSlot 标签（绑定装备）
            if (equipType == EquipmentType.Totem)
            {
                EquipmentHelper.AddTagToItem(itemPrefab, "DontDropOnDeadInSlot");
                ModBehaviour.DevLog("[EquipmentFactory] 为图腾添加绑定装备标签: " + itemPrefab.name);
            }

            // 注入 EquipmentModel（如果 Unity 中未配置）
            if (modelAgent != null && !HasEquipmentModel(itemPrefab))
            {
                InjectEquipmentModel(itemPrefab, modelAgent);
            }
            
            // 为所有装备类型注入 ItemGraphic（备用显示路径，修复假人显示问题）
            // 假人的 CharacterEquipmentController 可能需要 ItemGraphic 作为备用显示路径
            if (modelAgent != null)
            {
                InjectItemGraphicForEquipment(itemPrefab, modelAgent);
            }
        }

        // ========== 武器处理 ==========

        /// <summary>
        /// 处理武器类型物品
        /// </summary>
        private static void ProcessGunItem(
            Item itemPrefab, 
            string baseName, 
            ItemAgent modelAgent,
            Dictionary<string, ItemSetting_Gun> gunSettingsByBaseName,
            Dictionary<string, Buff> buffsByPrefix,
            Dictionary<string, Projectile> bulletsByPrefix)
        {
            // 添加 Gun Tag
            EquipmentHelper.AddTagToItem(itemPrefab, "Gun");
            
            // 获取 ItemSetting_Gun 组件
            ItemSetting_Gun gunSetting = null;
            if (gunSettingsByBaseName.ContainsKey(baseName))
            {
                gunSetting = gunSettingsByBaseName[baseName];
            }
            else
            {
                gunSetting = itemPrefab.GetComponent<ItemSetting_Gun>();
            }
            
            if (gunSetting == null)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 武器缺少 ItemSetting_Gun 组件: " + itemPrefab.name);
                return;
            }
            
            // 提取武器前缀用于匹配 Buff 和 Bullet
            string weaponPrefix = ExtractWeaponPrefix(baseName);
            
            // 自动关联 Buff（如果未配置）
            if (gunSetting.buff == null)
            {
                Buff matchedBuff;
                if (buffsByPrefix.TryGetValue(weaponPrefix, out matchedBuff))
                {
                    InjectGunBuff(gunSetting, matchedBuff);
                    ModBehaviour.DevLog("[EquipmentFactory] 自动关联 Buff: " + weaponPrefix + "_Buff -> " + itemPrefab.name);
                }
            }
            
            // 自动关联 Bullet（如果未配置）
            if (gunSetting.bulletPfb == null)
            {
                Projectile matchedBullet;
                if (bulletsByPrefix.TryGetValue(weaponPrefix, out matchedBullet))
                {
                    InjectGunBullet(gunSetting, matchedBullet);
                    ModBehaviour.DevLog("[EquipmentFactory] 自动关联 Bullet: " + weaponPrefix + "_Bullet -> " + itemPrefab.name);
                }
            }
            
            // 注入模型
            if (modelAgent != null)
            {
                // 注入 EquipmentModel（装备栏显示）
                if (!HasEquipmentModel(itemPrefab))
                {
                    InjectEquipmentModel(itemPrefab, modelAgent);
                }
                
                // 为枪械注入 ItemGraphic（手持和掉落显示的核心依赖）
                // 游戏原版通过 item.ItemGraphic 来：
                //   1. CreateHandheldAgent: IsGun + ItemGraphic != null → ItemAgent_Gun.BuildAgent(ItemGraphic.gameObject)
                //   2. CreatePickupAgent: 默认 PickupAgentPrefab → CreateGraphic() → ItemGraphicInfo.CreateAGraphic(item) → 用 ItemGraphic 实例化3D模型
                InjectItemGraphic(itemPrefab, modelAgent);
            }
        }

        /// <summary>
        /// 注入 Buff 到武器的 ItemSetting_Gun
        /// </summary>
        private static void InjectGunBuff(ItemSetting_Gun gunSetting, Buff buff)
        {
            try
            {
                FieldInfo buffField = typeof(ItemSetting_Gun).GetField("buff", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (buffField != null)
                {
                    buffField.SetValue(gunSetting, buff);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入 Buff 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入 Bullet 到武器的 ItemSetting_Gun
        /// </summary>
        private static void InjectGunBullet(ItemSetting_Gun gunSetting, Projectile bullet)
        {
            try
            {
                FieldInfo bulletField = typeof(ItemSetting_Gun).GetField("bulletPfb", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (bulletField != null)
                {
                    bulletField.SetValue(gunSetting, bullet);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入 Bullet 失败: " + e.Message);
            }
        }

        // ========== 通用辅助方法 ==========

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
                    
                    string key = keyField != null ? keyField.GetValue(entry) as string : null;
                    if (key == "EquipmentModel")
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
        /// 修复 Model 的 Layer 和 Shader
        /// </summary>
        private static void FixModelLayerAndShader(GameObject modelGo)
        {
            try
            {
                SetLayerRecursively(modelGo, CHARACTER_LAYER);
                
                if (gameShader == null)
                {
                    gameShader = Shader.Find(GAME_SHADER_NAME);
                }
                
                if (gameShader != null)
                {
                    var renderers = modelGo.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers)
                    {
                        if (renderer.sharedMaterials != null)
                        {
                            foreach (var mat in renderer.sharedMaterials)
                            {
                                if (mat != null && mat.shader != null)
                                {
                                    string oldShaderName = mat.shader.name;
                                    if (oldShaderName.Contains("Standard") || 
                                        oldShaderName.Contains("Lit") ||
                                        oldShaderName.Contains("Universal"))
                                    {
                                        mat.shader = gameShader;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 修复 Layer/Shader 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 为缺少 DuckovItemAgent 组件的模型创建运行时副本并添加组件
        /// AssetBundle 中的对象是只读的，动态添加的组件无法被正确序列化
        /// 所以需要先实例化创建可修改的运行时副本
        /// </summary>
        private static DuckovItemAgent CreateModelWrapper(GameObject originalModel, string modelName)
        {
            try
            {
                // 实例化 AssetBundle 中的对象，创建一个可修改的运行时副本
                GameObject runtimeCopy = UnityEngine.Object.Instantiate(originalModel);
                runtimeCopy.name = modelName;  // 保持原名
                runtimeCopy.hideFlags = HideFlags.HideAndDontSave;
                
                // 在运行时副本上添加 DuckovItemAgent 组件
                DuckovItemAgent agent = runtimeCopy.AddComponent<DuckovItemAgent>();
                
                // 确保 socketsList 被初始化
                var socketsField = typeof(DuckovItemAgent).GetField("socketsList", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (socketsField != null)
                {
                    var socketsList = socketsField.GetValue(agent);
                    if (socketsList == null)
                    {
                        socketsField.SetValue(agent, new List<Transform>());
                    }
                }
                
                // 设置 Layer
                SetLayerRecursively(runtimeCopy, CHARACTER_LAYER);
                
                // 不要销毁，让它作为 prefab 使用
                UnityEngine.Object.DontDestroyOnLoad(runtimeCopy);
                
                ModBehaviour.DevLog("[EquipmentFactory] 成功创建模型运行时副本: " + modelName);
                return agent;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 创建模型运行时副本失败: " + modelName + " - " + e.Message);
                return null;
            }
        }
        
        /// <summary>
        /// 递归设置 Layer
        /// </summary>
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
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

                var cacheField = agentUtilities.GetType().GetField("hashedAgentsCache", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (cacheField != null)
                {
                    cacheField.SetValue(agentUtilities, null);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入 EquipmentModel 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 为装备类型注入 ItemGraphic（通用版本，用于头盔、护甲等）
        /// 修复假人显示问题：假人的 CharacterEquipmentController 可能需要 ItemGraphic 作为备用显示路径
        /// </summary>
        private static void InjectItemGraphicForEquipment(Item item, ItemAgent modelAgent)
        {
            try
            {
                // 检查已有的 ItemGraphic 是否有效
                ItemGraphicInfo existingGraphic = item.ItemGraphic;
                if (existingGraphic != null)
                {
                    bool isValid = false;
                    try
                    {
                        GameObject existingGo = existingGraphic.gameObject;
                        if (existingGo != null)
                        {
                            Renderer[] renderers = existingGo.GetComponentsInChildren<Renderer>(true);
                            isValid = renderers != null && renderers.Length > 0;
                        }
                    }
                    catch { isValid = false; }
                    
                    if (isValid)
                    {
                        return; // 已有有效的 ItemGraphic，跳过
                    }
                }

                GameObject modelGo = modelAgent.gameObject;

                // 为装备创建通用的 ItemGraphicInfo 组件
                ItemGraphicInfo graphicInfo = modelGo.GetComponent<ItemGraphicInfo>();
                if (graphicInfo == null)
                {
                    graphicInfo = modelGo.AddComponent<ItemGraphicInfo>();
                }

                // 设置 groundPoint（掉落在地面时的定位点）
                if (graphicInfo.groundPoint == null)
                {
                    Transform existingGround = modelGo.transform.Find("GroundPoint");
                    if (existingGround != null)
                    {
                        graphicInfo.groundPoint = existingGround;
                    }
                    else
                    {
                        GameObject groundPointGo = new GameObject("GroundPoint");
                        groundPointGo.transform.SetParent(modelGo.transform);
                        groundPointGo.transform.localPosition = Vector3.zero;
                        groundPointGo.transform.localRotation = Quaternion.identity;
                        groundPointGo.transform.localScale = Vector3.one;
                        graphicInfo.groundPoint = groundPointGo.transform;
                    }
                }

                // 通过反射设置 item.itemGraphic 私有字段
                FieldInfo itemGraphicField = typeof(Item).GetField("itemGraphic",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemGraphicField != null)
                {
                    itemGraphicField.SetValue(item, graphicInfo);
                    ModBehaviour.DevLog("[EquipmentFactory] 成功注入 ItemGraphic (装备): " + item.name);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入装备 ItemGraphic 失败: " + item.name + " - " + e.Message);
            }
        }
        
        /// <summary>
        /// 为枪械注入 ItemGraphic（ItemGraphicInfo_Gun），使游戏原版的手持和掉落显示路径正常工作
        /// 手持路径：CreateHandheldAgent → IsGun + ItemGraphic != null → ItemAgent_Gun.BuildAgent(ItemGraphic.gameObject)
        /// 掉落路径：CreatePickupAgent → 默认 PickupAgentPrefab → CreateGraphic() → ItemGraphicInfo.CreateAGraphic(item)
        /// </summary>
        private static void InjectItemGraphic(Item item, ItemAgent modelAgent)
        {
            try
            {
                // 检查已有的 ItemGraphic 是否有效
                // AssetBundle 中的 ItemGraphic 引用可能在游戏更新后丢失（序列化引用断裂）
                ItemGraphicInfo existingGraphic = item.ItemGraphic;
                if (existingGraphic != null)
                {
                    // 验证已有 ItemGraphic 的 GameObject 是否有效
                    bool isValid = false;
                    try
                    {
                        // 检查 GameObject 是否存在且有实际的渲染内容
                        GameObject existingGo = existingGraphic.gameObject;
                        if (existingGo != null)
                        {
                            // 检查是否有 Renderer 或子对象（空壳 ItemGraphic 无法正常显示）
                            Renderer[] renderers = existingGo.GetComponentsInChildren<Renderer>(true);
                            isValid = renderers != null && renderers.Length > 0;
                            
                            string graphicType = existingGraphic.GetType().Name;
                            int childCount = existingGo.transform.childCount;
                            int rendererCount = renderers != null ? renderers.Length : 0;
                            ModBehaviour.DevLog("[EquipmentFactory] 已有 ItemGraphic 检查: " + item.name + 
                                " type=" + graphicType + 
                                " children=" + childCount + 
                                " renderers=" + rendererCount +
                                " valid=" + isValid);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[EquipmentFactory] 已有 ItemGraphic 检查异常: " + e.Message);
                        isValid = false;
                    }
                    
                    if (isValid)
                    {
                        ModBehaviour.DevLog("[EquipmentFactory] 物品已有有效 ItemGraphic，跳过注入: " + item.name);
                        return;
                    }
                    else
                    {
                        ModBehaviour.DevLog("[EquipmentFactory] 物品已有 ItemGraphic 但无效（无渲染器），将强制替换: " + item.name);
                    }
                }

                GameObject modelGo = modelAgent.gameObject;

                // 在模型 GameObject 上创建 ItemGraphicInfo_Gun 组件
                // ItemGraphicInfo_Gun 继承自 ItemGraphicInfo，是枪械专用的图形信息组件
                ItemGraphicInfo_Gun graphicInfo = modelGo.GetComponent<ItemGraphicInfo_Gun>();
                if (graphicInfo == null)
                {
                    graphicInfo = modelGo.AddComponent<ItemGraphicInfo_Gun>();
                }

                // 设置枪械动画类型为 gun（默认是 normal，会导致 BuildAgent 读取后覆盖为错误值）
                graphicInfo.handAnimationType = HandheldAnimationType.gun;

                // 设置 groundPoint（掉落在地面时的定位点）
                if (graphicInfo.groundPoint == null)
                {
                    Transform existingGround = modelGo.transform.Find("GroundPoint");
                    if (existingGround != null)
                    {
                        graphicInfo.groundPoint = existingGround;
                    }
                    else
                    {
                        GameObject groundPointGo = new GameObject("GroundPoint");
                        groundPointGo.transform.SetParent(modelGo.transform);
                        groundPointGo.transform.localPosition = Vector3.zero;
                        groundPointGo.transform.localRotation = Quaternion.identity;
                        groundPointGo.transform.localScale = Vector3.one;
                        graphicInfo.groundPoint = groundPointGo.transform;
                    }
                }

                // 确保 Sockets 父节点存在，并将 Muzzle/Tec 等关键节点移入其中
                // BuildAgent 会在 Sockets 下查找 Muzzle 和 Tec，如果找不到会创建默认位置 (0,0,0) 的节点
                // 导致枪口火焰出现在手上而不是枪口位置
                Transform socketsParent = modelGo.transform.Find("Sockets");
                if (socketsParent == null)
                {
                    // 创建 Sockets 父节点
                    GameObject socketsGo = new GameObject("Sockets");
                    socketsGo.transform.SetParent(modelGo.transform);
                    socketsGo.transform.localPosition = Vector3.zero;
                    socketsGo.transform.localRotation = Quaternion.identity;
                    socketsGo.transform.localScale = Vector3.one;
                    socketsParent = socketsGo.transform;
                }

                // 将根节点下的 Muzzle、Muzzle2、Tec 移入 Sockets 下
                // 这些节点可能直接在模型根节点下（AssetBundle 导出时的结构）
                string[] socketNodeNames = { "Muzzle", "Muzzle2", "Tec" };
                foreach (string nodeName in socketNodeNames)
                {
                    // 先检查是否已在 Sockets 下
                    Transform existingInSockets = socketsParent.Find(nodeName);
                    if (existingInSockets != null) continue;
                    
                    // 查找根节点下的同名节点并移入 Sockets
                    Transform nodeInRoot = modelGo.transform.Find(nodeName);
                    if (nodeInRoot != null)
                    {
                        nodeInRoot.SetParent(socketsParent);
                        ModBehaviour.DevLog("[EquipmentFactory] 将 " + nodeName + " 移入 Sockets: " + item.name);
                    }
                }

                // 设置 sockets（配件插槽，用于显示枪口、瞄准镜等附件模型）
                if (socketsParent.childCount > 0)
                {
                    var socketTransforms = new List<Transform>();
                    foreach (Transform child in socketsParent)
                    {
                        socketTransforms.Add(child);
                    }
                    graphicInfo.SetSockets(socketTransforms);
                }

                // 通过反射设置 item.itemGraphic 私有字段
                FieldInfo itemGraphicField = typeof(Item).GetField("itemGraphic",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemGraphicField != null)
                {
                    itemGraphicField.SetValue(item, graphicInfo);
                    ModBehaviour.DevLog("[EquipmentFactory] 成功注入 ItemGraphic (ItemGraphicInfo_Gun): " + item.name);
                }
                else
                {
                    ModBehaviour.DevLog("[EquipmentFactory] 未找到 itemGraphic 字段: " + item.name);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入 ItemGraphic 失败: " + item.name + " - " + e.Message);
            }
        }

    }
}
