// ============================================================================
// ZombieModePollution_RuntimeComponents.cs - standalone runtime components
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public sealed class ZombieModeSprinterDashRuntime : ZombieModeTimedRunScopedRuntime
    {
        private CharacterMainControl source;
        private Vector3 targetPosition;
        private Vector3 dashDirection;
        private float dashDistance;
        private float startupEndTime;
        private float dashDuration;
        private float dashEndTime;
        private bool dashStarted;
        private bool stopped;

        public void Initialize(
            int newRunId,
            CharacterMainControl newSource,
            Vector3 newTargetPosition,
            float newDashDistance,
            float startupSeconds,
            float newDashDuration)
        {
            source = newSource;
            targetPosition = newTargetPosition;
            dashDistance = Mathf.Max(0.5f, newDashDistance);
            dashDuration = Mathf.Max(0.05f, newDashDuration);
            startupEndTime = Time.unscaledTime + Mathf.Max(0.05f, startupSeconds);
            dashEndTime = 0f;
            dashStarted = false;
            stopped = false;
            InitializeTimedRuntime(newRunId, Mathf.Max(0.05f, startupSeconds) + dashDuration + 0.1f);
        }

        protected override void TickRuntime(ModBehaviour inst)
        {
            if (ShouldCancelDash())
            {
                StopDashVelocity();
                Destroy(gameObject);
                return;
            }

            RefreshTelegraphPosition();
            if (!dashStarted)
            {
                if (Time.unscaledTime < startupEndTime)
                {
                    return;
                }

                StartDash();
            }

            if (Time.unscaledTime >= dashEndTime)
            {
                StopDashVelocity();
                Destroy(gameObject);
                return;
            }

            source.SetForceMoveVelocity(dashDirection * (dashDistance / dashDuration));
        }

        protected override void OnRuntimeStopping(ModBehaviour inst, bool expired)
        {
            StopDashVelocity();
        }

        private void OnDestroy()
        {
            StopDashVelocity();
        }

        protected override void OnRuntimeResumedAfterPause(ModBehaviour inst, float pausedDuration)
        {
            startupEndTime += pausedDuration;
            if (dashStarted)
            {
                dashEndTime += pausedDuration;
            }
        }

        private void StartDash()
        {
            dashDirection = targetPosition - source.transform.position;
            dashDirection.y = 0f;
            if (dashDirection.sqrMagnitude <= 0.0001f)
            {
                dashDirection = source.transform.forward;
                dashDirection.y = 0f;
            }

            dashDirection = dashDirection.sqrMagnitude > 0.0001f ? dashDirection.normalized : Vector3.forward;
            if (dashDirection.sqrMagnitude > 0.0001f)
            {
                source.transform.rotation = Quaternion.LookRotation(dashDirection, Vector3.up);
            }

            dashStarted = true;
            dashEndTime = Time.unscaledTime + dashDuration;
        }

        private void RefreshTelegraphPosition()
        {
            if (source == null || source.transform == null)
            {
                return;
            }

            transform.position = source.transform.position + Vector3.up * 0.035f;
        }

        private bool ShouldCancelDash()
        {
            if (source == null || source.Health == null)
            {
                return true;
            }

            ZombieModeEnemyRuntimeMarker marker = source.GetComponent<ZombieModeEnemyRuntimeMarker>();
            if (marker != null && (marker.DeathSettled || marker.RemovedFromRuntime))
            {
                return true;
            }

            return source.Health.CurrentHealth <= 0f;
        }

        private void StopDashVelocity()
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            if (dashStarted && source != null)
            {
                source.SetForceMoveVelocity(Vector3.zero);
            }
        }
    }

    public sealed class ZombieModeTelegraphedAreaDamageRuntime : ZombieModeTimedRunScopedRuntime
    {
        private CharacterMainControl source;
        private Vector3 origin;
        private float radius;
        private float damage;
        private float triggerTime;
        private bool triggered;
        private bool followSourcePosition;

        public void Initialize(
            int newRunId,
            CharacterMainControl newSource,
            Vector3 newOrigin,
            float newRadius,
            float newDamage,
            float delay,
            bool newFollowSourcePosition = false)
        {
            source = newSource;
            origin = newOrigin;
            radius = newRadius;
            damage = newDamage;
            followSourcePosition = newFollowSourcePosition;
            triggerTime = Time.unscaledTime + Mathf.Max(0.05f, delay);
            triggered = false;
            InitializeTimedRuntime(newRunId, Mathf.Max(0.05f, delay) + 0.1f);
        }

        protected override void TickRuntime(ModBehaviour inst)
        {
            if (ShouldCancelFollowSourceRuntime())
            {
                Destroy(gameObject);
                return;
            }

            RefreshFollowSourceOrigin();
            if (triggered || Time.unscaledTime < triggerTime)
            {
                return;
            }

            triggered = true;
            inst.TryExecuteZombieModeTelegraphedAreaDamage(RuntimeRunId, source, origin, radius, damage);
            Destroy(gameObject);
        }

        private void RefreshFollowSourceOrigin()
        {
            if (!followSourcePosition || source == null || source.transform == null)
            {
                return;
            }

            origin = source.transform.position;
            transform.position = origin + Vector3.up * 0.03f;
        }

        private bool ShouldCancelFollowSourceRuntime()
        {
            if (!followSourcePosition)
            {
                return false;
            }

            if (source == null || source.Health == null)
            {
                return true;
            }

            ZombieModeEnemyRuntimeMarker marker = source.GetComponent<ZombieModeEnemyRuntimeMarker>();
            if (marker != null && (marker.DeathSettled || marker.RemovedFromRuntime))
            {
                return true;
            }

            return source.Health.CurrentHealth <= 0f;
        }

        protected override void OnRuntimeResumedAfterPause(ModBehaviour inst, float pausedDuration)
        {
            triggerTime += pausedDuration;
        }
    }

    public sealed class ZombieModeTelegraphedPlayerSlowRuntime : ZombieModeTimedRunScopedRuntime
    {
        private Vector3 origin;
        private float radius;
        private float slowPercent;
        private float slowDuration;
        private float triggerTime;
        private bool triggered;

        public void Initialize(
            int newRunId,
            Vector3 newOrigin,
            float newRadius,
            float newSlowPercent,
            float newSlowDuration,
            float delay)
        {
            origin = newOrigin;
            radius = Mathf.Max(0.5f, newRadius);
            slowPercent = Mathf.Clamp01(newSlowPercent);
            slowDuration = Mathf.Max(0.05f, newSlowDuration);
            triggerTime = Time.unscaledTime + Mathf.Max(0.05f, delay);
            triggered = false;
            InitializeTimedRuntime(newRunId, Mathf.Max(0.05f, delay) + 0.1f);
        }

        protected override void TickRuntime(ModBehaviour inst)
        {
            if (triggered || Time.unscaledTime < triggerTime)
            {
                return;
            }

            triggered = true;
            inst.TryApplyZombieModePlayerSlowInArea(RuntimeRunId, origin, radius, slowPercent, slowDuration);
            Destroy(gameObject);
        }

        protected override void OnRuntimeResumedAfterPause(ModBehaviour inst, float pausedDuration)
        {
            triggerTime += pausedDuration;
        }
    }

    public sealed class ZombieModeTelegraphedDamageCloudRuntime : ZombieModeTimedRunScopedRuntime
    {
        private CharacterMainControl source;
        private Vector3 origin;
        private float radius;
        private float duration;
        private float damagePerSecond;
        private float tickInterval;
        private float triggerTime;
        private bool triggered;
        private bool followSourceDuringTelegraph;
        private bool followSourceAfterSpawn;
        private string cloudName;
        private Color cloudColor;

        public void Initialize(
            int newRunId,
            CharacterMainControl newSource,
            Vector3 newOrigin,
            float newRadius,
            float newDuration,
            float newDamagePerSecond,
            float newTickInterval,
            float delay,
            string newCloudName,
            Color newCloudColor,
            bool newFollowSourceDuringTelegraph,
            bool newFollowSourceAfterSpawn)
        {
            source = newSource;
            origin = newOrigin;
            radius = Mathf.Max(0.5f, newRadius);
            duration = Mathf.Max(0.1f, newDuration);
            damagePerSecond = Mathf.Max(0f, newDamagePerSecond);
            tickInterval = Mathf.Max(0.1f, newTickInterval);
            triggerTime = Time.unscaledTime + Mathf.Max(0.05f, delay);
            triggered = false;
            followSourceDuringTelegraph = newFollowSourceDuringTelegraph;
            followSourceAfterSpawn = newFollowSourceAfterSpawn;
            cloudName = string.IsNullOrEmpty(newCloudName) ? "ZombieMode_DamageCloud" : newCloudName;
            cloudColor = newCloudColor;
            InitializeTimedRuntime(newRunId, Mathf.Max(0.05f, delay) + 0.1f);
        }

        protected override void TickRuntime(ModBehaviour inst)
        {
            if (ShouldCancelFollowSourceRuntime())
            {
                Destroy(gameObject);
                return;
            }

            RefreshFollowSourceOrigin();
            if (triggered || Time.unscaledTime < triggerTime)
            {
                return;
            }

            triggered = true;
            inst.SpawnZombieModeDamageCloud(
                RuntimeRunId,
                source,
                origin,
                radius,
                duration,
                damagePerSecond,
                tickInterval,
                cloudName,
                cloudColor,
                followSourceAfterSpawn);
            Destroy(gameObject);
        }

        private void RefreshFollowSourceOrigin()
        {
            if (!followSourceDuringTelegraph || source == null || source.transform == null)
            {
                return;
            }

            origin = source.transform.position;
            transform.position = origin + Vector3.up * 0.03f;
        }

        private bool ShouldCancelFollowSourceRuntime()
        {
            if (!followSourceDuringTelegraph)
            {
                return false;
            }

            if (source == null || source.Health == null)
            {
                return true;
            }

            ZombieModeEnemyRuntimeMarker marker = source.GetComponent<ZombieModeEnemyRuntimeMarker>();
            if (marker != null && (marker.DeathSettled || marker.RemovedFromRuntime))
            {
                return true;
            }

            return source.Health.CurrentHealth <= 0f;
        }

        protected override void OnRuntimeResumedAfterPause(ModBehaviour inst, float pausedDuration)
        {
            triggerTime += pausedDuration;
        }
    }

    public sealed class ZombieModeHarasserProjectileRuntime : ZombieModeTimedRunScopedRuntime
    {
        private CharacterMainControl source;
        private Vector3 targetPosition;
        private Vector3 velocity;
        private float speed;
        private float maxTravelDistance;
        private float travelledDistance;
        private float lastDistanceToTarget;
        private float impactRadius;
        private float damage;
        private float slowRadius;
        private float slowPercent;
        private float slowDuration;
        private bool resolved;

        public void Initialize(
            int newRunId,
            CharacterMainControl newSource,
            Vector3 origin,
            Vector3 target,
            float newSpeed,
            float newDamage,
            float lifetime,
            float newSlowRadius,
            float newSlowPercent,
            float newSlowDuration)
        {
            source = newSource;
            transform.position = origin;
            targetPosition = target;
            targetPosition.y = origin.y;
            Vector3 direction = targetPosition - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = source != null ? source.transform.forward : Vector3.forward;
                direction.y = 0f;
            }

            velocity = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            speed = Mathf.Max(0.1f, newSpeed);
            damage = Mathf.Max(0f, newDamage);
            slowRadius = Mathf.Max(0.5f, newSlowRadius);
            slowPercent = Mathf.Clamp01(newSlowPercent);
            slowDuration = Mathf.Max(0.1f, newSlowDuration);
            impactRadius = 0.85f;
            travelledDistance = 0f;
            float targetDistance = Mathf.Max(0.1f, direction.magnitude);
            maxTravelDistance = Mathf.Max(targetDistance, speed * Mathf.Max(0.1f, lifetime));
            lastDistanceToTarget = targetDistance;
            resolved = false;
            if (velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(velocity, Vector3.up);
            }

            InitializeTimedRuntime(newRunId, Mathf.Max(0.1f, lifetime) + 0.05f);
        }

        protected override void TickRuntime(ModBehaviour inst)
        {
            if (resolved)
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (IsPlayerHit(player))
            {
                ResolveImpact(inst, player.transform.position);
                return;
            }

            float step = speed * Time.unscaledDeltaTime;
            if (step <= 0f)
            {
                return;
            }

            transform.position += velocity * step;
            travelledDistance += step;

            if (IsPlayerHit(player))
            {
                ResolveImpact(inst, player.transform.position);
                return;
            }

            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            float distanceToTarget = toTarget.magnitude;
            if (distanceToTarget <= impactRadius ||
                travelledDistance >= maxTravelDistance ||
                distanceToTarget > lastDistanceToTarget + 0.05f)
            {
                ResolveImpact(inst, transform.position);
                return;
            }

            lastDistanceToTarget = distanceToTarget;
        }

        protected override void OnRuntimeStopping(ModBehaviour inst, bool expired)
        {
            if (expired && !resolved)
            {
                ResolveImpact(inst, transform.position);
            }
        }

        private bool IsPlayerHit(CharacterMainControl player)
        {
            if (player == null)
            {
                return false;
            }

            Vector3 delta = player.transform.position - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= impactRadius * impactRadius;
        }

        private void ResolveImpact(ModBehaviour inst, Vector3 impactPosition)
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            if (inst != null)
            {
                inst.TryExecuteZombieModeHarasserProjectileImpact(
                    RuntimeRunId,
                    source,
                    impactPosition,
                    damage,
                    slowRadius,
                    slowPercent,
                    slowDuration);
            }

            Destroy(gameObject);
        }
    }
    public sealed class ZombieModeThreatRuntime : MonoBehaviour
    {
        private int runId;
        private float cooldown;
        private float nextSkillTime;
        private float pauseStartTime = -1f;
        private ZombieModeEnemyRuntimeMarker marker;
        private ModBehaviour owner;

        public void Initialize(int newRunId, float newCooldown)
        {
            runId = newRunId;
            cooldown = Mathf.Max(1f, newCooldown);
            nextSkillTime = Time.unscaledTime + UnityEngine.Random.Range(1.5f, 4f);
            marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            owner = ModBehaviour.Instance;
        }

        private void Update()
        {
            float currentTime = Time.unscaledTime;

            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                if (pauseStartTime < 0f)
                {
                    pauseStartTime = currentTime;
                }
                return;
            }

            if (pauseStartTime >= 0f)
            {
                float pausedDuration = Mathf.Max(0f, currentTime - pauseStartTime);
                pauseStartTime = -1f;
                nextSkillTime += pausedDuration;
            }

            if (currentTime < nextSkillTime)
            {
                return;
            }

            nextSkillTime = currentTime + cooldown + UnityEngine.Random.Range(0f, 2f);

            inst.TryExecuteZombieModeEnemyRuntimeSkill(GetCachedMarker());
        }

        private ZombieModeEnemyRuntimeMarker GetCachedMarker()
        {
            if (marker == null)
            {
                marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            return marker;
        }
    }

    public sealed class ZombieModeCommanderAuraRuntime : MonoBehaviour
    {
        private int runId;
        private float radius;
        private float tickInterval;
        private float nextTickTime;
        private CharacterMainControl ownerCharacter;
        private ZombieModeEnemyRuntimeMarker marker;
        private ModBehaviour owner;
        private readonly Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets =
            new Dictionary<int, ZombieModeCommanderAuraTargetRuntime>();

        public void Initialize(int newRunId, float newRadius, float newTickInterval)
        {
            runId = newRunId;
            radius = Mathf.Max(0.5f, newRadius);
            tickInterval = Mathf.Max(0.2f, newTickInterval);
            nextTickTime = 0f;
            ownerCharacter = GetComponent<CharacterMainControl>();
            marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            owner = ModBehaviour.Instance;
        }

        private void Update()
        {
            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                ClearTargets();
                Destroy(this);
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            CharacterMainControl character = GetOwnerCharacter();
            ZombieModeEnemyRuntimeMarker runtimeMarker = GetMarker();

            if (character == null ||
                runtimeMarker == null ||
                runtimeMarker.RunId != runId ||
                runtimeMarker.DeathSettled ||
                runtimeMarker.RemovedFromRuntime ||
                !runtimeMarker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander) ||
                character.Health == null ||
                character.Health.CurrentHealth <= 0f)
            {
                ClearTargets();
                Destroy(this);
                return;
            }

            float now = inst.GetZombieModeRuntimeNow();
            if (now < nextTickTime)
            {
                return;
            }

            nextTickTime = now + tickInterval;
            inst.RefreshZombieModeCommanderAuraTargets(runId, character, radius, trackedTargets);
        }

        private CharacterMainControl GetOwnerCharacter()
        {
            if (ownerCharacter == null)
            {
                ownerCharacter = GetComponent<CharacterMainControl>();
            }

            return ownerCharacter;
        }

        private ZombieModeEnemyRuntimeMarker GetMarker()
        {
            if (marker == null)
            {
                marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            return marker;
        }

        private ModBehaviour GetRuntimeOwner()
        {
            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            return inst;
        }

        private void OnDisable()
        {
            ClearTargets();
        }

        private void OnDestroy()
        {
            ClearTargets();
        }

        private void ClearTargets()
        {
            int sourceId = gameObject != null ? gameObject.GetInstanceID() : 0;
            if (trackedTargets.Count <= 0)
            {
                return;
            }

            foreach (KeyValuePair<int, ZombieModeCommanderAuraTargetRuntime> entry in trackedTargets)
            {
                if (entry.Value != null)
                {
                    entry.Value.RemoveSource(sourceId);
                }
            }

            trackedTargets.Clear();
        }
    }

    public sealed class ZombieModeCommanderAuraTargetRuntime : MonoBehaviour
    {
        private int runId;
        private CharacterMainControl targetCharacter;
        private ModBehaviour owner;
        private readonly HashSet<int> sourceIds = new HashSet<int>();
        private readonly List<ZombieModeAttributeModifierRecord> auraModifierRecords =
            new List<ZombieModeAttributeModifierRecord>();

        public void ApplySource(int newRunId, int sourceId)
        {
            if (sourceId == 0)
            {
                return;
            }

            runId = newRunId;
            sourceIds.Add(sourceId);
            owner = ModBehaviour.Instance;
            EnsureModifiers();
        }

        public void RemoveSource(int sourceId)
        {
            if (sourceId != 0)
            {
                sourceIds.Remove(sourceId);
            }

            if (sourceIds.Count > 0)
            {
                return;
            }

            ReleaseModifiers();
            Destroy(this);
        }

        private void Update()
        {
            ModBehaviour inst = GetRuntimeOwner();
            CharacterMainControl character = GetTargetCharacter();
            if (inst == null ||
                inst.ZombieModeCurrentRunId != runId ||
                character == null ||
                character.CharacterItem == null ||
                character.Health == null ||
                character.Health.CurrentHealth <= 0f ||
                sourceIds.Count <= 0)
            {
                ReleaseModifiers();
                Destroy(this);
            }
        }

        private ModBehaviour GetRuntimeOwner()
        {
            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            return inst;
        }

        private void OnDestroy()
        {
            ReleaseModifiers();
        }

        private void EnsureModifiers()
        {
            CharacterMainControl character = GetTargetCharacter();
            if (character == null || character.CharacterItem == null)
            {
                return;
            }

            if (auraModifierRecords.Count > 0)
            {
                return;
            }

            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.WalkSpeed, ZombieModeTuning.CommanderAffixMoveSpeedBonus, this, auraModifierRecords, "Commander Aura WalkSpeed");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.RunSpeed, ZombieModeTuning.CommanderAffixMoveSpeedBonus, this, auraModifierRecords, "Commander Aura RunSpeed");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.MeleeDamageMultiplier, ZombieModeTuning.CommanderAffixDamageBonus, this, auraModifierRecords, "Commander Aura MeleeDamage");
            RuntimeStatModifierTracker.TryAdd(character, ZombieModeStatNames.GunDamageMultiplier, ZombieModeTuning.CommanderAffixDamageBonus, this, auraModifierRecords, "Commander Aura GunDamage");
        }

        private CharacterMainControl GetTargetCharacter()
        {
            if (targetCharacter == null)
            {
                targetCharacter = GetComponent<CharacterMainControl>();
            }

            return targetCharacter;
        }

        private void ReleaseModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(auraModifierRecords, "Commander Aura");
            sourceIds.Clear();
        }
    }

    public sealed class ZombieModeRegenerationAffixRuntime : MonoBehaviour
    {
        private int runId;
        private float nextTick;
        private CharacterMainControl cachedCharacter;
        private ModBehaviour owner;

        public void Initialize(int newRunId)
        {
            runId = newRunId;
            owner = ModBehaviour.Instance;
            ModBehaviour inst = owner;
            nextTick = (inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime) + 1f;
        }

        private void Update()
        {
            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            float now = inst.GetZombieModeRuntimeNow();
            if (now < nextTick)
            {
                return;
            }

            nextTick = now + 1f;

            CharacterMainControl character = GetCachedCharacter();
            if (character == null || character.Health == null || character.Health.CurrentHealth <= 0)
            {
                return;
            }

            float heal = Mathf.Max(1f, character.Health.MaxHealth * 0.025f);
            character.Health.SetHealth(Mathf.Min(character.Health.MaxHealth, character.Health.CurrentHealth + heal));
        }

        private ModBehaviour GetRuntimeOwner()
        {
            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            return inst;
        }

        private CharacterMainControl GetCachedCharacter()
        {
            if (cachedCharacter == null)
            {
                cachedCharacter = GetComponent<CharacterMainControl>();
            }

            return cachedCharacter;
        }
    }

    public sealed class ZombieModeShieldedAffixRuntime : MonoBehaviour
    {
        private int runId;
        private float shieldRemaining;
        private float shieldEndTime;
        private bool shieldActive;
        private ModBehaviour owner;

        public void ActivateShield(int newRunId, float amount, float duration)
        {
            runId = newRunId;
            shieldRemaining = amount;
            owner = ModBehaviour.Instance;
            ModBehaviour inst = owner;
            shieldEndTime = (inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime) + duration;
            shieldActive = true;
        }

        private void Update()
        {
            if (!shieldActive)
            {
                return;
            }

            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                shieldActive = false;
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            if (inst.GetZombieModeRuntimeNow() >= shieldEndTime)
            {
                shieldActive = false;
                shieldRemaining = 0f;
                return;
            }
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

        private float GetRuntimeNow()
        {
            ModBehaviour inst = GetRuntimeOwner();
            return inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
        }

        private ModBehaviour GetRuntimeOwner()
        {
            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            return inst;
        }
    }
}
