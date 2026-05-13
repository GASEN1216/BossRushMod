// ============================================================================
// DragonBreathWeaponConfig.cs - 龙息武器完整配置
// ============================================================================
// 模块说明：
//   配置龙息武器的配件槽位、弹药类型、耐久度、标签和属性
//   属性值比MCX Super略强，作为龙裔遗族Boss专属掉落
//   支持运行时配置（当玩家装备武器时自动应用）
// ============================================================================

using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using Duckov.Utilities;

namespace BossRush
{
    public static partial class DragonBreathWeaponConfig
    {
        /// <summary>
        /// 为龙息武器的ItemAgent添加火焰特效（从带火AK-47复制）
        /// 在玩家手持龙息武器时调用
        /// </summary>
        public static void TryAddFireEffectsToAgent(ItemAgent_Gun gunAgent)
        {
            if (gunAgent == null) return;
            if (gunAgent.Item == null) return;
            if (gunAgent.Item.TypeID != WEAPON_TYPE_ID) return;

            int agentInstanceId = gunAgent.GetInstanceID();

            // 检查是否已添加过特效
            if (effectsAddedAgents.Contains(agentInstanceId)) return;

            try
            {
                // 从预制体获取带火AK-47的模型
                GameObject sourceModel = GetFireAK47Model();

                // 如果找不到源模型，静默返回（日志已在GetFireAK47Model中输出一次）
                if (sourceModel == null) return;

                // 复制特效
                CopyFireEffects(sourceModel, gunAgent.gameObject);
                effectsAddedAgents.Add(agentInstanceId);
                ModBehaviour.DevLog("[DragonBreathWeapon] 已为龙息武器添加火焰特效");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 添加火焰特效失败: " + e.Message);
            }
        }

        /// <summary>
        /// 为展示家具（人体模特、武器展示柜等）上的龙息武器添加火焰特效
        /// 通过 Item 的 ActiveAgent 或 ItemGraphic 找到视觉模型，复用 CopyFireEffects 逻辑
        /// 在 Item.OnEnable 时调用
        /// </summary>
        public static void TryAddFireEffectsToDisplay(Item item)
        {
            if (item == null) return;
            if (item.TypeID != WEAPON_TYPE_ID) return;

            // 查找目标视觉对象
            GameObject targetVisual = null;

            // 优先使用 ActiveAgent（展示家具通常会创建一个 ItemAgent）
            if (item.AgentUtilities != null && item.AgentUtilities.ActiveAgent != null)
            {
                var agent = item.AgentUtilities.ActiveAgent;
                int agentInstanceId = agent.GetInstanceID();

                // 检查是否已添加过特效
                if (effectsAddedAgents.Contains(agentInstanceId)) return;

                targetVisual = agent.gameObject;

                try
                {
                    GameObject sourceModel = GetFireAK47Model();
                    if (sourceModel == null) return;

                    CopyFireEffects(sourceModel, targetVisual);
                    effectsAddedAgents.Add(agentInstanceId);
                    ModBehaviour.DevLog("[DragonBreathWeapon] 已为展示中的龙息武器添加火焰特效 (Agent: " + agent.name + ")");
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 展示火焰特效添加失败 (Agent): " + e.Message);
                }
                return;
            }

            // 备选：使用 ItemGraphic（某些展示方式可能直接用 ItemGraphic）
            if (item.ItemGraphic != null)
            {
                targetVisual = item.ItemGraphic.gameObject;
                int graphicInstanceId = targetVisual.GetInstanceID();

                // 检查是否已添加过特效
                if (effectsAddedAgents.Contains(graphicInstanceId)) return;

                try
                {
                    GameObject sourceModel = GetFireAK47Model();
                    if (sourceModel == null) return;

                    CopyFireEffects(sourceModel, targetVisual);
                    effectsAddedAgents.Add(graphicInstanceId);
                    ModBehaviour.DevLog("[DragonBreathWeapon] 已为展示中的龙息武器添加火焰特效 (Graphic: " + targetVisual.name + ")");
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 展示火焰特效添加失败 (Graphic): " + e.Message);
                }
            }
        }

