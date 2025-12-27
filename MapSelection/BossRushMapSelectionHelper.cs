// ============================================================================
// BossRushMapSelectionHelper.cs - 地图选择 UI 集成
// ============================================================================
// 模块说明：
//   管理 BossRush 与官方 MapSelectionView 的集成，包括：
//   - 创建 BossRush 传送费用（Cost）
//   - 打开带有 BossRush 条目的地图选择 UI
//   - 动态注入 BossRush 目的地条目
//   
// 主要功能：
//   - CreateBossRushCost: 创建 1 张船票的费用配置
//   - ShowBossRushMapSelection: 打开地图选择 UI
//   - InjectBossRushEntry: 注入 BossRush 目的地条目
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Duckov.Economy;
using Duckov.UI;
using Duckov.Scenes;
using TMPro;

namespace BossRush
{
    /// <summary>
    /// BossRush 地图选择 UI 辅助类
    /// 注：地图配置已统一到 ModBehaviour.BossRushMapConfig，此类仅负责 UI 交互
    /// </summary>
    public static class BossRushMapSelectionHelper
    {
        // BossRush 竞技场场景 ID（保留兼容性，从配置系统获取第一个地图）
        public static string BossRushSceneID
        {
            get
            {
                ModBehaviour.BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
                if (configs != null && configs.Length > 0)
                {
                    return configs[0].sceneID;
                }
                return "Level_DemoChallenge_Main";
            }
        }
        
        // BossRush 落点编号（使用默认起点）
        public const int BossRushBeaconIndex = 0;
        
        /// <summary>
        /// 获取当前待处理地图的目标子场景名称（使用统一配置系统）
        /// </summary>
        public static string GetPendingTargetSubSceneName()
        {
            ModBehaviour.BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
            if (pendingMapEntryIndex >= 0 && configs != null && pendingMapEntryIndex < configs.Length)
            {
                // 返回运行时场景名（子场景名）
                return configs[pendingMapEntryIndex].sceneName;
            }
            return null;
        }
        
        /// <summary>
        /// 获取当前待处理地图的主场景名称（用于判断是否需要保持传送标记）
        /// </summary>
        public static string GetPendingMainSceneName()
        {
            ModBehaviour.BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
            if (pendingMapEntryIndex >= 0 && configs != null && pendingMapEntryIndex < configs.Length)
            {
                return configs[pendingMapEntryIndex].sceneID;
            }
            return null;
        }
        
        // 当前选中的地图条目索引（用于传送后处理）
        private static int pendingMapEntryIndex = -1;
        
        // 是否已初始化 - 保留用于未来扩展
        #pragma warning disable CS0414
        private static bool initialized = false;
        #pragma warning restore CS0414
        
        // 动态创建的 BossRush 条目 GameObject 列表
        private static List<GameObject> bossRushEntryObjects = new List<GameObject>();
        
        // 保留兼容性的单个条目引用
        private static GameObject bossRushEntryObject = null;
        
        /// <summary>
        /// 获取 BossRush 船票的 TypeID
        /// </summary>
        public static int GetBossRushTicketTypeId()
        {
            // 通过反射获取 ModBehaviour 中的 bossRushTicketTypeId
            try
            {
                FieldInfo field = typeof(ModBehaviour).GetField("bossRushTicketTypeId", 
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    int typeId = (int)field.GetValue(null);
                    return typeId > 0 ? typeId : 868; // 默认回退到 868
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] GetBossRushTicketTypeId 失败: " + e.Message);
            }
            return 868; // 默认回退值
        }
        
        /// <summary>
        /// 创建 BossRush 传送费用（1 张船票）
        /// </summary>
        public static Cost CreateBossRushCost()
        {
            int ticketTypeId = GetBossRushTicketTypeId();
            
            // 创建 Cost 结构体：0 金钱 + 1 张船票
            // 使用 C# 5 兼容的语法
            Cost cost = new Cost();
            cost.money = 0L;
            cost.items = new Cost.ItemEntry[1];
            cost.items[0].id = ticketTypeId;
            cost.items[0].amount = 1L;
            
            return cost;
        }
        
        /// <summary>
        /// 检查玩家是否有足够的船票
        /// </summary>
        public static bool HasEnoughTickets()
        {
            Cost cost = CreateBossRushCost();
            return cost.Enough;
        }
        
