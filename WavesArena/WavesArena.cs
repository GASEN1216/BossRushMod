// ============================================================================
// WavesArena.cs - 波次与竞技场管理
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的波次系统和竞技场逻辑，包括：
//   - 波次敌人生成和管理
//   - 玩家传送到官方挑战场景
//   - 波次间隔倒计时
//   
// 主要功能：
//   - StartBossRush: 开始 BossRush 模式
//   - TeleportToBossRushAsync: 异步传送到竞技场
//   - SpawnNextEnemy: 生成下一波敌人
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

namespace BossRush
{
    /// <summary>
    /// 波次与竞技场管理模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 前期波次Boss排除

        /// <summary>
        /// 前期波次需要排除的强力 Boss 名称列表
        /// 包括：口口口口、四骑士、龙裔遗族和焚天龙皇
        /// </summary>
        private static readonly HashSet<string> EarlyWaveExcludedBosses = new HashSet<string>
        {
            "Cname_StormBoss1",    // 口口口口 或 四骑士
            "Cname_StormBoss2",    // 口口口口 或 四骑士
            "Cname_StormBoss3",    // 口口口口 或 四骑士
            "Cname_StormBoss4",    // 口口口口 或 四骑士
            "Cname_StormBoss5",    // 口口口口 或 四骑士
            "DragonDescendant",    // 龙裔遗族
            "boss_dragonking"      // 焚天龙皇
        };

        /// <summary>
        /// 检查是否是前期波次需要排除的强力Boss
        /// </summary>
        private bool IsEarlyWaveExcludedBoss(string bossName)
        {
            if (string.IsNullOrEmpty(bossName)) return false;
            return EarlyWaveExcludedBosses.Contains(bossName);
        }

        /// <summary>
        /// 预处理：确保前20波不出现强力Boss
        /// 在挑战开始时调用一次，将前20位中的强力Boss与后面的普通Boss交换
        /// </summary>
        private void EnsureEarlyWavesNoStrongBoss()
        {
            if (enemyPresets == null || enemyPresets.Count <= 20) return;

            int swapCount = 0;
            int nextSwapTarget = 20; // 从第20位开始找可交换的普通Boss

            for (int i = 0; i < 20 && i < enemyPresets.Count; i++)
            {
                if (!IsEarlyWaveExcludedBoss(enemyPresets[i].name)) continue;
                
                // 找一个第10位之后的普通Boss来交换
                while (nextSwapTarget < enemyPresets.Count && 
                       IsEarlyWaveExcludedBoss(enemyPresets[nextSwapTarget].name))
                {
                    nextSwapTarget++;
                }
                
                if (nextSwapTarget >= enemyPresets.Count) break; // 没有可交换的了
                
                // 交换
                var tmp = enemyPresets[i];
                enemyPresets[i] = enemyPresets[nextSwapTarget];
                enemyPresets[nextSwapTarget] = tmp;
                nextSwapTarget++;
                swapCount++;
            }
            
            if (swapCount > 0)
            {
                DevLog("[BossRush] 前20波强力Boss预处理完成，交换了 " + swapCount + " 个Boss");
            }
        }

        #endregion

        /// <summary>
        /// 开始 BossRush 模式（WavesArena 分部实现，由 ModBehaviour.StartBossRush 转发调用）
        /// </summary>
        private void StartBossRush_WavesArena(BossRushInteractable interactionSource = null)
        {
            if (IsActive)
            {
                ShowMessage(L10n.T("BossRush已经在进行中！", "BossRush is already in progress!"));
                return;
            }

            BossRushMapSelectionHelper.MarkEntryFlowFromDirectTeleport();

            // 没有从交互点传入时，使用默认难度（每波1个Boss）
            if (interactionSource == null)
            {
                bossesPerWave = 1;
            }

            // 标记：后续进入 DEMO 挑战地图应由 BossRush 控制
            bossRushArenaPlanned = true;
            
            // 设置 pending 地图索引为 DEMO 挑战地图（索引 0），确保中间场景检查能正确识别目标场景
            BossRushMapSelectionHelper.SetPendingMapEntryIndex(0);
            DevLog("[BossRush] F9 快捷启动：设置 pending 地图索引为 0 (DEMO挑战)");
            
            // 1. 尝试使用 CharacterMainControl.Main (如果是静态单例)
            // 由于不确定是否存在静态 Main 属性，我们使用 FindObjectOfType
            CharacterMainControl main = null;
            try
            {
                main = CharacterMainControl.Main;
            }
            catch { }

            if (main != null)
            {
                playerCharacter = main;
                try
                {
                    DevLog("[BossRush] StartBossRush: 使用 CharacterMainControl.Main 作为玩家角色: " + main.name + " (scene=" + main.gameObject.scene.name + ") pos=" + main.transform.position);
                }
                catch { }
            }
            else
            {
                try
                {
                    var candidate = FindObjectOfType<CharacterMainControl>();
                    if (candidate != null)
                    {
                        bool isMain = false;
                        try
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(candidate);
                        }
                        catch { }
                        try
                        {
                            DevLog("[BossRush] StartBossRush: FindObjectOfType 得到候选角色: " + candidate.name + " (scene=" + candidate.gameObject.scene.name + ") pos=" + candidate.transform.position + ", IsMainCharacter=" + isMain);
                        }
                        catch { }
                        if (isMain)
                        {
                            playerCharacter = candidate;
                        }
                    }
                }
                catch { }
            }

