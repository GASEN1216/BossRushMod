using System.Collections.Generic;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class TidegateEnemies
    {
        // 官方角色除了 AI 还有物理、动画与物品更新；整只远区活体挂起，返回时恢复同一实体。
        // 潮汐队伍继续赶路，灯塔揭示期间全图醒来响应声音与地图；不改变敌人的生成/死亡名额。
        internal const float ActivityWakeRadius = 55f, ActivitySleepRadius = 65f;
        private readonly HashSet<Slot> activitySuspended = new HashSet<Slot>();
        private float nextActivity;
#if BOSSRUSH_DEV
        internal bool DevActivityFrozen;
#endif

        private bool ActivitySuspended(Slot slot) { return activitySuspended.Contains(slot); }

        private void TickActivity()
        {
#if BOSSRUSH_DEV
            if (DevActivityFrozen) return;
#endif
            if (Time.time < nextActivity || BossRushUI.IsGamePaused()) return;
            nextActivity = Time.time + 0.35f;
            Vector3 at = player.transform.position;
            foreach (Slot slot in slots)
            {
                CharacterMainControl character = slot.Character;
                if (character == null || character.Health == null || character.Health.IsDead)
                { activitySuspended.Remove(slot); continue; }
                bool suspended = activitySuspended.Contains(slot);
                float radius = suspended ? ActivityWakeRadius : ActivitySleepRadius;
                bool keepAwake = slot.Group == "dig" || slot.Group == "vault" || slot.Kind == "boss" || revealUntil > Time.time
                    || (slot.Ai != null && (slot.Ai.aimTarget != null || slot.Ai.searchedEnemy != null || slot.Ai.isNoticing(6f)));
                bool near = (character.transform.position - at).sqrMagnitude <= radius * radius;
                if (suspended)
                {
                    if (!near && !keepAwake) continue;
                    PrepareActivityNavigation(character);
                    SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);
                    character.gameObject.SetActive(true);
                    activitySuspended.Remove(slot);
                }
                else if (!near && !keepAwake && character.gameObject.activeSelf)
                {
                    PrepareActivityNavigation(character);
                    character.gameObject.SetActive(false);
                    activitySuspended.Add(slot);
                }
            }
        }

        private void PrepareActivityNavigation(CharacterMainControl character)
        {
            // 在停用和恢复两端都清掉旧回调/路径，恢复前装回当前地图 mask。
            foreach (Seeker seeker in character.GetComponentsInChildren<Seeker>(true))
            { seeker.CancelCurrentPathRequest(); seeker.graphMask = navigation.Mask; }
            foreach (AI_PathControl path in character.GetComponentsInChildren<AI_PathControl>(true)) path.StopMove();
        }

        private void WakeActivityForBeacon()
        {
            nextActivity = 0f;
            TickActivity();
        }

        private void ClearActivity() { activitySuspended.Clear(); }
    }
}
