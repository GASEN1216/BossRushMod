using System.Collections;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
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

        private void RegisterZombieModeBossRuntime(int runId, CharacterMainControl boss, ZombieModeBossKind kind)
        {
            if (!IsZombieModeRunValid(runId) || boss == null)
            {
                return;
            }

            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = zombieModeRunState.CurrentWaveBossInstances[i];
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

        private void TickZombieModeBossController(float deltaTime)
        {
            if (!IsZombieModeActive ||
                zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat ||
                zombieModeRunState.CurrentWaveBossInstances.Count <= 0)
            {
                return;
            }

            float now = GetZombieModeRuntimeNow();
            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = zombieModeRunState.CurrentWaveBossInstances[i];
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

                if (now - instance.Lifecycle.LastReachableTime >= ZombieModeTuning.BossStuckTimeoutSeconds &&
                    now - instance.Lifecycle.LastHurtTime >= ZombieModeTuning.BossStuckTimeoutSeconds)
                {
                    TeleportZombieModeBossNearPlayer(instance);
                }

                // Tick state expirations (Titan DR, Hunter frenzy)
                ZombieModeTitanState titanState = instance.SkillState as ZombieModeTitanState;
                if (titanState != null && titanState.DamageReductionActive && now >= titanState.DamageReductionEndTime)
                {
                    titanState.DamageReductionActive = false;
                }

                ZombieModeHunterState hunterState = instance.SkillState as ZombieModeHunterState;
                if (hunterState != null && hunterState.FrenzyActive && now >= hunterState.FrenzyEndTime)
                {
                    hunterState.FrenzyActive = false;
                    RemoveZombieModeHunterFrenzyModifiers(hunterState);
                    if (instance.Character != null && hunterState.FrenzyOriginalScale > 0f)
                    {
                        Vector3 s = instance.Character.transform.localScale;
                        float ratio = hunterState.FrenzyOriginalScale / Mathf.Max(0.01f, s.x);
                        instance.Character.transform.localScale = s * ratio;
                    }
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

            instance.SkillState.Tick(this, instance, now);
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

        internal void TickZombieModeTitanState(ZombieModeTitanState titan, ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance != null ? instance.Character : null;
            if (boss == null) return;
            int runId = zombieModeRunState.RunId;

            if (now >= titan.NextShockwaveTime)
            {
                titan.NextShockwaveTime = now + ZombieModeTuning.TitanShockwaveCooldownSeconds;
                StartZombieModeTelegraphedAreaDamage(
                    runId,
                    boss,
                    boss.transform.position,
                    ZombieModeTuning.TitanShockwaveRadius,
                    ZombieModeTuning.TitanShockwaveDamage,
                    ZombieModeTuning.TitanShockwaveStartupSeconds,
                    L10n.T("BossRush_ZombieMode_BossSkill_TitanShockwave"));
            }
            if (now >= titan.NextDamageReductionTime && !titan.DamageReductionActive)
            {
                titan.NextDamageReductionTime = now + ZombieModeTuning.TitanDamageReductionCooldownSeconds;
                titan.DamageReductionActive = true;
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
            int runId = zombieModeRunState.RunId;

            if (now >= hunter.NextDashTime)
            {
                hunter.NextDashTime = now + ZombieModeTuning.HunterDashCooldownSeconds;
                boss.PopText(L10n.T("BossRush_ZombieMode_BossSkill_HunterDash"));
                Vector3 target = Vector3.MoveTowards(boss.transform.position, player.transform.position, ZombieModeTuning.HunterDashDistance);
                target.y = boss.transform.position.y;
                boss.transform.position = target;
                StartZombieModeTelegraphedAreaDamage(
                    runId,
                    boss,
                    player.transform.position,
                    ZombieModeTuning.HunterDashRadius,
                    ZombieModeTuning.HunterDashDamage,
                    ZombieModeTuning.HunterDashStartupSeconds,
                    L10n.T("BossRush_ZombieMode_BossSkill_HunterDash"));
            }
        }

        internal void TickZombieModeSplitterState(ZombieModeSplitterState splitter, ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance != null ? instance.Character : null;
            if (boss == null) return;
            int runId = zombieModeRunState.RunId;

            if (now >= splitter.NextSummonTime)
            {
                splitter.NextSummonTime = now + ZombieModeTuning.SplitterBossSummonCooldownSeconds;
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
            int runId = zombieModeRunState.RunId;

            if (now >= shielder.NextSelfShieldTime)
            {
                shielder.NextSelfShieldTime = now + ZombieModeTuning.ShielderSelfShieldCooldownSeconds;
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
                boss.PopText(L10n.T("BossRush_ZombieMode_BossSkill_ShielderGroupShield"));
                ApplyZombieModeBossShieldPulse(runId, boss.transform.position);
            }
        }

        internal void TickZombieModeCorruptorState(ZombieModeCorruptorState corruptor, ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance != null ? instance.Character : null;
            if (boss == null) return;
            CharacterMainControl player = CharacterMainControl.Main;
            int runId = zombieModeRunState.RunId;

            if (now >= corruptor.NextZoneTime && player != null)
            {
                corruptor.NextZoneTime = now + ZombieModeTuning.CorruptorZoneCooldownSeconds;
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
                new Color(0.45f, 0.10f, 0.65f, 0.50f));

            ZombieModeAreaTickRuntime runtime = zone.AddComponent<ZombieModeAreaTickRuntime>();
            runtime.Initialize(
                runId,
                source,
                ZombieModeTuning.CorruptorZoneRadius,
                ZombieModeTuning.CorruptorZoneStartupSeconds + ZombieModeTuning.CorruptorZoneDurationSeconds,
                ZombieModeTuning.CorruptorZoneDamagePerSecond,
                0.5f,
                ZombieModeTuning.CorruptorZoneSlowPercent);
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
                new Color(0.30f, 0.65f, 0.20f, 0.45f));

            ZombieModeAreaTickRuntime runtime = seg.AddComponent<ZombieModeAreaTickRuntime>();
            runtime.Initialize(
                runId,
                source,
                radius,
                ZombieModeTuning.CorruptorPoisonPathDurationSeconds,
                ZombieModeTuning.CorruptorPoisonPathDamagePerSecond,
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
                () => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat);
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
            StartZombieModeCoroutine(ProcessZombieModeSupportSpawnQueue(request.RunId), request.RunId);
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

        private void ClearZombieModeSupportSpawnQueue()
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

            CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, true);
            float radiusSqr = ZombieModeTuning.ShielderGroupShieldRadius * ZombieModeTuning.ShielderGroupShieldRadius;
            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
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

            zombieModeEnemyMarkerScratch.Clear();
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

            boss.transform.position = target;
            instance.Lifecycle.LastKnownPosition = target;
            instance.Lifecycle.LastReachableTime = GetZombieModeRuntimeNow();
            AICharacterController ai = boss.GetComponentInChildren<AICharacterController>();
            CharacterMainControl main = CharacterMainControl.Main;
            if (ai != null && main != null)
            {
                SetZombieModeEnemyTargetToMainPlayer(ai);
                ai.noticed = true;
            }
            DevLog("[ZombieMode] Boss stuck fallback teleport: " + instance.Kind.ToString());
        }

        private bool TryResolveZombieModeBossFallbackPosition(ZombieModeBossInstance instance, out Vector3 target)
        {
            target = Vector3.zero;
            if (instance == null || instance.Character == null)
            {
                return false;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 center = player != null ? player.transform.position : instance.Character.transform.position;
            if (SpawnPositionHelper.TryFindAroundPlayer(
                    center,
                    ringCount: 12,
                    radius: 16f,
                    resolved: out target,
                    liftOffset: ZombieModeTuning.NavMeshLiftOffset,
                    minPlayerDistance: 10f,
                    navMeshSampleRadius: ZombieModeTuning.NavMeshVirtualSpawnRadius))
            {
                return true;
            }

            if (TryGetNearestZombieModeMapSpawnPositionToPlayer(out target))
            {
                return true;
            }

            return SpawnPositionHelper.TrySampleNavMesh(
                center + Vector3.forward * 16f,
                out target,
                ZombieModeTuning.NavMeshLiftOffset,
                ZombieModeTuning.NavMeshVirtualSpawnRadius);
        }

        private void HandleZombieModeBossHurt(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl victim)
        {
            if (!IsZombieModeRunValid(runId) || marker == null || victim == null || !marker.IsBoss)
            {
                return;
            }

            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = zombieModeRunState.CurrentWaveBossInstances[i];
                if (instance == null || instance.Character != victim)
                {
                    continue;
                }

                instance.Marker = marker;
                instance.Lifecycle.LastHurtTime = GetZombieModeRuntimeNow();
                instance.Lifecycle.LastReachableTime = GetZombieModeRuntimeNow();
                instance.Lifecycle.LastKnownPosition = victim.transform.position;

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
            hunter.FrenzyActive = true;
            hunter.FrenzyEndTime = GetZombieModeRuntimeNow() + ZombieModeTuning.HunterFrenzyDurationSeconds;
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
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.MoveSpeed, ZombieModeTuning.HunterFrenzyMoveSpeedBonus, this, hunter.FrenzyModifierRecords, "Hunter Frenzy MoveSpeed");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.WalkSpeed, ZombieModeTuning.HunterFrenzyMoveSpeedBonus, this, hunter.FrenzyModifierRecords, "Hunter Frenzy WalkSpeed");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.RunSpeed, ZombieModeTuning.HunterFrenzyMoveSpeedBonus, this, hunter.FrenzyModifierRecords, "Hunter Frenzy RunSpeed");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.AttackSpeed, ZombieModeTuning.HunterFrenzyAttackSpeedBonus, this, hunter.FrenzyModifierRecords, "Hunter Frenzy AttackSpeed");
        }

        private void RemoveZombieModeHunterFrenzyModifiers(ZombieModeHunterState hunter)
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

        public float AbsorbZombieModeBossFinalDamage(CharacterMainControl boss, ZombieModeEnemyRuntimeMarker bossMarker, float finalDamage)
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

        public float ApplyZombieModeShielderAuraFinalDamageReduction(CharacterMainControl target, float finalDamage)
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
            if (target == null || zombieModeRunState.CurrentWaveBossInstances.Count <= 0) return false;

            float radiusSqr = ZombieModeTuning.ShielderAuraRadius * ZombieModeTuning.ShielderAuraRadius;
            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = zombieModeRunState.CurrentWaveBossInstances[i];
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

        public bool TryApplyZombieModeShielderAuraReductionPublic(CharacterMainControl target, ref float damageValue)
        {
            return TryApplyZombieModeShielderAuraReduction(target, ref damageValue);
        }

        private ZombieModeBossInstance FindZombieModeBossInstanceFor(CharacterMainControl boss, ZombieModeEnemyRuntimeMarker bossMarker = null)
        {
            if (boss == null && bossMarker == null) return null;
            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = zombieModeRunState.CurrentWaveBossInstances[i];
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

        private void HandleZombieModeBossDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            if (!IsZombieModeRunValid(runId) || marker == null || character == null || !marker.IsBoss)
            {
                return;
            }

            if (marker.BossKind == ZombieModeBossKind.Splitter)
            {
                DealZombieModeAreaDamageToPlayer(
                    runId,
                    character,
                    character.transform.position,
                    ZombieModeTuning.SplitterBossDeathRadius,
                    ZombieModeTuning.SplitterBossDeathDamage);
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
                DealZombieModeAreaDamageToPlayer(runId, character, character.transform.position, 6f, 60f);
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
                new Color(0.55f, 0.20f, 0.85f, 0.40f));

            ZombieModeAreaTickRuntime runtime = cloud.AddComponent<ZombieModeAreaTickRuntime>();
            runtime.Initialize(
                runId,
                source,
                ZombieModeTuning.CorruptorDeathCloudRadius,
                ZombieModeTuning.CorruptorDeathCloudDurationSeconds,
                ZombieModeTuning.CorruptorDeathCloudDamagePerSecond,
                ZombieModeTuning.CorruptorDeathCloudTickIntervalSeconds);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, cloud, runtime, null);
        }

        public void DealZombieModeRuntimeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            DealZombieModeAreaDamageToPlayer(runId, origin, radius, damage);
        }

        public void DealZombieModeRuntimeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            DealZombieModeAreaDamageToPlayer(runId, source, origin, radius, damage);
        }

        public void TryApplyZombieModePlayerSlow(int runId, float percent, float duration)
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

    public abstract class ZombieModeTimedRunScopedRuntime : MonoBehaviour
    {
        private int runtimeRunId;
        private float runtimeEndTime;
        private float runtimePauseStartTime = -1f;

        protected int RuntimeRunId
        {
            get { return runtimeRunId; }
        }

        protected void InitializeTimedRuntime(int newRunId, float duration)
        {
            runtimeRunId = newRunId;
            runtimeEndTime = Time.unscaledTime + Mathf.Max(0.05f, duration);
        }

        protected abstract void TickRuntime(ModBehaviour inst);

        protected virtual void OnRuntimeStopping(ModBehaviour inst, bool expired)
        {
        }

        protected virtual void OnRuntimeResumedAfterPause(ModBehaviour inst, float pausedDuration)
        {
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runtimeRunId)
            {
                OnRuntimeStopping(inst, false);
                Destroy(gameObject);
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                if (runtimePauseStartTime < 0f)
                {
                    runtimePauseStartTime = Time.unscaledTime;
                }
                return;
            }

            if (runtimePauseStartTime >= 0f)
            {
                float pausedDuration = Mathf.Max(0f, Time.unscaledTime - runtimePauseStartTime);
                runtimePauseStartTime = -1f;
                runtimeEndTime += pausedDuration;
                OnRuntimeResumedAfterPause(inst, pausedDuration);
            }

            if (Time.unscaledTime >= runtimeEndTime)
            {
                OnRuntimeStopping(inst, true);
                Destroy(gameObject);
                return;
            }

            TickRuntime(inst);
        }
    }

    /// <summary>
    /// 单一区域 tick + 区域伤害 runtime（审查 §2.2）。
    /// 之前 CorruptionZone / PoisonPath / DeathCloud 三类字段、TickRuntime 主体几乎逐字一致；
    /// 合并后 Initialize 接 slowPercent（默认 0），仅 Corruption 区域用减速。
    /// 任何对"区域 tick + 区域伤害"的微调（远端衰减、玩家进入预警）只需改一处。
    /// </summary>
    public sealed class ZombieModeAreaTickRuntime : ZombieModeTimedRunScopedRuntime
    {
        private CharacterMainControl source;
        private float radius;
        private float damagePerSecond;
        private float slowPercent;
        private float tickInterval;
        private float nextTickTime;

        public void Initialize(int newRunId, CharacterMainControl newSource, float newRadius, float duration, float dps, float tick, float slow = 0f)
        {
            source = newSource;
            radius = Mathf.Max(0.5f, newRadius);
            damagePerSecond = dps;
            slowPercent = Mathf.Max(0f, slow);
            tickInterval = Mathf.Max(0.1f, tick);
            nextTickTime = Time.unscaledTime + tickInterval;
            InitializeTimedRuntime(newRunId, duration);
        }

        protected override void OnRuntimeResumedAfterPause(ModBehaviour inst, float pausedDuration)
        {
            nextTickTime += pausedDuration;
        }

        protected override void TickRuntime(ModBehaviour inst)
        {
            if (Time.unscaledTime < nextTickTime)
            {
                return;
            }

            nextTickTime = Time.unscaledTime + tickInterval;
            float tickDamage = damagePerSecond * tickInterval;
            inst.DealZombieModeRuntimeAreaDamageToPlayer(RuntimeRunId, source, transform.position, radius, tickDamage);
            if (slowPercent > 0f)
            {
                inst.TryApplyZombieModePlayerSlow(RuntimeRunId, slowPercent, tickInterval * 2f);
            }
        }
    }

    public sealed class ZombieModeBossShieldRuntime : MonoBehaviour
    {
        private int runId;
        private float shieldRemaining;
        private float shieldEndTime;
        private bool shieldActive;

        public void ActivateShield(int newRunId, float amount, float duration)
        {
            runId = newRunId;
            shieldRemaining = Mathf.Max(shieldRemaining, amount);
            shieldEndTime = GetRuntimeNow() + duration;
            shieldActive = true;
        }

        public bool AbsorbDamage(ref float damageValue)
        {
            if (!shieldActive || shieldRemaining <= 0f)
            {
                return false;
            }

            if (GetRuntimeNow() >= shieldEndTime)
            {
                shieldActive = false;
                shieldRemaining = 0f;
                return false;
            }

            if (damageValue <= shieldRemaining)
            {
                shieldRemaining -= damageValue;
                damageValue = 0f;
            }
            else
            {
                damageValue -= shieldRemaining;
                shieldRemaining = 0f;
                shieldActive = false;
            }
            return true;
        }

        public float AbsorbDamage(float finalDamage)
        {
            if (!shieldActive || shieldRemaining <= 0f || finalDamage <= 0f)
            {
                return 0f;
            }

            if (GetRuntimeNow() >= shieldEndTime)
            {
                shieldActive = false;
                shieldRemaining = 0f;
                return 0f;
            }

            float absorbed = Mathf.Min(finalDamage, shieldRemaining);
            shieldRemaining -= absorbed;
            if (shieldRemaining <= 0f)
            {
                shieldRemaining = 0f;
                shieldActive = false;
            }

            return absorbed;
        }

        public bool IsShieldActive()
        {
            return shieldActive && GetRuntimeNow() < shieldEndTime && shieldRemaining > 0f;
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                shieldActive = false;
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            if (shieldActive && inst.GetZombieModeRuntimeNow() >= shieldEndTime)
            {
                shieldActive = false;
                shieldRemaining = 0f;
            }
        }

        private float GetRuntimeNow()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            return inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
        }
    }

    public sealed class ZombieModePlayerSlowRuntime : MonoBehaviour
    {
        private int runId;
        private float slowEndTime;
        private float currentSlowPercent;
        private bool slowActive;
        private readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> slowModifierRecords = new System.Collections.Generic.List<ZombieModeAttributeModifierRecord>();

        public void ApplySlow(int newRunId, float percent, float duration)
        {
            float now = GetRuntimeNow();
            if (slowActive && now >= slowEndTime)
            {
                ClearSlowState();
            }

            runId = newRunId;
            float endCandidate = now + duration;
            if (percent > currentSlowPercent || endCandidate > slowEndTime)
            {
                float newPercent = Mathf.Max(currentSlowPercent, percent);
                slowEndTime = Mathf.Max(slowEndTime, endCandidate);
                slowActive = true;
                if (!Mathf.Approximately(newPercent, currentSlowPercent))
                {
                    currentSlowPercent = newPercent;
                    ReapplySlowModifiers();
                }
                else
                {
                    currentSlowPercent = newPercent;
                    if (slowModifierRecords.Count <= 0)
                    {
                        ReapplySlowModifiers();
                    }
                }
            }
        }

        public float GetCurrentSlowPercent()
        {
            if (!slowActive || GetRuntimeNow() >= slowEndTime)
            {
                return 0f;
            }
            return currentSlowPercent;
        }

        private void Update()
        {
            if (!slowActive) return;
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                ClearSlowState();
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            if (inst.GetZombieModeRuntimeNow() >= slowEndTime)
            {
                ClearSlowState();
            }
        }

        private void OnDisable()
        {
            RemoveSlowModifiers();
        }

        private void OnDestroy()
        {
            RemoveSlowModifiers();
        }

        private void ClearSlowState()
        {
            slowActive = false;
            currentSlowPercent = 0f;
            slowEndTime = 0f;
            RemoveSlowModifiers();
        }

        private void ReapplySlowModifiers()
        {
            RemoveSlowModifiers();
            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (character == null)
            {
                return;
            }

            // 收口到 ZombieModeStatNames（审查 §2.3）。
            AddSlowModifier(character, ZombieModeStatNames.MoveSpeed);
            AddSlowModifier(character, ZombieModeStatNames.WalkSpeed);
            AddSlowModifier(character, ZombieModeStatNames.RunSpeed);
        }

        private void AddSlowModifier(CharacterMainControl character, string statName)
        {
            if (character == null || currentSlowPercent <= 0f)
            {
                return;
            }

            // 用 PercentageAdd 而非 Add（审查 §3.2）：传 -percent 等价于减速 percent。
            // 之前 Add(stat.BaseValue * -percent) 在玩家叠了 +50% MoveSpeed 装备时，
            // 50% 减速实际只削掉基础速度的 50%，叠加值不动 → 减速被装备稀释。
            RuntimeStatModifierTracker.TryAdd(
                character,
                statName,
                -currentSlowPercent,
                this,
                slowModifierRecords,
                "Player Slow " + statName);
        }

        private void RemoveSlowModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(slowModifierRecords, "Player Slow");
        }

        private float GetRuntimeNow()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            return inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
        }
    }
}
