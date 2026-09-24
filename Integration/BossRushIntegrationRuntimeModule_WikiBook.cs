using System;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;
using Duckov.ItemUsage;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class IntegrationRuntimeModule : BossRushRuntimeModuleBase
    {
        // ============================================================================
        // Wiki Book 配置
        // ============================================================================

        /// <summary>
        /// Wiki Book 物品 TypeID。
        /// 历史教程里曾用 500100 作为临时高位 ID；当前发布契约统一为 500007。
        /// </summary>
        private const int WIKI_BOOK_TYPE_ID = BossRushItemIds.AdventureJournal;

        // 【本文件不保存显示名与描述】
        // 这本书的名字和描述唯一落在 Localization/LocalizationInjector.cs 的
        // WIKI_BOOK_NAME_CN / WIKI_BOOK_DESC_CN（「冒险家日志」/「Adventurer's Journal」），
        // 由 InjectWikiBookLocalization 注入，物品预制体按 displayName 反查这个 key。
        //
        // 这里曾另存一套 WIKI_BOOK_DISPLAY_NAME_CN = "Boss Rush 百科全书" + 同名描述常量
        // 与一对包装属性，全部零调用点——同一物品挂着两套名字，而没生效的那套看起来更像
        // 权威常量，读代码的人会被带偏。已删除，不要再在这里补第二份。

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
        internal void InitializeWikiBookItem()
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
                    ModBehaviour.DevLog("[WikiBook] AssetBundle 加载失败，跳过初始化");
                    return;
                }

                // 配置并注册物品
                if (wikiBookPrefab != null)
                {
                    Item itemPrefab = wikiBookPrefab.GetComponent<Item>();
                    if (itemPrefab != null)
                    {
                        if (itemPrefab.TypeID != WIKI_BOOK_TYPE_ID)
                        {
                            itemPrefab.SetTypeID(WIKI_BOOK_TYPE_ID);
                        }

                        ConfigureWikiBookItem(itemPrefab);
                        ItemAssetsCollection.AddDynamicEntry(itemPrefab);

                        wikiBookTypeId = itemPrefab.TypeID;

                        ModBehaviour.DevLog("[WikiBook] 成功注册 Wiki Book 物品: TypeID=" + itemPrefab.TypeID);
                    }
                    else
                    {
                        ModBehaviour.DevLog("[WikiBook] WikiBook.prefab 上未找到 Item 组件");
                    }
                }

                // 初始化 UI 管理器
                if (wikiUIPrefab != null)
                {
                    WikiUIManager.Instance.SetUIPrefab(wikiUIPrefab);
                    ModBehaviour.DevLog("[WikiBook] Wiki UI Prefab 已设置");
                }

                ModBehaviour.DevLog("[WikiBook] 初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiBook] 初始化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 从 AssetBundle 加载 Wiki 资源
        /// </summary>
        /// <returns>是否加载成功</returns>
        private bool LoadWikiAssetBundle()
        {
            AssetBundle bundle = null;
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "ui", WIKI_BUNDLE_NAME);

                if (!File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[WikiBook] 未找到 AssetBundle: " + bundlePath);
                    return false;
                }

                bundle = ResourceBundleLoader.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[WikiBook] 加载 AssetBundle 失败: " + bundlePath);
                    return false;
                }

                // 获取所有资源名称（用于调试）
                string[] assetNames = bundle.GetAllAssetNames();
                if (assetNames != null)
                {
                    ModBehaviour.DevLog("[WikiBook] AssetBundle 包含 " + assetNames.Length + " 个资源:");
                    foreach (string name in assetNames)
                    {
                        ModBehaviour.DevLog("  - " + name);
                    }
                }

                // 加载所有 GameObject 资源
                UnityEngine.Object[] assets = ResourceBundleLoader.LoadAllAssets<GameObject>(bundle);
                if (assets == null || assets.Length == 0)
                {
                    ModBehaviour.DevLog("[WikiBook] AssetBundle 中未找到任何 GameObject");
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
                        ModBehaviour.DevLog("[WikiBook] 找到 Wiki UI Prefab: " + go.name);
                    }

                    // 查找 Wiki Book Item Prefab
                    if (goName.Contains("wikibook") || goName.Contains("wiki_book"))
                    {
                        wikiBookPrefab = go;
                        ModBehaviour.DevLog("[WikiBook] 找到 Wiki Book Prefab: " + go.name);
                    }
                }

                if (wikiUIPrefab == null)
                {
                    ModBehaviour.DevLog("[WikiBook] 警告：未找到 Wiki UI Prefab");
                }

                if (wikiBookPrefab == null)
                {
                    ModBehaviour.DevLog("[WikiBook] 警告：未找到 Wiki Book Item Prefab");
                }

                return wikiUIPrefab != null || wikiBookPrefab != null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiBook] 加载 AssetBundle 异常: " + e.Message);
                return false;
            }
            finally
            {
                AssetBundleUnloadHelper.TryUnload(bundle, "[WikiBook]");
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

                ModBehaviour.DevLog("[WikiBook] 已设置耐久度: MaxDurability=999, Durability=999");

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
                ModBehaviour.DevLog("[WikiBook] 已添加 WikiBookUsageBehavior");

                // 3. 关联 UsageUtilities 到 Item
                var usageField = typeof(Item).GetField("usageUtilities",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (usageField != null)
                {
                    usageField.SetValue(itemPrefab, usageUtils);
                }

                ModBehaviour.DevLog("[WikiBook] 物品配置完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiBook] 配置物品失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注入 Wiki Book 本地化（委托给 LocalizationInjector）
        /// </summary>
        internal void InjectWikiBookLocalization()
        {
            LocalizationInjector.InjectWikiBookLocalization(wikiBookTypeId);
            ModBehaviour.DevLog("[WikiBook] 本地化注入完成");
        }
    }
}
