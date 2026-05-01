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
            CharacterMainControl player = CharacterMainControl.Main;
            bool inside = false;
            if (player != null && zombieModeRunState.ActiveSafeZoneActive)
            {
                Vector3 delta = player.transform.position - zombieModeRunState.ActiveSafeZoneCenter;
                delta.y = 0f;
                inside = delta.sqrMagnitude <= zombieModeRunState.ActiveSafeZoneRadius * zombieModeRunState.ActiveSafeZoneRadius;
            }

            zombieModeRunState.PlayerInsideSafeZone = inside;
        }

        private bool ShouldSuppressZombieModeEnemyAggroForSafeZone()
        {
            return zombieModeRunState.ActiveSafeZoneActive &&
                   zombieModeRunState.PlayerInsideSafeZone &&
                   !zombieModeRunState.SafeZoneStealthBroken &&
                   (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.InitialPreparation ||
                    zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Preparation ||
                    zombieModeRunState.CombatPhase == ZombieModeCombatPhase.ExtractionOpportunity);
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

            if (suppressed)
            {
                ai.searchedEnemy = null;
                ai.noticed = false;
                return;
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
                    (!zombieModeRunState.PlayerInsideSafeZone || zombieModeRunState.SafeZoneStealthBroken));
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
                try { ItemAgent_Gun.OnMainCharacterShootEvent -= OnZombieModeMainCharacterShoot; } catch { }
                zombieModeShootStealthBreakerRegistered = false;
            });
        }

        private void OnZombieModeMainCharacterShoot(ItemAgent_Gun gunAgent)
        {
            if (!IsZombieModeActive)
            {
                return;
            }

            BreakZombieModeSafeZoneStealth(zombieModeRunState.RunId);
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
