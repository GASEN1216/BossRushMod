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
            DevLog("[BossRush] TeleportPlayerToCustomPosition: 开始等待场景初始化，目标位置: " + targetPosition);

            // 等待场景完全加载
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                bool mainExists = ReadMainExistsWithWarning("TeleportPlayerToCustomPosition");
                bool levelInited = ReadLevelInitedWithWarning("TeleportPlayerToCustomPosition");

                if (mainExists && levelInited)
                {
                    break;
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            // 额外等待一小段时间，确保游戏自身的出生点逻辑已执行完毕
            yield return new WaitForSeconds(0.5f);

            // [Mode E 修复] 使用统一入场判定，只在 Mode E 时跳过 customSpawnPos 传送
            // Mode E 设计为"玩家留在地图默认出生点"，不需要传送到 customSpawnPos
            BossRushEntryMode entryMode = DetermineBossRushEntryMode("TeleportPlayerToCustomPosition");
            bool isModeEEntry = entryMode == BossRushEntryMode.ModeE;
            if (isModeEEntry)
            {
                DevLog("[BossRush] TeleportPlayerToCustomPosition: 检测到 Mode E 入场条件，跳过传送");
            }

            // 传送玩家到目标位置
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    // Mode E 跳过传送，直接进入 SetupBossRushInGroundZero
                    Vector3 finalPosition = targetPosition;

                    if (!isModeEEntry)
                    {
                    // [修复] 使用 RaycastAll 找到最接近配置 Y 坐标的地面点（1m，防止卡到屋顶）
                    Vector3 rayStart = targetPosition + Vector3.up * 1f;
                    RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 5f);

                    if (hits != null && hits.Length > 0)
                    {
                        float configY = targetPosition.y;
                        float bestY = targetPosition.y;
                        float lowestY = float.MaxValue;

                        foreach (var h in hits)
                        {
                            // 优先选择接近配置 Y 坐标的点（允许 1 米误差）
                            if (Mathf.Abs(h.point.y - configY) < 1f)
                            {
                                bestY = h.point.y + 0.1f;
                                break;
                            }
                            // 否则选择最低的点
                            if (h.point.y < lowestY)
                            {
                                lowestY = h.point.y;
                                bestY = h.point.y + 0.1f;
                            }
                        }

                        finalPosition = new Vector3(targetPosition.x, bestY, targetPosition.z);
                        DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用 RaycastAll 修正落点: " + finalPosition + " (配置Y=" + configY + ")");
                    }
                    else
                    {
                        // 如果没有碰撞，使用单次射线检测
                        RaycastHit hit;
                        if (Physics.Raycast(rayStart, Vector3.down, out hit, 5f))
                        {
                            finalPosition = hit.point + new Vector3(0f, 0.1f, 0f);
                            DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用单次 Raycast 修正落点: " + finalPosition);
                        }
                    }

                    // 保存相机偏移
                    GameCamera camera = GameCamera.Instance;
                    Vector3 cameraOffset = Vector3.zero;
                    if (camera != null)
                    {
                        cameraOffset = camera.transform.position - main.transform.position;
                    }

                    // 传送玩家
                    try
                    {
                        main.SetPosition(finalPosition);
                        DevLog("[BossRush] TeleportPlayerToCustomPosition: 使用 SetPosition 传送玩家到 " + finalPosition);
                    }
                    catch (System.Exception e)
                    {
                        DevLog("[BossRush] SetPosition 失败: " + e.Message + "，改用 transform.position");
                        main.transform.position = finalPosition;
                    }

                    // 恢复相机位置
                    if (camera != null)
                    {
                        camera.transform.position = main.transform.position + cameraOffset;
                    }

                    DevLog("[BossRush] TeleportPlayerToCustomPosition: 传送完成");
                    } // end if (!isModeEEntry)

                    bool zombieModeOwnsCurrentEntry = IsZombieModeStartupInProgress() ||
                        ZombieModeMapSelectionHelper.HasPendingZombieEntry ||
                        IsZombieModeActive;
                    if (zombieModeOwnsCurrentEntry)
                    {
                        DevLog("[ZombieMode] TeleportPlayerToCustomPosition: 丧尸模式正在接管当前入图，跳过 BossRush GroundZero 初始化");
                        yield break;
                    }

                    // 在有效的 BossRush 竞技场场景执行初始化
                    string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                    BossRushMapConfig mapConfig = GetMapConfigBySceneName(currentScene);
                    if (mapConfig != null && mapConfig.customSpawnPos.HasValue)
                    {
                        // 启动该地图的 BossRush 初始化协程
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
            DevLog("[BossRush] ForceTeleportToSubScene: 开始强制传送到子场景 " + targetSubSceneID);

            // 等待场景完全加载（使用较短的间隔提高响应速度）
            const float maxWait = 10f;
            const float interval = 0.1f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                bool mainExists = ReadMainExistsWithWarning("ForceTeleportToSubScene");
                bool levelInited = ReadLevelInitedWithWarning("ForceTeleportToSubScene");

                if (mainExists && levelInited) break;

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            // 额外等待确保场景稳定
            yield return new WaitForSeconds(1.0f);

            // 方案1：查找场景中通往目标子场景的传送器并触发
            try
            {
                MultiSceneTeleporter[] teleporters = UnityEngine.Object.FindObjectsOfType<MultiSceneTeleporter>(true);
                MultiSceneTeleporter targetTeleporter = null;

                // [性能优化] 只在找到传送器时输出日志
                foreach (MultiSceneTeleporter t in teleporters)
                {
                    if (t == null) continue;

                    try
                    {
                        MultiSceneLocation target = t.Target;
                        string targetSceneID = target.SceneID;

                        // 精确匹配
                        if (targetSceneID == targetSubSceneID)
                        {
                            targetTeleporter = t;
                            break;
                        }

                        // 风暴区特殊处理：模糊匹配
                        if (targetSubSceneID == "Level_StormZone_B0")
                        {
                            string interactName = t.InteractName ?? "";
                            if (interactName.Contains("下去") || interactName.Contains("地下") ||
                                t.name.Contains("Down") || t.name.Contains("B0") ||
                                (targetSceneID != null && targetSceneID.Contains("B0")))
                            {
                                targetTeleporter = t;
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        string teleporterName = string.Empty;
                        try
                        {
                            teleporterName = t.name;
                        }
                        catch
                        {
                            teleporterName = "<unknown>";
                        }

                        LogIntegrationWarningLimited(
                            "ForceTeleportToSubScene_teleporter_scan",
                            "ForceTeleportToSubScene 读取传送器信息失败: " + teleporterName,
                            e);
                    }
                }

                if (targetTeleporter != null)
                {
                    DevLog("[BossRush] ForceTeleportToSubScene: 触发传送器 " + targetTeleporter.name);
                    targetTeleporter.DoTeleport();
                    yield break;
                }
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(
                    "ForceTeleportToSubScene_search",
                    "ForceTeleportToSubScene 查找目标传送器失败，准备回退到备用方案",
                    e);
            }

            // 方案2：使用 MultiSceneCore.LoadAndTeleport（备用方案）
            try
            {
                Duckov.Scenes.MultiSceneCore multiSceneCore = Duckov.Scenes.MultiSceneCore.Instance;
                if (multiSceneCore != null)
                {
                    DevLog("[BossRush] ForceTeleportToSubScene: 使用 LoadAndTeleport 备用方案");
                    Cysharp.Threading.Tasks.UniTaskExtensions.Forget(multiSceneCore.LoadAndTeleport(targetSubSceneID, targetPosition, true));
                }
                else
                {
                    // 回退：直接传送玩家到目标位置
                    bossRushArenaPlanned = false;
                    StartCoroutine(TeleportPlayerToCustomPosition(targetPosition));
                    BossRushMapSelectionHelper.ClearPendingMapEntry();
                    BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                }
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] ForceTeleportToSubScene 失败: " + e.Message);
                bossRushArenaPlanned = false;
                BossRushMapSelectionHelper.ClearPendingMapEntry();
                BossRushMapSelectionHelper.ClearPendingEntryFlowState();
            }
        }

        /// <summary>
        /// 在零号区设置 BossRush 模式（类似 SetupBossRushInDemoChallenge）
        /// </summary>
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
            Vector3 signPosition;
            if (currentMapConfig != null && currentMapConfig.defaultSignPos.HasValue)
            {
                signPosition = currentMapConfig.defaultSignPos.Value;
                DevLog("[BossRush] SetupBossRushInGroundZero: 使用配置的交互点位置: " + signPosition);
            }
            else
            {
                signPosition = playerPosition + new Vector3(-2f, 0f, 1f);
                DevLog("[BossRush] SetupBossRushInGroundZero: 使用玩家位置偏移: " + signPosition);
            }
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
            // 使用配置系统获取刷新点（使用 mapConfig 避免与实例字段 config 混淆）
            BossRushMapConfig mapConfig = GetMapConfigBySceneName(sceneName);
            if (mapConfig != null && mapConfig.spawnPoints != null)
            {
                currentMapSpawnPoints = mapConfig.spawnPoints;
                DevLog("[BossRush] SetCurrentMapSpawnPoints: 使用 " + mapConfig.displayName + " 刷新点，共 " + mapConfig.spawnPoints.Length + " 个");
            }
            else
            {
                currentMapSpawnPoints = null;
                DevLog("[BossRush] [WARNING] SetCurrentMapSpawnPoints: 未找到场景 JSON 配置 " + sceneName);
            }
        }

        /// <summary>
        /// BossRush 地图物品复制配置
        /// </summary>
        private class MapObjectCloneConfig
        {
            public string templateName;      // 模板对象名称
            public string parentNamePrefix;  // 父对象名称前缀（用于查找）
            public Vector3 targetPosition;   // 目标位置
            public string cloneName;         // 克隆后的名称
            public float? rotationY;         // Y轴旋转角度（可选，null表示使用模板旋转）

            public MapObjectCloneConfig(string template, string parentPrefix, Vector3 pos, string name, float? rotation = null)
            {
                templateName = template;
                parentNamePrefix = parentPrefix;
                targetPosition = pos;
                cloneName = name;
                rotationY = rotation;
            }
        }

        /// <summary>
        /// 获取指定地图的物品复制配置列表
        /// </summary>
        private List<MapObjectCloneConfig> GetMapCloneConfigs(string sceneName)
        {
            List<MapObjectCloneConfig> configs = new List<MapObjectCloneConfig>();

            if (sceneName == "Level_GroundZero_1")
            {
                // 零号区地图的复制配置

                // 1. 路障 - 封堵出口
                configs.Add(new MapObjectCloneConfig(
                    "Prfb_Roadblock_1",
                    "Group_",
                    new Vector3(425.35f, 0.02f, 254.49f),
                    "BossRush_Roadblock"
                ));

                // 2. 火焰烟雾特效 - 复制到出口位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_GroundZero_1",
                    new Vector3(447.50f, 0.01f, 288.27f),
                    "BossRush_Exit_FireSmoke"
                ));

                // 3. 铁丝网 - 封堵地形缺口
                configs.Add(new MapObjectCloneConfig(
                    "Prfb_BarbedWire_01_03_20",
                    "Group_",
                    new Vector3(455.80f, 0.02f, 306.78f),
                    "BossRush_BarbedWire"
                ));
            }
            else if (sceneName == "Level_ChallengeSnow")
            {
                // 零度挑战地图的复制配置

                // 1. 集装箱 - 作为竞技场边界
                configs.Add(new MapObjectCloneConfig(
                    "Pfb_Container_01_B_Season_64",
                    "Env",
                    new Vector3(242.54f, -0.01f, 259.44f),
                    "BossRush_Container_Clone",
                    270f  // Y轴旋转270度
                ));

                // 2. 篝火 - 作为交互点载体
                configs.Add(new MapObjectCloneConfig(
                    "Pfb_Campingfire",
                    "Env",
                    new Vector3(225.32f, 0.01f, 285.64f),
                    "BossRush_Campfire_Interact",
                    0f
                ));
            }
            else if (sceneName == "Level_HiddenWarehouse")
            {
                // 仓库区地图的围栏配置（使用 Prfb_Roadblock_33）
                // 围栏数据从 Player.log 提取

                // 南侧围栏（旋转0°和180°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(125.90f, 0.00f, 162.66f), "BossRush_Barrier_1", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(123.65f, 0.00f, 162.67f), "BossRush_Barrier_2", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(121.36f, 0.00f, 162.67f), "BossRush_Barrier_3", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(119.09f, 0.00f, 162.65f), "BossRush_Barrier_4", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(116.81f, 0.00f, 162.64f), "BossRush_Barrier_5", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(114.54f, 0.00f, 162.64f), "BossRush_Barrier_6", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(112.27f, 0.00f, 162.63f), "BossRush_Barrier_7", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.02f, 0.00f, 162.64f), "BossRush_Barrier_8", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(107.76f, 0.00f, 162.65f), "BossRush_Barrier_9", 0f));

                // 西侧围栏（旋转270°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.72f, 0.00f, 163.89f), "BossRush_Barrier_10", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 166.16f), "BossRush_Barrier_11", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 168.44f), "BossRush_Barrier_12", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.75f, 0.00f, 170.70f), "BossRush_Barrier_13", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 172.90f), "BossRush_Barrier_14", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.74f, 0.00f, 175.11f), "BossRush_Barrier_15", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.76f, 0.00f, 177.37f), "BossRush_Barrier_16", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.76f, 0.00f, 179.62f), "BossRush_Barrier_17", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.77f, 0.00f, 181.81f), "BossRush_Barrier_18", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.79f, 0.00f, 184.01f), "BossRush_Barrier_19", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.80f, 0.00f, 186.30f), "BossRush_Barrier_20", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.80f, 0.00f, 188.55f), "BossRush_Barrier_21", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.79f, 0.00f, 190.81f), "BossRush_Barrier_22", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.82f, 0.00f, 193.07f), "BossRush_Barrier_23", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.83f, 0.00f, 195.29f), "BossRush_Barrier_24", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.84f, 0.00f, 197.51f), "BossRush_Barrier_25", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.87f, 0.00f, 199.79f), "BossRush_Barrier_26", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.88f, 0.00f, 202.05f), "BossRush_Barrier_27", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.88f, 0.00f, 204.25f), "BossRush_Barrier_28", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.89f, 0.00f, 206.50f), "BossRush_Barrier_29", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.90f, 0.00f, 208.77f), "BossRush_Barrier_30", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.91f, 0.00f, 210.97f), "BossRush_Barrier_31", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.92f, 0.00f, 213.22f), "BossRush_Barrier_32", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(106.92f, 0.00f, 214.60f), "BossRush_Barrier_33", 270f));

                // 北侧围栏（旋转180°和特殊角度）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(107.93f, 0.00f, 215.85f), "BossRush_Barrier_34", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.17f, 0.00f, 215.86f), "BossRush_Barrier_35", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(110.43f, 0.00f, 216.92f), "BossRush_Barrier_36", 300f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(111.13f, 0.00f, 218.23f), "BossRush_Barrier_37", 150f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(118.65f, 0.00f, 218.73f), "BossRush_Barrier_38", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(120.92f, 0.00f, 218.72f), "BossRush_Barrier_39", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(124.35f, 0.00f, 218.83f), "BossRush_Barrier_40", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(127.81f, 0.00f, 218.77f), "BossRush_Barrier_41", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(130.08f, 0.00f, 218.77f), "BossRush_Barrier_42", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(132.32f, 0.00f, 218.76f), "BossRush_Barrier_43", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(134.58f, 0.00f, 218.78f), "BossRush_Barrier_44", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(136.80f, 0.00f, 218.79f), "BossRush_Barrier_45", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(139.07f, 0.00f, 218.78f), "BossRush_Barrier_46", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(141.35f, 0.00f, 218.79f), "BossRush_Barrier_47", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(143.62f, 0.00f, 218.79f), "BossRush_Barrier_48", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(145.88f, 0.00f, 218.81f), "BossRush_Barrier_49", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(148.10f, 0.00f, 218.79f), "BossRush_Barrier_50", 0f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.38f, 0.00f, 218.69f), "BossRush_Barrier_51", 15f));

                // 东侧围栏（旋转270°）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.16f, 0.00f, 214.34f), "BossRush_Barrier_52", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 212.05f), "BossRush_Barrier_53", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 209.79f), "BossRush_Barrier_54", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 207.58f), "BossRush_Barrier_55", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 205.34f), "BossRush_Barrier_56", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.19f, 0.00f, 203.05f), "BossRush_Barrier_57", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.16f, 0.00f, 200.83f), "BossRush_Barrier_58", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.15f, 0.00f, 198.57f), "BossRush_Barrier_59", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.13f, 0.00f, 196.52f), "BossRush_Barrier_60", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 194.30f), "BossRush_Barrier_61", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 192.01f), "BossRush_Barrier_62", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.06f, 0.00f, 189.82f), "BossRush_Barrier_63", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 187.61f), "BossRush_Barrier_64", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.07f, 0.00f, 185.38f), "BossRush_Barrier_65", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.10f, 0.00f, 183.12f), "BossRush_Barrier_66", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 180.88f), "BossRush_Barrier_67", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.09f, 0.00f, 178.68f), "BossRush_Barrier_68", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.12f, 0.00f, 176.52f), "BossRush_Barrier_69", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.12f, 0.00f, 174.28f), "BossRush_Barrier_70", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.13f, 0.00f, 172.10f), "BossRush_Barrier_71", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.15f, 0.00f, 169.86f), "BossRush_Barrier_72", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 167.65f), "BossRush_Barrier_73", 270f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(150.17f, 0.00f, 165.50f), "BossRush_Barrier_74", 270f));

                // 南侧围栏补充（旋转180°和特殊角度）
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(130.64f, 0.00f, 162.87f), "BossRush_Barrier_75", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(132.84f, 0.00f, 162.84f), "BossRush_Barrier_76", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(135.07f, 0.00f, 162.82f), "BossRush_Barrier_77", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(137.24f, 0.00f, 162.80f), "BossRush_Barrier_78", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(139.52f, 0.00f, 162.80f), "BossRush_Barrier_79", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(141.76f, 0.00f, 162.78f), "BossRush_Barrier_80", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(144.03f, 0.00f, 162.80f), "BossRush_Barrier_81", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(146.31f, 0.00f, 162.81f), "BossRush_Barrier_82", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(148.57f, 0.00f, 162.78f), "BossRush_Barrier_83", 180f));
                configs.Add(new MapObjectCloneConfig("Prfb_Roadblock_33", "ENV", new Vector3(149.91f, 0.00f, 163.34f), "BossRush_Barrier_84", 285f));

                // 撤离点 - 复制场景中的 Exit(Clone) 到指定位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_HiddenWarehouse",
                    new Vector3(108.64f, 0.02f, 213.95f),
                    "BossRush_Exit_FireSmoke"
                ));
            }
            else if (sceneName == "Level_Farm_01")
            {
                // 农场镇地图的围栏配置（使用 Prfb_Shop_Shelf_01_53 商店货架）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Zone_D1/Pfb_Store_01/Indoor/Prfb_Shop_Shelf_01_53
                // 直接父对象是 Indoor

                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(368.33f, 0.02f, 600.91f), "BossRush_Barrier_1", 2f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(365.53f, 0.02f, 597.44f), "BossRush_Barrier_2", 272f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(384.55f, 0.02f, 600.93f), "BossRush_Barrier_3", 2f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(420.01f, 0.02f, 589.50f), "BossRush_Barrier_4", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(419.89f, 0.02f, 582.90f), "BossRush_Barrier_5", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(420.07f, 0.02f, 576.10f), "BossRush_Barrier_6", 92f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(400.42f, 0.02f, 557.33f), "BossRush_Barrier_7", 182f));
                configs.Add(new MapObjectCloneConfig("Prfb_Shop_Shelf_01_53", "Indoor", new Vector3(368.64f, 0.02f, 557.40f), "BossRush_Barrier_8", 182f));

                // 撤离点 - 复制场景中的 Exit(Clone) 到指定位置
                configs.Add(new MapObjectCloneConfig(
                    "Exit(Clone)",
                    "Level_Farm_01",
                    new Vector3(355.37f, 0.02f, 589.19f),
                    "BossRush_Exit_FireSmoke"
                ));
            }
            else if (sceneName == "Level_JLab_1")
            {
                // J-Lab 实验室地图的围栏配置（使用 Pfb_JLABContainer_13 集装箱）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Center_01/Group/Pfb_JLABContainer_13

                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-94.99f, 0.00f, -56.06f), "BossRush_Barrier_1", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-80.70f, 0.00f, -44.28f), "BossRush_Barrier_2", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-65.28f, 1.01f, -17.18f), "BossRush_Barrier_3", 90f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-11.35f, 0.00f, -16.58f), "BossRush_Barrier_4", 90f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(3.02f, 0.00f, -49.92f), "BossRush_Barrier_5", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(3.06f, 0.00f, -54.86f), "BossRush_Barrier_6", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(5.85f, 0.00f, -56.93f), "BossRush_Barrier_7", 330f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-24.20f, 0.00f, -73.11f), "BossRush_Barrier_8", 270f));
                configs.Add(new MapObjectCloneConfig("Pfb_JLABContainer_13", "Group", new Vector3(-54.28f, 0.00f, -63.48f), "BossRush_Barrier_9", 270f));

                // 撤离点 - 复制场景中的 ExitNoSmoke_1 到指定位置（无烟雾版本，使用 CapsuleCollider）
                configs.Add(new MapObjectCloneConfig(
                    "ExitNoSmoke_1",
                    "Exits",
                    new Vector3(-90.76f, 0.02f, -56.25f),
                    "BossRush_Exit_JLab"
                ));
            }
            else if (sceneName == "Level_StormZone_B0")
            {
                // 风暴区地下地图的围栏配置（使用 Pfb_BarbedWire_01_03 铁丝网）
                // 围栏数据从 Player.log 提取
                // 模板路径: Env/Boss/BarbedWire_Line/Pfb_BarbedWire_01_03

                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(102.76f, 0.09f, 454.12f), "BossRush_Barrier_1", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(105.05f, 0.00f, 454.16f), "BossRush_Barrier_2", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(107.33f, 0.00f, 454.09f), "BossRush_Barrier_3", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(109.64f, 0.00f, 454.07f), "BossRush_Barrier_4", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(111.90f, 0.00f, 454.12f), "BossRush_Barrier_5", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(114.18f, 0.00f, 454.09f), "BossRush_Barrier_6", 360f));
                configs.Add(new MapObjectCloneConfig("Pfb_BarbedWire_01_03", "BarbedWire_Line", new Vector3(116.46f, 0.00f, 454.04f), "BossRush_Barrier_7", 345f));

                // 撤离点使用 CreateBossRushExit 方法创建（不再复制模板）
            }
            else if (sceneName == "Level_SnowMilitaryBase")
            {
                // 37号实验区地图的围栏配置（使用 Pfb_Car_02 汽车作为障碍物）
                // 模板路径: Pfb_MilitaryBase/Indoor/Pfb_Car_02

                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(471.95f, -0.01f, 525.12f), "BossRush_Barrier_1", 276f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(477.32f, -0.01f, 523.98f), "BossRush_Barrier_2", 276f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(520.18f, -0.01f, 537.38f), "BossRush_Barrier_3", 276f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(521.07f, -0.01f, 541.55f), "BossRush_Barrier_4", 261f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(520.36f, -0.01f, 562.45f), "BossRush_Barrier_5", 261f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(494.75f, 0.75f, 576.29f), "BossRush_Barrier_6", 291f));
                configs.Add(new MapObjectCloneConfig("Pfb_Car_02", "Indoor", new Vector3(476.92f, 0.02f, 574.87f), "BossRush_Barrier_7", 276f));
            }
            else if (sceneName == "Level_SnowMilitaryBase_ColdStorage")
            {
                // 迷宫地图的围栏配置（使用 Pfb_RoadblockGRP_3 路障组合）
                // 模板路径: Pfb_RoadblockGRP_3/Col_Wall_FowBlock_1
                // 根对象: Pfb_RoadblockGRP_3（场景根级对象，parentNamePrefix 留空）

                configs.Add(new MapObjectCloneConfig("Pfb_RoadblockGRP_3", "", new Vector3(-11.28f, 0.00f, -34.97f), "BossRush_Barrier_1", 272f));
                configs.Add(new MapObjectCloneConfig("Pfb_RoadblockGRP_3", "", new Vector3(-11.13f, 0.00f, -30.67f), "BossRush_Barrier_2", 272f));
                configs.Add(new MapObjectCloneConfig("Pfb_RoadblockGRP_3", "", new Vector3(7.31f, 0.00f, -65.16f), "BossRush_Barrier_3", 182f));
                configs.Add(new MapObjectCloneConfig("Pfb_RoadblockGRP_3", "", new Vector3(11.50f, 0.00f, -65.29f), "BossRush_Barrier_4", 182f));
            }
            // 后续可以添加其他地图的配置

            return configs;
        }

        /// <summary>
        /// 使用游戏原生 exitPrefab 创建 BossRush 撤离点
        /// </summary>
        private void CreateBossRushExit(Vector3 position, string exitName)
        {
            try
            {
                // 方案1：使用 LevelManager.ExitCreator.exitPrefab（最优方案，直接使用游戏原生预制体）
                if (LevelManager.Instance != null && LevelManager.Instance.ExitCreator != null)
                {
                    GameObject exitPrefab = LevelManager.Instance.ExitCreator.exitPrefab;
                    if (exitPrefab != null)
                    {
                        GameObject exit = UnityEngine.Object.Instantiate(exitPrefab, position, Quaternion.identity);
                        exit.name = exitName;
                        exit.SetActive(true);

                        // 确保 CountDownArea 启用
                        CountDownArea countDown = exit.GetComponent<CountDownArea>();
                        if (countDown != null)
                        {
                            countDown.enabled = true;
                        }

                        // 禁用烟雾/粒子效果（室内场景不需要）
                        DisableExitSmokeEffects(exit);

                        DevLog("[BossRush] 使用 exitPrefab 创建撤离点: " + exitName + " 位置: " + position);
                        return;
                    }
                }

                // 方案2：从头创建一个简单的撤离点（无烟雾效果）
                // 跳过 FindObjectsOfType 遍历，直接创建简单撤离点，性能更优
                CreateSimpleExit(position, exitName);
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] CreateBossRushExit 失败: " + e.Message);
                // 回退到简单撤离点
                CreateSimpleExit(position, exitName);
            }
        }

        /// <summary>
        /// 禁用撤离点的烟雾/粒子效果（用于室内场景）
        /// [性能优化] 使用字符串缓存避免重复 ToLower 调用
        /// </summary>
        private void DisableExitSmokeEffects(GameObject exit)
        {
            try
            {
                int disabledCount = 0;

                // 查找并禁用名称包含烟雾/粒子相关关键词的子对象
                foreach (Transform child in exit.GetComponentsInChildren<Transform>(true))
                {
                    if (child == exit.transform) continue;  // 跳过根对象

                    // [性能优化] 使用 IndexOf 替代 Contains + ToLower，减少字符串分配
                    string name = child.name;
                    if (name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("fog", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("particle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("effect", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("vfx", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        child.gameObject.SetActive(false);
                        disabledCount++;
                    }
                }

                if (disabledCount > 0)
                {
                    DevLog("[BossRush] 已禁用撤离点烟雾效果，禁用子对象数: " + disabledCount);
                }
            }
            catch (Exception e)
            {
                LogIntegrationWarningLimited(
                    "DisableExitSmokeEffects",
                    "禁用撤离点烟雾效果失败",
                    e);
            }
        }

        /// <summary>
        /// 从头创建一个简单的撤离点（当无法获取预制体时使用）
        /// </summary>
        private void CreateSimpleExit(Vector3 position, string exitName)
        {
            try
            {
                // 创建撤离点 GameObject
                GameObject exit = new GameObject(exitName);
                exit.transform.position = position;

                // 添加触发器 Collider
                BoxCollider collider = exit.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(3f, 2f, 3f);  // 3x2x3 的触发区域
                collider.center = new Vector3(0f, 1f, 0f);  // 中心稍微抬高

                // 添加 CountDownArea 组件
                CountDownArea countDown = exit.AddComponent<CountDownArea>();

                // 通过反射设置 requiredExtrationTime（私有字段）
                try
                {
                    System.Reflection.FieldInfo timeField = typeof(CountDownArea).GetField("requiredExtrationTime",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (timeField != null)
                    {
                        timeField.SetValue(countDown, 5f);  // 5秒撤离时间
                    }
                }
                catch (Exception e)
                {
                    LogIntegrationWarningLimited(
                        "CreateSimpleExit_requiredExtractionTime",
                        "设置简易撤离点倒计时失败",
                        e);
                }

                // 订阅撤离成功事件
                countDown.onCountDownSucceed = new UnityEngine.Events.UnityEvent();
                countDown.onCountDownSucceed.AddListener(() => {
                    DevLog("[BossRush] 撤离成功！");
                    // 触发游戏的撤离逻辑
                    try
                    {
                        if (LevelManager.Instance != null)
                        {
                            // 创建撤离信息并通知
                            EvacuationInfo info = new EvacuationInfo(
                                Duckov.Scenes.MultiSceneCore.ActiveSubSceneID,
                                position
                            );
                            LevelManager.Instance.NotifyEvacuated(info);
                        }
                    }
                    catch (System.Exception e)
                    {
                        DevLog("[BossRush] 调用 NotifyEvacuated 失败: " + e.Message);
                    }
                });

                // 订阅倒计时开始/停止事件（显示UI）
                countDown.onCountDownStarted = new UnityEngine.Events.UnityEvent<CountDownArea>();
                countDown.onCountDownStarted.AddListener((area) => {
                    EvacuationCountdownUI.Request(area);
                });

                countDown.onCountDownStopped = new UnityEngine.Events.UnityEvent<CountDownArea>();
                countDown.onCountDownStopped.AddListener((area) => {
                    EvacuationCountdownUI.Release(area);
                });

                // 室内场景不创建视觉指示器（绿色光柱）

                DevLog("[BossRush] 创建简单撤离点: " + exitName + " 位置: " + position);
            }
            catch (System.Exception e)
            {
                DevLog("[BossRush] CreateSimpleExit 失败: " + e.Message);
            }
        }

    }
}