        /// <summary>
        /// 为展示家具创建的 ItemGraphic 视觉模型添加火焰特效
        /// Showcase.RefreshSlot 通过 ItemGraphicInfo.CreateAGraphic 创建视觉模型，
        /// 不经过 ItemAgent 系统，所以需要直接对 GameObject 操作
        /// </summary>
        public static void TryAddFireEffectsToGraphic(GameObject targetVisual)
        {
            if (targetVisual == null) return;

            int instanceId = targetVisual.GetInstanceID();

            // 检查是否已添加过特效
            if (effectsAddedAgents.Contains(instanceId)) return;

            try
            {
                GameObject sourceModel = GetFireAK47Model();
                if (sourceModel == null) return;

                CopyFireEffects(sourceModel, targetVisual);
                effectsAddedAgents.Add(instanceId);
                ModBehaviour.DevLog("[DragonBreathWeapon] 已为展示家具中的龙息武器添加火焰特效: " + targetVisual.name);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 展示家具火焰特效添加失败: " + e.Message);
            }
        }


        /// <summary>
        /// 获取带火AK-47的模型（从预制体，带缓存）
        /// </summary>
        private static GameObject GetFireAK47Model()
        {
            // 使用缓存
            if (cachedFireAK47Model != null) return cachedFireAK47Model;

            // 如果已经尝试过查找但失败了，直接返回null（避免重复查找）
            if (fireAK47ModelSearched) return null;

            // 标记已尝试查找
            fireAK47ModelSearched = true;

            try
            {
                // 从ItemAssetsCollection获取带火AK-47的预制体
                var prefab = ItemAssetsCollection.GetPrefab(FIRE_AK47_TYPE_ID);
                if (prefab == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 未找到带火AK-47预制体 (TypeID=" + FIRE_AK47_TYPE_ID + ")");
                    return null;
                }

                // 通过 ItemGraphic 获取模型（游戏更新后的标准方式，与 CreateHandheldAgent 逻辑一致）
                ItemGraphicInfo itemGraphic = prefab.ItemGraphic;
                if (itemGraphic != null)
                {
                    cachedFireAK47Model = itemGraphic.gameObject;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 通过 ItemGraphic 找到带火AK-47模型: " + cachedFireAK47Model.name);
                    return cachedFireAK47Model;
                }

                ModBehaviour.DevLog("[DragonBreathWeapon] 带火AK-47预制体中未找到 ItemGraphic");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 获取带火AK-47模型失败: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 从源对象复制火焰特效到目标对象
        /// 烟雾和火花放在枪身（根节点），发光特效放在Muzzle
        /// </summary>
        private static void CopyFireEffects(GameObject source, GameObject target)
        {
            if (source == null || target == null) return;

            ModBehaviour.DevLog("[DragonBreathWeapon] === 开始复制火焰特效 ===");
            ModBehaviour.DevLog("[DragonBreathWeapon] 源对象: " + source.name);
            ModBehaviour.DevLog("[DragonBreathWeapon] 目标对象: " + target.name);

            // [性能优化] 调试用的层级打印只在DevModeEnabled开启时执行，避免不必要的字符串拼接开销
            if (ModBehaviour.DevModeEnabled)
            {
                PrintChildHierarchy(source.transform, "[源]", 0);
                PrintChildHierarchy(target.transform, "[目标]", 0);
            }

            Transform sourceRoot = source.transform;
            Transform targetRoot = target.transform;

            // 查找Muzzle位置（用于发光特效）
            Transform muzzleTransform = FindChildRecursive(targetRoot, "Muzzle");
            if (muzzleTransform == null)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 警告：未找到Muzzle");
            }

            // 烟雾和火花放在枪身根节点上（平行于枪身）
            CopyParticleSystemToBody(sourceRoot, targetRoot, "Smoke");
            CopyParticleSystemToBody(sourceRoot, targetRoot, "Spark");

            // 发光特效放在Muzzle（如果有的话）
            Transform lightParent = muzzleTransform != null ? muzzleTransform : targetRoot;
            CopySodaPointLights(sourceRoot, lightParent);

            ModBehaviour.DevLog("[DragonBreathWeapon] === 火焰特效复制完成 ===");
        }

        /// <summary>
        /// 复制粒子系统到枪身（根节点），调整位置使其在枪身中心
        /// </summary>
        private static void CopyParticleSystemToBody(Transform source, Transform targetRoot, string name)
        {
            try
            {
                Transform sourcePS = source.Find(name);
                if (sourcePS == null)
                {
                    sourcePS = FindChildRecursive(source, name);
                }

                if (sourcePS == null)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 未找到粒子系统: " + name);
                    return;
                }

                // 检查目标是否已有同名对象
                Transform existingPS = FindChildRecursive(targetRoot, name);
                if (existingPS != null)
                {
                    EnsureParticleSystemPlaying(existingPS.gameObject);
                    ModBehaviour.DevLog("[DragonBreathWeapon] 跳过已存在的粒子系统: " + name);
                    return;
                }

                // 复制粒子系统到根节点
                GameObject copy = UnityEngine.Object.Instantiate(sourcePS.gameObject, targetRoot);
                copy.name = name;

                // 放在枪身中心位置（根据龙息武器模型调整）
                // 枪身大约在 Y=0.1~0.2, Z=0.2~0.5 的范围
                copy.transform.localPosition = new Vector3(0f, 0.15f, 0.35f);
                copy.transform.localRotation = Quaternion.identity;  // 不旋转，平行于枪身
                copy.transform.localScale = sourcePS.localScale;
                copy.SetActive(true);

                EnsureParticleSystemPlaying(copy);

                ModBehaviour.DevLog("[DragonBreathWeapon] 复制粒子系统: " + name +
                    " 到枪身 localPos=" + copy.transform.localPosition);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 复制粒子系统失败 (" + name + "): " + e.Message);
            }
        }

