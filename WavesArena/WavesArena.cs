// ============================================================================
// WavesArena.cs - 波次与竞技场的宿主兼容入口
// 运行时状态和算法由 WavesArenaRuntimeModule 持有；跨模式入场协调见
// BossRushEntryFlow.cs 与 WavesArenaEntryAndTeleport.cs。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;
using Duckov;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static HashSet<string> EarlyWaveExcludedBosses { get { return WavesArenaRuntimeModule.EarlyWaveExcludedBosses; } }

        private void EnsureEarlyWavesNoStrongBoss()
        {
            wavesArenaRuntime.EnsureEarlyWavesNoStrongBoss();
        }


        public void StartNextWaveCountdown(bool showInitialBanner = true, bool suppressImmediateRepeatBanner = false)
        {
            wavesArenaRuntime.StartNextWaveCountdown(showInitialBanner, suppressImmediateRepeatBanner);
        }

        internal void ShowNextWaveCountdownBanner(int secondsInt)
        {
            wavesArenaRuntime.ShowNextWaveCountdownBanner(secondsInt);
        }

        /// <summary>
        /// 敌人死亡事件处理（带DamageInfo参数）
        /// <para>仅用于普通模式（弹指可灭/有点意思/无间炼狱），Mode D 有独立的死亡处理逻辑</para>
        /// </summary>
        private void OnEnemyDiedWithDamageInfo(Health deadHealth, DamageInfo damageInfo)
        {
            wavesArenaRuntime.OnEnemyDiedWithDamageInfo(deadHealth, damageInfo);
        }

        private void HandleBossDeath(CharacterMainControl bossMain, DamageInfo damageInfo)
        {
            wavesArenaRuntime.HandleBossDeath(bossMain, damageInfo);
        }

        internal void ProceedAfterWaveFinished() { wavesArenaRuntime.ProceedAfterWaveFinished(); }
        private void OnBossSpawnFailed(EnemyPresetInfo preset) { wavesArenaRuntime.OnBossSpawnFailed(preset); }
        internal void UnregisterEnemyRecoveryForArena(CharacterMainControl boss) { UnregisterEnemyRecovery(boss); }
        internal bool CheckBossKillAchievementsOnceForArena(CharacterMainControl boss) { return CheckBossKillAchievementsOnce(boss); }
        internal bool ArenaUsesInteractBetweenWaves { get { return config != null && config.useInteractBetweenWaves; } }

        private void InitializeEnemyPresets() { wavesArenaRuntime.InitializeEnemyPresets(); }
        internal bool EnsureEnemyPresetsReadyForGameplayCatalogs() { return wavesArenaRuntime.EnsureEnemyPresetsReadyForGameplayCatalogs(); }
        internal int EnemyPresetInitializationScanCount { get { return wavesArenaRuntime.EnemyPresetInitializationScanCount; } }
        private EnemyPresetInfo PickRandomEnemyForInfiniteHell() { return wavesArenaRuntime.PickRandomEnemyForInfiniteHell(); }
        private static bool IsRuntimeCharacterPresetClone(CharacterRandomPreset preset) { return WavesArenaRuntimeModule.IsRuntimeCharacterPresetClone(preset); }
        private string GetLocalizedCharacterName(string nameKey) { return wavesArenaRuntime.GetLocalizedCharacterName(nameKey); }

        internal void ResetBossPoolFilterStateForArena() { ResetBossPoolFilterStateForEnemyPresetRefresh(); }
        internal bool IsBossPoolFilterInitializedForArena { get { return bossPoolFilterInitialized; } }
        internal void InitializeBossPoolFilterForArena() { InitializeBossPoolFilter(); }
        internal void RegisterDragonDescendantPresetForArena() { RegisterDragonDescendantPreset(); }
        internal void RegisterDragonKingPresetForArena() { RegisterDragonKingPreset(); }
        internal void RegisterPhantomWitchPresetForArena() { RegisterPhantomWitchPreset(); }
        internal bool IsManagedBossPresetForArena(EnemyPresetInfo preset) { return IsManagedBossPreset(preset); }
        internal void NotifyArenaPresetCatalogsRefreshed()
        {
            if (PetNestRuntime != null) PetNestRuntime.NotifyEnemyPresetsRefreshed();
            if (CodexRuntime != null) CodexRuntime.NotifyEnemyPresetsRefreshed();
        }


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


        internal bool IsInfiniteHellMode_WavesArena { get { return infiniteHellMode; } }

        private void UpdateCashMagnet()
        {
            if (wavesArenaRuntime != null) wavesArenaRuntime.UpdateCashMagnet();
        }

        private void ClearCashMagnetState()
        {
            if (wavesArenaRuntime != null) wavesArenaRuntime.ClearCashMagnetState();
        }

        /// <summary>
        /// 强制杀死所有敌人（用于F10调试，忽略范围限制）
        /// 直接调用Health.Kill()而不是Destroy，确保触发死亡事件
        /// </summary>
        private void ForceKillAllEnemies() { wavesArenaRuntime.ForceKillAllEnemies(); }
        private void ClearEnemiesForBossRush() { wavesArenaRuntime.ClearEnemiesForBossRush(); }
        private IEnumerator ContinuousClearEnemiesUntilWaveStart()
        {
            return wavesArenaRuntime.ContinuousClearEnemiesUntilWaveStart();
        }

        internal void RefreshCharacterCacheForArena() { RefreshCharacterCache(); }
        internal List<CharacterMainControl> ArenaCharacterCache { get { return WavesArenaRuntimeModule.CharacterCache; } }
        internal bool ArenaCharacterCacheNeedsRefresh
        {
            get { return WavesArenaRuntimeModule.CharacterCacheNeedsRefresh; }
            set { WavesArenaRuntimeModule.CharacterCacheNeedsRefresh = value; }
        }
        internal List<GameObject> ArenaReusableDestroyList { get { return WavesArenaRuntimeModule.ReusableDestroyList; } }
        internal bool ArenaCenterSetForCleanup { get { return WavesArenaRuntimeModule.ArenaCenterSet; } }
        internal Vector3 ArenaCenterForCleanup { get { return WavesArenaRuntimeModule.ArenaCenter; } }
        internal CharacterRandomPreset ArenaEggSpawnPreset { get { return BossRushAudioRuntimeService.EggSpawnPreset; } }
        internal bool IsModeETrackedEnemyForArena(CharacterMainControl enemy) { return modeEFEnemyRegistry.IsTracked(enemy); }
        internal bool IsDeathWraithCharacterForArena(CharacterMainControl enemy)
        {
            return IsDeathWraithCharacter_DeathWraith(enemy);
        }
        internal WaitForSeconds ArenaSharedWait05s { get { return sharedWait05s; } }

        private System.Collections.Generic.List<EnemyPresetInfo> enemyPresets
        {
            get { return wavesArenaRuntime.EnemyPresets; }
            set { wavesArenaRuntime.EnemyPresets = value; }
        }

        private int _enemyPresetInitializationScanCount
        {
            get { return wavesArenaRuntime.EnemyPresetInitializationScanCount; }
            set { wavesArenaRuntime.EnemyPresetInitializationScanCount = value; }
        }

        private float minBossBaseHealth
        {
            get { return wavesArenaRuntime.MinBossBaseHealth; }
            set { wavesArenaRuntime.MinBossBaseHealth = value; }
        }

        private float maxBossBaseHealth
        {
            get { return wavesArenaRuntime.MaxBossBaseHealth; }
            set { wavesArenaRuntime.MaxBossBaseHealth = value; }
        }

        private static bool _enemyPresetsInitialized
        {
            get { return WavesArenaRuntimeModule.EnemyPresetsInitialized; }
            set { WavesArenaRuntimeModule.EnemyPresetsInitialized = value; }
        }

        private bool waitingForNextWave
        {
            get { return wavesArenaRuntime.WaitingForNextWave; }
            set { wavesArenaRuntime.WaitingForNextWave = value; }
        }

        private float waveCountdown
        {
            get { return wavesArenaRuntime.WaveCountdown; }
            set { wavesArenaRuntime.WaveCountdown = value; }
        }

        private int lastWaveCountdownSeconds
        {
            get { return wavesArenaRuntime.LastWaveCountdownSeconds; }
            set { wavesArenaRuntime.LastWaveCountdownSeconds = value; }
        }


        private float daXingXingCleanTimer
        {
            get { return wavesArenaRuntime.DaXingXingCleanTimer; }
            set { wavesArenaRuntime.DaXingXingCleanTimer = value; }
        }

        private int totalEnemies
        {
            get { return wavesArenaRuntime.TotalEnemies; }
            set { wavesArenaRuntime.TotalEnemies = value; }
        }

        private int defeatedEnemies
        {
            get { return wavesArenaRuntime.DefeatedEnemies; }
            set { wavesArenaRuntime.DefeatedEnemies = value; }
        }

        private string nextWaveBossName
        {
            get { return wavesArenaRuntime.NextWaveBossName; }
            set { wavesArenaRuntime.NextWaveBossName = value; }
        }

        private int bossesPerWave
        {
            get { return wavesArenaRuntime.BossesPerWave; }
            set { wavesArenaRuntime.BossesPerWave = value; }
        }

        private int bossesInCurrentWaveTotal
        {
            get { return wavesArenaRuntime.BossesInCurrentWaveTotal; }
            set { wavesArenaRuntime.BossesInCurrentWaveTotal = value; }
        }

        private int bossesInCurrentWaveRemaining
        {
            get { return wavesArenaRuntime.BossesInCurrentWaveRemaining; }
            set { wavesArenaRuntime.BossesInCurrentWaveRemaining = value; }
        }

        private System.Collections.Generic.List<MonoBehaviour> currentWaveBosses
        {
            get { return wavesArenaRuntime.CurrentWaveBosses; }
        }

        private void DisableAllSpawners()
        {
            wavesArenaRuntime.DisableAllSpawners();
        }

        internal void TryFixStuckWaveIfNoBossAlive()
        {
            wavesArenaRuntime.TryFixStuckWaveIfNoBossAlive();
        }

        internal bool TickWavesArenaRuntime(float deltaTime)
        {
            return wavesArenaRuntime.TickWavesArenaRuntime(deltaTime);
        }

        internal void TickWavesArenaBossCleanupRuntime(float deltaTime)
        {
            wavesArenaRuntime.TickWavesArenaBossCleanupRuntime(deltaTime);
        }

        /// <summary>
        /// Mode G 运行状态的全 partial 共享 no-throw 读取（异常视为未运行，保持 Legacy 行为）。
        /// 只反映 lifecycle（LifecyclePhase != None），绝不包含 late sink quarantine。
        /// </summary>
        internal static bool IsModeGRunInProgressSafe()
        {
            try
            {
                return ModeGRuntimeGates.IsModeGRunInProgress;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Mode H 是否正在进行（no-throw，未运行时恒 false）。
        /// 供 Legacy 清怪循环等旧路径做加法分支使用（设计提案 §19.2）。
        /// </summary>
        internal static bool IsModeHRunInProgressSafe()
        {
            try
            {
                return ModeHRuntimeGates.IsModeHRunOwnerActive;
            }
            catch
            {
                return false;
            }
        }
    }
}