        /// <summary>
        /// 打开带有 BossRush 条目的地图选择 UI
        /// </summary>
        public static void ShowBossRushMapSelection()
        {
            try
            {
                // 设置 BossRush 传送标记，确保场景加载后触发 BossRush 设置逻辑
                SetBossRushArenaPlanned(true);
                
                // 获取 MapSelectionView 实例
                MapSelectionView mapView = MapSelectionView.Instance;
                if (mapView == null)
                {
                    ModBehaviour.DevLog("[BossRush] MapSelectionView.Instance 为 null，无法打开地图选择 UI");
                    // 回退到直接传送
                    FallbackToDirectTeleport();
                    return;
                }
                
                // 注入 BossRush 条目
                bool injected = InjectBossRushEntry(mapView);
                if (!injected)
                {
                    ModBehaviour.DevLog("[BossRush] 无法注入 BossRush 条目，回退到直接传送");
                    FallbackToDirectTeleport();
                    return;
                }
                
                // 打开地图选择 UI
                mapView.Open(null);
                
                // Open() 后验证条目状态
                ModBehaviour.DevLog("[BossRush] Open() 后验证条目状态:");
                MapSelectionEntry[] entriesAfterOpen = mapView.GetComponentsInChildren<MapSelectionEntry>(true);
                foreach (MapSelectionEntry e in entriesAfterOpen)
                {
                    bool isActive = e.gameObject.activeInHierarchy;
                    bool selfActive = e.gameObject.activeSelf;
                    ModBehaviour.DevLog("[BossRush] 条目: " + e.gameObject.name + ", sceneID=" + e.SceneID + ", activeInHierarchy=" + isActive + ", activeSelf=" + selfActive);
                }
                
                // 启动协程监控 UI 关闭，以便恢复隐藏的条目
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod != null)
                {
                    mod.StartCoroutine(WatchMapSelectionViewClose(mapView));
                    
                    // 关键：延迟几帧后再次设置显示名称，防止被 UI 系统的 Refresh() 覆盖
                    mod.StartCoroutine(DelayedRefreshDisplayNames());
                }
                
                ModBehaviour.DevLog("[BossRush] 已打开 MapSelectionView，等待玩家选择");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ShowBossRushMapSelection 失败: " + e.Message);
                FallbackToDirectTeleport();
            }
        }
        
        /// <summary>
        /// 监控 MapSelectionView 关闭，关闭后恢复隐藏的条目
        /// 不再覆盖名称，让它显示场景原本的地名
        /// </summary>
        private static System.Collections.IEnumerator WatchMapSelectionViewClose(MapSelectionView mapView)
        {
            // 等待 UI 关闭（检测 gameObject 是否激活）
            while (mapView != null && mapView.gameObject.activeInHierarchy)
            {
                yield return null;
            }
            
            // UI 已关闭，恢复隐藏的条目
            RestoreHiddenEntries();
            ModBehaviour.DevLog("[BossRush] MapSelectionView 已关闭，已恢复隐藏的条目");
        }
        
        /// <summary>
        /// 延迟后刷新所有 BossRush 条目的显示名称
        /// 防止被 UI 系统的 Refresh() 覆盖
        /// 使用多次刷新机制确保名称正确显示
        /// </summary>
        private static System.Collections.IEnumerator DelayedRefreshDisplayNames()
        {
            // 多次刷新：在不同时机刷新显示名称，确保覆盖 UI 系统的 Refresh()
            // 第一次：等待帧结束后立即刷新
            yield return new WaitForEndOfFrame();
            RefreshAllEntryDisplayNames("帧结束后");
            
            // 第二次：等待 0.1 秒后刷新（覆盖可能的延迟 Refresh）
            yield return new WaitForSeconds(0.1f);
            RefreshAllEntryDisplayNames("0.1秒后");
            
            // 第三次：等待 0.3 秒后刷新（最终保障）
            yield return new WaitForSeconds(0.2f);
            RefreshAllEntryDisplayNames("0.3秒后");
        }
        
        /// <summary>
        /// 刷新所有 BossRush 条目的显示名称
        /// </summary>
        private static void RefreshAllEntryDisplayNames(string debugTag)
        {
            // 获取地图配置
            ModBehaviour.BossRushMapConfig[] mapConfigs = ModBehaviour.GetAllMapConfigs();
            if (mapConfigs == null || bossRushEntryObjects == null)
            {
                return;
            }
            
            // 刷新所有条目的显示名称
            for (int i = 0; i < bossRushEntryObjects.Count && i < mapConfigs.Length; i++)
            {
                GameObject entryObj = bossRushEntryObjects[i];
                if (entryObj == null) continue;
                
                ModBehaviour.BossRushMapConfig mapConfig = mapConfigs[i];
                string displayName = mapConfig.displayName;
                
                // 如果配置的显示名称为空，尝试从场景信息获取
                if (string.IsNullOrEmpty(displayName))
                {
                    SceneInfoEntry sceneInfo = SceneInfoCollection.GetSceneInfo(mapConfig.sceneID);
                    displayName = sceneInfo != null ? sceneInfo.DisplayName : "未知地图";
                }
                
                SetEntryDisplayNameDirect(entryObj, displayName);
                ModBehaviour.DevLog("[BossRush] 延迟刷新显示名称(" + debugTag + "): " + displayName + " (index=" + i + ")");
            }
        }

        
        // 存储被隐藏的原有条目，以便恢复
        private static List<GameObject> hiddenEntries = new List<GameObject>();
        
        /// <summary>
        /// 获取待处理的自定义传送位置（场景加载后使用，使用统一配置系统）
        /// </summary>
        public static Vector3? GetPendingCustomPosition()
        {
            ModBehaviour.BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
            if (pendingMapEntryIndex >= 0 && configs != null && pendingMapEntryIndex < configs.Length)
            {
                return configs[pendingMapEntryIndex].customSpawnPos;
            }
            return null;
        }
        
        /// <summary>
        /// 清除待处理的地图条目索引
        /// </summary>
        public static void ClearPendingMapEntry()
        {
            pendingMapEntryIndex = -1;
        }
        
