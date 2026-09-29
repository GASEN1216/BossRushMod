using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        private readonly List<ZombieModeEnemyRuntimeMarker> bossEnemyMarkerScratch = new List<ZombieModeEnemyRuntimeMarker>();

        private const int ZombieModeSupportSpawnRequestsPerFrame = 3;

        private enum ZombieModeSupportSpawnKind
        {
            SmallSplit,
            SplitterChild
        }

        private struct ZombieModeSupportSpawnRequest
        {
            public int RunId;
            public ZombieModeSupportSpawnKind Kind;
            public Vector3 Position;
            public float Scale;
        }

        private readonly System.Collections.Generic.Queue<ZombieModeSupportSpawnRequest> zombieModeSupportSpawnRequests =
            new System.Collections.Generic.Queue<ZombieModeSupportSpawnRequest>();

        private bool zombieModeSupportSpawnProcessorStarted;

        internal void RegisterZombieModeBossRuntime(int runId, CharacterMainControl boss, ZombieModeBossKind kind)
        {
            if (!IsZombieModeRunValid(runId) || boss == null)
            {
                return;
            }

            for (int i = 0; i < runState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = runState.CurrentWaveBossInstances[i];
                if (instance == null || instance.Character != boss)
                {
                    continue;
                }

                instance.Kind = kind;
                if (instance.Marker == null)
                {
                    instance.Marker = boss.GetComponent<ZombieModeEnemyRuntimeMarker>();
                }
                float now = GetZombieModeRuntimeNow();
                instance.Lifecycle.LastKnownPosition = boss.transform.position;
                instance.Lifecycle.LastReachableTime = GetZombieModeRuntimeNow();
                instance.Lifecycle.LastHurtTime = GetZombieModeRuntimeNow();
                instance.SkillState = CreateZombieModeBossSkillState(kind);
                instance.SkillState.Reset(now, boss.transform.localScale.x);
                ZombieModeBossVisuals.Attach(owner, instance);
                break;
            }
        }

        private static ZombieModeBossSkillState CreateZombieModeBossSkillState(ZombieModeBossKind kind)
        {
            switch (kind)
            {
                case ZombieModeBossKind.Titan: return new ZombieModeTitanState();
                case ZombieModeBossKind.Hunter: return new ZombieModeHunterState();
                case ZombieModeBossKind.Splitter: return new ZombieModeSplitterState();
                case ZombieModeBossKind.Shielder: return new ZombieModeShielderState();
                default: return new ZombieModeCorruptorState();
            }
        }

        internal void TickZombieModeBossController(float deltaTime)
        {
            if (!owner.IsZombieModeActive ||
                runState.CombatPhase != ZombieModeCombatPhase.Combat ||
                runState.CurrentWaveBossInstances.Count <= 0)
            {
                return;
            }

            float now = GetZombieModeRuntimeNow();
            for (int i = 0; i < runState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = runState.CurrentWaveBossInstances[i];
                if (instance == null || !instance.Lifecycle.Alive || instance.Character == null)
                {
                    continue;
                }

                Vector3 current = instance.Character.transform.position;
                if ((current - instance.Lifecycle.LastKnownPosition).sqrMagnitude > 1f)
                {
                    instance.Lifecycle.LastKnownPosition = current;
                    instance.Lifecycle.LastReachableTime = now;
                }
                else if (IsZombieModeBossEngagedWithPlayer(current))
                {
                    // 贴脸交战的 Boss 本来就不需要移动，不能按“位移不足”当成卡死。
                    instance.Lifecycle.LastReachableTime = now;
                }

                // 受击时间不能阻止解卡：玩家持续射击一个卡在墙边的 Boss 时，
                // LastHurtTime 会不断刷新；若把它作为硬门槛，波次可能永远无法结束。
                if (now - instance.Lifecycle.LastReachableTime >= ZombieModeTuning.BossStuckTimeoutSeconds)
                {
                    TeleportZombieModeBossNearPlayer(instance);
                }

                // Tick state expirations (Titan DR). Hunter frenzy 触发后持续到死亡，没有到期。
                ZombieModeTitanState titanState = instance.SkillState as ZombieModeTitanState;
                if (titanState != null && titanState.DamageReductionActive && now >= titanState.DamageReductionEndTime)
                {
                    titanState.DamageReductionActive = false;
                }

                TryExecuteZombieModeBossSkill(instance, now);
            }
        }

        private void TryExecuteZombieModeBossSkill(ZombieModeBossInstance instance, float now)
        {
            if (instance == null || instance.SkillState == null || instance.Character == null)
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            instance.SkillState.Tick(owner, instance, now);
        }

        // ====================================================================
        // 5 个 per-kind Tick 方法（被对应 SkillState 子类的 Tick override 调用）
        // ====================================================================
        // 多态化前：BossController 主循环用 switch + 强制下行转换分发 5 种 Boss
        // 技能逻辑；每加一个 Boss 要改 Create/Cooldown/Tick 三处。
        // 多态化后：SkillState.Tick(mod, instance, now) → mod.TickZombieMode<Kind>State(...)。
        // BossController 内仍然集中"如何"实现（telegraph / 召唤 / 护盾），SkillState
        // 只负责"何时"和"哪个 Boss"。审查 §2.1。
        //
        // Boss 技能使用独立 L10n key。玩家看到的是技能起手，而不是只有 Boss 名。
        // ====================================================================

        /// <summary>
        /// 同场多 Boss 的伤害技能节流：窗口空着就占用并返回 true；被占用时返回 false，
        /// 调用方不消耗自己的冷却，下一帧再试。只管有伤害判定的起手（震波、冲刺、腐蚀圈），护盾与召唤不受限。
        /// </summary>
        private bool TryClaimZombieModeBossSkillWindow(float now)
        {
            if (now < runState.NextBossSkillWindowTime) return false;
            runState.NextBossSkillWindowTime = now + ZombieModeTuning.BossSkillGlobalSpacingSeconds;
            return true;
        }

        internal void TickZombieModeTitanState(ZombieModeTitanState titan, ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance != null ? instance.Character : null;
            if (boss == null) return;
            int runId = runState.RunId;

            if (now >= titan.NextShockwaveTime && TryClaimZombieModeBossSkillWindow(now))
            {
                titan.NextShockwaveTime = now + ZombieModeTuning.TitanShockwaveCooldownSeconds;
                ZombieModeBossVisuals.Pulse(instance.Marker);
                StartZombieModeTelegraphedAreaDamage(
                    runId,
                    boss,
                    boss.transform.position,
                    ZombieModeTuning.TitanShockwaveRadius,
                    ZombieModeTuning.TitanShockwaveDamage * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave),
                    ZombieModeTuning.TitanShockwaveStartupSeconds,
                    L10n.T("BossRush_ZombieMode_BossSkill_TitanShockwave"));
            }
            if (now >= titan.NextDamageReductionTime && !titan.DamageReductionActive)
            {
                titan.NextDamageReductionTime = now + ZombieModeTuning.TitanDamageReductionCooldownSeconds;
                titan.DamageReductionActive = true;
                ZombieModeBossVisuals.Pulse(instance.Marker);
                titan.DamageReductionEndTime = now
                    + ZombieModeTuning.TitanDamageReductionStartupSeconds
                    + ZombieModeTuning.TitanDamageReductionDurationSeconds;
                boss.PopText(L10n.T("BossRush_ZombieMode_BossSkill_TitanFortify"));
            }
        }

        internal void TickZombieModeHunterState(ZombieModeHunterState hunter, ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance != null ? instance.Character : null;
            CharacterMainControl player = CharacterMainControl.Main;
            if (boss == null || player == null) return;
            int runId = runState.RunId;

            if (now >= hunter.NextDashTime && TryClaimZombieModeBossSkillWindow(now))
            {
                hunter.NextDashTime = now + ZombieModeTuning.HunterDashCooldownSeconds;
                ZombieModeBossVisuals.Pulse(instance.Marker);
                boss.PopText(L10n.T("BossRush_ZombieMode_BossSkill_HunterDash"));
                GameObject telegraph = CreateZombieModeFlatZoneVisual(
                    "ZombieMode_HunterDashTelegraph", boss.transform.position,
                    ZombieModeTuning.HunterDashRadius, 0.02f, ZombieModeBossVisuals.GetAccent(instance.Kind));
                ZombieModeSprinterDashRuntime dash = telegraph.AddComponent<ZombieModeSprinterDashRuntime>();
                dash.Initialize(runId, boss, player.transform.position,
                    ZombieModeTuning.HunterDashDistance, ZombieModeTuning.HunterDashStartupSeconds, 0.18f,
                    ZombieModeTuning.HunterDashRadius,
                    ZombieModeTuning.HunterDashDamage * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave));
                RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, telegraph, dash, null);
            }
        }

        internal void TickZombieModeSplitterState(ZombieModeSplitterState splitter, ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance != null ? instance.Character : null;
            if (boss == null) return;
            int runId = runState.RunId;

            if (now >= splitter.NextSummonTime)
            {
                splitter.NextSummonTime = now + ZombieModeTuning.SplitterBossSummonCooldownSeconds;
                ZombieModeBossVisuals.Pulse(instance.Marker);
                boss.PopText(L10n.T("BossRush_ZombieMode_BossSkill_SplitterSummon"));
                for (int i = 0; i < ZombieModeTuning.SplitterBossSummonCount; i++)
                {
                    Vector3 offset = Quaternion.Euler(0f, 360f * i / ZombieModeTuning.SplitterBossSummonCount, 0f) * Vector3.forward * 2f;
                    QueueZombieModeSplitterChildSpawn(runId, boss.transform.position + offset, ZombieModeTuning.SplitterBossSummonScale);
                }
            }
        }

        internal void TickZombieModeShielderState(ZombieModeShielderState shielder, ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance != null ? instance.Character : null;
            if (boss == null) return;
            int runId = runState.RunId;

            if (now >= shielder.NextSelfShieldTime)
            {
                shielder.NextSelfShieldTime = now + ZombieModeTuning.ShielderSelfShieldCooldownSeconds;
                ZombieModeBossVisuals.Pulse(instance.Marker);
                boss.PopText(L10n.T("BossRush_ZombieMode_BossSkill_ShielderSelfShield"));
                if (boss.Health != null)
                {
                    float amount = boss.Health.MaxHealth * ZombieModeTuning.ShielderSelfShieldPercent;
                    ZombieModeEnemyRuntimeMarker bossMarker = EnsureZombieModeBossMarker(instance);
                    if (bossMarker != null)
                    {
                        ZombieModeBossShieldRuntime shield = EnsureZombieModeBossShieldRuntime(bossMarker);
                        if (shield != null)
                        {
                            shield.ActivateShield(runId, amount, ZombieModeTuning.ShielderSelfShieldDurationSeconds);
                        }
                    }
                }
            }
            if (now >= shielder.NextGroupShieldTime)
            {
                shielder.NextGroupShieldTime = now + ZombieModeTuning.ShielderGroupShieldCooldownSeconds;
                ZombieModeBossVisuals.Pulse(instance.Marker);
                boss.PopText(L10n.T("BossRush_ZombieMode_BossSkill_ShielderGroupShield"));
                ApplyZombieModeBossShieldPulse(runId, boss.transform.position);
            }
        }

        internal void TickZombieModeCorruptorState(ZombieModeCorruptorState corruptor, ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance != null ? instance.Character : null;
            if (boss == null) return;
            CharacterMainControl player = CharacterMainControl.Main;
            int runId = runState.RunId;

            if (now >= corruptor.NextZoneTime && player != null && TryClaimZombieModeBossSkillWindow(now))
            {
                corruptor.NextZoneTime = now + ZombieModeTuning.CorruptorZoneCooldownSeconds;
                ZombieModeBossVisuals.Pulse(instance.Marker);
                SpawnZombieModeCorruptionZone(runId, boss, player.transform.position);
                boss.PopText(L10n.T("BossRush_ZombieMode_BossSkill_CorruptorZone"));
            }
            if (now >= corruptor.NextPoisonPathTime)
            {
                corruptor.NextPoisonPathTime = now + ZombieModeTuning.CorruptorPoisonPathTickIntervalSeconds;
                SpawnZombieModePoisonPathSegment(runId, boss, boss.transform.position);
            }
        }

        private void SpawnZombieModeCorruptionZone(int runId, CharacterMainControl source, Vector3 origin)
        {
            if (!IsZombieModeRunValid(runId)) return;

            // 共享 disk mesh 替代 CreatePrimitive(Cylinder)（审查 §3.3）。
            GameObject zone = CreateZombieModeFlatZoneVisual(
                "ZombieMode_CorruptionZone",
                origin + Vector3.up * 0.04f,
                ZombieModeTuning.CorruptorZoneRadius,
                0.04f,
                new Color(0.58f, 0.36f, 0.74f, 0.55f));

            ZombieModeAreaTickRuntime runtime = zone.AddComponent<ZombieModeAreaTickRuntime>();
            runtime.Initialize(
                runId,
                source,
                ZombieModeTuning.CorruptorZoneRadius,
                ZombieModeTuning.CorruptorZoneStartupSeconds + ZombieModeTuning.CorruptorZoneDurationSeconds,
                ZombieModeTuning.CorruptorZoneDamagePerSecond * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave),
                0.5f,
                ZombieModeTuning.CorruptorZoneSlowPercent,
                ZombieModeTuning.CorruptorZoneStartupSeconds);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, zone, runtime, null);
        }

        private void SpawnZombieModePoisonPathSegment(int runId, CharacterMainControl source, Vector3 origin)
        {
            if (!IsZombieModeRunValid(runId)) return;

            float radius = ZombieModeTuning.CorruptorPoisonPathWidth * 0.5f;
            // 共享 disk mesh 替代 CreatePrimitive(Cylinder)（审查 §3.3）。
            GameObject seg = CreateZombieModeFlatZoneVisual(
                "ZombieMode_PoisonPath",
                origin + Vector3.up * 0.03f,
                radius,
                0.03f,
                new Color(0.46f, 0.70f, 0.36f, 0.50f));

            ZombieModeAreaTickRuntime runtime = seg.AddComponent<ZombieModeAreaTickRuntime>();
            runtime.Initialize(
                runId,
                source,
                radius,
                ZombieModeTuning.CorruptorPoisonPathDurationSeconds,
                ZombieModeTuning.CorruptorPoisonPathDamagePerSecond * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave),
                0.5f);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, seg, runtime, null);
        }

        private async UniTask SpawnZombieModeSplitterChildAsync(int runId, Vector3 position, float scale)
        {
            CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(
                runId,
                position,
                ZombieModeEnemyKind.Normal,
                true,
                () => runState.CombatPhase == ZombieModeCombatPhase.Combat);
            if (zombie != null)
            {
                zombie.transform.localScale = zombie.transform.localScale * scale;
            }
        }

        private void QueueZombieModeSmallSplitSpawn(int runId, Vector3 position)
        {
            ZombieModeSupportSpawnRequest request = new ZombieModeSupportSpawnRequest();
            request.RunId = runId;
            request.Kind = ZombieModeSupportSpawnKind.SmallSplit;
            request.Position = position;
            request.Scale = 1f;
            QueueZombieModeSupportSpawn(request);
        }

        private void QueueZombieModeSplitterChildSpawn(int runId, Vector3 position, float scale)
        {
            ZombieModeSupportSpawnRequest request = new ZombieModeSupportSpawnRequest();
            request.RunId = runId;
            request.Kind = ZombieModeSupportSpawnKind.SplitterChild;
            request.Position = position;
            request.Scale = scale;
            QueueZombieModeSupportSpawn(request);
        }

        private void QueueZombieModeSupportSpawn(ZombieModeSupportSpawnRequest request)
        {
            if (!IsZombieModeRunValid(request.RunId))
            {
                return;
            }

            zombieModeSupportSpawnRequests.Enqueue(request);
            if (zombieModeSupportSpawnProcessorStarted)
            {
                return;
            }

            zombieModeSupportSpawnProcessorStarted = true;
            owner.StartZombieModeBossCoroutineForRuntimeModule(ProcessZombieModeSupportSpawnQueue(request.RunId), request.RunId);
        }

        private IEnumerator ProcessZombieModeSupportSpawnQueue(int runId)
        {
            while (IsZombieModeRunValid(runId) && zombieModeSupportSpawnRequests.Count > 0)
            {
                int processed = 0;
                while (processed < ZombieModeSupportSpawnRequestsPerFrame && zombieModeSupportSpawnRequests.Count > 0)
                {
                    ZombieModeSupportSpawnRequest request = zombieModeSupportSpawnRequests.Dequeue();
                    processed++;

                    if (!IsZombieModeRunValid(request.RunId))
                    {
                        continue;
                    }

                    if (request.Kind == ZombieModeSupportSpawnKind.SmallSplit)
                    {
                        SpawnZombieModeSmallSplitAsync(request.RunId, request.Position).Forget();
                    }
                    else
                    {
                        SpawnZombieModeSplitterChildAsync(request.RunId, request.Position, request.Scale).Forget();
                    }
                }

                if (zombieModeSupportSpawnRequests.Count > 0)
                {
                    yield return null;
                }
            }

            zombieModeSupportSpawnProcessorStarted = false;
        }

        internal void ClearZombieModeSupportSpawnQueue()
        {
            zombieModeSupportSpawnRequests.Clear();
            zombieModeSupportSpawnProcessorStarted = false;
        }

        private void ApplyZombieModeBossShieldPulse(int runId, Vector3 origin)
        {
            ApplyZombieModeShielderGroupShield(runId, origin);
        }

        private static ZombieModeEnemyRuntimeMarker EnsureZombieModeBossMarker(ZombieModeBossInstance instance)
        {
            if (instance == null)
            {
                return null;
            }

            ZombieModeEnemyRuntimeMarker marker = instance.Marker;
            if (marker == null && instance.Character != null)
            {
                marker = instance.Character.GetComponent<ZombieModeEnemyRuntimeMarker>();
                instance.Marker = marker;
            }

            if (marker != null && marker.Owner == null)
            {
                marker.Owner = instance.Character;
            }

            return marker;
        }

        private static ZombieModeBossShieldRuntime EnsureZombieModeBossShieldRuntime(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.gameObject == null)
            {
                return null;
            }

            ZombieModeBossShieldRuntime shield = marker.AllyShield;
            if (shield == null)
            {
                shield = marker.gameObject.GetComponent<ZombieModeBossShieldRuntime>();
                if (shield == null)
                {
                    shield = marker.gameObject.AddComponent<ZombieModeBossShieldRuntime>();
                }

                marker.AllyShield = shield;
            }

            return shield;
        }

        private void ApplyZombieModeShielderGroupShield(int runId, Vector3 origin)
        {
            if (!IsZombieModeRunValid(runId)) return;

            CollectZombieModeRuntimeEnemyMarkers(runId, bossEnemyMarkerScratch, true);
            float radiusSqr = ZombieModeTuning.ShielderGroupShieldRadius * ZombieModeTuning.ShielderGroupShieldRadius;
            for (int i = 0; i < bossEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = bossEnemyMarkerScratch[i];
                if (marker == null || marker.RunId != runId) continue;

                Vector3 delta = marker.transform.position - origin;
                delta.y = 0f;
                if (delta.sqrMagnitude > radiusSqr) continue;

                CharacterMainControl ch = marker.Owner;
                if (ch == null)
                {
                    ch = marker.GetComponent<CharacterMainControl>();
                    marker.Owner = ch;
                }
                if (ch == null || ch.Health == null || ch.Health.CurrentHealth <= 0f) continue;

                float amount = ch.Health.MaxHealth * ZombieModeTuning.ShielderGroupShieldPercent;
                ZombieModeBossShieldRuntime shield = EnsureZombieModeBossShieldRuntime(marker);
                if (shield == null)
                {
                    continue;
                }
                shield.ActivateShield(runId, amount, ZombieModeTuning.ShielderGroupShieldDurationSeconds);
                marker.AllyShield = shield;
                ch.PopText(L10n.T("BossRush_ZombieMode_Affix_Shielded"));
            }

            bossEnemyMarkerScratch.Clear();
        }

        /// <summary>
        /// Boss 是否正贴身与玩家交战。交战中的 Boss 不移动是正常战斗形态
        /// （举盾、起手 telegraph、近战贴脸），不属于卡死。
        /// </summary>
        private bool IsZombieModeBossEngagedWithPlayer(Vector3 bossPosition)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return false;
            }

            Vector3 delta = bossPosition - player.transform.position;

            // 垂直差必须单独判：只压平 y 的话，掉进地形、卡在楼板上下或被几何体吞掉的
            // Boss 只要水平投影离玩家够近，就会被当成「正在贴脸交战」而永远不解卡，
            // 波次就此卡死。贴脸交战的真实形态是水平近**且**基本同一高度。
            if (Mathf.Abs(delta.y) > ZombieModeTuning.BossStuckEngagedHeightTolerance) return false;

            delta.y = 0f;
            float engagedDistance = ZombieModeTuning.BossStuckEngagedDistance;
            return delta.sqrMagnitude <= engagedDistance * engagedDistance;
        }

        private void TeleportZombieModeBossNearPlayer(ZombieModeBossInstance instance)
        {
            if (instance == null || instance.Character == null)
            {
                return;
            }

            CharacterMainControl boss = instance.Character;
            Vector3 target;
            if (!TryResolveZombieModeBossFallbackPosition(instance, out target))
            {
                return;
            }

            try
            {
                boss.SetPosition(target);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] Boss SetPosition 失败，回退 transform.position: " + e.Message);
                boss.transform.position = target;
            }

            NavMeshAgent navAgent = boss.GetComponentInChildren<NavMeshAgent>(true);
            if (navAgent != null && navAgent.enabled)
            {
                try { navAgent.Warp(target); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] Boss NavMeshAgent.Warp 失败: " + e.Message); }
            }

            Rigidbody rb = boss.GetComponent<Rigidbody>();
            if (rb == null) rb = boss.GetComponentInChildren<Rigidbody>();
            if (rb != null)
            {
                try { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } catch { }
            }
            instance.Lifecycle.LastKnownPosition = target;
            instance.Lifecycle.LastReachableTime = GetZombieModeRuntimeNow();
            instance.Lifecycle.LastHurtTime = GetZombieModeRuntimeNow();
            ZombieModeEnemyRuntimeMarker marker = EnsureZombieModeBossMarker(instance);
            AICharacterController ai = GetZombieModeEnemyAI(boss.gameObject, marker);
            CharacterMainControl main = CharacterMainControl.Main;
            if (ai != null && main != null)
            {
                SetZombieModeEnemyTargetToMainPlayer(ai);
                ai.noticed = true;
            }
            ModBehaviour.DevLog("[ZombieMode] Boss stuck fallback teleport: " + instance.Kind.ToString());
        }

        private bool TryResolveZombieModeBossFallbackPosition(ZombieModeBossInstance instance, out Vector3 target)
        {
            target = Vector3.zero;
            if (instance == null || instance.Character == null)
            {
                return false;
            }

            return TryGetZombieModeReliableSpawnPosition(out target);
        }

        internal void HandleZombieModeBossHurt(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl victim)
        {
            if (!IsZombieModeRunValid(runId) || marker == null || victim == null || !marker.IsBoss)
            {
                return;
            }

            for (int i = 0; i < runState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = runState.CurrentWaveBossInstances[i];
                if (instance == null || instance.Character != victim)
                {
                    continue;
                }

                instance.Marker = marker;
                instance.Lifecycle.LastHurtTime = GetZombieModeRuntimeNow();

                // Hunter low-HP frenzy trigger
                ZombieModeHunterState hunterDamageState = instance.SkillState as ZombieModeHunterState;
                if (instance.Kind == ZombieModeBossKind.Hunter &&
                    hunterDamageState != null &&
                    !hunterDamageState.FrenzyActive &&
                    victim.Health != null &&
                    victim.Health.MaxHealth > 0f &&
                    victim.Health.CurrentHealth / victim.Health.MaxHealth <= ZombieModeTuning.HunterFrenzyHpThreshold)
                {
                    ActivateZombieModeHunterFrenzy(instance);
                }

                // Splitter HP-threshold split
                ZombieModeSplitterState splitterDamageState = instance.SkillState as ZombieModeSplitterState;
                if (instance.Kind == ZombieModeBossKind.Splitter && splitterDamageState != null && victim.Health != null && victim.Health.MaxHealth > 0f)
                {
                    float ratio = victim.Health.CurrentHealth / victim.Health.MaxHealth;
                    if (!splitterDamageState.FirstSplitTriggered && ratio <= ZombieModeTuning.SplitterBossSplitFirstHpThreshold)
                    {
                        splitterDamageState.FirstSplitTriggered = true;
                        TriggerZombieModeSplitterHpSplit(runId, victim);
                    }
                    if (!splitterDamageState.SecondSplitTriggered && ratio <= ZombieModeTuning.SplitterBossSplitSecondHpThreshold)
                    {
                        splitterDamageState.SecondSplitTriggered = true;
                        TriggerZombieModeSplitterHpSplit(runId, victim);
                    }
                }
                break;
            }
        }

        private void ActivateZombieModeHunterFrenzy(ZombieModeBossInstance instance)
        {
            if (instance == null || instance.Character == null) return;
            ZombieModeHunterState hunter = instance.SkillState as ZombieModeHunterState;
            if (hunter == null) return;
            // 狂暴是低血后的最终形态：触发一次、持续到死亡（owner 2026-09-27 定）。
            // 此前 15 秒到期后下一击又重新触发，体型在 1 与 1.08 倍之间来回跳。
            hunter.FrenzyActive = true;
            instance.Character.PopText(L10n.T("BossRush_ZombieMode_Boss_Hunter"));
            ApplyZombieModeHunterFrenzyModifiers(instance.Character, hunter);
            instance.Character.transform.localScale = instance.Character.transform.localScale * 1.08f;
        }

        private void ApplyZombieModeHunterFrenzyModifiers(CharacterMainControl character, ZombieModeHunterState hunter)
        {
            if (character == null || hunter == null)
            {
                return;
            }

            RemoveZombieModeHunterFrenzyModifiers(hunter);
            // 用 PercentageAdd 而非 Add（审查 §3.2）：避免被装备 / Buff 倍率稀释。
            // ZombieModeStatNames 收口（§2.3）。
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.MoveSpeed, ZombieModeTuning.HunterFrenzyMoveSpeedBonus, owner, hunter.FrenzyModifierRecords, "Hunter Frenzy MoveSpeed");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.WalkSpeed, ZombieModeTuning.HunterFrenzyMoveSpeedBonus, owner, hunter.FrenzyModifierRecords, "Hunter Frenzy WalkSpeed");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.RunSpeed, ZombieModeTuning.HunterFrenzyMoveSpeedBonus, owner, hunter.FrenzyModifierRecords, "Hunter Frenzy RunSpeed");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.AttackSpeed, ZombieModeTuning.HunterFrenzyAttackSpeedBonus, owner, hunter.FrenzyModifierRecords, "Hunter Frenzy AttackSpeed");
        }

        internal void RemoveZombieModeHunterFrenzyModifiers(ZombieModeHunterState hunter)
        {
            if (hunter == null || hunter.FrenzyModifierRecords.Count <= 0)
            {
                return;
            }

            RuntimeStatModifierTracker.RemoveAll(hunter.FrenzyModifierRecords, "Hunter Frenzy");
        }

        private void TriggerZombieModeSplitterHpSplit(int runId, CharacterMainControl victim)
        {
            if (!IsZombieModeRunValid(runId) || victim == null) return;
            for (int i = 0; i < ZombieModeTuning.SplitterBossSplitCount; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 360f * i / ZombieModeTuning.SplitterBossSplitCount, 0f) * Vector3.forward * 2f;
                QueueZombieModeSplitterChildSpawn(runId, victim.transform.position + offset, ZombieModeTuning.SplitterBossSplitChildScale);
            }
        }

        internal float AbsorbZombieModeBossFinalDamage(CharacterMainControl boss, ZombieModeEnemyRuntimeMarker bossMarker, float finalDamage)
        {
            if (boss == null || finalDamage <= 0f)
            {
                return 0f;
            }

            float damageAfterAbsorb = finalDamage;
            bool changed = false;

            ZombieModeBossShieldRuntime shield = bossMarker != null ? bossMarker.AllyShield : null;
            if (shield != null && shield.IsShieldActive())
            {
                float absorbed = shield.AbsorbDamage(finalDamage);
                if (absorbed > 0f)
                {
                    damageAfterAbsorb = Mathf.Max(0f, damageAfterAbsorb - absorbed);
                    changed = true;
                }
            }

            ZombieModeBossInstance titan = FindZombieModeBossInstanceFor(boss, bossMarker);
            ZombieModeTitanState titanReductionState = titan != null ? titan.SkillState as ZombieModeTitanState : null;
            if (titan != null &&
                titan.Kind == ZombieModeBossKind.Titan &&
                titanReductionState != null &&
                titanReductionState.DamageReductionActive)
            {
                damageAfterAbsorb *= (1f - ZombieModeTuning.TitanDamageReductionPercent);
                changed = true;
            }

            if (TryApplyZombieModeShielderAuraReduction(boss, ref damageAfterAbsorb))
            {
                changed = true;
            }

            return changed ? Mathf.Max(0f, finalDamage - damageAfterAbsorb) : 0f;
        }

        internal float ApplyZombieModeShielderAuraFinalDamageReduction(CharacterMainControl target, float finalDamage)
        {
            if (target == null || finalDamage <= 0f)
            {
                return 0f;
            }

            float reducedDamage = finalDamage;
            if (!TryApplyZombieModeShielderAuraReduction(target, ref reducedDamage))
            {
                return 0f;
            }

            return Mathf.Max(0f, finalDamage - reducedDamage);
        }

        private bool TryApplyZombieModeShielderAuraReduction(CharacterMainControl target, ref float damageValue)
        {
            if (target == null || runState.CurrentWaveBossInstances.Count <= 0) return false;

            float radiusSqr = ZombieModeTuning.ShielderAuraRadius * ZombieModeTuning.ShielderAuraRadius;
            for (int i = 0; i < runState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = runState.CurrentWaveBossInstances[i];
                if (instance == null || !instance.Lifecycle.Alive || instance.Character == null) continue;
                if (instance.Kind != ZombieModeBossKind.Shielder) continue;
                if (instance.Character == target) continue;

                Vector3 delta = target.transform.position - instance.Character.transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radiusSqr)
                {
                    damageValue *= (1f - ZombieModeTuning.ShielderAuraDamageReductionPercent);
                    return true;
                }
            }
            return false;
        }

        internal bool TryApplyZombieModeShielderAuraReductionPublic(CharacterMainControl target, ref float damageValue)
        {
            return TryApplyZombieModeShielderAuraReduction(target, ref damageValue);
        }

        private ZombieModeBossInstance FindZombieModeBossInstanceFor(CharacterMainControl boss, ZombieModeEnemyRuntimeMarker bossMarker = null)
        {
            if (boss == null && bossMarker == null) return null;
            for (int i = 0; i < runState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = runState.CurrentWaveBossInstances[i];
                if (instance == null)
                {
                    continue;
                }

                if ((boss != null && instance.Character == boss) ||
                    (bossMarker != null && instance.Marker == bossMarker))
                {
                    return instance;
                }
            }
            return null;
        }

        internal void HandleZombieModeBossDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            if (!IsZombieModeRunValid(runId) || marker == null || character == null || !marker.IsBoss)
            {
                return;
            }

            ZombieModeBossVisuals.PlayDeath(marker);
            if (marker.BossKind == ZombieModeBossKind.Splitter)
            {
                DealZombieModeExplosionAreaDamage(
                    runId,
                    character,
                    character.transform.position,
                    ZombieModeTuning.SplitterBossDeathRadius,
                    ZombieModeTuning.SplitterBossDeathDamage * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave));
                int count = ZombieModeTuning.SplitterBossDeathSpawnCount;
                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = Quaternion.Euler(0f, 360f * i / Mathf.Max(1, count), 0f) * Vector3.forward * 2f;
                    QueueZombieModeSmallSplitSpawn(runId, character.transform.position + offset);
                }
            }
            else if (marker.BossKind == ZombieModeBossKind.Corruptor)
            {
                SpawnZombieModeDeathCloud(runId, character, character.transform.position);
            }
            else if (marker.BossKind == ZombieModeBossKind.Titan)
            {
                DealZombieModeExplosionAreaDamage(
                    runId,
                    character,
                    character.transform.position,
                    ZombieModeTuning.TitanShockwaveRadius,
                    ZombieModeTuning.TitanShockwaveDamage * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave));
            }
        }

        private void SpawnZombieModeDeathCloud(int runId, CharacterMainControl source, Vector3 origin)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            // 共享 disk mesh 替代 CreatePrimitive(Cylinder)（审查 §3.3）。
            GameObject cloud = CreateZombieModeFlatZoneVisual(
                "ZombieMode_DeathCloud",
                origin + Vector3.up * 0.05f,
                ZombieModeTuning.CorruptorDeathCloudRadius,
                0.04f,
                new Color(0.62f, 0.42f, 0.82f, 0.50f));

            ZombieModeAreaTickRuntime runtime = cloud.AddComponent<ZombieModeAreaTickRuntime>();
            runtime.Initialize(
                runId,
                source,
                ZombieModeTuning.CorruptorDeathCloudRadius,
                ZombieModeTuning.CorruptorDeathCloudDurationSeconds,
                ZombieModeTuning.CorruptorDeathCloudDamagePerSecond * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave),
                ZombieModeTuning.CorruptorDeathCloudTickIntervalSeconds);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, cloud, runtime, null);
        }

        internal void DealZombieModeRuntimeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            DealZombieModeAreaDamageToPlayer(runId, origin, radius, damage);
        }

        internal void DealZombieModeRuntimeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            DealZombieModeAreaDamageToPlayer(runId, source, origin, radius, damage);
        }

        internal void TryApplyZombieModePlayerSlow(int runId, float percent, float duration)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            ZombieModePlayerSlowRuntime runtime = player.gameObject.GetComponent<ZombieModePlayerSlowRuntime>();
            if (runtime == null)
            {
                runtime = player.gameObject.AddComponent<ZombieModePlayerSlowRuntime>();
            }
            runtime.ApplySlow(runId, percent, duration);
        }
    }
}
