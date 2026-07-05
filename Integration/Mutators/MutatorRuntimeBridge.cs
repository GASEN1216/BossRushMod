// ============================================================================
// MutatorRuntimeBridge.cs - 变异词条统一启停入口
// ============================================================================
// 模块说明：
//   把"读 BossRushConfig.enableMutators / mutatorCount → 抽词条 → 显示横幅"这套
//   重复模板抽到一个公共入口，给标准 BossRush / Mode D / Mode E / Mode F 复用，
//   避免每个模式自己写一遍 try-catch + 数值 Clamp + UI 触发。
//
// 设计原则：
//   - 不强制依赖 ModBehaviour 实例，所有逻辑走 partial class 补丁，便于其它 partial 调用
//   - 抽取阶段：考虑配置开关 / 玩家是否存在 / 词条数量上下限
//   - 清理阶段：MutatorManager.RemoveAll 内部自带空守卫，可在所有退出路径上幂等调用
//   - 其它模式走这里的"随机种子"路径
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 给当前模式抽并应用变异词条。
        /// 标准 BossRush / 无尽地狱 / Mode D / E / F 各自在开局流程里调用一次，
        /// modeTag 用于过滤只在特定模式生效的词条（如流血加速仅 ModeF）。
        /// </summary>
        /// <param name="modeTag">日志前缀兼模式过滤标签，例如 "BossRush" / "ModeD" / "ModeE" / "ModeF"</param>
        internal void TryRollMutatorsForMode(string modeTag)
        {
            try
            {
                if (config == null || !config.enableMutators) return;

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;

                int count = ClampMutatorCount(config.mutatorCount);
                MutatorManager.RollAndApply(player, count, null, modeTag);
                MutatorUI.ShowBanner();

                DevLog("[" + (string.IsNullOrEmpty(modeTag) ? "Mutator" : modeTag) +
                       "] 已抽取并应用 " + count + " 条变异词条");
            }
            catch (Exception e)
            {
                DevLog("[" + (string.IsNullOrEmpty(modeTag) ? "Mutator" : modeTag) +
                       "] [WARNING] 变异词条抽取失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理变异词条（所有非标准 BossRush 模式的退出路径都该调一次，幂等）
        /// </summary>
        /// <param name="modeTag">日志前缀</param>
        internal void ClearMutatorsForMode(string modeTag)
        {
            try
            {
                MutatorManager.RemoveAll();
                MutatorUI.HideAll();
            }
            catch (Exception e)
            {
                DevLog("[" + (string.IsNullOrEmpty(modeTag) ? "Mutator" : modeTag) +
                       "] [WARNING] 变异词条清理失败: " + e.Message);
            }
        }
    }
}
