using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool zombieModeShootStealthBreakerRegistered;

        /// <summary>
        /// 主槽（正常安全区，带商人）与副槽（准备期便携安全区，不带商人）任一激活。
        /// 两槽只在准备期并存，下一波开始时一起清除。
        /// </summary>
        private bool AnyZombieModeSafeZoneActive
        {
            get
            {
                return zombieModeRunState.ActiveSafeZoneActive || zombieModeRunState.PortableSafeZoneActive;
            }
        }

        private void TickZombieModeSafeZone()
        {
            if (!IsZombieModeActive ||
                !AnyZombieModeSafeZoneActive ||
                !ZombieModePhaseGuards.AllowsSafeZone(zombieModeRunState.CombatPhase))
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
            // 抑制判定必须先于物理禁入：弹出本身不得附带去仇恨，否则玩家在区外时
            // 蹭到边界的丧尸会被永久清空 forceTracePlayerDistance 而再也不追击。
            bool shouldSuppress = ShouldSuppressZombieModeEnemyAggroForSafeZone();
            KeepZombieModeEnemiesOutsideSafeZone(shouldSuppress);
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
            if (player == null || !AnyZombieModeSafeZoneActive)
            {
                return false;
            }

            return IsZombieModePositionInsideActiveSafeZone(player.transform.position);
        }

        /// <summary>
        /// 判断位置是否落在单个安全区槽的水平圆内。padding 用于敌人禁入的外扩范围。
        /// </summary>
        private static bool IsPositionInsideZombieModeSafeZoneSlot(
            Vector3 position,
            bool slotActive,
            Vector3 slotCenter,
            float slotRadius,
            float padding)
        {
            if (!slotActive || slotRadius <= 0f)
            {
                return false;
            }

            float effectiveRadius = slotRadius + padding;
            Vector3 delta = position - slotCenter;
            delta.y = 0f;
            return delta.sqrMagnitude <= effectiveRadius * effectiveRadius;
        }

        private bool IsZombieModePositionInsideActiveSafeZone(Vector3 position)
        {
            return IsPositionInsideZombieModeSafeZoneSlot(
                       position,
                       zombieModeRunState.ActiveSafeZoneActive,
                       zombieModeRunState.ActiveSafeZoneCenter,
                       zombieModeRunState.ActiveSafeZoneRadius,
                       0f) ||
                   IsPositionInsideZombieModeSafeZoneSlot(
                       position,
                       zombieModeRunState.PortableSafeZoneActive,
                       zombieModeRunState.PortableSafeZoneCenter,
                       zombieModeRunState.PortableSafeZoneRadius,
                       0f);
        }

        private bool IsZombieModePositionInsideSafeZoneEnemyExclusion(Vector3 position)
        {
            Vector3 unusedCenter;
            float unusedRadius;
            return TryResolveZombieModeSafeZoneExclusionSlot(position, out unusedCenter, out unusedRadius);
        }

        /// <summary>
        /// 解析位置落在哪个安全区槽的敌人禁入范围内，并返回该槽的圆心与半径。
        /// 弹出方向必须按敌人实际所在的那个槽计算，主槽优先。
        /// </summary>
        private bool TryResolveZombieModeSafeZoneExclusionSlot(Vector3 position, out Vector3 center, out float radius)
        {
            float padding = ZombieModeTuning.SafeZoneEnemyExclusionPadding;
            if (IsPositionInsideZombieModeSafeZoneSlot(
                    position,
                    zombieModeRunState.ActiveSafeZoneActive,
                    zombieModeRunState.ActiveSafeZoneCenter,
                    zombieModeRunState.ActiveSafeZoneRadius,
                    padding))
            {
                center = zombieModeRunState.ActiveSafeZoneCenter;
                radius = zombieModeRunState.ActiveSafeZoneRadius;
                return true;
            }

            if (IsPositionInsideZombieModeSafeZoneSlot(
                    position,
                    zombieModeRunState.PortableSafeZoneActive,
                    zombieModeRunState.PortableSafeZoneCenter,
                    zombieModeRunState.PortableSafeZoneRadius,
                    padding))
            {
                center = zombieModeRunState.PortableSafeZoneCenter;
                radius = zombieModeRunState.PortableSafeZoneRadius;
                return true;
            }

            center = Vector3.zero;
            radius = 0f;
            return false;
        }

        private bool ShouldSuppressZombieModeEnemyAggroForSafeZone()
        {
            return AnyZombieModeSafeZoneActive &&
                   IsZombieModePlayerInsideActiveSafeZone() &&
                   ZombieModePhaseGuards.AllowsSafeZone(zombieModeRunState.CombatPhase);
        }

        /// <summary>
        /// 把越界敌人推出安全区。suppressThreat 由 tick 统一判定后传入：
        /// 物理禁入与仇恨抑制是两条独立规则，弹出不得擅自去仇恨。
        /// </summary>
        private void KeepZombieModeEnemiesOutsideSafeZone(bool suppressThreat)
        {
            if (!AnyZombieModeSafeZoneActive)
            {
                return;
            }

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
                if (!IsZombieModePositionInsideSafeZoneEnemyExclusion(enemyTransform.position))
                {
                    continue;
                }

                TryMoveZombieModeEnemyOutsideSafeZone(
                    record.GameObject,
                    marker,
                    suppressThreat);
            }
        }

        /// <summary>
        /// 将进入安全区的丧尸立即推出边界。安全区的物理禁入与隐匿仇恨是两条独立规则：
        /// 即使玩家已经取消保护，安全区仍然不能成为丧尸的站位区域。
        /// </summary>
        private bool TryMoveZombieModeEnemyOutsideSafeZone(
            GameObject enemyObject,
            ZombieModeEnemyRuntimeMarker marker,
            bool suppressThreat)
        {
            if (enemyObject == null || !AnyZombieModeSafeZoneActive)
            {
                return false;
            }

            CharacterMainControl owner = marker != null ? marker.Owner : null;
            if (owner == null)
            {
                owner = enemyObject.GetComponent<CharacterMainControl>();
                if (marker != null)
                {
                    marker.Owner = owner;
                }
            }

            Transform enemyTransform = owner != null ? owner.transform : enemyObject.transform;
            Vector3 slotCenter;
            float slotRadius;
            if (enemyTransform == null ||
                !TryResolveZombieModeSafeZoneExclusionSlot(enemyTransform.position, out slotCenter, out slotRadius))
            {
                return false;
            }

            Vector3 delta = enemyTransform.position - slotCenter;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.01f)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                delta = player != null
                    ? enemyTransform.position - player.transform.position
                    : Vector3.forward;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.01f)
                {
                    delta = Vector3.forward;
                }
            }

            float deltaDistanceSqr = delta.sqrMagnitude;
            float inverseDistance = 1f / Mathf.Sqrt(deltaDistanceSqr);
            float ejectionRadius = slotRadius +
                                   ZombieModeTuning.SafeZoneEnemyExclusionPadding +
                                   ZombieModeTuning.SafeZoneEnemyEjectionClearance;
            Vector3 destination = slotCenter +
                                  delta * (ejectionRadius * inverseDistance);
            destination.y = enemyTransform.position.y;
            Vector3 resolved;
            if (SpawnPositionHelper.TrySampleNavMesh(
                    destination,
                    out resolved,
                    ZombieModeTuning.NavMeshLiftOffset,
                    ZombieModeTuning.SafeZoneEnemyEjectionNavMeshRadius) &&
                !IsZombieModePositionInsideSafeZoneEnemyExclusion(resolved))
            {
                destination = resolved;
            }

            try
            {
                if (owner != null)
                {
                    owner.SetPosition(destination);
                }
                else
                {
                    enemyTransform.position = destination;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] safe-zone enemy ejection SetPosition failed: " + e.Message);
                enemyTransform.position = destination;
            }

            SetZombieModeEnemyThreatSuppressed(enemyObject, marker, suppressThreat);
            return true;
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
                // 释放路径以这个开关早退。任何来源的逐敌抑制都必须记账，
                // 否则被抑制的敌人再也不会恢复追击。
                zombieModeRunState.SafeZoneThreatSuppressed = true;
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
                   (AnyZombieModeSafeZoneActive &&
                    !IsZombieModePlayerInsideActiveSafeZone());
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
