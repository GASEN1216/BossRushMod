// ============================================================================
// PetNestModeGate.cs - 遗种巢随从进局的模式门控（实施计划 步骤 6）
// ============================================================================
// 首版门控表（设计提案 §5.4）：
//   ✅ 标准三档 / Mode D 白手起家 / Mode E 划地为营 / Mode F 血猎追击
//   ❌ Mode G 宿命回响 —— 三轴反制遥测只认玩家直伤，崽的伤害会侵蚀"总血贡献"
//      分母语义，且宿敌叙事容不下第三者。**入口一刀切禁，不做局内特判**。
//   ❌ 末日丧尸模式 —— 独立生命周期与独立奖励系统（ZombieMode/AGENTS.md 边界）。
//   ❌ Mode H 百战留痕 —— 观战模式：玩家不下场，擂台由 arena isolation lease 独占并
//      清空原生敌人，塞一只随从进去只会污染隔离核对。**实装期新增的保守判定，
//      脑暴 §5.4 的门控表写于 Mode H 立项之前，`Needs owner confirmation`。**
//   ✅ 基地 —— 自由活动（不走本门控，见基地闲逛崽）。
//
// 硬约束（tests/PetNestModeGateGuard.py 守卫）：
//   - **只经公开只读门面判定**，不得引用 ModeG / ZombieMode / ModeH 的内部符号
//     （ModeGRuntimeGates / ZombieModePhaseGuards / ModeHRuntimeGates / 各自的 RunState）；
//   - 判定 no-throw，异常一律 fail-closed 为"不允许带崽"。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>随从进局的模式门控。唯一判定入口。</summary>
    internal static class PetNestModeGate
    {
        /// <summary>禁入模式的稳定原因 id（面板与日志共用）。</summary>
        internal const string ReasonModeG = "mode_g_banned";
        internal const string ReasonZombie = "zombie_mode_banned";
        internal const string ReasonModeH = "mode_h_banned";
        internal const string ReasonNoRunActive = "no_run_active";
        internal const string ReasonQueryFailed = "mode_query_failed";

        /// <summary>
        /// 当前局是否允许带崽。owner 为 null 或任何查询异常一律返回 false。
        /// </summary>
        internal static bool IsCompanionAllowed(ModBehaviour owner, out string blockReasonId)
        {
            blockReasonId = null;
            if (owner == null)
            {
                blockReasonId = ReasonQueryFailed;
                return false;
            }

            try
            {
                // 一刀切禁入名单优先判定：命中即拒，不看是否有别的模式同时活跃
                if (ModBehaviour.IsModeGRunInProgressSafe())
                {
                    blockReasonId = ReasonModeG;
                    return false;
                }
                if (owner.IsZombieModeActive)
                {
                    blockReasonId = ReasonZombie;
                    return false;
                }
                if (ModBehaviour.IsModeHRunInProgressSafe())
                {
                    blockReasonId = ReasonModeH;
                    return false;
                }

                // 允许名单：标准三档 / 竞技场 / D / E / F
                bool allowed = owner.IsActive
                    || owner.IsBossRushArenaActive
                    || owner.IsModeDActive
                    || owner.IsModeEActive
                    || owner.IsModeFActive;

                if (!allowed)
                {
                    blockReasonId = ReasonNoRunActive;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 模式门控查询异常，fail-closed 不带崽: " + e.Message);
                blockReasonId = ReasonQueryFailed;
                return false;
            }
        }

        /// <summary>
        /// 当前是否处于禁入模式（与"没有任何局在跑"区分开：
        /// 前者要给玩家明确的"本模式不能带崽"提示，后者只是还没开局）。
        /// </summary>
        internal static bool IsInBannedMode(ModBehaviour owner)
        {
            if (owner == null) return false;
            try
            {
                return ModBehaviour.IsModeGRunInProgressSafe()
                    || owner.IsZombieModeActive
                    || ModBehaviour.IsModeHRunInProgressSafe();
            }
            catch (Exception)
            {
                // 查询失败按"不在禁入模式"处理：真正的拦截由 IsCompanionAllowed 兜底
                return false;
            }
        }
    }
}
