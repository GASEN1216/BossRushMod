using System;
using Duckov.Utilities;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// WIRE+：把官方爆炸的遮挡检测搬到天空岛的实际海拔上。**只在天空岛独立出击场景里生效**。
    ///
    /// 官方 `ExplosionManager.CheckObsticle` 把射线的起终点 y **硬编码成 0.5**：
    ///
    /// <code>
    ///   startPoint.y = 0.5f;
    ///   endPoint.y   = 0.5f;
    ///   return Physics.RaycastNonAlloc(..., wallLayerMask | groundLayerMask) &gt; 0;
    /// </code>
    ///
    /// 这个 0.5 是按原版地面在 y≈0 写死的「齐腰高度的水平视线」。天空岛的 12 个岛按
    /// `ArtSource/SkyIsland/layout.json`（布局 v2）分布在 y = 0 / 5 / 7 / 10 / 13 / 15 / 16 / 18 / 21 / 26，
    /// 除登云码头以外**没有任何一块地面在 0.5 米附近**，射线整条跑在岛体下方的空气里、
    /// 恒定打空、`CheckObsticle` 恒返回 false —— 于是：
    ///
    /// - 官方 `CreateExplosion` 又没有距离衰减，圈内一律吃满伤；
    /// - 噬风的三段风暴脉冲隔着墙、隔着岩壁照样打满，躲掩体完全没有意义；
    /// - 玩家和敌人的手雷、套装反击爆炸同样穿墙，双向失衡。
    ///
    /// **修法刻意保持与官方数值逐位一致**：把两点压到
    /// <c>min(startPoint.y, endPoint.y) + 0.3</c>。官方传进来的是
    /// `爆点 + 0.2` 与 `目标 + 0.6`，所以在 y=0 的平地上算出来正好是 <b>0.5</b>，
    /// 与原版一个比特都不差；只有地面被抬高时才跟着抬高。
    ///
    /// **为什么还要按场景门控**：原版部分地图也有抬高的平台，那里的 0.5 是官方调过的手感，
    /// 不属于本次任务范围。门控让这个补丁在官方地图上彻底不执行（`armed` 为 false 时
    /// 直接 `return true` 交还原方法），只在天空岛把它换掉。
    /// 句柄由 <see cref="SkyIslandRaidLease"/> 在 raid 场景加载时登记、卸载时撤销；
    /// 即使撤销漏掉，活动场景句柄对不上也不会误伤别的地图。
    /// </summary>
    [HarmonyPatch(typeof(ExplosionManager), "CheckObsticle")]
    internal static class SkyIslandExplosionObstaclePatch
    {
        /// <summary>官方在 y=0 平地上的等效高度：`min(0.2, 0.6) + 0.3 == 0.5`。</summary>
        internal const float EyeHeightAboveLower = 0.3f;

        private static bool armed;
        private static int armedSceneHandle;

        /// <summary>raid 场景就位时登记它的句柄。重复调用幂等。</summary>
        internal static void Arm(Scene scene)
        {
            if (!scene.IsValid()) return;
            armedSceneHandle = scene.handle;
            armed = true;
        }

        /// <summary>场景卸载或租约释放时撤销。重复调用幂等。</summary>
        internal static void Disarm()
        {
            armed = false;
            armedSceneHandle = 0;
        }

        /// <summary>
        /// 补丁此刻是否**真的**对这个场景生效。只读，给 F3 岛内验收用。
        ///
        /// 判据与 <see cref="Prefix"/> 的前两行逐字一致（`armed` + 句柄相等），
        /// 不是「登记过没有」而是「这一帧调用官方 CheckObsticle 会不会走到我们这条路径」。
        /// CR-2026-09-09-006 是「Fixed（待实机）」，没有这个入口，人在岛内也无从确认它挂上了。
        /// </summary>
        internal static bool IsArmedFor(Scene scene)
        {
            return armed && scene.IsValid() && scene.handle == armedSceneHandle;
        }

        /// <summary>纯逻辑：给定官方传入的两点，返回压平后共用的 y。隔离回归直接验证它在平地上等于 0.5。</summary>
        internal static float FlattenHeight(float startY, float endY)
        {
            return Mathf.Min(startY, endY) + EyeHeightAboveLower;
        }

        [HarmonyPrefix]
        private static bool Prefix(Vector3 startPoint, Vector3 endPoint, ref bool __result)
        {
            // 未在天空岛：一个 bool 读，随后原方法照常执行，官方地图零行为变化。
            if (!armed) return true;
            try
            {
                // 句柄比较不分配（Scene 是只含 handle 的结构体），也不依赖场景名/路径字符串。
                if (SceneManager.GetActiveScene().handle != armedSceneHandle) return true;
                float y = FlattenHeight(startPoint.y, endPoint.y);
                startPoint.y = y;
                endPoint.y = y;
                Vector3 delta = endPoint - startPoint;
                float distance = delta.magnitude;
                // 与官方同址爆炸的退化情形一致：零长射线打不到任何东西，视为无遮挡。
                if (distance <= 0.0001f) { __result = false; return false; }
                // 层掩码与官方 `obsticleLayers` 同一组合；同样不传 QueryTriggerInteraction，沿用全局设置。
                int mask = GameplayDataSettings.Layers.wallLayerMask.value |
                    GameplayDataSettings.Layers.groundLayerMask.value;
                __result = Physics.Raycast(new Ray(startPoint, delta / distance), distance, mask);
                return false;
            }
            catch (Exception e)
            {
                // 任何意外都退回官方实现：宁可遮挡判定不准，也不能让一次爆炸抛穿官方伤害循环。
                Debug.LogWarning("[SkyIsland] 爆炸遮挡检测回退官方实现：" + e.Message);
                return true;
            }
        }
    }
}
