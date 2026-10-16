using System.Collections.Generic;

namespace BossRush
{
    /// <summary>已登记 Boss 死亡兜底轮询（从 ModeGRuntimeModule.cs 原样提取，行数预算见 LargeFileBudgetGuard）。</summary>
    internal sealed partial class ModeGRuntimeModule
    {
        private readonly List<Health> _deadSweepScratch = new List<Health>(3);
        private float _nextDeadSweepTime;
        /// <summary>已登记 Boss 死亡兜底轮询间隔（秒）：只比几个 IsDead，玩家感觉不到这点延迟。</summary>
        private const float DeadSweepIntervalSeconds = 0.25f;

        /// <summary>
        /// 主路由收不到的 Boss 死亡在这里补结案。官方 Health.OnDead 是多播委托，前面任何一个订阅者抛异常，
        /// 后面的订阅者（包括本模式的遥测）都收不到这次死亡：创意工坊击杀提示类 Mod 不判空就读攻击者，
        /// 流血 / 毒区这类没有攻击者的死亡必然抛 NRE。Boss 倒了却不计击杀，整波一直卡着
        /// （玩家反馈 2026-10-16：宿命回响 Boss 流血死亡后卡进度）。
        /// 只在战斗 / 最后处决阶段扫：生成与批量激活期间的死亡归技术丢失，交给主路由判。
        /// </summary>
        private void SweepSilentBossDeaths()
        {
            if (_waveSpawnInFlight || _batchActivationInProgress || _spawnTransaction.ActiveBossCount == 0) return;
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
                ModBehaviour.DevLog("[ModeG] [WARNING] Boss 死亡没收到 OnDead（可能被别的订阅者异常截断），兜底结案");
                HandleBossDead(health, default(DamageInfo));
                settled = true;
            }
            _deadSweepScratch.Clear();
            // HandleBossDead 已 MarkKilled 但后续路由抛异常时，存活归零也必须进结算，不能留在半截。
            if (settled && _spawnTransaction.ActiveBossCount == 0) _waveSettlementPending = true;
        }
    }
}
