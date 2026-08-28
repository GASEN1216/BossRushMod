// ============================================================================
// PetNestDeathSuppressionRegistry.cs - 遗种巢额外死亡掉落抑制（实施计划 步骤 7）
// ============================================================================
// 保险带，不是主路径：
//   随从「重伤不死」的主机制是致死钳制链第四消费者（钳 1 血 + 退场）。
//   但极端时序下（无敌窗口未及生效、非 Hurt 路径的直接置死）随从仍可能真的进入
//   死亡分支。本注册表让 Patches/Combat/CharacterOnDeadPatch 的 Prefix 在命中随从
//   身份时**跳过本 Mod 的两个额外掉落 handler**（霜之哀伤、幽灵女巫镰刀）——
//   随从倒下不是战利品事件。
//
// 契约（与 ModeG / ModeH 的同款注册表一致）：
//   - 命中时**只**跳过本 Mod 的额外掉落 handler，不得返回 false、不得跳过或改写
//     原版 OnDead 与 Health.OnDead；
//   - 查询是 O(1) 引用身份比较，未激活时零分配快路径；
//   - 异常一律 fail-open=false（让原两个 handler 继续），绝不打断宿主 OnDead。
//
// 身份来源：直接复用 PetNestCompanionAgent 的静态身份表，不再维护第二份簿记——
// 随从的登记/退表已经与组件同寿命，复制一份只会多一个失步来源。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>遗种巢额外死亡掉落抑制身份表。薄封装，身份来源是随从组件的静态表。</summary>
    public static class PetNestDeathSuppressionRegistry
    {
        /// <summary>
        /// 抑制表是否激活。零分配 bool 快路径：没有随从在场时死亡热路径直接返回。
        /// </summary>
        public static bool IsSuppressionArmed
        {
            get
            {
                try { return PetNestCompanionAgent.IsCompanionArmed; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>
        /// 该 Health 对应的死亡是否属于遗种巢随从。
        /// 异常 fail-open=false：抑制表故障不得拖崩宿主死亡流程。
        /// </summary>
        public static bool IsPetNestOnDeadSuppressionActive(Health deadHealth)
        {
            if (deadHealth == null) return false;
            try
            {
                return PetNestCompanionAgent.IsCompanionHealth(deadHealth);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