        /// <summary>
        /// 打印子对象层级结构（调试用，最多2层）
        /// [性能优化] 移除LINQ，使用手动循环
        /// </summary>
        private static void PrintChildHierarchy(Transform parent, string prefix, int depth)
        {
            if (depth > 2) return;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                string indent = new string(' ', depth * 2);

                // [性能优化] 手动构建组件名称字符串，避免LINQ分配
                var components = child.GetComponents<Component>();
                string compNames = "";
                int compCount = 0;
                for (int j = 0; j < components.Length && compCount < 3; j++)
                {
                    var c = components[j];
                    if (c != null && !(c is Transform))
                    {
                        if (compCount > 0) compNames += ", ";
                        compNames += c.GetType().Name;
                        compCount++;
                    }
                }

                ModBehaviour.DevLog(prefix + indent + "├─ " + child.name +
                    (string.IsNullOrEmpty(compNames) ? "" : " (" + compNames + ")") +
                    " scale=" + child.localScale);

                PrintChildHierarchy(child, prefix, depth + 1);
            }
        }

        // ========== ParticleSystem反射缓存 ==========
        private static Type cachedPSType = null;
        private static PropertyInfo cachedIsPlayingProp = null;
        private static MethodInfo cachedPlayMethod = null;
        private static bool psReflectionCached = false;

        /// <summary>
        /// 确保粒子系统正在播放（使用反射避免直接引用ParticleSystem类型，带缓存）
        /// </summary>
        private static void EnsureParticleSystemPlaying(GameObject obj)
        {
            if (obj == null) return;

            try
            {
                // 缓存ParticleSystem类型和方法（只执行一次）
                if (!psReflectionCached)
                {
                    CacheParticleSystemReflection();
                }

                if (cachedPSType == null) return;

                // 获取组件
                var ps = obj.GetComponent(cachedPSType);
                if (ps == null) return;

                // 检查是否正在播放
                bool isPlaying = cachedIsPlayingProp != null && (bool)cachedIsPlayingProp.GetValue(ps, null);

                if (!isPlaying && cachedPlayMethod != null)
                {
                    cachedPlayMethod.Invoke(ps, new object[] { true });  // withChildren = true
                    ModBehaviour.DevLog("[DragonBreathWeapon] 启动粒子系统: " + obj.name);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] EnsureParticleSystemPlaying异常: " + e.Message);
            }
        }

        /// <summary>
        /// 缓存ParticleSystem的反射信息（只执行一次）
        /// </summary>
        private static void CacheParticleSystemReflection()
        {
            if (psReflectionCached) return;

            try
            {
                // 获取ParticleSystem类型
                cachedPSType = typeof(Component).Assembly.GetType("UnityEngine.ParticleSystem");
                if (cachedPSType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        cachedPSType = asm.GetType("UnityEngine.ParticleSystem");
                        if (cachedPSType != null) break;
                    }
                }

                if (cachedPSType != null)
                {
                    cachedIsPlayingProp = cachedPSType.GetProperty("isPlaying", BindingFlags.Public | BindingFlags.Instance);
                    cachedPlayMethod = cachedPSType.GetMethod("Play", new Type[] { typeof(bool) });
                }
            }
            catch { }

