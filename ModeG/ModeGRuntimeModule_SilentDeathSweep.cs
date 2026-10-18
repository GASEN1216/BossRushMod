using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class ModeGRuntimeModule
    {
        private readonly List<Health> _deadSweepScratch = new List<Health>(3);
        private float _nextDeadSweepTime;
        private const float DeadSweepIntervalSeconds = 0.25f;

        // Health.OnDead 的前置订阅者抛异常会截断遥测订阅。只补正式战斗中已登记的 Boss，
        // 生成 / 批量激活期仍由既有 TechnicalLoss 路由处理；MarkKilled 保证重复事件不重结算。
        private void SweepSilentBossDeaths()
        {
            if (!CoopRunHasWorldAuthority || !CanContinueRun()
                || _waveSpawnInFlight || _batchActivationInProgress || _spawnTransaction.ActiveBossCount == 0) return;
            if (_state.combatPhase != ModeGCombatPhase.Fighting && _state.combatPhase != ModeGCombatPhase.LastStand) return;
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextDeadSweepTime) return;
            _nextDeadSweepTime = now + DeadSweepIntervalSeconds;

            _deadSweepScratch.Clear();
            _spawnTransaction.CollectActiveBossHealth(_deadSweepScratch);
            bool settled = false;
            for (int i = 0; i < _deadSweepScratch.Count; i++)
            {
                Health health = _deadSweepScratch[i];
                if (ReferenceEquals(health, null)) continue;
                bool dead;
                try { dead = health == null || health.IsDead; }
                catch { dead = false; }
                if (!dead) continue;
                ModBehaviour.DevLog("[ModeG] [WARNING] Boss death missed OnDead; settling registered boss");
                // 先发原资产的个人死亡事实：HandleBossDead 会注销 exact Health，之后无法再归因。
                ModeGEntryRuntime.PublishCoopCombat(health, default(DamageInfo), true);
                HandleBossDead(health, default(DamageInfo));
                settled = true;
            }
            _deadSweepScratch.Clear();
            if (settled && _spawnTransaction.ActiveBossCount == 0) _waveSettlementPending = true;
        }
    }
}