            // 2. 如果没找到，尝试查找所有 CharacterMainControl 并检查 IsMainCharacter 属性
            if (playerCharacter == null)
            {
                try
                {
                    var allCharacters = FindObjectsOfType<CharacterMainControl>();
                    foreach (var character in allCharacters)
                    {
                        bool isMain = false;
                        try
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(character);
                        }
                        catch { }
                        try
                        {
                            DevLog("[BossRush] StartBossRush: 扫描角色: " + character.name + " (scene=" + character.gameObject.scene.name + ") pos=" + character.transform.position + ", IsMainCharacter=" + isMain);
                        }
                        catch { }
                        if (isMain)
                        {
                            playerCharacter = character;
                            break;
                        }
                    }
                }
                catch { }
            }

            // 3. 如果还是没找到，尝试通过 Tag 查找
            if (playerCharacter == null)
            {
                try
                {
                    var playerObj = GameObject.FindGameObjectWithTag("Player");
                    if (playerObj != null)
                    {
                        var candidate = playerObj.GetComponent<CharacterMainControl>();
                        if (candidate != null)
                        {
                            bool isMain = false;
                            try
                            {
                                isMain = CharacterMainControlExtensions.IsMainCharacter(candidate);
                            }
                            catch { }
                            try
                            {
                                DevLog("[BossRush] StartBossRush: Tag=Player 得到角色: " + candidate.name + " (scene=" + candidate.gameObject.scene.name + ") pos=" + candidate.transform.position + ", IsMainCharacter=" + isMain);
                            }
                            catch { }
                            playerCharacter = candidate;
                        }
                        else
                        {
                            DevLog("[BossRush] [WARNING] StartBossRush: Tag=Player 对象上没有 CharacterMainControl 组件: " + playerObj.name);
                        }
                    }
                    else
                    {
                        DevLog("[BossRush] [WARNING] StartBossRush: 未找到 Tag=Player 对象");
                    }
                }
                catch { }
            }
            
            if (playerCharacter == null)
            {
                ShowMessage(L10n.T("无法找到玩家角色！请确保在游戏中！", "Player not found! Make sure you are in game!"));
                DevLog("[BossRush] [ERROR] 无法找到玩家角色！");
                return;
            }
            else
            {
                try
                {
                    var finalMain = playerCharacter as CharacterMainControl;
                    if (finalMain != null)
                    {
                        DevLog("[BossRush] StartBossRush: 最终锁定玩家角色: " + finalMain.name + " (scene=" + finalMain.gameObject.scene.name + ") pos=" + finalMain.transform.position);
                    }
                    else
                    {
                        DevLog("[BossRush] [WARNING] StartBossRush: 最终 playerCharacter 不是 CharacterMainControl 类型: " + playerCharacter.name);
                    }
                }
                catch { }
            }
            
            ShowMessage(L10n.T("开始BossRush模式，正在前往竞技场...", "Starting BossRush, heading to arena..."));
            DevLog("[BossRush] 开始BossRush模式，正在前往竞技场...");
            
            try
            {
                if (MultiSceneCore.Instance != null)
                {
                    CharacterMainControl finalMainForTeleport = null;
                    try
                    {
                        finalMainForTeleport = playerCharacter as CharacterMainControl;
                    }
                    catch {}
                    if (finalMainForTeleport == null)
                    {
                        try
                        {
                            finalMainForTeleport = CharacterMainControl.Main;
                        }
                        catch {}
                    }

                    if (finalMainForTeleport != null)
                    {
                        TeleportToBossRushAsync();
                    }
                    else
                    {
                        DevLog("[BossRush] [WARNING] StartBossRush: 未找到用于传送的玩家角色 CharacterMainControl");
                    }
                }
                else
                {
                    DevLog("[BossRush] [WARNING] StartBossRush: MultiSceneCore.Instance 为 null，尝试使用 SceneLoader 方案");
                    TeleportToBossRushAsync();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] StartBossRush: 启动传送任务时出错: " + e.Message);
            }

            // 直接开始第一波Boss（会在短暂延迟后在玩家附近生成）
        }

        private async void TeleportToBossRushAsync_WavesArena()
        {
            bool usedTicketTeleport = false;

            // 直接使用 SceneLoader 方案加载 BossRush 场景（与原版挑战船票流程一致）
            try
            {
                if (SceneLoader.Instance != null)
                {
                    DevLog("[BossRush] TeleportToBossRushAsync: 使用 SceneLoader.LoadScene 加载 BossRush 场景, SceneID=" + BossRushArenaSceneID);
                    try
                    {
                        await SceneLoader.Instance.LoadScene(
                            BossRushArenaSceneID,
                            null,
                            false,
                            false,
                            true,
                            false,
                            default(MultiSceneLocation),
                            true,
                            false
                        );
                        usedTicketTeleport = true;
                    }
                    catch (Exception ex)
                    {
                        DevLog("[BossRush] [ERROR] TeleportToBossRushAsync: SceneLoader.LoadScene 调用失败: " + ex.Message + "\n" + ex.StackTrace);
                    }
                }
                else
                {
                    DevLog("[BossRush] [ERROR] TeleportToBossRushAsync: SceneLoader.Instance 为 null，无法加载 BossRush 场景");
                }
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] [ERROR] TeleportToBossRushAsync: 处理 SceneLoader 方案时出错: " + ex.Message);
            }

            if (!usedTicketTeleport)
            {
                ShowMessage(L10n.T("进入BossRush场景失败，请查看日志", "Failed to enter BossRush scene, check logs"));
            }
        }




        private void TryCreateReturnInteractable_WavesArena()
        {
            try
            {
                if (GameObject.Find("BossRushReturnButton_DemoChallenge") != null)
                {
                    return;
                }

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                if (main == null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch {}
                }

                if (main == null)
                {
                    DevLog("[BossRush] [WARNING] TryCreateReturnInteractable: 无法找到玩家角色");
                    return;
                }

                Vector3 pos = main.transform.position + main.transform.forward * 2f;
                pos.y += 0.5f;

                GameObject returnButton = GameObject.CreatePrimitive(PrimitiveType.Cube);
                returnButton.name = "BossRushReturnButton_DemoChallenge";
                returnButton.transform.position = pos;

                var renderer = returnButton.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = Color.green;
                }

                var col = returnButton.GetComponent<Collider>();
                if (col != null)
                {
                    col.isTrigger = true;
                }

                returnButton.AddComponent<BossRushReturnInteractable>();

                DevLog("[BossRush] 已创建 BossRush 返回出生点交互点");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] TryCreateReturnInteractable 出错: " + e.Message);
            }
        }


        /// <summary>
        /// 开始第一波Boss（在竞技场内）- 单波生成模式
        /// </summary>
        public void StartFirstWave()
        {
            // [DEBUG] 记录当前状态
            DevLog("[BossRush] StartFirstWave 调用: IsActive=" + IsActive + ", bossesPerWave=" + bossesPerWave + ", infiniteHellMode=" + infiniteHellMode);
            
            if (!IsActive)
            {
                // 记录玩家当前位置作为出生点（BossRush失败时传送回此处）
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        demoChallengeStartPosition = main.transform.position;
                        DevLog("[BossRush] 已记录玩家出生点: " + demoChallengeStartPosition);
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 记录玩家出生点失败: " + e.Message);
                }
                
                // 每次挑战开始时随机打乱本次要挑战的敌人顺序
                try
                {
                    if (enemyPresets != null && enemyPresets.Count > 1)
                    {
                        // Fisher-Yates 洗牌
                        for (int i = enemyPresets.Count - 1; i > 0; i--)
                        {
                            int j = UnityEngine.Random.Range(0, i + 1);
                            if (j != i)
                            {
                                var tmp = enemyPresets[i];
                                enemyPresets[i] = enemyPresets[j];
                                enemyPresets[j] = tmp;
                            }
                        }
                        
                        // 弹指可灭/有点意思模式：预处理，确保前20波不出现强力Boss
                        // 将前20位中的强力Boss与第20位之后的普通Boss交换
                        if (!infiniteHellMode)
                        {
                            EnsureEarlyWavesNoStrongBoss();
                        }
                        
                        DevLog("[BossRush] 已随机打乱本次 BossRush 的敌人出场顺序");
                    }
                }
                catch (Exception shuffleEx)
                {
                    DevLog("[BossRush] [WARNING] 打乱敌人顺序时出错: " + shuffleEx.Message);
                }

                // 清理场景中现有的敌人，准备开始BossRush
                ClearEnemiesForBossRush();
                
                BeginAchievementSession(infiniteHellMode ? "InfiniteHell" : "BossRush");
                ShowMessage(L10n.T("开始BossRush挑战！", "BossRush challenge started!"));
                SetBossRushRuntimeActive(true);
                currentEnemyIndex = 0;
                defeatedEnemies = 0;
                
                // 使用过滤后的 Boss 池计算总数，确保横幅显示正确的 Boss 数量
                var filteredPresetsForCount = GetFilteredEnemyPresets();
                totalEnemies = (filteredPresetsForCount != null) ? filteredPresetsForCount.Count : 0;
                
                bossesInCurrentWaveTotal = 0;
                bossesInCurrentWaveRemaining = 0;
                currentWaveBosses.Clear();
                
                // 清空掉落追踪字典
                bossSpawnTimes.Clear();
                bossOriginalLootCounts.Clear();
                countedDeadBosses.Clear();
                
                DevLog("[BossRush] 启动单波生成模式，共 " + totalEnemies + " 个敌人（已过滤）");
                
                // 订阅敌人死亡事件（只订阅一次）
                Health.OnDead -= OnEnemyDiedWithDamageInfo; // 先取消避免重复
                Health.OnDead += OnEnemyDiedWithDamageInfo;
                
                // 立即生成第一个敌人
                SpawnNextEnemy();
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

        /// <summary>
        /// 从刷怪点数组中选取多个不在安全距离内的点，按距玩家由近到远排序
        /// <para>用于多Boss同波生成时分配不重复的安全刷怪位置</para>
        /// </summary>
        private static List<Vector3> FindMultipleSafeSpawnPoints(int count, Vector3[] spawnPoints, Vector3 playerPos)
        {
            var result = new List<Vector3>(count);
            if (spawnPoints == null || spawnPoints.Length == 0 || count <= 0)
            {
                return result;
            }

            // 收集所有安全距离外的点，按距玩家由近到远排序
            var safeCandidates = new List<(int idx, Vector3 pos, float distSqr)>();
            var allByDistance = new List<(int idx, Vector3 pos, float distSqr)>(spawnPoints.Length);

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                float distSqr = (spawnPoints[i] - playerPos).sqrMagnitude;
                allByDistance.Add((i, spawnPoints[i], distSqr));
                if (distSqr >= SPAWN_SAFE_DISTANCE_SQR)
                {
                    safeCandidates.Add((i, spawnPoints[i], distSqr));
                }
            }

            // 按距离由近到远排序
            safeCandidates.Sort((a, b) => a.distSqr.CompareTo(b.distSqr));

            var usedIndices = new HashSet<int>();

            // 优先使用安全距离外的点
            for (int i = 0; i < safeCandidates.Count && result.Count < count; i++)
            {
                result.Add(GetSafeBossSpawnPosition(safeCandidates[i].pos));
                usedIndices.Add(safeCandidates[i].idx);
            }

            // 不够的话，从未使用的点中按距离由远到近补充
            if (result.Count < count)
            {
                allByDistance.Sort((a, b) => b.distSqr.CompareTo(a.distSqr));
                for (int i = 0; i < allByDistance.Count && result.Count < count; i++)
                {
                    if (!usedIndices.Contains(allByDistance[i].idx))
                    {
                        result.Add(GetSafeBossSpawnPosition(allByDistance[i].pos));
                        usedIndices.Add(allByDistance[i].idx);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 从刷怪点列表中选取多个不在安全距离内的点（List版本）
        /// </summary>
        private static List<Vector3> FindMultipleSafeSpawnPoints(int count, List<Vector3> spawnPoints, Vector3 playerPos)
        {
            var result = new List<Vector3>(count);
            if (spawnPoints == null || spawnPoints.Count == 0 || count <= 0)
            {
                return result;
            }

            var safeCandidates = new List<(int idx, Vector3 pos, float distSqr)>();
            var allByDistance = new List<(int idx, Vector3 pos, float distSqr)>(spawnPoints.Count);

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                float distSqr = (spawnPoints[i] - playerPos).sqrMagnitude;
                allByDistance.Add((i, spawnPoints[i], distSqr));
                if (distSqr >= SPAWN_SAFE_DISTANCE_SQR)
                {
                    safeCandidates.Add((i, spawnPoints[i], distSqr));
                }
            }

            safeCandidates.Sort((a, b) => a.distSqr.CompareTo(b.distSqr));

            var usedIndices = new HashSet<int>();

            for (int i = 0; i < safeCandidates.Count && result.Count < count; i++)
            {
                result.Add(GetSafeBossSpawnPosition(safeCandidates[i].pos));
                usedIndices.Add(safeCandidates[i].idx);
            }

            if (result.Count < count)
            {
                allByDistance.Sort((a, b) => b.distSqr.CompareTo(a.distSqr));
                for (int i = 0; i < allByDistance.Count && result.Count < count; i++)
                {
                    if (!usedIndices.Contains(allByDistance[i].idx))
                    {
                        result.Add(GetSafeBossSpawnPosition(allByDistance[i].pos));
                        usedIndices.Add(allByDistance[i].idx);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 校验并修正Boss位置（生成后调用，防止Boss卡在地下）
        /// </summary>
        private void ValidateAndFixBossPosition(CharacterMainControl boss)
        {
            if (boss == null) return;
            
            try
            {
                Vector3 currentPos = boss.transform.position;

                bool needsRecovery = false;
                string reason = null;

                Vector3 groundAlignedPos;
                if (TryResolveGroundAlignedPosition(currentPos, 8f, 5f, out groundAlignedPos))
                {
                    if (groundAlignedPos.y - currentPos.y >= 0.75f)
                    {
                        needsRecovery = true;
                        reason = "spawn_below_ground";
                    }
                }
                else
                {
                    EnemyRecoveryState recoveryState;
                    if (enemyRecoveryStates.TryGetValue(boss, out recoveryState) &&
                        recoveryState.hasExcludedAnchorPosition &&
                        recoveryState.excludedAnchorPosition.y - currentPos.y >= 6f)
                    {
                        needsRecovery = true;
                        reason = "spawn_void";
                    }
                }

                if (!needsRecovery)
                {
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    return;
                }

                EnemyRecoveryState state;
                if (!enemyRecoveryStates.TryGetValue(boss, out state))
                {
                    state = new EnemyRecoveryState
                    {
                        lastSamplePosition = currentPos,
                        lastMovedTime = Time.time,
                        lastRecoveryTime = -4f,
                        excludedAnchorPosition = currentPos,
                        hasExcludedAnchorPosition = true,
                        continuousFallSamples = 0
                    };
                }

                Vector3 recoveredPos;
                if (TryRecoverEnemyToNearestSpawnPoint(boss, state, main, reason, out recoveredPos))
                {
                    state.lastMovedTime = Time.time;
                    state.lastRecoveryTime = Time.time;
                    state.lastSamplePosition = recoveredPos;
                    enemyRecoveryStates[boss] = state;
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] ValidateAndFixBossPosition 异常: " + e.Message);
            }
        }
        
        /// <summary>
        /// 延迟校验Boss位置的协程（给地形加载留出时间）
        /// </summary>
        private IEnumerator DelayedBossPositionValidation(CharacterMainControl boss, float delay)
        {
            if (boss == null) yield break;
            
            yield return new WaitForSeconds(delay);
            
            if (boss != null && boss.gameObject != null)
            {
                ValidateAndFixBossPosition(boss);
            }
        }
        
        /// <summary>
        /// 生成下一个敌人（根据 bossesPerWave 支持单Boss或多Boss一波）
        /// </summary>
        private void SpawnNextEnemy()
        {
            // [DEBUG] 记录当前状态
            DevLog("[BossRush] SpawnNextEnemy 调用: bossesPerWave=" + bossesPerWave + ", currentEnemyIndex=" + currentEnemyIndex + ", totalEnemies=" + totalEnemies);
            
            // 通知快递员 Boss 战开始
            NotifyCourierBossFightStart();
            
            // 通知快递员有Boss了（不再是召唤间隔）
            NotifyCourierNoBoss(false);
            
            // 获取过滤后的 Boss 列表
            var filteredPresets = GetFilteredEnemyPresets();
            
            // 检查 Boss 池是否为空
            if (filteredPresets == null || filteredPresets.Count == 0)
            {
                ShowMessage(L10n.T("Boss池为空！请至少启用一个Boss。(Ctrl+F10 打开设置)", "Boss pool is empty! Enable at least one Boss. (Ctrl+F10 to open settings)"));
                DevLog("[BossRush] [WARNING] SpawnNextEnemy: Boss 池为空，无法生成敌人");
                return;
            }

            // 普通模式：跑完列表后直接通关
            if (!infiniteHellMode)
            {
                if (currentEnemyIndex >= filteredPresets.Count)
                {
                    // 所有敌人已击败，显示完成对话
                    OnAllEnemiesDefeated();
                    return;
                }
            }

            EnemyPresetInfo preset = null;
            if (infiniteHellMode)
            {
                // 无间炼狱：每一波按权重随机选择Boss，不再依赖 currentEnemyIndex 作为索引
                preset = PickRandomEnemyForInfiniteHell();
                if (preset == null)
                {
                    DevLog("[BossRush] [ERROR] SpawnNextEnemy: InfiniteHell 模式下未找到可用敌人预设");
                    return;
                }
            }
            else
            {
                // 弹指可灭/有点意思模式：按顺序选取Boss
                // 强力Boss已在挑战开始时预处理，前20波不会出现
                preset = filteredPresets[currentEnemyIndex];
            }

            DevLog("[BossRush] 生成第 " + (currentEnemyIndex + 1) + "/" + totalEnemies + " 波: " + preset.displayName);

            try
            {
                // 获取玩家
                CharacterMainControl playerMain = CharacterMainControl.Main;
                if (playerMain == null)
                {
                    DevLog("[BossRush] [ERROR] 玩家未找到，无法生成敌人");
                    return;
                }

                // 使用当前地图的刷新点（根据场景动态选择）
                Vector3[] spawnPoints = GetCurrentSpawnPoints();
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    DevLog("[BossRush] [ERROR] 当前地图刷新点为空，无法生成敌人");
                    return;
                }

                if (bossesPerWave <= 1)
                {
                    // 单Boss模式：每波只生成一个Boss，同样维护波次计数，便于自检逻辑使用
                    bossesInCurrentWaveTotal = 1;
                    bossesInCurrentWaveRemaining = 1;
                    currentWaveBosses.Clear();

                    Vector3 spawnPos = FindNearestSafeSpawnPoint(spawnPoints, playerMain.transform.position);

                    // 显示敌人生成横幅（在生成前显示）
                    ShowEnemyBanner(preset.displayName, spawnPos, playerMain.transform.position);

                    // 使用带验证的异步生成方法
                    SpawnBossWithVerificationAsync(preset, spawnPos, spawnPoints).Forget();
                }
                else
                {
                    // 多Boss模式：同一波生成 bossesPerWave 个相同Boss
                    bossesInCurrentWaveTotal = bossesPerWave;
                    bossesInCurrentWaveRemaining = bossesPerWave;
                    currentWaveBosses.Clear();

                    // 收集本波需要生成的所有Boss预设信息（位置由生成方法内部分配，确保不重复）
                    var bossSpawnInfos = new List<(EnemyPresetInfo preset, Vector3 position)>();
                    
                    for (int i = 0; i < bossesPerWave; i++)
                    {
                        EnemyPresetInfo wavePreset = preset;
                        if (infiniteHellMode)
                        {
                            var altPreset = PickRandomEnemyForInfiniteHell();
                            if (altPreset != null)
                            {
                                wavePreset = altPreset;
                            }
                        }

                        // 位置占位，实际位置由 SpawnMultipleBossesWithVerificationAsync 内部分配
                        bossSpawnInfos.Add((wavePreset, Vector3.zero));
                    }
                    
                    // 显示第一个Boss的横幅（使用第一个刷怪点作为参考）
                    if (bossSpawnInfos.Count > 0)
                    {
                        Vector3 bannerPos = GetSafeBossSpawnPosition(spawnPoints[0]);
                        ShowEnemyBanner(bossSpawnInfos[0].preset.displayName, bannerPos, playerMain.transform.position);
                    }
                    
                    // 使用带验证和重试的批量生成方法
                    SpawnMultipleBossesWithVerificationAsync(bossSpawnInfos, spawnPoints).Forget();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 生成敌人失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 单Boss模式：带验证的异步生成（包含重试机制）
        /// </summary>
        private async UniTaskVoid SpawnBossWithVerificationAsync(EnemyPresetInfo preset, Vector3 position, Vector3[] spawnPoints)
        {
            const int maxRetries = 3;
            int attempt = 0;
            CharacterMainControl spawnedBoss = null;
            
            while (attempt < maxRetries)
            {
                attempt++;
                
                if (attempt > 1)
                {
                    DevLog("[BossRush] 单Boss生成重试 #" + attempt + ": " + preset.displayName);
                    // 重试时使用安全距离外最近的刷怪点
                    CharacterMainControl retryPlayer = CharacterMainControl.Main;
                    Vector3 retryPlayerPos = retryPlayer != null ? retryPlayer.transform.position : Vector3.zero;
                    position = FindNearestSafeSpawnPoint(spawnPoints, retryPlayerPos);
                }
                
                spawnedBoss = await SpawnEnemyAtPositionAsync(preset, position);
                if (spawnedBoss != null)
                {
                    break;
                }
                
                if (attempt < maxRetries)
                {
                    // 等待一小段时间后重试
                    await UniTask.Delay(200);
                }
            }
            
            if (spawnedBoss == null)
            {
                DevLog("[BossRush] [ERROR] 单Boss生成失败，尝试次数: " + attempt + ", preset=" + preset.displayName);
                OnBossSpawnFailed(preset);
            }
            else
            {
                DevLog("[BossRush] 单Boss生成成功: " + preset.displayName + " (尝试次数: " + attempt + ")");
            }
        }
        
        /// <summary>
        /// 多Boss模式：带验证和重试的批量生成
        /// 确保每个Boss使用不同的刷怪点，避免位置冲突
        /// </summary>
        private async UniTaskVoid SpawnMultipleBossesWithVerificationAsync(
            List<(EnemyPresetInfo preset, Vector3 position)> bossSpawnInfos, 
            Vector3[] spawnPoints)
        {
            const int maxRetries = 3;
            int expectedCount = bossSpawnInfos.Count;
            
            DevLog("[BossRush] 开始批量生成 " + expectedCount + " 个Boss");
            
            // 重新分配刷怪点，确保每个Boss使用不同的安全位置
            CharacterMainControl multiPlayer = CharacterMainControl.Main;
            Vector3 multiPlayerPos = multiPlayer != null ? multiPlayer.transform.position : Vector3.zero;
            var assignedPositions = FindMultipleSafeSpawnPoints(expectedCount, spawnPoints, multiPlayerPos);
            for (int i = 0; i < bossSpawnInfos.Count && i < assignedPositions.Count; i++)
            {
                bossSpawnInfos[i] = (bossSpawnInfos[i].preset, assignedPositions[i]);
            }

            // 第一轮：串行生成所有Boss（避免并行时的潜在冲突）
            var results = new List<CharacterMainControl>();
            var failedInfos = new List<(EnemyPresetInfo preset, int originalIndex)>();
            
            for (int i = 0; i < bossSpawnInfos.Count; i++)
            {
                var info = bossSpawnInfos[i];
                CharacterMainControl spawnResult = null;
                
                try
                {
                    spawnResult = await SpawnEnemyAtPositionAsync(info.preset, info.position);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] Boss生成异常 #" + i + ": " + e.Message);
                }
                
                results.Add(spawnResult);
                
                if (spawnResult == null)
                {
                    failedInfos.Add((info.preset, i));
                }
                
                // 每个Boss生成后短暂等待，确保游戏状态稳定
                if (i < bossSpawnInfos.Count - 1)
                {
                    await UniTask.Delay(50);
                }
            }
            
            // 统计成功生成的数量
            int resolvedCount = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null)
                {
                    resolvedCount++;
                }
            }
            
            DevLog("[BossRush] 首轮生成完成: 已处理=" + resolvedCount + ", 失败=" + failedInfos.Count);
            
            // 重试失败的Boss
            int retryAttempt = 0;
            while (failedInfos.Count > 0 && retryAttempt < maxRetries)
            {
                retryAttempt++;
                DevLog("[BossRush] 开始重试失败的Boss (第 " + retryAttempt + " 轮), 剩余: " + failedInfos.Count);
                
                // 等待一小段时间后重试
                await UniTask.Delay(300);
                
                var stillFailed = new List<(EnemyPresetInfo preset, int originalIndex)>();

                // 为所有失败的Boss一次性分配不同的安全重试位置
                CharacterMainControl retryPlayerRef = CharacterMainControl.Main;
                Vector3 retryPlayerPos = retryPlayerRef != null ? retryPlayerRef.transform.position : Vector3.zero;
                var retryPositions = FindMultipleSafeSpawnPoints(failedInfos.Count, spawnPoints, retryPlayerPos);

                for (int ri = 0; ri < failedInfos.Count; ri++)
                {
                    var failedInfo = failedInfos[ri];
                    Vector3 newPos = ri < retryPositions.Count
                        ? retryPositions[ri]
                        : FindNearestSafeSpawnPoint(spawnPoints, retryPlayerPos);
                    
                    CharacterMainControl retryResult = null;
                    try
                    {
                        retryResult = await SpawnEnemyAtPositionAsync(failedInfo.preset, newPos);
                    }
                    catch (Exception e)
                    {
                        DevLog("[BossRush] Boss重试生成异常: " + e.Message);
                    }
                    
                    if (retryResult != null)
                    {
                        resolvedCount++;
                        DevLog("[BossRush] 重试成功: " + failedInfo.preset.displayName);
                    }
                    else
                    {
                        stillFailed.Add(failedInfo);
                    }
                    
                    // 每次重试后短暂等待
                    await UniTask.Delay(100);
                }
                
                failedInfos = stillFailed;
                DevLog("[BossRush] 重试轮 " + retryAttempt + " 完成: 当前已处理总数=" + resolvedCount + ", 仍失败=" + failedInfos.Count);
            }
            
            // 最终验证
            int finalFailCount = expectedCount - resolvedCount;
            if (finalFailCount > 0)
            {
                DevLog("[BossRush] [WARNING] 最终有 " + finalFailCount + " 个Boss生成失败，修正波次计数");
                int liveBossCount = PruneAndCountTrackedWaveBosses();

                // 修正波次计数，remaining 必须与当前仍存活并被追踪的 Boss 数量一致，避免卡波
                bossesInCurrentWaveTotal = liveBossCount;
                bossesInCurrentWaveRemaining = liveBossCount;
                
                // 如果全部失败，直接推进下一波
                if (liveBossCount <= 0)
                {
                    DevLog("[BossRush] [ERROR] 本波所有Boss生成失败，跳过本波");
                    ProceedAfterWaveFinished();
                }
            }
            else
            {
                DevLog("[BossRush] 批量生成完成: 本波 " + expectedCount + " 个目标Boss已全部处理");
            }
        }

        private int PruneAndCountTrackedWaveBosses()
        {
            if (currentWaveBosses == null || currentWaveBosses.Count == 0)
            {
                return 0;
            }

            int liveBossCount = 0;
            for (int i = currentWaveBosses.Count - 1; i >= 0; i--)
            {
                MonoBehaviour boss = currentWaveBosses[i];
                if (boss == null)
                {
                    currentWaveBosses.RemoveAt(i);
                    continue;
                }

                liveBossCount++;
            }

            return liveBossCount;
        }

        /// <summary>
        /// 获取当前地图的刷新点数组（使用 BossRushMapConfig 配置系统）
        /// </summary>
        private Vector3[] GetCurrentSpawnPoints()
        {
            // 使用配置系统获取当前场景的刷新点
            return GetCurrentSceneSpawnPoints();
        }
        
        public void StartNextWaveCountdown(bool showInitialBanner = true, bool suppressImmediateRepeatBanner = false)
        {
            float interval = GetWaveIntervalSeconds();
            bool milestoneBonusApplied = false;

            // 每5波额外休息时间
            float milestoneBonus = GetMilestoneRestBonusSeconds();
            if (milestoneBonus > 0f)
            {
                // 模式A/B: currentEnemyIndex 已在 ProceedAfterWaveFinished 中自增，代表已完成波数
                // 模式C: infiniteHellWaveIndex 已在 OnInfiniteHellWaveCompleted 中自增，代表已完成波数
                int completedWave = infiniteHellMode ? infiniteHellWaveIndex : currentEnemyIndex;
                if (completedWave > 0 && completedWave % 5 == 0)
                {
                    interval += milestoneBonus;
                    milestoneBonusApplied = true;
                    DevLog("[BossRush] 第 " + completedWave + " 波完成，额外休息 " + milestoneBonus + " 秒");
                }
            }

            if (!infiniteHellMode)
            {
                try
                {
                    nextWaveBossName = null;
                    // 使用过滤后的 Boss 列表，确保预告的 Boss 与实际生成的一致
                    var filteredPresets = GetFilteredEnemyPresets();
                    int presetCount = (filteredPresets != null) ? filteredPresets.Count : 0;
                    if (currentEnemyIndex >= 0 && currentEnemyIndex < presetCount)
                    {
                        EnemyPresetInfo nextPreset = filteredPresets[currentEnemyIndex];
                        if (nextPreset != null)
                        {
                            nextWaveBossName = nextPreset.displayName;
                        }
                    }
                }
                catch
                {
                    nextWaveBossName = null;
                }
            }
            else
            {
                nextWaveBossName = null;
            }
            if (interval <= 0f)
            {
                waitingForNextWave = false;
                lastWaveCountdownSeconds = -1;
                SpawnNextEnemy();
                return;
            }

            // 重置上一轮倒计时状态
            waitingForNextWave = true;
            waveCountdown = interval;
            int secondsInt = Mathf.RoundToInt(interval);
            if (secondsInt < 1)
            {
                secondsInt = 1;
            }

            if (showInitialBanner && (interval <= 5f || milestoneBonusApplied))
            {
                ShowNextWaveCountdownBanner(secondsInt);
                lastWaveCountdownSeconds = secondsInt;
            }
            else if (suppressImmediateRepeatBanner)
            {
                lastWaveCountdownSeconds = secondsInt;
            }
            else
            {
                lastWaveCountdownSeconds = -1;
            }
        }

        private void ShowNextWaveCountdownBanner(int secondsInt)
        {
            if (secondsInt < 1)
            {
                secondsInt = 1;
            }

            if (!infiniteHellMode && !string.IsNullOrEmpty(nextWaveBossName))
            {
                ShowBigBanner(L10n.T(
                    "<color=red>" + nextWaveBossName + "</color> 将在 <color=yellow>" + secondsInt + "</color> 秒后抵达战场...",
                    "<color=red>" + nextWaveBossName + "</color> arriving in <color=yellow>" + secondsInt + "</color> seconds..."
                ));
            }
            else
            {
                ShowBigBanner(L10n.T(
                    "下一波将在 <color=yellow>" + secondsInt + "</color> 秒后开始...",
                    "Next wave in <color=yellow>" + secondsInt + "</color> seconds..."
                ));
            }
        }

        /// <summary>
        /// 敌人死亡事件处理（带DamageInfo参数）
        /// <para>仅用于普通模式（弹指可灭/有点意思/无间炼狱），Mode D 有独立的死亡处理逻辑</para>
        /// </summary>
        private void OnEnemyDiedWithDamageInfo(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                // Mode D 有独立的敌人死亡处理（RegisterModeDEnemyDeath），不走普通模式逻辑
                // 避免 Mode D 打死敌人时误触发普通模式的通关判定
                if (modeDActive)
                {
                    return;
                }
                
                if (!IsActive || deadHealth == null)
                {
                    return;
                }

                CharacterMainControl deadCharacter = null;
                try
                {
                    deadCharacter = deadHealth.TryGetCharacter();
                }
                catch {}

                // 多Boss模式：检查是否是当前波的其中一名Boss
                if (bossesPerWave > 1 && currentWaveBosses != null && currentWaveBosses.Count > 0)
                {
                    MonoBehaviour matchedBoss = null;
                    for (int i = 0; i < currentWaveBosses.Count; i++)
                    {
                        MonoBehaviour boss = currentWaveBosses[i];
                        if (boss == null) continue;

                        bool isDeadBoss = false;

                        try
                        {
                            CharacterMainControl bossCharacter = boss as CharacterMainControl;
                            if (bossCharacter != null && deadCharacter != null)
                            {
                                isDeadBoss = (bossCharacter == deadCharacter);
                            }
                        }
                        catch {}

                        if (!isDeadBoss)
                        {
                            try
                            {
                                Health bossHealth = boss.GetComponent<Health>();
                                if (bossHealth == deadHealth || boss.gameObject == deadHealth.gameObject)
                                {
                                    isDeadBoss = true;
                                }
                            }
                            catch {}
                        }

                        if (isDeadBoss)
                        {
                            matchedBoss = boss;
                            break;
                        }
                    }

                    if (matchedBoss != null)
                    {
                        DevLog("[BossRush] 当前波有一名Boss被击败");

                        // 处理Boss掉落随机化
                        CharacterMainControl bossMainControl = matchedBoss as CharacterMainControl;
                        if (bossMainControl != null)
                        {
                            HandleBossDeath(bossMainControl, damageInfo);
                        }
                    }
                }
                else
                {
                    // 单Boss模式：保持原有逻辑
                    bool isCurrentBossDead = false;

                    if (currentBoss != null)
                    {
                        try
                        {
                            CharacterMainControl currentBossCharacter = currentBoss as CharacterMainControl;
                            if (currentBossCharacter != null && deadCharacter != null)
                            {
                                isCurrentBossDead = (currentBossCharacter == deadCharacter);
                            }
                        }
                        catch {}

                        if (!isCurrentBossDead)
                        {
                            try
                            {
                                isCurrentBossDead = (deadHealth.gameObject == ((MonoBehaviour)currentBoss).gameObject);
                            }
                            catch {}
                        }
                    }

                    if (currentBoss != null && isCurrentBossDead)
                    {
                        DevLog("[BossRush] 当前敌人已击败");
                        CharacterMainControl bossMainControl = currentBoss as CharacterMainControl;
                        if (bossMainControl != null)
                        {
                            HandleBossDeath(bossMainControl, damageInfo);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] OnEnemyDied 错误: " + e.Message);
            }
        }

        private void HandleBossDeath(CharacterMainControl bossMain, DamageInfo damageInfo)
        {
            try
            {
                if (!IsActive || bossMain == null)
                {
                    return;
                }

                if (countedDeadBosses.Contains(bossMain))
                {
                    return;
                }

                countedDeadBosses.Add(bossMain);
                UnregisterEnemyRecovery(bossMain);

                // 识别 Boss 类型并触发成就
                string bossType = IdentifyBossType(bossMain);
                CheckBossKillAchievements(bossType);

                // 无间炼狱：先累加现金池
                if (infiniteHellMode)
                {
                    try
                    {
                        float maxHp = 0f;
                        if (bossMain.Health != null)
                        {
                            maxHp = bossMain.Health.MaxHealth;
                        }
                        if (maxHp < 0f) maxHp = 0f;
                        long reward = (long)Mathf.Round(maxHp * 10f);
                        if (reward < 0L) reward = 0L;
                        infiniteHellCashPool += reward;
                        infiniteHellWaveCashThisWave += reward;
                    }
                    catch {}
                }

                if (bossesPerWave > 1 && currentWaveBosses != null && currentWaveBosses.Count > 0)
                {
                    for (int i = 0; i < currentWaveBosses.Count; i++)
                    {
                        MonoBehaviour boss = currentWaveBosses[i];
                        if (boss == null)
                        {
                            continue;
                        }

                        CharacterMainControl bossCharacter = null;
                        try
                        {
                            bossCharacter = boss as CharacterMainControl;
                        }
                        catch {}

                        if (bossCharacter == bossMain)
                        {
                            currentWaveBosses.RemoveAt(i);
                            break;
                        }
                    }
                }

                defeatedEnemies++;

                if (bossesPerWave > 1)
                {
                    bossesInCurrentWaveRemaining = Mathf.Max(0, bossesInCurrentWaveRemaining - 1);

                    if (bossesInCurrentWaveRemaining <= 0)
                    {
                        ProceedAfterWaveFinished();
                        return;
                    }
                }
                else
                {
                    // 单Boss模式：击杀后直接推进到下一波
                    ProceedAfterWaveFinished();
                    return;
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 当当前波所有Boss被击杀或因生成失败/异常被跳过时，推进到下一波或结束挑战
        /// </summary>
        private void ProceedAfterWaveFinished()
        {
            try
            {
                // 通知快递员 Boss 战结束
                NotifyCourierBossFightEnd();
                
                // 通知快递员当前没有Boss（召唤间隔期间）
                NotifyCourierNoBoss(true);
                
                currentEnemyIndex++;
                currentBoss = null;

                if (infiniteHellMode)
                {
                    // 无间炼狱：统一走专用逻辑
                    OnInfiniteHellWaveCompleted();
                    return;
                }

                // 使用过滤后的 Boss 列表判断是否还有下一波
                var filteredPresets = GetFilteredEnemyPresets();
                int presetCount = (filteredPresets != null) ? filteredPresets.Count : 0;
                if (currentEnemyIndex < presetCount)
                {
                    if (config != null && config.useInteractBetweenWaves)
                    {
                        try
                        {
                            if (bossRushSignInteract != null)
                            {
                                bossRushSignInteract.SetNextWaveMode();
                            }
                        }
                        catch {}
                    }
                    else
                    {
                        StartNextWaveCountdown();
                    }
                }
                else
                {
                    OnAllEnemiesDefeated();
                }
            }
            catch {}
        }

        /// <summary>
        /// Boss 在生成阶段失败时的统一处理：修正当前波计数并在必要时推进波次
        /// </summary>
        private void OnBossSpawnFailed(EnemyPresetInfo preset)
        {
            try
            {
                // 记录日志方便排查
                try
                {
                    string name = (preset != null ? preset.displayName : "<null>");
                    DevLog("[BossRush] OnBossSpawnFailed: Boss 生成失败, preset=" + name);
                }
                catch {}

                // 递增已击败敌人数，保持总数一致
                defeatedEnemies++;

                if (bossesPerWave > 1)
                {
                    // 多Boss模式：减少当前波剩余Boss数量
                    bossesInCurrentWaveRemaining = Mathf.Max(0, bossesInCurrentWaveRemaining - 1);

                    if (bossesInCurrentWaveRemaining <= 0)
                    {
                        ProceedAfterWaveFinished();
                    }
                }
                else
                {
                    // 单Boss模式：视为跳过该敌人，直接进入下一波
                    ProceedAfterWaveFinished();
                }
            }
            catch {}
        }

        /// <summary>
        /// 初始化敌人预设列表 - 动态识别所有显示名字的敌人
        /// [性能优化] 添加初始化标记，避免每次传送都重复扫描
        /// </summary>
        private void InitializeEnemyPresets()
        {
            // [性能优化] 如果已经初始化过，跳过重复扫描
            if (_enemyPresetsInitialized && enemyPresets != null && enemyPresets.Count > 0)
            {
                if (!IsActive && !modeDActive && !modeEActive && !modeFActive)
                {
                    int removed = PruneNonBossEnemyPresetsFromCache();
                    if (removed > 0)
                    {
                        ResetBossPoolFilterStateForEnemyPresetRefresh();
                    }
                }

                DevLog("[BossRush] 敌人预设已初始化，跳过重复扫描 (共 " + enemyPresets.Count + " 个)");
                return;
            }
            
            enemyPresets.Clear();;
            
            // 获取所有可能的敌人类型
            var enemyTypes = new List<EnemyPresetInfo>();
            
            // 仅通过游戏内的角色预设动态发现敌人类型
            TryDiscoverAdditionalEnemies(enemyTypes);
            
            // 按团队类型和基础生命值排序，使用排除法过滤（排除玩家和中立阵营）
            // 这样可以兼容其他mod添加的自定义敌对阵营
            enemyPresets = enemyTypes
                .Where(e =>
                    e.team != (int)Teams.player    // 排除玩家阵营
                    && e.team != (int)Teams.middle // 排除中立阵营
                    && e.baseHealth > 100f)
                .OrderBy(e => e.team)
                .ThenBy(e => e.baseHealth)
                .ToList();
            
            // 注册龙裔遗族Boss
            RegisterDragonDescendantPreset();
            
            // 注册龙王Boss
            RegisterDragonKingPreset();

            // 注册幽灵女巫Boss
            RegisterPhantomWitchPreset();

            PruneNonBossEnemyPresetsFromCache();

            // 计算 Boss 池基础血量范围
            try
            {
                if (enemyPresets != null && enemyPresets.Count > 0)
                {
                    float minH = float.MaxValue;
                    float maxH = 0f;
                    for (int i = 0; i < enemyPresets.Count; i++)
                    {
                        float h = enemyPresets[i].baseHealth;
                        if (h <= 0f)
                        {
                            continue;
                        }
                        if (h < minH)
                        {
                            minH = h;
                        }
                        if (h > maxH)
                        {
                            maxH = h;
                        }
                    }

                    if (minH < float.MaxValue && maxH > 0f && maxH >= minH)
                    {
                        minBossBaseHealth = minH;
                        maxBossBaseHealth = maxH;
                        DevLog("[BossRush] Boss池基础血量范围: " + minBossBaseHealth + " ~ " + maxBossBaseHealth);
                    }
                }
            }
            catch {}

            DevLog("[BossRush] 初始化完成，共发现 " + enemyPresets.Count + " 个敌人类型");
            
            // [性能优化] 标记初始化完成，后续传送不再重复扫描
            _enemyPresetsInitialized = true;
        }

        /// <summary>
        /// 无间炼狱模式下按权重随机选取一个敌人预设
        /// 权重根据基础血量与波次线性放大，高血量Boss在后期权重更高
        /// 同时应用用户设置的无间炼狱因子作为权重乘数
        /// </summary>
        private EnemyPresetInfo PickRandomEnemyForInfiniteHell()
        {
            // 使用过滤后的 Boss 列表
            var filteredPresets = GetFilteredEnemyPresets();
            if (filteredPresets == null || filteredPresets.Count == 0)
            {
                return null;
            }

            float refMin = minBossBaseHealth;
            float refMax = maxBossBaseHealth;

            // 如果没有有效范围，退化为按因子权重随机
            if (!(refMax > refMin && refMin > 0f))
            {
                // 即使没有血量范围，也应用用户设置的因子
                float totalFactorWeight = 0f;
                float[] factorWeights = new float[filteredPresets.Count];
                for (int i = 0; i < filteredPresets.Count; i++)
                {
                    float factor = GetBossInfiniteHellFactor(filteredPresets[i].name);
                    factorWeights[i] = factor;
                    totalFactorWeight += factor;
                }
                
                if (totalFactorWeight <= 0f)
                {
                    int idx = UnityEngine.Random.Range(0, filteredPresets.Count);
                    return filteredPresets[idx];
                }
                
                float rFactor = UnityEngine.Random.value * totalFactorWeight;
                float accFactor = 0f;
                for (int i = 0; i < filteredPresets.Count; i++)
                {
                    accFactor += factorWeights[i];
                    if (rFactor <= accFactor)
                    {
                        return filteredPresets[i];
                    }
                }
                return filteredPresets[filteredPresets.Count - 1];
            }

            // 计算每个Boss的权重
            float totalWeight = 0f;
            float[] weights = new float[filteredPresets.Count];
            // 基础系数：t * baseK + (wave/50)*t，t 为基础血量归一化
            const float baseK = 4f;
            float waveTerm = (float)infiniteHellWaveIndex / 50f;

            for (int i = 0; i < filteredPresets.Count; i++)
            {
                float h = filteredPresets[i].baseHealth;
                if (h <= 0f)
                {
                    h = refMin;
                }

                float t = Mathf.Clamp01((h - refMin) / (refMax - refMin));
                float w = 1f + t * baseK + waveTerm * t;
                if (w < 0.01f)
                {
                    w = 0.01f;
                }

                // 应用用户设置的无间炼狱因子作为权重乘数
                float userFactor = GetBossInfiniteHellFactor(filteredPresets[i].name);
                w *= userFactor;

                weights[i] = w;
                totalWeight += w;
            }

            if (totalWeight <= 0f)
            {
                int idx = UnityEngine.Random.Range(0, filteredPresets.Count);
                return filteredPresets[idx];
            }

            // 按累计权重抽样
            float r = UnityEngine.Random.value * totalWeight;
            float acc = 0f;
            for (int i = 0; i < filteredPresets.Count; i++)
            {
                acc += weights[i];
                if (r <= acc)
                {
                    return filteredPresets[i];
                }
            }

            // 理论上不会到这里，兜底返回最后一个
            return filteredPresets[filteredPresets.Count - 1];
        }


        private static bool IsRuntimeCharacterPresetClone(CharacterRandomPreset preset)
        {
            if (preset == null)
            {
                return false;
            }

            string runtimeName = null;
            try { runtimeName = preset.name; } catch { }

            return !string.IsNullOrEmpty(runtimeName) &&
                   runtimeName.IndexOf("(Clone)", StringComparison.Ordinal) >= 0;
        }

        private static bool IsBossPoolSpecialNoShowNamePreset(string nameKey)
        {
            return string.Equals(nameKey, "Cname_Boss_Red", StringComparison.Ordinal) ||
                   string.Equals(nameKey, "Cname_Boss_Blue", StringComparison.Ordinal);
        }

        private int PruneNonBossEnemyPresetsFromCache()
        {
            if (enemyPresets == null || enemyPresets.Count == 0)
            {
                return 0;
            }

            try
            {
                var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                if (allPresets == null || allPresets.Length == 0)
                {
                    return 0;
                }

                var showNameByKey = new Dictionary<string, bool>(StringComparer.Ordinal);
                for (int i = 0; i < allPresets.Length; i++)
                {
                    CharacterRandomPreset preset = allPresets[i];
                    if (preset == null || IsRuntimeCharacterPresetClone(preset))
                    {
                        continue;
                    }

                    string nameKey = preset.nameKey;
                    if (string.IsNullOrEmpty(nameKey))
                    {
                        continue;
                    }

                    bool existingShowName = false;
                    if (showNameByKey.TryGetValue(nameKey, out existingShowName))
                    {
                        showNameByKey[nameKey] = existingShowName || preset.showName;
                    }
                    else
                    {
                        showNameByKey[nameKey] = preset.showName;
                    }
                }

                int removed = 0;
                for (int i = enemyPresets.Count - 1; i >= 0; i--)
                {
                    EnemyPresetInfo preset = enemyPresets[i];
                    if (preset == null || string.IsNullOrEmpty(preset.name))
                    {
                        continue;
                    }

                    bool canonicalShowName = false;
                    if (!showNameByKey.TryGetValue(preset.name, out canonicalShowName) || canonicalShowName)
                    {
                        continue;
                    }

                    if (IsManagedBossPreset(preset))
                    {
                        continue;
                    }

                    if (IsBossPoolSpecialNoShowNamePreset(preset.name))
                    {
                        continue;
                    }

                    enemyPresets.RemoveAt(i);
                    removed++;
                }

                if (removed > 0)
                {
                    DevLog("[BossRush] 已从 Boss 池缓存中移除 " + removed + " 个被运行时 showName 克隆误判的小怪预设");
                }

                return removed;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 清理 Boss 池缓存中的误判小怪失败: " + e.Message);
                return 0;
            }
        }
        
        /// <summary>
        /// 尝试发现额外的敌人类型
        /// </summary>
        private void TryDiscoverAdditionalEnemies(List<EnemyPresetInfo> enemyList)
        {
            try
            {
                var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                if (allPresets != null && allPresets.Length > 0)
                {
                    foreach (var preset in allPresets)
                    {
                        if (preset == null)
                        {
                            continue;
                        }

                        if (IsRuntimeCharacterPresetClone(preset))
                        {
                            continue;
                        }

                        string nameKey = preset.nameKey;
                        if (string.IsNullOrEmpty(nameKey))
                        {
                            continue;
                        }

                        string displayName = GetLocalizedCharacterName(nameKey);
                        bool isSpecialUnknownBoss = IsBossPoolSpecialNoShowNamePreset(nameKey);

                        if (!preset.showName && !isSpecialUnknownBoss)
                        {
                            continue;
                        }

                        if (enemyList.Any(e => e.name == nameKey))
                        {
                            continue;
                        }

                        int team = (int)preset.team;
                        float health = (preset.health > 0f) ? preset.health : 100f;
                        float damage = preset.damageMultiplier;

                        var newEnemy = new EnemyPresetInfo
                        {
                            name = nameKey,
                            displayName = displayName,
                            team = team,
                            baseHealth = health,
                            baseDamage = damage
                        };

                        enemyList.Add(newEnemy);
                        DevLog("[BossRush] 发现额外敌人类型: " + nameKey + " (team=" + team + ", health=" + health + ")");
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 动态发现敌人时出现异常: " + e.Message);
            }
        }

        private string GetLocalizedCharacterName(string nameKey)
        {
            if (string.IsNullOrEmpty(nameKey))
            {
                return nameKey;
            }

            try
            {
                string[] types = new string[]
                {
                    "SodaCraft.Localizations.LocalizationManager, SodaLocalization",
                    "SodaCraft.Localizations.LocalizationManager, TeamSoda.Duckov.Core",
                    "LocalizationManager, Assembly-CSharp"
                };

                Type locType = null;
                for (int i = 0; i < types.Length; i++)
                {
                    locType = Type.GetType(types[i]);
                    if (locType != null)
                    {
                        break;
                    }
                }

                if (locType != null)
                {
                    var method = locType.GetMethod("ToPlainText", BindingFlags.Static | BindingFlags.Public);
                    if (method != null)
                    {
                        object result = method.Invoke(null, new object[] { nameKey });
                        string str = result as string;
                        if (!string.IsNullOrEmpty(str))
                        {
                            return str;
                        }
                    }
                }
            }
            catch
            {
            }

            return nameKey;
        }

    }
}
