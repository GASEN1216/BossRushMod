// ============================================================================
// FrostmourneAction.cs - 霜之哀伤右键技能动作
// ============================================================================
// 模块说明：
//   右键技能「亡灵召唤」：在玩家周围 5 个方位召唤 5 只 Cname_Zombie
//   设为玩家同阵营，血量 100，冷却 10 秒
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Common.Equipment;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 霜之哀伤右键技能动作 — 亡灵召唤
    /// </summary>
    public class FrostmourneAction : EquipmentAbilityAction
    {
        private static readonly float[] SummonAngleOffsets = new float[]
        {
            0f,
            -18f,
            18f,
            -36f,
            36f,
            -54f,
            54f,
            90f,
            -90f,
            144f,
            -144f
        };

        private static readonly float[] SummonRadiusScales = new float[]
        {
            1f,
            0.78f,
            1.16f,
            0.58f,
            1.34f
        };

        private static FrostmourneConfig _config;
        private bool spawningStarted;
        private bool spawningComplete;
        private int activeSpawnRequestId;

        // 缓存的僵尸预设
        private static CharacterRandomPreset cachedZombiePreset;
        private static bool presetSearchAttempted;

        // 已召唤的僵尸列表（用于清理）
        private static readonly List<CharacterMainControl> summonedZombies = new List<CharacterMainControl>();

        public static void SetConfig(FrostmourneConfig config)
        {
            _config = config;
        }

        protected override EquipmentAbilityConfig GetConfig()
        {
            if (_config == null) _config = new FrostmourneConfig();
            return _config;
        }

        protected override bool ShouldAutoConsumeStamina()
        {
            return false; // 不持续消耗体力
        }

        protected override bool IsReadyInternal()
        {
            int activeZombieCount = GetActiveSummonedZombieCount();
            if (activeZombieCount >= FrostmourneConfig.SummonCount)
            {
                LogIfVerbose("当前亡灵已达到上限，跳过召唤");
                return false;
            }

            return true;
        }

        protected override bool OnAbilityStart()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
            spawningComplete = false;
            return true;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {
            if (!spawningStarted)
            {
                spawningStarted = true;
                int spawnRequestId = activeSpawnRequestId;
                SpawnZombiesAsync(spawnRequestId).Forget();
            }

            if (spawningComplete || actionElapsedTime >= FrostmourneConfig.TotalActionDuration)
            {
                StopAction();
            }
        }

        protected override void OnAbilityStop()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
            spawningComplete = false;
        }

        private void OnDestroy()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
        }

        /// <summary>
        /// 异步生成 5 只僵尸
        /// </summary>
        private async UniTaskVoid SpawnZombiesAsync(int spawnRequestId)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    LogIfVerbose("玩家角色为空，取消召唤");
                    return;
                }

                // 查找僵尸预设
                CharacterRandomPreset zombiePreset = FindZombiePreset();
                if (zombiePreset == null)
                {
                    LogIfVerbose("未找到 Cname_Zombie 预设，取消召唤");
                    return;
                }

                Teams playerTeam = player.Team;
                Vector3 playerPos = player.transform.position;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                int successfulSummons = 0;

                // 清理之前已死亡的僵尸引用
                CleanupDeadZombies();
                int availableSlots = Mathf.Max(0, FrostmourneConfig.SummonCount - summonedZombies.Count);
                if (availableSlots <= 0)
                {
                    LogIfVerbose("当前亡灵数量已满，本次不再追加召唤");
                    return;
                }

                List<Vector3> reservedSpawnPositions = CollectReservedZombieSpawnPositions();
                int summonStartSlotIndex = summonedZombies.Count;

                // 仅补齐缺额，保证场上同时最多 5 只亡灵
                for (int i = 0; i < availableSlots; i++)
                {
                    if (!IsSpawnRequestStillValid(spawnRequestId, player, relatedScene))
                    {
                        return;
                    }

                    int slotIndex = summonStartSlotIndex + i;
                    List<Vector3> candidateSpawnPositions = BuildCandidateZombieSpawnPositions(
                        playerPos,
                        playerPos.y,
                        slotIndex,
                        reservedSpawnPositions);

                    if (candidateSpawnPositions.Count == 0)
                    {
                        LogIfVerbose("方位 " + slotIndex + " 无可用生成点，跳过");
                        continue;
                    }

                    bool summonSucceeded = false;
                    for (int candidateIndex = 0; candidateIndex < candidateSpawnPositions.Count; candidateIndex++)
                    {
                        if (!IsSpawnRequestStillValid(spawnRequestId, player, relatedScene))
                        {
                            return;
                        }

                        Vector3 spawnPos = candidateSpawnPositions[candidateIndex];

                        try
                        {
                            CharacterMainControl zombie = await zombiePreset.CreateCharacterAsync(
                                spawnPos, Vector3.forward, relatedScene, null, false);

                            if (!IsSpawnRequestStillValid(spawnRequestId, player, relatedScene))
                            {
                                if (zombie != null && zombie.gameObject != null)
                                {
                                    UnityEngine.Object.Destroy(zombie.gameObject);
                                }
                                return;
                            }

                            if (zombie == null)
                            {
                                continue;
                            }

                            // 设置血量为 100
                            SetZombieHealth(zombie, FrostmourneConfig.ZombieHealth);

                            // 设置为玩家同阵营
                            zombie.SetTeam(playerTeam);

                            // 禁止掉落
                            zombie.dropBoxOnDead = false;

                            // 设置名称
                            zombie.gameObject.name = "Frostmourne_Zombie_" + (summonedZombies.Count + 1);

                            // 激活
                            zombie.gameObject.SetActive(true);

                            // 设置 AI 追踪最近敌人
                            SetupZombieAI(zombie, player);

                            // 记录到列表
                            summonedZombies.Add(zombie);
                            reservedSpawnPositions.Add(zombie.transform.position);
                            RefreshSummonedZombieFollowerSlots(player);
                            successfulSummons++;
                            summonSucceeded = true;

                            LogIfVerbose("方位 " + slotIndex + " 僵尸召唤成功，候选点 #" + candidateIndex);
                            break;
                        }
                        catch (Exception e)
                        {
                            LogIfVerbose("方位 " + slotIndex + " 候选点 #" + candidateIndex + " 召唤异常: " + e.Message);
                        }
                    }

                    if (!summonSucceeded)
                    {
                        LogIfVerbose("方位 " + slotIndex + " 所有候选生成点均失败");
                    }

                    // 每只之间让出一帧，避免卡顿
                    await UniTask.Yield();
                }

                if (!IsSpawnRequestStillValid(spawnRequestId, player, relatedScene))
                {
                    return;
                }

                LogIfVerbose("亡灵召唤完成");

                // 显示召唤成功气泡
                CleanupDeadZombies();
                int currentCount = summonedZombies.Count;
                ShowSummonSuccessBubble(player, currentCount, successfulSummons);
            }
            catch (Exception e)
            {
                LogIfVerbose("SpawnZombiesAsync 异常: " + e.Message);
            }
            finally
            {
                if (spawnRequestId == activeSpawnRequestId)
                {
                    spawningComplete = true;
                }
            }
        }

        private static List<Vector3> CollectReservedZombieSpawnPositions()
        {
            List<Vector3> reservedPositions = new List<Vector3>();
            for (int i = 0; i < summonedZombies.Count; i++)
            {
                CharacterMainControl zombie = summonedZombies[i];
                if (zombie == null || zombie.transform == null)
                {
                    continue;
                }

                reservedPositions.Add(zombie.transform.position);
            }

            return reservedPositions;
        }

        private static List<Vector3> BuildCandidateZombieSpawnPositions(
            Vector3 playerPos,
            float fallbackY,
            int slotIndex,
            List<Vector3> reservedSpawnPositions)
        {
            List<Vector3> candidates = new List<Vector3>();
            int summonCount = Mathf.Max(1, FrostmourneConfig.SummonCount);
            float baseAngle = 360f * (slotIndex % summonCount) / summonCount;

            for (int radiusIndex = 0; radiusIndex < SummonRadiusScales.Length; radiusIndex++)
            {
                float scaledRadius = FrostmourneConfig.SummonRadius * SummonRadiusScales[radiusIndex];
                for (int angleIndex = 0; angleIndex < SummonAngleOffsets.Length; angleIndex++)
                {
                    float angle = baseAngle + SummonAngleOffsets[angleIndex];
                    Vector3 offset =
                        Quaternion.Euler(0f, angle, 0f) * Vector3.forward * scaledRadius;
                    Vector3 candidate = SnapToGround(playerPos + offset, fallbackY);

                    if (!IsSpawnPointValid(candidate))
                    {
                        continue;
                    }

                    if (IsSpawnPositionReserved(candidate, reservedSpawnPositions))
                    {
                        continue;
                    }

                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private static bool IsSpawnPositionReserved(
            Vector3 candidate,
            List<Vector3> reservedSpawnPositions)
        {
            if (reservedSpawnPositions == null)
            {
                return false;
            }

            const float minDistance = 1.1f;
            float minDistanceSqr = minDistance * minDistance;
            for (int i = 0; i < reservedSpawnPositions.Count; i++)
            {
                Vector3 delta = reservedSpawnPositions[i] - candidate;
                delta.y = 0f;
                if (delta.sqrMagnitude < minDistanceSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSpawnRequestStillValid(int spawnRequestId, CharacterMainControl player, int relatedScene)
        {
            if (this == null || spawnRequestId != activeSpawnRequestId || !spawningStarted)
            {
                return false;
            }

            if (!isActiveAndEnabled || player == null || CharacterMainControl.Main != player)
            {
                return false;
            }

            if (player.Health != null && player.Health.IsDead)
            {
                return false;
            }

            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == relatedScene;
        }

        /// <summary>
        /// 查找 Cname_Zombie 预设
        /// </summary>
        private static CharacterRandomPreset FindZombiePreset()
        {
            if (cachedZombiePreset != null) return cachedZombiePreset;
            if (presetSearchAttempted) return null;

            presetSearchAttempted = true;

            try
            {
                CharacterRandomPreset[] allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                foreach (CharacterRandomPreset preset in allPresets)
                {
                    if (preset == null) continue;
                    if (!string.IsNullOrEmpty(preset.nameKey) &&
                        preset.nameKey == FrostmourneConfig.ZombiePresetName)
                    {
                        cachedZombiePreset = preset;
                        ModBehaviour.DevLog("[Frostmourne] 找到 Cname_Zombie 预设: " + preset.name);
                        return cachedZombiePreset;
                    }
                }

                ModBehaviour.DevLog("[Frostmourne] [WARNING] 未找到 Cname_Zombie 预设");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 查找僵尸预设异常: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 设置僵尸血量
        /// </summary>
        private void SetZombieHealth(CharacterMainControl zombie, float targetHealth)
        {
            try
            {
                if (zombie.Health == null) return;

                Item characterItem = zombie.CharacterItem;
                if (characterItem == null) return;

                Stat hpStat = characterItem.GetStat("MaxHealth".GetHashCode());
                if (hpStat != null)
                {
                    float currentMax = zombie.Health.MaxHealth;
                    if (currentMax > 0.01f)
                    {
                        float scale = targetHealth / currentMax;
                        hpStat.BaseValue *= scale;
                    }
                    else
                    {
                        hpStat.BaseValue = targetHealth;
                    }
                }

                // 同步血量到满
                zombie.Health.SetHealth(zombie.Health.MaxHealth);
            }
            catch (Exception e)
            {
                LogIfVerbose("设置僵尸血量失败: " + e.Message);
            }
        }

        /// <summary>
        /// 设置僵尸 AI（跟随玩家附近，攻击敌人）
        /// </summary>
        private void SetupZombieAI(CharacterMainControl zombie, CharacterMainControl player)
        {
            try
            {
                AICharacterController ai = zombie.GetComponentInChildren<AICharacterController>();
                if (ai == null) return;

                // 设置 AI 战斗因子为 1（积极战斗）
                ai.forceTracePlayerDistance = 0f; // 不强制追踪玩家（它是友军）
                ai.searchedEnemy = null;
                ai.noticed = false;

                // 让 AI 自然寻敌（同阵营设置后，AI 会自动攻击敌对阵营）
            }
            catch (Exception e)
            {
                LogIfVerbose("设置僵尸 AI 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 将位置对齐到地面
        /// </summary>
        private static Vector3 SnapToGround(Vector3 position, float fallbackY)
        {
            RaycastHit hit;
            Vector3 samplePoint = position + Vector3.up * 5f;
            int groundMask = GameplayDataSettings.Layers.groundLayerMask;

            if (Physics.Raycast(samplePoint, Vector3.down, out hit, 15f, groundMask))
            {
                return hit.point;
            }

            position.y = fallbackY;
            return position;
        }

        /// <summary>
        /// 检查生成点是否可用（不在墙壁内）
        /// </summary>
        private static bool IsSpawnPointValid(Vector3 position)
        {
            // 检测墙壁等不可通过的障碍物
            int wallMask = LayerMask.GetMask("Default", "Wall");
            Collider[] hits = Physics.OverlapCapsule(
                position,
                position + Vector3.up * 1.5f,
                0.4f,
                wallMask
            );

            foreach (Collider col in hits)
            {
                if (col != null && !col.isTrigger)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 获取当前仍然存活的召唤亡灵数量
        /// </summary>
        internal static int GetActiveSummonedZombieCount()
        {
            CleanupDeadZombies();
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null)
            {
                RefreshSummonedZombieFollowerSlots(player);
            }
            return summonedZombies.Count;
        }

        internal static bool IsSummonCapReached()
        {
            return GetActiveSummonedZombieCount() >= FrostmourneConfig.SummonCount;
        }

        private static void RefreshSummonedZombieFollowerSlots(CharacterMainControl player)
        {
            if (player == null)
            {
                return;
            }

            for (int i = 0; i < summonedZombies.Count; i++)
            {
                CharacterMainControl zombie = summonedZombies[i];
                if (zombie == null || zombie.gameObject == null)
                {
                    continue;
                }

                FrostmourneZombieFollower follower = zombie.GetComponent<FrostmourneZombieFollower>();
                if (follower == null)
                {
                    follower = zombie.gameObject.AddComponent<FrostmourneZombieFollower>();
                }

                follower.Initialize(player, i);
            }
        }

        /// <summary>
        /// 清理已死亡的僵尸引用
        /// </summary>
        private static void CleanupDeadZombies()
        {
            for (int i = summonedZombies.Count - 1; i >= 0; i--)
            {
                CharacterMainControl zombie = summonedZombies[i];
                if (zombie == null || zombie.gameObject == null ||
                    (zombie.Health != null && zombie.Health.IsDead))
                {
                    summonedZombies.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 显示召唤成功的对话气泡
        /// </summary>
        private static void ShowSummonSuccessBubble(CharacterMainControl player, int currentCount, int successfulSummons)
        {
            if (player == null || player.transform == null) return;
            if (successfulSummons <= 0) return;

            try
            {
                int max = FrostmourneConfig.SummonCount;
                string bubbleText;
                bool isFreshFullSummon = currentCount >= max && currentCount == successfulSummons;
                if (isFreshFullSummon)
                {
                    // 全额召唤
                    bubbleText = L10n.T(
                        "<color=#81D4FA>亡灵仆从已召唤！</color>",
                        "<color=#81D4FA>Undead summoned!</color>");
                }
                else
                {
                    // 补充召唤
                    bubbleText = L10n.T(
                        "<color=#81D4FA>亡灵仆从已补充！</color>",
                        "<color=#81D4FA>Undead replenished!</color>");
                }

                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    bubbleText,
                    player.transform,
                    2.5f,
                    false,
                    false,
                    -1f,
                    2f
                );
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] 显示召唤气泡异常: " + e.Message);
            }
        }

        /// <summary>
        /// 重置预设缓存（场景切换时调用）
        /// </summary>
        internal static void ResetPresetCache()
        {
            cachedZombiePreset = null;
            presetSearchAttempted = false;
        }

        /// <summary>
        /// 清理所有召唤的僵尸
        /// </summary>
        internal static void CleanupAllSummonedZombies()
        {
            foreach (CharacterMainControl zombie in summonedZombies)
            {
                try
                {
                    if (zombie != null && zombie.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(zombie.gameObject);
                    }
                }
                catch { }
            }
            summonedZombies.Clear();
        }
    }

    internal sealed class FrostmourneZombieFollower : NPCFollowMovementBase
    {
        private const float FollowRingRadius = 2.8f;
        private const float FollowStopDistanceValue = 1.6f;
        private const float FollowRunDistanceValue = 4.5f;
        private const float FollowTeleportDistanceValue = 20f;
        private const float FollowRepathIntervalValue = 0.35f;
        private const float FollowSpeedBoostDistanceValue = 5.5f;
        private const float FollowSpeedResetDistanceValue = 3.5f;
        private const float NextWaypointDistance = 0.45f;
        private const float TurnSpeed = 360f;
        private const float Gravity = -9.8f;

        private CharacterMainControl zombieCharacter;
        private AICharacterController aiController;
        private Seeker seeker;
        private CharacterController characterController;
        private Animator animator;
        private Path path;
        private int currentWaypoint;
        private bool reachedEndOfPath;
        private bool moving;
        private bool waitingForPathResult;
        private int activePathRequestId;
        private float walkSpeed = 1.9f;
        private float runSpeed = 4.6f;
        private float verticalVelocity;
        private int followSlotIndex;
        private bool initialized;
        private static readonly int HashMoveSpeed = Animator.StringToHash("MoveSpeed");

        public void Initialize(CharacterMainControl player, int slotIndex)
        {
            bool slotChanged = followSlotIndex != Mathf.Max(0, slotIndex);

            if (!initialized)
            {
                CacheComponents();
                InitializeFollowDefaults();
                initialized = true;
                slotChanged = true;
            }

            followSlotIndex = Mathf.Max(0, slotIndex);
            if (player != null)
            {
                bool targetChanged = CurrentPlayerTransform != player.transform || !IsFollowingPlayer;
                if (targetChanged)
                {
                    EnablePlayerFollow(player.transform);
                }

                if (slotChanged && !targetChanged)
                {
                    StopMoveForFollow();
                }
            }
        }

        protected override float WalkSpeed
        {
            get { return walkSpeed; }
            set { walkSpeed = value; }
        }

        protected override float RunSpeed
        {
            get { return runSpeed; }
            set { runSpeed = value; }
        }

        protected override float FollowRepathInterval
        {
            get { return FollowRepathIntervalValue; }
        }

        protected override float FollowStopDistance
        {
            get { return FollowStopDistanceValue; }
        }

        protected override float FollowRunDistance
        {
            get { return FollowRunDistanceValue; }
        }

        protected override float FollowTeleportDistance
        {
            get { return FollowTeleportDistanceValue; }
        }

        protected override float FollowSpeedBoostDistance
        {
            get { return FollowSpeedBoostDistanceValue; }
        }

        protected override float FollowSpeedResetDistance
        {
            get { return FollowSpeedResetDistanceValue; }
        }

        protected override Seeker FollowSeeker
        {
            get { return seeker; }
        }

        protected override CharacterController FollowCharacterController
        {
            get { return characterController; }
        }

        protected override int FollowActivePathRequestId
        {
            get { return activePathRequestId; }
            set { activePathRequestId = value; }
        }

        protected override bool FollowWaitingForPathResult
        {
            get { return waitingForPathResult; }
            set { waitingForPathResult = value; }
        }

        protected override bool FollowReachedEndOfPath
        {
            get { return reachedEndOfPath; }
            set { reachedEndOfPath = value; }
        }

        protected override Vector3 GetFollowDestination(Transform target)
        {
            if (target == null)
            {
                return transform.position;
            }

            Vector3 forward = target.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            float angle = 360f * (followSlotIndex % FrostmourneConfig.SummonCount) / Mathf.Max(1, FrostmourneConfig.SummonCount);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * forward * FollowRingRadius;
            Vector3 desired = target.position + offset;

            return SnapToGround(desired, target.position.y);
        }

        protected override void StopCurrentFollowMovement()
        {
            if (seeker != null)
            {
                seeker.CancelCurrentPathRequest(true);
            }

            NPCPathingHelper.StopMovement(
                ref path,
                ref currentWaypoint,
                ref moving,
                ref waitingForPathResult,
                UpdateMoveAnimation);
        }

        protected override void HandleFollowPathComplete(Path pathResult, int requestId)
        {
            NPCPathingHelper.HandlePathComplete(
                pathResult,
                requestId,
                activePathRequestId,
                false,
                null,
                ref path,
                ref currentWaypoint,
                ref moving,
                ref waitingForPathResult,
                UpdateMoveAnimation,
                null);
        }

        private void Awake()
        {
            CacheComponents();
        }

        private void Start()
        {
            InitializeFollowDefaults();
            initialized = true;
        }

        private void Update()
        {
            if (!initialized || !IsFollowingPlayer)
            {
                return;
            }

            UpdateGravityVelocity();

            if (ShouldSuspendFollowForCombat())
            {
                if (moving || waitingForPathResult)
                {
                    StopMoveForFollow();
                }

                ApplyGravityOnly();
                return;
            }

            UpdateFollowDecision(moving, waitingForPathResult, path != null);
            if (!UpdatePathFollowing(ShouldRunWhileFollowing()))
            {
                ApplyGravityOnly();
            }
        }

        private void CacheComponents()
        {
            zombieCharacter = GetComponent<CharacterMainControl>();
            aiController = GetComponentInChildren<AICharacterController>();
            seeker = GetComponent<Seeker>();
            if (seeker == null)
            {
                seeker = GetComponentInChildren<Seeker>();
            }

            characterController = GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = GetComponentInChildren<CharacterController>();
            }

            animator = GetComponentInChildren<Animator>();
        }

        private bool ShouldSuspendFollowForCombat()
        {
            if (aiController == null)
            {
                return false;
            }

            DamageReceiver searchedEnemy = aiController.searchedEnemy;
            if (searchedEnemy == null)
            {
                aiController.noticed = false;
                return false;
            }

            try
            {
                if (searchedEnemy.health != null && searchedEnemy.health.IsDead)
                {
                    aiController.searchedEnemy = null;
                    aiController.noticed = false;
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        private bool UpdatePathFollowing(bool isRunning)
        {
            if (waitingForPathResult && path == null)
            {
                return false;
            }

            if (path == null || path.vectorPath == null || path.vectorPath.Count == 0)
            {
                NPCPathingHelper.StopMovement(
                    ref path,
                    ref currentWaypoint,
                    ref moving,
                    ref waitingForPathResult,
                    UpdateMoveAnimation);
                return false;
            }

            if (currentWaypoint >= path.vectorPath.Count)
            {
                reachedEndOfPath = true;
                NPCPathingHelper.StopMovement(
                    ref path,
                    ref currentWaypoint,
                    ref moving,
                    ref waitingForPathResult,
                    UpdateMoveAnimation);
                return false;
            }

            moving = true;
            reachedEndOfPath = false;

            float distanceToWaypoint;
            while (true)
            {
                Vector3 toWaypoint = path.vectorPath[currentWaypoint] - transform.position;
                toWaypoint.y = 0f;
                distanceToWaypoint = toWaypoint.magnitude;

                if (distanceToWaypoint < NextWaypointDistance)
                {
                    if (currentWaypoint + 1 < path.vectorPath.Count)
                    {
                        currentWaypoint++;
                    }
                    else
                    {
                        reachedEndOfPath = true;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            Vector3 direction = path.vectorPath[currentWaypoint] - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                NPCPathingHelper.StopMovement(
                    ref path,
                    ref currentWaypoint,
                    ref moving,
                    ref waitingForPathResult,
                    UpdateMoveAnimation);
                return false;
            }

            direction.Normalize();

            float speedMultiplier = 1f;
            if (reachedEndOfPath)
            {
                speedMultiplier = Mathf.Sqrt(Mathf.Clamp01(distanceToWaypoint / NextWaypointDistance));

                if (distanceToWaypoint < FollowStopDistanceValue)
                {
                    NPCPathingHelper.StopMovement(
                        ref path,
                        ref currentWaypoint,
                        ref moving,
                        ref waitingForPathResult,
                        UpdateMoveAnimation);
                    return false;
                }
            }

            float currentSpeed = (isRunning ? runSpeed : walkSpeed) * speedMultiplier;
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, TurnSpeed * Time.deltaTime);

            Vector3 move = direction * currentSpeed;
            move.y = verticalVelocity;

            if (characterController != null && characterController.enabled)
            {
                characterController.Move(move * Time.deltaTime);
            }
            else
            {
                transform.position += direction * currentSpeed * Time.deltaTime;
            }

            UpdateMoveAnimation(currentSpeed);
            return true;
        }

        private void UpdateGravityVelocity()
        {
            if (characterController != null && characterController.enabled && characterController.isGrounded)
            {
                if (verticalVelocity < 0f)
                {
                    verticalVelocity = -2f;
                }
                return;
            }

            verticalVelocity += Gravity * Time.deltaTime;
        }

        private void ApplyGravityOnly()
        {
            if (characterController != null && characterController.enabled)
            {
                characterController.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
            }
        }

        private void UpdateMoveAnimation(float speed)
        {
            if (animator == null)
            {
                return;
            }

            try
            {
                animator.SetFloat(HashMoveSpeed, speed);
            }
            catch
            {
            }
        }

        private static Vector3 SnapToGround(Vector3 position, float fallbackY)
        {
            RaycastHit hit;
            Vector3 samplePoint = position + Vector3.up * 5f;
            int groundMask = GameplayDataSettings.Layers.groundLayerMask;

            if (Physics.Raycast(samplePoint, Vector3.down, out hit, 15f, groundMask))
            {
                return hit.point;
            }

            position.y = fallbackY;
            return position;
        }
    }
}
