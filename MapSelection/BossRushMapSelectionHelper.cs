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
    /// </summary>
    public static class BossRushMapSelectionHelper
    {
        // BossRush 竞技场场景 ID
        public const string BossRushSceneID = "Level_DemoChallenge_Main";
        
        // BossRush 落点编号（使用默认起点）
        public const int BossRushBeaconIndex = 0;
        
        // 是否已初始化
        private static bool initialized = false;
        
        // 动态创建的 BossRush 条目 GameObject
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
                Debug.LogError("[BossRush] GetBossRushTicketTypeId 失败: " + e.Message);
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
                    Debug.LogError("[BossRush] MapSelectionView.Instance 为 null，无法打开地图选择 UI");
                    // 回退到直接传送
                    FallbackToDirectTeleport();
                    return;
                }
                
                // 注入 BossRush 条目
                bool injected = InjectBossRushEntry(mapView);
                if (!injected)
                {
                    Debug.LogWarning("[BossRush] 无法注入 BossRush 条目，回退到直接传送");
                    FallbackToDirectTeleport();
                    return;
                }
                
                // 打开地图选择 UI
                mapView.Open(null);
                
                // Open() 后验证条目状态
                Debug.Log("[BossRush] Open() 后验证条目状态:");
                MapSelectionEntry[] entriesAfterOpen = mapView.GetComponentsInChildren<MapSelectionEntry>(true);
                foreach (MapSelectionEntry e in entriesAfterOpen)
                {
                    bool isActive = e.gameObject.activeInHierarchy;
                    bool selfActive = e.gameObject.activeSelf;
                    Debug.Log("[BossRush] 条目: " + e.gameObject.name + ", sceneID=" + e.SceneID + ", activeInHierarchy=" + isActive + ", activeSelf=" + selfActive);
                }
                
                // 关键：Open() 后再次设置显示名称
                // 因为 Open() 会触发 OnEnable() -> Refresh()，可能会覆盖我们之前设置的文本
                // 此时需要再次强制设置正确的显示名称
                if (bossRushEntryObject != null)
                {
                    SceneInfoEntry sceneInfo = SceneInfoCollection.GetSceneInfo(BossRushSceneID);
                    string displayName = sceneInfo != null ? sceneInfo.DisplayName : "Boss Rush 竞技场";
                    SetEntryDisplayNameDirect(bossRushEntryObject, displayName);
                    Debug.Log("[BossRush] Open() 后再次设置显示名称: " + displayName);
                }
                
                // 启动协程监控 UI 关闭，以便恢复隐藏的条目
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod != null)
                {
                    mod.StartCoroutine(WatchMapSelectionViewClose(mapView));
                }
                
                Debug.Log("[BossRush] 已打开 MapSelectionView，等待玩家选择");
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] ShowBossRushMapSelection 失败: " + e.Message);
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
            Debug.Log("[BossRush] MapSelectionView 已关闭，已恢复隐藏的条目");
        }

        
        // 存储被隐藏的原有条目，以便恢复
        private static List<GameObject> hiddenEntries = new List<GameObject>();
        
        /// <summary>
        /// 注入 BossRush 目的地条目到 MapSelectionView
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
                    Debug.LogWarning("[BossRush] MapSelectionView 中未找到任何 MapSelectionEntry，无法注入");
                    return false;
                }
                
                // 清空之前隐藏的条目列表
                hiddenEntries.Clear();
                
                // 隐藏所有原有条目（除了 BossRush 条目）
                MapSelectionEntry templateEntry = null;
                foreach (MapSelectionEntry entry in existingEntries)
                {
                    if (entry == null) continue;
                    
                    if (entry.SceneID == BossRushSceneID)
                    {
                        // 已存在 BossRush 条目，更新其 Cost 并保持显示
                        UpdateEntryCost(entry, CreateBossRushCost());
                        Debug.Log("[BossRush] 已更新现有 BossRush 条目的 Cost");
                        // 继续隐藏其他条目
                    }
                    else
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
                }
                
                // 检查是否已存在 BossRush 条目
                foreach (MapSelectionEntry entry in existingEntries)
                {
                    if (entry != null && entry.SceneID == BossRushSceneID)
                    {
                        // 确保 BossRush 条目显示
                        entry.gameObject.SetActive(true);
                        // 不再覆盖名称，让它显示场景原本的地名（由 Refresh() 从 SceneInfoCollection 获取）
                        // 设置背景图片
                        SetEntryBackgroundImage(entry);
                        Debug.Log("[BossRush] 已隐藏 " + hiddenEntries.Count + " 个原有条目，保留 BossRush 条目");
                        return true;
                    }
                }
                
                // 没有现有 BossRush 条目，需要克隆创建
                if (templateEntry == null)
                {
                    Debug.LogWarning("[BossRush] 没有可用的模板 MapSelectionEntry");
                    return false;
                }
                
                // 克隆模板
                GameObject cloned = UnityEngine.Object.Instantiate(templateEntry.gameObject, templateEntry.transform.parent);
                cloned.name = "BossRush_MapSelectionEntry";
                cloned.SetActive(true); // 确保克隆的条目是激活的
                
                // 获取克隆的 MapSelectionEntry 组件
                MapSelectionEntry bossRushEntry = cloned.GetComponent<MapSelectionEntry>();
                if (bossRushEntry == null)
                {
                    Debug.LogWarning("[BossRush] 克隆的对象上没有 MapSelectionEntry 组件");
                    UnityEngine.Object.Destroy(cloned);
                    return false;
                }
                
                // 配置 BossRush 条目的字段（设置 sceneID、cost 等）
                // 必须在 Setup() 之前设置 sceneID，因为 Setup() 会调用 Refresh()
                ConfigureBossRushEntry(bossRushEntry);
                
                // 调用 Setup 方法关联到 MapSelectionView
                // Setup() 会调用 Refresh()，Refresh() 会从 SceneInfoCollection 获取场景信息并更新 UI
                bossRushEntry.Setup(mapView);
                
                // 获取场景信息
                SceneInfoEntry sceneInfo = SceneInfoCollection.GetSceneInfo(BossRushSceneID);
                string displayName = sceneInfo != null ? sceneInfo.DisplayName : "Boss Rush 竞技场";
                Debug.Log("[BossRush] 场景显示名称: " + displayName);
                
                // 直接查找克隆对象内部的 TextMeshProUGUI 组件并设置文本
                // 这是最可靠的方法，不依赖于 SerializeField 的绑定
                SetEntryDisplayNameDirect(cloned, displayName);
                
                // 更新缩略图
                UpdateEntryThumbnail(bossRushEntry);
                
                // 保存引用以便后续清理
                bossRushEntryObject = cloned;
                
                // 确保克隆的条目是激活的
                cloned.SetActive(true);
                Debug.Log("[BossRush] 已确保 BossRush 条目激活状态: " + cloned.activeInHierarchy);
                
                // 确保克隆的条目在最前面（siblingIndex = 0）
                cloned.transform.SetAsFirstSibling();
                Debug.Log("[BossRush] 已将 BossRush 条目移到最前面");
                
                // 验证所有条目的状态
                MapSelectionEntry[] allEntries = mapView.GetComponentsInChildren<MapSelectionEntry>(true);
                Debug.Log("[BossRush] MapSelectionView 中共有 " + allEntries.Length + " 个条目");
                foreach (MapSelectionEntry e in allEntries)
                {
                    bool isActive = e.gameObject.activeInHierarchy;
                    int siblingIndex = e.transform.GetSiblingIndex();
                    Debug.Log("[BossRush] 条目: " + e.gameObject.name + ", sceneID=" + e.SceneID + ", active=" + isActive + ", siblingIndex=" + siblingIndex);
                }
                
                Debug.Log("[BossRush] 成功注入 BossRush 条目，已隐藏 " + hiddenEntries.Count + " 个原有条目");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] InjectBossRushEntry 失败: " + e.Message);
                return false;
            }
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
                    Debug.Log("[BossRush] 场景信息存在: ID=" + sceneInfo.ID + ", DisplayName=" + sceneInfo.DisplayName);
                }
                else
                {
                    Debug.LogWarning("[BossRush] 场景信息不存在: " + BossRushSceneID + "，Refresh() 可能无法正确显示名称");
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
                
                Debug.Log("[BossRush] 已配置 BossRush 条目: sceneID=" + BossRushSceneID + ", beaconIndex=" + BossRushBeaconIndex);
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] ConfigureBossRushEntry 失败: " + e.Message);
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
                        Debug.Log("[BossRush] 已设置 MapSelectionEntry.fullScreenImage 字段");
                    }
                    else
                    {
                        Debug.LogWarning("[BossRush] 未找到 fullScreenImage 字段");
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
                                Debug.Log("[BossRush] 已设置 Image 组件背景: " + img.gameObject.name);
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[BossRush] 自定义背景图片为 null");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] SetEntryBackgroundImage 失败: " + e.Message);
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
                    Debug.LogWarning("[BossRush] 无法获取 Mod 路径");
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
                        Debug.LogWarning("[BossRush] 背景图片不存在: " + imagePath);
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
                    Debug.Log("[BossRush] 成功加载背景图片: " + imagePath);
                    return sprite;
                }
                else
                {
                    Debug.LogError("[BossRush] 无法解析图片数据");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] LoadCustomBackgroundSprite 失败: " + e.Message);
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
                                Debug.Log("[BossRush] Mod 路径: " + path);
                                return path;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] GetModPath 失败: " + e.Message);
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
                    Debug.LogWarning("[BossRush] 无法加载自定义背景图片");
                    return;
                }
                
                // 查找条目 GameObject 内部的所有 Image 组件
                Image[] images = entry.GetComponentsInChildren<Image>(true);
                int updatedCount = 0;
                
                Debug.Log("[BossRush] 条目层级路径: " + GetHierarchyPath(entry.transform));
                Debug.Log("[BossRush] 条目内部找到 " + images.Length + " 个 Image 组件");
                
                foreach (Image img in images)
                {
                    string spriteName = img.sprite != null ? img.sprite.name : "null";
                    string hierarchyPath = GetHierarchyPath(img.transform);
                    
                    // 只更新 sprite 名称以 "Map_" 开头的 Image（这些是地图预览图）
                    if (img.sprite != null && img.sprite.name.StartsWith("Map_"))
                    {
                        Debug.Log("[BossRush] 更新地图预览图: " + hierarchyPath + ", 原sprite=" + spriteName);
                        img.sprite = customBackgroundSprite;
                        // 强制刷新 Image 组件
                        img.SetAllDirty();
                        updatedCount++;
                    }
                }
                
                Debug.Log("[BossRush] 已更新 " + updatedCount + " 个地图预览图");
                
                // 强制刷新整个条目的布局
                LayoutRebuilder.ForceRebuildLayoutImmediate(entry.transform as RectTransform);
                
                // 强制刷新 Canvas
                Canvas.ForceUpdateCanvases();
                
                // 验证更新是否成功
                foreach (Image img in images)
                {
                    if (img.sprite != null)
                    {
                        Debug.Log("[BossRush] 验证 Image: " + img.gameObject.name + ", sprite=" + img.sprite.name);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] UpdateEntryThumbnail 失败: " + e.Message);
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
                Debug.Log("[BossRush] 克隆对象内部找到 " + textComps.Length + " 个 TextMeshProUGUI 组件");
                
                bool found = false;
                foreach (TextMeshProUGUI tmp in textComps)
                {
                    string objName = tmp.gameObject.name;
                    string hierarchyPath = GetHierarchyPath(tmp.transform);
                    Debug.Log("[BossRush] TextMeshProUGUI: " + objName + ", text=" + tmp.text + ", path=" + hierarchyPath);
                    
                    // 查找名为 "Text_MapName" 的组件（这是显示名称的组件）
                    if (objName == "Text_MapName")
                    {
                        Debug.Log("[BossRush] 找到 Text_MapName，设置显示名称: " + displayName);
                        tmp.text = displayName;
                        found = true;
                    }
                }
                
                if (!found)
                {
                    Debug.LogWarning("[BossRush] 未找到 Text_MapName 组件，尝试查找包含 'name' 的组件");
                    // 回退：查找名称包含 "name" 的组件
                    foreach (TextMeshProUGUI tmp in textComps)
                    {
                        string objName = tmp.gameObject.name.ToLower();
                        if (objName.Contains("name") || objName.Contains("title") || objName.Contains("display"))
                        {
                            Debug.Log("[BossRush] 回退设置 TextMeshProUGUI: " + tmp.gameObject.name + " -> " + displayName);
                            tmp.text = displayName;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] SetEntryDisplayNameDirect 失败: " + e.Message);
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
                        Debug.Log("[BossRush] 通过反射设置显示名称: " + displayName + ", 组件路径: " + GetHierarchyPath(textComp.transform));
                        textComp.text = displayName;
                    }
                    else
                    {
                        Debug.LogWarning("[BossRush] displayNameText 字段为 null，尝试查找子对象中的 TextMeshProUGUI");
                    }
                }
                
                // 方法2：直接查找条目内部的 TextMeshProUGUI 组件
                // 通常显示名称的 TextMeshProUGUI 组件名称包含 "Name" 或 "Title"
                TextMeshProUGUI[] textComps = entry.GetComponentsInChildren<TextMeshProUGUI>(true);
                Debug.Log("[BossRush] 条目内部找到 " + textComps.Length + " 个 TextMeshProUGUI 组件");
                
                foreach (TextMeshProUGUI tmp in textComps)
                {
                    string objName = tmp.gameObject.name.ToLower();
                    Debug.Log("[BossRush] TextMeshProUGUI: " + tmp.gameObject.name + ", text=" + tmp.text);
                    
                    // 查找可能是显示名称的组件
                    if (objName.Contains("name") || objName.Contains("title") || objName.Contains("display"))
                    {
                        Debug.Log("[BossRush] 更新 TextMeshProUGUI: " + tmp.gameObject.name + " -> " + displayName);
                        tmp.text = displayName;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] SetEntryDisplayName 失败: " + e.Message);
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
                        Debug.Log("[BossRush] 验证显示名称: " + textComp.text + ", 组件路径: " + GetHierarchyPath(textComp.transform));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] VerifyEntryDisplayName 失败: " + e.Message);
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
                            Debug.Log("[BossRush] 原 displayNameText 绑定: " + GetHierarchyPath(oldComp.transform));
                        }
                        
                        // 重新绑定到克隆后的组件
                        displayNameField.SetValue(entry, displayNameComp);
                        Debug.Log("[BossRush] 已重新绑定 displayNameText: " + GetHierarchyPath(displayNameComp.transform));
                    }
                }
                else
                {
                    Debug.LogWarning("[BossRush] 未找到 Text_MapName 组件，无法重新绑定 displayNameText");
                }
                
                // 重新绑定 costDisplay
                CostDisplay[] costDisplays = clonedObject.GetComponentsInChildren<CostDisplay>(true);
                if (costDisplays.Length > 0)
                {
                    FieldInfo costDisplayField = entryType.GetField("costDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (costDisplayField != null)
                    {
                        costDisplayField.SetValue(entry, costDisplays[0]);
                        Debug.Log("[BossRush] 已重新绑定 costDisplay");
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
                        Debug.Log("[BossRush] 已重新绑定 lockedIndicator");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] RebindSerializedFields 失败: " + e.Message);
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
                Debug.LogError("[BossRush] UpdateEntryCost 失败: " + e.Message);
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
                Debug.LogError("[BossRush] SetBossRushArenaPlanned 失败: " + e.Message);
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
                Debug.LogError("[BossRush] FallbackToDirectTeleport 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 恢复被隐藏的原有条目
        /// </summary>
        public static void RestoreHiddenEntries()
        {
            try
            {
                foreach (GameObject entry in hiddenEntries)
                {
                    if (entry != null)
                    {
                        entry.SetActive(true);
                    }
                }
                hiddenEntries.Clear();
                Debug.Log("[BossRush] 已恢复所有隐藏的条目");
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] RestoreHiddenEntries 失败: " + e.Message);
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
                if (bossRushEntryObject != null)
                {
                    UnityEngine.Object.Destroy(bossRushEntryObject);
                    bossRushEntryObject = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BossRush] Cleanup 失败: " + e.Message);
            }
        }
    }
}
