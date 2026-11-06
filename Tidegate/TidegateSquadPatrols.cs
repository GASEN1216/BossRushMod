using UnityEngine;

namespace BossRush
{
    internal sealed partial class TidegateEnemies
    {
        // 只在原有三秒采样中驱动镇街/渔港的小组。路口和伙伴的巡逻侧位是这组看得见的两端；
        // 抵达后驻足六秒再折返。听见枪声或找到敌人时，移动和瞄准归官方战斗 AI。
        private void TickSquadPatrol(Slot slot)
        {
            Slot lead = slot.Lead ?? slot;
            if (!lead.HasPatrol || slot.Ai.searchedEnemy != null || slot.Ai.isNoticing(4f)) return;
            if (lead.Character == null || lead.Character.Health == null || lead.Character.Health.IsDead || lead.Ai == null)
            {
                HoldPatrol(slot);
                return;
            }
            if (lead.Ai.searchedEnemy != null || lead.Ai.isNoticing(4f)) return;
            if (lead == slot)
            {
                if (lead.PatrolLeg == 0 || Time.time >= lead.GiveUpAt)
                {
                    if (Time.time < lead.HoldUntil) return;
                    if (!StartPatrolLeg(lead)) return;
                }
                else if ((lead.Character.transform.position - lead.PatrolGoal).sqrMagnitude <= 4f * 4f)
                {
                    if (lead.HoldUntil < 0f) lead.HoldUntil = Time.time + 6f;
                    if (Time.time < lead.HoldUntil) return;
                    if (!StartPatrolLeg(lead)) return;
                }
            }
            if (lead.PatrolLeg == 0 || tide.IsDeepAt(lead.PatrolGoal)) { HoldPatrol(slot); return; }
            Vector3 target = lead.PatrolGoal;
            if (slot != lead)
            {
                // 巡同一路口，错开一个肩位；只在这一段首帧找一次可走位置，不能走就用首哨已验的路点。
                if (slot.LastLeg != lead.PatrolLeg)
                {
                    Vector3 side = slot.Home - lead.Home;
                    side.y = 0f;
                    Vector3 beside = target + side.normalized * 2.2f, snapped, dry;
                    slot.PatrolGoal = navigation.TryNearestWalkable(beside, out snapped) && (snapped - beside).sqrMagnitude <= 4f
                        && CanStand(snapped, out dry) && SamePatrolArea(target, dry) ? dry : target;
                }
                target = slot.PatrolGoal;
            }
            if (tide.IsDeepAt(target)) { HoldPatrol(slot); return; }
            bool newLeg = slot.LastLeg != lead.PatrolLeg;
#if BOSSRUSH_DEV
            DevObservePatrol(slot, target, newLeg);
#endif
            slot.LastLeg = lead.PatrolLeg;
            slot.Ai.patrolPosition = target;
            if ((slot.Character.transform.position - target).sqrMagnitude > 3f * 3f && Time.time >= slot.NextMove
                && !slot.Ai.WaitingForPathResult() && (newLeg || !slot.Ai.HasPath()))
            {
                slot.NextMove = Time.time + 5f;
                slot.Ai.MoveToPos(target);
            }
        }

        private static void HoldPatrol(Slot slot)
        {
            if (slot.LastLeg < 0) return;
            slot.LastLeg = -1;
            slot.Ai.patrolPosition = slot.Character.transform.position;
            slot.Ai.StopMove();
        }

        private bool StartPatrolLeg(Slot lead)
        {
            Vector3 desired = lead.PatrolOut ? lead.Home : lead.PatrolPost, snapped, dry;
            if (!navigation.TryNearestWalkable(desired, out snapped) || (snapped - desired).sqrMagnitude > 4f * 4f
                || !CanStand(snapped, out dry) || !SamePatrolArea(lead.Character.transform.position, dry))
            {
                lead.HoldUntil = Time.time + 9f;
                lead.GiveUpAt = Time.time;
                lead.PatrolLeg = 0;
                lead.PatrolOut = false;
                HoldPatrol(lead);
                return false;
            }
            lead.PatrolOut = !lead.PatrolOut;
            lead.PatrolGoal = dry;
            lead.PatrolLeg++;
            lead.HoldUntil = -1f;
            lead.GiveUpAt = Time.time + 35f;
            return true;
        }
    }
}
