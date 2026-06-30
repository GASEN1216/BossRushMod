using System.Collections;
using Duckov.Buffs;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const int ZombieModeLifestealChanceCapPercent = 50;

        private sealed class ZombieModeProjectileSpreadSnapshot
        {
            public Item Item;
            public readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> ModifierRecords =
                new System.Collections.Generic.List<ZombieModeAttributeModifierRecord>();
        }

        private UnityEngine.Events.UnityAction<Health> zombieModeOptionPlayerHealthChangeHandler;
        private Health zombieModeOptionPlayerHealth;
        private float zombieModeOptionExplosionSkipLogTime = -999f;
        private CharacterMainControl zombieModeSpreadSubscribedPlayer;
        private bool zombieModeOptionRuntimeCleanupRegistered;
        private readonly System.Collections.Generic.Dictionary<int, ZombieModeProjectileSpreadSnapshot> zombieModeProjectileSpreadSnapshots =
            new System.Collections.Generic.Dictionary<int, ZombieModeProjectileSpreadSnapshot>();
        private readonly System.Collections.Generic.List<int> zombieModeProjectileSpreadRestoreScratch =
            new System.Collections.Generic.List<int>();
    }
    public sealed class ZombieModeGravityWellRuntime : MonoBehaviour
    {
        private int runId;
        private Vector3 origin;
        private float radius;
        private float pullStrength;
        private float endTime;
        private float nextTickTime;
        private ModBehaviour owner;

        public void Initialize(int newRunId, Vector3 newOrigin, float newRadius, float newPullStrength, float duration)
        {
            runId = newRunId;
            origin = newOrigin;
            radius = Mathf.Max(1f, newRadius);
            pullStrength = Mathf.Max(0.1f, newPullStrength);
            owner = ModBehaviour.Instance;
            ModBehaviour inst = owner;
            float now = inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
            endTime = now + Mathf.Max(0.5f, duration);
            nextTickTime = now;
        }

        private void Update()
        {
            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                Destroy(gameObject);
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            float now = inst.GetZombieModeRuntimeNow();
            if (now >= endTime)
            {
                Destroy(gameObject);
                return;
            }

            if (now < nextTickTime)
            {
                return;
            }

            nextTickTime = now + 0.2f;
            inst.RefreshZombieModeGravityWellTargets(runId, origin, radius, pullStrength);
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

    public sealed class ZombieModeEnemyStasisRuntime : MonoBehaviour
    {
        private int runId;
        private float slowPercent;
        private float endTime;
        private bool active;
        private CharacterMainControl cachedEnemy;
        private ModBehaviour owner;
        private readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> stasisModifierRecords =
            new System.Collections.Generic.List<ZombieModeAttributeModifierRecord>();

        public void Apply(int newRunId, float newSlowPercent, float duration)
        {
            runId = newRunId;
            slowPercent = Mathf.Clamp01(newSlowPercent);
            owner = ModBehaviour.Instance;
            ModBehaviour inst = owner;
            float now = inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
            endTime = Mathf.Max(endTime, now + Mathf.Max(0.1f, duration));
            EnsureModifiers();
        }

        private void Update()
        {
            ModBehaviour inst = GetRuntimeOwner();
            CharacterMainControl enemy = GetCachedEnemy();
            if (inst == null || inst.ZombieModeCurrentRunId != runId || enemy == null || enemy.Health == null || enemy.Health.CurrentHealth <= 0f)
            {
                ReleaseModifiers();
                Destroy(this);
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            if (inst.GetZombieModeRuntimeNow() >= endTime)
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
            if (active)
            {
                return;
            }

            CharacterMainControl enemy = GetCachedEnemy();
            if (enemy == null || enemy.CharacterItem == null)
            {
                return;
            }

            float debuff = -slowPercent;
            RuntimeStatModifierTracker.TryAdd(enemy, ZombieModeStatNames.MoveSpeed, debuff, this, stasisModifierRecords, "Enemy Stasis MoveSpeed");
            RuntimeStatModifierTracker.TryAdd(enemy, ZombieModeStatNames.WalkSpeed, debuff, this, stasisModifierRecords, "Enemy Stasis WalkSpeed");
            RuntimeStatModifierTracker.TryAdd(enemy, ZombieModeStatNames.RunSpeed, debuff, this, stasisModifierRecords, "Enemy Stasis RunSpeed");
            active = true;
        }

        private CharacterMainControl GetCachedEnemy()
        {
            if (cachedEnemy == null)
            {
                cachedEnemy = GetComponent<CharacterMainControl>();
            }

            return cachedEnemy;
        }

        private void ReleaseModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(stasisModifierRecords, "Enemy Stasis");
            active = false;
            endTime = 0f;
        }
    }

    public sealed class ZombieModePlayerProjectileRuntime : MonoBehaviour
    {
        private int runId;
        private bool helixEnabled;
        private float helixAmplitude;
        private float helixFrequency;
        private bool trailEnabled;
        private float trailRadius;
        private float trailDamage;
        private float nextTrailTime;
        private float elapsed;
        private Vector3 lastHelixOffset = Vector3.zero;
        private ModBehaviour owner;
        private int projectileInstanceId;
        private Transform cachedTransform;
        private static CharacterMainControl cachedTrailPlayer;
        private static int cachedTrailPlayerFrame = -1;

        private void Awake()
        {
            cachedTransform = transform;
        }

        public void Initialize(
            int newRunId,
            bool enableHelix,
            float amplitude,
            float frequency,
            bool enableTrail,
            float newTrailRadius,
            float newTrailDamage,
            int projectileId)
        {
            ResetRuntimeState();
            ClearRuntimeConfiguration();
            runId = newRunId;
            owner = ModBehaviour.Instance;
            projectileInstanceId = projectileId;
            helixEnabled = enableHelix;
            helixAmplitude = amplitude;
            helixFrequency = frequency;
            trailEnabled = enableTrail;
            trailRadius = newTrailRadius;
            trailDamage = newTrailDamage;
        }

        public void ResetRuntimeState()
        {
            elapsed = 0f;
            nextTrailTime = 0f;
            lastHelixOffset = Vector3.zero;
        }

        public void ClearRuntimeConfiguration()
        {
            runId = 0;
            helixEnabled = false;
            helixAmplitude = 0f;
            helixFrequency = 0f;
            trailEnabled = false;
            trailRadius = 0f;
            trailDamage = 0f;
        }

        private void OnDisable()
        {
            ResetRuntimeState();
            ClearRuntimeConfiguration();
        }

        private void OnDestroy()
        {
            UnregisterTrackedProjectile();
        }

        private void LateUpdate()
        {
            ModBehaviour inst = GetRuntimeOwner();
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                Destroy(this);
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            Transform projectileTransform = cachedTransform;
            if (projectileTransform == null)
            {
                projectileTransform = transform;
                cachedTransform = projectileTransform;
            }

            elapsed += Time.unscaledDeltaTime;
            if (helixEnabled)
            {
                Vector3 forward = projectileTransform.forward;
                if (forward.sqrMagnitude <= 0.001f)
                {
                    forward = Vector3.forward;
                }

                forward.Normalize();
                Vector3 lateral = Vector3.Cross(Vector3.up, forward);
                if (lateral.sqrMagnitude <= 0.001f)
                {
                    lateral = Vector3.Cross(Vector3.forward, forward);
                }

                lateral.Normalize();
                Vector3 vertical = Vector3.Cross(forward, lateral);
                float phase = elapsed * helixFrequency;
                Vector3 offset = lateral * (Mathf.Sin(phase) * helixAmplitude);
                offset += vertical * (Mathf.Cos(phase) * helixAmplitude * 0.55f);
                projectileTransform.position += offset - lastHelixOffset;
                lastHelixOffset = offset;
            }

            if (trailEnabled)
            {
                float now = inst.GetZombieModeRuntimeNow();
                if (now >= nextTrailTime)
                {
                    nextTrailTime = now + 0.15f;
                    CharacterMainControl player = GetCachedTrailPlayer();
                    if (player != null && inst.CanTriggerZombieModeProjectileTrailDamage(runId))
                    {
                        inst.DealZombieModeExplosionAreaDamage(runId, player, projectileTransform.position, trailRadius, trailDamage, false);
                    }
                }
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

        private static CharacterMainControl GetCachedTrailPlayer()
        {
            if (cachedTrailPlayerFrame != Time.frameCount)
            {
                cachedTrailPlayer = CharacterMainControl.Main;
                cachedTrailPlayerFrame = Time.frameCount;
            }

            return cachedTrailPlayer;
        }

        private void UnregisterTrackedProjectile()
        {
            if (projectileInstanceId == 0)
            {
                return;
            }

            ModBehaviour inst = GetRuntimeOwner();
            if (inst != null)
            {
                inst.UnregisterZombieModePlayerProjectileRuntime(projectileInstanceId);
            }
            projectileInstanceId = 0;
        }
    }
}
