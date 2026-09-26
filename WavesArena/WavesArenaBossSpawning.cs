// ============================================================================
// WavesArenaBossSpawning.cs - BossRush wave spawning and spawn verification
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 开始第一波Boss（在竞技场内）- 单波生成模式
        /// </summary>
        public void StartFirstWave() { wavesArenaRuntime.StartFirstWave(); }
        internal void ClearEnemiesForBossRushForArena() { ClearEnemiesForBossRush(); }
        internal void BeginAchievementSessionForArena(string mode) { BeginAchievementSession(mode); }
        internal void SetBossRushRuntimeActiveForArena(bool active) { SetBossRushRuntimeActive(active); }
        internal void TryRollMutatorsForArena(string mode) { TryRollMutatorsForMode(mode); }
        internal void SubscribeArenaBossDeaths()
        {
            Health.OnDead -= OnEnemyDiedWithDamageInfo;
            Health.OnDead += OnEnemyDiedWithDamageInfo;
        }

        /// <summary>通关奖励箱虚影控制器是否已起来，供 F3 验收断言胜利奖励链真的触发了。</summary>
        internal bool VictoryRewardCrateActiveForValidation
        {
            get
            {
                return _activeVictoryRewardShadowCrateController != null
                    && _activeVictoryRewardShadowCrateController.gameObject != null;
            }
        }

        /// <summary>
        /// Dev 验收专用：把 Boss 池临时收窄到 1 个，开一波标准 BossRush。
        /// 之后调用方清掉这一个 Boss，HandleBossDeath → ProceedAfterWaveFinished →
        /// OnAllEnemiesDefeated 就会按产品逻辑自然走完，胜利奖励链随之触发。
        ///
        /// 这样做的意义：胜利结算原本要打完全部 Boss 才能到达，45 分钟预算里跑不完。
        /// 收窄池子改的是「这一局有几个 Boss」这个玩家本就能在 Boss 池面板里改的设置，
        /// 不是绕过结算——结算判定用的还是 currentEnemyIndex >= presetCount 那条真实条件。
        ///
        /// 原 Boss 池开关与 bossesPerWave 由 <see cref="RestoreBossPoolAfterValidation"/> 还原，
        /// 调用方必须在 finally 里调它。
        /// </summary>
        internal bool DebugStartSingleBossVictoryForValidation(
            out Dictionary<string, bool> restoreStates,
            out int restoreBossesPerWave,
            out string reason)
        {
            restoreStates = null;
            restoreBossesPerWave = bossesPerWave;
            reason = null;
            if (!DevModeEnabled) { reason = "dev_mode_disabled"; return false; }
            if (IsActive) { reason = "arena_already_active"; return false; }

            try
            {
                if (!bossPoolFilterInitialized && enemyPresets != null && enemyPresets.Count > 0)
                {
                    InitializeBossPoolFilter();
                }

                List<EnemyPresetInfo> pool = GetFilteredEnemyPresets();
                if (pool == null || pool.Count == 0)
                {
                    reason = "filtered_boss_pool_empty";
                    return false;
                }

                restoreStates = new Dictionary<string, bool>(bossEnabledStates);
                string keep = pool[0].name;

                List<string> names = new List<string>(bossEnabledStates.Keys);
                for (int i = 0; i < names.Count; i++)
                {
                    if (names[i] != keep) SetBossEnabled(names[i], false);
                }
                SetBossEnabled(keep, true);

                List<EnemyPresetInfo> narrowed = GetFilteredEnemyPresets();
                if (narrowed == null || narrowed.Count != 1)
                {
                    reason = "narrow_failed_count=" + (narrowed == null ? "null" : narrowed.Count.ToString());
                    return false;
                }

                ConfigureBossRushMode(1, false);
                StartFirstWave();
                if (!IsActive)
                {
                    reason = "start_first_wave_did_not_activate";
                    return false;
                }
                DevLog("[BossRush] [Validation] 单 Boss 胜利链已开波，保留 Boss=" + keep);
                return true;
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + ":" + e.Message;
                return false;
            }
        }

        /// <summary>还原 <see cref="DebugStartSingleBossVictoryForValidation"/> 改动的 Boss 池与每波数量。</summary>
        internal void RestoreBossPoolAfterValidation(
            Dictionary<string, bool> restoreStates,
            int restoreBossesPerWave)
        {
            try
            {
                if (restoreStates != null)
                {
                    foreach (KeyValuePair<string, bool> kv in restoreStates)
                    {
                        SetBossEnabled(kv.Key, kv.Value);
                    }
                }
                ConfigureBossRushMode(restoreBossesPerWave, false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 还原验收 Boss 池失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取安全的Boss生成位置（只修正Y轴高度，不改变XZ坐标）
        /// </summary>
        /// <remarks>
        /// 委托 SpawnPositionHelper.SnapToGround：Raycast(groundLayerMask) 优先 → NavMesh 兜底 → +0.5m 兜底。
        /// 避免 NavMesh 采样把敌人吸到非预设点（屋顶、楼梯下、墙体内的 NavMesh）。
        /// </remarks>
        private static Vector3 GetSafeBossSpawnPosition(Vector3 rawPosition)
        {
            return SpawnPositionHelper.SnapToGround(rawPosition);
        }

        /// <summary>
        /// 玩家安全距离（米）：刷怪点距玩家小于此距离时不会被选中
        /// </summary>
        private const float SPAWN_SAFE_DISTANCE = 15f;
        private const float SPAWN_SAFE_DISTANCE_SQR = SPAWN_SAFE_DISTANCE * SPAWN_SAFE_DISTANCE;

        /// <summary>
        /// 从刷怪点数组中选取距玩家最近但不在安全距离内的点
        /// <para>如果所有点都在安全距离内，回退到距玩家最远的点</para>
        /// </summary>
        /// <param name="spawnPoints">候选刷怪点数组</param>
        /// <param name="playerPos">玩家当前位置</param>
        /// <returns>经过 GetSafeBossSpawnPosition Y轴修正后的安全刷怪位置</returns>
        private static Vector3 FindNearestSafeSpawnPoint(Vector3[] spawnPoints, Vector3 playerPos)
        {
            return SpawnPositionHelper.FindNearestSafeSpawnPoint(spawnPoints, playerPos, SPAWN_SAFE_DISTANCE);
        }

        /// <summary>
        /// 从刷怪点列表中选取距玩家最近但不在安全距离内的点（List版本）
        /// </summary>
        private static Vector3 FindNearestSafeSpawnPoint(List<Vector3> spawnPoints, Vector3 playerPos)
        {
            return SpawnPositionHelper.FindNearestSafeSpawnPoint(spawnPoints, playerPos, SPAWN_SAFE_DISTANCE);
        }

        private static List<Vector3> FindMultipleSafeSpawnPoints(int count, Vector3[] spawnPoints, Vector3 playerPos)
        {
            return SpawnPositionHelper.FindMultipleSafeSpawnPoints(count, spawnPoints, playerPos, SPAWN_SAFE_DISTANCE);
        }

        private static List<Vector3> FindMultipleSafeSpawnPoints(int count, List<Vector3> spawnPoints, Vector3 playerPos)
        {
            return SpawnPositionHelper.FindMultipleSafeSpawnPoints(count, spawnPoints, playerPos, SPAWN_SAFE_DISTANCE);
        }

        /// <summary>
        /// 校验并修正Boss位置（生成后调用，防止Boss卡在地下）
        /// </summary>
        private void ValidateAndFixBossPosition(CharacterMainControl boss)
        { enemyRecoveryMonitor.ValidateAndFixBossPosition(boss); }

        /// <summary>
        /// 延迟校验Boss位置的协程（给地形加载留出时间）
        /// </summary>
        private IEnumerator DelayedBossPositionValidation(CharacterMainControl boss, float delay)
        { return enemyRecoveryMonitor.DelayedBossPositionValidation(boss, delay); }

        internal void SpawnNextEnemy() { wavesArenaRuntime.SpawnNextEnemy(); }
        internal void OnAllEnemiesDefeatedForArena() { OnAllEnemiesDefeated(); }
        internal void OnBossSpawnFailedForArena(EnemyPresetInfo preset) { OnBossSpawnFailed(preset); }
        internal void ShowEnemyBannerForArena(string name, Vector3 enemyPos, Vector3 playerPos)
        {
            ShowEnemyBanner(name, enemyPos, playerPos);
        }
        internal UniTask<CharacterMainControl> SpawnEnemyAtPositionForArenaAsync(
            EnemyPresetInfo preset, Vector3 position, Func<bool> isSpawnCurrent)
        {
            return SpawnEnemyAtPositionAsync(preset, position, isSpawnCurrent);
        }

        private void BindArenaSpawnServices()
        {
            wavesArenaRuntime.BindLegacySpawnServices(IsDragonDescendantPreset, IsDragonKingPreset, IsPhantomWitchPreset,
                (position, child, notify, active) => SpawnDragonDescendant(position, isChildProtectionSummon: child,
                    notifyBossRushOnFailure: notify, isActiveCheck: active),
                (position, notify, active) => SpawnDragonKing(position, notifyBossRushOnFailure: notify, isActiveCheck: active),
                (position, notify, active) => SpawnPhantomWitch(position, notifyBossRushOnFailure: notify, isActiveCheck: active),
                character => ApplyBossStatMultiplier(character));
            wavesArenaRuntime.BindLootBoxPolicies(() => config != null, () => config.lootBoxBlocksBullets);
        }

    }
}
