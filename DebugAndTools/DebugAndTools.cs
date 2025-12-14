// ============================================================================
// DebugAndTools.cs - 调试工具
// ============================================================================
// 模块说明：
//   提供 BossRush 模组的调试工具和辅助方法，包括：
//   - DevLog: 开发模式日志输出
//   - GetTransformPath: 获取 Transform 的完整层级路径
//   - LogNearbyGameObjects: 输出玩家附近的 GameObject 信息
//   - OnInteractStartDebug: 交互事件监听，输出详细交互信息
//   
// 调试快捷键（仅在 DevModeEnabled = true 时生效）：
//   - F5: 输出玩家脚下的 GameObject
//   - F7: 输出最近的交互点信息
//   - F8: 输出场景中所有非玩家角色
//   - F9: 直接开始 BossRush
//   - F10: 直接触发通关
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
        private static void DevLog(string message)
        {
            if (DevModeEnabled)
            {
                Debug.Log(message);
            }
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

                List<Transform> nearby = new List<Transform>();

                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null)
                    {
                        continue;
                    }

                    Vector3 pos = t.position;
                    float distSq = (pos - playerPos).sqrMagnitude;

                    if (distSq <= radiusSq && distSq > 0.0001f)
                    {
                        nearby.Add(t);
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
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[BossRush] F5 调试：LogNearbyGameObjects 出错: " + e.Message);
            }
        }
    }
}
