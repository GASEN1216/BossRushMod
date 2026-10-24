using System;
using Duckov.Scenes;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>37 号探索遭遇的场景归属和有限候选；不以 Unity 的活动场景代替关卡 owner。</summary>
    internal static class EclipseBloomEncounterPlacement
    {
        internal const string SceneName = "Level_SnowMilitaryBase";
        internal const string MainSceneName = "Level_SnowMilitaryBase_Main";
        private static readonly Vector3 Anchor = new Vector3(481.5f, 0.04f, 549.5f);
        // 地图目标圈半径 22 m；候选从圈心按黄金角螺旋铺到 18 m，都落在玩家看得到的圈里。
        // 原先只有圈心 3 m 内 5 个点，军事基地里 14 m 内总有箱子、车辆或墙，五点全被净空检查否掉，花冠永远不刷。
        private const int SpiralCount = 24;
        private const float SpiralRadius = 18f;

        internal static int CandidateCount { get { return SpiralCount; } }
        internal static Vector3 ObjectiveAnchor { get { return Anchor; } }

        private static Vector3 Candidate(int index)
        {
            if (index <= 0) return Anchor;
            float radius = SpiralRadius * Mathf.Sqrt(index / (float)(SpiralCount - 1));
            float angle = index * 2.39996323f;
            return Anchor + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
        }

        internal static bool IsLoaded(Scene scene)
        {
            return scene.IsValid() && scene.isLoaded;
        }

        internal static bool TryGetLevelOwnerScene(out Scene scene)
        {
            scene = default(Scene);
            LevelManager level = LevelManager.Instance;
            if (level == null || level.IsBaseLevel || !IsLoaded(level.gameObject.scene)) return false;
            // 官方 MultiSceneCore.MainScene 本身就返回 Instance.gameObject.scene；
            // LevelManager 没有 Scene 属性，单场景关卡则由它的 GameObject 承担 owner。
            MultiSceneCore core = MultiSceneCore.Instance;
            scene = core != null ? core.gameObject.scene : level.gameObject.scene;
            return IsLoaded(scene);
        }

        internal static bool TryGetExplorationScene(out Scene scene)
        {
            scene = default(Scene);
            Scene levelScene;
            if (!TryGetLevelOwnerScene(out levelScene)
                || (!string.Equals(levelScene.name, MainSceneName, StringComparison.Ordinal)
                    && !string.Equals(levelScene.name, SceneName, StringComparison.Ordinal))) return false;
            // 主场景先到并不代表地图子场景已可用；不借活动场景或其它已加载地图兜底。
            scene = string.Equals(levelScene.name, SceneName, StringComparison.Ordinal)
                ? levelScene : SceneManager.GetSceneByName(SceneName);
            return IsLoaded(scene) && string.Equals(scene.name, SceneName, StringComparison.Ordinal)
                && ModBehaviour.GetMapConfigBySceneName(scene.name) != null;
        }

        internal static bool TryGetSpawnScene(out Scene scene)
        {
            Scene ownerScene;
            if (!TryGetLevelOwnerScene(out ownerScene)) { scene = default(Scene); return false; }
            Scene? subScene = MultiSceneCore.ActiveSubScene;
            scene = subScene.HasValue && IsLoaded(subScene.Value) ? subScene.Value : ownerScene;
            return IsLoaded(scene);
        }

        internal static bool IsCurrent(Scene encounterScene, Scene levelScene, LevelManager levelOwner)
        {
            Scene currentLevel;
            return levelOwner != null && LevelManager.Instance == levelOwner && !SceneLoader.IsSceneLoading
                && LevelManager.AfterInit
                && IsLoaded(encounterScene) && TryGetLevelOwnerScene(out currentLevel)
                && currentLevel.handle == levelScene.handle;
        }

        internal static bool IsLoadingSubScene
        {
            get { return MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading; }
        }

        internal static void BindToScene(GameObject instance, Scene scene)
        {
            if (instance == null || !IsLoaded(scene)) throw new InvalidOperationException("eclipse_encounter_scene_unavailable");
            // CreateCharacterAsync 会把角色挂在主场景的 active-with-scene 容器下。
            // 显式解除该父级再迁入目标子场景，使卸载能够销毁角色及其独立部位。
            if (instance.scene.handle == scene.handle) return;
            if (instance.transform.parent != null) instance.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(instance, scene);
            if (instance.scene.handle != scene.handle) throw new InvalidOperationException("eclipse_encounter_scene_binding_failed");
        }

        /// <summary>
        /// 每帧至多试一个候选。花瓣圈（3.8 m）必须基本净空，移动圈（8 m）大部分可走，
        /// 13 m 伤害外缘只要一半可走：扇区碰到墙或台阶时地面网格本来就会裁掉、判伤也查遮挡。
        /// relaxed 在一整轮候选都失败后由调用方打开，再降一档门槛；连通性仍由实际 A* 图证明。
        /// </summary>
        internal static bool TryResolveCandidate(int index, Vector3 player, bool relaxed, out Vector3 position)
        {
            position = Vector3.zero;
            if (index < 0 || index >= SpiralCount) return false;
            Vector3 raw = Candidate(index);
            Vector3 approach = raw - player;
            approach.y = 0f;
            if (approach.sqrMagnitude > 70f * 70f || approach.sqrMagnitude < 36f) return false;
            if (!SpawnPositionHelper.TryResolveReachableFrom(raw, player, 1.5f,
                    SpawnPositionHelper.DefaultLiftOffset, out position)) return false;
            Vector3 delta = position - player;
            delta.y = 0f;
            if (delta.sqrMagnitude < 36f || delta.sqrMagnitude > 70f * 70f) return false;
            return RingClear(position, 3.8f, 12, relaxed ? 10 : 12, 0.7f)
                && RingClear(position, 8f, 16, relaxed ? 9 : 12, 0.9f)
                && RingClear(position, EclipseBloomTuning.OuterRadius, 16, relaxed ? 5 : 8, 1.5f);
        }

        private static bool RingClear(Vector3 center, float radius, int samples, int required, float heightTolerance)
        {
            int passed = 0;
            for (int i = 0; i < samples; i++)
            {
                // 剩下的样本全过也达不到门槛时提前结束，省掉无意义的寻路查询。
                if (passed + (samples - i) < required) return false;
                float angle = i * Mathf.PI * 2f / samples;
                Vector3 edge = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                Vector3 grounded;
                if (SpawnPositionHelper.TryResolveReachableFrom(edge, center, 0.9f, 0f, out grounded)
                    && Mathf.Abs(grounded.y - center.y) <= heightTolerance
                    && !Physics.Linecast(center + Vector3.up, edge + Vector3.up,
                        GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) passed++;
            }
            return passed >= required;
        }
    }
}
