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
//   - F11: 查看背包物品详细信息（TypeID、Tags、品质、价值等）
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
            if (!HardcodedDevModeEnabled || !DevModeEnabled) return;
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

        private void ApplyDevModeRuntimeState()
        {
            if (DevModeEnabled)
            {
                RegisterInteractDebugListener();
                RegisterShootDebugListener();

                InventoryInspector inspector = GetComponent<InventoryInspector>();
                if (inspector == null)
                {
                    inspector = gameObject.AddComponent<InventoryInspector>();
                }
                else
                {
                    inspector.enabled = true;
                }

                return;
            }

            UnregisterInteractDebugListener();
            UnregisterShootDebugListener();

            InventoryInspector existingInspector = GetComponent<InventoryInspector>();
            if (existingInspector != null)
            {
                existingInspector.enabled = false;
            }
        }

        /// <summary>
        /// 开枪事件回调：输出枪口火焰和声音的实际值（用于调试龙息武器等自定义武器）
        /// </summary>
        /// <param name="gunAgent">开枪的枪械Agent</param>
        private static void OnMainCharacterShootDebug(ItemAgent_Gun gunAgent)
        {
            if (!HardcodedDevModeEnabled || !DevModeEnabled) return;
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
        internal const bool ModeEStartupProfilingEnabled = true;
        internal const bool ModeEFSpawnProfilingEnabled = true;
        internal const bool VerboseStartupDebugLogsEnabled = true;

        /// <summary>
        /// 当前是否启用开发日志输出
        /// </summary>
        internal static bool IsDevLoggingEnabled
        {
            get { return DevModeEnabled && VerboseStartupDebugLogsEnabled; }
        }

        /// <summary>
        /// 常规信息日志（始终通过 BossRush 自有日志入口输出）
        /// </summary>
        internal static void LogInfo(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Debug.Log(message);
        }

        /// <summary>
        /// 常规警告日志（始终通过 BossRush 自有日志入口输出）
        /// </summary>
        internal static void LogWarning(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Debug.LogWarning(message);
        }

        /// <summary>
        /// 常规错误日志（始终通过 BossRush 自有日志入口输出）
        /// </summary>
        internal static void LogError(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Debug.LogError(message);
        }

        /// <summary>
        /// 开发模式日志输出（仅在 DevModeEnabled = true 时输出）
        /// </summary>
        /// <param name="message">日志消息</param>
        [System.Diagnostics.Conditional("BOSSRUSH_DEV")]
        internal static void DevLog(string message)
        {
            if (!DevModeEnabled || string.IsNullOrEmpty(message))
            {
                return;
            }

            bool shouldLog =
                IsDevLoggingEnabled ||
                message.IndexOf("[WARNING]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("[ERROR]", StringComparison.OrdinalIgnoreCase) >= 0;

            if (shouldLog)
            {
                LogInfo(message);
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

    }
}