        /// <summary>
        /// 注入 BossRush 目的地条目到 MapSelectionView（支持多个地图）
        /// </summary>
        private static bool InjectBossRushEntry(MapSelectionView mapView)
        {
            try
            {
                // 查找 MapSelectionView 中的条目容器
                Transform viewTransform = mapView.transform;
                
                // 查找现有的 MapSelectionEntry 作为模板
                MapSelectionEntry[] existingEntries = viewTransform.GetComponentsInChildren<MapSelectionEntry>(true);
                
                if (existingEntries == null || existingEntries.Length == 0)
                {
                    ModBehaviour.DevLog("[BossRush] MapSelectionView 中未找到任何 MapSelectionEntry，无法注入");
                    return false;
                }
                
                // 清空之前隐藏的条目列表和创建的条目列表
                hiddenEntries.Clear();
                foreach (GameObject obj in bossRushEntryObjects)
                {
                    if (obj != null)
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                }
                bossRushEntryObjects.Clear();
                
                // 隐藏所有原有条目，保存模板
                MapSelectionEntry templateEntry = null;
                foreach (MapSelectionEntry entry in existingEntries)
                {
                    if (entry == null) continue;
                    
                    // 检查是否是我们之前创建的 BossRush 条目
                    bool isBossRushEntry = entry.gameObject.name.StartsWith("BossRush_");
                    
                    if (!isBossRushEntry)
                    {
                        // 保存模板（用于克隆）
                        if (templateEntry == null)
                        {
                            templateEntry = entry;
                        }
                        // 隐藏原有条目
                        entry.gameObject.SetActive(false);
                        hiddenEntries.Add(entry.gameObject);
                    }
                    else
                    {
                        // 销毁之前创建的 BossRush 条目
                        UnityEngine.Object.Destroy(entry.gameObject);
                    }
                }
                
                // 没有可用的模板
                if (templateEntry == null)
                {
                    ModBehaviour.DevLog("[BossRush] 没有可用的模板 MapSelectionEntry");
                    return false;
                }
                
                // 使用统一配置系统获取地图列表
                ModBehaviour.BossRushMapConfig[] mapConfigs = ModBehaviour.GetAllMapConfigs();
                if (mapConfigs == null || mapConfigs.Length == 0)
                {
                    ModBehaviour.DevLog("[BossRush] 没有可用的地图配置");
                    return false;
                }
                
                // 为每个地图条目创建 UI 条目
                Transform contentParent = templateEntry.transform.parent;
                for (int i = 0; i < mapConfigs.Length; i++)
                {
                    ModBehaviour.BossRushMapConfig mapConfig = mapConfigs[i];
                    
                    // 克隆模板
                    GameObject cloned = UnityEngine.Object.Instantiate(templateEntry.gameObject, contentParent);
                    cloned.name = "BossRush_MapSelectionEntry_" + i;
                    cloned.SetActive(true);
                    
                    // 获取克隆的 MapSelectionEntry 组件
                    MapSelectionEntry uiEntry = cloned.GetComponent<MapSelectionEntry>();
                    if (uiEntry == null)
                    {
                        ModBehaviour.DevLog("[BossRush] 克隆的对象上没有 MapSelectionEntry 组件");
                        UnityEngine.Object.Destroy(cloned);
                        continue;
                    }
                    
                    // 配置条目字段（使用统一配置）
                    ConfigureBossRushEntryWithMapConfig(uiEntry, mapConfig, i);
                    
                    // 调用 Setup 方法关联到 MapSelectionView
                    uiEntry.Setup(mapView);
                    
                    // 设置显示名称
                    SceneInfoEntry sceneInfo = SceneInfoCollection.GetSceneInfo(mapConfig.sceneID);
                    string displayName = !string.IsNullOrEmpty(mapConfig.displayName) ? mapConfig.displayName : 
                                         (sceneInfo != null ? sceneInfo.DisplayName : "未知地图");
                    SetEntryDisplayNameDirect(cloned, displayName);
                    ModBehaviour.DevLog("[BossRush] 创建地图条目: " + displayName + " (sceneID=" + mapConfig.sceneID + ")");
                    
                    // 更新缩略图（如果有自定义预览图）
                    if (!string.IsNullOrEmpty(mapConfig.previewImageName))
                    {
                        UpdateEntryThumbnailWithImage(uiEntry, mapConfig.previewImageName);
                    }
                    
                    // 保存引用
                    bossRushEntryObjects.Add(cloned);
                    
                    // 第一个条目保存到兼容性引用
                    if (i == 0)
                    {
                        bossRushEntryObject = cloned;
                    }
                }
                
                // 将所有 BossRush 条目移到最前面（按顺序）
                for (int i = bossRushEntryObjects.Count - 1; i >= 0; i--)
                {
                    if (bossRushEntryObjects[i] != null)
                    {
                        bossRushEntryObjects[i].transform.SetAsFirstSibling();
                    }
                }
                
                // 验证所有条目的状态
                MapSelectionEntry[] allEntries = mapView.GetComponentsInChildren<MapSelectionEntry>(true);
                ModBehaviour.DevLog("[BossRush] MapSelectionView 中共有 " + allEntries.Length + " 个条目");
                foreach (MapSelectionEntry e in allEntries)
                {
                    bool isActive = e.gameObject.activeInHierarchy;
                    int siblingIndex = e.transform.GetSiblingIndex();
                    ModBehaviour.DevLog("[BossRush] 条目: " + e.gameObject.name + ", sceneID=" + e.SceneID + ", active=" + isActive + ", siblingIndex=" + siblingIndex);
                }
                
                ModBehaviour.DevLog("[BossRush] 成功注入 " + bossRushEntryObjects.Count + " 个 BossRush 条目，已隐藏 " + hiddenEntries.Count + " 个原有条目");
                return bossRushEntryObjects.Count > 0;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] InjectBossRushEntry 失败: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }
        
