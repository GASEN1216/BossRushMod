// ============================================================================
// NPCGiftInteractable.cs - 通用NPC礼物赠送交互组件
// ============================================================================
// 通用的NPC礼物赠送交互组件，支持任意配置了好感度系统的NPC。
// 通过 npcId 参数化，无需为每个NPC创建单独的交互组件。
// 使用容器式UI（LootView）让玩家放入礼物进行赠送。
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using Duckov.UI;
using BossRush.UI;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 通用NPC礼物赠送交互组件
    /// </summary>
    public class NPCGiftInteractable : NPCInteractableBase
    {
        // ============================================================================
        // 交互设置
        // ============================================================================
        
        protected override void SetupInteractName()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_GiveGift";
                this.InteractName = "BossRush_GiveGift";
            }, "NPCGiftInteractable.SetupInteractName");
        }
        
        protected override bool IsInteractable()
        {
            // 始终可交互，即使今日已赠送（会显示不同的对话）
            return isInitialized && !string.IsNullOrEmpty(npcId);
        }
        
        // ============================================================================
        // 交互逻辑
        // ============================================================================
        
        protected override void DoInteract(CharacterMainControl character)
        {
            ModBehaviour.DevLog("[NPCGift] 玩家选择赠送礼物给: " + npcId);
            
            // 让NPC进入对话状态
            StartNPCDialogue();
            
            // 检查今日是否已赠送
            if (!NPCGiftSystem.CanGiftToday(npcId))
            {
                ShowAlreadyGiftedDialogue();
                return;
            }
            
            // 获取NPC配置
            var config = GetNPCConfig();
            var containerConfig = config as INPCGiftContainerConfig;
            
            // 打开容器式UI
            ModBehaviour.DevLog("[NPCGift] 打开礼物容器UI，NPC: " + npcId);
            NPCGiftContainerService.OpenService(npcId, npcController?.transform, containerConfig, npcController);
        }
        
        /// <summary>
        /// 显示今日已赠送礼物的对话
        /// </summary>
        private void ShowAlreadyGiftedDialogue()
        {
            string dialogue = NPCGiftSystem.GetAlreadyGiftedDialogue(npcId);
            NPCDialogueSystem.ShowDialogue(npcId, npcController?.transform, dialogue);
            
            ModBehaviour.DevLog("[NPCGift] 今日已赠送，显示对话: " + dialogue);
            EndNPCDialogue();
        }
    }
}
