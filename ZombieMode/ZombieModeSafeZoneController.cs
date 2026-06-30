using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool zombieModeShootStealthBreakerRegistered;

        private void TickZombieModeSafeZone()
        {
            if (!IsZombieModeActive ||
                !zombieModeRunState.ActiveSafeZoneActive ||
                !ZombieModePhaseGuards.AllowsBeacon(zombieModeRunState.CombatPhase))
            {
                ReleaseZombieModeSafeZoneThreatSuppression();
                return;
            }

            if (Time.unscaledTime - zombieModeRunState.LastSafeZoneTickTime < ZombieModeTuning.SafeZoneTickIntervalSeconds)
            {
                return;
            }

            zombieModeRunState.LastSafeZoneTickTime = Time.unscaledTime;
            UpdateZombieModeSafeZonePlayerPresence();
            UpdateZombieModeSafeZoneVisual();
            KeepZombieModeEnemiesOutsideSafeZone();
            bool shouldSuppress = ShouldSuppressZombieModeEnemyAggroForSafeZone();
            if (shouldSuppress)
            {
                zombieModeRunState.SafeZoneThreatSuppressed = shouldSuppress;
                SuppressZombieModeSafeZoneThreats();
            }
            else
            {
                ReleaseZombieModeSafeZoneThreatSuppression();
                zombieModeRunState.SafeZoneThreatSuppressed = shouldSuppress;
            }
        }

        private void UpdateZombieModeSafeZonePlayerPresence()
        {
            zombieModeRunState.PlayerInsideSafeZone = IsZombieModePlayerInsideActiveSafeZone();
        }

        private bool IsZombieModePlayerInsideActiveSafeZone()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || !zombieModeRunState.ActiveSafeZoneActive)
            {
                return false;
            }

            return IsZombieModePositionInsideActiveSafeZone(player.transform.position);
        }

        private bool IsZombieModePositionInsideActiveSafeZone(Vector3 position)
        {
            if (!zombieModeRunState.ActiveSafeZoneActive || zombieModeRunState.ActiveSafeZoneRadius <= 0f)
            {
                return false;
            }

            Vector3 delta = position - zombieModeRunState.ActiveSafeZoneCenter;
            delta.y = 0f;
            return delta.sqrMagnitude <= zombieModeRunState.ActiveSafeZoneRadius * zombieModeRunState.ActiveSafeZoneRadius;
        }

        private bool ShouldSuppressZombieModeEnemyAggroForSafeZone()
        {
            return zombieModeRunState.ActiveSafeZoneActive &&
                   IsZombieModePlayerInsideActiveSafeZone() &&
                   !zombieModeRunState.SafeZoneStealthBroken &&
                   ZombieModePhaseGuards.AllowsBeacon(zombieModeRunState.CombatPhase);
        }

        private void KeepZombieModeEnemiesOutsideSafeZone()
        {
            if (!zombieModeRunState.ActiveSafeZoneActive || zombieModeRunState.SafeZoneStealthBroken)
            {
                return;
            }

            Vector3 center = zombieModeRunState.ActiveSafeZoneCenter;
            float radius = zombieModeRunState.ActiveSafeZoneRadius;
            if (radius <= 0f)
            {
                return;
            }

            float radiusSqr = radius * radius;
            float repelRadius = radius + 1.5f;
            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    (record.Kind != ZombieModeRunOnlyObjectKind.Enemy && record.Kind != ZombieModeRunOnlyObjectKind.Boss) ||
                    record.GameObject == null)
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;
                if (marker == null)
                {
                    marker = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                    if (marker != null)
                    {
                        record.Target = marker;
                    }
                }

                CharacterMainControl owner = marker != null ? marker.Owner : null;
                if (owner == null)
                {
                    owner = record.GameObject.GetComponent<CharacterMainControl>();
                    if (marker != null)
                    {
                        marker.Owner = owner;
                    }
                }

                Transform enemyTransform = owner != null ? owner.transform : record.GameObject.transform;
                Vector3 delta = enemyTransform.position - center;
                delta.y = 0f;
                float deltaDistanceSqr = delta.sqrMagnitude;
                if (deltaDistanceSqr > radiusSqr)
                {
                    continue;
                }

                if (deltaDistanceSqr < 0.01f)
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    delta = player != null ? enemyTransform.position - player.transform.position : Random.insideUnitSphere;
                    delta.y = 0f;
                    deltaDistanceSqr = delta.sqrMagnitude;
                    if (deltaDistanceSqr < 0.01f)
                    {
                        delta = Vector3.forward;
                        deltaDistanceSqr = 1f;
                    }
                }

                float inverseDistance = 1f / Mathf.Sqrt(deltaDistanceSqr);
                Vector3 destination = center + delta * (repelRadius * inverseDistance);
                destination.y = enemyTransform.position.y;
                Vector3 resolved;
                if (SpawnPositionHelper.TrySampleNavMesh(
                        destination,
                        out resolved,
                        ZombieModeTuning.NavMeshLiftOffset,
                        ZombieModeTuning.NavMeshSafeZoneRadius))
                {
                    destination = resolved;
                }

                enemyTransform.position = destination;
                SetZombieModeEnemyThreatSuppressed(record.GameObject, marker, true);
            }
        }

        private void SuppressZombieModeSafeZoneThreats()
        {
            zombieModeRunState.SafeZoneThreatSuppressed = true;
            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    (record.Kind != ZombieModeRunOnlyObjectKind.Enemy && record.Kind != ZombieModeRunOnlyObjectKind.Boss) ||
                    record.GameObject == null)
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;
                if (marker == null && record.GameObject != null)
                {
                    marker = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                    if (marker != null)
                    {
                        record.Target = marker;
                    }
                }

                SetZombieModeEnemyThreatSuppressed(record.GameObject, marker, true);
            }
        }

        private void ReleaseZombieModeSafeZoneThreatSuppression()
        {
            if (!zombieModeRunState.SafeZoneThreatSuppressed)
            {
                return;
            }

            zombieModeRunState.SafeZoneThreatSuppressed = false;
            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    (record.Kind != ZombieModeRunOnlyObjectKind.Enemy && record.Kind != ZombieModeRunOnlyObjectKind.Boss) ||
                    record.GameObject == null)
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;
                if (marker == null && record.GameObject != null)
                {
                    marker = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                    if (marker != null)
                    {
                        record.Target = marker;
                    }
                }

                SetZombieModeEnemyThreatSuppressed(record.GameObject, marker, false);
            }
        }

        private void SetZombieModeEnemyThreatSuppressed(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker, bool suppressed)
        {
            if (enemyObject == null)
            {
                return;
            }

            if (marker == null || marker.gameObject != enemyObject)
            {
                marker = enemyObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            AICharacterController ai = GetZombieModeEnemyAI(enemyObject, marker);
            if (ai == null)
            {
                return;
            }

            if (suppressed)
            {
                if (marker != null && !marker.HasSuppressedForceTraceDistance)
                {
                    marker.SuppressedForceTraceDistance = ai.forceTracePlayerDistance;
                    marker.HasSuppressedForceTraceDistance = true;
                }

                ai.forceTracePlayerDistance = 0f;
                ai.searchedEnemy = null;
                ai.noticed = false;
                return;
            }

            if (marker != null && marker.HasSuppressedForceTraceDistance)
            {
                ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, marker.SuppressedForceTraceDistance);
                marker.SuppressedForceTraceDistance = 0f;
                marker.HasSuppressedForceTraceDistance = false;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            if (main != null)
            {
                SetZombieModeEnemyTargetToMainPlayer(ai);
            }
        }

        private bool ShouldZombieModeEnemyAggroPlayerNow()
        {
            return zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat ||
                   (zombieModeRunState.ActiveSafeZoneActive &&
                    (!IsZombieModePlayerInsideActiveSafeZone() || zombieModeRunState.SafeZoneStealthBroken));
        }

        private void TryRegisterZombieModeShootStealthBreaker(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeShootStealthBreakerRegistered)
            {
                return;
            }

            ItemAgent_Gun.OnMainCharacterShootEvent += OnZombieModeMainCharacterShoot;
            zombieModeShootStealthBreakerRegistered = true;
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.EventListener, null, null, delegate
            {
                try { ItemAgent_Gun.OnMainCharacterShootEvent -= OnZombieModeMainCharacterShoot; } catch (System.Exception e) { DevLog("[ZombieMode] 解绑 OnMainCharacterShootEvent 失败: " + e.Message); }
                zombieModeShootStealthBreakerRegistered = false;
            });
        }

        private void OnZombieModeMainCharacterShoot(ItemAgent_Gun gunAgent)
        {
            if (!IsZombieModeActive)
            {
                return;
            }

            UpdateZombieModeSafeZonePlayerPresence();
        }
    }

    public sealed class ZombieModeSafeZoneController : MonoBehaviour
    {
        public int RunId;

        public void Initialize(int runId)
        {
            RunId = runId;
        }
    }
}
