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
    public sealed class ZombieModeTelegraphedAreaDamageRuntime : ZombieModeTimedRunScopedRuntime
    {
        private CharacterMainControl source;
        private Vector3 origin;
        private float radius;
        private float damage;
        private float triggerTime;
        private bool triggered;

        public void Initialize(
            int newRunId,
            CharacterMainControl newSource,
            Vector3 newOrigin,
            float newRadius,
            float newDamage,
            float delay)
        {
            source = newSource;
            origin = newOrigin;
            radius = newRadius;
            damage = newDamage;
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
            inst.TryExecuteZombieModeTelegraphedAreaDamage(RuntimeRunId, source, origin, radius, damage);
            Destroy(gameObject);
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
                    pauseStartTime = Time.unscaledTime;
                }
                return;
            }

            if (pauseStartTime >= 0f)
            {
                float pausedDuration = Mathf.Max(0f, Time.unscaledTime - pauseStartTime);
                pauseStartTime = -1f;
                nextSkillTime += pausedDuration;
            }

            if (Time.unscaledTime < nextSkillTime)
            {
                return;
            }

            nextSkillTime = Time.unscaledTime + cooldown + UnityEngine.Random.Range(0f, 2f);
            if (marker == null)
            {
                marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            inst.TryExecuteZombieModeEnemyRuntimeSkill(marker);
        }
    }

    public sealed class ZombieModeCommanderAuraRuntime : MonoBehaviour
    {
        private int runId;
        private float radius;
        private float tickInterval;
        private float nextTickTime;
        private CharacterMainControl owner;
        private ZombieModeEnemyRuntimeMarker marker;
        private readonly Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets =
            new Dictionary<int, ZombieModeCommanderAuraTargetRuntime>();

        public void Initialize(int newRunId, float newRadius, float newTickInterval)
        {
            runId = newRunId;
            radius = Mathf.Max(0.5f, newRadius);
            tickInterval = Mathf.Max(0.2f, newTickInterval);
            nextTickTime = 0f;
            owner = GetComponent<CharacterMainControl>();
            marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
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

            if (owner == null)
            {
                owner = GetComponent<CharacterMainControl>();
            }

            if (marker == null)
            {
                marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            if (owner == null ||
                marker == null ||
                marker.RunId != runId ||
                marker.DeathSettled ||
                marker.RemovedFromRuntime ||
                !marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander) ||
                owner.Health == null ||
                owner.Health.CurrentHealth <= 0f)
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
            inst.RefreshZombieModeCommanderAuraTargets(runId, owner, radius, trackedTargets);
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
            ModBehaviour inst = ModBehaviour.Instance;
            CharacterMainControl character = GetComponent<CharacterMainControl>();
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

        private void OnDestroy()
        {
            ReleaseModifiers();
        }

        private void EnsureModifiers()
        {
            CharacterMainControl character = GetComponent<CharacterMainControl>();
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

        public void Initialize(int newRunId)
        {
            runId = newRunId;
            ModBehaviour inst = ModBehaviour.Instance;
            nextTick = (inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime) + 1f;
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
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

            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (character == null || character.Health == null || character.Health.CurrentHealth <= 0)
            {
                return;
            }

            float heal = Mathf.Max(1f, character.Health.MaxHealth * 0.025f);
            character.Health.SetHealth(Mathf.Min(character.Health.MaxHealth, character.Health.CurrentHealth + heal));
        }
    }

    public sealed class ZombieModeShieldedAffixRuntime : MonoBehaviour
    {
        private int runId;
        private float shieldRemaining;
        private float shieldEndTime;
        private bool shieldActive;

        public void ActivateShield(int newRunId, float amount, float duration)
        {
            runId = newRunId;
            shieldRemaining = amount;
            ModBehaviour inst = ModBehaviour.Instance;
            shieldEndTime = (inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime) + duration;
            shieldActive = true;
        }

        private void Update()
        {
            if (!shieldActive)
            {
                return;
            }

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
            ModBehaviour inst = ModBehaviour.Instance;
            return inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
        }
    }
}
