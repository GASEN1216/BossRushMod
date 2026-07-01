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
        Totem,      // 图腾 - 使用 "Totem" Tag
        MeleeWeapon // 近战武器 - 使用 "Weapon" Tag
    }

    /// <summary>
    /// 自定义装备/武器工厂 - 从 AssetBundle 加载装备、武器和Buff
    /// </summary>
    public static partial class EquipmentFactory
    {
        // ========== 缓存字典 ==========

        // 已加载的模型缓存（TypeID -> ItemAgent）
        private static Dictionary<int, ItemAgent> loadedModels = new Dictionary<int, ItemAgent>();

        // 已加载的模型缓存（BaseName -> ItemAgent），支持模型与物品分桶加载
        private static Dictionary<string, ItemAgent> loadedModelsByBaseName =
            new Dictionary<string, ItemAgent>(StringComparer.OrdinalIgnoreCase);

        // 已加载的Buff缓存（基础名 -> Buff预制体）
        private static Dictionary<string, Buff> loadedBuffs = new Dictionary<string, Buff>();

        // 已加载的子弹缓存（基础名 -> Projectile预制体）
        private static Dictionary<string, Projectile> loadedBullets = new Dictionary<string, Projectile>();

        // 已加载的武器缓存（TypeID -> Item）
        private static Dictionary<int, Item> loadedGuns = new Dictionary<int, Item>();

        // 已加载的 bundle 列表（避免重复加载）
        private static HashSet<string> loadedBundles = new HashSet<string>();

        // 自定义近战武器 TypeID 列表（防止被 ItemSetting_Gun 误识别为枪械）
        private static HashSet<int> customMeleeWeaponTypeIds = new HashSet<int>();

        // Mod 目录路径
        private static string modDirectory = null;

        // 装备资源目录（固定路径）
        private const string EQUIPMENT_PATH = "Assets/Equipment";

        // Character Layer 常量（游戏中 Character 层为 9）
        private const int CHARACTER_LAYER = 9;
        private const string HANDHELD_AGENT_KEY = "Handheld";

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
        /// 通过模型基础名获取已加载的 3D 模型
        /// </summary>
        public static bool TryGetLoadedModel(string baseName, out ItemAgent modelAgent)
        {
            modelAgent = null;
            if (string.IsNullOrEmpty(baseName))
            {
                return false;
            }

            return loadedModelsByBaseName.TryGetValue(baseName, out modelAgent);
        }

        /// <summary>
        /// 将已加载的近战模型绑定到物品 prefab，并补齐 Handheld 代理。
        /// </summary>
        public static bool TryBindLoadedMeleeModel(Item itemPrefab, string modelBaseName, string handheldBaseName)
        {
            if (itemPrefab == null)
            {
                return false;
            }

            ItemAgent modelAgent;
            if (!TryGetLoadedModel(modelBaseName, out modelAgent) || modelAgent == null)
            {
                return false;
            }

            loadedModels[itemPrefab.TypeID] = modelAgent;
            InjectItemGraphicForEquipment(itemPrefab, modelAgent, true);
            FinalizeCustomMeleeWeapon(itemPrefab, modelAgent, handheldBaseName);
            return true;
        }

        /// <summary>
        /// 将已加载的装备模型绑定到占位 Item，覆盖克隆源继承的外观。
        /// </summary>
        public static bool TryBindLoadedEquipmentModel(Item itemPrefab, string modelBaseName)
        {
            if (itemPrefab == null)
            {
                return false;
            }

            ItemAgent modelAgent;
            if (!TryGetLoadedModel(modelBaseName, out modelAgent) || modelAgent == null)
            {
                return false;
            }

            loadedModels[itemPrefab.TypeID] = modelAgent;
            SetAgentUtilityPrefab(itemPrefab, "EquipmentModel", modelAgent);
            InjectItemGraphicForEquipment(itemPrefab, modelAgent, true);
            return true;
        }

        /// <summary>
        /// 检查 TypeID 是否是已加载的自定义物品
        /// </summary>
        public static bool IsCustomEquipment(int typeId)
        {
            return loadedModels.ContainsKey(typeId) || loadedGuns.ContainsKey(typeId);
        }

        /// <summary>
        /// 注册自定义近战武器 TypeID（防止被 ItemSetting_Gun 误识别为枪械）
        /// </summary>
        public static void RegisterMeleeWeapon(int typeId)
        {
            customMeleeWeaponTypeIds.Add(typeId);
            ModBehaviour.DevLog("[EquipmentFactory] 已注册自定义近战武器 TypeID: " + typeId);
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
                case EquipmentType.MeleeWeapon: return "Weapon";
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

            // 近战武器类型
            if (nameLower.Contains("_melee_")) return EquipmentType.MeleeWeapon;
            if (nameLower.Contains("_sword_")) return EquipmentType.MeleeWeapon;
            if (nameLower.Contains("_axe_")) return EquipmentType.MeleeWeapon;

            // 图腾类型
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
                            // 如果已注册为近战武器，跳过枪械识别并清除 Gun 组件
                            if (customMeleeWeaponTypeIds.Contains(item.TypeID))
                            {
                                UnityEngine.Object.DestroyImmediate(gunSetting, true);
                                ModBehaviour.DevLog("[EquipmentFactory] TypeID=" + item.TypeID + " 已注册为近战武器，跳过枪械识别并清除 Gun 组件");
                            }
                            else
                            {
                                detectedType = EquipmentType.Gun;
                                gunSettingsByBaseName[baseName] = gunSetting;
                            }
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
                        loadedModelsByBaseName[baseName] = agent;

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
                int standaloneModelCount = 0;

                foreach (var modelEntry in modelsByBaseName)
                {
                    if (!itemsByBaseName.ContainsKey(modelEntry.Key))
                    {
                        standaloneModelCount++;
                    }
                }

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
                        if (equipType == EquipmentType.MeleeWeapon && modelAgent == null)
                        {
                            modelAgent = TryCreateEmbeddedMeleeModel(itemPrefab, baseName);
                            if (modelAgent != null)
                            {
                                modelsByBaseName[baseName] = modelAgent;
                            }
                        }

                        // 根据类型处理
                        if (equipType == EquipmentType.Gun)
                        {
                            // 武器处理
                            DragonKingBossGunConfig.TryConfigure(itemPrefab, baseName);
                            ProcessGunItem(itemPrefab, baseName, modelAgent, gunSettingsByBaseName, buffsByPrefix, bulletsByPrefix);
                            loadedGuns[itemPrefab.TypeID] = itemPrefab;
                        }
                        else if (equipType == EquipmentType.MeleeWeapon)
                        {
                            // 近战武器处理：只添加基础 Tag，具体组件/Stats 由配置器完成
                            EquipmentHelper.AddTagToItem(itemPrefab, "Weapon");
                            if (modelAgent != null)
                            {
                                InjectItemGraphicForEquipment(itemPrefab, modelAgent);
                            }
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

                        // 配置 P1 冰霜/雷霆套装（真实资源主路径）
                        FrostThunderSetConfig.TryConfigure(itemPrefab, baseName);

                        // 配置飞行图腾（设置本地化键和属性）
                        FlightTotemConfig.TryConfigure(itemPrefab, baseName);

                        // 配置逆鳞图腾（设置本地化键和属性）
                        ModBehaviour.TryConfigureReverseScale(itemPrefab, baseName);

                        // 配置龙息武器（配件槽位、弹药类型、耐久度、标签）
                        DragonBreathWeaponConfig.TryConfigure(itemPrefab, baseName);

                        // 配置 P0 新武器（毒蛇匕首 / 召唤法杖 / 能量盾 / 冰霜长矛 / 雷电戒指）
                        // 当 AssetBundle 提供了这五把武器的 prefab 时，让 LoadBundleInternal 也走 WeaponConfig.TryConfigure
                        // 写入 Stats / 标签 / Buff，避免依赖 ItemFactory.loadedItems 这条只有
                        // ItemFactory.LoadBundle 路径才会填充的缓存。
                        ViperDaggerWeaponConfig.TryConfigure(itemPrefab, baseName);
                        SummonStaffWeaponConfig.TryConfigure(itemPrefab, baseName);
                        EnergyShieldWeaponConfig.TryConfigure(itemPrefab, baseName);
                        FrostSpearWeaponConfig.TryConfigure(itemPrefab, baseName);
                        ThunderRingWeaponConfig.TryConfigure(itemPrefab, baseName);

                        // 注册到游戏物品系统
                        if (equipType == EquipmentType.MeleeWeapon)
                        {
                            FinalizeCustomMeleeWeapon(itemPrefab, modelAgent, baseName);
                        }

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

                loadedCount += standaloneModelCount;

                ModBehaviour.DevLog("[EquipmentFactory] Bundle '" + bundleName + "' 加载完成，共 " + loadedCount + " 个条目");
                return loadedCount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] LoadBundleInternal 出错: " + e.Message + "\n" + e.StackTrace);
                return 0;
            }
            // 注意：不要 Unload bundle，因为资源还在使用
        }

    }
}
