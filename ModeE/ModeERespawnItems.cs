// ============================================================================
// ModeERespawnItems.cs - 刷怪消耗品使用效果与自动发放
// ============================================================================
// 模块说明：
//   实现挑衅烟雾弹（Taunt Smoke）和混沌引爆器（Chaos Detonator）的使用效果逻辑。
//   挑衅烟雾弹：在最近10个刷怪点重新生成随机阵营Boss
//   混沌引爆器：在全图所有刷怪点重新生成随机阵营Boss
//   自动发放：每击杀10个Boss自动发放一个挑衅烟雾弹
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// Mode E 刷怪消耗品使用效果模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 刷怪消耗品字段

        /// <summary>Mode E 中累计 Boss 击杀数，每10次触发自动发放挑衅烟雾弹</summary>
        private int modeERespawnKillCounter = 0;

        /// <summary>5个有效NPC阵营，用于重生Boss时随机选择</summary>
        private static readonly Teams[] RespawnFactions = new Teams[]
        {
            Teams.scav,
            Teams.usec,
            Teams.bear,
            Teams.lab,
            Teams.wolf
        };

        private const int MODE_E_AGGRO_BATCH_SIZE = 10;

        private const int MODE_E_AGGRO_BATCH_INTERVAL_MS = 1000;

        private const float MODE_E_BOSSCALL_RANGE = 50f;

        private const float MODE_E_BOSSCALL_TRACE_DISTANCE = 80f;

        private const float MODE_E_ALL_KINGS_TRACE_DISTANCE = 9999f;

        private const float MODE_E_FORCED_AGGRO_MIN_FORGET_TIME = 30f;

        private const float MODE_E_FORCED_AGGRO_REPATH_MIN_DISTANCE = 2f;

        private const bool MODE_E_LOG_PER_ENEMY_WAKE = false;

        private readonly Queue<CharacterMainControl> modeEPendingAggroQueue = new Queue<CharacterMainControl>();

        private readonly Dictionary<CharacterMainControl, float> modeEPendingAggroTraceDistance
            = new Dictionary<CharacterMainControl, float>();

        private bool modeEAggroQueueRunning = false;

        private int modeEAggroQueueEpoch = 0;

        private const int MODE_E_SMOKE_GRENADE_TYPE_ID = 660;

        private UnityEngine.Object modeECachedSmokeVfxPrefab = null;

        #endregion

        #region 刷怪点收集方法

        /// <summary>
        /// 获取距离玩家最近的N个刷怪点
        /// 从 modeESpawnAllocation 合并所有阵营刷怪点，按距离玩家排序，取前 count 个
        /// 不足 count 个时返回全部
        /// </summary>
        private List<Vector3> GetNearestSpawnPoints(int count)
        {
            Vector3[] cachedPoints = GetModeEFlattenedSpawnPoints();
            List<Vector3> allPoints = cachedPoints.Length > 0
                ? new List<Vector3>(cachedPoints)
                : new List<Vector3>();
            if (allPoints.Count == 0) return allPoints;

            // 获取玩家位置
            Vector3 playerPos = Vector3.zero;
            CharacterMainControl playerRef = CharacterMainControl.Main;
            if (playerRef != null) playerPos = playerRef.transform.position;

            // 按距离玩家由近到远排序
            allPoints.Sort((a, b) =>
            {
                float distA = Vector3.SqrMagnitude(a - playerPos);
                float distB = Vector3.SqrMagnitude(b - playerPos);
                return distA.CompareTo(distB);
            });

            // 不足 count 个时返回全部
            if (allPoints.Count <= count) return allPoints;

            // 取前 count 个
            return allPoints.GetRange(0, count);
        }

        /// <summary>
        /// 获取全图所有刷怪点
        /// 从 modeESpawnAllocation 合并所有阵营刷怪点
        /// </summary>
        private List<Vector3> GetAllSpawnPoints()
        {
            Vector3[] cachedPoints = GetModeEFlattenedSpawnPoints();
            return cachedPoints.Length > 0
                ? new List<Vector3>(cachedPoints)
                : new List<Vector3>();
        }

        #endregion

        #region 烟雾VFX

        /// <summary>
        /// 在玩家位置触发原版烟雾弹效果
        /// 从原版烟雾弹(TypeID=660)的 Skill_Grenade 获取 Grenade prefab，
        /// 再从 Grenade.createOnExlode 获取 FowSmoke prefab 并实例化
        /// </summary>
        private UnityEngine.Object GetModeESmokeVfxPrefab()
        {
            if (modeECachedSmokeVfxPrefab != null)
            {
                return modeECachedSmokeVfxPrefab;
            }

            Item smokeItem = null;
            try
            {
                smokeItem = ItemAssetsCollection.InstantiateSync(MODE_E_SMOKE_GRENADE_TYPE_ID);
                if (smokeItem == null)
                {
                    DevLog("[ModeE] [WARNING] 无法实例化原版烟雾弹(" + MODE_E_SMOKE_GRENADE_TYPE_ID + ")，跳过VFX");
                    return null;
                }

                Skill_Grenade skillGrenade = smokeItem.GetComponentInChildren<Skill_Grenade>();
                if (skillGrenade == null)
                {
                    DevLog("[ModeE] [WARNING] 原版烟雾弹(" + MODE_E_SMOKE_GRENADE_TYPE_ID + ")无 Skill_Grenade 组件");
                    return null;
                }

                Grenade grenadePfb = skillGrenade.grenadePfb;
                if (grenadePfb == null)
                {
                    DevLog("[ModeE] [WARNING] 原版烟雾弹(" + MODE_E_SMOKE_GRENADE_TYPE_ID + ") Skill_Grenade.grenadePfb 为 null");
                    return null;
                }

                if (grenadePfb.createOnExlode == null)
                {
                    DevLog("[ModeE] [WARNING] Grenade.createOnExlode 为 null，无烟雾效果");
                    return null;
                }

                modeECachedSmokeVfxPrefab = grenadePfb.createOnExlode;
                return modeECachedSmokeVfxPrefab;
            }
            finally
            {
                try
                {
                    if (smokeItem != null && smokeItem.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(smokeItem.gameObject);
                    }
                }
                catch { }
            }
        }

        private void PlaySmokeVFX()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;

                UnityEngine.Object smokeVfxPrefab = GetModeESmokeVfxPrefab();
                if (smokeVfxPrefab == null)
                {
                    return;
                }

                Vector3 playerPos = player.transform.position;
                UnityEngine.Object.Instantiate(smokeVfxPrefab, playerPos, Quaternion.identity);
                DevLog("[ModeE] 已在玩家位置实例化原版烟雾效果(FowSmoke)");
            }
            catch (Exception e)
            {
                // VFX 播放失败不影响核心逻辑
                DevLog("[ModeE] [WARNING] PlaySmokeVFX 失败: " + e.Message);
            }
        }

        #endregion

        #region Boss 重生逻辑

        /// <summary>
        /// 在指定刷怪点列表生成随机阵营Boss（异步，每次生成间加延迟避免帧率卡顿）
        /// 遍历刷怪点，随机选择阵营，调用 SpawnSingleModeEBoss
        /// 新 Boss 自动注册到 modeEAliveEnemies 和 modeEFactionAliveMap（由 OnModeEEnemySpawned 处理）
        /// </summary>
        private async UniTaskVoid RespawnBossesAtPoints(List<Vector3> points)
        {
            try
            {
                if (points == null || points.Count == 0)
                {
                    DevLog("[ModeE] RespawnBossesAtPoints: 刷怪点列表为空，跳过");
                    return;
                }

                DevLog("[ModeE] 开始重生Boss，刷怪点数量: " + points.Count);
                int modeESessionToken = CurrentModeESessionToken;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                for (int i = 0; i < points.Count; i++)
                {
                    if (!IsModeESessionStillValid(modeESessionToken, relatedScene)) break; // 模式已结束或切到新局，停止生成

                    // 随机选择阵营
                    Teams faction = RespawnFactions[UnityEngine.Random.Range(0, RespawnFactions.Length)];

                    // 复用现有 SpawnSingleModeEBoss 逻辑生成Boss
                    // SpawnSingleModeEBoss 内部会处理：
                    //   - Boss预设匹配
                    //   - 注册到 modeEAliveEnemies 和 modeEFactionAliveMap
                    //   - 注册死亡事件（OnModeEEnemyDeath）
                    //   - 应用当前阵营死亡缩放倍率
                    SpawnSingleModeEBoss(
                        faction,
                        points[i],
                        modeESessionToken: modeESessionToken,
                        modeESessionRelatedScene: relatedScene);

                    // 每次生成间加延迟，避免帧率卡顿（与 ModeESpawnAllBosses 保持一致）
                    if (i + 1 < points.Count)
                    {
                        await UniTask.Delay(250);
                    }
                }

                DevLog("[ModeE] Boss 重生任务完成");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] RespawnBossesAtPoints 失败: " + e.Message);
            }
        }

        #endregion

        #region 物品使用效果

        /// <summary>
        /// 挑衅烟雾弹使用效果：在最近10个刷怪点重新生成随机阵营Boss
        /// 由 RespawnItemUsage.OnUse 调用（Mode E 激活检查已在 CanBeUsed 中完成）
        /// </summary>
        public void UseTauntSmoke()
        {
            try
            {
                // 检查刷怪点分配数据
                if (modeESpawnAllocation == null)
                {
                    ShowMessage(L10n.T(
                        "刷怪点数据异常，无法使用！",
                        "Spawn point data error, cannot use!"
                    ));
                    DevLog("[ModeE] UseTauntSmoke: modeESpawnAllocation 为 null");
                    return;
                }

                // 获取最近10个刷怪点
                List<Vector3> nearestPoints = GetNearestSpawnPoints(10);
                if (nearestPoints.Count == 0)
                {
                    ShowMessage(L10n.T(
                        "无可用刷怪点！",
                        "No available spawn points!"
                    ));
                    DevLog("[ModeE] UseTauntSmoke: 无可用刷怪点");
                    return;
                }

                DevLog("[ModeE] 使用挑衅烟雾弹，将在 " + nearestPoints.Count + " 个最近刷怪点重生Boss");

                // 播放烟雾VFX
                PlaySmokeVFX();

                // 在刷怪点重生Boss（fire-and-forget）
                RespawnBossesAtPoints(nearestPoints).Forget();

                ShowBigBanner(L10n.T(
                    "<color=yellow>挑衅烟雾弹</color> 已激活！<color=red>" + nearestPoints.Count + "</color> 个Boss正在赶来...",
                    "<color=yellow>Taunt Smoke</color> activated! <color=red>" + nearestPoints.Count + "</color> Bosses incoming..."
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] UseTauntSmoke 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 混沌引爆器使用效果：在全图所有刷怪点重新生成随机阵营Boss
        /// 由 RespawnItemUsage.OnUse 调用（Mode E 激活检查已在 CanBeUsed 中完成）
        /// </summary>
        public void UseChaosDetonator()
        {
            try
            {
                // 检查刷怪点分配数据
                if (modeESpawnAllocation == null)
                {
                    ShowMessage(L10n.T(
                        "刷怪点数据异常，无法使用！",
                        "Spawn point data error, cannot use!"
                    ));
                    DevLog("[ModeE] UseChaosDetonator: modeESpawnAllocation 为 null");
                    return;
                }

                // 获取全图所有刷怪点
                List<Vector3> allPoints = GetAllSpawnPoints();
                if (allPoints.Count == 0)
                {
                    ShowMessage(L10n.T(
                        "无可用刷怪点！",
                        "No available spawn points!"
                    ));
                    DevLog("[ModeE] UseChaosDetonator: 无可用刷怪点");
                    return;
                }

                DevLog("[ModeE] 使用混沌引爆器，将在全图 " + allPoints.Count + " 个刷怪点重生Boss");

                // 播放烟雾VFX
                PlaySmokeVFX();

                // 在全图刷怪点重生Boss（fire-and-forget）
                RespawnBossesAtPoints(allPoints).Forget();

                ShowBigBanner(L10n.T(
                    "<color=red>混沌引爆器</color> 已引爆！全图 <color=red>" + allPoints.Count + "</color> 个Boss正在涌来...",
                    "<color=red>Chaos Detonator</color> activated! <color=red>" + allPoints.Count + "</color> Bosses spawning across the map..."
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] UseChaosDetonator 失败: " + e.Message);
            }
        }

        private bool IsValidModeEEnemyTarget(CharacterMainControl enemy)
        {
            if (enemy == null) return false;
            if (enemy.gameObject == null) return false;
            if (enemy.Health == null) return false;
            if (enemy.Health.IsDead) return false;
            if (enemy.Health.CurrentHealth <= 0f) return false;

            return true;
        }

        private List<CharacterMainControl> GetEnemyBossesInRange(float radius)
        {
            List<CharacterMainControl> enemies = new List<CharacterMainControl>(modeEAliveEnemies.Count);
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return enemies;

            float radiusSqr = radius * radius;
            Vector3 playerPos = player.transform.position;

            for (int i = 0; i < modeEAliveEnemies.Count; i++)
            {
                CharacterMainControl enemy = modeEAliveEnemies[i];
                if (!IsValidModeEEnemyTarget(enemy)) continue;
                if (enemy.Team == modeEPlayerFaction) continue;

                Vector3 offset = enemy.transform.position - playerPos;
                if (offset.sqrMagnitude <= radiusSqr)
                {
                    enemies.Add(enemy);
                }
            }

            return enemies;
        }

        private List<CharacterMainControl> GetAllEnemyBosses()
        {
            List<CharacterMainControl> enemies = new List<CharacterMainControl>(modeEAliveEnemies.Count);

            for (int i = 0; i < modeEAliveEnemies.Count; i++)
            {
                CharacterMainControl enemy = modeEAliveEnemies[i];
                if (!IsValidModeEEnemyTarget(enemy)) continue;
                if (enemy.Team == modeEPlayerFaction) continue;

                enemies.Add(enemy);
            }

            return enemies;
        }

        private bool TryForceActivateModeEEnemy(CharacterMainControl enemy, out bool wokeInactiveEnemy)
        {
            wokeInactiveEnemy = false;
            if (!IsValidModeEEnemyTarget(enemy))
            {
                return false;
            }

            try
            {
                GameObject enemyObject = enemy.gameObject;
                if (enemyObject == null)
                {
                    return false;
                }

                bool wasInactive = !enemyObject.activeSelf || !enemyObject.activeInHierarchy;
                wokeInactiveEnemy = wasInactive;

                try
                {
                    Duckov.Utilities.SetActiveByPlayerDistance.Unregister(enemyObject, enemyObject.scene.buildIndex);
                }
                catch { }

                try
                {
                    enemy.SetSleeping(false);
                }
                catch { }

                if (!enemyObject.activeSelf)
                {
                    enemyObject.SetActive(true);
                }

                AICharacterController ai = enemy.GetComponentInChildren<AICharacterController>(true);
                if (ai != null && !ai.enabled)
                {
                    ai.enabled = true;
                }

                if (MODE_E_LOG_PER_ENEMY_WAKE && wasInactive)
                {
                    DevLog("[ModeE] 已强制激活远距离休眠Boss: " + enemyObject.name);
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] TryForceActivateModeEEnemy 失败: " + e.Message);
                return false;
            }
        }

        private void ClearPendingBossAggroQueue()
        {
            modeEAggroQueueEpoch++;
            modeEPendingAggroQueue.Clear();
            modeEPendingAggroTraceDistance.Clear();
            modeEAggroQueueRunning = false;
        }

        private int QueueBossAggroToPlayer(List<CharacterMainControl> enemies, float traceDistance)
        {
            if (enemies == null || enemies.Count == 0)
            {
                return 0;
            }

            int queuedCount = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                CharacterMainControl enemy = enemies[i];
                if (!IsValidModeEEnemyTarget(enemy))
                {
                    continue;
                }

                float existingTraceDistance;
                if (modeEPendingAggroTraceDistance.TryGetValue(enemy, out existingTraceDistance))
                {
                    if (traceDistance > existingTraceDistance)
                    {
                        modeEPendingAggroTraceDistance[enemy] = traceDistance;
                    }
                    continue;
                }

                modeEPendingAggroTraceDistance[enemy] = traceDistance;
                modeEPendingAggroQueue.Enqueue(enemy);
                queuedCount++;
            }

            if (!modeEAggroQueueRunning && modeEPendingAggroQueue.Count > 0)
            {
                ProcessPendingBossAggroQueue().Forget();
            }

            return queuedCount;
        }

        private async UniTaskVoid ProcessPendingBossAggroQueue()
        {
            if (modeEAggroQueueRunning)
            {
                return;
            }

            int queueEpoch = modeEAggroQueueEpoch;
            int batchIndex = 0;
            modeEAggroQueueRunning = true;
            try
            {
                while (modeEActive && modeEAggroQueueEpoch == queueEpoch && modeEPendingAggroQueue.Count > 0)
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player == null || player.mainDamageReceiver == null)
                    {
                        break;
                    }

                    batchIndex++;
                    int processedThisBatch = 0;
                    int wokeInactiveThisBatch = 0;
                    int failedThisBatch = 0;
                    while (processedThisBatch < MODE_E_AGGRO_BATCH_SIZE && modeEPendingAggroQueue.Count > 0)
                    {
                        CharacterMainControl enemy = modeEPendingAggroQueue.Dequeue();

                        float traceDistance;
                        if (!modeEPendingAggroTraceDistance.TryGetValue(enemy, out traceDistance))
                        {
                            continue;
                        }

                        modeEPendingAggroTraceDistance.Remove(enemy);
                        bool wokeInactiveEnemy;
                        if (ForceBossAggroToPlayer(enemy, player, traceDistance, out wokeInactiveEnemy))
                        {
                            if (wokeInactiveEnemy)
                            {
                                wokeInactiveThisBatch++;
                            }
                        }
                        else
                        {
                            failedThisBatch++;
                        }

                        processedThisBatch++;
                    }

                    LogModeEAggroBatch(batchIndex, processedThisBatch, wokeInactiveThisBatch, failedThisBatch, modeEPendingAggroQueue.Count);

                    if (modeEPendingAggroQueue.Count > 0)
                    {
                        await UniTask.Delay(MODE_E_AGGRO_BATCH_INTERVAL_MS);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ProcessPendingBossAggroQueue failed: " + e.Message);
            }
            finally
            {
                if (modeEAggroQueueEpoch == queueEpoch)
                {
                    modeEAggroQueueRunning = false;

                    if (!modeEActive)
                    {
                        ClearPendingBossAggroQueue();
                    }
                }
            }
        }

        private bool ForceBossAggroToPlayer(CharacterMainControl enemy, CharacterMainControl player, float traceDistance, out bool wokeInactiveEnemy)
        {
            wokeInactiveEnemy = false;
            if (!IsValidModeEEnemyTarget(enemy) || player == null || player.mainDamageReceiver == null)
            {
                return false;
            }

            try
            {
                if (!TryForceActivateModeEEnemy(enemy, out wokeInactiveEnemy))
                {
                    return false;
                }

                AICharacterController ai = enemy.GetComponentInChildren<AICharacterController>(true);
                if (ai == null)
                {
                    return false;
                }

                // 直接设置 searchedEnemy,确保立即生效
                ai.searchedEnemy = player.mainDamageReceiver;

                // 设置 forceTracePlayerDistance 确保持续追踪
                ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, traceDistance);
                ai.traceTargetChance = 1f;
                ai.forgetTime = Mathf.Max(ai.forgetTime, MODE_E_FORCED_AGGRO_MIN_FORGET_TIME);

                // 设置目标和通知信息
                ai.SetTarget(player.mainDamageReceiver.transform);
                ai.SetNoticedToTarget(player.mainDamageReceiver);
                ai.noticed = true;
                PrimeBossChaseToPlayer(ai, player);

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ForceBossAggroToPlayer 失败: " + e.Message);
                return false;
            }
        }

        private void LogModeEAggroBatch(int batchIndex, int processedCount, int wokeInactiveCount, int failedCount, int remainingCount)
        {
            if (processedCount <= 0 && failedCount <= 0)
            {
                return;
            }

            DevLog(
                "[ModeE] Boss仇恨批处理 #" + batchIndex +
                ": handled=" + processedCount +
                ", woke=" + wokeInactiveCount +
                ", failed=" + failedCount +
                ", remaining=" + remainingCount
            );
        }

        private void PrimeBossChaseToPlayer(AICharacterController ai, CharacterMainControl player)
        {
            if (ai == null || player == null)
            {
                return;
            }

            CharacterMainControl enemy = ai.CharacterMainControl;
            if (enemy == null)
            {
                return;
            }

            enemy.SetRunInput(true);

            Vector3 playerPosition = player.transform.position;
            Vector3 chaseOffset = playerPosition - enemy.transform.position;
            chaseOffset.y = 0f;
            if (chaseOffset.sqrMagnitude <= MODE_E_FORCED_AGGRO_REPATH_MIN_DISTANCE * MODE_E_FORCED_AGGRO_REPATH_MIN_DISTANCE)
            {
                return;
            }

            ai.MoveToPos(playerPosition);
        }

        public void UseBosscallWhistle()
        {
            try
            {
                List<CharacterMainControl> nearbyEnemies = GetEnemyBossesInRange(MODE_E_BOSSCALL_RANGE);
                if (nearbyEnemies.Count == 0)
                {
                    // 使用横幅提示玩家
                    ShowBigBanner(L10n.T(
                        "<color=yellow>猎王响哨</color>：50米内没有可被引来的敌对Boss！",
                        "<color=yellow>Bosscall Whistle</color>: No enemy Bosses within 50 meters to provoke!"
                    ));
                    return;
                }

                int affected = QueueBossAggroToPlayer(nearbyEnemies, MODE_E_BOSSCALL_TRACE_DISTANCE);
                if (affected <= 0)
                {
                    // 使用横幅提示玩家
                    ShowBigBanner(L10n.T(
                        "<color=yellow>猎王响哨</color>：敌对Boss未能锁定你为目标！",
                        "<color=yellow>Bosscall Whistle</color>: Enemy Bosses failed to lock onto you!"
                    ));
                    return;
                }

                PlaySmokeVFX();
                ShowMessage(L10n.T("Boss 将按每秒10只分批苏醒并追击你。", "Bosses will wake and aggro in batches of 10 per second."));
                ShowBigBanner(L10n.T(
                    "<color=yellow>猎王响哨</color>已吹响！附近 <color=red>" + affected + "</color> 名敌对Boss正朝你袭来！",
                    "<color=yellow>Bosscall Whistle</color> blown! <color=red>" + affected + "</color> nearby enemy Bosses are coming for you!"
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] UseBosscallWhistle 失败: " + e.Message);
            }
        }

        public void UseAllKingsBanner()
        {
            try
            {
                List<CharacterMainControl> allEnemies = GetAllEnemyBosses();
                if (allEnemies.Count == 0)
                {
                    // 使用横幅提示玩家
                    ShowBigBanner(L10n.T(
                        "<color=red>血狩烽火</color>：当前没有可被引来的敌对Boss！",
                        "<color=red>Bloodhunt Beacon</color>: There are no enemy Bosses to provoke right now!"
                    ));
                    return;
                }

                int affected = QueueBossAggroToPlayer(allEnemies, MODE_E_ALL_KINGS_TRACE_DISTANCE);
                if (affected <= 0)
                {
                    // 使用横幅提示玩家
                    ShowBigBanner(L10n.T(
                        "<color=red>血狩烽火</color>：敌对Boss未能锁定你为目标！",
                        "<color=red>Bloodhunt Beacon</color>: Enemy Bosses failed to lock onto you!"
                    ));
                    return;
                }

                PlaySmokeVFX();
                ShowMessage(L10n.T("Boss 将按每秒10只分批苏醒并追击你。", "Bosses will wake and aggro in batches of 10 per second."));
                ShowBigBanner(L10n.T(
                    "<color=red>血狩烽火</color>已点燃！全图 <color=red>" + affected + "</color> 名敌对Boss都将你视作首要猎物！",
                    "<color=red>Bloodhunt Beacon</color> ignited! <color=red>" + affected + "</color> enemy Bosses across the map now hunt you first!"
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] UseAllKingsBanner 失败: " + e.Message);
            }
        }

        #endregion

        #region 自动发放机制

        /// <summary>
        /// Boss击杀计数 + 自动发放检查
        /// 递增 modeERespawnKillCounter，每达到10的倍数时向玩家背包添加一个挑衅烟雾弹
        /// 背包满时在玩家位置掉落物品
        /// 在 OnModeEEnemyDeath 中调用
        /// </summary>
        private void CheckRespawnItemAutoGrant()
        {
            try
            {
                if (!modeEActive) return;

                modeERespawnKillCounter++;
                DevLog("[ModeE] Boss击杀计数: " + modeERespawnKillCounter);

                // 每10次击杀发放一个挑衅烟雾弹
                if (modeERespawnKillCounter % 10 == 0)
                {
                    Item tauntSmoke = ItemAssetsCollection.InstantiateSync(RespawnItemConfig.TAUNT_SMOKE_TYPE_ID);
                    if (tauntSmoke != null)
                    {
                        // 尝试放入玩家背包，失败则在玩家位置掉落
                        bool added = ItemUtilities.SendToPlayerCharacterInventory(tauntSmoke, false);
                        if (!added)
                        {
                            // 背包满，在玩家位置掉落物品
                            CharacterMainControl player = CharacterMainControl.Main;
                            if (player != null)
                            {
                                tauntSmoke.Drop(player, true);
                                DevLog("[ModeE] 背包已满，挑衅烟雾弹掉落在玩家位置");
                            }
                        }

                        DevLog("[ModeE] 自动发放挑衅烟雾弹（累计击杀: " + modeERespawnKillCounter + "）");

                        // 使用横幅提示
                        ShowBigBanner(L10n.T(
                            "击杀 <color=yellow>" + modeERespawnKillCounter + "</color> 个Boss！获得 <color=yellow>挑衅烟雾弹</color> ×1",
                            "Killed <color=yellow>" + modeERespawnKillCounter + "</color> Bosses! Received <color=yellow>Taunt Smoke</color> ×1"
                        ));
                    }
                    else
                    {
                        DevLog("[ModeE] [WARNING] 自动发放挑衅烟雾弹失败：ItemAssetsCollection.InstantiateSync 返回 null");
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] CheckRespawnItemAutoGrant 失败: " + e.Message);
            }
        }

        #endregion
    }
}
