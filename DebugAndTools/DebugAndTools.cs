// ============================================================================
// DebugAndTools.cs - 调试工具
// ============================================================================
// 模块说明：
//   提供 BossRush 模组的调试工具和辅助方法，包括：
//   - DevLog: 开发模式日志输出
//   - GetTransformPath: 获取 Transform 的完整层级路径
//   - LogNearbyGameObjects: 输出玩家附近的 GameObject 信息
//   
// 调试快捷键（仅在 DevModeEnabled = true 时生效）：
//   - F5: 输出玩家脚下的 GameObject
//   - F7: 输出最近的交互点信息
//   - F8: 输出场景中所有非玩家角色
//   - F9: 直接开始 BossRush
//   - F10: 直接触发通关
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 调试工具模块
    /// </summary>
    public partial class ModBehaviour
    {
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
