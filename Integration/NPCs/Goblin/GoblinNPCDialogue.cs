// ============================================================================
// GoblinNPCDialogue.cs - 哥布林NPC控制器（对话状态）
// ============================================================================
// 模块说明：
//   哥布林NPC控制器的对话部分，使用 partial class 机制
//   包含 StartDialogue、EndDialogue、FacePlayer 等方法
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using Duckov.UI.DialogueBubbles;
using BossRush.Constants;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 哥布林NPC控制器 - 对话状态管理
    /// </summary>
    public partial class GoblinNPCController
    {
        // ============================================================================
        // 对话接口
        // ============================================================================
        
        /// <summary>
        /// 开始对话（玩家打开了UI）
        /// 哥布林进入待机状态
        /// </summary>
        public void StartDialogue()
        {
            // 停止之前的停留协程（防止之前的协程结束时恢复走路）
            if (currentStayCoroutine != null)
            {
                StopCoroutine(currentStayCoroutine);
                currentStayCoroutine = null;
                ModBehaviour.DevLog("[GoblinNPC] StartDialogue: 停止之前的停留协程");
            }
            
            isInDialogue = true;
            
            // 停止移动
            if (movement != null)
            {
                movement.StopMove();
            }
            
            // 面向玩家
            FacePlayer();
            
            // 播放待机动画
            StartIdleAnimation();
            
            ModBehaviour.DevLog("[GoblinNPC] 开始对话，进入待机状态");
        }
        
        /// <summary>
        /// 结束对话（UI关闭）
        /// 哥布林停留10秒后恢复走路
        /// </summary>
        public void EndDialogue()
        {
            // 使用带停留时间的方法，默认停留10秒
            EndDialogueWithStay(GoblinNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION);
        }
        
        /// <summary>
        /// 延迟结束对话
        /// </summary>
        /// <param name="delay">延迟时间（秒）</param>
        public void EndDialogueDelayed(float delay)
        {
            StartCoroutine(EndDialogueDelayedCoroutine(delay));
        }
        
        /// <summary>
        /// 结束对话并在原地停留指定时间后恢复走路
        /// 用于交互后让哥布林停留一段时间
        /// </summary>
        /// <param name="stayDuration">停留时间（秒），默认10秒</param>
        /// <param name="showFarewell">是否显示告别对话，默认false（只有商店和重铸UI关闭时才传true）</param>
        public void EndDialogueWithStay(float stayDuration = 10f, bool showFarewell = false)
        {
            // 停止之前的停留协程（防止多个协程同时运行导致提前恢复走路）
            if (currentStayCoroutine != null)
            {
                StopCoroutine(currentStayCoroutine);
                currentStayCoroutine = null;
                ModBehaviour.DevLog("[GoblinNPC] 停止之前的停留协程");
            }
            
            // 启动新的停留协程
            currentStayCoroutine = StartCoroutine(EndDialogueWithStayCoroutine(stayDuration, showFarewell));
        }
        
        // ============================================================================
        // 私有方法
        // ============================================================================
        
        /// <summary>
        /// 面向玩家
        /// </summary>
        private void FacePlayer()
        {
            if (playerTransform == null) return;
            
            NPCExceptionHandler.TryExecute(() =>
            {
                Vector3 direction = playerTransform.position - transform.position;
                direction.y = 0;  // 只在水平面上旋转
                if (direction.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }, "GoblinNPCDialogue.FacePlayer");
        }
        
        /// <summary>
        /// 待机3秒并显示气泡
        /// 3秒后设置 IsIdle=false 并恢复走路
        /// 根据召唤类型显示不同反应（钻石=爱心，砖石=碎心）
        /// </summary>
        private IEnumerator IdleAndShowBubble()
        {
            // 进入待机状态（确保 IsIdle=true）
            isIdling = true;
            SafeSetBool(hash_IsIdle, true);
            SafeSetBool(hash_IsRunning, false);
            
            // 如果需要显示对话
            if (showDialogueOnArrival)
            {
                showDialogueOnArrival = false;  // 重置标志
                
                // 根据召唤类型显示不同反应
                if (isPositiveSummon)
                {
                    // 钻石召唤：显示爱心动画
                    ShowLoveHeartBubble();
                    
                    // 等待0.5秒让哥布林完全停下来
                    yield return new WaitForSeconds(0.5f);
                    
                    // 显示正面对话气泡
                    ShowDiamondDialogue();
                    
                    ModBehaviour.DevLog("[GoblinNPC] 钻石召唤：显示爱心和正面对话");
                }
                else
                {
                    // 砖石召唤：显示心碎动画
                    ShowBrokenHeartBubble();
                    
                    // 等待0.5秒让哥布林完全停下来
                    yield return new WaitForSeconds(0.5f);
                    
                    // 显示负面对话气泡
                    ShowBrickStoneDialogue();
                    
                    ModBehaviour.DevLog("[GoblinNPC] 砖石召唤：显示碎心和负面对话");
                }
                
                isPositiveSummon = false;  // 重置标志
            }
            else
            {
                // 普通召唤：只显示裂开的心气泡
                ShowBrokenHeartBubble();
            }
            
            ModBehaviour.DevLog("[GoblinNPC] 进入待机状态，显示气泡，持续3秒");
            
            // 待机3秒
            yield return new WaitForSeconds(GoblinNPCConstants.IDLE_DURATION_AFTER_STOP);
            
            // 3秒后设置 IsIdle=false
            isIdling = false;
            SafeSetBool(hash_IsIdle, false);
            ModBehaviour.DevLog("[GoblinNPC] 待机3秒结束，设置 IsIdle=false");
            
            // 如果不在对话中，恢复走路
            if (!isInDialogue)
            {
                if (movement != null)
                {
                    movement.ResumeWalking();
                }
                ModBehaviour.DevLog("[GoblinNPC] 恢复走路");
            }
            else
            {
                ModBehaviour.DevLog("[GoblinNPC] 在对话中，继续待机");
            }
        }
        
        /// <summary>
        /// 延迟结束对话的协程
        /// </summary>
        private IEnumerator EndDialogueDelayedCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            EndDialogue();
        }
        
        /// <summary>
        /// 结束对话并停留的协程
        /// </summary>
        /// <param name="stayDuration">停留时间</param>
        /// <param name="showFarewell">是否显示告别对话</param>
        private IEnumerator EndDialogueWithStayCoroutine(float stayDuration, bool showFarewell = false)
        {
            // 先进入待机状态，阻止移动（在等待期间保持待机）
            isIdling = true;
            SafeSetBool(hash_IsIdle, true);
            SafeSetBool(hash_IsRunning, false);
            
            // 保持待机状态，不恢复走路
            ModBehaviour.DevLog("[GoblinNPC] 对话结束，停留 " + stayDuration + " 秒，显示告别: " + showFarewell);
            
            // 显示告别对话气泡（仅在明确指定时）
            if (showFarewell)
            {
                NPCDialogueSystem.ShowFarewell(GoblinAffinityConfig.NPC_ID, transform);
            }
            
            // 隐藏好感度面板
            AffinityUIManager.HideAffinityPanel();
            
            // 等待指定时间（在此期间 isIdling=true 会阻止 GoblinMovement 的漫步决策）
            yield return new WaitForSeconds(stayDuration);
            
            // 等待结束后，标记对话结束
            isInDialogue = false;
            
            // 停止待机动画
            StopIdleAnimation();
            
            // 清除协程引用
            currentStayCoroutine = null;
            
            // 恢复走路
            if (movement != null)
            {
                movement.ResumeWalking();
            }
            
            ModBehaviour.DevLog("[GoblinNPC] 停留结束，恢复走路");
        }
        
        /// <summary>
        /// 显示砖石召唤后的不满对话
        /// </summary>
        private void ShowBrickStoneDialogue()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                // 使用通用对话系统的特殊事件API
                int level = AffinityManager.GetLevel(GoblinAffinityConfig.NPC_ID);
                string dialogue = NPCDialogueSystem.GetSpecialDialogue(GoblinAffinityConfig.NPC_ID, "AfterBrickStone", level);
                
                // 显示对话气泡
                if (!string.IsNullOrEmpty(dialogue))
                {
                    DialogueBubblesManager.Show(dialogue, transform, GoblinNPCConstants.NAME_TAG_HEIGHT, false, false, -1f, 3f);
                    ModBehaviour.DevLog("[GoblinNPC] 显示砖石召唤对话: " + dialogue);
                }
            }, "GoblinNPCDialogue.ShowBrickStoneDialogue");
        }
        
        /// <summary>
        /// 显示钻石召唤后的开心对话
        /// </summary>
        private void ShowDiamondDialogue()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                // 使用通用对话系统的特殊事件API
                int level = AffinityManager.GetLevel(GoblinAffinityConfig.NPC_ID);
                string dialogue = NPCDialogueSystem.GetSpecialDialogue(GoblinAffinityConfig.NPC_ID, "AfterDiamond", level);
                
                // 显示对话气泡
                if (!string.IsNullOrEmpty(dialogue))
                {
                    DialogueBubblesManager.Show(dialogue, transform, GoblinNPCConstants.NAME_TAG_HEIGHT, false, false, -1f, 3f);
                    ModBehaviour.DevLog("[GoblinNPC] 显示钻石召唤对话: " + dialogue);
                }
            }, "GoblinNPCDialogue.ShowDiamondDialogue");
        }
    }
}
