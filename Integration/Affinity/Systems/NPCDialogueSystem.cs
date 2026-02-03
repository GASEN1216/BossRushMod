// ============================================================================
// NPCDialogueSystem.cs - 通用NPC对话系统
// ============================================================================
// 模块说明：
//   通用的NPC对话显示系统，支持任意实现 INPCDialogueConfig 的NPC。
//   使用游戏原版的 DialogueBubblesManager 显示对话气泡。
// ============================================================================

using System;
using UnityEngine;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// 通用NPC对话系统
    /// </summary>
    public static class NPCDialogueSystem
    {
        // 默认配置
        private const float DEFAULT_DIALOGUE_HEIGHT = 2.5f;
        private const float DEFAULT_DURATION = 3f;
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 获取指定NPC的对话内容
        /// </summary>
        public static string GetDialogue(string npcId, DialogueCategory category)
        {
            int level = AffinityManager.GetLevel(npcId);
            return GetDialogue(npcId, category, level);
        }
        
        /// <summary>
        /// 获取指定NPC和等级的对话内容
        /// </summary>
        public static string GetDialogue(string npcId, DialogueCategory category, int level)
        {
            var config = AffinityManager.GetNPCConfig(npcId);
            var dialogueConfig = config as INPCDialogueConfig;
            
            if (dialogueConfig != null)
            {
                return dialogueConfig.GetDialogue(category, level);
            }
            
            return GetDefaultDialogue(category);
        }
        
        /// <summary>
        /// 获取特殊事件对话
        /// </summary>
        public static string GetSpecialDialogue(string npcId, string eventKey)
        {
            int level = AffinityManager.GetLevel(npcId);
            return GetSpecialDialogue(npcId, eventKey, level);
        }
        
        /// <summary>
        /// 获取特殊事件对话（指定等级）
        /// </summary>
        public static string GetSpecialDialogue(string npcId, string eventKey, int level)
        {
            var config = AffinityManager.GetNPCConfig(npcId);
            var dialogueConfig = config as INPCDialogueConfig;
            
            if (dialogueConfig != null)
            {
                return dialogueConfig.GetSpecialDialogue(eventKey, level);
            }
            
            return null;
        }
        
        /// <summary>
        /// 在NPC头顶显示对话气泡
        /// </summary>
        public static void ShowDialogue(string npcId, Transform target, DialogueCategory category)
        {
            if (target == null) return;
            
            string text = GetDialogue(npcId, category);
            ShowDialogue(npcId, target, text);
        }
        
        /// <summary>
        /// 在NPC头顶显示指定文本的对话气泡
        /// </summary>
        public static void ShowDialogue(string npcId, Transform target, string text)
        {
            float duration = GetDialogueDuration(npcId);
            ShowDialogue(npcId, target, text, duration);
        }
        
        /// <summary>
        /// 在NPC头顶显示指定文本的对话气泡（自定义时长）
        /// </summary>
        public static void ShowDialogue(string npcId, Transform target, string text, float duration)
        {
            if (target == null || string.IsNullOrEmpty(text)) return;
            
            float height = GetDialogueHeight(npcId);
            
            try
            {
                DialogueBubblesManager.Show(
                    text,
                    target,
                    height,
                    false,  // 不需要交互
                    false,  // 不可跳过
                    -1f,    // 默认速度
                    duration
                );
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCDialogue] 显示对话失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示问候对话
        /// </summary>
        public static void ShowGreeting(string npcId, Transform target)
        {
            ShowDialogue(npcId, target, DialogueCategory.Greeting);
        }
        
        /// <summary>
        /// 显示收到礼物后的对话
        /// </summary>
        public static void ShowAfterGift(string npcId, Transform target)
        {
            ShowDialogue(npcId, target, DialogueCategory.AfterGift);
        }
        
        /// <summary>
        /// 显示等级提升对话
        /// </summary>
        public static void ShowLevelUp(string npcId, Transform target, int newLevel)
        {
            string text = GetDialogue(npcId, DialogueCategory.LevelUp, newLevel);
            ShowDialogue(npcId, target, text, 4f);  // 等级提升对话显示更长时间
        }
        
        /// <summary>
        /// 显示今日已赠送礼物的提示
        /// </summary>
        public static void ShowAlreadyGifted(string npcId, Transform target)
        {
            ShowDialogue(npcId, target, DialogueCategory.AlreadyGifted);
        }
        
        /// <summary>
        /// 显示告别对话
        /// </summary>
        public static void ShowFarewell(string npcId, Transform target)
        {
            ShowDialogue(npcId, target, DialogueCategory.Farewell);
        }
        
        // ============================================================================
        // 私有方法
        // ============================================================================
        
        /// <summary>
        /// 获取对话气泡高度
        /// </summary>
        private static float GetDialogueHeight(string npcId)
        {
            var config = AffinityManager.GetNPCConfig(npcId);
            var dialogueConfig = config as INPCDialogueConfig;
            
            if (dialogueConfig != null)
            {
                return dialogueConfig.DialogueBubbleHeight;
            }
            
            return DEFAULT_DIALOGUE_HEIGHT;
        }
        
        /// <summary>
        /// 获取对话显示时长
        /// </summary>
        private static float GetDialogueDuration(string npcId)
        {
            var config = AffinityManager.GetNPCConfig(npcId);
            var dialogueConfig = config as INPCDialogueConfig;
            
            if (dialogueConfig != null)
            {
                return dialogueConfig.DefaultDialogueDuration;
            }
            
            return DEFAULT_DURATION;
        }
        
        /// <summary>
        /// 获取默认对话
        /// </summary>
        private static string GetDefaultDialogue(DialogueCategory category)
        {
            switch (category)
            {
                case DialogueCategory.Greeting:
                    return L10n.T("你好！", "Hello!");
                case DialogueCategory.AfterGift:
                    return L10n.T("谢谢你的礼物！", "Thanks for the gift!");
                case DialogueCategory.LevelUp:
                    return L10n.T("我们的关系更好了！", "Our relationship is better!");
                case DialogueCategory.Shopping:
                    return L10n.T("看看我的商品吧！", "Check out my goods!");
                case DialogueCategory.AlreadyGifted:
                    return L10n.T("今天已经收到礼物了~", "Already received a gift today~");
                case DialogueCategory.Farewell:
                    return L10n.T("再见！", "Goodbye!");
                default:
                    return "";
            }
        }
    }
}
