using System;
using Duckov.Scenes;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>野外趣事共用的普通出击门与落点。暂停不结束委托，计时由各活动 owner 暂停。</summary>
    internal static class FieldAdventureSupport
    {
        internal static bool TryGetRaid(ModBehaviour owner, out CharacterMainControl player, out Scene scene)
        {
            player = null;
            scene = default(Scene);
            try
            {
                LevelManager level = LevelManager.Instance;
                if (owner == null || level == null || level.IsBaseLevel || !level.IsRaidMap || !LevelManager.AfterInit
                    || SceneLoader.IsSceneLoading) return false;
                MultiSceneCore core = MultiSceneCore.Instance;
                if (core != null && core.IsLoading) return false;
                if (owner.IsActive || owner.IsModeDActive || owner.IsModeEActive || owner.IsModeFActive
                    || owner.IsZombieModeActive || owner.IsBossRushArenaActive
                    || owner.IsCampaignFinalBossActive || owner.CampaignArenaActiveForRuntime
                    || WavesArenaRuntimeModule.ArenaPlanned || WavesArenaRuntimeModule.ArenaActive
                    || ModBehaviour.IsModeGRunInProgressSafe() || ModBehaviour.IsModeHRunInProgressSafe()
                    || BossRushMapSelectionHelper.GetPendingEntryKind() != BossRushPendingEntryKind.None)
                    return false;
                // 进入中的模式也拥有地图；不能等到 IsActive 才关闭野外活动。
                if (owner.CampaignZombieRunForRuntime != null
                    && owner.CampaignZombieRunForRuntime.LifecyclePhase != ZombieModeLifecyclePhase.None)
                    return false;
                if (owner.ModeHRuntime != null && owner.ModeHRuntime.RunState != null
                    && owner.ModeHRuntime.RunState.Lifecycle != ModeHLifecycle.None) return false;
                Scene ownerScene = core != null ? core.gameObject.scene : level.gameObject.scene;
                if (!IsRaidScene(ownerScene)) return false;
                Scene? subScene = MultiSceneCore.ActiveSubScene;
                scene = subScene.HasValue && subScene.Value.IsValid() && subScene.Value.isLoaded
                    ? subScene.Value : ownerScene;
                if (!IsRaidScene(scene) || !SceneRuntimeGate.CanRunGameplayRuntimeNow(scene.name)) return false;
                player = CharacterMainControl.Main;
                return player != null && player.Health != null && !player.Health.IsDead;
            }
            catch (Exception)
            {
                player = null;
                scene = default(Scene);
                return false;
            }
        }

        private static bool IsRaidScene(Scene scene)
        {
            return scene.IsValid() && scene.isLoaded && !SceneRuntimeGate.IsBaseHubSceneName(scene.name)
                && !SceneRuntimeGate.IsModResourceScene(scene) && SceneRuntimeGate.IsGameplaySceneName(scene.name);
        }

        /// <summary>有限候选，必须有真实导航连通性、实体地面、身体净空，且玩家沿真实路线走得到；找不到就延后尝试。</summary>
        internal static bool TryFindGround(Vector3 origin, float minDistance, float maxDistance, out Vector3 position)
        {
            position = Vector3.zero;
            if (!IsFinite(origin.x) || !IsFinite(origin.y) || !IsFinite(origin.z)
                || !IsFinite(minDistance) || !IsFinite(maxDistance)
                || minDistance < 0f || maxDistance < minDistance) return false;
            try
            {
                if (!SpawnPositionHelper.IsReachableAnchor(origin)) return false;
                float phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                for (int i = 0; i < 12; i++)
                {
                    float angle = phase + i * 2.39996323f;
                    float radius = Mathf.Lerp(minDistance, maxDistance, (i % 3 + 1) / 4f);
                    Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    Vector3 resolved;
                    if (!SpawnPositionHelper.TryResolveReachableFrom(candidate, origin, 1.5f, 0f, out resolved))
                        continue;
                    Vector3 delta = resolved - origin;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < minDistance * minDistance || delta.sqrMagnitude > maxDistance * maxDistance)
                        continue;
                    RaycastHit ground;
                    if (!Physics.Raycast(resolved + Vector3.up * 1.5f, Vector3.down, out ground, 3f,
                            GameplayDataSettings.Layers.groundLayerMask, QueryTriggerInteraction.Ignore)
                        || Vector3.Dot(ground.normal, Vector3.up) < 0.85f
                        || Mathf.Abs(ground.point.y - resolved.y) > 0.65f) continue;
                    Vector3 bottom = ground.point + Vector3.up * 0.9f;
                    if (Physics.CheckCapsule(bottom, ground.point + Vector3.up * 2f, 0.8f,
                            GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 placed = ground.point + Vector3.up * 0.04f;
                    // 最贵的一步放最后：真算路并沿路扫墙，导航图铺出空气墙外的点在这里作废。
                    if (!FieldAdventureRoute.IsWalkable(origin, placed)) continue;
                    position = placed;
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FieldAdventures] 落点查询失败: " + e.Message);
            }
            return false;
        }

        /// <summary>
        /// 由远到近逐档找落点：bands 按 {min, max} 成对给出，前一档整轮落空才试下一档。
        /// 拥挤地图里远档的 12 个候选常常全落在墙里或楼外；放宽只缩短距离，连通、地面与净空判据不变。
        /// </summary>
        internal static bool TryFindGroundBands(Vector3 origin, float[] bands, out Vector3 position)
        {
            position = Vector3.zero;
            if (bands == null) return false;
            for (int i = 0; i + 1 < bands.Length; i += 2)
            {
                if (TryFindGround(origin, bands[i], bands[i + 1], out position)) return true;
            }
            return false;
        }

        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        internal static void Notify(string cn, string en)
        {
            try
            {
                Duckov.UI.NotificationText.Push(L10n.T(cn, en));
            }
            catch (Exception e) { ModBehaviour.DevLog("[FieldAdventures] 通知投递失败: " + e.Message); }
        }
    }
}
