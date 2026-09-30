using System;

namespace BossRush
{
    /// <summary>
    /// COMPAT：普通巡逻敌人的纯调度状态。服务持有 Actor、距离与重试退避，本类只管理租约与活跃预算。
    /// 单线程使用；方法不依赖 Unity，不在推进时分配对象。
    /// </summary>
    internal sealed class SkyIslandPatrolSchedule
    {
        private enum SlotState : byte { Unused, Pending, Active, Suspended, Defeated }

        private readonly SlotState[] states;
        private readonly int activeLimit;
        private int generation, activeCount, pendingCount;
        private bool closed;

        internal SkyIslandPatrolSchedule(int slotCount, int activeLimit)
        {
            if (slotCount < 0) throw new ArgumentOutOfRangeException("slotCount");
            if (activeLimit < 0) throw new ArgumentOutOfRangeException("activeLimit");
            states = new SlotState[slotCount];
            this.activeLimit = activeLimit;
        }

        internal int Generation { get { return generation; } }
        internal int ActiveCount { get { return activeCount; } }
        internal int PendingCount { get { return pendingCount; } }
        internal bool Closed { get { return closed; } }

        internal bool CanSpawn(int i)
        {
            return !closed && Valid(i) && states[i] == SlotState.Unused && pendingCount == 0 &&
                activeCount + pendingCount < activeLimit;
        }

        /// <summary>成功后再取 Generation 作为本次请求的 token。最多一个未完成的请求。</summary>
        internal bool TryReserve(int i)
        {
            if (!CanSpawn(i)) return false;
            generation = unchecked(generation + 1);
            states[i] = SlotState.Pending;
            pendingCount++;
            return true;
        }

        /// <summary>返回 false 时服务必须回收该请求返回的 Actor；不会接纳旧 token 或重复完成。</summary>
        internal bool CompleteSpawn(int i, int generation, bool activate)
        {
            if (!OwnsPending(i, generation)) return false;
            pendingCount--;
            states[i] = activate ? SlotState.Active : SlotState.Suspended;
            if (activate) activeCount++;
            return true;
        }

        /// <summary>可重试失败归还 Unused；最终失败结束该 slot。旧 token 不能释放新请求。</summary>
        internal bool AbortSpawn(int i, int generation, bool retryable)
        {
            if (!OwnsPending(i, generation)) return false;
            pendingCount--;
            states[i] = retryable ? SlotState.Unused : SlotState.Defeated;
            return true;
        }

        internal bool TryActivate(int i)
        {
            if (closed || !Valid(i) || states[i] != SlotState.Suspended ||
                activeCount + pendingCount >= activeLimit) return false;
            states[i] = SlotState.Active;
            activeCount++;
            return true;
        }

        /// <summary>只停用同一活体，不销毁、不标记死亡；服务保留它的 HP 与装备。</summary>
        internal bool Suspend(int i)
        {
            if (closed || !Valid(i) || states[i] != SlotState.Active) return false;
            states[i] = SlotState.Suspended;
            activeCount--;
            return true;
        }

        internal bool MarkDefeated(int i)
        {
            if (closed || !Valid(i) || states[i] == SlotState.Defeated) return false;
            if (states[i] == SlotState.Active) activeCount--;
            else if (states[i] == SlotState.Pending) pendingCount--;
            states[i] = SlotState.Defeated;
            return true;
        }

        internal bool IsSpawned(int i)
        {
            return Valid(i) && (states[i] == SlotState.Active || states[i] == SlotState.Suspended);
        }
        internal bool IsActive(int i) { return Valid(i) && states[i] == SlotState.Active; }
        internal bool IsPending(int i) { return Valid(i) && states[i] == SlotState.Pending; }
        internal bool IsDefeated(int i) { return Valid(i) && states[i] == SlotState.Defeated; }

        /// <summary>首次关闭撤销所有活体与在途记录并推进 token；重复关闭无副作用。关闭不伪造死亡。</summary>
        internal void Close()
        {
            if (closed) return;
            closed = true;
            generation = unchecked(generation + 1);
            activeCount = pendingCount = 0;
            for (int i = 0; i < states.Length; i++)
                if (states[i] != SlotState.Defeated) states[i] = SlotState.Unused;
        }

        private bool Valid(int i) { return i >= 0 && i < states.Length; }
        private bool OwnsPending(int i, int generation)
        {
            return !closed && Valid(i) && generation == this.generation && states[i] == SlotState.Pending;
        }
    }
}