            psReflectionCached = true;
        }

        // ========== SodaPointLight反射缓存 ==========
        private static Type cachedSodaLightType = null;
        private static MethodInfo cachedSyncToLightMethod = null;
        private static bool sodaLightReflectionCached = false;

        /// <summary>
        /// 复制SodaPointLight组件到目标父对象下
        /// [性能优化] 移除LINQ，使用手动循环
        /// </summary>
        private static void CopySodaPointLights(Transform source, Transform targetParent)
        {
            try
            {
                // [性能优化] 手动查找SodaPointLight，避免LINQ分配
                var allComponents = source.GetComponentsInChildren<Component>(true);
                var sodaLights = new List<Component>();
                for (int i = 0; i < allComponents.Length; i++)
                {
                    var c = allComponents[i];
                    if (c != null && c.GetType().Name == "SodaPointLight")
                    {
                        sodaLights.Add(c);
                    }
                }

                if (sodaLights.Count == 0)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 源对象中未找到SodaPointLight");
                    return;
                }

                ModBehaviour.DevLog("[DragonBreathWeapon] 找到 " + sodaLights.Count + " 个SodaPointLight");
                ModBehaviour.DevLog("[DragonBreathWeapon] 发光点父对象: " + targetParent.name);

                // 缓存SodaPointLight类型和方法（只执行一次）
                if (!sodaLightReflectionCached && sodaLights.Count > 0)
                {
                    cachedSodaLightType = sodaLights[0].GetType();
                    cachedSyncToLightMethod = cachedSodaLightType.GetMethod("SyncToLight",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    sodaLightReflectionCached = true;
                }

                int copyCount = 0;
                for (int idx = 0; idx < sodaLights.Count; idx++)
                {
                    var light = sodaLights[idx];
                    string lightName = light.gameObject.name;
                    string newName = "FireLight_" + copyCount;

                    // 检查目标是否已有同名对象
                    if (FindChildRecursive(targetParent, newName) != null)
                    {
                        ModBehaviour.DevLog("[DragonBreathWeapon] 跳过已存在的: " + newName);
                        copyCount++;
                        continue;
                    }

                    // 记录源对象的缩放信息
                    Vector3 srcLocalScale = light.transform.localScale;
                    Vector3 srcLossyScale = light.transform.lossyScale;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 源 " + lightName +
                        " localScale=" + srcLocalScale + " lossyScale=" + srcLossyScale);

                    // 复制发光点对象到目标父对象
                    GameObject copy = UnityEngine.Object.Instantiate(light.gameObject, targetParent);
                    copy.name = newName;

                    // 将发光点放在原点
                    copy.transform.localPosition = Vector3.zero;
                    copy.transform.localRotation = Quaternion.identity;

                    // 使用和火AK一样的scale（直接使用源对象的scale）
                    copy.transform.localScale = srcLocalScale;
                    copy.SetActive(true);

                    // 调用SodaPointLight的SyncToLight方法来初始化材质属性（使用缓存的方法）
                    if (cachedSyncToLightMethod != null)
                    {
                        var sodaLightComponent = copy.GetComponent(cachedSodaLightType);
                        if (sodaLightComponent != null)
                        {
                            cachedSyncToLightMethod.Invoke(sodaLightComponent, null);
                            ModBehaviour.DevLog("[DragonBreathWeapon] 已调用SyncToLight");
                        }
                    }

                    // 记录复制后的缩放信息
                    Vector3 dstLossyScale = copy.transform.lossyScale;
                    ModBehaviour.DevLog("[DragonBreathWeapon] 复制 " + copy.name +
                        " localScale=" + copy.transform.localScale + " lossyScale=" + dstLossyScale);

                    copyCount++;
                }

                if (copyCount > 0)
                {
                    ModBehaviour.DevLog("[DragonBreathWeapon] 复制发光点: " + copyCount + "个");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonBreathWeapon] 复制发光点失败: " + e.Message);
            }
        }


        /// <summary>
        /// 递归查找子对象
        /// </summary>
        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null) return null;

            Transform found = parent.Find(name);
            if (found != null) return found;

            for (int i = 0; i < parent.childCount; i++)
            {
                found = FindChildRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }
    }
}
