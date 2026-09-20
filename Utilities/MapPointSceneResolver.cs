// ============================================================================
// MapPointSceneResolver.cs - 官方地图标记的场景参数解析（跨模块）
// ============================================================================
// 调用方：Mode F 撤离点、丧尸模式安全区、天空岛序章的零号区目标。
// 三个模块原本各写一份，这里收成一份（根 AGENTS.md §4.9：跨模块基础设施放 Utilities/）。
// ============================================================================

using System;
using Duckov.Scenes;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 解析 <c>SimplePointOfInterest.Create(position, sceneID, ...)</c> 的第二个参数。
    ///
    /// 【为什么不能直接传场景名】官方 <c>MiniMapDisplay.HandlePointOfInterest</c> 只绘制
    /// <c>targetSceneIndex == MultiSceneCore.ActiveSubScene.Value.buildIndex</c> 的标记，其中 targetSceneIndex 取自
    /// <c>SimplePointOfInterest.OverrideScene</c> = <c>SceneInfoCollection.GetBuildIndex(overrideSceneID)</c>。
    /// 这个参数要的是官方场景表里的**场景 ID**，不是 Unity 场景资源名。ID 查不到时 <c>GetBuildIndex</c> 返回 -1，
    /// <c>OverrideScene</c> 退化成「对象自己所在的场景」，而 <c>Create</c> 又会把对象搬进 <c>MultiSceneCore.MainScene</c>，
    /// 于是和当前子场景永远对不上——**地图上整条标记不显示，日志里也没有任何报错**。
    ///
    /// 【两者确实会不一样】2026-09-20 从 <c>Duckov_Data/resources.assets</c> 里实读官方 <c>SceneInfoCollection</c>
    /// （条目是 id / SceneReference GUID / displayName key 三元组）核对：大多数条目的 id 与场景资源名相同
    /// （`Level_GroundZero_Main`、`Level_GroundZero_1`、`Level_GroundZero_Cave` 都是），但至少有三条不同：
    ///
    ///   id `Base_SceneV2_2`      -> Assets/Scenes/Base_SceneV2_Sub_01.unity
    ///   id `Level_Factory_Main`  -> Assets/Scenes/Factory/Factory_Main.unity
    ///   id `Prepare`             -> Assets/Scenes/PREPARE.unity
    ///
    /// 所以「零号区传场景名也能工作」只是因为那三条恰好同名，不是可以依赖的规律：基地的子场景就是反例。
    ///
    /// 【解析顺序】
    ///   1. 按当前激活场景的 buildIndex 反查官方 ID——这一条永远正确，因为它直接来自官方场景表；
    ///   2. <c>MultiSceneCore.ActiveSubSceneID</c>（<c>SubSceneEntry.sceneID</c> 本身就是官方 ID）；
    ///   3. 实在都拿不到时才退回场景名，让调用方至少有个非空值。
    /// </summary>
    internal static class MapPointSceneResolver
    {
        /// <summary>当前应当写进 POI 的官方场景 ID；一个都解析不出来时返回 null。</summary>
        internal static string Resolve()
        {
            string activeName = null;
            try
            {
                Scene active = SceneManager.GetActiveScene();
                activeName = active.name;
                string byBuildIndex = SceneInfoCollection.GetSceneID(active.buildIndex);
                if (!string.IsNullOrEmpty(byBuildIndex)) return byBuildIndex;
            }
            catch (Exception)
            {
                // 官方场景表尚未就绪，走下面两条退路
            }
            try
            {
                string subScene = MultiSceneCore.ActiveSubSceneID;
                if (!string.IsNullOrEmpty(subScene)) return subScene;
            }
            catch (Exception)
            {
                // 同上
            }
            return string.IsNullOrEmpty(activeName) ? null : activeName;
        }

        /// <summary>解析不出来时用调用方自己的兜底值。</summary>
        internal static string Resolve(string fallback)
        {
            string resolved = Resolve();
            return string.IsNullOrEmpty(resolved) ? fallback : resolved;
        }
    }
}
