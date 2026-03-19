// ============================================================================
// GoblinNPCAnimation.cs - 哥布林NPC控制器（动画和气泡）
// ============================================================================
// 模块说明：
//   哥布林NPC控制器的动画部分，使用 partial class 机制
//   包含名字标签、气泡显示、动画参数设置方法
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
    /// 哥布林NPC控制器 - 动画和气泡显示
    /// </summary>
    public partial class GoblinNPCController
    {
        // ============================================================================
        // 名字标签方法
        // ============================================================================
        
        /// <summary>
        /// 创建头顶名字标签（与快递员阿稳完全一致的设置）
        /// </summary>
        private void CreateNameTag()
        {
            // 使用本地化名称
            string goblinName = "叮当";
            try
            {
                goblinName = BossRush.L10n.T("叮当", "Dingdang");
            }
            catch
            {
                // 如果本地化失败，使用默认名称
            }

            if (NPCNameTagHelper.RegisterOriginalHealthBarName(
                transform,
                goblinName,
                GoblinNPCConstants.NAME_TAG_HEIGHT,
                "[GoblinNPC]"))
            {
                ModBehaviour.DevLog("[GoblinNPC] 原版名字组件注册成功: " + goblinName);
            }
        }

        void OnDestroy()
        {
            NPCNameTagHelper.UnregisterOriginalHealthBarName(transform);
        }
        
        // ============================================================================
        // 动画控制方法
        // ============================================================================
        
        /// <summary>
        /// 初始化默认动画参数
        /// 默认保持待机，避免静止刷出时错误进入走路动画
        /// </summary>
        private void InitializeDefaultAnimatorParams()
        {
            SafeSetFloat(hash_MoveSpeed, 0f);
            SafeSetBool(hash_IsRunning, false);
            SafeSetBool(hash_IsIdle, true);
        }

        /// <summary>
        /// 强制进入原地待机状态
        /// 用于婚礼教堂等必须站桩的场景
        /// </summary>
        public void EnterStationaryIdleState()
        {
            isRunningToPlayer = false;
            isBraking = false;
            isIdling = true;

            if (movement == null)
            {
                movement = GetComponent<GoblinMovement>();
            }

            if (movement != null)
            {
                movement.StopMove();
                movement.enabled = false;
            }

            InitializeDefaultAnimatorParams();
        }

        /// <summary>
        /// 退出原地站桩待机，恢复正常移动状态。
        /// </summary>
        public void ExitStationaryIdleState()
        {
            isRunningToPlayer = false;
            isBraking = false;
            isInDialogue = false;
            isIdling = false;

            if (currentStayCoroutine != null)
            {
                StopCoroutine(currentStayCoroutine);
                currentStayCoroutine = null;
            }

            if (movement == null)
            {
                movement = GetComponent<GoblinMovement>();
            }

            if (movement != null)
            {
                movement.enabled = true;
            }

            InitializeDefaultAnimatorParams();
        }

        /// <summary>
        /// 开始播放急停动画（距离4米时触发）
        /// 设置 IsRunning=false，触发 DoStop，然后设置 IsIdle=true
        /// </summary>
        private void StartBrakeAnimation()
        {
            isBraking = true;
            
            // 1. 先停止跑步动画
            SafeSetBool(hash_IsRunning, false);
            
            // 2. 触发急停动画（DoStop trigger）
            SafeSetTrigger(hash_DoStop);
            
            // 3. 设置待机状态
            SafeSetBool(hash_IsIdle, true);
            
            ModBehaviour.DevLog("[GoblinNPC] 距离4米，播放急停动画（IsRunning=false, DoStop触发, IsIdle=true）");
        }
        
        /// <summary>
        /// 开始待机动画
        /// </summary>
        private void StartIdleAnimation()
        {
            isIdling = true;
            InitializeDefaultAnimatorParams();
        }
        
        /// <summary>
        /// 停止待机动画
        /// </summary>
        private void StopIdleAnimation()
        {
            isIdling = false;
            SafeSetBool(hash_IsIdle, false);
        }
        
        // ============================================================================
        // 安全设置动画参数的辅助方法
        // ============================================================================
        
        private void SafeSetBool(int hash, bool value)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (animator != null) animator.SetBool(hash, value);
            }, "GoblinNPCAnimation.SafeSetBool");
        }
        
        private void SafeSetTrigger(int hash)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (animator != null) animator.SetTrigger(hash);
            }, "GoblinNPCAnimation.SafeSetTrigger");
        }
        
        private void SafeSetFloat(int hash, float value)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (animator != null) animator.SetFloat(hash, value);
            }, "GoblinNPCAnimation.SafeSetFloat");
        }
        
        // ============================================================================
        // 气泡显示方法
        // ============================================================================
        
        /// <summary>
        /// 显示裂开的心气泡
        /// 使用通用 NPCHeartBubbleHelper，优先序列帧动画，回退文字气泡
        /// </summary>
        public void ShowBrokenHeartBubble()
        {
            NPCHeartBubbleHelper.ShowBrokenHeart(
                transform,
                GoblinNPCConstants.NAME_TAG_HEIGHT + GoblinNPCConstants.BUBBLE_OFFSET_Y,
                GoblinNPCConstants.NAME_TAG_HEIGHT + GoblinNPCConstants.BUBBLE_ANIMATION_OFFSET_Y,
                GoblinNPCConstants.BUBBLE_DURATION,
                "[GoblinNPC]");
        }

        /// <summary>
        /// 显示冒爱心气泡
        /// 使用通用 NPCHeartBubbleHelper，优先序列帧动画，回退文字气泡
        /// </summary>
        public void ShowLoveHeartBubble()
        {
            NPCHeartBubbleHelper.ShowLoveHeart(
                transform,
                GoblinNPCConstants.NAME_TAG_HEIGHT + GoblinNPCConstants.BUBBLE_OFFSET_Y,
                GoblinNPCConstants.NAME_TAG_HEIGHT + GoblinNPCConstants.BUBBLE_ANIMATION_OFFSET_Y,
                GoblinNPCConstants.BUBBLE_DURATION,
                "[GoblinNPC]");
        }
    }
}
