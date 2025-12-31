// ============================================================================
// WikiBookItem.cs - Wiki 百科全书物品
// ============================================================================
// 模块说明：
//   管理 Wiki Book 物品的加载、配置和注册，包括：
//   - 从 AssetBundle 加载 Wiki UI 和书物品预制体
//   - 动态添加使用行为（打开 Wiki UI，不消耗物品）
//   - 本地化注入
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Duckov.ItemUsage;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Wiki Book 物品模块
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // Wiki Book 配置
        // ============================================================================
        
        /// <summary>
        /// Wiki Book 物品 TypeID（需要与 Unity 预制体中的 typeID 一致）
        /// </summary>
        private const int WIKI_BOOK_TYPE_ID = 500100;
        
        /// <summary>
        /// Wiki Book 显示名称（中文）
        /// </summary>
        private const string WIKI_BOOK_DISPLAY_NAME_CN = "Boss Rush 百科全书";
        
        /// <summary>
        /// Wiki Book 显示名称（英文）
        /// </summary>
        private const string WIKI_BOOK_DISPLAY_NAME_EN = "Boss Rush Encyclopedia";
        
        /// <summary>
        /// Wiki Book 描述（中文）
        /// </summary>
        private const string WIKI_BOOK_DESCRIPTION_CN = "记载了 Boss Rush 模式的各种知识，包括 Boss 介绍、装备说明、模式攻略等。";
        
        /// <summary>
        /// Wiki Book 描述（英文）
        /// </summary>
        private const string WIKI_BOOK_DESCRIPTION_EN = "A comprehensive guide to Boss Rush mode, including boss introductions, equipment descriptions, and strategy guides.";
        
        /// <summary>
        /// 获取本地化的 Wiki Book 名称
        /// </summary>
        private static string WIKI_BOOK_DISPLAY_NAME { get { return L10n.T(WIKI_BOOK_DISPLAY_NAME_CN, WIKI_BOOK_DISPLAY_NAME_EN); } }
        
        /// <summary>
        /// 获取本地化的 Wiki Book 描述
        /// </summary>
        private static string WIKI_BOOK_DESCRIPTION { get { return L10n.T(WIKI_BOOK_DESCRIPTION_CN, WIKI_BOOK_DESCRIPTION_EN); } }
        
        /// <summary>
        /// Wiki AssetBundle 文件名
        /// </summary>
        private const string WIKI_BUNDLE_NAME = "bossrush_wiki";
        
        // Wiki Book 物品是否已初始化
        private bool wikiBookInitialized = false;
        
        // Wiki Book 物品 TypeID（运行时确认）
        private int wikiBookTypeId = WIKI_BOOK_TYPE_ID;
        
        // 缓存的 Wiki UI Prefab
        private GameObject wikiUIPrefab = null;
        
        // 缓存的 Wiki Book Item Prefab
        private GameObject wikiBookPrefab = null;
        
        // ============================================================================
        // 初始化方法
        // ============================================================================
        
        /// <summary>
        /// 初始化 Wiki Book 物品（从 AssetBundle 加载）
        /// </summary>
        private void InitializeWikiBookItem()
        {
            if (wikiBookInitialized)
            {
                return;
            }
            wikiBookInitialized = true;
            
            try
            {
                // 加载 AssetBundle
                if (!LoadWikiAssetBundle())
                {
                    DevLog("[WikiBook] AssetBundle 加载失败，跳过初始化");
                    return;
                }
                
                // 配置并注册物品
                if (wikiBookPrefab != null)
                {
                    Item itemPrefab = wikiBookPrefab.GetComponent<Item>();
                    if (itemPrefab != null)
                    {
                        ConfigureWikiBookItem(itemPrefab);
                        ItemAssetsCollection.AddDynamicEntry(itemPrefab);
                        
                        if (wikiBookTypeId <= 0 && itemPrefab.TypeID > 0)
                        {
                            wikiBookTypeId = itemPrefab.TypeID;
                        }
                        
                        DevLog("[WikiBook] 成功注册 Wiki Book 物品: TypeID=" + itemPrefab.TypeID);
                    }
                    else
                    {
                        DevLog("[WikiBook] WikiBook.prefab 上未找到 Item 组件");
                    }
                }
                
                // 初始化 UI 管理器
                if (wikiUIPrefab != null)
                {
                    WikiUIManager.Instance.SetUIPrefab(wikiUIPrefab);
                    DevLog("[WikiBook] Wiki UI Prefab 已设置");
                }
                
                DevLog("[WikiBook] 初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[WikiBook] 初始化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 从 AssetBundle 加载 Wiki 资源
        /// </summary>
        /// <returns>是否加载成功</returns>
        private bool LoadWikiAssetBundle()
        {
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "ui", WIKI_BUNDLE_NAME);
                
                if (!File.Exists(bundlePath))
                {
                    DevLog("[WikiBook] 未找到 AssetBundle: " + bundlePath);
                    return false;
                }
                
                // 通过反射加载 AssetBundle（与 BirthdayCakeItem 保持一致）
                Type assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule");
                if (assetBundleType == null)
                {
                    assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine");
                }
                if (assetBundleType == null)
                {
                    DevLog("[WikiBook] 无法找到 AssetBundle 类型");
                    return false;
                }
                
                MethodInfo loadFromFile = assetBundleType.GetMethod("LoadFromFile", new Type[] { typeof(string) });
                if (loadFromFile == null)
                {
                    DevLog("[WikiBook] 未找到 LoadFromFile 方法");
                    return false;
                }
                
                object bundle = loadFromFile.Invoke(null, new object[] { bundlePath });
                if (bundle == null)
                {
                    DevLog("[WikiBook] 加载 AssetBundle 失败: " + bundlePath);
                    return false;
                }
                
                // 获取所有资源名称（用于调试）
                MethodInfo getAllAssetNames = assetBundleType.GetMethod("GetAllAssetNames");
                if (getAllAssetNames != null)
                {
                    string[] assetNames = getAllAssetNames.Invoke(bundle, null) as string[];
                    if (assetNames != null)
                    {
                        DevLog("[WikiBook] AssetBundle 包含 " + assetNames.Length + " 个资源:");
                        foreach (string name in assetNames)
                        {
                            DevLog("  - " + name);
                        }
                    }
                }
                
                // 加载所有 GameObject 资源
                MethodInfo loadAllAssets = assetBundleType.GetMethod("LoadAllAssets", new Type[] { typeof(Type) });
                if (loadAllAssets == null)
                {
                    DevLog("[WikiBook] 未找到 LoadAllAssets 方法");
                    return false;
                }
                
                UnityEngine.Object[] assets = loadAllAssets.Invoke(bundle, new object[] { typeof(GameObject) }) as UnityEngine.Object[];
                if (assets == null || assets.Length == 0)
                {
                    DevLog("[WikiBook] AssetBundle 中未找到任何 GameObject");
                    return false;
                }
                
                // 查找 WikiUI 和 WikiBook prefab
                foreach (UnityEngine.Object obj in assets)
                {
                    GameObject go = obj as GameObject;
                    if (go == null) continue;
                    
                    string goName = go.name.ToLower();
                    
                    // 查找 Wiki UI Prefab
                    if (goName.Contains("wikiui") || goName.Contains("wiki_ui"))
                    {
                        wikiUIPrefab = go;
                        DevLog("[WikiBook] 找到 Wiki UI Prefab: " + go.name);
                    }
                    
                    // 查找 Wiki Book Item Prefab
                    if (goName.Contains("wikibook") || goName.Contains("wiki_book"))
                    {
                        wikiBookPrefab = go;
                        DevLog("[WikiBook] 找到 Wiki Book Prefab: " + go.name);
                    }
                }
                
                if (wikiUIPrefab == null)
                {
                    DevLog("[WikiBook] 警告：未找到 Wiki UI Prefab");
                }
                
                if (wikiBookPrefab == null)
                {
                    DevLog("[WikiBook] 警告：未找到 Wiki Book Item Prefab");
                }
                
                return wikiUIPrefab != null || wikiBookPrefab != null;
            }
            catch (Exception e)
            {
                DevLog("[WikiBook] 加载 AssetBundle 异常: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 配置 Wiki Book 物品（添加使用行为）
        /// </summary>
        private void ConfigureWikiBookItem(Item itemPrefab)
        {
            if (itemPrefab == null) return;
            
            try
            {
                // 0. 设置物品为使用耐久度模式，防止使用后被消耗
                // CA_UseItem.OnFinish() 中：
                // - Stackable 物品会减少 StackCount
                // - UseDurability 物品会检查耐久度（不会立即销毁）
                // - 其他物品会被直接销毁
                // UseDurability 是只读属性，由 MaxDurability > 0 决定
                // 所以我们设置 MaxDurability 和 Durability，使书可以无限使用
                itemPrefab.MaxDurability = 999f;  // 设置极高最大耐久度
                itemPrefab.Durability = 999f;     // 设置极高当前耐久度
                
                DevLog("[WikiBook] 已设置耐久度: MaxDurability=999, Durability=999");
                
                // 1. 添加 UsageUtilities 组件
                UsageUtilities usageUtils = itemPrefab.GetComponent<UsageUtilities>();
                if (usageUtils == null)
                {
                    usageUtils = itemPrefab.gameObject.AddComponent<UsageUtilities>();
                }
                
                // 确保 behaviors 列表存在
                if (usageUtils.behaviors == null)
                {
                    usageUtils.behaviors = new System.Collections.Generic.List<UsageBehavior>();
                }
                
                // 2. 添加 WikiBookUsageBehavior（打开 UI，不消耗物品）
                WikiBookUsageBehavior wikiUsage = itemPrefab.gameObject.AddComponent<WikiBookUsageBehavior>();
                usageUtils.behaviors.Add(wikiUsage);
                DevLog("[WikiBook] 已添加 WikiBookUsageBehavior");
                
                // 3. 关联 UsageUtilities 到 Item
                var usageField = typeof(Item).GetField("usageUtilities", 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (usageField != null)
                {
                    usageField.SetValue(itemPrefab, usageUtils);
                }
                
                DevLog("[WikiBook] 物品配置完成");
            }
            catch (Exception e)
            {
                DevLog("[WikiBook] 配置物品失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入 Wiki Book 本地化（委托给 LocalizationInjector）
        /// </summary>
        private void InjectWikiBookLocalization()
        {
            LocalizationInjector.InjectWikiBookLocalization(wikiBookTypeId);
            DevLog("[WikiBook] 本地化注入完成");
        }
    }
    
    // ============================================================================
    // WikiBookUsageBehavior - Wiki Book 使用行为
    // ============================================================================
    
    /// <summary>
    /// Wiki Book 使用行为：打开 Wiki UI，不消耗物品
    /// </summary>
    public class WikiBookUsageBehavior : UsageBehavior
    {
        /// <summary>
        /// 检查物品是否可以使用
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            // Wiki Book 始终可以使用
            return true;
        }
        
        /// <summary>
        /// 使用物品时调用
        /// </summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 打开 Wiki UI
                WikiUIManager.Instance.OpenUI();
                ModBehaviour.DevLog("[WikiBook] 打开 Wiki UI");
                
                // 注意：这里不调用 Consume() 或任何消耗物品的方法
                // 所以书不会被消耗，可以无限次使用
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiBook] 打开 UI 失败: " + e.Message);
            }
        }
    }
}
