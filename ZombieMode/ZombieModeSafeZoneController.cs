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
                (zombieModeRunState.CombatPhase != ZombieModeCombatPhase.InitialPreparation &&
                 zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Preparation &&
                 zombieModeRunState.CombatPhase != ZombieModeCombatPhase.ExtractionOpportunity))
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
                   (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.InitialPreparation ||
                    zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Preparation ||
                    zombieModeRunState.CombatPhase == ZombieModeCombatPhase.ExtractionOpportunity);
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
                }

                CharacterMainControl owner = marker != null ? marker.Owner : null;
                if (owner == null)
                {
                    owner = record.GameObject.GetComponent<CharacterMainControl>();
                }

                Transform enemyTransform = owner != null ? owner.transform : record.GameObject.transform;
                Vector3 delta = enemyTransform.position - center;
                delta.y = 0f;
                if (delta.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                if (delta.sqrMagnitude < 0.01f)
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    delta = player != null ? enemyTransform.position - player.transform.position : Random.insideUnitSphere;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < 0.01f)
                    {
                        delta = Vector3.forward;
                    }
                }

                Vector3 destination = center + delta.normalized * repelRadius;
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
                SetZombieModeEnemyThreatSuppressed(record.GameObject, true);
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

                SetZombieModeEnemyThreatSuppressed(record.GameObject, true);
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

                SetZombieModeEnemyThreatSuppressed(record.GameObject, false);
            }
        }

        private void SetZombieModeEnemyThreatSuppressed(GameObject enemyObject, bool suppressed)
        {
            if (enemyObject == null)
            {
                return;
            }

            AICharacterController ai = enemyObject.GetComponentInChildren<AICharacterController>();
            if (ai == null)
            {
                return;
            }

            ZombieModeEnemyRuntimeMarker marker = enemyObject.GetComponent<ZombieModeEnemyRuntimeMarker>();

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
