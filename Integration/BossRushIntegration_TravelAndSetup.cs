using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 将玩家传送到自定义位置（用于 BossRush 地图选择中的非默认地图）
        /// [修复] 使用 RaycastAll 找到最接近配置 Y 坐标的地面点，避免在室内场景传送到房顶
        /// </summary>
        private System.Collections.IEnumerator TeleportPlayerToCustomPosition(Vector3 targetPosition)
        {
            if (ShouldSkipLegacySceneSetupForModeH()) yield break;
            DevLog("[BossRush] TeleportPlayerToCustomPosition: 开始等待场景初始化，目标位置: " + targetPosition);

            yield return bossRushIntegrationRuntime.WaitForCustomTeleportSceneReady();

            // 等待期间可能进入 H，或认证已消费 typed intent；两种情况都由 H 持有位置。
            if (ShouldSkipLegacySceneSetupForModeH()) yield break;

            BossRushEntryMode entryMode = DetermineBossRushEntryMode("TeleportPlayerToCustomPosition");
            bool isModeEEntry = entryMode == BossRushEntryMode.ModeE;
            if (isModeEEntry)
            {
                DevLog("[BossRush] TeleportPlayerToCustomPosition: 检测到 Mode E 入场条件，跳过传送");
            }

            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    Vector3 finalPosition = bossRushIntegrationRuntime.ApplyCustomTeleportPosition(
                        targetPosition,
                        main,
                        isModeEEntry);

                    bool zombieModeOwnsCurrentEntry = IsZombieModeStartupInProgress() ||
                        ZombieModeMapSelectionHelper.HasPendingZombieEntry ||
                        IsZombieModeActive;
                    if (zombieModeOwnsCurrentEntry)
                    {
                        DevLog("[ZombieMode] TeleportPlayerToCustomPosition: 丧尸模式正在接管当前入图，跳过 BossRush GroundZero 初始化");
                        yield break;
                    }

                    string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    BossRushMapConfig mapConfig = GetMapConfigBySceneName(currentScene);
                    if (mapConfig != null && mapConfig.customSpawnPos.HasValue)
                    {
                        StartCoroutine(SetupBossRushInGroundZero(finalPosition, entryMode));
                    }
                }
                else
                {
                    DevLog("[BossRush] TeleportPlayerToCustomPosition: CharacterMainControl.Main 为 null");
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] TeleportPlayerToCustomPosition 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 强制传送到指定的子场景（用于风暴区地下等需要特定子场景的情况）
        /// 当游戏随机选择了错误的子场景时，查找并触发场景中的传送器
        /// </summary>
        private System.Collections.IEnumerator ForceTeleportToSubScene(string targetSubSceneID, Vector3 targetPosition)
        {
            return bossRushIntegrationRuntime.ForceTeleportToSubScene(targetSubSceneID, targetPosition);
        }

        private System.Collections.IEnumerator SetupBossRushInGroundZero(Vector3 playerPosition, BossRushEntryMode? resolvedEntryMode = null)
        {
            if (IsZombieModeStartupInProgress() ||
                ZombieModeMapSelectionHelper.HasPendingZombieEntry ||
                IsZombieModeActive)
            {
                DevLog("[ZombieMode] SetupBossRushInGroundZero: 丧尸模式正在接管当前入图，跳过普通 BossRush 初始化");
                yield break;
            }

            DevLog("[BossRush] SetupBossRushInGroundZero: 开始初始化零号区 BossRush 模式");

            // 0. 重置 spawner 禁用标志（确保能重新禁用新场景的 spawner）
            spawnersDisabled = false;

            // [性能优化] 先根据地图配置设置竞技场中心，确保后续清理和禁用操作有范围限制
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            SetArenaCenterFromMapConfig(currentSceneName);

            // 提前检测 Mode E / Mode F / Mode D 条件（必须在 DisableAllSpawners 之前，Mode E 需要先扫描 CharacterSpawnerRoot 位置）
            BossRushEntryMode entryMode = resolvedEntryMode ?? DetermineBossRushEntryMode("SetupBossRushInGroundZero");

            // Mode E 提前分支：先扫描刷怪点（AllocateSpawnPoints 内部调用），再禁用spawner和清理敌人
            // 跳过路牌、撤离点、快递员等 BossRush 竞技场逻辑
            if (entryMode == BossRushEntryMode.ModeH)
            {
                // Mode H 显式 intent：本协程不做任何 Legacy 竞技场初始化，
                // 只把控制权交给唯一 ModeHRuntimeModule 实例；隔离租约、观战租约、
                // 生产认证与 Season 创建全部由该实例在 host.OnSceneLoaded 内完成。
                DevLog("[BossRush] SetupBossRushInGroundZero: 检测到显式 Mode H 入场意图，跳过 Legacy 接管");
                yield break;
            }

            if (entryMode == BossRushEntryMode.ModeE)
            {
                DevLog("[BossRush] SetupBossRushInGroundZero: 检测到营旗+裸装入场，将启动 Mode E");
                // 初始化敌人预设和Boss池（Mode E 生成Boss需要）
                InitializeEnemyPresets();
                InitializeItemValueCacheAsync();
                InitializeBossPoolFilter();
                bossRushArenaActive = true;

                // 预缓存原地图刷怪点位置（必须在 DisableAllSpawners 之前）
                PreCacheMapSpawnerPositions();
                ScheduleModeEStartupWarmup("GroundZeroModeE");

                // 禁用 spawner 和清理敌人（Mode E 仍需要）
                DisableAllSpawners();
                DevLog("[BossRush] SetupBossRushInGroundZero Mode E: 已禁用竞技场范围内的敌怪生成器");
                ClearEnemiesForBossRush();

                yield return new UnityEngine.WaitForSeconds(0.5f);
                bool startedModeE = TryStartModeE();
                if (startedModeE)
                {
                    bool verifiedModeE = false;
                    yield return StartCoroutine(WaitForModeEStartupVerification(result => verifiedModeE = result));
                    if (verifiedModeE)
                    {
                        SpawnCommonNPCs("GroundZero场景 Mode E 初始化完成");
                        ScheduleRestoreFollowingSpouse(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, "GroundZero场景 Mode E 初始化完成");
                        BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                        yield break;
                    }
                }

                StopModeEStartupWarmupIfPending();
                DevLog("[BossRush] [WARNING] SetupBossRushInGroundZero: Mode E 启动未通过验证，回退到普通 BossRush 初始化流程");
            }

            // Mode F 提前分支：复用 DEMO 的初始化顺序，跳过普通 BossRush 竞技场入口流程
            if (entryMode == BossRushEntryMode.ModeF)
            {
                DevLog("[BossRush] SetupBossRushInGroundZero: 检测到船票+血猎收发器+裸装入场，将启动 Mode F");
                PreCacheMapSpawnerPositions();
                ScheduleModeEStartupWarmup("GroundZeroModeF");

                DisableAllSpawners();
                DevLog("[BossRush] SetupBossRushInGroundZero Mode F: 已禁用竞技场范围内的敌怪生成器");
                ClearEnemiesForBossRush();

                yield return new UnityEngine.WaitForSeconds(0.5f);
                bool startedModeF = TryStartModeF();
                if (startedModeF)
                {
                    SpawnCommonNPCs("GroundZero场景 Mode F 初始化完成");
                    ScheduleRestoreFollowingSpouse(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, "GroundZero场景 Mode F 初始化完成");
                }
                else
                {
                    DevLog("[BossRush] [WARNING] SetupBossRushInGroundZero: Mode F 启动失败，已跳过 Mode F 额外 NPC 生成");
                }
                BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                yield break;
            }

            // Mode G 独立分支：成功或失败都不得落入普通 BossRush 初始化。
            if (entryMode == BossRushEntryMode.ModeG)
            {
                DevLog("[BossRush] SetupBossRushInGroundZero: 检测到船票+宿命回响信物，将保留玩家当前装备并尝试启动 Mode G");
                yield return new UnityEngine.WaitForSeconds(0.5f);
                bool openedModeG = ModeGInteractable.TryOpenConfirmation(this);
                while (openedModeG && ModeGInteractable.IsConfirmationOpen)
                {
                    yield return null;
                }
                bool startedModeG = modeGActive;
                if (startedModeG)
                {
                    SpawnCommonNPCs("GroundZero场景 Mode G 初始化完成");
                    ScheduleRestoreFollowingSpouse(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                        "GroundZero场景 Mode G 初始化完成");
                }
                else
                {
                    if (ModeGInteractable.LastConfirmationAttemptedStart)
                    {
                        DevLog("[BossRush] [WARNING] SetupBossRushInGroundZero: Mode G 启动失败，已中止本次竞技场初始化");
                        ShowMessage(L10n.T(
                            "宿命回响启动失败，入场物品已恢复。",
                            "Fate Echo failed to start. Entry items were restored."));
                    }
                    else if (!openedModeG)
                    {
                        // 确认页从未打开（可用性闸/地图未登记/展示资源预检/preview 无效）。
                        // 玩家带着船票和信物专程进图，只退款不解释等于静默失败；
                        // 与路牌 OnTimeOut 路径同款文案，避免玩家以为物品被吞。
                        DevLog("[BossRush] [WARNING] SetupBossRushInGroundZero: Mode G 确认页未能打开，本次入场中止");
                        ShowMessage(L10n.T(
                            "宿命回响入口准备失败，请稍后重试。",
                            "Fate Echo entry is not ready. Please try again later."));
                    }
                    else
                    {
                        DevLog("[BossRush] SetupBossRushInGroundZero: Mode G 确认页已取消，竞技场环境保持不变");
                    }
                }
                if (!openedModeG) TryRefundModeGPendingPrepaidTicket();
                yield break;
            }

            // 1. [重要] 先生成地图阻挡物和撤离点（必须在销毁 spawner 之前执行）
            // 因为撤离点模板 ExitNoSmoke Variant 位于 EnemySpawner_TestBossZone 下
            SpawnBossRushMapObjects();

            // 2. 禁用场景中的 spawner，阻止敌怪生成（会销毁 EnemySpawner_TestBossZone）（现在有 50m 范围限制）
            DisableAllSpawners();
            DevLog("[BossRush] SetupBossRushInGroundZero: 已禁用竞技场范围内的敌怪生成器");

            // 3. 启动持续清理敌人协程（直到波次开始）
            StartCoroutine(ContinuousClearEnemiesUntilWaveStart());

            // 4. 等待场景稳定
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // 5. 清理场景中现有的敌人（现在有 50m 范围限制）
            ClearEnemiesForBossRush();
            DevLog("[BossRush] SetupBossRushInGroundZero: 已清理竞技场范围内的敌人");

            // 6. 创建 BossRush 交互点（优先使用配置的位置，否则使用玩家位置偏移）
            BossRushMapConfig currentMapConfig = GetMapConfigBySceneName(currentSceneName);
            Vector3 signPosition = BossRushMapRuntime.ResolveArenaSignPosition(currentMapConfig, playerPosition);
            TryCreateArenaDifficultyEntryPoint(signPosition);
            DevLog("[BossRush] SetupBossRushInGroundZero: 已创建 BossRush 交互点，位置=" + signPosition);

            // 7. 设置当前地图的刷新点（使用当前场景名）
            SetCurrentMapSpawnPoints(currentSceneName);

            // 8. 标记 BossRush 竞技场已激活
            bossRushArenaActive = true;
            InitializeEnemyPresets();
            InitializeItemValueCacheAsync(); // 异步初始化物品价值缓存
            InitializeBossPoolFilter();
            DevLog("[BossRush] SetupBossRushInGroundZero: 零号区 BossRush 模式初始化完成");

            // 9. 延迟启动 Mode D
            if (entryMode == BossRushEntryMode.ModeD)
            {
                DevLog("[BossRush] SetupBossRushInGroundZero: 检测到裸体入场（无营旗无收发器），将启动 Mode D");
                yield return new UnityEngine.WaitForSeconds(0.5f);
                TryStartModeD();
            }

            // 10. 统一生成公共NPC（快递员、哥布林、护士）
            SpawnCommonNPCs("GroundZero场景初始化完成");
            ScheduleRestoreFollowingSpouse(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, "GroundZero场景初始化完成");
            BossRushMapSelectionHelper.ClearPendingEntryFlowState();
        }

        /// <summary>
        /// 根据场景名称设置当前地图的刷新点（使用 BossRushMapConfig 配置系统）
        /// </summary>
        private void SetCurrentMapSpawnPoints(string sceneName)
        {
            mapRuntime.SetCurrentMapSpawnPoints(bossRushIntegrationRuntime.ResolveMapSpawnPointsForScene(sceneName));
        }

        internal System.Collections.IEnumerator TeleportPlayerToCustomPositionForIntegration(Vector3 targetPosition)
        {
            return TeleportPlayerToCustomPosition(targetPosition);
        }

        internal void SetBossRushArenaPlannedForIntegration(bool planned)
        {
            bossRushArenaPlanned = planned;
        }

        internal void ClearBossRushPendingMapEntryForIntegration()
        {
            BossRushMapSelectionHelper.ClearPendingMapEntry();
        }

        internal void ClearBossRushPendingEntryFlowStateForIntegration()
        {
            BossRushMapSelectionHelper.ClearPendingEntryFlowState();
        }

    }
}
