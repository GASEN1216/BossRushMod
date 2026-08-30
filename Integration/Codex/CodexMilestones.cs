// ============================================================================
// CodexMilestones.cs - 鸭皇图鉴里程碑判定
// ============================================================================
// 职责：
//   把图鉴的解锁进度翻译成既有成就系统的一次 TryUnlock 调用。
//
// 硬约束：
//   - **零新发奖管线**：奖励、通知、存档全部由 BossRushAchievementManager 负责，
//     本文件只做判定与转发。成就 id 在 Achievement/BossRushAchievementManager.cs
//     的 RegisterAllAchievements() 注册（主控接线，见交付说明 E-2）。
//   - **幂等**：BossRushAchievementManager.TryUnlock 自带"已解锁则返回 false"的
//     幂等语义；本文件再加一层 _lastEvaluatedUnlockedCount 短路，避免每次击杀都
//     白跑四次字典查询（解锁数没变就什么都不用判）。
//   - **全程 no-throw**：调用点在击杀结算链上，异常不得回灌 Health.OnDead。
//   - 成就系统未初始化时 TryUnlock 会直接返回 false，因此先调一次幂等的
//     Initialize()（已初始化时是一次 bool 早返）。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>图鉴里程碑判定。只调既有成就系统，零新发奖管线。</summary>
    internal static class CodexMilestones
    {
        /// <summary>
        /// 上一次已评估过的解锁条目数。-1 表示"未评估过"，强制下次全量判定。
        /// 它只是省调用的短路缓存，不是真值——真值永远是成就系统自己的已解锁集合。
        /// </summary>
        private static int _lastEvaluatedUnlockedCount = -1;

        /// <summary>
        /// 一次击杀之后的里程碑评估。全程 no-throw。
        /// lastKillSeconds &lt;= 0 表示本次没有有效计时（不参与速杀判定）。
        /// </summary>
        internal static void Evaluate(CodexData data, float lastKillSeconds)
        {
            if (data == null) return;

            try
            {
                BossRushAchievementManager.Initialize();

                int unlocked = data.UnlockedCount;
                if (unlocked != _lastEvaluatedUnlockedCount)
                {
                    EvaluateUnlockCount(unlocked);
                }

                // 速杀与解锁数无关：同一个 Boss 反复速杀也应该能补发
                if (lastKillSeconds > 0f && lastKillSeconds <= CodexTuning.FastKillSeconds)
                {
                    BossRushAchievementManager.TryUnlock(CodexTuning.AchievementFastKill);
                }
            }
            catch (Exception e)
            {
                LogFailure("evaluate", e);
            }
        }

        /// <summary>
        /// 面板打开时的补发评估（老档补齐；不传 lastKillSeconds）。
        /// 目录数量会随 Boss 池筛选变化，因此这里强制清短路缓存后全量重判，
        /// 否则"筛掉几个 Boss 之后其实已经集齐"的情况永远发不出全录成就。
        /// </summary>
        internal static void EvaluateOnPanelOpen(CodexData data)
        {
            if (data == null) return;

            try
            {
                BossRushAchievementManager.Initialize();
                _lastEvaluatedUnlockedCount = -1;
                EvaluateUnlockCount(data.UnlockedCount);
            }
            catch (Exception e)
            {
                LogFailure("panel_open", e);
            }
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建 / 切档）。</summary>
        internal static void ResetStaticCaches()
        {
            _lastEvaluatedUnlockedCount = -1;
        }

        #region 私有

        /// <summary>按解锁条目数判定四个累计里程碑。</summary>
        private static void EvaluateUnlockCount(int unlocked)
        {
            if (unlocked <= 0)
            {
                _lastEvaluatedUnlockedCount = unlocked;
                return;
            }

            BossRushAchievementManager.TryUnlock(CodexTuning.AchievementFirstEntry);

            if (unlocked >= CodexTuning.MilestoneTenThreshold)
            {
                BossRushAchievementManager.TryUnlock(CodexTuning.AchievementTen);
            }

            if (unlocked >= CodexTuning.MilestoneTwentyThreshold)
            {
                BossRushAchievementManager.TryUnlock(CodexTuning.AchievementTwenty);
            }

            // 目录为空时（尚未构建 / 构建失败）绝不发全录成就：
            // unlocked >= 0 恒成立会白送一个最高难度成就。
            int total = CodexBossCatalog.Count;
            if (total > 0 && unlocked >= total)
            {
                BossRushAchievementManager.TryUnlock(CodexTuning.AchievementAll);
            }

            _lastEvaluatedUnlockedCount = unlocked;
        }

        private static void LogFailure(string stage, Exception e)
        {
            try
            {
                ModBehaviour.DevLog(CodexTuning.LogPrefix + "[WARNING] 里程碑评估 "
                    + stage + " 异常: " + e.Message);
            }
            catch (Exception)
            {
                // 连日志都失败时静默：诊断路径绝不拖崩宿主
            }
        }

        #endregion
    }
}
