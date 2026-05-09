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
        private enum BossRushEntryFlowSource
        {
            None,
            MapSelectionUi,
            DirectTeleport
        }

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

        // 地图选择 UI 流程与“船票已被 UI 扣除”的跨场景状态
        private static BossRushEntryFlowSource pendingEntryFlowSource = BossRushEntryFlowSource.None;
        private static bool pendingPrepaidTicketForCurrentEntry = false;
        
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
        /// 标记本次 BossRush 启动来自地图选择 UI。
        /// 仅在实际进入目标地图后，才会把船票视为“已由 UI 预扣”。
        /// </summary>
        public static void MarkEntryFlowFromMapSelectionUi()
        {
            pendingEntryFlowSource = BossRushEntryFlowSource.MapSelectionUi;
            pendingPrepaidTicketForCurrentEntry = false;
        }

        /// <summary>
        /// 标记本次 BossRush 启动来自直接传送路径（不走地图选择扣票）。
        /// </summary>
        public static void MarkEntryFlowFromDirectTeleport()
        {
            pendingEntryFlowSource = BossRushEntryFlowSource.DirectTeleport;
            pendingPrepaidTicketForCurrentEntry = false;
        }

        /// <summary>
        /// 目标 BossRush 场景已开始加载。
        /// 若本次来自地图选择 UI，则视为船票已在 UI 确认阶段扣除。
        /// </summary>
        public static void MarkTargetSceneLoadStarted()
        {
            pendingPrepaidTicketForCurrentEntry =
                pendingEntryFlowSource == BossRushEntryFlowSource.MapSelectionUi;
        }

        /// <summary>
        /// 当前这次 BossRush 入场，船票是否已在地图选择 UI 中预扣。
        /// </summary>
        public static bool HasPendingPrepaidTicket()
        {
            return pendingPrepaidTicketForCurrentEntry;
        }

        /// <summary>
        /// 清理当前 BossRush 入场的预扣票状态，避免串到下一次。
        /// </summary>
        public static void ClearPendingEntryFlowState()
        {
            pendingEntryFlowSource = BossRushEntryFlowSource.None;
            pendingPrepaidTicketForCurrentEntry = false;
        }
        
        /// <summary>
        /// 打开带有 BossRush 条目的地图选择 UI
        /// </summary>
        public static void ShowBossRushMapSelection()
        {
            try
            {
                MarkEntryFlowFromMapSelectionUi();

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
        /// 延迟后刷新所有 BossRush 条目的显示名称（防止被 UI 系统的 Refresh() 覆盖）。
        /// 三连刷实现已抽到 MapSelectionEntryInjectionHelper.DelayedRefreshDisplayNames。
        /// </summary>
        private static System.Collections.IEnumerator DelayedRefreshDisplayNames()
        {
            return MapSelectionEntryInjectionHelper.DelayedRefreshDisplayNames(
                () => RefreshAllEntryDisplayNames("延迟刷新"));
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
                string displayName = GetBossRushEntryDisplayName(mapConfig);
                MapSelectionEntryInjectionHelper.SetEntryDisplayNameDirect(entryObj, displayName);
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
        /// 自适应原版 UI 的列容器结构，按 sceneID 匹配模板
        /// </summary>
        private static bool InjectBossRushEntry(MapSelectionView mapView)
        {
            try
            {
                ModBehaviour.BossRushMapConfig[] mapConfigs = ModBehaviour.GetAllMapConfigs();
                if (mapConfigs == null || mapConfigs.Length == 0)
                {
                    ModBehaviour.DevLog("[BossRush] 没有可用的地图配置");
                    return false;
                }

                MapSelectionEntryInjectionHelper.RestoreHiddenEntries(hiddenEntries);
                MapSelectionEntryInjectionHelper.DestroyCreatedEntries(bossRushEntryObjects);
                bossRushEntryObject = null;

                MapSelectionEntryInjectionHelper.TemplateContext context;
                int createdCount = MapSelectionEntryInjectionHelper.InjectEntries(
                    mapView,
                    "BossRush_MapSelectionEntry_",
                    bossRushEntryObjects,
                    hiddenEntries,
                    mapConfigs,
                    ConfigureBossRushEntryWithMapConfig,
                    GetBossRushEntryDisplayName,
                    SetupBossRushCostDisplay,
                    OnBossRushEntryCreated,
                    out context);

                bossRushEntryObject = bossRushEntryObjects.Count > 0 ? bossRushEntryObjects[0] : null;

                if (context != null)
                {
                    ModBehaviour.DevLog("[BossRush] 发现 " + context.ColumnOrder.Count + " 个列容器，共 " + context.TemplateCandidates.Count + " 个原版条目");
                    for (int i = 0; i < context.ColumnOrder.Count; i++)
                    {
                        Transform column = context.ColumnOrder[i];
                        List<MapSelectionEntry> entries = context.ColumnGroups.ContainsKey(column)
                            ? context.ColumnGroups[column]
                            : null;
                        ModBehaviour.DevLog("[BossRush] 列容器[" + i + "]: " + column.name + ", 条目数: " + (entries != null ? entries.Count : 0));
                    }
                }

                ModBehaviour.DevLog("[BossRush] 成功注入 " + createdCount + " 个 BossRush 条目，已隐藏 " + hiddenEntries.Count + " 个原有条目");
                return createdCount > 0;
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
                MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "sceneID", mapConfig.sceneID);
                MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "beaconIndex", mapConfig.beaconIndex);
                Cost bossRushCost = CreateBossRushCost();
                MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "cost", bossRushCost);
                
                MapSelectionEntryInjectionHelper.SetMapSelectionEntryField(entry, "conditions", null);
                
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

        private static string GetBossRushEntryDisplayName(ModBehaviour.BossRushMapConfig mapConfig)
        {
            if (mapConfig == null)
            {
                return "未知地图";
            }

            if (!string.IsNullOrEmpty(mapConfig.displayName))
            {
                return mapConfig.displayName;
            }

            SceneInfoEntry sceneInfo = SceneInfoCollection.GetSceneInfo(mapConfig.sceneID);
            return sceneInfo != null ? sceneInfo.DisplayName : "未知地图";
        }

        private static void SetupBossRushCostDisplay(GameObject entryObject)
        {
            if (entryObject == null)
            {
                return;
            }

            CostDisplay costDisplay = entryObject.GetComponentInChildren<CostDisplay>(true);
            if (costDisplay == null)
            {
                return;
            }

            Cost bossRushCost = CreateBossRushCost();
            MapSelectionEntryInjectionHelper.ClearCostDisplayItems(costDisplay);
            costDisplay.Setup(bossRushCost, 1);
            costDisplay.gameObject.SetActive(true);
            ModBehaviour.DevLog("[BossRush] 已刷新 CostDisplay，显示 BossRush 船票");
        }

        private static void OnBossRushEntryCreated(
            MapSelectionEntry uiEntry,
            GameObject entryObject,
            ModBehaviour.BossRushMapConfig mapConfig,
            int entryIndex,
            MapSelectionEntry template,
            Transform targetParent)
        {
            if (uiEntry == null || entryObject == null || mapConfig == null)
            {
                return;
            }

            string displayName = GetBossRushEntryDisplayName(mapConfig);
            if (!string.IsNullOrEmpty(mapConfig.previewImageName))
            {
                UpdateEntryThumbnailWithImage(uiEntry, mapConfig.previewImageName);
            }

            string templateName = template != null && template.gameObject != null ? template.gameObject.name : "null";
            string parentName = targetParent != null ? targetParent.name : "null";
            ModBehaviour.DevLog("[BossRush] 创建地图条目: " + displayName + " (sceneID=" + mapConfig.sceneID + ", 模板=" + templateName + ", 容器=" + parentName + ")");
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
        /// 内部可见，便于 ZombieMode 复用同一份预览图加载逻辑。
        /// </summary>
        internal static void UpdateEntryThumbnailWithImage(MapSelectionEntry entry, string imageName)
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
                
                // 尝试 Assets/preview 子目录
                string imagePath = System.IO.Path.Combine(modPath, "Assets", "preview", imageName);
                if (!System.IO.File.Exists(imagePath))
                {
                    // 回退到 Assets 目录
                    imagePath = System.IO.Path.Combine(modPath, "Assets", imageName);
                    if (!System.IO.File.Exists(imagePath))
                    {
                        // 回退到根目录
                        imagePath = System.IO.Path.Combine(modPath, imageName);
                        if (!System.IO.File.Exists(imagePath))
                        {
                            return null;
                        }
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
                MarkEntryFlowFromDirectTeleport();

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
