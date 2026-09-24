using System.Collections;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void RegisterZombieModeBossRuntime(int runId, CharacterMainControl boss, ZombieModeBossKind kind)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RegisterZombieModeBossRuntime(runId, boss, kind);
        }

        private void TickZombieModeBossController(float deltaTime)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeBossController(deltaTime);
        }

        internal void TickZombieModeTitanState(ZombieModeTitanState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeTitanState(state, instance, now);
        }

        internal void TickZombieModeHunterState(ZombieModeHunterState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeHunterState(state, instance, now);
        }

        internal void TickZombieModeSplitterState(ZombieModeSplitterState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeSplitterState(state, instance, now);
        }

        internal void TickZombieModeShielderState(ZombieModeShielderState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeShielderState(state, instance, now);
        }

        internal void TickZombieModeCorruptorState(ZombieModeCorruptorState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeCorruptorState(state, instance, now);
        }

        private void HandleZombieModeBossHurt(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl victim)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.HandleZombieModeBossHurt(runId, marker, victim);
        }

        public float AbsorbZombieModeBossFinalDamage(CharacterMainControl boss, ZombieModeEnemyRuntimeMarker bossMarker, float finalDamage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.AbsorbZombieModeBossFinalDamage(boss, bossMarker, finalDamage) : 0f;
        }

        public float ApplyZombieModeShielderAuraFinalDamageReduction(CharacterMainControl target, float finalDamage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.ApplyZombieModeShielderAuraFinalDamageReduction(target, finalDamage) : 0f;
        }

        public bool TryApplyZombieModeShielderAuraReductionPublic(CharacterMainControl target, ref float damageValue)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryApplyZombieModeShielderAuraReductionPublic(target, ref damageValue);
        }

        private void HandleZombieModeBossDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.HandleZombieModeBossDeathEffects(runId, marker, character);
        }

        public void DealZombieModeRuntimeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeRuntimeAreaDamageToPlayer(runId, origin, radius, damage);
        }

        public void DealZombieModeRuntimeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeRuntimeAreaDamageToPlayer(runId, source, origin, radius, damage);
        }

        public void TryApplyZombieModePlayerSlow(int runId, float percent, float duration)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryApplyZombieModePlayerSlow(runId, percent, duration);
        }

        private void ClearZombieModeSupportSpawnQueue()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ClearZombieModeSupportSpawnQueue();
        }

        private void RemoveZombieModeHunterFrenzyModifiers(ZombieModeHunterState hunter)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RemoveZombieModeHunterFrenzyModifiers(hunter);
        }

        internal Coroutine StartZombieModeBossCoroutineForRuntimeModule(IEnumerator routine, int runId)
        {
            return StartZombieModeCoroutine(routine, runId);
        }

        internal bool TryGetNearestZombieModeMapSpawnPositionToPlayerForBossRuntimeModule(out Vector3 position)
        {
            return TryGetNearestZombieModeMapSpawnPositionToPlayer(out position);
        }

        internal AICharacterController GetZombieModeEnemyAIForBossRuntimeModule(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker)
        {
            return GetZombieModeEnemyAI(enemyObject, marker);
        }

        internal void SetZombieModeEnemyTargetToMainPlayerForBossRuntimeModule(AICharacterController ai)
        {
            SetZombieModeEnemyTargetToMainPlayer(ai);
        }

        internal int CollectZombieModeRuntimeEnemyMarkersForBossRuntimeModule(
            int runId,
            System.Collections.Generic.List<ZombieModeEnemyRuntimeMarker> results,
            bool includeBosses)
        {
            return CollectZombieModeRuntimeEnemyMarkers(runId, results, includeBosses);
        }

        internal float GetZombieModeBossDamageScaleForRuntimeModule(int wave)
        {
            return GetZombieModeBossDamageScale(wave);
        }

    }

    public abstract class ZombieModeTimedRunScopedRuntime : MonoBehaviour
    {
        private int runtimeRunId;
        private float runtimeEndTime;
        private float runtimePauseStartTime = -1f;
        private ModBehaviour owner;

        protected int RuntimeRunId
        {
            get { return runtimeRunId; }
        }

        protected void InitializeTimedRuntime(int newRunId, float duration)
        {
            runtimeRunId = newRunId;
            owner = ModBehaviour.Instance;
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
            float currentTime = Time.unscaledTime;

            ModBehaviour inst = GetRuntimeOwner();
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
                    runtimePauseStartTime = currentTime;
                }
                return;
            }

            if (runtimePauseStartTime >= 0f)
            {
                float pausedDuration = Mathf.Max(0f, currentTime - runtimePauseStartTime);
                runtimePauseStartTime = -1f;
                runtimeEndTime += pausedDuration;
                OnRuntimeResumedAfterPause(inst, pausedDuration);
            }

            if (currentTime >= runtimeEndTime)
            {
                OnRuntimeStopping(inst, true);
                // 地面圈到时淡出 0.18 s 再删（纯表现，审美审查 VA-27）；没有地面圈的 runtime 照旧直接删。
                ZombieModeZoneVisuals.FadeOutAndDestroy(gameObject, this);
                return;
            }

            TickRuntime(inst);
        }

        private ModBehaviour GetRuntimeOwner()
        {
            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runtimeRunId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            return inst;
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
        private bool followSourcePosition;
        private float startupSeconds;
        private bool armed;

        public void Initialize(
            int newRunId,
            CharacterMainControl newSource,
            float newRadius,
            float duration,
            float dps,
            float tick,
            float slow = 0f,
            float startupDelay = 0f,
            bool followSource = false)
        {
            source = newSource;
            radius = Mathf.Max(0.5f, newRadius);
            damagePerSecond = dps;
            slowPercent = Mathf.Max(0f, slow);
            tickInterval = Mathf.Max(0.1f, tick);
            followSourcePosition = followSource;
            nextTickTime = Time.unscaledTime + Mathf.Max(Mathf.Max(0f, startupDelay), tickInterval);
            startupSeconds = Mathf.Max(0f, startupDelay);
            armed = startupSeconds <= 0f;
            InitializeTimedRuntime(newRunId, duration);
        }

        protected override void OnRuntimeResumedAfterPause(ModBehaviour inst, float pausedDuration)
        {
            nextTickTime += pausedDuration;
        }

        protected override void TickRuntime(ModBehaviour inst)
        {
            if (followSourcePosition && source != null && source.transform != null)
            {
                transform.position = source.transform.position + Vector3.up * 0.04f;
            }

            if (!armed)
            {
                // 启动期读条（纯表现，审美审查 UC-19）：腐蚀领域启动期内圈涨满，第一次结算时刻不变。
                ZombieModeZoneVisuals.SetCountdown(gameObject, 1f - (nextTickTime - Time.unscaledTime) / startupSeconds);
                armed = Time.unscaledTime >= nextTickTime;
            }

            if (Time.unscaledTime < nextTickTime)
            {
                return;
            }

            nextTickTime = Time.unscaledTime + tickInterval;
            float tickDamage = damagePerSecond * tickInterval;
            if (tickDamage > 0f)
            {
                inst.DealZombieModeRuntimeAreaDamageToPlayer(RuntimeRunId, source, transform.position, radius, tickDamage);
            }

            if (slowPercent > 0f)
            {
                inst.TryApplyZombieModePlayerSlowInArea(RuntimeRunId, transform.position, radius, slowPercent, tickInterval * 2f);
            }
        }
    }

    public sealed class ZombieModeBossShieldRuntime : MonoBehaviour
    {
        private int runId;
        private float shieldRemaining;
        private float shieldEndTime;
        private bool shieldActive;
        private ModBehaviour owner;

        public void ActivateShield(int newRunId, float amount, float duration)
        {
            runId = newRunId;
            owner = ModBehaviour.Instance;
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

            if (shieldActive && inst.GetZombieModeRuntimeNow() >= shieldEndTime)
            {
                shieldActive = false;
                shieldRemaining = 0f;
            }
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
