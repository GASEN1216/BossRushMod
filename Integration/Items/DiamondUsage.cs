// ============================================================================
// DiamondUsage.cs - 钻石物品使用行为
// ============================================================================
// 模块说明：
//   实现钻石物品的使用逻辑：
//   - 有哥布林时：消耗物品，召唤哥布林跑向玩家，增加好感度+5
//   - 无哥布林时：不消耗，显示气泡提示
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using ItemStatsSystem;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// 钻石物品使用行为
    /// </summary>
    public class DiamondUsage : UsageBehavior
    {
        /// <summary>
        /// 显示设置（物品描述中显示的使用说明）
        /// </summary>
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = L10n.T("使用：召唤哥布林", "Use: Summon Goblin")
                };
            }
        }
        
        /// <summary>
        /// 检查物品是否可以使用（始终可用）
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            return true;
        }
        
        /// <summary>
        /// 使用物品时的逻辑
        /// 注意：物品消耗由游戏框架自动处理（CA_UseItem.OnFinish），这里不需要手动消耗
        /// </summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 获取玩家角色
                CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
                if (player == null) return;
                
                // 检查场景中是否存在哥布林
                GoblinNPCController goblinController = ModBehaviour.Instance?.GetGoblinController();
                
                if (goblinController == null)
                {
                    // 无哥布林：显示气泡提示
                    // 注意：即使没有哥布林，物品也会被消耗（由框架处理）
                    ShowNoGoblinHint(player);
                    return;
                }
                
                // 有哥布林：召唤哥布林跑向玩家
                // 物品消耗由游戏框架自动处理，这里只需要触发召唤逻辑
                // 使用正面召唤方法，会在到达后显示爱心和正面对话
                goblinController.RunToPlayerWithDialogue(true);  // true = 正面召唤（钻石）
                
                // 增加好感度（真钻石，哥布林会很开心）- 使用通用API
                AffinityManager.AddPoints(GoblinAffinityConfig.NPC_ID, DiamondConfig.AFFINITY_BONUS);
                
                ModBehaviour.DevLog("[Diamond] 成功召唤哥布林，好感度增加: " + DiamondConfig.AFFINITY_BONUS);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Diamond] 使用物品出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示无哥布林提示气泡
        /// </summary>
        private void ShowNoGoblinHint(CharacterMainControl player)
        {
            try
            {
                string hint = DiamondConfig.GetNoGoblinHint();
                DialogueBubblesManager.Show(hint, player.transform, 2.5f, false, false, -1f, 2f);
            }
            catch { }
        }
    }
}
