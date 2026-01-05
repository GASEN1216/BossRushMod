// ============================================================================
// DebugAndTools.cs - 调试工具
// ============================================================================
// 模块说明：
//   提供 BossRush 模组的调试工具和辅助方法，包括：
//   - DevLog: 开发模式日志输出
//   - GetTransformPath: 获取 Transform 的完整层级路径
//   - LogNearbyGameObjects: 输出玩家附近的 GameObject 信息
//   - OnInteractStartDebug: 交互事件监听，输出详细交互信息
//   - 放置模式：预览并复制建筑物到指定位置
//   
// 调试快捷键（仅在 DevModeEnabled = true 时生效）：
//   - F5: 输出玩家脚下的 GameObject 并记住最近的对象
//   - F6: 切换放置模式（预览 F5 记住的对象，鼠标左键确认放置）
//   - F7: 输出最近的交互点信息

// 抑制 DevModeEnabled = false 时的"无法访问的代码"警告
#pragma warning disable CS0162
//   - F8: 输出场景中所有非玩家角色
//   - F9: 直接开始 BossRush
//   - F10: 直接触发通关
//   - F11: 给予生日蛋糕并显示祝福横幅
//
// 放置模式说明：
//   1. 按 F5 选择并记住一个建筑物
//   2. 按 F6 进入放置模式（自动收起武器）
//   3. 移动鼠标，预览对象会跟随鼠标在地板上移动
//   4. 滚轮上下滚动可旋转对象（每次 15°）
//   5. 鼠标左键点击确认放置（可连续放置多个）
//   6. 鼠标右键点击删除鼠标处的建筑物（会输出详细日志）
//   7. 再次按 F6 退出放置模式
//
// 交互调试（仅在 DevModeEnabled = true 时生效）：
//   - 自动监听所有 InteractableBase.OnInteractStartStaticEvent 事件
//   - 输出交互对象的详细信息，便于分析传送塔等交互机制
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 调试工具模块
    /// </summary>
    public partial class ModBehaviour
    {
        // 交互调试监听是否已注册
        private static bool interactDebugListenerRegistered = false;
        
        // 开枪调试监听是否已注册
        private static bool shootDebugListenerRegistered = false;
        
        // F5 记住的最近 GameObject，供 F6 复制使用
        private static GameObject lastNearestGameObject = null;
        
        // ============================================================================
        // 放置模式相关变量
        // ============================================================================
        // 放置模式是否激活
        private static bool placementModeActive = false;
        // 预览用的克隆对象
        private static GameObject placementPreviewObject = null;
        // 原始材质缓存（用于恢复）
        private static Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
        // 预览材质（半透明）- 保留用于未来放置模式功能
        #pragma warning disable CS0414
        private static Material previewMaterial = null;
        #pragma warning restore CS0414
        // 当前旋转角度（Y轴）
        private static float placementRotationY = 0f;
        // 每次滚轮旋转的角度
        private const float ROTATION_STEP = 15f;
        
        // ============================================================================
        // 右键删除确认相关变量
        // ============================================================================
        // 当前选中待删除的对象（第一次右键选中，第二次右键删除）
        private static GameObject pendingDeleteObject = null;
        // 选中对象的原始材质缓存（用于恢复）
        private static Dictionary<Renderer, Material[]> pendingDeleteOriginalMaterials = new Dictionary<Renderer, Material[]>();
        // 描边材质
        private static Material outlineMaterial = null;
        
        // ============================================================================
        // 预制体列表相关变量（滚轮切换）
        // ============================================================================
        // 附近预制体列表
        private static List<GameObject> nearbyPrefabList = new List<GameObject>();
        // 当前选中的预制体索引
        private static int currentPrefabIndex = 0;
        // 预制体列表是否已初始化
        private static bool prefabListInitialized = false;
        // 扫描半径
        private const float PREFAB_SCAN_RADIUS = 50f;

        /// <summary>
        /// 注册交互调试监听（仅在 DevModeEnabled = true 时生效）
        /// 在 Awake 或 Start 中调用
        /// </summary>
        private void RegisterInteractDebugListener()
        {
            if (!DevModeEnabled) return;
            if (interactDebugListenerRegistered) return;

            try
            {
                InteractableBase.OnInteractStartStaticEvent += OnInteractStartDebug;
                interactDebugListenerRegistered = true;
                DevLog("[BossRush] 交互调试监听已注册");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 注册交互调试监听失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注销交互调试监听
        /// 在 OnDestroy 中调用
        /// </summary>
        private void UnregisterInteractDebugListener()
        {
            if (!interactDebugListenerRegistered) return;

            try
            {
                InteractableBase.OnInteractStartStaticEvent -= OnInteractStartDebug;
                interactDebugListenerRegistered = false;
            }
            catch {}
        }

        /// <summary>
        /// 注册开枪调试监听（仅在 DevModeEnabled = true 时生效）
        /// 在 Awake 或 Start 中调用
        /// </summary>
        private void RegisterShootDebugListener()
        {
            if (!DevModeEnabled) return;
            if (shootDebugListenerRegistered) return;

            try
            {
                ItemAgent_Gun.OnMainCharacterShootEvent += OnMainCharacterShootDebug;
                shootDebugListenerRegistered = true;
                DevLog("[BossRush] 开枪调试监听已注册");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 注册开枪调试监听失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注销开枪调试监听
        /// 在 OnDestroy 中调用
        /// </summary>
        private void UnregisterShootDebugListener()
        {
            if (!shootDebugListenerRegistered) return;

            try
            {
                ItemAgent_Gun.OnMainCharacterShootEvent -= OnMainCharacterShootDebug;
                shootDebugListenerRegistered = false;
            }
            catch {}
        }

        /// <summary>
        /// 开枪事件回调：输出枪口火焰和声音的实际值（用于调试龙息武器等自定义武器）
        /// </summary>
        /// <param name="gunAgent">开枪的枪械Agent</param>
        private static void OnMainCharacterShootDebug(ItemAgent_Gun gunAgent)
        {
            if (!DevModeEnabled) return;
            if (gunAgent == null) return;

            try
            {
                // 获取 ItemSetting_Gun 组件
                ItemSetting_Gun gunSetting = gunAgent.GunItemSetting;
                if (gunSetting == null)
                {
                    DevLog("[开枪调试] GunItemSetting 为 null");
                    return;
                }

                // 获取武器名称
                string weaponName = "<unknown>";
                Item weaponItem = gunAgent.Item;
                if (weaponItem != null)
                {
                    weaponName = weaponItem.DisplayName;
                }

                // 获取 muzzleFxPfb（枪口火焰预制体）
                string muzzleFxInfo = "null";
                if (gunSetting.muzzleFxPfb != null)
                {
                    muzzleFxInfo = gunSetting.muzzleFxPfb.name;
                }

                // 获取 shootKey（开枪声音Key）
                string shootKey = gunSetting.shootKey ?? "null";

                // 获取 bulletPfb（子弹预制体）
                string bulletPfbInfo = "null";
                if (gunSetting.bulletPfb != null)
                {
                    bulletPfbInfo = gunSetting.bulletPfb.name;
                }

                // 输出调试信息
                DevLog("[开枪调试] 武器: " + weaponName + 
                       ", shootKey: '" + shootKey + "'" +
                       ", muzzleFxPfb: " + muzzleFxInfo +
                       ", bulletPfb: " + bulletPfbInfo);
            }
            catch (Exception e)
            {
                DevLog("[开枪调试] 输出失败: " + e.Message);
            }
        }

        /// <summary>
        /// 交互事件回调：输出详细的交互信息
        /// </summary>
        /// <param name="interactable">被交互的对象</param>
        private static void OnInteractStartDebug(InteractableBase interactable)
        {
            if (!DevModeEnabled) return;
            if (interactable == null) return;

            try
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                GameObject go = interactable.gameObject;
                string goName = go != null ? go.name : "<null>";
                string typeName = interactable.GetType().Name;
                Vector3 pos = interactable.transform.position;
                string path = GetTransformPath(interactable.transform);

                // 基础信息
                DevLog("========== [交互调试] 交互开始 ==========");
                DevLog("[交互调试] 场景: " + sceneName);
                DevLog("[交互调试] 对象名称: " + goName);
                DevLog("[交互调试] 类型: " + typeName);
                DevLog("[交互调试] 位置: " + pos);
                DevLog("[交互调试] 层级路径: " + path);

                // InteractName
                string interactName = "";
                try { interactName = interactable.InteractName; } catch {}
                DevLog("[交互调试] InteractName: " + interactName);

                // 交互时间
                float interactTime = 0f;
                try { interactTime = interactable.InteractTime; } catch {}
                DevLog("[交互调试] InteractTime: " + interactTime);

                // 是否为交互组
                bool isGroup = false;
                try
                {
                    var groupField = typeof(InteractableBase).GetField("interactableGroup", BindingFlags.Public | BindingFlags.Instance);
                    if (groupField != null)
                    {
                        isGroup = (bool)groupField.GetValue(interactable);
                    }
                }
                catch {}
                DevLog("[交互调试] 是否为交互组: " + isGroup);

                // 交互组成员
                try
                {
                    var list = interactable.GetInteractableList();
                    if (list != null && list.Count > 0)
                    {
                        DevLog("[交互调试] 交互组成员数量: " + list.Count);
                        for (int i = 0; i < list.Count; i++)
                        {
                            var member = list[i];
                            if (member == null) continue;

                            string memberName = member.gameObject != null ? member.gameObject.name : "<null>";
                            string memberType = member.GetType().Name;
                            string memberInteractName = "";
                            try { memberInteractName = member.InteractName; } catch {}

                            DevLog("[交互调试]   成员 #" + (i + 1) + ": name=" + memberName + 
                                   ", type=" + memberType + 
                                   ", InteractName=" + memberInteractName);
                        }
                    }
                }
                catch {}

                // 检查是否有 MultiInteraction 父组件
                try
                {
                    MultiInteraction multiInteract = null;
                    Transform parent = interactable.transform.parent;
                    while (parent != null && multiInteract == null)
                    {
                        multiInteract = parent.GetComponent<MultiInteraction>();
                        parent = parent.parent;
                    }

                    if (multiInteract == null)
                    {
                        // 也检查同级
                        multiInteract = interactable.GetComponent<MultiInteraction>();
                    }

                    if (multiInteract != null)
                    {
                        DevLog("[交互调试] 所属 MultiInteraction: " + multiInteract.gameObject.name);
                        
                        // 获取 MultiInteraction 的 Interactables 列表
                        try
                        {
                            var interactables = multiInteract.Interactables;
                            if (interactables != null)
                            {
                                DevLog("[交互调试] MultiInteraction 包含 " + interactables.Count + " 个交互选项:");
                                for (int i = 0; i < interactables.Count; i++)
                                {
                                    var item = interactables[i];
                                    if (item == null) continue;

                                    string itemName = item.gameObject != null ? item.gameObject.name : "<null>";
                                    string itemType = item.GetType().Name;
                                    string itemInteractName = "";
                                    try { itemInteractName = item.InteractName; } catch {}

                                    DevLog("[交互调试]   选项 #" + (i + 1) + ": name=" + itemName + 
                                           ", type=" + itemType + 
                                           ", InteractName=" + itemInteractName);
                                }
                            }
                        }
                        catch {}
                    }
                    else
                    {
                        DevLog("[交互调试] 无 MultiInteraction 父组件");
                    }
                }
                catch {}

                // 输出所有组件
                try
                {
                    if (go != null)
                    {
                        Component[] components = go.GetComponents<Component>();
                        if (components != null && components.Length > 0)
                        {
                            string compList = "";
                            for (int i = 0; i < components.Length; i++)
                            {
                                if (components[i] == null) continue;
                                if (compList.Length > 0) compList += ", ";
                                compList += components[i].GetType().Name;
                            }
                            DevLog("[交互调试] 组件列表: " + compList);
                        }
                    }
                }
                catch {}

                // 检查是否需要物品
                try
                {
                    var requireItemField = typeof(InteractableBase).GetField("requireItem", BindingFlags.Public | BindingFlags.Instance);
                    var requireItemIdField = typeof(InteractableBase).GetField("requireItemId", BindingFlags.Public | BindingFlags.Instance);
                    
                    if (requireItemField != null)
                    {
                        bool requireItem = (bool)requireItemField.GetValue(interactable);
                        if (requireItem && requireItemIdField != null)
                        {
                            int requireItemId = (int)requireItemIdField.GetValue(interactable);
                            DevLog("[交互调试] 需要物品: ID=" + requireItemId);
                        }
                    }
                }
                catch {}

                DevLog("========== [交互调试] 信息输出完毕 ==========");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 交互调试输出失败: " + e.Message);
            }
        }

        /// <summary>
        /// 开发模式日志输出（仅在 DevModeEnabled = true 时输出）
        /// </summary>
        /// <param name="message">日志消息</param>
        internal static void DevLog(string message)
        {
            if (DevModeEnabled)
            {
                Debug.Log(message);
            }
        }

        /// <summary>
        /// 获取 Mod 根目录路径
        /// </summary>
        /// <returns>Mod 目录路径，失败时返回 null</returns>
        public static string GetModPath()
        {
            try
            {
                // 方法1：通过程序集位置获取
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    string modDir = System.IO.Path.GetDirectoryName(assemblyLocation);
                    if (!string.IsNullOrEmpty(modDir) && System.IO.Directory.Exists(modDir))
                    {
                        return modDir;
                    }
                }
                
                // 方法2：通过 ModBehaviour.info.path 获取
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod != null)
                {
                    var infoField = typeof(Duckov.Modding.ModBehaviour).GetField("info", 
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (infoField != null)
                    {
                        object infoObj = infoField.GetValue(mod);
                        if (infoObj != null)
                        {
                            var pathField = infoObj.GetType().GetField("path");
                            if (pathField != null)
                            {
                                string path = pathField.GetValue(infoObj) as string;
                                if (!string.IsNullOrEmpty(path))
                                {
                                    return path;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] GetModPath 失败: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 找到 Transform 的最近预制体根对象
        /// 优先返回最近的 Pfb_/Prfb_ 前缀对象，找不到则返回容器下的第一层子对象
        /// 例如：Env/Zone_D1/Pfb_Store_01/Indoor/Prfb_Shop_Shelf_01_53/Col_Default -> Prfb_Shop_Shelf_01_53
        /// </summary>
        /// <param name="t">目标 Transform</param>
        /// <returns>最近的预制体 Transform，如果找不到则返回 null</returns>
        private static Transform FindPrefabRoot(Transform t)
        {
            if (t == null) return null;
            
            // 优先查找：从当前对象向上找，返回第一个以 Pfb_ 或 Prfb_ 开头的对象
            Transform current = t;
            while (current != null)
            {
                string name = current.name;
                if (name.StartsWith("Pfb_") || name.StartsWith("Prfb_"))
                {
                    return current;
                }
                current = current.parent;
            }
            
            // 备用逻辑：如果没找到预制体前缀，向上查找到容器下的第一层子对象
            current = t;
            Transform lastValid = t;
            
            while (current.parent != null)
            {
                string parentName = current.parent.name;
                // 如果父对象是 Scene、Env、Zone 或类似的根容器，当前对象就是根
                if (parentName == "Scene" || parentName == "Env" ||
                    parentName.StartsWith("Scene_") || parentName.StartsWith("Level_") || 
                    parentName.StartsWith("Group_") || parentName.StartsWith("Zone_"))
                {
                    return current;
                }
                lastValid = current;
                current = current.parent;
            }
            
            // 如果没有找到容器，返回最顶层的对象
            return lastValid;
        }
        
        /// <summary>
        /// 检查一个 Transform 是否是另一个 Transform 的子对象（或自身）
        /// </summary>
        /// <param name="child">要检查的 Transform</param>
        /// <param name="parent">父 Transform</param>
        /// <returns>如果 child 是 parent 或 parent 的子对象，返回 true</returns>
        private static bool IsChildOf(Transform child, Transform parent)
        {
            if (child == null || parent == null) return false;
            if (child == parent) return true;
            
            Transform current = child.parent;
            while (current != null)
            {
                if (current == parent) return true;
                current = current.parent;
            }
            return false;
        }
        
        /// <summary>
        /// 获取 Transform 的完整层级路径
        /// </summary>
        /// <param name="t">目标 Transform</param>
        /// <returns>从根节点到目标的完整路径，如 "Root/Parent/Child"</returns>
        private static string GetTransformPath(Transform t)
        {
            if (t == null)
            {
                return "<null>";
            }

            string path = t.name;
            Transform current = t.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>
        /// 输出玩家附近的 GameObject 信息（用于调试）
        /// 过滤掉玩家自身及其子对象，只输出场景中的独立物体
        /// </summary>
        /// <param name="playerPos">玩家位置</param>
        /// <param name="radius">搜索半径</param>
        /// <param name="maxCount">最大输出数量</param>
        private static void LogNearbyGameObjects(Vector3 playerPos, float radius, int maxCount)
        {
            try
            {
                float radiusSq = radius * radius;
                Transform[] all = UnityEngine.Object.FindObjectsOfType<Transform>();

                if (all == null || all.Length == 0)
                {
                    DevLog("[BossRush] F5 调试：场景中未找到任何 Transform");
                    return;
                }
                
                // 获取玩家的 Transform，用于过滤
                Transform playerTransform = null;
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    playerTransform = main.transform;
                }

                List<Transform> nearby = new List<Transform>();

                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null)
                    {
                        continue;
                    }
                    
                    // 过滤掉玩家自身及其所有子对象
                    if (playerTransform != null && IsChildOf(t, playerTransform))
                    {
                        continue;
                    }

                    Vector3 pos = t.position;
                    float distSq = (pos - playerPos).sqrMagnitude;

                    if (distSq <= radiusSq && distSq > 0.0001f)
                    {
                        // 添加包含任意 Collider 组件的 GameObject（包括 BoxCollider, SphereCollider, CapsuleCollider 等）
                        if (t.gameObject.GetComponent<UnityEngine.Collider>() != null)
                        {
                            nearby.Add(t);
                        }
                    }
                }

                if (nearby.Count == 0)
                {
                    DevLog("[BossRush] F5 调试：玩家周围 " + radius + "m 内未找到任何 GameObject");
                    return;
                }

                nearby.Sort(delegate (Transform a, Transform b)
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return 1;
                    if (b == null) return -1;

                    float da = (a.position - playerPos).sqrMagnitude;
                    float db = (b.position - playerPos).sqrMagnitude;

                    if (da < db) return -1;
                    if (da > db) return 1;
                    return 0;
                });

                int outputCount = nearby.Count;
                if (outputCount > maxCount)
                {
                    outputCount = maxCount;
                }

                DevLog("[BossRush] F5 调试：玩家位置=" + playerPos +
                       "，半径=" + radius + "m，附近 GameObject 数量=" + nearby.Count +
                       "，输出前 " + outputCount + " 个：");

                for (int i = 0; i < outputCount; i++)
                {
                    Transform t = nearby[i];
                    if (t == null)
                    {
                        continue;
                    }

                    GameObject go = t.gameObject;
                    Vector3 pos = t.position;
                    float dist = UnityEngine.Mathf.Sqrt((pos - playerPos).sqrMagnitude);

                    string path = GetTransformPath(t);
                    string tag = "";
                    string layerName = "";
                    bool active = false;

                    try
                    {
                        if (go != null)
                        {
                            tag = go.tag;
                            layerName = UnityEngine.LayerMask.LayerToName(go.layer);
                            active = go.activeInHierarchy;
                        }
                    }
                    catch
                    {
                    }

                    string componentsInfo = "";

                    try
                    {
                        if (go != null)
                        {
                            Component[] components = go.GetComponents<Component>();
                            if (components != null && components.Length > 0)
                            {
                                string compNames = "";
                                bool first = true;

                                for (int j = 0; j < components.Length; j++)
                                {
                                    Component c = components[j];
                                    if (c == null)
                                    {
                                        continue;
                                    }

                                    string name = c.GetType().Name;
                                    if (string.IsNullOrEmpty(name))
                                    {
                                        continue;
                                    }

                                    if (!first)
                                    {
                                        compNames += ", ";
                                    }
                                    compNames += name;
                                    first = false;
                                }

                                if (!string.IsNullOrEmpty(compNames))
                                {
                                    componentsInfo = "[" + compNames + "]";
                                }
                            }
                        }
                    }
                    catch
                    {
                    }

                    DevLog("[BossRush] F5 GO #" + (i + 1) +
                           " path=" + path +
                           ", name=" + (go != null ? go.name : "<null>") +
                           ", pos=" + pos +
                           ", dist=" + dist.ToString("F2") +
                           ", tag=" + tag +
                           ", layer=" + layerName +
                           ", active=" + active +
                           ", components=" + componentsInfo);
                    
                    // 记住第一个（最近的）GameObject 的根 Prefab，供 F6 复制使用
                    if (i == 0 && go != null)
                    {
                        // 找到根 Prefab（Scene 下的第一层子对象）
                        Transform root = FindPrefabRoot(t);
                        if (root != null)
                        {
                            lastNearestGameObject = root.gameObject;
                            DevLog("[BossRush] F5 已记住根对象: " + root.name + "，按 F6 可复制到脚下");
                        }
                        else
                        {
                            lastNearestGameObject = go;
                            DevLog("[BossRush] F5 已记住此对象，按 F6 可复制到脚下");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] F5 调试：LogNearbyGameObjects 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// F6 调试：复制 F5 记住的 GameObject 到玩家脚下
        /// </summary>
        /// <param name="playerPos">玩家位置（目标位置）</param>
        private void CloneRememberedGameObject(Vector3 playerPos)
        {
            try
            {
                if (lastNearestGameObject == null)
                {
                    DevLog("[BossRush] F6 调试：没有记住的对象，请先按 F5 选择一个对象");
                    return;
                }
                
                // 复制 GameObject
                GameObject original = lastNearestGameObject;
                GameObject clone = UnityEngine.Object.Instantiate(original);
                clone.name = original.name + "_Clone";
                clone.transform.position = playerPos;
                
                // 保持原始旋转和缩放
                clone.transform.rotation = original.transform.rotation;
                clone.transform.localScale = original.transform.localScale;
                
                string originalPath = GetTransformPath(original.transform);
                
                DevLog("[BossRush] F6 调试：已复制 GameObject 到玩家脚下");
                DevLog("[BossRush]   原对象: " + original.name + ", 路径=" + originalPath);
                DevLog("[BossRush]   克隆到: " + playerPos + ", 新名称=" + clone.name);
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] F6 调试：CloneRememberedGameObject 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// F5 调试：输出玩家脚下/最近的建筑物详细信息
        /// 使用反射访问 Building 类，避免直接引用导致的程序集依赖问题
        /// </summary>
        private void LogNearbyBuildingInfo(Vector3 playerPos, float radius = 10f)
        {
            try
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                
                // 通过反射获取 Building 类型
                Type buildingType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        buildingType = asm.GetType("Duckov.Buildings.Building");
                        if (buildingType != null) break;
                    }
                    catch { }
                }
                
                if (buildingType == null)
                {
                    DevLog("[BossRush] F5 建筑调试：未找到 Building 类型");
                    return;
                }
                
                // 查找场景中所有 Building 组件
                UnityEngine.Object[] allBuildings = UnityEngine.Object.FindObjectsOfType(buildingType);
                
                if (allBuildings == null || allBuildings.Length == 0)
                {
                    DevLog("[BossRush] F5 建筑调试：场景 " + sceneName + " 中未找到任何 Building 组件");
                    return;
                }
                
                // 按距离排序，找到最近的建筑
                List<Component> nearbyBuildings = new List<Component>();
                foreach (var obj in allBuildings)
                {
                    Component building = obj as Component;
                    if (building == null || building.gameObject == null) continue;
                    
                    float dist = Vector3.Distance(playerPos, building.transform.position);
                    if (dist <= radius)
                    {
                        nearbyBuildings.Add(building);
                    }
                }
                
                // 按距离排序
                nearbyBuildings.Sort((a, b) => 
                    Vector3.Distance(playerPos, a.transform.position).CompareTo(
                    Vector3.Distance(playerPos, b.transform.position)));
                
                if (nearbyBuildings.Count == 0)
                {
                    DevLog("[BossRush] F5 建筑调试：玩家周围 " + radius + "m 内未找到任何建筑物（场景共有 " + allBuildings.Length + " 个建筑）");
                    return;
                }
                
                DevLog("[BossRush] F5 建筑调试：场景=" + sceneName + ", 玩家位置=" + playerPos + ", 半径=" + radius + "m, 找到 " + nearbyBuildings.Count + " 个建筑");
                
                // 获取 Building 类的属性和方法
                PropertyInfo idProp = buildingType.GetProperty("ID");
                PropertyInfo displayNameProp = buildingType.GetProperty("DisplayName");
                PropertyInfo descriptionProp = buildingType.GetProperty("Description");
                PropertyInfo guidProp = buildingType.GetProperty("GUID");
                PropertyInfo dimensionsProp = buildingType.GetProperty("Dimensions");
                
                // 获取 BuildingManager 类型
                Type buildingManagerType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        buildingManagerType = asm.GetType("Duckov.Buildings.BuildingManager");
                        if (buildingManagerType != null) break;
                    }
                    catch { }
                }
                MethodInfo getBuildingInfoMethod = null;
                if (buildingManagerType != null)
                {
                    getBuildingInfoMethod = buildingManagerType.GetMethod("GetBuildingInfo", BindingFlags.Public | BindingFlags.Static);
                }
                
                // 输出每个建筑的详细信息
                int outputCount = Math.Min(nearbyBuildings.Count, 5); // 最多输出5个
                for (int i = 0; i < outputCount; i++)
                {
                    Component building = nearbyBuildings[i];
                    if (building == null) continue;
                    
                    float dist = Vector3.Distance(playerPos, building.transform.position);
                    string path = GetTransformPath(building.transform);
                    
                    // 基本信息（通过反射获取）
                    string id = "";
                    string displayName = "";
                    string description = "";
                    int guid = 0;
                    string dimensions = "";
                    
                    try 
                    { 
                        if (idProp != null) 
                        { 
                            object val = idProp.GetValue(building, null); 
                            if (val != null) id = val.ToString(); 
                        } 
                    } 
                    catch { }
                    
                    try 
                    { 
                        if (displayNameProp != null) 
                        { 
                            object val = displayNameProp.GetValue(building, null); 
                            if (val != null) displayName = val.ToString(); 
                        } 
                    } 
                    catch { }
                    
                    try 
                    { 
                        if (descriptionProp != null) 
                        { 
                            object val = descriptionProp.GetValue(building, null); 
                            if (val != null) description = val.ToString(); 
                        } 
                    } 
                    catch { }
                    
                    try 
                    { 
                        if (guidProp != null) 
                        { 
                            object val = guidProp.GetValue(building, null); 
                            if (val != null) guid = (int)val; 
                        } 
                    } 
                    catch { }
                    
                    try 
                    { 
                        if (dimensionsProp != null) 
                        { 
                            object val = dimensionsProp.GetValue(building, null); 
                            if (val != null) dimensions = val.ToString(); 
                        } 
                    } 
                    catch { }
                    
                    // 获取 BuildingInfo 详细信息
                    string costInfo = "";
                    string requireBuildingsInfo = "";
                    int maxAmount = 0;
                    int currentAmount = 0;
                    bool reachedLimit = false;
                    
                    if (getBuildingInfoMethod != null && !string.IsNullOrEmpty(id))
                    {
                        try
                        {
                            object infoObj = getBuildingInfoMethod.Invoke(null, new object[] { id });
                            if (infoObj != null)
                            {
                                Type infoType = infoObj.GetType();
                                
                                // 检查 Valid 属性
                                PropertyInfo validProp = infoType.GetProperty("Valid");
                                bool valid = validProp != null && (bool)validProp.GetValue(infoObj, null);
                                
                                if (valid)
                                {
                                    // 获取 maxAmount
                                    FieldInfo maxAmountField = infoType.GetField("maxAmount");
                                    if (maxAmountField != null) maxAmount = (int)maxAmountField.GetValue(infoObj);
                                    
                                    // 获取 CurrentAmount
                                    PropertyInfo currentAmountProp = infoType.GetProperty("CurrentAmount");
                                    if (currentAmountProp != null) currentAmount = (int)currentAmountProp.GetValue(infoObj, null);
                                    
                                    // 获取 ReachedAmountLimit
                                    PropertyInfo reachedLimitProp = infoType.GetProperty("ReachedAmountLimit");
                                    if (reachedLimitProp != null) reachedLimit = (bool)reachedLimitProp.GetValue(infoObj, null);
                                    
                                    // 获取 cost
                                    FieldInfo costField = infoType.GetField("cost");
                                    if (costField != null)
                                    {
                                        object costObj = costField.GetValue(infoObj);
                                        if (costObj != null)
                                        {
                                            Type costType = costObj.GetType();
                                            FieldInfo moneyField = costType.GetField("money");
                                            FieldInfo itemsField = costType.GetField("items");
                                            
                                            long money = moneyField != null ? (long)moneyField.GetValue(costObj) : 0;
                                            Array items = null;
                                            if (itemsField != null)
                                            {
                                                items = itemsField.GetValue(costObj) as Array;
                                            }
                                            int itemCount = items != null ? items.Length : 0;
                                            
                                            if (money > 0 || itemCount > 0)
                                            {
                                                costInfo = "金钱=" + money;
                                                if (itemCount > 0) costInfo += ", 物品数=" + itemCount;
                                            }
                                        }
                                    }
                                    
                                    // 获取 requireBuildings
                                    FieldInfo requireBuildingsField = infoType.GetField("requireBuildings");
                                    if (requireBuildingsField != null)
                                    {
                                        string[] requireBuildings = requireBuildingsField.GetValue(infoObj) as string[];
                                        if (requireBuildings != null && requireBuildings.Length > 0)
                                        {
                                            requireBuildingsInfo = string.Join(",", requireBuildings);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    
                    // 获取子对象信息
                    int childCount = building.transform.childCount;
                    string childrenInfo = "";
                    for (int c = 0; c < Math.Min(childCount, 5); c++)
                    {
                        Transform child = building.transform.GetChild(c);
                        if (child != null)
                        {
                            childrenInfo += (c > 0 ? ", " : "") + child.name;
                        }
                    }
                    if (childCount > 5) childrenInfo += "...";
                    
                    // 获取组件列表
                    Component[] components = building.GetComponents<Component>();
                    string componentsInfo = "";
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        string typeName = comp.GetType().Name;
                        componentsInfo += (componentsInfo.Length > 0 ? ", " : "") + typeName;
                    }
                    
                    DevLog("[BossRush] F5 建筑 #" + (i + 1) + 
                           ": ID=" + id +
                           ", 名称=" + displayName +
                           ", GUID=" + guid +
                           ", 距离=" + dist.ToString("F2") + "m" +
                           ", 位置=" + building.transform.position +
                           ", 尺寸=" + dimensions +
                           ", 数量=" + currentAmount + "/" + maxAmount +
                           ", 达上限=" + reachedLimit);
                    
                    DevLog("[BossRush]   路径=" + path +
                           ", 费用=[" + costInfo + "]" +
                           ", 前置建筑=[" + requireBuildingsInfo + "]");
                    
                    if (!string.IsNullOrEmpty(description))
                    {
                        DevLog("[BossRush]   描述=" + description);
                    }
                    
                    DevLog("[BossRush]   子对象(" + childCount + ")=[" + childrenInfo + "]" +
                           ", 组件=[" + componentsInfo + "]");
                }
                
                if (nearbyBuildings.Count > outputCount)
                {
                    DevLog("[BossRush] F5 建筑调试：还有 " + (nearbyBuildings.Count - outputCount) + " 个建筑未显示");
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] F5 建筑调试出错: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        // ============================================================================
        // 放置模式功能
        // ============================================================================
        
        /// <summary>
        /// 切换放置模式（F6 调用）
        /// </summary>
        private void TogglePlacementMode()
        {
            if (placementModeActive)
            {
                // 退出放置模式
                ExitPlacementMode();
            }
            else
            {
                // 进入放置模式
                EnterPlacementMode();
            }
        }
        
        /// <summary>
        /// 进入放置模式
        /// </summary>
        private void EnterPlacementMode()
        {
            // 检查是否有 F5 记住的对象
            if (lastNearestGameObject == null)
            {
                DevLog("[BossRush] 放置模式：没有记住的对象，请先按 F5 选择一个对象");
                return;
            }
            
            // 获取玩家
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                DevLog("[BossRush] 放置模式：未找到玩家");
                return;
            }
            
            try
            {
                // 收起武器
                main.ChangeHoldItem(null);
                DevLog("[BossRush] 放置模式：已收起武器");
                
                // 初始化旋转角度为原对象的Y轴旋转
                placementRotationY = lastNearestGameObject.transform.eulerAngles.y;
                
                // 创建预览对象
                CreatePreviewObject();
                
                if (placementPreviewObject != null)
                {
                    placementModeActive = true;
                    DevLog("[BossRush] 放置模式：已进入，鼠标左键确认放置，滚轮旋转，再次按 F6 退出");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 进入放置模式失败: " + e.Message);
                ExitPlacementMode();
            }
        }
        
        /// <summary>
        /// 退出放置模式
        /// </summary>
        private void ExitPlacementMode()
        {
            placementModeActive = false;
            
            // 销毁预览对象
            if (placementPreviewObject != null)
            {
                UnityEngine.Object.Destroy(placementPreviewObject);
                placementPreviewObject = null;
            }
            
            // 清理材质缓存
            originalMaterials.Clear();
            
            // 清除待删除选中状态
            ClearPendingDelete();
            
            // 清空预制体列表
            nearbyPrefabList.Clear();
            prefabListInitialized = false;
            currentPrefabIndex = 0;
            
            DevLog("[BossRush] 放置模式：已退出");
        }
        
        /// <summary>
        /// 切换预制体（滚轮触发）
        /// </summary>
        /// <param name="direction">方向：1=下一个，-1=上一个</param>
        private void SwitchPrefab(int direction)
        {
            // 如果列表未初始化，先扫描附近预制体
            if (!prefabListInitialized)
            {
                ScanNearbyPrefabs();
            }
            
            // 如果列表为空，无法切换
            if (nearbyPrefabList.Count == 0)
            {
                DevLog("[BossRush] 放置模式：附近没有可用的预制体");
                return;
            }
            
            // 计算新索引
            currentPrefabIndex += direction;
            
            // 循环索引
            if (currentPrefabIndex >= nearbyPrefabList.Count)
            {
                currentPrefabIndex = 0;
            }
            else if (currentPrefabIndex < 0)
            {
                currentPrefabIndex = nearbyPrefabList.Count - 1;
            }
            
            // 获取新的预制体
            GameObject newPrefab = nearbyPrefabList[currentPrefabIndex];
            if (newPrefab == null)
            {
                // 如果对象已被销毁，从列表中移除并重试
                nearbyPrefabList.RemoveAt(currentPrefabIndex);
                if (nearbyPrefabList.Count > 0)
                {
                    currentPrefabIndex = currentPrefabIndex % nearbyPrefabList.Count;
                    SwitchPrefab(0); // 重新获取当前索引的对象
                }
                return;
            }
            
            // 保存当前预览对象的位置和旋转
            Vector3 currentPos = Vector3.zero;
            if (placementPreviewObject != null)
            {
                currentPos = placementPreviewObject.transform.position;
                UnityEngine.Object.Destroy(placementPreviewObject);
                placementPreviewObject = null;
            }
            
            // 切换到新预制体
            lastNearestGameObject = newPrefab;
            placementRotationY = newPrefab.transform.eulerAngles.y;
            
            // 创建新的预览对象
            CreatePreviewObject();
            
            // 恢复位置
            if (placementPreviewObject != null && currentPos != Vector3.zero)
            {
                placementPreviewObject.transform.position = currentPos;
            }
            
            DevLog("[BossRush] 放置模式：切换到预制体 [" + (currentPrefabIndex + 1) + "/" + nearbyPrefabList.Count + "] " + newPrefab.name);
        }
        
        /// <summary>
        /// 扫描玩家附近的预制体，构建可切换列表（按基础名称去重）
        /// </summary>
        private void ScanNearbyPrefabs()
        {
            nearbyPrefabList.Clear();
            
            // 获取玩家位置
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                DevLog("[BossRush] 放置模式：无法获取玩家位置");
                prefabListInitialized = true;
                return;
            }
            
            Vector3 playerPos = main.transform.position;
            Transform playerTransform = main.transform;
            
            // 扫描附近所有带碰撞器的对象
            Collider[] colliders = Physics.OverlapSphere(playerPos, PREFAB_SCAN_RADIUS);
            HashSet<GameObject> addedRoots = new HashSet<GameObject>();
            HashSet<string> addedBaseNames = new HashSet<string>(); // 用于按基础名称去重
            
            foreach (var col in colliders)
            {
                if (col == null) continue;
                
                // 跳过玩家自身
                if (IsChildOf(col.transform, playerTransform)) continue;
                
                // 找到根对象
                GameObject root = FindTrueRootObject(col.transform);
                if (root == null) continue;
                
                // 跳过已添加的对象实例
                if (addedRoots.Contains(root)) continue;
                
                // 跳过预览对象
                if (root.name.EndsWith("_Preview")) continue;
                
                // 跳过没有渲染器的对象（可能是纯碰撞体）
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                
                // 获取基础名称（去掉 _Clone 后缀和数字后缀）
                string baseName = GetBasePrefabName(root.name);
                
                // 按基础名称去重，只保留第一个遇到的
                if (addedBaseNames.Contains(baseName)) continue;
                
                // 添加到列表
                nearbyPrefabList.Add(root);
                addedRoots.Add(root);
                addedBaseNames.Add(baseName);
            }
            
            // 按基础名称排序，方便查找
            nearbyPrefabList.Sort((a, b) => string.Compare(GetBasePrefabName(a.name), GetBasePrefabName(b.name), StringComparison.Ordinal));
            
            // 如果当前选中的对象在列表中，设置为当前索引
            if (lastNearestGameObject != null)
            {
                int idx = nearbyPrefabList.IndexOf(lastNearestGameObject);
                if (idx >= 0)
                {
                    currentPrefabIndex = idx;
                }
                else
                {
                    // 尝试按基础名称查找
                    string targetBaseName = GetBasePrefabName(lastNearestGameObject.name);
                    for (int i = 0; i < nearbyPrefabList.Count; i++)
                    {
                        if (GetBasePrefabName(nearbyPrefabList[i].name) == targetBaseName)
                        {
                            currentPrefabIndex = i;
                            break;
                        }
                    }
                }
            }
            
            prefabListInitialized = true;
            DevLog("[BossRush] 放置模式：扫描到 " + nearbyPrefabList.Count + " 个不同类型的预制体（半径 " + PREFAB_SCAN_RADIUS + "m）");
        }
        
        /// <summary>
        /// 获取预制体的基础名称（去掉 _Clone、_Preview 和末尾数字后缀）
        /// 例如：Prfb_BoxGroup_16_Clone -> Prfb_BoxGroup_16
        ///       Prfb_Wall_01 -> Prfb_Wall_01
        /// </summary>
        private string GetBasePrefabName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            
            // 去掉 _Clone 后缀
            if (name.EndsWith("_Clone"))
            {
                name = name.Substring(0, name.Length - 6);
            }
            
            // 去掉 _Preview 后缀
            if (name.EndsWith("_Preview"))
            {
                name = name.Substring(0, name.Length - 8);
            }
            
            // 去掉末尾的 (数字) 格式，如 "Object (1)"
            int parenIdx = name.LastIndexOf(" (");
            if (parenIdx > 0 && name.EndsWith(")"))
            {
                name = name.Substring(0, parenIdx);
            }
            
            return name;
        }
        
        /// <summary>
        /// 创建预览对象（完整克隆，只禁用碰撞器）
        /// 简化方案：直接实例化原始对象，保持完整渲染，只禁用碰撞器避免物理干扰
        /// 确认放置时复制一份真实的，然后销毁预览
        /// </summary>
        private void CreatePreviewObject()
        {
            if (lastNearestGameObject == null) return;
            
            // 克隆对象（完整克隆，保持所有渲染效果）
            placementPreviewObject = UnityEngine.Object.Instantiate(lastNearestGameObject);
            placementPreviewObject.name = lastNearestGameObject.name + "_Preview";
            
            // 只禁用碰撞器，避免物理干扰，但保持完整渲染
            Collider[] colliders = placementPreviewObject.GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                col.enabled = false;
            }
            
            // 禁用 Rigidbody 避免物理模拟
            Rigidbody[] rigidbodies = placementPreviewObject.GetComponentsInChildren<Rigidbody>(true);
            foreach (var rb in rigidbodies)
            {
                rb.isKinematic = true;
            }
            
            DevLog("[BossRush] 放置模式：预览对象已创建（完整渲染模式）");
        }
        
        /// <summary>
        /// 占位方法（保持兼容性）
        /// </summary>
        private void DisableAllNonRenderComponents(GameObject previewObj)
        {
            // 简化方案不再需要此方法，保留空实现以防其他地方调用
            if (previewObj == null) return;
            int disabledCount = 0;
            if (disabledCount > 0)
            {
                DevLog("[BossRush] 放置模式：已禁用 " + disabledCount + " 个组件");
            }
        }
        
        /// <summary>
        /// 更新放置模式（在 Update 中调用）
        /// </summary>
        private void UpdatePlacementMode()
        {
            if (!placementModeActive || placementPreviewObject == null) return;
            
            try
            {
                // 获取相机
                Camera cam = null;
                try
                {
                    if (GameCamera.Instance != null)
                    {
                        cam = GameCamera.Instance.renderCamera;
                    }
                }
                catch { }
                
                if (cam == null)
                {
                    cam = Camera.main;
                }
                
                if (cam == null) return;
                
                // 处理滚轮：默认旋转预览对象，按住Shift时切换预制体
                float scroll = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
                if (scroll != 0f)
                {
                    bool shiftHeld = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
                    
                    if (shiftHeld)
                    {
                        // 按住Shift：切换预制体
                        SwitchPrefab(scroll > 0f ? 1 : -1);
                    }
                    else
                    {
                        // 不按Shift：旋转预览对象
                        if (scroll > 0f)
                        {
                            placementRotationY += ROTATION_STEP;
                        }
                        else
                        {
                            placementRotationY -= ROTATION_STEP;
                        }
                        // 保持角度在 0-360 范围内
                        if (placementRotationY >= 360f) placementRotationY -= 360f;
                        if (placementRotationY < 0f) placementRotationY += 360f;
                        
                        DevLog("[BossRush] 放置模式：旋转角度 = " + placementRotationY.ToString("F0") + "°");
                    }
                }
                
                // 获取鼠标位置并转换为射线
                Vector3 mousePos = UnityEngine.Input.mousePosition;
                Ray ray = cam.ScreenPointToRay(mousePos);
                
                // 射线检测地板和墙体（扩大检测范围）
                LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask | 
                                       Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
                RaycastHit hit;
                
                // 先尝试检测地板/墙体
                bool hitSomething = Physics.Raycast(ray, out hit, 500f, groundMask, QueryTriggerInteraction.Ignore);
                
                // 如果没有命中，尝试用更宽泛的检测（所有碰撞体）
                if (!hitSomething)
                {
                    hitSomething = Physics.Raycast(ray, out hit, 500f, ~0, QueryTriggerInteraction.Ignore);
                }
                
                if (hitSomething)
                {
                    // 移动预览对象到命中点
                    placementPreviewObject.transform.position = hit.point;
                    
                    // 应用旋转（保持原始X和Z轴旋转，只修改Y轴）
                    if (lastNearestGameObject != null)
                    {
                        Vector3 originalEuler = lastNearestGameObject.transform.eulerAngles;
                        placementPreviewObject.transform.rotation = Quaternion.Euler(originalEuler.x, placementRotationY, originalEuler.z);
                    }
                }
                
                // 检测鼠标左键点击 - 确认放置
                if (UnityEngine.Input.GetMouseButtonDown(0))
                {
                    ConfirmPlacement();
                }
                
                // 检测鼠标右键点击 - 选中/删除建筑物（两次确认）
                if (UnityEngine.Input.GetMouseButtonDown(1))
                {
                    HandleRightClick(cam);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 更新放置模式失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 处理右键点击（两次确认删除：第一次描边选中，第二次删除）
        /// </summary>
        private void HandleRightClick(Camera cam)
        {
            if (cam == null) return;
            
            try
            {
                Vector3 mousePos = UnityEngine.Input.mousePosition;
                Ray ray = cam.ScreenPointToRay(mousePos);
                
                // 检测所有碰撞体
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // 找到命中对象的真正根对象
                    Transform hitTransform = hit.collider.transform;
                    GameObject targetObj = FindTrueRootObject(hitTransform);
                    
                    if (targetObj != null)
                    {
                        // 检查是否点击的是同一个对象
                        if (pendingDeleteObject != null && pendingDeleteObject == targetObj)
                        {
                            // 第二次右键点击同一对象 - 执行删除
                            ConfirmDeleteObject(targetObj);
                        }
                        else
                        {
                            // 第一次右键点击或点击了不同对象 - 选中并描边
                            SelectObjectForDelete(targetObj);
                        }
                    }
                    else
                    {
                        // 点击空白处，取消选中
                        ClearPendingDelete();
                        DevLog("[BossRush] 放置模式：未找到可选中的对象");
                    }
                }
                else
                {
                    // 点击空白处，取消选中
                    ClearPendingDelete();
                    DevLog("[BossRush] 放置模式：右键未命中任何对象，已取消选中");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 右键处理失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 找到真正的根对象（向上查找直到没有父对象或父对象是场景根）
        /// </summary>
        private GameObject FindTrueRootObject(Transform hitTransform)
        {
            if (hitTransform == null) return null;
            
            Transform current = hitTransform;
            
            // 向上查找，直到找到场景根下的第一层对象
            while (current.parent != null)
            {
                string parentName = current.parent.name;
                // 如果父对象是场景根容器，当前对象就是我们要的根对象
                if (parentName == "Scene" || parentName.StartsWith("Scene_") || 
                    parentName.StartsWith("Level_") || parentName.StartsWith("Group_") ||
                    current.parent.parent == null)
                {
                    return current.gameObject;
                }
                current = current.parent;
            }
            
            // 如果没有父对象，返回自身
            return current.gameObject;
        }
        
        /// <summary>
        /// 选中对象并添加描边效果
        /// </summary>
        private void SelectObjectForDelete(GameObject targetObj)
        {
            // 先清除之前的选中
            ClearPendingDelete();
            
            pendingDeleteObject = targetObj;
            string objName = targetObj.name;
            string objPath = GetTransformPath(targetObj.transform);
            Vector3 objPos = targetObj.transform.position;
            
            DevLog("[BossRush] 放置模式选中：");
            DevLog("[BossRush]   对象名称: " + objName);
            DevLog("[BossRush]   对象路径: " + objPath);
            DevLog("[BossRush]   对象位置: " + objPos);
            DevLog("[BossRush]   再次右键此对象可删除，右键其他位置取消选中");
            
            // 创建描边材质（红色高亮）
            if (outlineMaterial == null)
            {
                Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    outlineMaterial = new Material(urpShader);
                    outlineMaterial.SetFloat("_Surface", 1); // Transparent
                    outlineMaterial.SetFloat("_Blend", 0);
                    outlineMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    outlineMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    outlineMaterial.SetFloat("_ZWrite", 0);
                    outlineMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    outlineMaterial.EnableKeyword("_EMISSION");
                    outlineMaterial.SetColor("_BaseColor", new Color(1f, 0.3f, 0.3f, 0.7f));
                    outlineMaterial.SetColor("_EmissionColor", new Color(1f, 0f, 0f, 1f));
                    outlineMaterial.renderQueue = 3100;
                }
                else
                {
                    outlineMaterial = new Material(Shader.Find("Standard"));
                    outlineMaterial.SetFloat("_Mode", 3);
                    outlineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    outlineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    outlineMaterial.SetInt("_ZWrite", 0);
                    outlineMaterial.EnableKeyword("_ALPHABLEND_ON");
                    outlineMaterial.EnableKeyword("_EMISSION");
                    outlineMaterial.color = new Color(1f, 0.3f, 0.3f, 0.7f);
                    outlineMaterial.SetColor("_EmissionColor", new Color(1f, 0f, 0f, 1f));
                    outlineMaterial.renderQueue = 3100;
                }
            }
            
            // 应用描边材质到所有渲染器
            Renderer[] renderers = targetObj.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                
                // 保存原始材质
                pendingDeleteOriginalMaterials[renderer] = renderer.sharedMaterials;
                
                // 创建混合材质数组（原始材质 + 描边材质叠加效果）
                Material[] newMats = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < newMats.Length; i++)
                {
                    // 创建原始材质的副本并添加红色叠加
                    if (renderer.sharedMaterials[i] != null)
                    {
                        newMats[i] = new Material(renderer.sharedMaterials[i]);
                        // 尝试设置颜色叠加
                        if (newMats[i].HasProperty("_BaseColor"))
                        {
                            Color origColor = newMats[i].GetColor("_BaseColor");
                            newMats[i].SetColor("_BaseColor", new Color(
                                Mathf.Min(1f, origColor.r + 0.5f),
                                origColor.g * 0.5f,
                                origColor.b * 0.5f,
                                origColor.a
                            ));
                        }
                        else if (newMats[i].HasProperty("_Color"))
                        {
                            Color origColor = newMats[i].GetColor("_Color");
                            newMats[i].SetColor("_Color", new Color(
                                Mathf.Min(1f, origColor.r + 0.5f),
                                origColor.g * 0.5f,
                                origColor.b * 0.5f,
                                origColor.a
                            ));
                        }
                        // 添加自发光
                        if (newMats[i].HasProperty("_EmissionColor"))
                        {
                            newMats[i].EnableKeyword("_EMISSION");
                            newMats[i].SetColor("_EmissionColor", new Color(0.5f, 0f, 0f, 1f));
                        }
                    }
                    else
                    {
                        newMats[i] = outlineMaterial;
                    }
                }
                renderer.materials = newMats;
            }
        }
        
        /// <summary>
        /// 确认删除选中的对象
        /// </summary>
        private void ConfirmDeleteObject(GameObject targetObj)
        {
            string objName = targetObj.name;
            string objPath = GetTransformPath(targetObj.transform);
            Vector3 objPos = targetObj.transform.position;
            
            DevLog("[BossRush] 放置模式删除确认：");
            DevLog("[BossRush]   对象名称: " + objName);
            DevLog("[BossRush]   对象路径: " + objPath);
            DevLog("[BossRush]   对象位置: " + objPos);
            
            // 清除待删除状态
            pendingDeleteObject = null;
            pendingDeleteOriginalMaterials.Clear();
            
            // 销毁对象
            UnityEngine.Object.Destroy(targetObj);
            
            DevLog("[BossRush] 放置模式：已删除 " + objName + " 位于 " + objPos);
        }
        
        /// <summary>
        /// 清除待删除选中状态，恢复原始材质
        /// </summary>
        private void ClearPendingDelete()
        {
            if (pendingDeleteObject != null)
            {
                // 恢复原始材质
                foreach (var kvp in pendingDeleteOriginalMaterials)
                {
                    Renderer renderer = kvp.Key;
                    Material[] originalMats = kvp.Value;
                    
                    if (renderer != null && originalMats != null)
                    {
                        renderer.sharedMaterials = originalMats;
                    }
                }
                
                DevLog("[BossRush] 放置模式：已取消选中 " + pendingDeleteObject.name);
                pendingDeleteObject = null;
            }
            pendingDeleteOriginalMaterials.Clear();
        }
        
        /// <summary>
        /// 确认放置（鼠标左键点击时调用）
        /// </summary>
        private void ConfirmPlacement()
        {
            if (!placementModeActive || placementPreviewObject == null || lastNearestGameObject == null)
            {
                return;
            }
            
            try
            {
                // 在预览位置创建真正的克隆
                Vector3 placePos = placementPreviewObject.transform.position;
                
                // 使用当前旋转角度
                Vector3 originalEuler = lastNearestGameObject.transform.eulerAngles;
                Quaternion placeRot = Quaternion.Euler(originalEuler.x, placementRotationY, originalEuler.z);
                
                GameObject clone = UnityEngine.Object.Instantiate(lastNearestGameObject);
                clone.name = lastNearestGameObject.name + "_Clone";
                clone.transform.position = placePos;
                clone.transform.rotation = placeRot;
                clone.transform.localScale = lastNearestGameObject.transform.localScale;
                
                DevLog("[BossRush] 放置模式：已在 " + placePos + " 放置 " + clone.name + "，旋转=" + placementRotationY.ToString("F0") + "°");
                
                // 不退出放置模式，允许继续放置
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 确认放置失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 检查放置模式是否激活（供外部调用）
        /// </summary>
        public static bool IsPlacementModeActive()
        {
            return placementModeActive;
        }

        // ============================================================================
        // F4 调试：输出玩家装备的两把武器的详细信息
        // ============================================================================
        
        /// <summary>
        /// 输出玩家装备在武器槽位的两把武器的详细信息
        /// 包括基础参数、Stats属性、Variables变量、Constants常量、配件槽位等
        /// </summary>
        private void LogPlayerWeaponsDetailedInfo()
        {
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                DevLog("[BossRush] F4 武器调试：未找到玩家 CharacterMainControl");
                return;
            }

            DevLog("========== [F4 武器调试] 玩家武器详细信息 ==========");

            // 获取主武器槽位
            Slot primSlot = main.PrimWeaponSlot();
            Item primWeapon = primSlot != null ? primSlot.Content : null;
            
            // 获取副武器槽位
            Slot secSlot = main.SecWeaponSlot();
            Item secWeapon = secSlot != null ? secSlot.Content : null;

            // 输出主武器信息
            DevLog("---------- [主武器槽位 PrimaryWeapon] ----------");
            if (primWeapon != null)
            {
                LogWeaponDetailedInfo(primWeapon, "主武器");
            }
            else
            {
                DevLog("[主武器] 槽位为空");
            }

            // 输出副武器信息
            DevLog("---------- [副武器槽位 SecondaryWeapon] ----------");
            if (secWeapon != null)
            {
                LogWeaponDetailedInfo(secWeapon, "副武器");
            }
            else
            {
                DevLog("[副武器] 槽位为空");
            }

            DevLog("========== [F4 武器调试] 输出完毕 ==========");
        }

        /// <summary>
        /// 输出单个武器的详细信息
        /// </summary>
        /// <param name="weapon">武器Item</param>
        /// <param name="prefix">日志前缀（如"主武器"、"副武器"）</param>
        private void LogWeaponDetailedInfo(Item weapon, string prefix)
        {
            if (weapon == null)
            {
                DevLog("[" + prefix + "] 武器为空");
                return;
            }

            try
            {
                // ========== 基础信息 ==========
                DevLog("[" + prefix + "] === 基础信息 ===");
                DevLog("[" + prefix + "] 名称: " + weapon.DisplayName);
                DevLog("[" + prefix + "] TypeID: " + weapon.TypeID);
                DevLog("[" + prefix + "] 品质(Quality): " + weapon.Quality);
                DevLog("[" + prefix + "] 显示品质(DisplayQuality): " + weapon.DisplayQuality);
                DevLog("[" + prefix + "] 价值(Value): " + weapon.Value);
                DevLog("[" + prefix + "] 单位重量(UnitSelfWeight): " + weapon.UnitSelfWeight.ToString("F3") + " kg");
                DevLog("[" + prefix + "] 总重量(TotalWeight): " + weapon.TotalWeight.ToString("F3") + " kg");
                
                // 耐久度信息
                if (weapon.UseDurability)
                {
                    DevLog("[" + prefix + "] 耐久度: " + weapon.Durability.ToString("F1") + " / " + weapon.MaxDurability.ToString("F1"));
                    DevLog("[" + prefix + "] 耐久损耗(DurabilityLoss): " + (weapon.DurabilityLoss * 100f).ToString("F1") + "%");
                    DevLog("[" + prefix + "] 最大耐久(含损耗): " + weapon.MaxDurabilityWithLoss.ToString("F1"));
                }
                else
                {
                    DevLog("[" + prefix + "] 耐久度: 无耐久系统");
                }

                // ========== Tags 标签 ==========
                DevLog("[" + prefix + "] === Tags 标签 ===");
                if (weapon.Tags != null && weapon.Tags.Count > 0)
                {
                    string tagList = "";
                    foreach (var tag in weapon.Tags)
                    {
                        if (tag != null)
                        {
                            if (tagList.Length > 0) tagList += ", ";
                            tagList += tag.name;
                        }
                    }
                    DevLog("[" + prefix + "] Tags: " + tagList);
                }
                else
                {
                    DevLog("[" + prefix + "] Tags: 无");
                }

                // ========== Stats 属性 ==========
                DevLog("[" + prefix + "] === Stats 属性 ===");
                if (weapon.Stats != null && weapon.Stats.Count > 0)
                {
                    foreach (var stat in weapon.Stats)
                    {
                        if (stat != null)
                        {
                            string modInfo = "";
                            if (stat.Modifiers != null && stat.Modifiers.Count > 0)
                            {
                                modInfo = " (基础值=" + stat.BaseValue.ToString("F2") + ", 修正器数量=" + stat.Modifiers.Count + ")";
                            }
                            DevLog("[" + prefix + "] Stat[" + stat.Key + "]: " + stat.Value.ToString("F4") + modInfo);
                        }
                    }
                }
                else
                {
                    DevLog("[" + prefix + "] Stats: 无");
                }

                // ========== Variables 变量 ==========
                DevLog("[" + prefix + "] === Variables 变量 ===");
                if (weapon.Variables != null && weapon.Variables.Count > 0)
                {
                    foreach (var variable in weapon.Variables)
                    {
                        if (variable != null)
                        {
                            string valueStr = variable.GetValueDisplayString();
                            string typeStr = variable.DataType.ToString();
                            DevLog("[" + prefix + "] Var[" + variable.Key + "] (" + typeStr + "): " + valueStr);
                        }
                    }
                }
                else
                {
                    DevLog("[" + prefix + "] Variables: 无");
                }

                // ========== Constants 常量 ==========
                DevLog("[" + prefix + "] === Constants 常量 ===");
                if (weapon.Constants != null && weapon.Constants.Count > 0)
                {
                    foreach (var constant in weapon.Constants)
                    {
                        if (constant != null)
                        {
                            string valueStr = constant.GetValueDisplayString();
                            string typeStr = constant.DataType.ToString();
                            DevLog("[" + prefix + "] Const[" + constant.Key + "] (" + typeStr + "): " + valueStr);
                        }
                    }
                    
                    // 额外调试：使用hash方式获取Caliber（与游戏内部逻辑一致）
                    int caliberHash = "Caliber".GetHashCode();
                    string caliberByHash = weapon.Constants.GetString(caliberHash, "<NOT_FOUND>");
                    string caliberByKey = weapon.Constants.GetString("Caliber", "<NOT_FOUND>");
                    DevLog("[" + prefix + "] [调试] Caliber(hash=" + caliberHash + "): '" + caliberByHash + "'");
                    DevLog("[" + prefix + "] [调试] Caliber(key): '" + caliberByKey + "'");
                }
                else
                {
                    DevLog("[" + prefix + "] Constants: 无");
                }

                // ========== 配件槽位 ==========
                DevLog("[" + prefix + "] === 配件槽位 (Slots) ===");
                if (weapon.Slots != null && weapon.Slots.Count > 0)
                {
                    DevLog("[" + prefix + "] 槽位数量: " + weapon.Slots.Count);
                    int slotIndex = 0;
                    foreach (var slot in weapon.Slots)
                    {
                        slotIndex++;
                        if (slot != null)
                        {
                            string slotKey = slot.Key;
                            string slotDisplayName = slot.DisplayName;
                            Item slotContent = slot.Content;
                            
                            if (slotContent != null)
                            {
                                DevLog("[" + prefix + "] 槽位#" + slotIndex + " [" + slotKey + "] (" + slotDisplayName + "): " + slotContent.DisplayName + " (TypeID=" + slotContent.TypeID + ")");
                                
                                // 输出配件的详细信息
                                LogAccessoryInfo(slotContent, prefix, slotIndex);
                            }
                            else
                            {
                                DevLog("[" + prefix + "] 槽位#" + slotIndex + " [" + slotKey + "] (" + slotDisplayName + "): 空");
                            }
                        }
                    }
                }
                else
                {
                    DevLog("[" + prefix + "] 配件槽位: 无");
                }

                // ========== 其他信息 ==========
                DevLog("[" + prefix + "] === 其他信息 ===");
                DevLog("[" + prefix + "] 是否可堆叠: " + weapon.Stackable);
                DevLog("[" + prefix + "] 是否可出售: " + weapon.CanBeSold);
                DevLog("[" + prefix + "] 是否可丢弃: " + weapon.CanDrop);
                DevLog("[" + prefix + "] 是否可修复: " + weapon.Repairable);
                DevLog("[" + prefix + "] 是否已检视: " + weapon.Inspected);
                DevLog("[" + prefix + "] 是否为枪械(IsGun): " + weapon.GetBool("IsGun", false));
                DevLog("[" + prefix + "] 音效Key: " + weapon.SoundKey);
                
                // ========== 枪械专属信息（ItemSetting_Gun） ==========
                ItemSetting_Gun gunSetting = weapon.GetComponent<ItemSetting_Gun>();
                if (gunSetting != null)
                {
                    DevLog("[" + prefix + "] === 枪械专属信息 (ItemSetting_Gun) ===");
                    DevLog("[" + prefix + "] shootKey (开枪声音): " + gunSetting.shootKey);
                    DevLog("[" + prefix + "] reloadKey (换弹声音): " + gunSetting.reloadKey);
                    DevLog("[" + prefix + "] triggerMode (扳机模式): " + gunSetting.triggerMode);
                    DevLog("[" + prefix + "] reloadMode (换弹模式): " + gunSetting.reloadMode);
                    DevLog("[" + prefix + "] autoReload (自动换弹): " + gunSetting.autoReload);
                    DevLog("[" + prefix + "] element (元素类型): " + gunSetting.element);
                    DevLog("[" + prefix + "] muzzleFxPfb (枪口火焰): " + (gunSetting.muzzleFxPfb != null ? gunSetting.muzzleFxPfb.name : "null"));
                    DevLog("[" + prefix + "] bulletPfb (子弹预制体): " + (gunSetting.bulletPfb != null ? gunSetting.bulletPfb.name : "null"));
                    DevLog("[" + prefix + "] buff (附加Buff): " + (gunSetting.buff != null ? gunSetting.buff.name : "null"));
                    
                    // 输出武器模型上的粒子系统和特效信息
                    LogWeaponVisualEffects(weapon, prefix);
                }
                else
                {
                    DevLog("[" + prefix + "] [提示] 该武器没有 ItemSetting_Gun 组件（非枪械）");
                }
            }
            catch (Exception e)
            {
                DevLog("[" + prefix + "] 输出武器信息时出错: " + e.Message);
            }
        }

        /// <summary>
        /// 输出配件的详细信息
        /// </summary>
        /// <param name="accessory">配件Item</param>
        /// <param name="weaponPrefix">武器前缀</param>
        /// <param name="slotIndex">槽位索引</param>
        private void LogAccessoryInfo(Item accessory, string weaponPrefix, int slotIndex)
        {
            if (accessory == null) return;

            string prefix = weaponPrefix + ".配件#" + slotIndex;

            try
            {
                // 配件基础信息
                DevLog("[" + prefix + "]   品质: " + accessory.Quality + ", 价值: " + accessory.Value);
                
                // 配件耐久
                if (accessory.UseDurability)
                {
                    DevLog("[" + prefix + "]   耐久: " + accessory.Durability.ToString("F1") + "/" + accessory.MaxDurability.ToString("F1"));
                }

                // 配件 Stats
                if (accessory.Stats != null && accessory.Stats.Count > 0)
                {
                    string statsStr = "";
                    foreach (var stat in accessory.Stats)
                    {
                        if (stat != null)
                        {
                            if (statsStr.Length > 0) statsStr += ", ";
                            statsStr += stat.Key + "=" + stat.Value.ToString("F2");
                        }
                    }
                    if (statsStr.Length > 0)
                    {
                        DevLog("[" + prefix + "]   Stats: " + statsStr);
                    }
                }

                // 配件 Constants（通常包含配件的关键属性）
                if (accessory.Constants != null && accessory.Constants.Count > 0)
                {
                    string constStr = "";
                    foreach (var constant in accessory.Constants)
                    {
                        if (constant != null)
                        {
                            if (constStr.Length > 0) constStr += ", ";
                            constStr += constant.Key + "=" + constant.GetValueDisplayString();
                        }
                    }
                    if (constStr.Length > 0)
                    {
                        DevLog("[" + prefix + "]   Constants: " + constStr);
                    }
                }

                // 配件 Modifiers（配件对武器属性的修正）
                if (accessory.Modifiers != null && accessory.Modifiers.Count > 0)
                {
                    DevLog("[" + prefix + "]   Modifiers数量: " + accessory.Modifiers.Count);
                }
            }
            catch (Exception e)
            {
                DevLog("[" + prefix + "]   输出配件信息时出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 输出武器模型上的粒子系统和视觉特效信息
        /// 用于学习带火AK-47等武器的火焰特效实现
        /// </summary>
        private void LogWeaponVisualEffects(Item weapon, string prefix)
        {
            if (weapon == null) return;
            
            try
            {
                DevLog("[" + prefix + "] === 视觉特效信息 ===");
                
                // 获取武器GameObject（Item对象）
                GameObject weaponGO = weapon.gameObject;
                if (weaponGO == null)
                {
                    DevLog("[" + prefix + "] 武器GameObject为null");
                    return;
                }
                
                // 尝试获取 ItemAgent（武器模型）
                // 通过 CharacterMainControl.GetGun() 获取当前手持的武器Agent
                ItemAgent_Gun gunAgent = null;
                try
                {
                    var main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        gunAgent = main.GetGun();
                        // 确认是同一把武器
                        if (gunAgent != null && gunAgent.Item != weapon)
                        {
                            gunAgent = null;
                        }
                    }
                }
                catch { }
                
                // 输出 ItemAgent 信息
                if (gunAgent != null)
                {
                    DevLog("[" + prefix + "] ItemAgent: " + gunAgent.name);
                    
                    // ========== 输出武器模型的Transform信息（调试朝向问题） ==========
                    DevLog("[" + prefix + "] === 武器模型Transform信息 ===");
                    Transform agentTransform = gunAgent.transform;
                    DevLog("[" + prefix + "] Agent.localPosition: " + agentTransform.localPosition);
                    DevLog("[" + prefix + "] Agent.localRotation: " + agentTransform.localRotation.eulerAngles);
                    DevLog("[" + prefix + "] Agent.localScale: " + agentTransform.localScale);
                    DevLog("[" + prefix + "] Agent.position (世界): " + agentTransform.position);
                    DevLog("[" + prefix + "] Agent.rotation (世界): " + agentTransform.rotation.eulerAngles);
                    DevLog("[" + prefix + "] Agent.forward: " + agentTransform.forward);
                    DevLog("[" + prefix + "] Agent.right: " + agentTransform.right);
                    DevLog("[" + prefix + "] Agent.up: " + agentTransform.up);
                    
                    // 输出父对象信息
                    if (agentTransform.parent != null)
                    {
                        DevLog("[" + prefix + "] Agent.parent: " + agentTransform.parent.name);
                        DevLog("[" + prefix + "] Parent.localRotation: " + agentTransform.parent.localRotation.eulerAngles);
                    }
                    
                    // 输出 handheldSocket 和 handAnimationType
                    DuckovItemAgent duckovAgent = gunAgent as DuckovItemAgent;
                    if (duckovAgent != null)
                    {
                        DevLog("[" + prefix + "] handheldSocket: " + duckovAgent.handheldSocket);
                        DevLog("[" + prefix + "] handAnimationType: " + duckovAgent.handAnimationType);
                    }
                    
                    // 输出所有子对象的Transform信息
                    DevLog("[" + prefix + "] === 子对象Transform详情 ===");
                    LogChildTransforms(agentTransform, prefix, 0, 3);
                    
                    LogGameObjectVisualEffects(gunAgent.gameObject, prefix, "ItemAgent");
                }
                else
                {
                    DevLog("[" + prefix + "] ItemAgent: 未获取到（武器可能不在手上）");
                }
                
                // 输出 Item 对象的特效信息
                LogGameObjectVisualEffects(weaponGO, prefix, "Item");
            }
            catch (Exception e)
            {
                DevLog("[" + prefix + "] 输出视觉特效信息出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 输出指定GameObject上的粒子系统和灯光信息
        /// </summary>
        private void LogGameObjectVisualEffects(GameObject go, string prefix, string source)
        {
            if (go == null) return;
            
            try
            {
                // 查找所有粒子系统（使用Component基类避免直接引用ParticleSystem）
                var particles = go.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && c.GetType().Name == "ParticleSystem")
                    .ToArray();
                    
                if (particles != null && particles.Length > 0)
                {
                    DevLog("[" + prefix + "] [" + source + "] 粒子系统数量: " + particles.Length);
                    for (int i = 0; i < particles.Length; i++)
                    {
                        var ps = particles[i];
                        if (ps == null) continue;
                        
                        string psPath = GetTransformPath(ps.transform, go.transform);
                        
                        // 使用反射获取粒子系统属性
                        bool isPlaying = false;
                        bool isEmitting = false;
                        bool loop = false;
                        float duration = 0f;
                        
                        try
                        {
                            var psType = ps.GetType();
                            var isPlayingProp = psType.GetProperty("isPlaying");
                            var isEmittingProp = psType.GetProperty("isEmitting");
                            var mainProp = psType.GetProperty("main");
                            
                            if (isPlayingProp != null) isPlaying = (bool)isPlayingProp.GetValue(ps, null);
                            if (isEmittingProp != null) isEmitting = (bool)isEmittingProp.GetValue(ps, null);
                            
                            if (mainProp != null)
                            {
                                var main = mainProp.GetValue(ps, null);
                                if (main != null)
                                {
                                    var mainType = main.GetType();
                                    var loopProp = mainType.GetProperty("loop");
                                    var durationProp = mainType.GetProperty("duration");
                                    if (loopProp != null) loop = (bool)loopProp.GetValue(main, null);
                                    if (durationProp != null) duration = (float)durationProp.GetValue(main, null);
                                }
                            }
                        }
                        catch { }
                        
                        DevLog("[" + prefix + "] [" + source + "] 粒子#" + (i+1) + ": " + ps.name + 
                               " (路径: " + psPath + ")" +
                               " [播放:" + isPlaying + ", 发射:" + isEmitting + 
                               ", 循环:" + loop + ", 时长:" + duration.ToString("F2") + "s]");
                    }
                }
                else
                {
                    DevLog("[" + prefix + "] [" + source + "] 无粒子系统");
                }
                
                // 查找所有Light组件（发光效果）
                Light[] lights = go.GetComponentsInChildren<Light>(true);
                if (lights != null && lights.Length > 0)
                {
                    DevLog("[" + prefix + "] [" + source + "] 灯光数量: " + lights.Length);
                    for (int i = 0; i < lights.Length; i++)
                    {
                        var light = lights[i];
                        if (light == null) continue;
                        
                        string lightPath = GetTransformPath(light.transform, go.transform);
                        DevLog("[" + prefix + "] [" + source + "] 灯光#" + (i+1) + ": " + light.name +
                               " (路径: " + lightPath + ")" +
                               " [类型:" + light.type + ", 颜色:" + light.color + 
                               ", 强度:" + light.intensity.ToString("F2") + ", 范围:" + light.range.ToString("F2") + "]");
                    }
                }
                
                // 输出子对象层级结构（仅前3层）
                DevLog("[" + prefix + "] [" + source + "] === 子对象层级 ===");
                LogChildHierarchy(go.transform, prefix, 0, 3);
            }
            catch (Exception e)
            {
                DevLog("[" + prefix + "] [" + source + "] 输出特效信息出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取相对于根对象的Transform路径
        /// </summary>
        private string GetTransformPath(Transform target, Transform root)
        {
            if (target == null) return "";
            if (target == root) return target.name;
            
            string path = target.name;
            Transform current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
        
        /// <summary>
        /// 递归输出子对象层级
        /// </summary>
        private void LogChildHierarchy(Transform parent, string prefix, int depth, int maxDepth)
        {
            if (parent == null || depth >= maxDepth) return;
            
            string indent = new string(' ', depth * 2);
            
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null) continue;
                
                // 收集组件信息
                var components = child.GetComponents<Component>();
                string compStr = "";
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    string typeName = comp.GetType().Name;
                    // 跳过Transform
                    if (typeName == "Transform") continue;
                    if (compStr.Length > 0) compStr += ", ";
                    compStr += typeName;
                }
                
                string activeStr = child.gameObject.activeSelf ? "" : " [禁用]";
                DevLog("[" + prefix + "] " + indent + "├─ " + child.name + activeStr + 
                       (compStr.Length > 0 ? " (" + compStr + ")" : ""));
                
                // 递归子对象
                LogChildHierarchy(child, prefix, depth + 1, maxDepth);
            }
        }
        
        /// <summary>
        /// 递归输出子对象的Transform详细信息（用于调试武器朝向问题）
        /// </summary>
        private void LogChildTransforms(Transform parent, string prefix, int depth, int maxDepth)
        {
            if (parent == null || depth >= maxDepth) return;
            
            string indent = new string(' ', depth * 2);
            
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null) continue;
                
                string activeStr = child.gameObject.activeSelf ? "" : "[禁用]";
                DevLog("[" + prefix + "] " + indent + "├─ " + child.name + " " + activeStr);
                DevLog("[" + prefix + "] " + indent + "   localPos: " + child.localPosition);
                DevLog("[" + prefix + "] " + indent + "   localRot: " + child.localRotation.eulerAngles);
                DevLog("[" + prefix + "] " + indent + "   localScale: " + child.localScale);
                
                // 递归子对象
                LogChildTransforms(child, prefix, depth + 1, maxDepth);
            }
        }
        
        // ============================================================================
        // 帧率显示（DevMode）
        // ============================================================================
        
        // 帧率计算变量
        private float fpsUpdateInterval = 0.5f;  // 更新间隔（秒）
        private float fpsAccumulator = 0f;       // 帧时间累加器
        private int fpsFrameCount = 0;           // 帧计数
        private float currentFps = 0f;           // 当前帧率
        private float fpsTimeSinceLastUpdate = 0f;  // 距上次更新的时间
        private GUIStyle fpsLabelStyle = null;   // 帧率显示样式（缓存）
        
        /// <summary>
        /// 更新帧率计算（在 Update 中调用）
        /// </summary>
        private void UpdateFpsCounter()
        {
            if (!DevModeEnabled) return;
            
            fpsTimeSinceLastUpdate += Time.unscaledDeltaTime;
            fpsAccumulator += Time.unscaledDeltaTime;
            fpsFrameCount++;
            
            // 每隔 fpsUpdateInterval 秒更新一次帧率
            if (fpsTimeSinceLastUpdate >= fpsUpdateInterval)
            {
                currentFps = fpsFrameCount / fpsAccumulator;
                fpsFrameCount = 0;
                fpsAccumulator = 0f;
                fpsTimeSinceLastUpdate = 0f;
            }
        }
        
        /// <summary>
        /// 绘制帧率显示（在 OnGUI 中调用）
        /// </summary>
        private void DrawFpsCounter()
        {
            if (!DevModeEnabled) return;
            
            // 初始化样式（仅一次）
            if (fpsLabelStyle == null)
            {
                fpsLabelStyle = new GUIStyle(GUI.skin.label);
                fpsLabelStyle.fontSize = 16;
                fpsLabelStyle.fontStyle = FontStyle.Bold;
                fpsLabelStyle.normal.textColor = Color.white;
            }
            
            // 根据帧率设置颜色
            if (currentFps >= 60f)
            {
                fpsLabelStyle.normal.textColor = Color.green;
            }
            else if (currentFps >= 30f)
            {
                fpsLabelStyle.normal.textColor = Color.yellow;
            }
            else
            {
                fpsLabelStyle.normal.textColor = Color.red;
            }
            
            // 在屏幕左上角显示帧率
            string fpsText = "FPS: " + currentFps.ToString("F1");
            GUI.Label(new Rect(10, 10, 120, 30), fpsText, fpsLabelStyle);
        }
    }
}
