// ============================================================================
// NPCGiftInteractable.cs - 通用NPC礼物赠送交互组件
// ============================================================================
// 模块说明：
//   通用的NPC礼物赠送交互组件，支持任意配置了好感度系统的NPC。
//   通过 npcId 参数化，无需为每个NPC创建单独的交互组件。
//   使用 ConfirmDialogUI 组件显示确认对话框
//   遵循 KISS/YAGNI/SOLID 原则
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
            
            // 尝试赠送手持物品
            TryGiveHandheldItem(character);
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
        
        /// <summary>
        /// 尝试赠送玩家手持的物品
        /// </summary>
        private void TryGiveHandheldItem(CharacterMainControl character)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                Item handheldItem = GetPlayerHandheldItem(character);
                
                if (handheldItem == null)
                {
                    string noItemMsg = L10n.T("手上没有拿着东西~", "You're not holding anything~");
                    NPCDialogueSystem.ShowDialogue(npcId, npcController?.transform, noItemMsg);
                    NotificationText.Push(noItemMsg);
                    EndNPCDialogue();
                    return;
                }
                
                // 显示确认对话框
                ShowGiftConfirmDialog(handheldItem, character);
            }, "NPCGiftInteractable.TryGiveHandheldItem");
        }
        
        /// <summary>
        /// 获取玩家当前手持的物品
        /// </summary>
        private Item GetPlayerHandheldItem(CharacterMainControl character)
        {
            if (character == null) return null;
            
            return NPCExceptionHandler.TryExecute(() =>
            {
                var holdAgent = character.CurrentHoldItemAgent;
                if (holdAgent != null && holdAgent.Item != null)
                {
                    return holdAgent.Item;
                }
                return null;
            }, "NPCGiftInteractable.GetPlayerHandheldItem", (Item)null);
        }
        
        /// <summary>
        /// 显示赠送确认对话框
        /// </summary>
        private void ShowGiftConfirmDialog(Item item, CharacterMainControl character)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                var config = GetNPCConfig();
                string npcName = config?.DisplayName ?? npcId;
                string itemName = item.DisplayName ?? L10n.T("物品", "Item");
                
                string message = L10n.T(
                    "将赠送 <color=red>" + itemName + "</color> 给" + npcName,
                    "Give <color=red>" + itemName + "</color> to " + npcName
                );
                
                // 使用 ConfirmDialogUI 组件显示确认对话框
                ConfirmDialogUI.Show(
                    message,
                    () => OnGiftConfirmed(item, character),
                    () => OnGiftCanceled()
                );
            }, "NPCGiftInteractable.ShowGiftConfirmDialog");
        }
        
        /// <summary>
        /// 确认赠送
        /// </summary>
        private void OnGiftConfirmed(Item item, CharacterMainControl character)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                ConfirmDialogUI.Hide();
                
                string itemName = item.DisplayName ?? L10n.T("物品", "Item");
                ModBehaviour.DevLog("[NPCGift] 玩家确认赠送: " + itemName + " 给 " + npcId);
                
                // 使用通用礼物系统赠送
                bool success = NPCGiftSystem.GiveGift(npcId, item, npcController?.transform, npcController);
                
                if (success)
                {
                    // 从玩家手中移除物品
                    if (character != null)
                    {
                        NPCExceptionHandler.TryExecute(() => character.ChangeHoldItem(null), 
                            "NPCGiftInteractable.OnGiftConfirmed - ChangeHoldItem");
                        RemoveItemFromPlayer(character, item);
                    }
                }
                else
                {
                    NPCDialogueSystem.ShowAlreadyGifted(npcId, npcController?.transform);
                }
                
                // 礼物赠送后不显示告别对话（已有礼物反应对话）
                EndNPCDialogue();
            }, "NPCGiftInteractable.OnGiftConfirmed");
        }
        
        /// <summary>
        /// 取消赠送
        /// </summary>
        private void OnGiftCanceled()
        {
            ModBehaviour.DevLog("[NPCGift] 玩家取消了赠送");
            ConfirmDialogUI.Hide();
            EndInteraction();
        }
        
        /// <summary>
        /// 从玩家背包移除物品
        /// </summary>
        private void RemoveItemFromPlayer(CharacterMainControl character, Item item)
        {
            if (character == null || item == null) return;
            
            NPCExceptionHandler.TryExecute(() =>
            {
                Item charItem = character.CharacterItem;
                if (charItem != null)
                {
                    Inventory inventory = charItem.Inventory;
                    if (inventory != null)
                    {
                        inventory.RemoveItem(item);
                        ModBehaviour.DevLog("[NPCGift] 已从玩家背包移除物品: " + item.DisplayName);
                        return;
                    }
                }
                
                Inventory directInventory = character.GetComponent<Inventory>();
                if (directInventory != null)
                {
                    directInventory.RemoveItem(item);
                }
            }, "NPCGiftInteractable.RemoveItemFromPlayer");
        }
    }
}
