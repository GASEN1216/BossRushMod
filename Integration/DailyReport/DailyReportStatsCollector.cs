// ============================================================================
// DailyReportStatsCollector.cs - 今日战绩采集（P0/P1）
// ============================================================================
// 硬约束：
//   - Health.OnHurt / OnDead 是全 Mod 最热的事件，采集路径**必须零分配**：
//     只做判空、bool 判定和整数/浮点自增，禁止字符串拼接、LINQ、装箱。
//     角色显示名之类的东西只在跨天结算时取一次，不在这里取。
//   - 事件订阅遵循 AGENTS.md 4.6：私有 bool 幂等 + 命名 handler + 对称退订，
//     禁止匿名 lambda（退订退不掉）。
//   - 开关关闭或模块未 bootstrap 时立即早返，做到真正 dormant。
//
// 订阅落点说明：Health 的两个静态事件由 Utilities/PlayerLifecycleRuntimeHooks.cs
// 统一注册（那是全 Mod 的 Health 订阅集中点）；本类只负责自己的
// EconomyManager / RaidUtilities 订阅与全部 handler 实现。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>日报战绩采集器。静态，无 MonoBehaviour。</summary>
    internal static class DailyReportStatsCollector
    {
        #region 订阅状态

        private static readonly object _lock = new object();
        private static bool _subscribed;

        /// <summary>是否已订阅经济与 raid 事件。</summary>
        internal static bool IsSubscribed { get { return _subscribed; } }

        #endregion

        #region 订阅（幂等 + 对称退订）

        /// <summary>幂等订阅。模块 bootstrap 时调用。</summary>
        internal static void EnsureSubscribed()
        {
            lock (_lock)
            {
                if (_subscribed) return;
                try
                {
                    Duckov.Economy.EconomyManager.OnMoneyChanged += HandleMoneyChanged;
                    RaidUtilities.OnNewRaid += HandleNewRaid;
                    RaidUtilities.OnRaidEnd += HandleRaidEnd;
                    _subscribed = true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 统计事件订阅失败: " + e.Message);
                }
            }
        }

        /// <summary>幂等退订。</summary>
        internal static void ShutdownSubscription()
        {
            lock (_lock)
            {
                if (!_subscribed) return;
                try
                {
                    Duckov.Economy.EconomyManager.OnMoneyChanged -= HandleMoneyChanged;
                    RaidUtilities.OnNewRaid -= HandleNewRaid;
                    RaidUtilities.OnRaidEnd -= HandleRaidEnd;
                }
                catch (Exception)
                {
                    // 退订失败也要把标志置回，避免重复订阅越滚越多
                }
                _subscribed = false;
            }
        }

        #endregion

        #region Health 事件（由 PlayerLifecycleRuntimeHooks 转发）

        /// <summary>
        /// 全局死亡事件。区分「玩家击杀敌人」与「玩家阵亡」两种情况。
        /// 热路径：只做判空 + bool 判定 + 整数自增。
        /// </summary>
        internal static void OnGlobalDead(Health target, DamageInfo info)
        {
            if (!IsActive()) return;
            try
            {
                if (target == null) return;

                if (target.IsMainCharacterHealth)
                {
                    DailyReportService.ReportPlayerDeath();
                    return;
                }

                // 只统计玩家自己打死的，避免把 NPC 互殴、环境伤害算进战绩
                if (info.fromCharacter == null || !info.fromCharacter.IsMainCharacter) return;

                CharacterMainControl victim = target.TryGetCharacter();
                bool isBoss = victim != null && victim.isBossCharacter;
                DailyReportService.ReportKill(isBoss);
            }
            catch (Exception)
            {
                // 统计不是关键路径：热事件里绝不抛，也不打日志（会刷屏）
            }
        }

        /// <summary>
        /// 全局受伤事件。累计双向伤害与最大单次伤害。
        /// 这是全 Mod 最热的事件，实现里不允许出现任何分配。
        /// </summary>
        internal static void OnGlobalHurt(Health target, DamageInfo info)
        {
            if (!IsActive()) return;
            try
            {
                if (target == null || info.fromCharacter == null) return;

                float dmg = info.finalDamage;
                if (dmg <= 0f) return;

                bool fromPlayer = info.fromCharacter.IsMainCharacter;
                bool toPlayer = target.IsMainCharacterHealth;

                if (fromPlayer && !toPlayer)
                {
                    DailyReportService.ReportDamageDealt(dmg);
                }
                else if (!fromPlayer && toPlayer)
                {
                    DailyReportService.ReportDamageTaken(dmg);
                }
            }
            catch (Exception)
            {
                // 同上：热路径静默
            }
        }

        #endregion

        #region 经济 / raid 事件

        /// <summary>
        /// 本 Mod 自己发钱时置位，避免奖金被当成玩家「进账」计入当日统计。
        /// 只在同步的发放调用外包一层，窗口极窄；官方 OnMoneyChanged 是同步派发。
        /// </summary>
        private static bool _suppressMoneyDelta;

        /// <summary>发放路径用：包住自家 EconomyManager.Add，防止奖金自我计入统计。</summary>
        internal static void SetMoneyDeltaSuppressed(bool suppressed)
        {
            _suppressMoneyDelta = suppressed;
        }

        /// <summary>金钱变动。官方给的是 (旧值, 新值)，这里换算成差值。</summary>
        private static void HandleMoneyChanged(long oldValue, long newValue)
        {
            if (_suppressMoneyDelta) return;
            if (!IsActive()) return;
            try
            {
                DailyReportService.ReportMoneyDelta(newValue - oldValue);
            }
            catch (Exception)
            {
                // 静默
            }
        }

        private static void HandleNewRaid(RaidUtilities.RaidInfo info)
        {
            if (!IsActive()) return;
            try
            {
                DailyReportService.ReportRaidStarted();
            }
            catch (Exception)
            {
                // 静默
            }
        }

        /// <summary>
        /// raid 结束。官方在阵亡与撤离两种情况下都会触发，
        /// 用 info.dead 区分——只有非阵亡结束才算成功撤离。
        /// </summary>
        private static void HandleRaidEnd(RaidUtilities.RaidInfo info)
        {
            if (!IsActive()) return;
            try
            {
                if (!info.dead)
                {
                    DailyReportService.ReportExtraction();
                }
            }
            catch (Exception)
            {
                // 静默
            }
        }

        #endregion

        #region 门控

        /// <summary>
        /// 采集是否生效。开关关闭时整条采集链路 dormant。
        /// 这个判断在最热的事件里每次都会跑，因此只读一个 bool 属性，不做任何分配。
        /// </summary>
        private static bool IsActive()
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner == null || !owner.IsDailyReportConfiguredEnabled()) return false;
                // Mode H 整体不进个人战绩（owner 2026-09-03 定）。门控放在这里而不是
                // 逐个 handler：击杀、双向伤害与玩家阵亡是同一个采集器的三类输出，
                // 只挡击杀会让同一场比赛「击杀不算但伤害算」，日报里出现自相矛盾的数据。
                // ERROR 完整互换期间官方会把伤害/击杀来源改写成主角，三类都会被污染。
                if (ModBehaviour.IsModeHRunInProgressSafe()) return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订。</summary>
        internal static void ResetStaticCaches()
        {
            ShutdownSubscription();
        }

        #endregion
    }
}
