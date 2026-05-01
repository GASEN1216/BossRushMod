using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
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
                instance.LastKnownPosition = boss.transform.position;
                instance.LastReachableTime = Time.unscaledTime;
                instance.LastHurtTime = Time.unscaledTime;
                instance.RuntimeRegistered = true;
                instance.NextSkillTime = Time.unscaledTime + UnityEngine.Random.Range(2f, 5f);
                instance.SkillSequence = 0;
                float now = Time.unscaledTime;
                instance.NextTitanShockwaveTime = now + UnityEngine.Random.Range(2f, 5f);
                instance.NextTitanDamageReductionTime = now + UnityEngine.Random.Range(8f, 12f);
                instance.NextHunterDashTime = now + UnityEngine.Random.Range(2f, 4f);
                instance.NextSplitterSummonTime = now + UnityEngine.Random.Range(3f, 6f);
                instance.NextShielderSelfShieldTime = now + UnityEngine.Random.Range(4f, 8f);
                instance.NextShielderGroupShieldTime = now + UnityEngine.Random.Range(10f, 14f);
                instance.NextCorruptorZoneTime = now + UnityEngine.Random.Range(2f, 5f);
                instance.NextCorruptorPoisonPathTime = now + UnityEngine.Random.Range(1f, 3f);
                instance.HunterFrenzyOriginalScale = boss.transform.localScale.x;
                break;
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

            float now = Time.unscaledTime;
            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = zombieModeRunState.CurrentWaveBossInstances[i];
                if (instance == null || !instance.Alive || instance.Character == null)
                {
                    continue;
                }

                Vector3 current = instance.Character.transform.position;
                if ((current - instance.LastKnownPosition).sqrMagnitude > 1f)
                {
                    instance.LastKnownPosition = current;
                    instance.LastReachableTime = now;
                }

                if (now - instance.LastReachableTime >= ZombieModeTuning.BossStuckTimeoutSeconds &&
                    now - instance.LastHurtTime >= ZombieModeTuning.BossStuckTimeoutSeconds)
                {
                    TeleportZombieModeBossNearPlayer(instance);
                }

                // Tick state expirations (Titan DR, Hunter frenzy)
                if (instance.TitanDamageReductionActive && now >= instance.TitanDamageReductionEndTime)
                {
                    instance.TitanDamageReductionActive = false;
                }

                if (instance.HunterFrenzyActive && now >= instance.HunterFrenzyEndTime)
                {
                    instance.HunterFrenzyActive = false;
                    if (instance.Character != null && instance.HunterFrenzyOriginalScale > 0f)
                    {
                        Vector3 s = instance.Character.transform.localScale;
                        float ratio = instance.HunterFrenzyOriginalScale / Mathf.Max(0.01f, s.x);
                        instance.Character.transform.localScale = s * ratio;
                    }
                }

                TryExecuteZombieModeBossSkill(instance);
            }
        }

        private float GetZombieModeBossSkillCooldown(ZombieModeBossKind kind)
        {
            switch (kind)
            {
                case ZombieModeBossKind.Titan:
                    return ZombieModeTuning.TitanShockwaveCooldownSeconds;
                case ZombieModeBossKind.Hunter:
                    return ZombieModeTuning.HunterDashCooldownSeconds;
                case ZombieModeBossKind.Splitter:
                    return ZombieModeTuning.SplitterBossSummonCooldownSeconds;
                case ZombieModeBossKind.Shielder:
                    return ZombieModeTuning.ShielderSelfShieldCooldownSeconds;
                default:
                    return ZombieModeTuning.CorruptorZoneCooldownSeconds;
            }
        }

        private void TryExecuteZombieModeBossSkill(ZombieModeBossInstance instance)
        {
            if (instance == null)
            {
                return;
            }

            TickZombieModeBossSkillRotation(instance, Time.unscaledTime);
        }

        private void TickZombieModeBossSkillRotation(ZombieModeBossInstance instance, float now)
        {
            CharacterMainControl boss = instance.Character;
            CharacterMainControl player = CharacterMainControl.Main;
            if (boss == null || player == null)
            {
                return;
            }

            int runId = zombieModeRunState.RunId;
            switch (instance.Kind)
            {
                case ZombieModeBossKind.Titan:
                    if (now >= instance.NextTitanShockwaveTime)
                    {
                        instance.NextTitanShockwaveTime = now + ZombieModeTuning.TitanShockwaveCooldownSeconds;
                        StartZombieModeTelegraphedAreaDamage(
                            runId,
                            boss,
                            boss.transform.position,
                            ZombieModeTuning.TitanShockwaveRadius,
                            ZombieModeTuning.TitanShockwaveDamage,
                            ZombieModeTuning.TitanShockwaveStartupSeconds,
                            L10n.T("BossRush_ZombieMode_Boss_Titan"));
                    }
                    if (now >= instance.NextTitanDamageReductionTime && !instance.TitanDamageReductionActive)
                    {
                        instance.NextTitanDamageReductionTime = now + ZombieModeTuning.TitanDamageReductionCooldownSeconds;
                        instance.TitanDamageReductionActive = true;
                        instance.TitanDamageReductionEndTime = now
                            + ZombieModeTuning.TitanDamageReductionStartupSeconds
                            + ZombieModeTuning.TitanDamageReductionDurationSeconds;
                        boss.PopText(L10n.T("BossRush_ZombieMode_Boss_Titan"));
                    }
                    break;

                case ZombieModeBossKind.Hunter:
                    if (now >= instance.NextHunterDashTime)
                    {
                        instance.NextHunterDashTime = now + ZombieModeTuning.HunterDashCooldownSeconds;
                        boss.PopText(L10n.T("BossRush_ZombieMode_Boss_Hunter"));
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
                            L10n.T("BossRush_ZombieMode_Boss_Hunter"));
                    }
                    break;

                case ZombieModeBossKind.Splitter:
                    if (now >= instance.NextSplitterSummonTime &&
                        zombieModeRunState.PerformanceTier < ZombieModePerformanceTier.SoftProtect)
                    {
                        instance.NextSplitterSummonTime = now + ZombieModeTuning.SplitterBossSummonCooldownSeconds;
                        boss.PopText(L10n.T("BossRush_ZombieMode_Boss_Splitter"));
                        for (int i = 0; i < ZombieModeTuning.SplitterBossSummonCount; i++)
                        {
                            Vector3 offset = Quaternion.Euler(0f, 360f * i / ZombieModeTuning.SplitterBossSummonCount, 0f) * Vector3.forward * 2f;
                            SpawnZombieModeSplitterChildAsync(runId, boss.transform.position + offset, ZombieModeTuning.SplitterBossSummonScale).Forget();
                        }
                    }
                    break;

                case ZombieModeBossKind.Shielder:
                    if (now >= instance.NextShielderSelfShieldTime)
                    {
                        instance.NextShielderSelfShieldTime = now + ZombieModeTuning.ShielderSelfShieldCooldownSeconds;
                        boss.PopText(L10n.T("BossRush_ZombieMode_Boss_Shielder"));
                        if (boss.Health != null)
                        {
                            float amount = boss.Health.MaxHealth * ZombieModeTuning.ShielderSelfShieldPercent;
                            ZombieModeBossShieldRuntime shield = boss.gameObject.GetComponent<ZombieModeBossShieldRuntime>();
                            if (shield == null)
                            {
                                shield = boss.gameObject.AddComponent<ZombieModeBossShieldRuntime>();
                            }
                            shield.ActivateShield(runId, amount, ZombieModeTuning.ShielderSelfShieldDurationSeconds);
                        }
                    }
                    if (now >= instance.NextShielderGroupShieldTime)
                    {
                        instance.NextShielderGroupShieldTime = now + ZombieModeTuning.ShielderGroupShieldCooldownSeconds;
                        ApplyZombieModeBossShieldPulse(runId, boss.transform.position);
                    }
                    break;

                case ZombieModeBossKind.Corruptor:
                    if (now >= instance.NextCorruptorZoneTime)
                    {
                        instance.NextCorruptorZoneTime = now + ZombieModeTuning.CorruptorZoneCooldownSeconds;
                        SpawnZombieModeCorruptionZone(runId, player.transform.position);
                        boss.PopText(L10n.T("BossRush_ZombieMode_Boss_Corruptor"));
                    }
                    if (now >= instance.NextCorruptorPoisonPathTime)
                    {
                        instance.NextCorruptorPoisonPathTime = now + ZombieModeTuning.CorruptorPoisonPathTickIntervalSeconds;
                        SpawnZombieModePoisonPathSegment(runId, boss.transform.position);
                    }
                    break;
            }
        }

        private void SpawnZombieModeCorruptionZone(int runId, Vector3 origin)
        {
            if (!IsZombieModeRunValid(runId)) return;

            GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            zone.name = "ZombieMode_CorruptionZone";
            zone.transform.position = origin + Vector3.up * 0.04f;
            zone.transform.localScale = new Vector3(
                ZombieModeTuning.CorruptorZoneRadius * 2f,
                0.04f,
                ZombieModeTuning.CorruptorZoneRadius * 2f);
            Collider c = zone.GetComponent<Collider>();
            if (c != null) Destroy(c);
            try
            {
                Renderer r = zone.GetComponent<Renderer>();
                if (r != null) r.material.color = new Color(0.45f, 0.10f, 0.65f, 0.50f);
            }
            catch { }

            ZombieModeCorruptionZoneRuntime runtime = zone.AddComponent<ZombieModeCorruptionZoneRuntime>();
            runtime.Initialize(
                runId,
                ZombieModeTuning.CorruptorZoneRadius,
                ZombieModeTuning.CorruptorZoneStartupSeconds + ZombieModeTuning.CorruptorZoneDurationSeconds,
                ZombieModeTuning.CorruptorZoneDamagePerSecond,
                ZombieModeTuning.CorruptorZoneSlowPercent,
                0.5f);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, zone, runtime, null);
        }

        private void SpawnZombieModePoisonPathSegment(int runId, Vector3 origin)
        {
            if (!IsZombieModeRunValid(runId)) return;

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            seg.name = "ZombieMode_PoisonPath";
            seg.transform.position = origin + Vector3.up * 0.03f;
            float radius = ZombieModeTuning.CorruptorPoisonPathWidth * 0.5f;
            seg.transform.localScale = new Vector3(radius * 2f, 0.03f, radius * 2f);
            Collider c = seg.GetComponent<Collider>();
            if (c != null) Destroy(c);
            try
            {
                Renderer r = seg.GetComponent<Renderer>();
                if (r != null) r.material.color = new Color(0.30f, 0.65f, 0.20f, 0.45f);
            }
            catch { }

            ZombieModePoisonPathRuntime runtime = seg.AddComponent<ZombieModePoisonPathRuntime>();
            runtime.Initialize(
                runId,
                radius,
                ZombieModeTuning.CorruptorPoisonPathDurationSeconds,
                ZombieModeTuning.CorruptorPoisonPathDamagePerSecond,
                0.5f);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, seg, runtime, null);
        }

        private async UniTask SpawnZombieModeSplitterChildAsync(int runId, Vector3 position, float scale)
        {
            CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(runId, position, ZombieModeEnemyKind.Normal, true);
            if (zombie != null)
            {
                zombie.transform.localScale = zombie.transform.localScale * scale;
            }
        }

        private void ApplyZombieModeBossShieldPulse(int runId, Vector3 origin)
        {
            ApplyZombieModeShielderGroupShield(runId, origin);
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

                CharacterMainControl ch = marker.GetComponent<CharacterMainControl>();
                if (ch == null || ch.Health == null || ch.Health.CurrentHealth <= 0f) continue;

                float amount = ch.Health.MaxHealth * ZombieModeTuning.ShielderGroupShieldPercent;
                ZombieModeBossShieldRuntime shield = ch.gameObject.GetComponent<ZombieModeBossShieldRuntime>();
                if (shield == null)
                {
                    shield = ch.gameObject.AddComponent<ZombieModeBossShieldRuntime>();
                }
                shield.ActivateShield(runId, amount, ZombieModeTuning.ShielderGroupShieldDurationSeconds);
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

            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 center = player != null ? player.transform.position : instance.Character.transform.position;
            Vector3 offset = Random.insideUnitSphere;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.01f)
            {
                offset = Vector3.forward;
            }

            Vector3 target = center + offset.normalized * 16f + Vector3.up * ZombieModeTuning.NavMeshLiftOffset;
            instance.Character.transform.position = target;
            instance.LastKnownPosition = target;
            instance.LastReachableTime = Time.unscaledTime;
            DevLog("[ZombieMode] Boss stuck fallback teleport: " + instance.Kind.ToString());
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

                instance.LastHurtTime = Time.unscaledTime;
                instance.LastReachableTime = Time.unscaledTime;
                instance.LastKnownPosition = victim.transform.position;

                // Hunter low-HP frenzy trigger
                if (instance.Kind == ZombieModeBossKind.Hunter &&
                    !instance.HunterFrenzyActive &&
                    victim.Health != null &&
                    victim.Health.MaxHealth > 0f &&
                    victim.Health.CurrentHealth / victim.Health.MaxHealth <= ZombieModeTuning.HunterFrenzyHpThreshold)
                {
                    ActivateZombieModeHunterFrenzy(instance);
                }

                // Splitter HP-threshold split
                if (instance.Kind == ZombieModeBossKind.Splitter && victim.Health != null && victim.Health.MaxHealth > 0f)
                {
                    float ratio = victim.Health.CurrentHealth / victim.Health.MaxHealth;
                    if (!instance.SplitterFirstSplitTriggered && ratio <= ZombieModeTuning.SplitterBossSplitFirstHpThreshold)
                    {
                        instance.SplitterFirstSplitTriggered = true;
                        TriggerZombieModeSplitterHpSplit(runId, victim);
                    }
                    if (!instance.SplitterSecondSplitTriggered && ratio <= ZombieModeTuning.SplitterBossSplitSecondHpThreshold)
                    {
                        instance.SplitterSecondSplitTriggered = true;
                        TriggerZombieModeSplitterHpSplit(runId, victim);
                    }
                }
                break;
            }
        }

        private void ActivateZombieModeHunterFrenzy(ZombieModeBossInstance instance)
        {
            if (instance == null || instance.Character == null) return;
            instance.HunterFrenzyActive = true;
            instance.HunterFrenzyEndTime = Time.unscaledTime + ZombieModeTuning.HunterFrenzyDurationSeconds;
            // Visual feedback: scale up slightly via the existing speed-multiplier helper
            instance.Character.PopText(L10n.T("BossRush_ZombieMode_Boss_Hunter"));
            ApplyZombieModeAiSpeedMultiplier(instance.Character, 1f + ZombieModeTuning.HunterFrenzyMoveSpeedBonus);
        }

        private void TriggerZombieModeSplitterHpSplit(int runId, CharacterMainControl victim)
        {
            if (!IsZombieModeRunValid(runId) || victim == null) return;
            for (int i = 0; i < ZombieModeTuning.SplitterBossSplitCount; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 360f * i / ZombieModeTuning.SplitterBossSplitCount, 0f) * Vector3.forward * 2f;
                SpawnZombieModeSplitterChildAsync(runId, victim.transform.position + offset, ZombieModeTuning.SplitterBossSplitChildScale).Forget();
            }
        }

        public bool TryAbsorbZombieModeBossDamage(CharacterMainControl boss, ref float damageValue)
        {
            if (boss == null) return false;

            // Boss self-shield (Shielder/Shielder group/Titan-shielded-via-runtime)
            ZombieModeBossShieldRuntime shield = boss.gameObject.GetComponent<ZombieModeBossShieldRuntime>();
            if (shield != null && shield.IsShieldActive())
            {
                shield.AbsorbDamage(ref damageValue);
                if (damageValue <= 0f) return true;
            }

            // Titan damage reduction self-buff
            ZombieModeBossInstance titan = FindZombieModeBossInstanceFor(boss);
            if (titan != null &&
                titan.Kind == ZombieModeBossKind.Titan &&
                titan.TitanDamageReductionActive)
            {
                damageValue *= (1f - ZombieModeTuning.TitanDamageReductionPercent);
                return true;
            }

            // Shielder aura damage reduction (15% within 6m of any alive Shielder boss)
            if (TryApplyZombieModeShielderAuraReduction(boss, ref damageValue))
            {
                return true;
            }

            return false;
        }

        public float AbsorbZombieModeBossFinalDamage(CharacterMainControl boss, float finalDamage)
        {
            if (boss == null || finalDamage <= 0f)
            {
                return 0f;
            }

            float damageAfterAbsorb = finalDamage;
            bool changed = false;

            ZombieModeBossShieldRuntime shield = boss.gameObject.GetComponent<ZombieModeBossShieldRuntime>();
            if (shield != null && shield.IsShieldActive())
            {
                float absorbed = shield.AbsorbDamage(finalDamage);
                if (absorbed > 0f)
                {
                    damageAfterAbsorb = Mathf.Max(0f, damageAfterAbsorb - absorbed);
                    changed = true;
                }
            }

            ZombieModeBossInstance titan = FindZombieModeBossInstanceFor(boss);
            if (titan != null &&
                titan.Kind == ZombieModeBossKind.Titan &&
                titan.TitanDamageReductionActive)
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
                if (instance == null || !instance.Alive || instance.Character == null) continue;
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

        private ZombieModeBossInstance FindZombieModeBossInstanceFor(CharacterMainControl boss)
        {
            if (boss == null) return null;
            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = zombieModeRunState.CurrentWaveBossInstances[i];
                if (instance != null && instance.Character == boss)
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
                    character.transform.position,
                    ZombieModeTuning.SplitterBossDeathRadius,
                    ZombieModeTuning.SplitterBossDeathDamage);
                int count = zombieModeRunState.PerformanceTier >= ZombieModePerformanceTier.SoftProtect
                    ? ZombieModeTuning.SplitterBossDeathSpawnCountSoftProtect
                    : ZombieModeTuning.SplitterBossDeathSpawnCount;
                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = Quaternion.Euler(0f, 360f * i / Mathf.Max(1, count), 0f) * Vector3.forward * 2f;
                    SpawnZombieModeSmallSplitAsync(runId, character.transform.position + offset).Forget();
                }
            }
            else if (marker.BossKind == ZombieModeBossKind.Corruptor)
            {
                SpawnZombieModeDeathCloud(runId, character.transform.position);
            }
            else if (marker.BossKind == ZombieModeBossKind.Titan)
            {
                DealZombieModeAreaDamageToPlayer(runId, character.transform.position, 6f, 60f);
            }
        }

        private void SpawnZombieModeDeathCloud(int runId, Vector3 origin)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            GameObject cloud = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cloud.name = "ZombieMode_DeathCloud";
            cloud.transform.position = origin + Vector3.up * 0.05f;
            cloud.transform.localScale = new Vector3(
                ZombieModeTuning.CorruptorDeathCloudRadius * 2f,
                0.04f,
                ZombieModeTuning.CorruptorDeathCloudRadius * 2f);
            Collider c = cloud.GetComponent<Collider>();
            if (c != null) Destroy(c);
            try
            {
                Renderer r = cloud.GetComponent<Renderer>();
                if (r != null)
                {
                    r.material.color = new Color(0.55f, 0.20f, 0.85f, 0.40f);
                }
            }
            catch { }

            ZombieModeDeathCloudRuntime runtime = cloud.AddComponent<ZombieModeDeathCloudRuntime>();
            runtime.Initialize(
                runId,
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

    public sealed class ZombieModeCorruptionZoneRuntime : MonoBehaviour
    {
        private int runId;
        private float radius;
        private float endTime;
        private float damagePerSecond;
        private float slowPercent;
        private float tickInterval;
        private float nextTickTime;

        public void Initialize(int newRunId, float newRadius, float duration, float dps, float slow, float tick)
        {
            runId = newRunId;
            radius = newRadius;
            damagePerSecond = dps;
            slowPercent = slow;
            tickInterval = Mathf.Max(0.1f, tick);
            endTime = Time.unscaledTime + duration;
            nextTickTime = Time.unscaledTime + tickInterval;
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.unscaledTime >= endTime)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.unscaledTime < nextTickTime)
            {
                return;
            }

            nextTickTime = Time.unscaledTime + tickInterval;
            float tickDamage = damagePerSecond * tickInterval;
            inst.DealZombieModeRuntimeAreaDamageToPlayer(runId, transform.position, radius, tickDamage);
            if (slowPercent > 0f)
            {
                inst.TryApplyZombieModePlayerSlow(runId, slowPercent, tickInterval * 2f);
            }
        }
    }

    public sealed class ZombieModePoisonPathRuntime : MonoBehaviour
    {
        private int runId;
        private float radius;
        private float endTime;
        private float damagePerSecond;
        private float tickInterval;
        private float nextTickTime;

        public void Initialize(int newRunId, float newRadius, float duration, float dps, float tick)
        {
            runId = newRunId;
            radius = Mathf.Max(0.5f, newRadius);
            damagePerSecond = dps;
            tickInterval = Mathf.Max(0.1f, tick);
            endTime = Time.unscaledTime + duration;
            nextTickTime = Time.unscaledTime + tickInterval;
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.unscaledTime >= endTime)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.unscaledTime < nextTickTime)
            {
                return;
            }

            nextTickTime = Time.unscaledTime + tickInterval;
            float tickDamage = damagePerSecond * tickInterval;
            inst.DealZombieModeRuntimeAreaDamageToPlayer(runId, transform.position, radius, tickDamage);
        }
    }

    public sealed class ZombieModeDeathCloudRuntime : MonoBehaviour
    {
        private int runId;
        private float radius;
        private float endTime;
        private float damagePerSecond;
        private float tickInterval;
        private float nextTickTime;

        public void Initialize(int newRunId, float newRadius, float duration, float dps, float tick)
        {
            runId = newRunId;
            radius = newRadius;
            damagePerSecond = dps;
            tickInterval = Mathf.Max(0.1f, tick);
            endTime = Time.unscaledTime + duration;
            nextTickTime = Time.unscaledTime + tickInterval;
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.unscaledTime >= endTime)
            {
                Destroy(gameObject);
                return;
            }

            if (Time.unscaledTime < nextTickTime)
            {
                return;
            }

            nextTickTime = Time.unscaledTime + tickInterval;
            float tickDamage = damagePerSecond * tickInterval;
            inst.DealZombieModeRuntimeAreaDamageToPlayer(runId, transform.position, radius, tickDamage);
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
            shieldEndTime = Time.unscaledTime + duration;
            shieldActive = true;
        }

        public bool AbsorbDamage(ref float damageValue)
        {
            if (!shieldActive || shieldRemaining <= 0f)
            {
                return false;
            }

            if (Time.unscaledTime >= shieldEndTime)
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

            if (Time.unscaledTime >= shieldEndTime)
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
            return shieldActive && Time.unscaledTime < shieldEndTime && shieldRemaining > 0f;
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                shieldActive = false;
                return;
            }

            if (shieldActive && Time.unscaledTime >= shieldEndTime)
            {
                shieldActive = false;
                shieldRemaining = 0f;
            }
        }
    }

    public sealed class ZombieModePlayerSlowRuntime : MonoBehaviour
    {
        private int runId;
        private float slowEndTime;
        private float currentSlowPercent;
        private bool slowActive;

        public void ApplySlow(int newRunId, float percent, float duration)
        {
            runId = newRunId;
            float endCandidate = Time.unscaledTime + duration;
            if (percent > currentSlowPercent || endCandidate > slowEndTime)
            {
                currentSlowPercent = Mathf.Max(currentSlowPercent, percent);
                slowEndTime = Mathf.Max(slowEndTime, endCandidate);
                slowActive = true;
            }
        }

        public float GetCurrentSlowPercent()
        {
            if (!slowActive || Time.unscaledTime >= slowEndTime)
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
                slowActive = false;
                currentSlowPercent = 0f;
                return;
            }

            if (Time.unscaledTime >= slowEndTime)
            {
                slowActive = false;
                currentSlowPercent = 0f;
            }
        }
    }
}
