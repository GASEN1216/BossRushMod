// ============================================================================
// PetNestModeGate.cs - 遗种巢随从进局的模式门控（实施计划 步骤 6）
// ============================================================================
// owner 明确范围：所有游戏地图与模式允许出战，唯一模式例外是 Mode H 鸭王杯。
// 基地由独立闲逛 owner 生成非战斗实体；其余已初始化关卡不要求 IsRaidMap 或 BossRush 标志。
// Mode G 只统计主角直伤，宠物击杀仍经已登记 Boss 死亡结案；丧尸计数只认本局敌人 marker。
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
        /// <summary>历史诊断 id 保留兼容；G / 丧尸不再参与门控，唯一禁入模式为 H。</summary>
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
                // 唯一禁入模式：看台与擂台由鸭王杯独占。
                if (ModBehaviour.IsModeHRunInProgressSafe() || BossRushMapSelectionHelper.HasPendingModeHEntryIntent())
                {
                    blockReasonId = ReasonModeH;
                    return false;
                }

                // 基地走非战斗闲逛实体，其余游戏关卡一律允许，包括非 Raid 的自定义地图。
                bool allowed = LevelManager.Instance != null && !LevelManager.Instance.IsBaseLevel;

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

    }
}
