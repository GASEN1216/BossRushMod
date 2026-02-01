// ============================================================================
// BrickStoneUsage.cs - 砖石物品使用行为
// ============================================================================
// 模块说明：
//   实现砖石物品的使用逻辑：
//   - 有哥布林时：消耗物品，召唤哥布林跑向玩家并急停3秒
//   - 无哥布林时：不消耗，显示气泡提示
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// 砖石物品使用行为
    /// </summary>
    public class BrickStoneUsage : UsageBehavior
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
                    // 无哥布林：显示气泡提示，不消耗物品
                    ShowNoGoblinHint(player);
                    return;
                }
                
                // 有哥布林：消耗物品并召唤
                if (ConsumeOneItem(item, player))
                {
                    goblinController.RunToPlayer();
                    ModBehaviour.DevLog("[BrickStone] 成功召唤哥布林");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BrickStone] 使用物品出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示无哥布林提示气泡
        /// </summary>
        private void ShowNoGoblinHint(CharacterMainControl player)
        {
            try
            {
                string hint = BrickStoneConfig.GetNoGoblinHint();
                DialogueBubblesManager.Show(hint, player.transform, 2.5f, false, false, -1f, 2f);
            }
            catch { }
        }
        
        /// <summary>
        /// 消耗一个物品
        /// </summary>
        private bool ConsumeOneItem(Item item, CharacterMainControl player)
        {
            try
            {
                if (item == null || player == null) return false;
                
                var inventory = player.CharacterItem?.Inventory;
                if (inventory == null) return false;
                
                if (item.Stackable && item.StackCount > 1)
                {
                    item.StackCount -= 1;
                }
                else
                {
                    inventory.RemoveItem(item);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
