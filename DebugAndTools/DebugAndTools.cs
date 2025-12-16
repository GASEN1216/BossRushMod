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
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 调试工具模块
    /// </summary>
    public partial class ModBehaviour
    {
        // 交互调试监听是否已注册
        private static bool interactDebugListenerRegistered = false;
        
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
        // 预览材质（半透明）
        private static Material previewMaterial = null;
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
                Debug.LogError("[BossRush] 注册交互调试监听失败: " + e.Message);
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
                Debug.LogError("[BossRush] 交互调试输出失败: " + e.Message);
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
        /// 找到 Transform 的根 Prefab（向上查找到 Scene 下的第一层子对象）
        /// 例如：Scene/Group_Fact/Prfb_Roadblock_1/Default_2 -> Prfb_Roadblock_1
        /// </summary>
        /// <param name="t">目标 Transform</param>
        /// <returns>根 Prefab 的 Transform，如果找不到则返回 null</returns>
        private static Transform FindPrefabRoot(Transform t)
        {
            if (t == null) return null;
            
            // 向上查找，直到父对象的名字是 "Scene" 或没有父对象
            Transform current = t;
            Transform lastValid = t;
            
            while (current.parent != null)
            {
                string parentName = current.parent.name;
                // 如果父对象是 Scene 或类似的根容器，当前对象就是 Prefab 根
                if (parentName == "Scene" || parentName.StartsWith("Scene_") || 
                    parentName.StartsWith("Level_") || parentName.StartsWith("Group_"))
                {
                    return current;
                }
                lastValid = current;
                current = current.parent;
            }
            
            // 如果没有找到 Scene，返回最顶层的对象
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
                        // 只添加包含 BoxCollider 组件的 GameObject
                        if (t.gameObject.GetComponent<UnityEngine.BoxCollider>() != null)
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
                Debug.LogError("[BossRush] F5 调试：LogNearbyGameObjects 出错: " + e.Message);
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
                Debug.LogError("[BossRush] F6 调试：CloneRememberedGameObject 出错: " + e.Message);
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
                Debug.LogError("[BossRush] F5 建筑调试出错: " + e.Message + "\n" + e.StackTrace);
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
                Debug.LogError("[BossRush] 进入放置模式失败: " + e.Message);
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
                Debug.LogError("[BossRush] 更新放置模式失败: " + e.Message);
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
                Debug.LogError("[BossRush] 右键处理失败: " + e.Message);
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
                Debug.LogError("[BossRush] 确认放置失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 检查放置模式是否激活（供外部调用）
        /// </summary>
        public static bool IsPlacementModeActive()
        {
            return placementModeActive;
        }
    }
}
