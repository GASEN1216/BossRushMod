// ============================================================================
// RandomEventModeGate.cs - 随机事件的模式门控（方案二 步骤 3）
// ============================================================================
// 门控表（方案二 §模式门控矩阵，九入口逐个实读代码定案）：
//   ✅ 标准三档 BossRush / 无间炼狱（owner.IsActive）
//   ✅ 白手起家 Mode D（owner.IsModeDActive，空投品质另行压低）
//   ❌ 划地为营 Mode E —— 多阵营 + 扫箱令交互语义不清，要开需另行立项。
//   ❌ 血猎追击 Mode F —— 悬赏雷达只认注册 Boss，阶段播报会与事件横幅打架。
//   ❌ 宿命回响 Mode G —— 九波固定编排 + 反制遥测，运行期连 Legacy tick 都整体冻结。
//   ❌ 斗蛐蛐 Mode H —— 观战模式，擂台由隔离租约独占并清空原生敌人。
//   ❌ 末日丧尸 —— 独立生命周期与独立奖励体系（ZombieMode/AGENTS.md 边界）。
//   ❌ 普通撤离图 / 基地 —— 官方 spawner、任务与天气都活跃，本系统定位是「BossRush 局内」。
//   ❌ 空闲竞技场（IsBossRushArenaActive）—— **刻意不入允许名单**：
//      空闲竞技场的维护循环会把事件生成物当野怪/野箱清掉，等于事件凭空消失。
//
// 硬约束（tests/RandomEventsModeGateGuard.py 守卫）：
//   - 一刀切**禁入名单先判**，命中即拒，不看是否有别的模式同时活跃；
//   - **只经公开只读门面判定**，不得引用任何模式子系统的内部符号；
//   - 判定 no-throw，任何异常一律 fail-closed 为「不允许调度事件」；
//   - 唯一判定入口：调度器、HUD、F3 都只能问这里，禁止另写一份模式判定。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>随机事件的模式门控。唯一判定入口。只经公开只读门面，禁引内部符号。</summary>
    internal static class RandomEventModeGate
    {
        #region 稳定原因 id（面板、日志、F3 共用）

        internal const string ReasonModeG = "mode_g_banned";
        internal const string ReasonModeH = "mode_h_banned";
        internal const string ReasonZombie = "zombie_mode_banned";
        internal const string ReasonModeE = "mode_e_banned";
        internal const string ReasonModeF = "mode_f_banned";
        internal const string ReasonNoRunActive = "no_run_active";
        internal const string ReasonSceneNotReady = "scene_not_ready";
        internal const string ReasonQueryFailed = "mode_query_failed";

        #endregion

        #region 判定

        /// <summary>
        /// 当前是否允许调度随机事件。owner 为 null 或任何查询异常一律返回 false。
        /// </summary>
        internal static bool IsEventsAllowed(ModBehaviour owner, out string blockReasonId)
        {
            blockReasonId = null;
            if (owner == null)
            {
                blockReasonId = ReasonQueryFailed;
                return false;
            }

            try
            {
                // ── 一刀切禁入名单优先判定：命中即拒 ────────────────────────────
                if (ModBehaviour.IsModeGRunInProgressSafe())
                {
                    blockReasonId = ReasonModeG;
                    return false;
                }
                if (ModBehaviour.IsModeHRunInProgressSafe())
                {
                    blockReasonId = ReasonModeH;
                    return false;
                }
                if (owner.IsZombieModeActive)
                {
                    blockReasonId = ReasonZombie;
                    return false;
                }
                if (owner.IsModeEActive)
                {
                    blockReasonId = ReasonModeE;
                    return false;
                }
                if (owner.IsModeFActive)
                {
                    blockReasonId = ReasonModeF;
                    return false;
                }

                // ── 允许名单：标准三档 / 无间炼狱（IsActive）+ 白手起家 ──────────
                //    ⚠️ 刻意不含 IsBossRushArenaActive（空闲竞技场会清掉事件生成物）。
                bool allowed = owner.IsActive || owner.IsModeDActive;
                if (!allowed)
                {
                    blockReasonId = ReasonNoRunActive;
                    return false;
                }

                // ── 场景就绪：切图中 / 加载屏 / 主菜单一律不调度 ────────────────
                if (!SceneRuntimeGate.CanRunGameplayRuntimeNow(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
                {
                    blockReasonId = ReasonSceneNotReady;
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix
                    + "模式门控查询异常，fail-closed 不调度: " + e.Message);
                blockReasonId = ReasonQueryFailed;
                return false;
            }
        }

        /// <summary>
        /// 局身份签名：允许时返回非 0，禁止时返回 0。
        /// 值 = 允许位（1 = 标准/无间炼狱，2 = 白手起家）左移 16 位后混入场景 buildIndex。
        /// 调度器用「签名边缘轮询」判定开局与局末，从而不必侵入四处以上的模式启停代码。
        /// no-throw：异常返回 0（= 不调度）。
        /// </summary>
        internal static int ComputeRunSignature(ModBehaviour owner)
        {
            try
            {
                string ignoredReason;
                if (!IsEventsAllowed(owner, out ignoredReason)) return 0;

                int bits = 0;
                if (owner.IsActive) bits |= 1;
                if (owner.IsModeDActive) bits |= 2;

                int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                // IsEventsAllowed 已保证 bits >= 1，末位再或上 1 兜底，确保结果恒非 0
                return (bits << 16) ^ (scene & 0xFFFF) ^ 0x0001;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        #endregion
    }
}