        /// <summary>
        /// 配置 BossRush 条目的字段（使用统一的 BossRushMapConfig 配置）
        /// </summary>
        private static void ConfigureBossRushEntryWithMapConfig(MapSelectionEntry entry, ModBehaviour.BossRushMapConfig mapConfig, int entryIndex)
        {
            try
            {
                // 使用反射设置私有字段
                Type entryType = typeof(MapSelectionEntry);
                
                // 设置 sceneID（使用加载用场景ID）
                FieldInfo sceneIdField = entryType.GetField("sceneID", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sceneIdField != null)
                {
                    sceneIdField.SetValue(entry, mapConfig.sceneID);
                }
                
                // 设置 beaconIndex
                FieldInfo beaconField = entryType.GetField("beaconIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                if (beaconField != null)
                {
                    beaconField.SetValue(entry, mapConfig.beaconIndex);
                }
                
                // 设置 cost
                FieldInfo costField = entryType.GetField("cost", BindingFlags.NonPublic | BindingFlags.Instance);
                if (costField != null)
                {
                    costField.SetValue(entry, CreateBossRushCost());
                }
                
                // 清除 conditions（无解锁条件）
                FieldInfo conditionsField = entryType.GetField("conditions", BindingFlags.NonPublic | BindingFlags.Instance);
                if (conditionsField != null)
                {
                    conditionsField.SetValue(entry, null);
                }
                
                // 添加点击事件监听，记录选中的地图索引
                BossRushMapEntryClickHandler clickHandler = entry.gameObject.GetComponent<BossRushMapEntryClickHandler>();
                if (clickHandler == null)
                {
                    clickHandler = entry.gameObject.AddComponent<BossRushMapEntryClickHandler>();
                }
                clickHandler.entryIndex = entryIndex;
                
                ModBehaviour.DevLog("[BossRush] 已配置 BossRush 条目: sceneID=" + mapConfig.sceneID + ", beaconIndex=" + mapConfig.beaconIndex + ", entryIndex=" + entryIndex);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ConfigureBossRushEntryWithMapConfig 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 配置 BossRush 条目的字段（旧版方法，保留兼容性）
        /// </summary>
        [System.Obsolete("请使用 ConfigureBossRushEntryWithMapConfig 代替")]
        private static void ConfigureBossRushEntryWithMapEntry(MapSelectionEntry entry, object mapEntry, int entryIndex)
        {
            // 旧方法已废弃，不再使用
            ModBehaviour.DevLog("[BossRush] ConfigureBossRushEntryWithMapEntry 已废弃，请使用 ConfigureBossRushEntryWithMapConfig");
        }
        
        /// <summary>
        /// 配置 BossRush 条目的字段（原始方法，保留兼容性）
        /// </summary>
        private static void ConfigureBossRushEntry_Legacy(MapSelectionEntry entry)
        {
            try
            {
                // 使用反射设置私有字段
                Type entryType = typeof(MapSelectionEntry);
                
                // 设置 sceneID
                FieldInfo sceneIdField = entryType.GetField("sceneID", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sceneIdField != null)
                {
                    sceneIdField.SetValue(entry, BossRushSceneID);
                }
                
                // 设置 beaconIndex
                FieldInfo beaconField = entryType.GetField("beaconIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                if (beaconField != null)
                {
                    beaconField.SetValue(entry, BossRushBeaconIndex);
                }
                
                // 设置 cost
                FieldInfo costField = entryType.GetField("cost", BindingFlags.NonPublic | BindingFlags.Instance);
                if (costField != null)
                {
                    costField.SetValue(entry, CreateBossRushCost());
                }
                
                // 清除 conditions（无解锁条件）
                FieldInfo conditionsField = entryType.GetField("conditions", BindingFlags.NonPublic | BindingFlags.Instance);
                if (conditionsField != null)
                {
                    conditionsField.SetValue(entry, null);
                }
                
                ModBehaviour.DevLog("[BossRush] 已配置 BossRush 条目: sceneID=" + BossRushSceneID + ", beaconIndex=" + BossRushBeaconIndex);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ConfigureBossRushEntry_Legacy 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 设置待处理的地图条目索引（由点击处理器调用）
        /// </summary>
        public static void SetPendingMapEntryIndex(int index)
        {
            pendingMapEntryIndex = index;
            ModBehaviour.DevLog("[BossRush] 设置待处理地图条目索引: " + index);
        }
        
        /// <summary>
        /// 更新条目缩略图（使用指定的图片文件名）
        /// </summary>
        private static void UpdateEntryThumbnailWithImage(MapSelectionEntry entry, string imageName)
        {
            try
            {
                Sprite sprite = LoadCustomBackgroundSpriteByName(imageName);
                if (sprite == null) return;
                
                // 查找条目 GameObject 内部的所有 Image 组件
                Image[] images = entry.GetComponentsInChildren<Image>(true);
                
                foreach (Image img in images)
                {
                    // 只更新 sprite 名称以 "Map_" 开头的 Image（这些是地图预览图）
                    if (img.sprite != null && img.sprite.name.StartsWith("Map_"))
                    {
                        img.sprite = sprite;
                        img.SetAllDirty();
                    }
                }
                
                // 设置 fullScreenImage 字段
                FieldInfo imageField = typeof(MapSelectionEntry).GetField("fullScreenImage", BindingFlags.NonPublic | BindingFlags.Instance);
                if (imageField != null)
                {
                    imageField.SetValue(entry, sprite);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] UpdateEntryThumbnailWithImage 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 根据文件名加载自定义背景图片
        /// </summary>
        private static Sprite LoadCustomBackgroundSpriteByName(string imageName)
        {
            try
            {
                string modPath = GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;
                
                // 尝试 Assets 子目录
                string imagePath = System.IO.Path.Combine(modPath, "Assets", imageName);
                if (!System.IO.File.Exists(imagePath))
                {
                    // 回退到根目录
                    imagePath = System.IO.Path.Combine(modPath, imageName);
                    if (!System.IO.File.Exists(imagePath))
                    {
                        return null;
                    }
                }
                
                byte[] imageData = System.IO.File.ReadAllBytes(imagePath);
                Texture2D texture = new Texture2D(2, 2);
                if (ImageConversion.LoadImage(texture, imageData))
                {
                    return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] LoadCustomBackgroundSpriteByName 失败: " + e.Message);
            }
            return null;
        }
        
        // 缓存的自定义背景图片
        private static Sprite customBackgroundSprite = null;
        
        /// <summary>
        /// 配置 BossRush 条目的字段
        /// </summary>
        private static void ConfigureBossRushEntry(MapSelectionEntry entry)
        {
            try
            {
                // 先检查场景信息是否存在
                SceneInfoEntry sceneInfo = SceneInfoCollection.GetSceneInfo(BossRushSceneID);
                if (sceneInfo != null)
                {
                    ModBehaviour.DevLog("[BossRush] 场景信息存在: ID=" + sceneInfo.ID + ", DisplayName=" + sceneInfo.DisplayName);
                }
                else
                {
                    ModBehaviour.DevLog("[BossRush] 场景信息不存在: " + BossRushSceneID + "，Refresh() 可能无法正确显示名称");
                }
                
                // 使用反射设置私有字段
                Type entryType = typeof(MapSelectionEntry);
                
                // 设置 sceneID
                FieldInfo sceneIdField = entryType.GetField("sceneID", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sceneIdField != null)
                {
                    sceneIdField.SetValue(entry, BossRushSceneID);
                }
                
                // 设置 beaconIndex
                FieldInfo beaconField = entryType.GetField("beaconIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                if (beaconField != null)
                {
                    beaconField.SetValue(entry, BossRushBeaconIndex);
                }
                
                // 设置 cost
                FieldInfo costField = entryType.GetField("cost", BindingFlags.NonPublic | BindingFlags.Instance);
                if (costField != null)
                {
                    costField.SetValue(entry, CreateBossRushCost());
                }
                
                // 清除 conditions（无解锁条件）
                FieldInfo conditionsField = entryType.GetField("conditions", BindingFlags.NonPublic | BindingFlags.Instance);
                if (conditionsField != null)
                {
                    conditionsField.SetValue(entry, null);
                }
                
                // 注意：这里不设置显示名称，因为 Setup() 会调用 Refresh() 覆盖
                // 显示名称将在 Setup() 后通过 SetEntryDisplayName() 设置
                
                // 设置自定义背景图片
                SetEntryBackgroundImage(entry);
                
                ModBehaviour.DevLog("[BossRush] 已配置 BossRush 条目: sceneID=" + BossRushSceneID + ", beaconIndex=" + BossRushBeaconIndex);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ConfigureBossRushEntry 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 设置条目的背景图片
        /// </summary>
        private static void SetEntryBackgroundImage(MapSelectionEntry entry)
        {
            try
            {
                // 加载自定义背景图片（如果还没加载）
                if (customBackgroundSprite == null)
                {
                    customBackgroundSprite = LoadCustomBackgroundSprite();
                }
                
                if (customBackgroundSprite != null)
                {
                    // 设置 fullScreenImage 字段（MapSelectionEntry 的私有字段）
                    FieldInfo imageField = typeof(MapSelectionEntry).GetField("fullScreenImage", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (imageField != null)
                    {
                        imageField.SetValue(entry, customBackgroundSprite);
                        ModBehaviour.DevLog("[BossRush] 已设置 MapSelectionEntry.fullScreenImage 字段");
                    }
                    else
                    {
                        ModBehaviour.DevLog("[BossRush] 未找到 fullScreenImage 字段");
                    }
                    
                    // 尝试查找并设置 MapSelectionView 中的背景 Image 组件
                    // fullScreenImage 可能只是数据字段，实际显示可能在 MapSelectionView 的某个 Image 组件上
                    MapSelectionView mapView = MapSelectionView.Instance;
                    if (mapView != null)
                    {
                        // 尝试查找名为 "Background" 或 "FullScreenImage" 的 Image 组件
                        Image[] images = mapView.GetComponentsInChildren<Image>(true);
                        foreach (Image img in images)
                        {
                            string imgName = img.gameObject.name.ToLower();
                            if (imgName.Contains("background") || imgName.Contains("fullscreen") || imgName.Contains("preview"))
                            {
                                img.sprite = customBackgroundSprite;
                                ModBehaviour.DevLog("[BossRush] 已设置 Image 组件背景: " + img.gameObject.name);
                            }
                        }
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[BossRush] 自定义背景图片为 null");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] SetEntryBackgroundImage 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 加载自定义背景图片
        /// 尝试从 Mod 目录加载 demo-preview.png
        /// </summary>
        private static Sprite LoadCustomBackgroundSprite()
        {
            try
            {
                // 获取 Mod 目录路径
                string modPath = GetModPath();
                if (string.IsNullOrEmpty(modPath))
                {
                    ModBehaviour.DevLog("[BossRush] 无法获取 Mod 路径");
                    return null;
                }
                
                // 尝试加载背景图片（先尝试 Assets 子目录，再尝试根目录）
                string imagePath = System.IO.Path.Combine(modPath, "Assets", "demo-preview.png");
                if (!System.IO.File.Exists(imagePath))
                {
                    // 回退到根目录
                    imagePath = System.IO.Path.Combine(modPath, "demo-preview.png");
                    if (!System.IO.File.Exists(imagePath))
                    {
                        ModBehaviour.DevLog("[BossRush] 背景图片不存在: " + imagePath);
                        return null;
                    }
                }
                
                // 读取图片数据
                byte[] imageData = System.IO.File.ReadAllBytes(imagePath);
                
                // 创建 Texture2D 并加载图片
                Texture2D texture = new Texture2D(2, 2);
                if (ImageConversion.LoadImage(texture, imageData))
                {
                    // 创建 Sprite
                    Sprite sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f)
                    );
                    ModBehaviour.DevLog("[BossRush] 成功加载背景图片: " + imagePath);
                    return sprite;
                }
                else
                {
                    ModBehaviour.DevLog("[BossRush] 无法解析图片数据");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] LoadCustomBackgroundSprite 失败: " + e.Message);
            }
            return null;
        }
        
        /// <summary>
        /// 获取 Mod 目录路径
        /// </summary>
        private static string GetModPath()
        {
            try
            {
                // 通过 ModBehaviour.info.path 获取 Mod 路径
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod != null)
                {
                    // 获取 info 属性（类型为 ModInfo）
                    PropertyInfo infoProp = typeof(Duckov.Modding.ModBehaviour).GetProperty("info");
                    if (infoProp != null)
                    {
                        object infoObj = infoProp.GetValue(mod, null);
                        if (infoObj != null)
                        {
                            // 获取 ModInfo.path 字段
                            FieldInfo pathField = infoObj.GetType().GetField("path");
                            if (pathField != null)
                            {
                                string path = pathField.GetValue(infoObj) as string;
                                ModBehaviour.DevLog("[BossRush] Mod 路径: " + path);
                                return path;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] GetModPath 失败: " + e.Message);
            }
            return null;
        }
        
        /// <summary>
        /// 更新条目内部的缩略图 Image
        /// 只更新 sprite 名称包含 "Map_" 的 Image 组件（这些是地图预览图）
        /// </summary>
        private static void UpdateEntryThumbnail(MapSelectionEntry entry)
        {
            try
            {
                if (customBackgroundSprite == null)
                {
                    customBackgroundSprite = LoadCustomBackgroundSprite();
                }
                
                if (customBackgroundSprite == null)
                {
                    ModBehaviour.DevLog("[BossRush] 无法加载自定义背景图片");
                    return;
                }
                
                // 查找条目 GameObject 内部的所有 Image 组件
                Image[] images = entry.GetComponentsInChildren<Image>(true);
                int updatedCount = 0;
                
                ModBehaviour.DevLog("[BossRush] 条目层级路径: " + GetHierarchyPath(entry.transform));
                ModBehaviour.DevLog("[BossRush] 条目内部找到 " + images.Length + " 个 Image 组件");
                
                foreach (Image img in images)
                {
                    string spriteName = img.sprite != null ? img.sprite.name : "null";
                    string hierarchyPath = GetHierarchyPath(img.transform);
                    
                    // 只更新 sprite 名称以 "Map_" 开头的 Image（这些是地图预览图）
                    if (img.sprite != null && img.sprite.name.StartsWith("Map_"))
                    {
                        ModBehaviour.DevLog("[BossRush] 更新地图预览图: " + hierarchyPath + ", 原sprite=" + spriteName);
                        img.sprite = customBackgroundSprite;
                        // 强制刷新 Image 组件
                        img.SetAllDirty();
                        updatedCount++;
                    }
                }
                
                ModBehaviour.DevLog("[BossRush] 已更新 " + updatedCount + " 个地图预览图");
                
                // 强制刷新整个条目的布局
                LayoutRebuilder.ForceRebuildLayoutImmediate(entry.transform as RectTransform);
                
                // 强制刷新 Canvas
                Canvas.ForceUpdateCanvases();
                
                // 验证更新是否成功
                foreach (Image img in images)
                {
                    if (img.sprite != null)
                    {
                        ModBehaviour.DevLog("[BossRush] 验证 Image: " + img.gameObject.name + ", sprite=" + img.sprite.name);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] UpdateEntryThumbnail 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取 Transform 的层级路径
        /// </summary>
        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
        
        /// <summary>
        /// 直接设置克隆对象内部的显示名称（不依赖 SerializeField 绑定）
        /// </summary>
        private static void SetEntryDisplayNameDirect(GameObject clonedObject, string displayName)
        {
            try
            {
                // 直接查找克隆对象内部的所有 TextMeshProUGUI 组件
                TextMeshProUGUI[] textComps = clonedObject.GetComponentsInChildren<TextMeshProUGUI>(true);
                ModBehaviour.DevLog("[BossRush] 克隆对象内部找到 " + textComps.Length + " 个 TextMeshProUGUI 组件");
                
                bool found = false;
                foreach (TextMeshProUGUI tmp in textComps)
                {
                    string objName = tmp.gameObject.name;
                    string hierarchyPath = GetHierarchyPath(tmp.transform);
                    ModBehaviour.DevLog("[BossRush] TextMeshProUGUI: " + objName + ", text=" + tmp.text + ", path=" + hierarchyPath);
                    
                    // 查找名为 "Text_MapName" 的组件（这是显示名称的组件）
                    if (objName == "Text_MapName")
                    {
                        ModBehaviour.DevLog("[BossRush] 找到 Text_MapName，设置显示名称: " + displayName);
                        tmp.text = displayName;
                        found = true;
                    }
                }
                
                if (!found)
                {
                    ModBehaviour.DevLog("[BossRush] 未找到 Text_MapName 组件，尝试查找包含 'name' 的组件");
                    // 回退：查找名称包含 "name" 的组件
                    foreach (TextMeshProUGUI tmp in textComps)
                    {
                        string objName = tmp.gameObject.name.ToLower();
                        if (objName.Contains("name") || objName.Contains("title") || objName.Contains("display"))
                        {
                            ModBehaviour.DevLog("[BossRush] 回退设置 TextMeshProUGUI: " + tmp.gameObject.name + " -> " + displayName);
                            tmp.text = displayName;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] SetEntryDisplayNameDirect 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 设置条目的显示名称（通过 MapSelectionEntry 组件）
        /// </summary>
        private static void SetEntryDisplayName(MapSelectionEntry entry, string displayName)
        {
            try
            {
                // 方法1：通过反射获取 displayNameText 字段
                FieldInfo displayNameField = typeof(MapSelectionEntry).GetField("displayNameText", BindingFlags.NonPublic | BindingFlags.Instance);
                if (displayNameField != null)
                {
                    TextMeshProUGUI textComp = displayNameField.GetValue(entry) as TextMeshProUGUI;
                    if (textComp != null)
                    {
                        ModBehaviour.DevLog("[BossRush] 通过反射设置显示名称: " + displayName + ", 组件路径: " + GetHierarchyPath(textComp.transform));
                        textComp.text = displayName;
                    }
                    else
                    {
                        ModBehaviour.DevLog("[BossRush] displayNameText 字段为 null，尝试查找子对象中的 TextMeshProUGUI");
                    }
                }
                
                // 方法2：直接查找条目内部的 TextMeshProUGUI 组件
                // 通常显示名称的 TextMeshProUGUI 组件名称包含 "Name" 或 "Title"
                TextMeshProUGUI[] textComps = entry.GetComponentsInChildren<TextMeshProUGUI>(true);
                ModBehaviour.DevLog("[BossRush] 条目内部找到 " + textComps.Length + " 个 TextMeshProUGUI 组件");
                
                foreach (TextMeshProUGUI tmp in textComps)
                {
                    string objName = tmp.gameObject.name.ToLower();
                    ModBehaviour.DevLog("[BossRush] TextMeshProUGUI: " + tmp.gameObject.name + ", text=" + tmp.text);
                    
                    // 查找可能是显示名称的组件
                    if (objName.Contains("name") || objName.Contains("title") || objName.Contains("display"))
                    {
                        ModBehaviour.DevLog("[BossRush] 更新 TextMeshProUGUI: " + tmp.gameObject.name + " -> " + displayName);
                        tmp.text = displayName;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] SetEntryDisplayName 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 验证条目的显示名称是否设置成功
        /// </summary>
        private static void VerifyEntryDisplayName(MapSelectionEntry entry)
        {
            try
            {
                FieldInfo displayNameField = typeof(MapSelectionEntry).GetField("displayNameText", BindingFlags.NonPublic | BindingFlags.Instance);
                if (displayNameField != null)
                {
                    TextMeshProUGUI textComp = displayNameField.GetValue(entry) as TextMeshProUGUI;
                    if (textComp != null)
                    {
                        ModBehaviour.DevLog("[BossRush] 验证显示名称: " + textComp.text + ", 组件路径: " + GetHierarchyPath(textComp.transform));
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] VerifyEntryDisplayName 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 重新绑定克隆后的 SerializeField 字段
        /// 克隆时 SerializeField 字段仍然引用模板的组件，需要重新绑定到克隆后的组件
        /// </summary>
        private static void RebindSerializedFields(MapSelectionEntry entry, GameObject clonedObject)
        {
            try
            {
                Type entryType = typeof(MapSelectionEntry);
                
                // 重新绑定 displayNameText
                // 查找克隆对象内部名为 "Text_MapName" 的 TextMeshProUGUI 组件
                TextMeshProUGUI[] textComps = clonedObject.GetComponentsInChildren<TextMeshProUGUI>(true);
                TextMeshProUGUI displayNameComp = null;
                
                foreach (TextMeshProUGUI tmp in textComps)
                {
                    // 根据日志，显示名称组件的名字是 "Text_MapName"
                    if (tmp.gameObject.name == "Text_MapName")
                    {
                        displayNameComp = tmp;
                        break;
                    }
                }
                
                if (displayNameComp != null)
                {
                    FieldInfo displayNameField = entryType.GetField("displayNameText", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (displayNameField != null)
                    {
                        // 检查当前绑定的组件路径
                        TextMeshProUGUI oldComp = displayNameField.GetValue(entry) as TextMeshProUGUI;
                        if (oldComp != null)
                        {
                            ModBehaviour.DevLog("[BossRush] 原 displayNameText 绑定: " + GetHierarchyPath(oldComp.transform));
                        }
                        
                        // 重新绑定到克隆后的组件
                        displayNameField.SetValue(entry, displayNameComp);
                        ModBehaviour.DevLog("[BossRush] 已重新绑定 displayNameText: " + GetHierarchyPath(displayNameComp.transform));
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[BossRush] 未找到 Text_MapName 组件，无法重新绑定 displayNameText");
                }
                
                // 重新绑定 costDisplay
                CostDisplay[] costDisplays = clonedObject.GetComponentsInChildren<CostDisplay>(true);
                if (costDisplays.Length > 0)
                {
                    FieldInfo costDisplayField = entryType.GetField("costDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (costDisplayField != null)
                    {
                        costDisplayField.SetValue(entry, costDisplays[0]);
                        ModBehaviour.DevLog("[BossRush] 已重新绑定 costDisplay");
                    }
                }
                
                // 重新绑定 lockedIndicator
                // 查找名为 "LockedIndicator" 的 GameObject
                Transform lockedIndicator = clonedObject.transform.Find("LockedIndicator");
                if (lockedIndicator == null)
                {
                    // 递归查找
                    foreach (Transform child in clonedObject.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == "LockedIndicator")
                        {
                            lockedIndicator = child;
                            break;
                        }
                    }
                }
                
                if (lockedIndicator != null)
                {
                    FieldInfo lockedField = entryType.GetField("lockedIndicator", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (lockedField != null)
                    {
                        lockedField.SetValue(entry, lockedIndicator.gameObject);
                        ModBehaviour.DevLog("[BossRush] 已重新绑定 lockedIndicator");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] RebindSerializedFields 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 更新现有条目的 Cost
        /// </summary>
        private static void UpdateEntryCost(MapSelectionEntry entry, Cost newCost)
        {
            try
            {
                FieldInfo costField = typeof(MapSelectionEntry).GetField("cost", BindingFlags.NonPublic | BindingFlags.Instance);
                if (costField != null)
                {
                    costField.SetValue(entry, newCost);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] UpdateEntryCost 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 设置 BossRush 传送标记
        /// </summary>
        private static void SetBossRushArenaPlanned(bool planned)
        {
            try
            {
                FieldInfo field = typeof(ModBehaviour).GetField("bossRushArenaPlanned", 
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    field.SetValue(null, planned);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] SetBossRushArenaPlanned 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 回退到直接传送（当 MapSelectionView 不可用时）
        /// </summary>
        private static void FallbackToDirectTeleport()
        {
            try
            {
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod != null)
                {
                    mod.StartBossRush(null);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] FallbackToDirectTeleport 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 恢复被隐藏的原有条目，并销毁动态创建的 BossRush 条目
        /// </summary>
        public static void RestoreHiddenEntries()
        {
            try
            {
                // 恢复被隐藏的原有条目
                foreach (GameObject entry in hiddenEntries)
                {
                    if (entry != null)
                    {
                        entry.SetActive(true);
                    }
                }
                hiddenEntries.Clear();
                
                // 销毁动态创建的 BossRush 条目，防止残留在 MapSelectionView 中
                foreach (GameObject obj in bossRushEntryObjects)
                {
                    if (obj != null)
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                }
                bossRushEntryObjects.Clear();
                bossRushEntryObject = null;
                
                ModBehaviour.DevLog("[BossRush] 已恢复所有隐藏的条目并清理 BossRush 条目");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] RestoreHiddenEntries 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 清理动态创建的条目并恢复原有条目
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                // 恢复被隐藏的原有条目
                RestoreHiddenEntries();
                
                // 销毁动态创建的 BossRush 条目
                foreach (GameObject obj in bossRushEntryObjects)
                {
                    if (obj != null)
                    {
                        UnityEngine.Object.Destroy(obj);
                    }
                }
                bossRushEntryObjects.Clear();
                bossRushEntryObject = null;
                
                // 清除待处理的地图索引
                pendingMapEntryIndex = -1;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] Cleanup 失败: " + e.Message);
            }
        }
    }
    
    /// <summary>
    /// BossRush 地图条目点击处理器
    /// 用于在玩家点击地图条目时记录选中的地图索引
    /// </summary>
    public class BossRushMapEntryClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public int entryIndex = -1;
        
        public void OnPointerClick(PointerEventData eventData)
        {
            // 记录选中的地图索引，供场景加载后使用
            if (entryIndex >= 0)
            {
                BossRushMapSelectionHelper.SetPendingMapEntryIndex(entryIndex);
                ModBehaviour.DevLog("[BossRush] 玩家点击地图条目: " + entryIndex);
            }
        }
    }
}
