using System;

namespace BossRush
{
    internal struct RollCallLedgerEntry
    {
        internal int TargetId;
        internal float HitDamage;
    }

    /// <summary>点名窗口、去重、冷却与首击快照；独立于 Unity，执行回归直接链接本文件。</summary>
    internal sealed class RollCallLedgerRules
    {
        private readonly RollCallLedgerEntry[] entries;
        private readonly float windowSeconds;
        private readonly float cooldownSeconds;
        private int count;
        private float expiresAt;
        private float nextTriggerAt;

        internal RollCallLedgerRules(int targetCount, float windowSeconds, float cooldownSeconds)
        {
            if (targetCount < 1) throw new ArgumentOutOfRangeException("targetCount");
            if (!IsFinite(windowSeconds) || windowSeconds <= 0f) throw new ArgumentOutOfRangeException("windowSeconds");
            if (!IsFinite(cooldownSeconds) || cooldownSeconds < 0f) throw new ArgumentOutOfRangeException("cooldownSeconds");
            entries = new RollCallLedgerEntry[targetCount];
            this.windowSeconds = windowSeconds;
            this.cooldownSeconds = cooldownSeconds;
        }

        internal int Count { get { return count; } }
        internal float ExpiresAt { get { return expiresAt; } }
        internal float NextTriggerAt { get { return nextTriggerAt; } }

        /// <summary>
        /// 接受一个新名字才返回 true；第三名返回独立快照，并在调用方发出任何伤害前清名单、进冷却。
        /// 窗口从第一名开始，不被重复命中或第二名延长；恰好到窗口终点仍算本轮。
        /// </summary>
        internal bool ObserveHit(int targetId, float hitDamage, float now,
            out int ordinal, out RollCallLedgerEntry[] completed)
        {
            ordinal = 0;
            completed = null;
            if (targetId == 0 || !IsFinite(hitDamage) || hitDamage <= 0f || !IsFinite(now)) return false;
            Expire(now);
            if (now < nextTriggerAt) return false;
            for (int i = 0; i < count; i++)
                if (entries[i].TargetId == targetId) return false;

            if (count == 0) expiresAt = now + windowSeconds;
            entries[count] = new RollCallLedgerEntry { TargetId = targetId, HitDamage = hitDamage };
            ordinal = ++count;
            if (count < entries.Length) return true;

            completed = (RollCallLedgerEntry[])entries.Clone();
            nextTriggerAt = now + cooldownSeconds;
            ResetSequence();
            return true;
        }

        internal bool Expire(float now)
        {
            if (count == 0 || !IsFinite(now) || now <= expiresAt) return false;
            ResetSequence();
            return true;
        }

        /// <summary>卸下装备清本轮名单，但不能洗掉已经开始的冷却。</summary>
        internal void ResetSequence()
        {
            Array.Clear(entries, 0, entries.Length);
            count = 0;
            expiresAt = 0f;
        }

        internal void ResetAll()
        {
            ResetSequence();
            nextTriggerAt = 0f;
        }

        internal static float CalculateBonus(float hitDamage, float baseDamage, float ratio)
        {
            if (!IsFinite(hitDamage) || hitDamage < 0f || !IsFinite(baseDamage) || baseDamage < 0f
                || !IsFinite(ratio) || ratio < 0f) return 0f;
            float value = baseDamage + hitDamage * ratio;
            return IsFinite(value) ? value : 0f;
        }

        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
